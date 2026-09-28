using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public interface ITrackedDownloadService
    {
        TrackedDownload Find(string downloadId);
        void StopTracking(string downloadId);
        void StopTracking(List<string> downloadIds);
        TrackedDownload TrackDownload(DownloadClientDefinition downloadClient, DownloadClientItem downloadItem);
        List<TrackedDownload> GetTrackedDownloads();
        void UpdateTrackable(List<TrackedDownload> trackedDownloads);
    }

    public class TrackedDownloadService : ITrackedDownloadService,
                                          IHandle<BookInfoRefreshedEvent>,
                                          IHandle<AuthorDeletedEvent>
    {
        private readonly IParsingService _parsingService;
        private readonly IHistoryService _historyService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly Logger _logger;
        private readonly ICached<TrackedDownload> _cache;

        public TrackedDownloadService(IParsingService parsingService,
                                      ICacheManager cacheManager,
                                      IHistoryService historyService,
                                      IEventAggregator eventAggregator,
                                      IDownloadHistoryService downloadHistoryService,
                                      ICustomFormatCalculationService formatCalculator,
                                      Logger logger)
        {
            _parsingService = parsingService;
            _historyService = historyService;
            _cache = cacheManager.GetCache<TrackedDownload>(GetType());
            _formatCalculator = formatCalculator;
            _eventAggregator = eventAggregator;
            _downloadHistoryService = downloadHistoryService;
            _cache = cacheManager.GetCache<TrackedDownload>(GetType());
            _logger = logger;
        }

        public TrackedDownload Find(string downloadId)
        {
            return _cache.Find(downloadId);
        }

        public void UpdateBookCache(int bookId)
        {
            var updateCacheItems = _cache.Values.Where(x => x.RemoteBook != null && x.RemoteBook.Books.Any(a => a.Id == bookId)).ToList();

            if (updateCacheItems.Any())
            {
                foreach (var item in updateCacheItems)
                {
                    var parsedBookInfo = Parser.Parser.ParseBookTitle(item.DownloadItem.Title);
                    item.RemoteBook = null;

                    if (parsedBookInfo != null)
                    {
                        item.RemoteBook = _parsingService.Map(parsedBookInfo);
                    }
                }

                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }

        // Queue quality for a light novel (2026-09-21): the client's title is graded like a manga
        // name -- a volume-tokened EPUB batch read "CBZ" in Activity while History said EPUB. The
        // library regrade the search applies runs here too, and the media type follows it.
        private static void RegradeForLibrary(TrackedDownload trackedDownload, string title)
        {
            var remoteBook = trackedDownload.RemoteBook;

            if (remoteBook?.ParsedBookInfo == null || remoteBook.Author == null)
            {
                return;
            }

            if (ParsingService.RegradeForLibrary(remoteBook.ParsedBookInfo, title, remoteBook.Author, null, null))
            {
                remoteBook.MediaType = ParsingService.ResolveMediaType(remoteBook.ParsedBookInfo, remoteBook.Author, null, title);
            }
        }

        public void StopTracking(string downloadId)
        {
            var trackedDownload = _cache.Find(downloadId);

            _cache.Remove(downloadId);
            _eventAggregator.PublishEvent(new TrackedDownloadsRemovedEvent(new List<TrackedDownload> { trackedDownload }));
        }

        public void StopTracking(List<string> downloadIds)
        {
            var trackedDownloads = new List<TrackedDownload>();

            foreach (var downloadId in downloadIds)
            {
                var trackedDownload = _cache.Find(downloadId);
                _cache.Remove(downloadId);
                trackedDownloads.Add(trackedDownload);
            }

            _eventAggregator.PublishEvent(new TrackedDownloadsRemovedEvent(trackedDownloads));
        }

        public TrackedDownload TrackDownload(DownloadClientDefinition downloadClient, DownloadClientItem downloadItem)
        {
            var existingItem = Find(downloadItem.DownloadId);

            if (existingItem != null && existingItem.State != TrackedDownloadState.Downloading)
            {
                LogItemChange(existingItem, existingItem.DownloadItem, downloadItem);

                existingItem.DownloadItem = downloadItem;
                existingItem.IsTrackable = true;

                return existingItem;
            }

            var trackedDownload = new TrackedDownload
            {
                DownloadClient = downloadClient.Id,
                DownloadItem = downloadItem,
                Protocol = downloadClient.Protocol,
                IsTrackable = true
            };

            try
            {
                var parsedBookInfo = Parser.Parser.ParseBookTitle(trackedDownload.DownloadItem.Title);
                var historyItems = _historyService.FindByDownloadId(downloadItem.DownloadId)
                    .OrderByDescending(h => h.Date)
                    .ToList();

                var firstHistoryItem = historyItems.FirstOrDefault();
                var grabbedEvent = historyItems.FirstOrDefault(v => v.EventType == EntityHistoryEventType.Grabbed);
                var grabbedBookIds = historyItems.Where(v => v.EventType == EntityHistoryEventType.Grabbed)
                    .Select(h => h.BookId)
                    .Distinct()
                    .ToList();

                // Light novels (2026-09): the class the grab row chose (falling back to the newest row).
                var grabbedMediaType = firstHistoryItem != null
                    ? HistoryService.MediaTypeOf(grabbedEvent ?? firstHistoryItem)
                    : (MediaType?)null;

                // A grabbed download is the grab's book (2026-09-20): the decision engine already
                // matched the release against the searched series, and the client's title alone is
                // ambiguous between look-alike series ("Sword Art Online Progressive - Canon of the
                // Golden Rule v01" parses as SAO Progressive v1) -- mapping by title first re-homed
                // a grab onto the wrong series every day.
                if (grabbedEvent != null && grabbedEvent.AuthorId > 0 && grabbedBookIds.Any())
                {
                    try
                    {
                        var info = parsedBookInfo
                                   ?? Parser.Parser.ParseBookTitle(grabbedEvent.SourceTitle)
                                   ?? Parser.Parser.ParseBookTitleWithSearchCriteria(grabbedEvent.SourceTitle,
                                       grabbedEvent.Author,
                                       new List<Book> { grabbedEvent.Book },
                                       grabbedMediaType);

                        if (info != null)
                        {
                            trackedDownload.RemoteBook = _parsingService.Map(info, grabbedEvent.AuthorId, grabbedBookIds);
                            RegradeForLibrary(trackedDownload, downloadItem.Title);
                            _logger.Debug("Mapped '{0}' from its grab history: {1}", downloadItem.Title, trackedDownload.RemoteBook);
                        }
                    }
                    catch (Exception ex)
                    {
                        // An orphan grab row (a grabbed book deleted while its author remains, until
                        // the housekeeper runs) makes the book lookup throw on the row-count check.
                        // That must not take the download out of the queue -- the outer catch returns
                        // null and the item vanishes from it -- so the title path stands in, as it
                        // does for a download Mangarr never grabbed.
                        _logger.Debug(ex, "Grab history of '{0}' no longer resolves; mapping from the title", downloadItem.Title);

                        if (parsedBookInfo != null)
                        {
                            trackedDownload.RemoteBook = _parsingService.Map(parsedBookInfo);
                            RegradeForLibrary(trackedDownload, downloadItem.Title);
                        }
                    }
                }
                else if (parsedBookInfo != null)
                {
                    trackedDownload.RemoteBook = _parsingService.Map(parsedBookInfo);
                    RegradeForLibrary(trackedDownload, downloadItem.Title);
                }

                var downloadHistory = _downloadHistoryService.GetLatestDownloadHistoryItem(downloadItem.DownloadId);

                if (downloadHistory != null)
                {
                    var state = GetStateFromHistory(downloadHistory.EventType);
                    trackedDownload.State = state;

                    if (downloadHistory.EventType == DownloadHistoryEventType.DownloadImportIncomplete)
                    {
                        var messages = Json.Deserialize<List<TrackedDownloadStatusMessage>>(downloadHistory.Data["statusMessages"]).ToArray();
                        trackedDownload.Warn(messages);

                        // A persisted failure consisting solely of "already imported" rejections
                        // means the content is fully in the library; queue one more import attempt
                        // so CompletedDownloadService resolves it to Imported and it leaves the queue.
                        if (messages.Any() && messages.All(m => m.Messages != null && m.Messages.Any() && m.Messages.All(AlreadyImportedSpecification.IsAlreadyImportedRejection)))
                        {
                            trackedDownload.State = TrackedDownloadState.ImportPending;
                        }
                    }
                }

                if (historyItems.Any())
                {
                    trackedDownload.Indexer = grabbedEvent?.Data?.GetValueOrDefault("indexer");

                    // Only a download the grab did not map reaches the history fallback: rows
                    // without a Grabbed event (a download Mangarr did not grab), and grabs whose
                    // own mapping came back empty.
                    if (trackedDownload.RemoteBook?.Author == null ||
                        trackedDownload.RemoteBook.Books.Empty())
                    {
                        // Try parsing the original source title and if that fails, try parsing it as a special
                        var historyAuthor = firstHistoryItem.Author;
                        var historyBooks = new List<Book> { firstHistoryItem.Book };

                        parsedBookInfo = Parser.Parser.ParseBookTitle(firstHistoryItem.SourceTitle);

                        if (parsedBookInfo != null)
                        {
                            trackedDownload.RemoteBook = _parsingService.Map(parsedBookInfo,
                                firstHistoryItem.AuthorId,
                                historyItems.Where(v => v.EventType == EntityHistoryEventType.Grabbed).Select(h => h.BookId)
                                    .Distinct());
                        }
                        else
                        {
                            // The grab's media type reaches the re-parse (2026-09-18): an Audible-named
                            // audio release the generic parser cannot read is bridged here as the
                            // decision maker bridged it, so the queue item carries its RemoteBook.
                            parsedBookInfo =
                                Parser.Parser.ParseBookTitleWithSearchCriteria(firstHistoryItem.SourceTitle,
                                    historyAuthor,
                                    historyBooks,
                                    grabbedMediaType);

                            if (parsedBookInfo != null)
                            {
                                trackedDownload.RemoteBook = _parsingService.Map(parsedBookInfo,
                                    firstHistoryItem.AuthorId,
                                    historyItems.Where(v => v.EventType == EntityHistoryEventType.Grabbed).Select(h => h.BookId)
                                        .Distinct());
                            }
                        }
                    }

                    if (trackedDownload.RemoteBook != null &&
                        Enum.TryParse(grabbedEvent?.Data?.GetValueOrDefault("indexerFlags"), true, out IndexerFlags flags))
                    {
                        trackedDownload.RemoteBook.Release ??= new ReleaseInfo();
                        trackedDownload.RemoteBook.Release.IndexerFlags = flags;
                    }

                    // Light novels (2026-09): the client's title is mapped with no author and no
                    // library regrade, so a tokenless light-novel title grades Archive here while
                    // the decision maker graded it EPUB / audio. The grab row carries the class it
                    // chose (falling back to the newest row); the queue item inherits it so the
                    // queue spec compares like with like. Manga rows all say archive, as before.
                    if (trackedDownload.RemoteBook != null)
                    {
                        trackedDownload.RemoteBook.MediaType = grabbedMediaType.Value;
                    }
                }

                // Re-bound a history-inherited mapping with the download client's own title: the
                // indexer title may be volume-less (the batch bridge fans out to every volume of
                // the series) while the torrent folder name carries the real range
                // ("Series - Volumes 1 to 20"). Over-expansion breaks VerifyImport's count check
                // and creates queue rows/history noise for volumes the release never contained.
                // Only a true range triggers; a bounded grab intersected with the same range is a
                // no-op, and a mismatched range never empties the mapping. Note: UpdateCachedItem
                // (book/author-deletion events) rebuilds the mapping without this bounding.
                if (trackedDownload.RemoteBook?.Books != null && trackedDownload.RemoteBook.Books.Count > 1)
                {
                    var clientRange = new ParsedBookInfo();
                    MangaVolumeParser.ParseVolume(trackedDownload.DownloadItem.Title, clientRange);

                    if (clientRange.VolumeStart.HasValue && clientRange.VolumeEnd.HasValue)
                    {
                        var bounded = trackedDownload.RemoteBook.Books
                            .Where(b => b.VolumeNumber >= clientRange.VolumeStart.Value &&
                                        b.VolumeNumber <= clientRange.VolumeEnd.Value)
                            .ToList();

                        if (bounded.Any() && bounded.Count < trackedDownload.RemoteBook.Books.Count)
                        {
                            _logger.Info("Bounded mapping for '{0}': {1} -> {2} books via client-title range {3}-{4}",
                                trackedDownload.DownloadItem.Title, trackedDownload.RemoteBook.Books.Count,
                                bounded.Count, clientRange.VolumeStart, clientRange.VolumeEnd);
                            trackedDownload.RemoteBook.Books = bounded;
                        }
                    }
                }

                // Calculate custom formats
                if (trackedDownload.RemoteBook != null)
                {
                    trackedDownload.RemoteBook.CustomFormats = _formatCalculator.ParseCustomFormat(trackedDownload.RemoteBook, downloadItem.TotalSize);
                }

                // Track it so it can be displayed in the queue even though we can't determine which artist it is for
                if (trackedDownload.RemoteBook == null)
                {
                    _logger.Trace("No Book found for download '{0}'", trackedDownload.DownloadItem.Title);
                }
            }
            catch (Exception e)
            {
                _logger.Debug(e, "Failed to find book for " + downloadItem.Title);
                return null;
            }

            LogItemChange(trackedDownload, existingItem?.DownloadItem, trackedDownload.DownloadItem);

            _cache.Set(trackedDownload.DownloadItem.DownloadId, trackedDownload);
            return trackedDownload;
        }

        public List<TrackedDownload> GetTrackedDownloads()
        {
            return _cache.Values.ToList();
        }

        public void UpdateTrackable(List<TrackedDownload> trackedDownloads)
        {
            var untrackable = GetTrackedDownloads().ExceptBy(t => t.DownloadItem.DownloadId, trackedDownloads, t => t.DownloadItem.DownloadId, StringComparer.CurrentCulture).ToList();

            foreach (var trackedDownload in untrackable)
            {
                trackedDownload.IsTrackable = false;
            }
        }

        private void LogItemChange(TrackedDownload trackedDownload, DownloadClientItem existingItem, DownloadClientItem downloadItem)
        {
            if (existingItem == null ||
                existingItem.Status != downloadItem.Status ||
                existingItem.CanBeRemoved != downloadItem.CanBeRemoved ||
                existingItem.CanMoveFiles != downloadItem.CanMoveFiles)
            {
                _logger.Debug("Tracking '{0}:{1}': ClientState={2}{3} ReadarrStage={4} Book='{5}' OutputPath={6}.",
                    downloadItem.DownloadClientInfo.Name,
                    downloadItem.Title,
                    downloadItem.Status,
                    downloadItem.CanBeRemoved ? "" : downloadItem.CanMoveFiles ? " (busy)" : " (readonly)",
                    trackedDownload.State,
                    trackedDownload.RemoteBook?.ParsedBookInfo,
                    downloadItem.OutputPath);
            }
        }

        private void UpdateCachedItem(TrackedDownload trackedDownload)
        {
            var parsedEpisodeInfo = Parser.Parser.ParseBookTitle(trackedDownload.DownloadItem.Title);

            trackedDownload.RemoteBook = parsedEpisodeInfo == null ? null : _parsingService.Map(parsedEpisodeInfo, 0, new[] { 0 });
        }

        private static TrackedDownloadState GetStateFromHistory(DownloadHistoryEventType eventType)
        {
            switch (eventType)
            {
                case DownloadHistoryEventType.DownloadImportIncomplete:
                    return TrackedDownloadState.ImportFailed;
                case DownloadHistoryEventType.DownloadImported:
                    return TrackedDownloadState.Imported;
                case DownloadHistoryEventType.DownloadFailed:
                    return TrackedDownloadState.DownloadFailed;
                case DownloadHistoryEventType.DownloadIgnored:
                    return TrackedDownloadState.Ignored;
                default:
                    return TrackedDownloadState.Downloading;
            }
        }

        public void Handle(BookInfoRefreshedEvent message)
        {
            var needsToUpdate = false;

            foreach (var episode in message.Removed)
            {
                var cachedItems = _cache.Values.Where(t =>
                                            t.RemoteBook?.Books != null &&
                                            t.RemoteBook.Books.Any(e => e.Id == episode.Id))
                                        .ToList();

                if (cachedItems.Any())
                {
                    needsToUpdate = true;
                }

                cachedItems.ForEach(UpdateCachedItem);
            }

            if (needsToUpdate)
            {
                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }

        public void Handle(AuthorDeletedEvent message)
        {
            var cachedItems = _cache.Values.Where(t =>
                                        t.RemoteBook?.Author != null &&
                                        t.RemoteBook.Author.Id == message.Author.Id)
                                    .ToList();

            if (cachedItems.Any())
            {
                cachedItems.ForEach(UpdateCachedItem);

                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }
    }
}
