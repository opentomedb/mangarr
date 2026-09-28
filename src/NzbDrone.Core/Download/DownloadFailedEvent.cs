using System.Collections.Generic;
using NzbDrone.Common.Localization;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Download
{
    public class DownloadFailedEvent : IEvent
    {
        public DownloadFailedEvent()
        {
            Data = new Dictionary<string, string>();
        }

        public int AuthorId { get; set; }
        public List<int> BookIds { get; set; }
        public QualityModel Quality { get; set; }
        public string SourceTitle { get; set; }
        public string DownloadClient { get; set; }
        public string DownloadId { get; set; }
        public string Message { get; set; }

        // Server messages (2026-09-26): Message's template, so HistoryService can store its key beside it. Not
        // serialized: DownloadFailedEvent is an internal event, never returned as API JSON.
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ServerText MessageText { get; set; }

        public Dictionary<string, string> Data { get; set; }
        public TrackedDownload TrackedDownload { get; set; }
        public bool SkipRedownload { get; set; }
        public ReleaseSourceType ReleaseSource { get; set; }
    }
}
