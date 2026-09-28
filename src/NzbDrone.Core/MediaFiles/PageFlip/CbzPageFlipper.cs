using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace NzbDrone.Core.MediaFiles.PageFlip
{
    public interface ICbzPageFlipper
    {
        void Flip(string cbzPath);
        byte[] GetPage(string cbzPath, bool last, out string extension);
    }

    // Rewrites a CBZ/ZIP with its page images in reverse order — a pure reorder, so flipping
    // twice restores the original sequence. Pages get the converter's clean zero-padded naming;
    // non-image entries (ComicInfo.xml) are carried over untouched. The new archive is written
    // beside the original and only swapped in after its entry count verifies.
    public class CbzPageFlipper : ICbzPageFlipper
    {
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"
        };

        private static List<ZipArchiveEntry> ImageEntries(ZipArchive zip)
        {
            return zip.Entries
                .Where(e => !e.FullName.EndsWith("/") && ImageExtensions.Contains(Path.GetExtension(e.FullName)))
                .OrderBy(e => e.FullName, StringComparer.Ordinal)
                .ToList();
        }

        public void Flip(string cbzPath)
        {
            var tempPath = cbzPath + ".flip-tmp";
            var sourceSize = new FileInfo(cbzPath).Length;
            int expectedEntries;

            try
            {
                using (var source = ZipFile.OpenRead(cbzPath))
                {
                    var images = ImageEntries(source);

                    if (images.Count == 0)
                    {
                        throw new InvalidOperationException($"No image pages found in {cbzPath}");
                    }

                    var others = source.Entries
                        .Where(e => !e.FullName.EndsWith("/") && !images.Contains(e))
                        .ToList();

                    expectedEntries = images.Count + others.Count;

                    using var outStream = File.Create(tempPath);
                    using var zip = new ZipArchive(outStream, ZipArchiveMode.Create);

                    var index = 1;
                    for (var i = images.Count - 1; i >= 0; i--)
                    {
                        var name = index.ToString("D4") + Path.GetExtension(images[i].FullName).ToLowerInvariant();
                        CopyEntry(images[i], zip, name);
                        index++;
                    }

                    foreach (var other in others)
                    {
                        CopyEntry(other, zip, other.FullName);
                    }
                }

                using (var check = ZipFile.OpenRead(tempPath))
                {
                    if (check.Entries.Count(e => !e.FullName.EndsWith("/")) != expectedEntries)
                    {
                        throw new InvalidOperationException($"Flip verification failed for {cbzPath}");
                    }
                }

                // The rebuild takes time on large volumes. If another writer replaced the
                // archive meanwhile (e.g. a download import upgrading this book — converted
                // volumes deliberately sit below the CBZ cutoff), swapping our rebuild over
                // it would destroy the newer file. The newer file wins — abort the flip.
                var currentInfo = new FileInfo(cbzPath);
                if (!currentInfo.Exists || currentInfo.Length != sourceSize)
                {
                    throw new InvalidOperationException($"Archive was replaced during flip; aborted to avoid clobbering the new file: {cbzPath}");
                }

                File.Move(tempPath, cbzPath, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        public byte[] GetPage(string cbzPath, bool last, out string extension)
        {
            using var zip = ZipFile.OpenRead(cbzPath);
            var images = ImageEntries(zip);

            if (images.Count == 0)
            {
                extension = null;
                return null;
            }

            var entry = last ? images[images.Count - 1] : images[0];
            extension = Path.GetExtension(entry.FullName).ToLowerInvariant();

            using var buffer = new MemoryStream();
            using var input = entry.Open();
            input.CopyTo(buffer);
            return buffer.ToArray();
        }

        private static void CopyEntry(ZipArchiveEntry source, ZipArchive destination, string name)
        {
            var entry = destination.CreateEntry(name, CompressionLevel.NoCompression);
            using var input = source.Open();
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }
}
