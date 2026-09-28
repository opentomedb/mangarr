using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Books
{
    public class MonitoringOptions : IEmbeddedDocument
    {
        public MonitoringOptions()
        {
            BooksToMonitor = new List<string>();
        }

        public MonitorTypes Monitor { get; set; }
        public List<string> BooksToMonitor { get; set; }
        public bool Monitored { get; set; }

        // Light novels (2026-09): the Add modal's EPUB / Audiobook checkboxes as media-type names
        // ("ebook", "audio"; "archive" for completeness). Editions of a media type NOT listed are
        // unmonitored after the post-add scan (BookMonitoredService); null or empty = every edition
        // stays as minted (the manga add, and every API caller that does not send it). Lives here
        // rather than on AddAuthorOptions because SetBookMonitoredStatus receives MonitoringOptions.
        public List<string> MonitorMediaTypes { get; set; }
    }
}
