using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Core.Extras.Metadata.ComicInfo
{
    public interface IComicInfoEmbedder
    {
        bool CanEmbed(string path);
        string ReadExisting(string cbzPath);
        ComicInfoEmbedResult Embed(string cbzPath, string xml);
    }

    public enum ComicInfoEmbedStatus
    {
        Written,
        Unchanged,
        Skipped,   // not a zip-based comic (CBR/PDF) — nothing done
        Failed     // original left untouched
    }

    public class ComicInfoEmbedResult
    {
        public ComicInfoEmbedStatus Status { get; set; }
        public string Error { get; set; }
    }

    // Writes ComicInfo.xml INTO a CBZ, safely. The Books share is irreplaceable (xfs, no snapshots),
    // so the original is never modified in place: a fresh archive is built in a sibling temp file,
    // verified entry-for-entry against the source, and only then atomically swapped in — with a .bak
    // kept until the swap succeeds. Any failure leaves the original exactly as it was.
    public class ComicInfoEmbedder : IComicInfoEmbedder
    {
        private const string EntryName = "ComicInfo.xml";

        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public ComicInfoEmbedder(IDiskProvider diskProvider, Logger logger)
        {
            _diskProvider = diskProvider;
            _logger = logger;
        }

        // ComicInfo.xml is only meaningful inside a zip-based comic. CBR is RAR (not writable by
        // .NET) and PDF carries its own metadata, so both are left alone.
        public bool CanEmbed(string path)
        {
            var ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return ext == ".cbz" || ext == ".zip";
        }

        public string ReadExisting(string cbzPath)
        {
            if (!CanEmbed(cbzPath) || !_diskProvider.FileExists(cbzPath))
            {
                return null;
            }

            try
            {
                using var zip = ZipFile.OpenRead(cbzPath);
                var entry = zip.Entries.FirstOrDefault(e => e.FullName.Equals(EntryName, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    return null;
                }

                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not read existing ComicInfo from {0}", cbzPath);
                return null;
            }
        }

        public ComicInfoEmbedResult Embed(string cbzPath, string xml)
        {
            if (!CanEmbed(cbzPath))
            {
                return new ComicInfoEmbedResult { Status = ComicInfoEmbedStatus.Skipped };
            }

            if (!_diskProvider.FileExists(cbzPath))
            {
                return new ComicInfoEmbedResult { Status = ComicInfoEmbedStatus.Failed, Error = "File not found" };
            }

            var tempPath = cbzPath + ".mangarr-tmp";
            var backupPath = cbzPath + ".mangarr-bak";
            var sourceSize = _diskProvider.GetFileSize(cbzPath);

            try
            {
                // Names (excluding the old ComicInfo) that MUST survive into the new archive.
                string[] expectedNames;
                using (var source = ZipFile.OpenRead(cbzPath))
                {
                    expectedNames = source.Entries
                        .Where(e => !e.FullName.Equals(EntryName, StringComparison.OrdinalIgnoreCase))
                        .Where(e => !e.FullName.EndsWith("/"))
                        .Select(e => e.FullName)
                        .ToArray();
                }

                CleanupStale(tempPath);

                // Build the replacement archive: every original entry copied byte-for-byte on
                // extraction, plus the new ComicInfo.xml.
                using (var source = ZipFile.OpenRead(cbzPath))
                using (var outStream = File.Create(tempPath))
                using (var dest = new ZipArchive(outStream, ZipArchiveMode.Create))
                {
                    foreach (var entry in source.Entries)
                    {
                        if (entry.FullName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith("/"))
                        {
                            continue;
                        }

                        // Images are already compressed; storing avoids pointless CPU and keeps size stable.
                        var copy = dest.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                        copy.LastWriteTime = entry.LastWriteTime;

                        using var input = entry.Open();
                        using var output = copy.Open();
                        input.CopyTo(output);
                    }

                    var infoEntry = dest.CreateEntry(EntryName, CompressionLevel.Optimal);
                    using var infoStream = infoEntry.Open();
                    var bytes = new UTF8Encoding(false).GetBytes(xml);
                    infoStream.Write(bytes, 0, bytes.Length);
                }

                // VERIFY the rebuilt archive before it is allowed anywhere near the original.
                Verify(tempPath, expectedNames);

                // The rebuild of a large volume takes tens of seconds. If another writer replaced
                // the archive in the meantime (e.g. a download import upgrading this book),
                // swapping our rebuild over it would destroy the newer file and fail the import's
                // size verification. The newer file wins — discard the rebuild.
                if (!_diskProvider.FileExists(cbzPath) || _diskProvider.GetFileSize(cbzPath) != sourceSize)
                {
                    _logger.Warn("Archive {0} was replaced during ComicInfo embed; discarding rebuild", cbzPath);
                    CleanupStale(tempPath);
                    return new ComicInfoEmbedResult { Status = ComicInfoEmbedStatus.Failed, Error = "Archive was replaced during embed; aborted to avoid clobbering the new file" };
                }

                // Atomic-ish swap with a retained backup: move original aside, move temp into place,
                // then drop the backup. If the second move throws, the backup is restored.
                if (_diskProvider.FileExists(backupPath))
                {
                    _diskProvider.DeleteFile(backupPath);
                }

                File.Move(cbzPath, backupPath);

                try
                {
                    File.Move(tempPath, cbzPath);
                }
                catch
                {
                    // Put the original back — never leave the library file missing.
                    if (!_diskProvider.FileExists(cbzPath))
                    {
                        File.Move(backupPath, cbzPath);
                    }

                    throw;
                }

                _diskProvider.DeleteFile(backupPath);

                _logger.Debug("Embedded ComicInfo.xml into {0}", cbzPath);
                return new ComicInfoEmbedResult { Status = ComicInfoEmbedStatus.Written };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to embed ComicInfo into {0}; original left untouched", cbzPath);
                CleanupStale(tempPath);
                return new ComicInfoEmbedResult { Status = ComicInfoEmbedStatus.Failed, Error = ex.Message };
            }
        }

        private void Verify(string archivePath, string[] expectedNames)
        {
            using var zip = ZipFile.OpenRead(archivePath);

            var names = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);

            foreach (var expected in expectedNames)
            {
                if (!names.Contains(expected))
                {
                    throw new InvalidDataException($"Rebuilt archive is missing original entry '{expected}'");
                }
            }

            var info = zip.Entries.FirstOrDefault(e => e.FullName.Equals(EntryName, StringComparison.OrdinalIgnoreCase));
            if (info == null)
            {
                throw new InvalidDataException("Rebuilt archive is missing ComicInfo.xml");
            }

            // A comic archive with no image entries would mean we destroyed the content.
            if (!expectedNames.Any())
            {
                throw new InvalidDataException("Refusing to write: source archive had no content entries");
            }
        }

        private void CleanupStale(string tempPath)
        {
            if (_diskProvider.FileExists(tempPath))
            {
                _diskProvider.DeleteFile(tempPath);
            }
        }
    }
}
