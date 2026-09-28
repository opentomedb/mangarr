using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.IndexerSearch
{
    public interface ISearchForReleases
    {
        // Every monitored media type of the book / series, merged. A manga volume has one
        // monitored Archive edition, so this is the one search it always was.
        Task<List<DownloadDecision>> BookSearch(int bookId, bool missingOnly, bool userInvokedSearch, bool interactiveSearch);
        Task<List<DownloadDecision>> AuthorSearch(int authorId, bool missingOnly, bool userInvokedSearch, bool interactiveSearch);

        // Light novels (2026-09): one edition class only (the scheduled search asks per class).
        // stampLastSearch (final review I1, 2026-09-20): false leaves Book.LastSearchTime alone --
        // only the scheduled cutoff-unmet run passes false, so it cannot burn the missing search's
        // per-book back-off for an edition it never wanted.
        Task<List<DownloadDecision>> BookSearch(int bookId, MediaType mediaType, bool missingOnly, bool userInvokedSearch, bool interactiveSearch, bool stampLastSearch);
        Task<List<DownloadDecision>> AuthorSearch(int authorId, MediaType mediaType, bool missingOnly, bool userInvokedSearch, bool interactiveSearch, bool stampLastSearch);
    }

    public class ReleaseSearchService : ISearchForReleases
    {
        private readonly IIndexerFactory _indexerFactory;
        private readonly IBookService _bookService;
        private readonly IAuthorService _authorService;
        private readonly IMakeDownloadDecision _makeDownloadDecision;
        private readonly Logger _logger;

        public ReleaseSearchService(IIndexerFactory indexerFactory,
                                IBookService bookService,
                                IAuthorService authorService,
                                IMakeDownloadDecision makeDownloadDecision,
                                Logger logger)
        {
            _indexerFactory = indexerFactory;
            _bookService = bookService;
            _authorService = authorService;
            _makeDownloadDecision = makeDownloadDecision;
            _logger = logger;
        }

        public async Task<List<DownloadDecision>> BookSearch(int bookId, bool missingOnly, bool userInvokedSearch, bool interactiveSearch)
        {
            var book = _bookService.GetBook(bookId);

            var decisions = await BookSearch(book, missingOnly, userInvokedSearch, interactiveSearch);

            return DeDupeDecisions(decisions);
        }

        public async Task<List<DownloadDecision>> BookSearch(int bookId, MediaType mediaType, bool missingOnly, bool userInvokedSearch, bool interactiveSearch, bool stampLastSearch)
        {
            var book = _bookService.GetBook(bookId);

            var decisions = await BookSearch(book, mediaType, missingOnly, userInvokedSearch, interactiveSearch, stampLastSearch);

            return DeDupeDecisions(decisions);
        }

        public async Task<List<DownloadDecision>> AuthorSearch(int authorId, bool missingOnly, bool userInvokedSearch, bool interactiveSearch)
        {
            var author = _authorService.GetAuthor(authorId);

            var decisions = await AuthorSearch(author, missingOnly, userInvokedSearch, interactiveSearch);

            return DeDupeDecisions(decisions);
        }

        public async Task<List<DownloadDecision>> AuthorSearch(int authorId, MediaType mediaType, bool missingOnly, bool userInvokedSearch, bool interactiveSearch, bool stampLastSearch)
        {
            var author = _authorService.GetAuthor(authorId);

            var decisions = await AuthorSearch(author, mediaType, missingOnly, userInvokedSearch, interactiveSearch, stampLastSearch);

            return DeDupeDecisions(decisions);
        }

        public async Task<List<DownloadDecision>> AuthorSearch(Author author, bool missingOnly, bool userInvokedSearch, bool interactiveSearch)
        {
            var books = _bookService.GetBooksByAuthor(author.Id).Where(a => a.Monitored).ToList();
            var decisions = new List<DownloadDecision>();

            // A manga series has one Archive edition per volume; enumerating its editions here would
            // lazy-load one query per volume where this path never read any.
            var mediaTypes = author.Library == LibraryType.LightNovel
                ? MonitoredMediaTypes(books)
                : new List<MediaType> { MediaType.Archive };

            foreach (var mediaType in mediaTypes)
            {
                decisions.AddRange(await AuthorSearch(author, books, mediaType, userInvokedSearch, interactiveSearch, true));
            }

            return decisions;
        }

        public async Task<List<DownloadDecision>> AuthorSearch(Author author, MediaType mediaType, bool missingOnly, bool userInvokedSearch, bool interactiveSearch, bool stampLastSearch = true)
        {
            var books = _bookService.GetBooksByAuthor(author.Id).Where(a => a.Monitored).ToList();

            return await AuthorSearch(author, books, mediaType, userInvokedSearch, interactiveSearch, stampLastSearch);
        }

        private async Task<List<DownloadDecision>> AuthorSearch(Author author, List<Book> books, MediaType mediaType, bool userInvokedSearch, bool interactiveSearch, bool stampLastSearch)
        {
            // Copy-in hold (2026-09-16, D4a): an entry whose owned files are still coming in is not
            // searched automatically; an interactive search (a person at the table) still runs.
            if (author.CopyInPending && !interactiveSearch)
            {
                _logger.Info("Search for \"{0}\" skipped: copy-in pending", author.Name);
                return new List<DownloadDecision>();
            }

            var searchSpec = Get<AuthorSearchCriteria>(author, userInvokedSearch, interactiveSearch);

            // Light novels (2026-09): the volumes whose edition of THIS class is monitored. A manga
            // volume's one Archive edition is monitored whenever the volume is (minted monitored,
            // Part A keeps at most one per class), so the manga list is Book.Monitored, as before --
            // taken as it stands, because Book.Editions is a lazy load (one query per volume) and a
            // manga series search never read editions.
            searchSpec.Books = author.Library == LibraryType.LightNovel
                ? books.Where(b => b.EditionOf(mediaType)?.Monitored == true).ToList()
                : books;
            searchSpec.MediaType = mediaType;
            searchSpec.StampLastSearch = stampLastSearch;

            return await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec);
        }

        public async Task<List<DownloadDecision>> BookSearch(Book book, bool missingOnly, bool userInvokedSearch, bool interactiveSearch)
        {
            var decisions = new List<DownloadDecision>();

            foreach (var mediaType in MonitoredMediaTypes(new List<Book> { book }))
            {
                decisions.AddRange(await BookSearch(book, mediaType, missingOnly, userInvokedSearch, interactiveSearch, true));
            }

            return decisions;
        }

        public async Task<List<DownloadDecision>> BookSearch(Book book, MediaType mediaType, bool missingOnly, bool userInvokedSearch, bool interactiveSearch, bool stampLastSearch = true)
        {
            var author = _authorService.GetAuthor(book.AuthorId);

            // Copy-in hold (D4a), as AuthorSearch.
            if (author.CopyInPending && !interactiveSearch)
            {
                _logger.Info("Search for \"{0}\" skipped: copy-in pending", author.Name);
                return new List<DownloadDecision>();
            }

            var edition = book.EditionOf(mediaType);

            // Covered volumes (2026-09-17, D4): the audiobook is inside another volume's file, so
            // no automatic audio search; a person at the interactive table still gets one.
            if (mediaType == MediaType.Audio && edition?.CoveredByVolume != null && !interactiveSearch)
            {
                _logger.Info("Search for \"{0}\" audio skipped: covered by Vol. {1}", book.Title, edition.CoveredByVolume);
                return new List<DownloadDecision>();
            }

            var searchSpec = Get<BookSearchCriteria>(author, new List<Book> { book }, userInvokedSearch, interactiveSearch);

            // Light novels (2026-09): the searched edition and its class ride on the criteria (the
            // categories split and Unknown-quality releases follow them); the title is the
            // volume's display edition -- both editions of a light-novel volume carry the same
            // title, and a manga volume has only the one.
            searchSpec.MediaType = mediaType;
            searchSpec.StampLastSearch = stampLastSearch;
            searchSpec.Edition = edition;
            searchSpec.BookTitle = book.PrimaryEdition().Title;
            searchSpec.VolumeNumber = book.VolumeNumber;

            // Light-novel audio (2026-09-18, D4): the subtitle tier's sources ride on the Audio
            // search only -- `edition` is the Audio edition here.
            if (mediaType == MediaType.Audio)
            {
                searchSpec.Subtitle = book.Subtitle;
                searchSpec.AudiobookTitle = edition?.AudiobookTitle;
            }

            // searchSpec.BookIsbn = book.Isbn13;
            if (book.ReleaseDate.HasValue)
            {
                searchSpec.BookYear = book.ReleaseDate.Value.Year;
            }

            return await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec);
        }

        // The classes a search without an explicit media type covers: one per monitored edition
        // class across the books, Archive when no editions are loaded (unrefreshed rows).
        private static List<MediaType> MonitoredMediaTypes(List<Book> books)
        {
            var types = books.SelectMany(b => b.Editions?.Value ?? new List<Edition>())
                             .Where(e => e.Monitored)
                             .Select(e => e.MediaType)
                             .Distinct()
                             .OrderBy(t => t)
                             .ToList();

            return types.Any() ? types : new List<MediaType> { MediaType.Archive };
        }

        private TSpec Get<TSpec>(Author author, List<Book> books, bool userInvokedSearch, bool interactiveSearch)
            where TSpec : SearchCriteriaBase, new()
        {
            var spec = new TSpec();

            spec.Books = books;
            spec.Author = author;
            spec.UserInvokedSearch = userInvokedSearch;
            spec.InteractiveSearch = interactiveSearch;

            return spec;
        }

        private static TSpec Get<TSpec>(Author author, bool userInvokedSearch, bool interactiveSearch)
            where TSpec : SearchCriteriaBase, new()
        {
            var spec = new TSpec();
            spec.Author = author;
            spec.UserInvokedSearch = userInvokedSearch;
            spec.InteractiveSearch = interactiveSearch;

            return spec;
        }

        private async Task<List<DownloadDecision>> Dispatch(Func<IIndexer, Task<IList<ReleaseInfo>>> searchAction, SearchCriteriaBase criteriaBase)
        {
            var indexers = criteriaBase.InteractiveSearch ?
                _indexerFactory.InteractiveSearchEnabled() :
                _indexerFactory.AutomaticSearchEnabled();

            // Filter indexers to untagged indexers and indexers with intersecting tags
            indexers = indexers.Where(i => i.Definition.Tags.Empty() || i.Definition.Tags.Intersect(criteriaBase.Author.Tags).Any()).ToList();

            _logger.ProgressInfo("Searching indexers for {0}. {1} active indexers", criteriaBase, indexers.Count);

            var tasks = indexers.Select(indexer => DispatchIndexer(searchAction, indexer, criteriaBase));

            var batch = await Task.WhenAll(tasks);

            var reports = batch.SelectMany(x => x).ToList();

            _logger.ProgressDebug("Total of {0} reports were found for {1} from {2} indexers", reports.Count, criteriaBase, indexers.Count);

            // Update the last search time for all albums if at least 1 indexer was searched -- unless
            // this search does not stamp (final review I1: the scheduled cutoff-unmet run).
            if (indexers.Any() && criteriaBase.StampLastSearch)
            {
                var lastSearchTime = DateTime.UtcNow;
                _logger.Debug("Setting last search time to: {0}", lastSearchTime);

                criteriaBase.Books.ForEach(a => a.LastSearchTime = lastSearchTime);
                _bookService.UpdateLastSearchTime(criteriaBase.Books);
            }

            return _makeDownloadDecision.GetSearchDecision(reports, criteriaBase).ToList();
        }

        private async Task<IList<ReleaseInfo>> DispatchIndexer(Func<IIndexer, Task<IList<ReleaseInfo>>> searchAction, IIndexer indexer, SearchCriteriaBase criteriaBase)
        {
            try
            {
                return await searchAction(indexer);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while searching for {0}", criteriaBase);
            }

            return Array.Empty<ReleaseInfo>();
        }

        private List<DownloadDecision> DeDupeDecisions(List<DownloadDecision> decisions)
        {
            // De-dupe reports by guid so duplicate results aren't returned. Pick the one with the least rejections and higher indexer priority.
            return decisions.GroupBy(d => d.RemoteBook.Release.Guid)
                .Select(d => d.OrderBy(v => v.Rejections.Count()).ThenBy(v => v.RemoteBook?.Release?.IndexerPriority ?? IndexerDefinition.DefaultPriority).First())
                .ToList();
        }
    }
}
