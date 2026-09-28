using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Localization;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.History
{
    public interface IHistoryService
    {
        PagingSpec<EntityHistory> Paged(PagingSpec<EntityHistory> pagingSpec);
        EntityHistory MostRecentForBook(int bookId);
        EntityHistory MostRecentForBook(int bookId, MediaType mediaType);
        EntityHistory MostRecentForDownloadId(string downloadId);
        EntityHistory Get(int historyId);
        List<EntityHistory> GetByAuthor(int authorId, EntityHistoryEventType? eventType);
        List<EntityHistory> GetByBook(int bookId, EntityHistoryEventType? eventType);
        List<EntityHistory> Find(string downloadId, EntityHistoryEventType eventType);
        List<EntityHistory> FindByDownloadId(string downloadId);
        string FindDownloadId(TrackImportedEvent trackedDownload);
        List<EntityHistory> Since(DateTime date, EntityHistoryEventType? eventType);
        void UpdateMany(IList<EntityHistory> items);
    }

    public class HistoryService : IHistoryService,
                                  IHandle<BookGrabbedEvent>,
                                  IHandle<BookImportIncompleteEvent>,
                                  IHandle<TrackImportedEvent>,
                                  IHandle<DownloadFailedEvent>,
                                  IHandle<BookFileDeletedEvent>,
                                  IHandle<BookFileRenamedEvent>,
                                  IHandle<BookFileRetaggedEvent>,
                                  IHandle<AuthorDeletedEvent>,
                                  IHandle<DownloadIgnoredEvent>
    {
        private readonly IHistoryRepository _historyRepository;
        private readonly IServerMessageLocalizer _serverMessages;
        private readonly Logger _logger;

        public HistoryService(IHistoryRepository historyRepository, IServerMessageLocalizer serverMessages, Logger logger)
        {
            _historyRepository = historyRepository;
            _serverMessages = serverMessages;
            _logger = logger;
        }

        public PagingSpec<EntityHistory> Paged(PagingSpec<EntityHistory> pagingSpec)
        {
            return _historyRepository.GetPaged(pagingSpec);
        }

        public EntityHistory MostRecentForBook(int bookId)
        {
            return _historyRepository.MostRecentForBook(bookId);
        }

        public EntityHistory MostRecentForBook(int bookId, MediaType mediaType)
        {
            return _historyRepository.GetByBook(bookId, null)
                                     .Where(h => MediaTypeOf(h) == mediaType)
                                     .MaxBy(h => h.Date);
        }

        // Which edition class a history row was for. Rows written before 2026-09 carry no key;
        // their quality says which class they were (every manga row is an archive quality), so
        // old history keeps working unchanged.
        public static MediaType MediaTypeOf(EntityHistory history)
        {
            return MediaTypeOf(history.Data, history.Quality);
        }

        // The same from a row's parts (DownloadFailedEvent carries the grab row's Data and Quality).
        public static MediaType MediaTypeOf(Dictionary<string, string> data, QualityModel quality)
        {
            if (data != null &&
                data.TryGetValue(EntityHistory.MEDIA_TYPE, out var value) &&
                Enum.TryParse<MediaType>(value, true, out var parsed))
            {
                return parsed;
            }

            return MediaTypes.OfQuality(quality?.Quality ?? Quality.Unknown);
        }

        private static string MediaTypeKey(MediaType mediaType)
        {
            return mediaType.ToString().ToLowerInvariant();
        }

        public EntityHistory MostRecentForDownloadId(string downloadId)
        {
            return _historyRepository.MostRecentForDownloadId(downloadId);
        }

        public EntityHistory Get(int historyId)
        {
            return _historyRepository.Get(historyId);
        }

        public List<EntityHistory> GetByAuthor(int authorId, EntityHistoryEventType? eventType)
        {
            return _historyRepository.GetByAuthor(authorId, eventType);
        }

        public List<EntityHistory> GetByBook(int bookId, EntityHistoryEventType? eventType)
        {
            return _historyRepository.GetByBook(bookId, eventType);
        }

        public List<EntityHistory> Find(string downloadId, EntityHistoryEventType eventType)
        {
            return _historyRepository.FindByDownloadId(downloadId).Where(c => c.EventType == eventType).ToList();
        }

        public List<EntityHistory> FindByDownloadId(string downloadId)
        {
            return _historyRepository.FindByDownloadId(downloadId);
        }

        public string FindDownloadId(TrackImportedEvent trackedDownload)
        {
            _logger.Debug("Trying to find downloadId for {0} from history", trackedDownload.ImportedBook.Path);

            var bookIds = new List<int> { trackedDownload.BookInfo.Book.Id };
            var allHistory = _historyRepository.FindDownloadHistory(trackedDownload.BookInfo.Author.Id, trackedDownload.ImportedBook.Quality);

            //Find download related items for these episodes
            var booksHistory = allHistory.Where(h => bookIds.Contains(h.BookId)).ToList();

            var processedDownloadId = booksHistory
                .Where(c => c.EventType != EntityHistoryEventType.Grabbed && c.DownloadId != null)
                .Select(c => c.DownloadId);

            var stillDownloading = booksHistory.Where(c => c.EventType == EntityHistoryEventType.Grabbed && !processedDownloadId.Contains(c.DownloadId)).ToList();

            string downloadId = null;

            if (stillDownloading.Any())
            {
                var matchingHistory = stillDownloading.Where(c => c.BookId == trackedDownload.BookInfo.Book.Id).ToList();

                if (matchingHistory.Count != 1)
                {
                    return null;
                }

                var newDownloadId = matchingHistory.Single().DownloadId;

                if (downloadId == null || downloadId == newDownloadId)
                {
                    downloadId = newDownloadId;
                }
                else
                {
                    return null;
                }
            }

            return downloadId;
        }

        public void Handle(BookGrabbedEvent message)
        {
            // A re-grab of the same release (e.g. a failed batch re-found by a scheduled
            // search) would duplicate the per-book fan-out rows — a whole-series batch
            // writes one row per mapped volume — and duplicated rows corrupt
            // failed-download scoping. One Grabbed row per (book, download) is enough.
            var previouslyGrabbed = message.DownloadId.IsNullOrWhiteSpace()
                ? new HashSet<int>()
                : new HashSet<int>(_historyRepository.FindByDownloadId(message.DownloadId)
                    .Where(h => h.EventType == EntityHistoryEventType.Grabbed)
                    .Select(h => h.BookId));

            foreach (var book in message.Book.Books)
            {
                if (previouslyGrabbed.Contains(book.Id))
                {
                    continue;
                }

                var history = new EntityHistory
                {
                    EventType = EntityHistoryEventType.Grabbed,
                    Date = DateTime.UtcNow,
                    Quality = message.Book.ParsedBookInfo.Quality,
                    SourceTitle = message.Book.Release.Title,
                    AuthorId = book.AuthorId,
                    BookId = book.Id,
                    DownloadId = message.DownloadId
                };

                history.Data.Add("Indexer", message.Book.Release.Indexer);
                history.Data.Add("NzbInfoUrl", message.Book.Release.InfoUrl);
                history.Data.Add("ReleaseGroup", message.Book.ParsedBookInfo.ReleaseGroup);
                history.Data.Add("Age", message.Book.Release.Age.ToString());
                history.Data.Add("AgeHours", message.Book.Release.AgeHours.ToString());
                history.Data.Add("AgeMinutes", message.Book.Release.AgeMinutes.ToString());
                history.Data.Add("PublishedDate", message.Book.Release.PublishDate.ToString("s") + "Z");
                history.Data.Add("DownloadClient", message.DownloadClient);
                history.Data.Add("DownloadClientName", message.DownloadClientName);
                history.Data.Add("Size", message.Book.Release.Size.ToString());
                history.Data.Add("DownloadUrl", message.Book.Release.DownloadUrl);
                history.Data.Add("Guid", message.Book.Release.Guid);
                history.Data.Add("Protocol", ((int)message.Book.Release.DownloadProtocol).ToString());
                history.Data.Add("DownloadForced", (!message.Book.DownloadAllowed).ToString());
                history.Data.Add("CustomFormatScore", message.Book.CustomFormatScore.ToString());
                history.Data.Add("ReleaseSource", message.Book.ReleaseSource.ToString());
                history.Data.Add("IndexerFlags", message.Book.Release.IndexerFlags.ToString());
                history.Data.Add(EntityHistory.MEDIA_TYPE, MediaTypeKey(message.Book.MediaType));

                if (!message.Book.ParsedBookInfo.ReleaseHash.IsNullOrWhiteSpace())
                {
                    history.Data.Add("ReleaseHash", message.Book.ParsedBookInfo.ReleaseHash);
                }

                if (message.Book.Release is TorrentInfo torrentRelease)
                {
                    history.Data.Add("TorrentInfoHash", torrentRelease.InfoHash);
                }

                _historyRepository.Insert(history);
            }
        }

        public void Handle(BookImportIncompleteEvent message)
        {
            if (message.TrackedDownload.RemoteBook == null)
            {
                return;
            }

            // The same for every volume of the download: serialized and recorded once.
            var statusMessages = message.TrackedDownload.StatusMessages.ToJson();
            var statusMessageKeys = StatusMessageKeys(message.TrackedDownload.StatusMessages);

            foreach (var book in message.TrackedDownload.RemoteBook.Books)
            {
                var history = new EntityHistory
                {
                    EventType = EntityHistoryEventType.BookImportIncomplete,
                    Date = DateTime.UtcNow,
                    Quality = message.TrackedDownload.RemoteBook.ParsedBookInfo?.Quality ?? new QualityModel(),
                    SourceTitle = message.TrackedDownload.DownloadItem.Title,
                    AuthorId = book.AuthorId,
                    BookId = book.Id,
                    DownloadId = message.TrackedDownload.DownloadItem.DownloadId
                };

                history.Data.Add("StatusMessages", statusMessages);

                if (statusMessageKeys != null)
                {
                    history.Data.Add("StatusMessageKeys", statusMessageKeys);
                }

                history.Data.Add("ReleaseGroup", message.TrackedDownload?.RemoteBook?.ParsedBookInfo?.ReleaseGroup);
                history.Data.Add("IndexerFlags", message.TrackedDownload?.RemoteBook?.Release?.IndexerFlags.ToString());
                history.Data.Add(EntityHistory.MEDIA_TYPE, MediaTypeKey(message.TrackedDownload.RemoteBook.MediaType));

                _historyRepository.Insert(history);
            }
        }

        public void Handle(TrackImportedEvent message)
        {
            if (!message.NewDownload)
            {
                return;
            }

            var downloadId = message.DownloadId;

            if (downloadId.IsNullOrWhiteSpace())
            {
                downloadId = FindDownloadId(message);
            }

            var history = new EntityHistory
            {
                EventType = EntityHistoryEventType.BookFileImported,
                Date = DateTime.UtcNow,
                Quality = message.BookInfo.Quality,
                SourceTitle = message.ImportedBook.SceneName ?? Path.GetFileNameWithoutExtension(message.BookInfo.Path),
                AuthorId = message.BookInfo.Author.Id,
                BookId = message.BookInfo.Book.Id,
                DownloadId = downloadId
            };

            history.Data.Add("FileId", message.ImportedBook.Id.ToString());
            history.Data.Add("DroppedPath", message.BookInfo.Path);
            history.Data.Add("ImportedPath", message.ImportedBook.Path);
            history.Data.Add("DownloadClient", message.DownloadClientInfo?.Type);
            history.Data.Add("DownloadClientName", message.DownloadClientInfo?.Name);
            history.Data.Add("ReleaseGroup", message.BookInfo.ReleaseGroup);
            history.Data.Add("Size", message.BookInfo.Size.ToString());
            history.Data.Add("IndexerFlags", message.BookInfo.IndexerFlags.ToString());
            history.Data.Add(EntityHistory.MEDIA_TYPE, MediaTypeKey(message.BookInfo.Edition?.MediaType ?? MediaTypes.OfExtension(Path.GetExtension(message.BookInfo.Path))));

            _historyRepository.Insert(history);
        }

        public void Handle(DownloadFailedEvent message)
        {
            foreach (var bookId in message.BookIds)
            {
                var history = new EntityHistory
                {
                    EventType = EntityHistoryEventType.DownloadFailed,
                    Date = DateTime.UtcNow,
                    Quality = message.Quality,
                    SourceTitle = message.SourceTitle,
                    AuthorId = message.AuthorId,
                    BookId = bookId,
                    DownloadId = message.DownloadId
                };

                history.Data.Add("DownloadClient", message.DownloadClient);
                history.Data.Add("DownloadClientName", message.TrackedDownload?.DownloadItem.DownloadClientInfo.Name);
                history.Data.Add("Message", message.Message);
                AddMessageRecord(history, message.MessageText);
                history.Data.Add("ReleaseGroup", message.TrackedDownload?.RemoteBook?.ParsedBookInfo?.ReleaseGroup ?? message.Data.GetValueOrDefault(EntityHistory.RELEASE_GROUP));
                history.Data.Add("Size", message.TrackedDownload?.DownloadItem.TotalSize.ToString() ?? message.Data.GetValueOrDefault(EntityHistory.SIZE));
                history.Data.Add("Indexer", message.TrackedDownload?.RemoteBook?.Release?.Indexer ?? message.Data.GetValueOrDefault(EntityHistory.INDEXER));
                history.Data.Add(EntityHistory.MEDIA_TYPE, MediaTypeKey(message.TrackedDownload?.RemoteBook?.MediaType ?? MediaTypeOf(message.Data, message.Quality)));

                _historyRepository.Insert(history);
            }
        }

        public void Handle(BookFileDeletedEvent message)
        {
            if (message.Reason == DeleteMediaFileReason.NoLinkedEpisodes)
            {
                _logger.Debug("Removing book file from DB as part of cleanup routine, not creating history event.");
                return;
            }
            else if (message.Reason == DeleteMediaFileReason.ManualOverride)
            {
                _logger.Debug("Removing book file from DB as part of manual override of existing file, not creating history event.");
                return;
            }

            var history = new EntityHistory
            {
                EventType = EntityHistoryEventType.BookFileDeleted,
                Date = DateTime.UtcNow,
                Quality = message.BookFile.Quality,
                SourceTitle = message.BookFile.Path,
                AuthorId = message.BookFile.Author.Value.Id,
                BookId = message.BookFile.Edition.Value.BookId
            };

            history.Data.Add("Reason", message.Reason.ToString());
            history.Data.Add("ReleaseGroup", message.BookFile.ReleaseGroup);
            history.Data.Add("IndexerFlags", message.BookFile.IndexerFlags.ToString());
            history.Data.Add(EntityHistory.MEDIA_TYPE, MediaTypeKey(message.BookFile.Edition.Value.MediaType));

            _historyRepository.Insert(history);
        }

        public void Handle(BookFileRenamedEvent message)
        {
            var sourcePath = message.OriginalPath;
            var path = message.BookFile.Path;

            var history = new EntityHistory
            {
                EventType = EntityHistoryEventType.BookFileRenamed,
                Date = DateTime.UtcNow,
                Quality = message.BookFile.Quality,
                SourceTitle = message.OriginalPath,
                AuthorId = message.BookFile.Author.Value.Id,
                BookId = message.BookFile.Edition.Value.BookId
            };

            history.Data.Add("SourcePath", sourcePath);
            history.Data.Add("Path", path);
            history.Data.Add("ReleaseGroup", message.BookFile.ReleaseGroup);
            history.Data.Add("Size", message.BookFile.Size.ToString());
            history.Data.Add("IndexerFlags", message.BookFile.IndexerFlags.ToString());
            history.Data.Add(EntityHistory.MEDIA_TYPE, MediaTypeKey(message.BookFile.Edition.Value.MediaType));

            _historyRepository.Insert(history);
        }

        public void Handle(BookFileRetaggedEvent message)
        {
            var path = message.BookFile.Path;

            var history = new EntityHistory
            {
                EventType = EntityHistoryEventType.BookFileRetagged,
                Date = DateTime.UtcNow,
                Quality = message.BookFile.Quality,
                SourceTitle = path,
                AuthorId = message.BookFile.Author.Value.Id,
                BookId = message.BookFile.Edition.Value.BookId
            };

            history.Data.Add("TagsScrubbed", message.Scrubbed.ToString());
            history.Data.Add("Diff", message.Diff.Select(x => new
            {
                Field = x.Key,
                OldValue = x.Value.Item1,
                NewValue = x.Value.Item2
            }).ToJson());
            history.Data.Add(EntityHistory.MEDIA_TYPE, MediaTypeKey(message.BookFile.Edition.Value.MediaType));

            _historyRepository.Insert(history);
        }

        public void Handle(AuthorDeletedEvent message)
        {
            _historyRepository.DeleteForAuthor(message.Author.Id);
        }

        public void Handle(DownloadIgnoredEvent message)
        {
            var historyToAdd = new List<EntityHistory>();
            foreach (var bookId in message.BookIds)
            {
                var history = new EntityHistory
                {
                    EventType = EntityHistoryEventType.DownloadIgnored,
                    Date = DateTime.UtcNow,
                    Quality = message.Quality,
                    SourceTitle = message.SourceTitle,
                    AuthorId = message.AuthorId,
                    BookId = bookId,
                    DownloadId = message.DownloadId
                };

                history.Data.Add("DownloadClient", message.DownloadClientInfo.Name);
                history.Data.Add("Message", message.Message);
                AddMessageRecord(history, message.MessageText);
                history.Data.Add("ReleaseGroup", message.TrackedDownload?.RemoteBook?.ParsedBookInfo?.ReleaseGroup);
                history.Data.Add("Size", message.TrackedDownload?.DownloadItem.TotalSize.ToString());
                history.Data.Add("Indexer", message.TrackedDownload?.RemoteBook?.Release?.Indexer);
                history.Data.Add(EntityHistory.MEDIA_TYPE, MediaTypeKey(message.TrackedDownload?.RemoteBook?.MediaType ?? MediaTypes.OfQuality(message.Quality?.Quality)));

                historyToAdd.Add(history);
            }

            _historyRepository.InsertMany(historyToAdd);
        }

        public List<EntityHistory> Since(DateTime date, EntityHistoryEventType? eventType)
        {
            return _historyRepository.Since(date, eventType);
        }

        public void UpdateMany(IList<EntityHistory> items)
        {
            _historyRepository.UpdateMany(items);
        }

        // Server messages (2026-09-26, plan ruling R5): the Server* key and the rendered placeholders of a
        // message built from a template, beside the English "Message", so History shows it in the UI language
        // (HistoryResource). Old rows and plain messages have neither and are matched whole.
        private void AddMessageRecord(EntityHistory history, ServerText text)
        {
            var record = _serverMessages.Record(text);

            if (record != null)
            {
                history.Data.Add("MessageKey", record.Key);
                history.Data.Add("MessageArgs", System.Text.Json.JsonSerializer.Serialize(record.Args));
            }
        }

        // Server messages (2026-09-27, follow-ups): "StatusMessageKeys" parallels "StatusMessages" -- per status
        // message, per message, the {key, args} record of a message built from a template, or null -- so the
        // import-incomplete details show in the UI language (HistoryResource). Null (not stored) when no message has a key.
        private string StatusMessageKeys(TrackedDownloadStatusMessage[] statusMessages)
        {
            var records = statusMessages
                .Select(s => s.Messages?.Select((m, i) => RecordOf(m, s.MessageTexts != null && i < s.MessageTexts.Count ? s.MessageTexts[i] : null)).ToList())
                .ToList();

            return records.Any(r => r != null && r.Any(x => x != null)) ? records.ToJson() : null;
        }

        // Only a carrier that still describes the stored English (the localizer's own rule).
        private ServerMessageRecord RecordOf(string english, ServerText text)
        {
            return text != null && text.English == english ? _serverMessages.Record(text) : null;
        }
    }
}
