namespace NzbDrone.Core.Configuration
{
    // Beta readiness (2026-09-28, F2): which imports get ComicInfo.xml embedded. NewDownloads = the
    // import carries a download-client DownloadId (a grab, or a Manual Import of a queue item); a disk
    // scan, Library Import or Manual Import of loose files does not. A fresh install defaults to
    // NewDownloads; migration 059 keeps an existing library on AllImports (the old behaviour).
    public enum WriteComicInfoType
    {
        NewDownloads,
        AllImports,
        Never
    }
}
