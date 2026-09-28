using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books
{
    public interface IRefreshBookService
    {
        bool RefreshBookInfo(Book book, List<Book> remoteBooks, Author remoteData, bool forceUpdateFileTags);
        bool RefreshBookInfo(List<Book> books, List<Book> remoteBooks, Author remoteData, bool forceBookRefresh, bool forceUpdateFileTags, DateTime? lastUpdate);
    }

    public class RefreshBookService : RefreshEntityServiceBase<Book, Edition>,
        IRefreshBookService,
        IExecute<RefreshBookCommand>,
        IExecute<BulkRefreshBookCommand>
    {
        private readonly IBookService _bookService;
        private readonly IAuthorService _authorService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IAddAuthorService _addAuthorService;
        private readonly IEditionService _editionService;
        private readonly IProvideAuthorInfo _authorInfo;
        private readonly IProvideBookInfo _bookInfo;
        private readonly IRefreshEditionService _refreshEditionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IHistoryService _historyService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ICheckIfBookShouldBeRefreshed _checkIfBookShouldBeRefreshed;
        private readonly IMapCoversToLocal _mediaCoverService;
        private readonly Logger _logger;

        public RefreshBookService(IBookService bookService,
                                  IAuthorService authorService,
                                  IRootFolderService rootFolderService,
                                  IAddAuthorService addAuthorService,
                                  IEditionService editionService,
                                  IAuthorMetadataService authorMetadataService,
                                  IProvideAuthorInfo authorInfo,
                                  IProvideBookInfo bookInfo,
                                  IRefreshEditionService refreshEditionService,
                                  IMediaFileService mediaFileService,
                                  IHistoryService historyService,
                                  IEventAggregator eventAggregator,
                                  ICheckIfBookShouldBeRefreshed checkIfBookShouldBeRefreshed,
                                  IMapCoversToLocal mediaCoverService,
                                  Logger logger)
        : base(logger, authorMetadataService)
        {
            _bookService = bookService;
            _authorService = authorService;
            _rootFolderService = rootFolderService;
            _addAuthorService = addAuthorService;
            _editionService = editionService;
            _authorInfo = authorInfo;
            _bookInfo = bookInfo;
            _refreshEditionService = refreshEditionService;
            _mediaFileService = mediaFileService;
            _historyService = historyService;
            _eventAggregator = eventAggregator;
            _checkIfBookShouldBeRefreshed = checkIfBookShouldBeRefreshed;
            _mediaCoverService = mediaCoverService;
            _logger = logger;
        }

        private Author GetSkyhookData(Book book)
        {
            try
            {
                var tuple = _bookInfo.GetBookInfo(book.ForeignBookId);
                var author = _authorInfo.GetAuthorInfo(tuple.Item1);
                var newbook = tuple.Item2;

                newbook.Author = author;
                newbook.AuthorMetadata = author.Metadata.Value;
                newbook.AuthorMetadataId = book.AuthorMetadataId;
                newbook.AuthorMetadata.Value.Id = book.AuthorMetadataId;

                author.Books = new List<Book> { newbook };
                return author;
            }
            catch (BookNotFoundException)
            {
                _logger.Error($"Could not find book with id {book.ForeignBookId}");
            }

            return null;
        }

        protected override RemoteData GetRemoteData(Book local, List<Book> remote, Author data)
        {
            var result = new RemoteData();

            var book = remote.SingleOrDefault(x => x.ForeignBookId == local.ForeignBookId);

            if (book == null && ShouldDelete(local))
            {
                return result;
            }

            if (book == null)
            {
                data = GetSkyhookData(local);
                book = data.Books.Value.SingleOrDefault(x => x.ForeignBookId == local.ForeignBookId);
            }

            result.Entity = book;
            if (result.Entity != null)
            {
                result.Entity.Id = local.Id;
            }

            return result;
        }

        protected override void EnsureNewParent(Book local, Book remote)
        {
            // Make sure the appropriate author exists (it could be that an book changes parent)
            // The authorMetadata entry will be in the db but make sure a corresponding author is too
            // so that the book doesn't just disappear.

            // TODO filter by metadata id before hitting database
            _logger.Trace($"Ensuring parent author exists [{remote.AuthorMetadata.Value.ForeignAuthorId}]");

            var newAuthor = _authorService.FindById(remote.AuthorMetadata.Value.ForeignAuthorId);

            if (newAuthor == null)
            {
                var oldAuthor = local.Author.Value;
                var addAuthor = new Author
                {
                    Metadata = remote.AuthorMetadata.Value,
                    MetadataProfileId = oldAuthor.MetadataProfileId,
                    QualityProfileId = oldAuthor.QualityProfileId,
                    RootFolderPath = _rootFolderService.GetBestRootFolderPath(oldAuthor.Path),
                    Monitored = oldAuthor.Monitored,
                    Tags = oldAuthor.Tags
                };
                _logger.Debug($"Adding missing parent author {addAuthor}");
                _addAuthorService.AddAuthor(addAuthor);
            }
        }

        protected override bool ShouldDelete(Book local)
        {
            // not manually added and has no files
            return local.AddOptions.AddType != BookAddType.Manual &&
                !_mediaFileService.GetFilesByBook(local.Id).Any();
        }

        protected override void LogProgress(Book local)
        {
            _logger.ProgressInfo("Updating Info for {0}", local.Title);
        }

        protected override bool IsMerge(Book local, Book remote)
        {
            return local.ForeignBookId != remote.ForeignBookId;
        }

        protected override UpdateResult UpdateEntity(Book local, Book remote)
        {
            UpdateResult result;

            remote.UseDbFieldsFrom(local);

            if (local.Title != (remote.Title ?? "Unknown") ||
                local.ForeignBookId != remote.ForeignBookId ||
                local.AuthorMetadata.Value.ForeignAuthorId != remote.AuthorMetadata.Value.ForeignAuthorId)
            {
                result = UpdateResult.UpdateTags;
            }
            else if (!local.Equals(remote))
            {
                result = UpdateResult.Standard;
            }
            else
            {
                result = UpdateResult.None;
            }

            // Force update and fetch covers if images have changed so that we can write them into tags
            // if (remote.Images.Any() && !local.Images.SequenceEqual(remote.Images))
            // {
            //     _mediaCoverService.EnsureBookCovers(remote);
            //     result = UpdateResult.UpdateTags;
            // }
            local.UseMetadataFrom(remote);

            local.AuthorMetadataId = remote.AuthorMetadata.Value.Id;
            local.LastInfoSync = DateTime.UtcNow;

            return result;
        }

        protected override UpdateResult MergeEntity(Book local, Book target, Book remote)
        {
            _logger.Warn($"Book {local} was merged with {remote} because the original was a duplicate.");

            // Update edition ids for the files: each file moves to the target's edition of ITS media
            // type (an .epub to the Ebook edition, an .m4b to the Audio one). A manga file has one
            // choice, as before.
            var files = _mediaFileService.GetFilesByBook(local.Id);
            foreach (var file in files)
            {
                file.EditionId = MergeTargetEdition(file, target).Id;
            }

            _mediaFileService.Update(files);

            // Update book ids for history
            var items = _historyService.GetByBook(local.Id, null);
            items.ForEach(x => x.BookId = target.Id);
            _historyService.UpdateMany(items);

            // Finally delete the old book
            _bookService.DeleteMany(new List<Book> { local });

            return UpdateResult.UpdateTags;
        }

        // The target edition a merged file moves to: the edition of the file's own media type, else
        // the display edition. LN PDF (2026-09-22): a file whose edition is gone is typed by its
        // extension against the TARGET's library (only read then), so a light novel's PDF goes to the
        // Ebook edition.
        public static Edition MergeTargetEdition(BookFile file, Book target)
        {
            var mediaType = file.Edition?.Value?.MediaType ?? MediaTypes.OfFile(file.Path, LibraryTypes.Of(target));

            return target.EditionOf(mediaType) ?? target.PrimaryEdition();
        }

        protected override Book GetEntityByForeignId(Book local)
        {
            return _bookService.FindById(local.ForeignBookId);
        }

        protected override void SaveEntity(Book local)
        {
            // Use UpdateMany to avoid firing the book edited event
            _bookService.UpdateMany(new List<Book> { local });
        }

        protected override void DeleteEntity(Book local, bool deleteFiles)
        {
            _bookService.DeleteBook(local.Id, deleteFiles);
        }

        protected override List<Edition> GetRemoteChildren(Book local, Book remote)
        {
            return remote.Editions.Value.DistinctBy(m => m.ForeignEditionId).ToList();
        }

        protected override List<Edition> GetLocalChildren(Book entity, List<Edition> remoteChildren)
        {
            return _editionService.GetEditionsForRefresh(entity.Id, remoteChildren.Select(x => x.ForeignEditionId).ToList());
        }

        protected override Tuple<Edition, List<Edition>> GetMatchingExistingChildren(List<Edition> existingChildren, Edition remote)
        {
            var existingChild = existingChildren.SingleOrDefault(x => x.ForeignEditionId == remote.ForeignEditionId);
            return Tuple.Create(existingChild, new List<Edition>());
        }

        protected override void PrepareNewChild(Edition child, Book entity)
        {
            child.BookId = entity.Id;
            child.Book = entity;
        }

        protected override void PrepareExistingChild(Edition local, Edition remote, Book entity)
        {
            local.BookId = entity.Id;
            local.Book = entity;

            remote.UseDbFieldsFrom(local);
        }

        protected override void AddChildren(List<Edition> children)
        {
            // hack - add the chilren in refresh children so we can control monitored status
        }

        // At most one monitored edition per media type: a light-novel volume has an Ebook and an
        // Audio edition, each monitored on its own. Within a media type that ended up with more
        // than one monitored edition, keep the one with files (then the most popular) and unmonitor
        // the rest; a media type with NO monitored edition is left alone (the per-edition toggle
        // must survive a refresh) -- except a single-edition book (every manga volume), whose only
        // edition is always monitored, exactly as before.
        private void MonitorSingleEdition(SortedChildren children)
        {
            children.Old.ForEach(x => x.Monitored = false);

            var changed = CollapseMonitoredPerMediaType(children.Future, x => x.Id > 0 ? _mediaFileService.GetFilesByEdition(x.Id).Count : 0);

            // force update of anything we've messed with
            var extraToUpdate = children.UpToDate.Where(x => changed.Contains(x)).ToList();
            children.UpToDate = children.UpToDate.Except(extraToUpdate).ToList();
            children.Updated.AddRange(extraToUpdate);

            Debug.Assert(children.Future.GroupBy(x => x.MediaType).All(g => g.Count(x => x.Monitored) <= 1), "at most one monitored edition per media type");
        }

        // Returns the editions whose Monitored flag was changed. Public static so the rule is unit-tested
        // without the refresh plumbing (same shape as MangaSeriesMetadataProvider.DetermineVolumeCount).
        public static List<Edition> CollapseMonitoredPerMediaType(List<Edition> future, Func<Edition, int> fileCount)
        {
            var changed = new List<Edition>();

            if (future.Count == 1 && !future[0].Monitored)
            {
                future[0].Monitored = true;
                changed.Add(future[0]);
                return changed;
            }

            foreach (var group in future.GroupBy(x => x.MediaType))
            {
                var monitored = group.Where(x => x.Monitored).ToList();

                if (monitored.Count <= 1)
                {
                    continue;
                }

                var toMonitor = monitored.OrderByDescending(fileCount)
                    .ThenByDescending(x => x.Ratings.Popularity)
                    .First();

                foreach (var edition in monitored.Where(x => x != toMonitor))
                {
                    edition.Monitored = false;
                    changed.Add(edition);
                }
            }

            return changed;
        }

        // A newly minted edition arrives Monitored = true (BookInfoProxy.MintEdition), which would
        // re-tick a format left unticked at add time on every catalogue volume minted later (final
        // review I3). Seed it from the series instead: an added edition of a class that NO existing
        // edition of the series monitors is inserted unmonitored. A series with no editions yet (the
        // first refresh after add) keeps the minted flags -- AuthorScannedHandler applies the add-time
        // choice after that scan. Manga never enters: no query, the one Archive edition is minted
        // monitored as before.
        private void SeedAddedEditionsFromSeries(List<Edition> added)
        {
            var author = added.FirstOrDefault()?.Book?.Value?.Author?.Value;

            if (author == null || author.Library != LibraryType.LightNovel)
            {
                return;
            }

            var existing = _editionService.GetEditionsByAuthor(author.Id);

            foreach (var edition in SeedMonitoredFromSeries(added, existing))
            {
                _logger.Debug("New {0} edition of {1} unmonitored: no volume of {2} monitors that format", edition.MediaType, edition.Book.Value, author.Name);
            }
        }

        // Returns the editions it unmonitored. Public static so the rule is unit-tested without the
        // refresh plumbing (same shape as CollapseMonitoredPerMediaType).
        public static List<Edition> SeedMonitoredFromSeries(List<Edition> added, List<Edition> existing)
        {
            var changed = new List<Edition>();

            if (!existing.Any())
            {
                return changed;
            }

            var monitoredTypes = existing.Where(e => e.Monitored).Select(e => e.MediaType).ToHashSet();

            foreach (var edition in added.Where(e => e.Monitored && !monitoredTypes.Contains(e.MediaType)))
            {
                edition.Monitored = false;
                changed.Add(edition);
            }

            return changed;
        }

        protected override bool RefreshChildren(SortedChildren localChildren, List<Edition> remoteChildren, Author remoteData, bool forceChildRefresh, bool forceUpdateFileTags, DateTime? lastUpdate)
        {
            // a new light-novel volume's editions follow the series' per-format choice
            SeedAddedEditionsFromSeries(localChildren.Added);

            // at most one monitored edition per media type
            MonitorSingleEdition(localChildren);

            localChildren.All.ForEach(x => _logger.Trace($"release: {x} monitored: {x.Monitored}"));

            _editionService.InsertMany(localChildren.Added);

            return _refreshEditionService.RefreshEditionInfo(localChildren.Added, localChildren.Updated, localChildren.Merged, localChildren.Deleted, localChildren.UpToDate, remoteChildren, forceUpdateFileTags);
        }

        protected override void PublishEntityUpdatedEvent(Book entity)
        {
            // Fetch fresh from DB so all lazy loads are available
            _eventAggregator.PublishEvent(new BookUpdatedEvent(_bookService.GetBook(entity.Id)));
        }

        public bool RefreshBookInfo(List<Book> books, List<Book> remoteBooks, Author remoteData, bool forceBookRefresh, bool forceUpdateFileTags, DateTime? lastUpdate)
        {
            var updated = false;

            foreach (var book in books)
            {
                if (forceBookRefresh || _checkIfBookShouldBeRefreshed.ShouldRefresh(book))
                {
                    updated |= RefreshBookInfo(book, remoteBooks, remoteData, forceUpdateFileTags);
                }
                else
                {
                    _logger.Debug("Skipping refresh of book: {0}", book.Title);
                }
            }

            return updated;
        }

        public bool RefreshBookInfo(Book book, List<Book> remoteBooks, Author remoteData, bool forceUpdateFileTags)
        {
            return RefreshEntityInfo(book, remoteBooks, remoteData, true, forceUpdateFileTags, null);
        }

        public bool RefreshBookInfo(Book book)
        {
            Author data;

            try
            {
                data = GetSkyhookData(book);
            }
            catch (EditionUnavailableException e)
            {
                // Preferred Edition (2026-09-24, ruling S10, M5 pre-review fix): the series' bound edition
                // is gone from the catalogue -- a Warn, and the volume stays exactly as stored.
                _logger.Warn("Couldn't refresh info for {0}: {1}; keeping it as stored", book, e.Message);
                return false;
            }

            return RefreshBookInfo(book, data.Books, data, false);
        }

        public void Execute(BulkRefreshBookCommand message)
        {
            var books = _bookService.GetBooks(message.BookIds);

            foreach (var book in books)
            {
                RefreshBookInfo(book);
            }
        }

        public void Execute(RefreshBookCommand message)
        {
            if (message.BookId.HasValue)
            {
                var book = _bookService.GetBook(message.BookId.Value);

                RefreshBookInfo(book);
            }
        }
    }
}
