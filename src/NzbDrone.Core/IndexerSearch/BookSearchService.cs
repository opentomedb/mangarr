using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Queue;

namespace NzbDrone.Core.IndexerSearch
{
    internal class BookSearchService : IExecute<BookSearchCommand>,
                               IExecute<MissingBookSearchCommand>,
                               IExecute<CutoffUnmetBookSearchCommand>
    {
        // Best-effort audio (D5): a light novel whose audio has not arrived yet is probed this often.
        private static readonly TimeSpan AudioProbeInterval = TimeSpan.FromDays(7);

        private readonly ISearchForReleases _releaseSearchService;
        private readonly IBookService _bookService;
        private readonly IAuthorService _authorService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IBookCutoffService _bookCutoffService;
        private readonly IQueueService _queueService;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly Logger _logger;

        public BookSearchService(ISearchForReleases releaseSearchService,
            IBookService bookService,
            IAuthorService authorService,
            IEditionService editionService,
            IMediaFileService mediaFileService,
            IBookCutoffService bookCutoffService,
            IQueueService queueService,
            IProcessDownloadDecisions processDownloadDecisions,
            Logger logger)
        {
            _releaseSearchService = releaseSearchService;
            _bookService = bookService;
            _authorService = authorService;
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _bookCutoffService = bookCutoffService;
            _queueService = queueService;
            _processDownloadDecisions = processDownloadDecisions;
            _logger = logger;
        }

        // Light novels (2026-09): the unit of a scheduled search is (volume, media type) -- the
        // EPUB edition and the Audio edition of one volume are searched separately, each in its
        // own categories and profile. A manga volume has one Archive edition, so its unit is the
        // volume, exactly as before.
        // stampLastSearch (final review I1, 2026-09-20): the SCHEDULED cutoff-unmet run passes
        // false. Book.LastSearchTime is per volume but the two scheduled searches want different
        // EDITIONS of it, so a cutoff run stamping a volume that also has a fileless edition would
        // make the missing search's 24h back-off skip that edition once the volume ages out.
        private async Task SearchForBulkBooks(List<(Book Book, MediaType MediaType)> wanted, bool userInvokedSearch, bool stampLastSearch = true)
        {
            // Manga: one series search covers every wanted volume of that series at once (single
            // volumes and packs alike), where the per-volume search costs a multi-variant fan-out
            // across every indexer PER volume — 13 missing volumes of one series were 13 rounds of
            // the same query, which is what tripped the private-tracker rate limits and starved the
            // volumes searched later in the run. A series with a single wanted volume keeps the
            // precise per-volume search.
            var bySeries = wanted
                .GroupBy(w => (w.Book.AuthorId, w.MediaType))
                .OrderBy(g => g.Min(w => w.Book.LastSearchTime ?? DateTime.MinValue))
                .ToList();

            _logger.ProgressInfo("Performing missing search for {0} volume editions across {1} series searches", wanted.Count, bySeries.Count);
            var downloadedCount = 0;

            foreach (var series in bySeries)
            {
                var seriesBooks = series.Select(w => w.Book).ToList();
                var mediaType = series.Key.MediaType;
                List<DownloadDecision> decisions;

                try
                {
                    decisions = seriesBooks.Count > 1
                        ? await _releaseSearchService.AuthorSearch(series.Key.AuthorId, mediaType, true, userInvokedSearch, false, stampLastSearch)
                        : await _releaseSearchService.BookSearch(seriesBooks[0].Id, mediaType, false, userInvokedSearch, false, stampLastSearch);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to search for {0}", seriesBooks.Count > 1 ? $"{seriesBooks.Count} {mediaType} editions of author {series.Key.AuthorId}" : $"book: [{seriesBooks[0]}] ({mediaType})");
                    continue;
                }

                var processed = await _processDownloadDecisions.ProcessDecisions(decisions);

                downloadedCount += processed.Grabbed.Count;
            }

            _logger.ProgressInfo("Completed search for {0} volume editions. {1} reports downloaded.", wanted.Count, downloadedCount);
        }

        public void Execute(BookSearchCommand message)
        {
            var manual = message.Trigger == CommandTrigger.Manual;

            // Light novels (2026-09-22): several volumes of ONE series in one command (Wanted →
            // Search Selected, a failed batch) searched volume by volume put a multi-variant query
            // on every indexer per volume -- six Classroom of the Elite volumes exhausted a private tracker.
            // Each (series, media type) with two or more of the command's volumes monitored in that
            // class is one series search instead; the rest keep the per-volume search.
            var covered = SearchLightNovelSeries(message, manual);

            foreach (var bookId in message.BookIds.Distinct())
            {
                if (covered.TryGetValue(bookId, out var coveredTypes))
                {
                    var book = _bookService.GetBook(bookId);
                    var remaining = (message.MediaType.HasValue ? new[] { message.MediaType.Value } : new[] { MediaType.Ebook, MediaType.Audio })
                        .Where(t => !coveredTypes.Contains(t) && book.EditionOf(t)?.Monitored == true)
                        .ToList();

                    foreach (var type in remaining)
                    {
                        var typed = _releaseSearchService.BookSearch(bookId, type, false, manual, false, true).GetAwaiter().GetResult();
                        _processDownloadDecisions.ProcessDecisions(typed).GetAwaiter().GetResult();
                    }

                    continue;
                }

                // A typed command (a light-novel re-search after a failed grab) searches that one
                // class; an untyped one is every monitored class -- the one Archive search for manga.
                var decisions = message.MediaType.HasValue
                    ? _releaseSearchService.BookSearch(bookId, message.MediaType.Value, false, manual, false, true).GetAwaiter().GetResult()
                    : _releaseSearchService.BookSearch(bookId, false, manual, false).GetAwaiter().GetResult();
                var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();

                _logger.ProgressInfo("Volume search completed. {0} reports downloaded.", processed.Grabbed.Count);
            }
        }

        // Returns the (book → media types) the series searches covered.
        private Dictionary<int, HashSet<MediaType>> SearchLightNovelSeries(BookSearchCommand message, bool manual)
        {
            var covered = new Dictionary<int, HashSet<MediaType>>();
            var ids = message.BookIds.Distinct().ToList();

            if (ids.Count < 2)
            {
                return covered;
            }

            var types = message.MediaType.HasValue ? new[] { message.MediaType.Value } : new[] { MediaType.Ebook, MediaType.Audio };
            var series = _bookService.GetBooks(ids)
                .Where(b => b.Author.Value.Library == LibraryType.LightNovel)
                .GroupBy(b => b.AuthorId);

            foreach (var group in series)
            {
                foreach (var type in types)
                {
                    var volumes = group.Where(b => b.EditionOf(type)?.Monitored == true).ToList();

                    if (volumes.Count < 2)
                    {
                        continue;
                    }

                    // Server messages (2026-09-26): one literal template per media type, so the sidebar never
                    // shows the English enum name inside a translated line. Same English as "One {type} ...".
                    if (type == MediaType.Audio)
                    {
                        _logger.ProgressInfo("One Audio series search for {0} volumes of {1}", volumes.Count, group.First().Author.Value.Name);
                    }
                    else if (type == MediaType.Ebook)
                    {
                        _logger.ProgressInfo("One Ebook series search for {0} volumes of {1}", volumes.Count, group.First().Author.Value.Name);
                    }
                    else
                    {
                        _logger.ProgressInfo("One Archive series search for {0} volumes of {1}", volumes.Count, group.First().Author.Value.Name);
                    }

                    var decisions = _releaseSearchService.AuthorSearch(group.Key, type, false, manual, false, true).GetAwaiter().GetResult();
                    var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();
                    _logger.ProgressInfo("Series search completed. {0} reports downloaded.", processed.Grabbed.Count);

                    foreach (var volume in volumes)
                    {
                        if (!covered.TryGetValue(volume.Id, out var set))
                        {
                            covered[volume.Id] = set = new HashSet<MediaType>();
                        }

                        set.Add(type);
                    }
                }
            }

            return covered;
        }

        public void Execute(MissingBookSearchCommand message)
        {
            List<Book> books;

            if (message.AuthorId.HasValue)
            {
                var authorId = message.AuthorId.Value;

                var pagingSpec = new PagingSpec<Book>
                {
                    Page = 1,
                    PageSize = 100000,
                    SortDirection = SortDirection.Ascending,
                    SortKey = "Id"
                };

                pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Author.Value.Monitored == true);

                books = _bookService.BooksWithoutFiles(pagingSpec).Records.Where(e => e.AuthorId.Equals(authorId)).ToList();
            }
            else
            {
                var pagingSpec = new PagingSpec<Book>
                {
                    Page = 1,
                    PageSize = 100000,
                    SortDirection = SortDirection.Ascending,
                    SortKey = "Id"
                };

                pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Author.Value.Monitored == true);

                books = _bookService.BooksWithoutFiles(pagingSpec).Records.ToList();
            }

