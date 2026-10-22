using System.Collections.Generic;
using System.Linq;
using System.Text;
using MonoTorrent;
using NLog;

namespace NzbDrone.Core.MediaFiles.TorrentInfo
{
    // Path is relative to the torrent root, '/'-separated, without the torrent's own name folder.
    public record TorrentFileEntry(int Index, string Path, long Length);

    public interface ITorrentFileInfoReader
    {
        string GetHashFromTorrentFile(byte[] fileContents);
        (string Name, List<TorrentFileEntry> Files) GetFilesFromTorrentFile(byte[] torrentFile);
    }

    public class TorrentFileInfoReader : ITorrentFileInfoReader
    {
        private readonly Logger _logger;

        public TorrentFileInfoReader(Logger logger)
        {
            _logger = logger;
        }

        public string GetHashFromTorrentFile(byte[] fileContents)
        {
            try
            {
                return Torrent.Load(fileContents).InfoHash.ToHex();
            }
            catch
            {
                _logger.Trace("Invalid torrent file contents: {0}", Encoding.ASCII.GetString(fileContents));
                throw;
            }
        }

        public (string Name, List<TorrentFileEntry> Files) GetFilesFromTorrentFile(byte[] torrentFile)
        {
            try
            {
                var torrent = Torrent.Load(torrentFile);
                var files = torrent.Files
                    .Select((f, i) => new TorrentFileEntry(i, f.Path.Replace('\\', '/'), f.Length))
                    .ToList();

                return (torrent.Name, files);
            }
            catch
            {
                _logger.Trace("Invalid torrent file contents: {0}", Encoding.ASCII.GetString(torrentFile));
                throw;
            }
        }
    }
}
