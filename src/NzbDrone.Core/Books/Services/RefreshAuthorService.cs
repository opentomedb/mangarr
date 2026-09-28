using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books
{
    public interface IRefreshAuthorService
    {
    }

    public class RefreshAuthorService : RefreshEntityServiceBase<Author, Book>,
        IRefreshAuthorService,
        IExecute<RefreshAuthorCommand>,
        IExecute<BulkRefreshAuthorCommand>
    {
        private readonly IProvideAuthorInfo _authorInfo;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IMetadataProfileService _metadataProfileService;
        private readonly IRefreshBookService _refreshBookService;
        private readonly IRefreshSeriesService _refreshSeriesService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IMediaFileService _mediaFileService;
        private readonly IHistoryService _historyService;
        private readonly IRootFolderService _rootFolderService;
        private readonly ICheckIfAuthorShouldBeRefreshed _checkIfAuthorShouldBeRefreshed;
        private readonly IMonitorNewBookService _monitorNewBookService;
        private readonly IConfigService _configService;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly Logger _logger;

        public RefreshAuthorService(IProvideAuthorInfo authorInfo,
                                    IAuthorService authorService,
                                    IAuthorMetadataService authorMetadataService,
                                    IBookService bookService,
                                    IMetadataProfileService metadataProfileService,
                                    IRefreshBookService refreshBookService,
                                    IRefreshSeriesService refreshSeriesService,
                                    IEventAggregator eventAggregator,
                                    IManageCommandQueue commandQueueManager,
                                    IMediaFileService mediaFileService,
                                    IHistoryService historyService,
                                    IRootFolderService rootFolderService,
                                    ICheckIfAuthorShouldBeRefreshed checkIfAuthorShouldBeRefreshed,
                                    IMonitorNewBookService monitorNewBookService,
                                    IConfigService configService,
                                    IImportListExclusionService importListExclusionService,
                                    Logger logger)
        : base(logger, authorMetadataService)
        {
            _authorInfo = authorInfo;
            _authorService = authorService;
            _bookService = bookService;
            _metadataProfileService = metadataProfileService;
            _refreshBookService = refreshBookService;
            _refreshSeriesService = refreshSeriesService;
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
            _mediaFileService = mediaFileService;
            _historyService = historyService;
            _rootFolderService = rootFolderService;
            _checkIfAuthorShouldBeRefreshed = checkIfAuthorShouldBeRefreshed;
            _monitorNewBookService = monitorNewBookService;
            _configService = configService;
            _importListExclusionService = importListExclusionService;
            _logger = logger;
        }

        private Author GetSkyhookData(string foreignId, bool light = false)
        {
            try
            {
                // light=true: volume STRUCTURE only (fast, used on initial add). A background full
                // refresh (queued in RefreshSelectedAuthors) fills per-volume details + covers after.
                return _authorInfo.GetAuthorInfo(foreignId, true, resolveVolumeDetails: !light);
            }
            catch (AuthorNotFoundException)
            {
                _logger.Error($"Could not find author with id {foreignId}");
            }

            return null;
        }

        protected override RemoteData GetRemoteData(Author local, List<Author> remote, Author data)
        {
            var result = new RemoteData();

            if (data != null)
            {
                result.Entity = data;
                result.Metadata = new List<AuthorMetadata> { data.Metadata.Value };
            }

            return result;
        }

        protected override bool ShouldDelete(Author local)
        {
            return !_mediaFileService.GetFilesByAuthor(local.Id).Any();
        }

        protected override void LogProgress(Author local)
        {
            _logger.ProgressInfo("Updating Info for {0}", local.Name);
        }

        protected override bool IsMerge(Author local, Author remote)
        {
            _logger.Trace($"local: {local.AuthorMetadataId} remote: {remote.Metadata.Value.Id}");
            return local.AuthorMetadataId != remote.Metadata.Value.Id;
        }

        protected override UpdateResult UpdateEntity(Author local, Author remote)
        {
            var result = UpdateResult.None;

            if (!local.Metadata.Value.Equals(remote.Metadata.Value))
            {
                result = UpdateResult.UpdateTags;
            }

            var existingCleanName = local.CleanName;

            local.UseMetadataFrom(remote);

            // IX_Authors_CleanName is UNIQUE (migration 046): if a remote rename lands on a
            // CleanName another series already holds, saving would throw on every refresh and
            // this author would become permanently un-refreshable. Keep the stored CleanName
            // instead and warn.
            if (local.CleanName != existingCleanName)
            {
                var collision = _authorService.FindByCleanNameExact(local.CleanName);

                if (collision != null && collision.Id != local.Id)
                {
                    _logger.Warn("Refresh wants to rename CleanName of {0} from '{1}' to '{2}', but that name is already used by {3}; keeping '{1}'",
                        local, existingCleanName, local.CleanName, collision);
                    local.CleanName = existingCleanName;
                }
            }

            local.Metadata = remote.Metadata;
            local.Series = remote.Series.Value;
            local.LastInfoSync = DateTime.UtcNow;

            try
            {
                local.Path = new DirectoryInfo(local.Path).FullName;
                local.Path = local.Path.GetActualCasing();
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Couldn't update author path for " + local.Path);
            }

            return result;
        }

        protected override UpdateResult MoveEntity(Author local, Author remote)
        {
            _logger.Debug($"Updating foreign id for {local} to {remote}");

            // We are moving from one metadata to another (will already have been poplated)
            local.AuthorMetadataId = remote.Metadata.Value.Id;
            local.Metadata = remote.Metadata.Value;

            // Update list exclusion if one exists
            var importExclusion = _importListExclusionService.FindByForeignId(local.Metadata.Value.ForeignAuthorId);

            if (importExclusion != null)
            {
                importExclusion.ForeignId = remote.Metadata.Value.ForeignAuthorId;
                _importListExclusionService.Update(importExclusion);
            }

            // Do the standard update
            UpdateEntity(local, remote);

            // We know we need to update tags as author id has changed
            return UpdateResult.UpdateTags;
        }

        protected override UpdateResult MergeEntity(Author local, Author target, Author remote)
        {
            _logger.Warn($"Author {local} was replaced with {remote} because the original was a duplicate.");

            // Update list exclusion if one exists
            var importExclusionLocal = _importListExclusionService.FindByForeignId(local.Metadata.Value.ForeignAuthorId);

            if (importExclusionLocal != null)
            {
                var importExclusionTarget = _importListExclusionService.FindByForeignId(target.Metadata.Value.ForeignAuthorId);
                if (importExclusionTarget == null)
                {
                    importExclusionLocal.ForeignId = remote.Metadata.Value.ForeignAuthorId;
                    _importListExclusionService.Update(importExclusionLocal);
                }
            }

            // move any books over to the new author and remove the local author
            var books = _bookService.GetBooksByAuthor(local.Id);
            books.ForEach(x => x.AuthorMetadataId = target.AuthorMetadataId);
            _bookService.UpdateMany(books);
            _authorService.DeleteAuthor(local.Id, false);

            // Update history entries to new id
            var items = _historyService.GetByAuthor(local.Id, null);
            items.ForEach(x => x.AuthorId = target.Id);
            _historyService.UpdateMany(items);

            // We know we need to update tags as author id has changed
            return UpdateResult.UpdateTags;
        }

        protected override Author GetEntityByForeignId(Author local)
        {
            return _authorService.FindById(local.ForeignAuthorId);
        }

        protected override void SaveEntity(Author local)
        {
            try
            {
                _authorService.UpdateAuthor(local);
            }
            catch (DbException ex) when (ex.Message.Contains("CleanName"))
            {
                // The pre-check in UpdateEntity raced another writer. Fall back to the stored
                // CleanName so this refresh still lands instead of failing forever.
                var stored = _authorService.GetAuthor(local.Id);
                _logger.Warn("CleanName '{0}' for {1} collided on save (unique index); keeping stored '{2}'",
                    local.CleanName, local, stored.CleanName);
                local.CleanName = stored.CleanName;
                _authorService.UpdateAuthor(local);
            }
        }

        protected override void DeleteEntity(Author local, bool deleteFiles)
        {
            _authorService.DeleteAuthor(local.Id, deleteFiles);
        }

        protected override List<Book> GetRemoteChildren(Author local, Author remote)
        {
            var filtered = _metadataProfileService.FilterBooks(remote, local.MetadataProfileId);

            var all = filtered.DistinctBy(m => m.ForeignBookId).ToList();
            var ids = all.Select(x => x.ForeignBookId).ToList();
            var excluded = _importListExclusionService.FindByForeignId(ids).Select(x => x.ForeignId).ToList();
            return all.Where(x => !excluded.Contains(x.ForeignBookId)).ToList();
        }

        protected override List<Book> GetLocalChildren(Author entity, List<Book> remoteChildren)
        {
            return _bookService.GetBooksForRefresh(entity.AuthorMetadataId,
                                                     remoteChildren.Select(x => x.ForeignBookId).ToList());
        }

        protected override Tuple<Book, List<Book>> GetMatchingExistingChildren(List<Book> existingChildren, Book remote)
        {
            var existingChild = existingChildren.SingleOrDefault(x => x.ForeignBookId == remote.ForeignBookId);
            var mergeChildren = new List<Book>();
            return Tuple.Create(existingChild, mergeChildren);
        }

        protected override void PrepareNewChild(Book child, Author entity)
        {
            child.Author = entity;
            child.AuthorMetadata = entity.Metadata.Value;
            child.AuthorMetadataId = entity.Metadata.Value.Id;
            child.Added = DateTime.UtcNow;
            child.LastInfoSync = DateTime.MinValue;
            child.Monitored = entity.Monitored;
        }

        protected override void PrepareExistingChild(Book local, Book remote, Author entity)
        {
            local.Author = entity;
            local.AuthorMetadata = entity.Metadata.Value;
            local.AuthorMetadataId = entity.Metadata.Value.Id;

            remote.UseDbFieldsFrom(local);
        }

        protected override void ProcessChildren(Author entity, SortedChildren children)
        {
            foreach (var book in children.Added)
            {
                book.Monitored = _monitorNewBookService.ShouldMonitorNewBook(book, children.UpToDate, entity.MonitorNewItems);
            }
        }

        protected override void AddChildren(List<Book> children)
        {
            _bookService.InsertMany(children);
        }

        protected override bool RefreshChildren(SortedChildren localChildren, List<Book> remoteChildren, Author remoteData, bool forceChildRefresh, bool forceUpdateFileTags, DateTime? lastUpdate)
        {
            return _refreshBookService.RefreshBookInfo(localChildren.All, remoteChildren, remoteData, forceChildRefresh, forceUpdateFileTags, lastUpdate);
        }

        protected override void PublishEntityUpdatedEvent(Author entity)
        {
            _eventAggregator.PublishEvent(new AuthorUpdatedEvent(entity));
        }

        protected override void PublishRefreshCompleteEvent(Author entity)
        {
            MarkAudioAvailableFromAudible(entity);

            // little hack - trigger the series update here
            _refreshSeriesService.RefreshSeriesInfo(entity.AuthorMetadataId, entity.Series, entity, false, false, null);
            _eventAggregator.PublishEvent(new AuthorRefreshCompleteEvent(entity));
        }

        // Audible lists the series (an Audio edition now carries an ASIN): its audiobooks exist, so
        // the entry leaves "not yet" here rather than at the first import -- Rascal Does Not Dream sat
        // pending with 16 Yen Audio titles live (2026-09-21). Runs after the base class's final
        // SaveEntity, on that same entity (an earlier version wrote the DB mid-refresh and the final
        // save put the stale false back). The import path still flips it for a series Audible does
        // not carry; nothing ever flips it back, and a failure here never fails the refresh.
        private void MarkAudioAvailableFromAudible(Author entity)
        {
            if (entity.Library != LibraryType.LightNovel || entity.AudioAvailable)
            {
                return;
            }

            try
            {
                var listed = _bookService.GetBooksByAuthor(entity.Id)
                    .Any(b => b.Editions?.Value?.Any(e => e.MediaType == MediaType.Audio && e.Asin.IsNotNullOrWhiteSpace()) == true);

                if (!listed)
                {
                    return;
                }

                entity.AudioAvailable = true;
                _authorService.UpdateAuthor(entity);
                _logger.Info("{0}: Audible lists its audiobooks; the audio editions are no longer pending", entity.Name);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not mark audio available for {0}", entity.Name);
            }
        }

        protected override void PublishChildrenUpdatedEvent(Author entity, List<Book> newChildren, List<Book> updateChildren, List<Book> deleteChildren)
        {
            _eventAggregator.PublishEvent(new BookInfoRefreshedEvent(entity, newChildren, updateChildren, deleteChildren));
        }

        private void Rescan(List<int> authorIds, bool isNew, CommandTrigger trigger, bool infoUpdated)
        {
            var rescanAfterRefresh = _configService.RescanAfterRefresh;
            var shouldRescan = true;

            if (isNew)
            {
                _logger.Trace("Forcing rescan. Reason: New author added");
                shouldRescan = true;
            }
            else if (rescanAfterRefresh == RescanAfterRefreshType.Never)
            {
                _logger.Trace("Skipping rescan. Reason: never rescan after refresh");
                shouldRescan = false;
            }
            else if (rescanAfterRefresh == RescanAfterRefreshType.AfterManual && trigger != CommandTrigger.Manual)
            {
                _logger.Trace("Skipping rescan. Reason: not after automatic refreshes");
                shouldRescan = false;
            }
            else if (!infoUpdated)
            {
                _logger.Trace("Skipping rescan. Reason: no metadata updated");
                shouldRescan = false;
            }

            if (shouldRescan)
            {
                // Rescan only the refreshed authors' own folders, not every root folder. A full-root
                // rescan re-parses every file across un-added series and (addNewAuthors is false) does a
                // wasted per-file remote search for each — a storm on every refresh/add. Scoping to the
                // authors' paths keeps adoption working without it.
                var folders = _authorService.GetAuthors(authorIds)
                    .Select(x => x.Path)
                    .Where(p => p.IsNotNullOrWhiteSpace())
                    .ToList();

                _commandQueueManager.Push(new RescanFoldersCommand(folders, FilterFilesType.Matched, false, authorIds));
            }
        }

        private void RefreshSelectedAuthors(List<int> authorIds, bool isNew, CommandTrigger trigger)
        {
            var updated = false;
            var authors = _authorService.GetAuthors(authorIds);

            foreach (var author in authors)
            {
                try
                {
                    // On initial add, resolve only the volume STRUCTURE so the series + file import
                    // complete in seconds; a background full refresh (queued below) then fills the
                    // per-volume covers/dates/descriptions — Sonarr/Radarr-style instant add.
                    var data = GetSkyhookData(author.ForeignAuthorId, light: isNew);
                    updated |= RefreshEntityInfo(author, null, data, true, false, null);
                }
                catch (EditionUnavailableException e)
                {
                    // Preferred Edition (2026-09-24, ruling S10): the bound edition is gone from the
                    // catalogue -- a Warn, and the entry stays exactly as stored (no update, no delete).
                    _logger.Warn("Couldn't refresh info for {0}: {1}; keeping it as stored", author, e.Message);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't refresh info for {0}", author);
                }
            }

            Rescan(authorIds, isNew, trigger, updated);

            if (isNew)
            {
                // Background pass: full per-volume metadata + covers, off the user's critical path.
                // isNewAuthor=false so it does a normal full resolve (and won't re-queue itself).
                foreach (var id in authorIds)
                {
                    _commandQueueManager.Push(new RefreshAuthorCommand(id, false), CommandPriority.Low);
                }
            }
        }

        public void Execute(BulkRefreshAuthorCommand message)
        {
            RefreshSelectedAuthors(message.AuthorIds, message.AreNewAuthors, message.Trigger);
        }

        public void Execute(RefreshAuthorCommand message)
        {
            var trigger = message.Trigger;
            var isNew = message.IsNewAuthor;

            if (message.AuthorId.HasValue)
            {
                RefreshSelectedAuthors(new List<int> { message.AuthorId.Value }, isNew, trigger);
            }
            else
            {
                var updated = false;
                var authors = _authorService.GetAllAuthors().OrderBy(c => c.Name).ToList();
                var authorIds = authors.Select(x => x.Id).ToList();

                var updatedGoodreadsAuthors = new HashSet<string>();

                if (message.LastExecutionTime.HasValue && message.LastExecutionTime.Value.AddDays(14) > DateTime.UtcNow)
                {
                    updatedGoodreadsAuthors = _authorInfo.GetChangedAuthors(message.LastStartTime.Value);
                }

                foreach (var author in authors)
                {
                    var manualTrigger = message.Trigger == CommandTrigger.Manual;

                    // Always consult ShouldRefresh on automatic runs: the bookinfo "changed authors"
                    // feed is meaningless for manga (manga series aren't bookinfo authors), so gating
                    // refresh behind it would skip every series and the status-driven self-heal would
                    // never fire. ShouldRefresh's own cadence (Continuing ~2d, Ended ~30d) limits it.
                    if (manualTrigger ||
                        (updatedGoodreadsAuthors != null && updatedGoodreadsAuthors.Contains(author.ForeignAuthorId)) ||
                        _checkIfAuthorShouldBeRefreshed.ShouldRefresh(author))
                    {
                        try
                        {
                            LogProgress(author);
                            var data = GetSkyhookData(author.ForeignAuthorId);

                            // 2026-09-17: every book is refreshed on automatic passes too. The provider
                            // resolves the whole series in one call, so ShouldRefreshBook's per-book gate
                            // (meant to spare a metadata call per book) only discarded the volume data of
                            // settled volumes. Cost: one book-row write per book per pass (LastInfoSync);
                            // editions are untouched unless changed; and a metadata UpdateTags result now
                            // fans the tag sync out to every book of the author, not only the gated ones.
                            updated |= RefreshEntityInfo(author, null, data, true, false, message.LastStartTime);
                        }
                        catch (EditionUnavailableException e)
                        {
                            // Preferred Edition (2026-09-24, ruling S10): as in RefreshSelectedAuthors.
                            _logger.Warn("Couldn't refresh info for {0}: {1}; keeping it as stored", author, e.Message);
                        }
                        catch (Exception e)
                        {
                            _logger.Error(e, "Couldn't refresh info for {0}", author);
                        }
                    }
                    else
                    {
                        _logger.Info("Skipping refresh of author: {0}", author.Name);
                    }
                }

                Rescan(authorIds, isNew, trigger, updated);
            }
        }
    }
}