            var manual = message.Trigger == CommandTrigger.Manual;
            var missing = books;

            // Scheduled runs back off perpetual misses: recently released volumes get every
            // run (new uploads appear for weeks after release), older never-found ones only
            // re-search once per 24h instead of every 6h — the full multi-variant fan-out
            // across all indexers for the same stale misses is what tripped private-tracker rate
            // limits. Manual searches are never gated.
            if (!manual)
            {
                var now = DateTime.UtcNow;
                missing = missing.Where(b =>
                    (b.ReleaseDate.HasValue && b.ReleaseDate.Value > now.AddDays(-60)) ||
                    !b.LastSearchTime.HasValue ||
                    b.LastSearchTime.Value < now.AddHours(-24)).ToList();
            }

            // Wanted edition filter (2026-09-21): Search All on a page filtered to one edition
            // searches that class only. The weekly probe is itself an audio search AND it stamps
            // Author.LastAudioSearch, so a run scoped to another class must not run it at all --
            // filtering its results away afterwards would burn the stamp for a search never made.
            // Search All scope (2026-09-21): the probe is light novels by definition, so a run
            // scoped to manga must not make it either, for that same stamp.
            var probe = (!message.Library.HasValue || message.Library == LibraryType.LightNovel) &&
                        (!message.MediaType.HasValue || message.MediaType == MediaType.Audio)
                ? PendingAudioProbe(message.AuthorId, manual)
                : new List<(Book Book, MediaType MediaType)>();

