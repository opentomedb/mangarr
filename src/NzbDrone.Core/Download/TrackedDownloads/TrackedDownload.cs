using NzbDrone.Common.Localization;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class TrackedDownload
    {
        public int DownloadClient { get; set; }
        public DownloadClientItem DownloadItem { get; set; }
        public DownloadClientItem ImportItem { get; set; }
        public TrackedDownloadState State { get; set; }
        public TrackedDownloadStatus Status { get; private set; }
        public RemoteBook RemoteBook { get; set; }
        public TrackedDownloadStatusMessage[] StatusMessages { get; private set; }
        public DownloadProtocol Protocol { get; set; }
        public string Indexer { get; set; }
        public bool IsTrackable { get; set; }

        public TrackedDownload()
        {
            StatusMessages = new TrackedDownloadStatusMessage[] { };
        }

        // Controller ruling (2026-09-26): this has always run string.Format even with zero arguments, unlike
        // Decision.Reject(string)/Rejection(string), which never format. WithEnglish reproduces that exactly
        // -- new ServerText(message, args) would stop formatting a zero-arg call -- so English stays
        // byte-identical (a message such as "a {{b}}" still unescapes to "a {b}").
        public void Warn(string message, params object[] args)
        {
            Warn(ServerText.WithEnglish(string.Format(message, args), message, args));
        }

        // Server messages (2026-09-26): the warning keeps its template for the queue in the UI language.
        public void Warn(ServerText message)
        {
            Warn(new TrackedDownloadStatusMessage(DownloadItem.Title, message));
        }

        public void Warn(params TrackedDownloadStatusMessage[] statusMessages)
        {
            Status = TrackedDownloadStatus.Warning;
            StatusMessages = statusMessages;
        }
    }

    public enum TrackedDownloadState
    {
        Downloading,
        DownloadFailed,
        DownloadFailedPending,
        ImportPending,
        Importing,
        ImportFailed,
        Imported,
        Ignored
    }

    public enum TrackedDownloadStatus
    {
        Ok,
        Warning,
        Error
    }
}
