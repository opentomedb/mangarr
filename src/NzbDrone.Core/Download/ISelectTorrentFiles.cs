namespace NzbDrone.Core.Download
{
    // A torrent client that can download only chosen files of a torrent (per-file priorities).
    public interface ISelectTorrentFiles
    {
        bool CanSelectFiles { get; }
    }
}