            // Light novels (2026-09): a missing volume is searched once per wanted edition -- every
            // monitored edition without a file whose class is not pending -- plus the weekly probe of
            // pending audio; a queued (volume, media type) is skipped, not the whole volume. Manga:
            // one Archive edition per volume, the same list as before.
            var queued = QueuedEditions();
            var wanted = BothEditionsOnly(WantedEditions(missing, message.Library, (author, edition, files) => files.Empty()), message.BothEditions)
                .Concat(probe)
                .Where(w => !message.MediaType.HasValue || w.MediaType == message.MediaType.Value)
                .Where(w => !queued.Contains((w.Book.Id, w.MediaType)))
                .ToList();

            SearchForBulkBooks(wanted, manual).GetAwaiter().GetResult();
        }

        public void Execute(CutoffUnmetBookSearchCommand message)
        {
            var pagingSpec = new PagingSpec<Book>
            {
                Page = 1,
                PageSize = 100000,
                SortDirection = SortDirection.Ascending,
                SortKey = "Id"
            };

            pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Author.Value.Monitored == true);

            var books = _bookCutoffService.BooksWhereCutoffUnmet(pagingSpec).Records.ToList();

            var queued = QueuedEditions();
            var wanted = BothEditionsOnly(WantedEditions(books, message.Library, BelowCutoff), message.BothEditions)
                // Wanted edition filter (2026-09-21), as the missing search above.
                .Where(w => !message.MediaType.HasValue || w.MediaType == message.MediaType.Value)
                .Where(w => !queued.Contains((w.Book.Id, w.MediaType)))
                .ToList();

            var manual = message.Trigger == CommandTrigger.Manual;

            // Final review I1 (2026-09-20): a scheduled upgrade hunt must not stamp LastSearchTime --
            // the missing search reads that same stamp for the volume's OTHER edition. A manual run
            // stamps as every other search does: the user asked for this volume now.
            SearchForBulkBooks(wanted, manual, manual).GetAwaiter().GetResult();
        }

        // (volume, media type) pairs already in the queue: a downloading EPUB must not stop the
        // audiobook search of the same volume. Manga queue rows are Archive.
        private HashSet<(int BookId, MediaType MediaType)> QueuedEditions()
        {
            return _queueService.GetQueue()
                .Where(q => q.Book != null)
                .Select(q => (q.Book.Id, q.RemoteBook?.MediaType ?? MediaType.Archive))
                .ToHashSet();
        }

        // The (volume, media type) pairs to search. A manga volume has one Archive edition and the
        // query that produced the row already established it is wanted, so the row is scheduled as
        // it stands -- no edition or file lookup, the same reads as before. A light-novel volume is
        // judged per monitored edition, with the editions and files of its series read once per
        // series (Book.Editions / Edition.BookFiles are lazy: one query per row each). Pending audio
        // (author.AudioAvailable == false) is left to the weekly probe.
        private List<(Book Book, MediaType MediaType)> WantedEditions(List<Book> books, LibraryType? library, Func<Author, Edition, List<BookFile>, bool> isWanted)
        {
            var wanted = new List<(Book Book, MediaType MediaType)>();

            foreach (var series in books.GroupBy(b => b.AuthorId))
            {
                var author = series.First().Author.Value;

                // Search All scope (2026-09-21): a Wanted page filtered to one library searches
                // that library only. Judged here, where the series' author is already resolved --
                // a per-volume test would lazy-load one author per row.
                if (library.HasValue && author.Library != library.Value)
                {
                    continue;
                }

                // Copy-in hold (2026-09-16, D4b): nothing of a held entry is scheduled.
                if (author.CopyInPending)
                {
                    _logger.Debug("{0}: copy-in pending, volumes not scheduled", author.Name);
                    continue;
                }

                if (author.Library != LibraryType.LightNovel)
                {
                    wanted.AddRange(series.Select(b => (b, MediaType.Archive)));
                    continue;
                }

                var editions = _editionService.GetEditionsByAuthor(author.Id).Where(e => e.Monitored).ToLookup(e => e.BookId);
                var files = _mediaFileService.GetFilesByAuthor(author.Id).ToLookup(f => f.EditionId);

                foreach (var book in series)
                {
                    foreach (var edition in editions[book.Id])
                    {
                        if (edition.MediaType == MediaType.Audio && !author.AudioAvailable)
                        {
                            continue;
                        }

                        // Covered volumes (2026-09-17, D4): the audiobook is inside another volume's file.
                        if (edition.MediaType == MediaType.Audio && edition.CoveredByVolume.HasValue)
                        {
                            continue;
                        }

                        // Unreleased audio (2026-09-21): Audible lists it for later, or not yet -- nothing to find.
                        if (edition.MediaType == MediaType.Audio && files[edition.Id].Empty() && edition.IsUnreleasedAudio(DateTime.UtcNow))
                        {
                            continue;
                        }

                        if (isWanted(author, edition, files[edition.Id].ToList()))
                        {
                            wanted.Add((book, edition.MediaType));
                        }
                    }
                }
            }

            return wanted;
        }

        // The Both preset (2026-09-21): that page lists a volume only when its EPUB AND its
        // audiobook are both wanted, so its Search All searches those volumes and nothing else --
        // sending the library alone scheduled every wanted light-novel edition instead. Wanting
        // both is a property of the volume rather than of one edition, so it is judged over the
        // group, as the page's own HAVING clause is. Both implies light novels: a manga volume has
        // one Archive edition and never an Audio one, so it drops out without a library test. The
        // weekly audio probe is left out of the group on purpose -- it stamps Author.LastAudioSearch
        // when it is built, so discarding its rows here would burn a week of probes for a search
        // never made.
        private static IEnumerable<(Book Book, MediaType MediaType)> BothEditionsOnly(List<(Book Book, MediaType MediaType)> wanted, bool bothEditions)
        {
            if (!bothEditions)
            {
                return wanted;
            }

            return wanted
                .GroupBy(w => w.Book.Id)
                .Where(g => g.Any(w => w.MediaType == MediaType.Ebook) && g.Any(w => w.MediaType == MediaType.Audio))
                .SelectMany(g => g);
        }

        // Cutoff unmet, per edition: a file of this edition sits below the cutoff of the profile
        // of ITS media type (the audio profile for the Audio edition).
        private static bool BelowCutoff(Author author, Edition edition, List<BookFile> files)
        {
            var profile = author.QualityProfileFor(edition.MediaType);
            var cutoff = profile.GetIndex(profile.Cutoff);

            return files.Any(f => profile.GetIndex(f.Quality.Quality).CompareTo(cutoff) < 0);
        }

        // Best-effort audio (D5): a light novel whose audio has not arrived yet has PENDING audio
        // editions. They are left out of the normal list above and searched here once a week per
        // series -- one series search per probe -- then stamped on Author.LastAudioSearch. A manual
        // run probes every pending series regardless of the stamp: the user asked.
        private List<(Book Book, MediaType MediaType)> PendingAudioProbe(int? authorId, bool manual)
        {
            var now = DateTime.UtcNow;

            var authors = (authorId.HasValue ? new List<Author> { _authorService.GetAuthor(authorId.Value) } : _authorService.GetAllAuthors())
                // Copy-in hold (2026-09-16, D4b): a held entry is not probed, so its weekly stamp is not burnt.
                .Where(a => a != null && a.Monitored && a.Library == LibraryType.LightNovel && !a.AudioAvailable && !a.CopyInPending)
                .Where(a => manual || !a.LastAudioSearch.HasValue || a.LastAudioSearch.Value < now - AudioProbeInterval)
                .ToList();

            var wanted = new List<(Book Book, MediaType MediaType)>();

            foreach (var author in authors)
            {
                var audioEditions = _editionService.GetEditionsByAuthor(author.Id)
                    .Where(e => e.Monitored && e.MediaType == MediaType.Audio)
                    // Covered volumes (2026-09-17, D4): the audiobook is inside another volume's file.
                    .Where(e => !e.CoveredByVolume.HasValue)
                    .ToLookup(e => e.BookId);
                var files = _mediaFileService.GetFilesByAuthor(author.Id).ToLookup(f => f.EditionId);

                // released volumes only, as BooksWithoutFiles lists them (a volume with no date counts)
                wanted.AddRange(_bookService.GetBooksByAuthor(author.Id)
                    .Where(b => b.Monitored && (!b.ReleaseDate.HasValue || b.ReleaseDate.Value <= now))
                    .Where(b => audioEditions[b.Id].Any(e => files[e.Id].Empty()))
                    .Select(b => (b, MediaType.Audio)));

                author.LastAudioSearch = now;
            }

            if (authors.Any())
            {
                _logger.Debug("Weekly audio probe for {0} light novel(s) with no audio yet", authors.Count);
                _authorService.UpdateLastAudioSearch(authors);
            }

            return wanted;
        }
    }
}
