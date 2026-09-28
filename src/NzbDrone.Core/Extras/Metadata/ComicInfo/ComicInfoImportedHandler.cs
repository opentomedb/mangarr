using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Extras.Metadata.ComicInfo
{
    // Embeds ComicInfo.xml when a volume is imported, so a reader (Komga) sees Mangarr's metadata
    // without any manual step. BookImportedEvent fires for EVERY import -- a grab, but also a disk scan,
    // a Library Import and a Manual Import of files the user already owns -- and embedding rebuilds the
    // archive, overwriting Series/Number/Summary a user may have curated. Settings -> Metadata ->
    // Write ComicInfo To (beta readiness 2026-09-28, F2) decides: New Downloads (default) only when the
    // import carries a download-client DownloadId; All Imports every time (the old behaviour, which an
    // existing library keeps); Never. The explicit backfill (System -> Tasks -> Embed Metadata) is apart.
    public class ComicInfoImportedHandler : IHandle<BookImportedEvent>
    {
        private readonly IComicInfoWriter _writer;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public ComicInfoImportedHandler(IComicInfoWriter writer, IConfigService configService, Logger logger)
        {
            _writer = writer;
            _configService = configService;
            _logger = logger;
        }

        public void Handle(BookImportedEvent message)
        {
            var mode = _configService.WriteComicInfo;

            if (mode == WriteComicInfoType.Never)
            {
                return;
            }

            if (mode == WriteComicInfoType.NewDownloads && message.DownloadId.IsNullOrWhiteSpace())
            {
                _logger.Debug("ComicInfo not embedded for {0}: not a new download (Write ComicInfo To: New Downloads)", message.Book?.Title);
                return;
            }

            foreach (var file in message.ImportedBooks)
            {
                try
                {
                    _writer.WriteForFile(message.Author, message.Book, file);
                }
                catch (System.Exception ex)
                {
                    // Metadata embedding must never fail an import — the file is already in place.
                    _logger.Warn(ex, "Could not embed ComicInfo for {0}", file.Path);
                }
            }
        }
    }
}
