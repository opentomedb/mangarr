using NzbDrone.Common.Localization;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.MediaFiles.Chapters
{
    // Volumes built from chapter packs (2026-10-21): a volume a chapter pack was mapped to but did not
    // build (the pack built others). HistoryService records the reason on that volume's history. A monthly
    // volume pack raises it too, for a matched file that did not import.
    public class ChapterVolumeSkippedEvent : IEvent
    {
        public TrackedDownload TrackedDownload { get; }
        public Book Book { get; }
        public ServerText Reason { get; }

        public ChapterVolumeSkippedEvent(TrackedDownload trackedDownload, Book book, ServerText reason)
        {
            TrackedDownload = trackedDownload;
            Book = book;
            Reason = reason;
        }
    }
}
