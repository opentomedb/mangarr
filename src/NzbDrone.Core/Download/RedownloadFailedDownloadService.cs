using System;
using System.Collections.Concurrent;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.History;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public class RedownloadFailedDownloadService : IHandle<DownloadFailedEvent>
    {
        private readonly IConfigService _configService;
        private readonly IBookService _bookService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        // Light novels (2026-09-22): when a new series' batch of grabs fails one by one, each failure
        // used to queue its own single-volume search -- 13 failures in two minutes were 13 volume
        // searches across every indexer (Classroom of the Elite exhausted a private tracker's hourly limit). One
        // typed series search now answers the burst; this remembers when it last ran per
        // (series, media type) so the next failures of the same burst do not repeat it. The scheduled
        // missing search picks up anything the series search did not.
        private static readonly TimeSpan SeriesResearchCooldown = TimeSpan.FromMinutes(30);
        private readonly ConcurrentDictionary<(int AuthorId, MediaType MediaType), DateTime> _lastSeriesResearch = new ();

        public RedownloadFailedDownloadService(IConfigService configService,
                                               IBookService bookService,
                                               IMediaFileService mediaFileService,
                                               IManageCommandQueue commandQueueManager,
                                               Logger logger)
        {
            _configService = configService;
            _bookService = bookService;
            _mediaFileService = mediaFileService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        [EventHandleOrder(EventHandleOrder.Last)]
        public void Handle(DownloadFailedEvent message)
        {
            if (message.SkipRedownload)
            {
                _logger.Debug("Skip redownloading requested by user");
                return;
            }

            if (!_configService.AutoRedownloadFailed)
            {
                _logger.Debug("Auto redownloading failed books is disabled");
                return;
            }

            if (message.ReleaseSource == ReleaseSourceType.InteractiveSearch && !_configService.AutoRedownloadFailedFromInteractiveSearch)
            {
                _logger.Debug("Auto redownloading failed books from interactive search is disabled");
                return;
            }

            // Light novels (2026-09, final review I1): the failed grab was for ONE edition class
            // (the grab row says which); a light-novel volume is still wanted when THAT edition is
            // monitored and fileless -- its EPUB on disk does not satisfy a failed audiobook grab --
            // and the re-search is typed to that class so it never fans out to the other one. A
            // manga volume has one Archive edition: the book-level file check and the untyped
            // search below are exactly the old path.
            var mediaType = message.TrackedDownload?.RemoteBook?.MediaType ?? HistoryService.MediaTypeOf(message.Data, message.Quality);

            // Re-search only books still wanted (monitored, no file), deduplicated: a failed
            // whole-series batch maps to every volume of the series, and duplicated grab
            // history rows can double the list — one failed batch re-searched 26 ids for
            // 10 minutes against rate-limited indexers before this filter existed.
            // Deliberately NOT BooksWithoutFiles: its ReleaseDate <= now gate drops books
            // with null/wrong dates, and a book whose download just failed is wanted
            // regardless of what its metadata date claims.
            var wanted = _bookService.GetBooks(message.BookIds.Distinct())
                .Where(b => b.Monitored && b.Author.Value.Monitored)
                .Where(b => IsWanted(b, mediaType))
                .ToList();

            var bookIds = wanted.Select(b => b.Id).ToList();

            if (bookIds.Count == 0)
            {
                _logger.Debug("No wanted books remain from failed download, skipping search");
                return;
            }

            // A light-novel re-search is typed; a manga one stays untyped (null = every monitored
            // class = the one Archive search), so its queued command is byte-identical to before.
            var isLightNovel = wanted[0].Author.Value.Library == LibraryType.LightNovel;
            var searchType = isLightNovel ? mediaType : (MediaType?)null;

            // Light novel with more than one volume of this class still wanted: one typed series search,
            // at most once per cooldown. A single wanted volume keeps the precise volume search (a
            // series query can miss one volume on an indexer that caps its results).
            if (isLightNovel && WantedInSeries(message.AuthorId, mediaType) > 1)
            {
                var key = (message.AuthorId, mediaType);
                var now = DateTime.UtcNow;

                if (_lastSeriesResearch.TryGetValue(key, out var last) && now - last < SeriesResearchCooldown)
                {
                    _logger.Debug("Series re-search for author {0} ({1}) ran {2:0} min ago; the scheduled missing search covers the rest",
                                  message.AuthorId, mediaType, (now - last).TotalMinutes);
                    return;
                }

                _lastSeriesResearch[key] = now;
                _logger.Debug("Failed download of a light novel with several {0} volumes wanted: one series search", mediaType);

                _commandQueueManager.Push(new AuthorSearchCommand
                {
                    AuthorId = message.AuthorId,
                    MediaType = mediaType
                });

                return;
            }

            if (bookIds.Count == 1)
            {
                _logger.Debug("Failed download only contains one book, searching again");

                _commandQueueManager.Push(new BookSearchCommand(bookIds, searchType));

                return;
            }

            var booksInAuthor = _bookService.GetBooksByAuthor(message.AuthorId);

            if (bookIds.Count == booksInAuthor.Count)
            {
                _logger.Debug("Failed download was entire author, searching again");

                _commandQueueManager.Push(new AuthorSearchCommand
                {
                    AuthorId = message.AuthorId,
                    MediaType = searchType
                });

                return;
            }

            _logger.Debug("Failed download contains multiple books, searching again");

            _commandQueueManager.Push(new BookSearchCommand(bookIds, searchType));
        }

        private int WantedInSeries(int authorId, MediaType mediaType)
        {
            var now = DateTime.UtcNow;

            return _bookService.GetBooksByAuthor(authorId)
                .Where(b => b.Monitored && IsWanted(b, mediaType))
                .Count(b => mediaType != MediaType.Audio || !(b.EditionOf(MediaType.Audio)?.IsUnreleasedAudio(now) ?? false));
        }

        private bool IsWanted(Book book, MediaType mediaType)
        {
            if (book.Author.Value.Library != LibraryType.LightNovel)
            {
                return _mediaFileService.GetFilesByBook(book.Id).Count == 0;
            }

            var edition = book.EditionOf(mediaType);

            return edition?.Monitored == true && _mediaFileService.GetFilesByEdition(edition.Id).Empty();
        }
    }
}
