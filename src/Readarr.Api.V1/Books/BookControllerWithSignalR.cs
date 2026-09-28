using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.SignalR;
using Readarr.Api.V1.Author;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Books
{
    public abstract class BookControllerWithSignalR : RestControllerWithSignalR<BookResource, Book>
    {
        protected readonly IBookService _bookService;
        protected readonly ISeriesBookLinkService _seriesBookLinkService;
        protected readonly IAuthorStatisticsService _authorStatisticsService;
        protected readonly IUpgradableSpecification _qualityUpgradableSpecification;
        protected readonly IMapCoversToLocal _coverMapper;
        protected readonly IMediaFileService _mediaFileService;

        protected BookControllerWithSignalR(IBookService bookService,
                                        ISeriesBookLinkService seriesBookLinkService,
                                        IAuthorStatisticsService authorStatisticsService,
                                        IMapCoversToLocal coverMapper,
                                        IUpgradableSpecification qualityUpgradableSpecification,
                                        IMediaFileService mediaFileService,
                                        IBroadcastSignalRMessage signalRBroadcaster)
            : base(signalRBroadcaster)
        {
            _bookService = bookService;
            _seriesBookLinkService = seriesBookLinkService;
            _authorStatisticsService = authorStatisticsService;
            _coverMapper = coverMapper;
            _qualityUpgradableSpecification = qualityUpgradableSpecification;
            _mediaFileService = mediaFileService;
        }

        protected override BookResource GetResourceById(int id)
        {
            var book = _bookService.GetBook(id);
            var resource = MapToResource(book, true);
            return resource;
        }

        protected override BookResource GetResourceByIdForBroadcast(int id)
        {
            var book = _bookService.GetBook(id);
            var resource = MapToResource(book, false);
            return resource;
        }

        protected BookResource MapToResource(Book book, bool includeAuthor)
        {
            var resource = book.ToResource();
            var author = book.Author?.Value;

            if (includeAuthor && author != null)
            {
                resource.Author = author.ToResource();
            }

            FetchAndLinkBookStatistics(resource);
            LinkCutoff(resource, book, author);
            MapCoversToLocal(resource);

            return resource;
        }

        protected List<BookResource> MapToResource(List<Book> books, bool includeAuthor)
        {
            var seriesLinks = _seriesBookLinkService.GetLinksByBook(books.Select(x => x.Id).ToList())
                .GroupBy(x => x.BookId)
                .ToDictionary(x => x.Key, y => y.ToList());

            // mediaTypes[] needs each book's files (has-file / file count per edition): one query
            // for the page instead of one lazy load per book. A manga book gets its one edition's
            // files, as Book.BookFiles always did.
            var files = _mediaFileService.GetFilesByBooks(books.Select(x => x.Id).ToList())
                .GroupBy(f => f.Edition?.Value?.BookId ?? 0)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var book in books)
            {
                if (seriesLinks.TryGetValue(book.Id, out var links))
                {
                    book.SeriesLinks = links;
                }
                else
                {
                    book.SeriesLinks = new List<SeriesBookLink>();
                }

                book.BookFiles = files.GetValueOrDefault(book.Id) ?? new List<BookFile>();
            }

            var result = books.ToResource();

            var authorDict = new Dictionary<int, NzbDrone.Core.Books.Author>();
            for (var i = 0; i < books.Count; i++)
            {
                var book = books[i];
                var resource = result[i];
                var author = authorDict.GetValueOrDefault(book.AuthorMetadataId) ?? book.Author?.Value;

                if (author == null)
                {
                    continue;
                }

                authorDict[author.AuthorMetadataId] = author;

                if (includeAuthor)
                {
                    resource.Author = author.ToResource();
                }

                LinkCutoff(resource, book, author);
            }

            var authorStats = _authorStatisticsService.AuthorStatistics();
            LinkAuthorStatistics(result, authorStats);
            MapCoversToLocal(result.ToArray());

            return result;
        }

        // mediaTypes[].cutoffNotMet: a file is judged against the profile of its edition's media
        // type (Author.QualityProfileFor), the rule BookFileResource.qualityCutoffNotMet uses too.
        private void LinkCutoff(BookResource resource, Book book, NzbDrone.Core.Books.Author author)
        {
            if (author == null || resource.MediaTypes == null)
            {
                return;
            }

            var files = book.BookFiles?.Value ?? new List<BookFile>();

            foreach (var entry in resource.MediaTypes)
            {
                var edition = book.EditionOf(entry.MediaType);
                var profile = author.QualityProfileFor(entry.MediaType);

                if (edition == null || profile == null)
                {
                    continue;
                }

                // B3b (2026-09-18): a covered edition is satisfied by its carrier (B3a made it never
                // Wanted) — never cutoff-unmet in the badge either.
                entry.CutoffNotMet = !edition.CoveredByVolume.HasValue &&
                                     files.Where(f => f.EditionId == edition.Id)
                                          .Any(f => _qualityUpgradableSpecification.QualityCutoffNotMet(profile, f.Quality));
            }
        }

        private void FetchAndLinkBookStatistics(BookResource resource)
        {
            LinkAuthorStatistics(resource, _authorStatisticsService.AuthorStatistics(resource.AuthorId));
        }

        private void LinkAuthorStatistics(List<BookResource> resources, List<AuthorStatistics> authorStatistics)
        {
            var bookStatsDict = authorStatistics.SelectMany(x => x.BookStatistics).ToDictionary(x => x.BookId);

            foreach (var book in resources)
            {
                if (bookStatsDict.TryGetValue(book.Id, out var stats))
                {
                    book.Statistics = stats.ToResource();
                }
            }
        }

        private void LinkAuthorStatistics(BookResource resource, AuthorStatistics authorStatistics)
        {
            if (authorStatistics?.BookStatistics != null)
            {
                var dictBookStats = authorStatistics.BookStatistics.ToDictionary(v => v.BookId);

                resource.Statistics = dictBookStats.GetValueOrDefault(resource.Id).ToResource();
            }
        }

        private void MapCoversToLocal(params BookResource[] books)
        {
            foreach (var bookResource in books)
            {
                _coverMapper.ConvertToLocalUrls(bookResource.Id, MediaCoverEntity.Book, bookResource.Images);
            }
        }
    }
}
