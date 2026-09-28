using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.RootFolders;
using Readarr.Http;

namespace Readarr.Api.V1.Author
{
    // Fix Match (D5): which AniList entry a series binds to.
    //
    //   GET /api/v1/author/{id}/matchcandidates?term=   AniList's page for the term (default: the
    //                                                  series name), in AniList's order — the user picks
    //   PUT /api/v1/author/{id}/match {"aniListId":30598}   bind; {"aniListId":null} unbinds, and the
    //                                                  next refresh re-runs the ranked search
    //
    // The PUT stores the id and queues a RefreshAuthor, which fetches the entry by id and rewrites
    // poster / description / aliases; SignalR updates the page. An id AniList does not know is a
    // 400 and nothing is stored. This is the only API writer of AniListId — the author edit PUT
    // never copies metadata (Author.ApplyChanges). The PUT answers with the author linked the
    // way GET /api/v1/author/{id} links it, so the page can take the object whole.
    [V1ApiController("author")]
    public class AuthorMatchController : Controller
    {
        private readonly IAuthorService _authorService;
        private readonly IAuthorMetadataService _authorMetadataService;
        private readonly IBookService _bookService;
        private readonly IAniListService _aniListService;
        private readonly IMangaSeriesMetadataProvider _mangaMetadataProvider;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IMapCoversToLocal _coverMapper;
        private readonly IAuthorStatisticsService _authorStatisticsService;
        private readonly IRootFolderService _rootFolderService;

        public AuthorMatchController(IAuthorService authorService,
                                     IAuthorMetadataService authorMetadataService,
                                     IBookService bookService,
                                     IAniListService aniListService,
                                     IMangaSeriesMetadataProvider mangaMetadataProvider,
                                     IManageCommandQueue commandQueueManager,
                                     IMapCoversToLocal coverMapper,
                                     IAuthorStatisticsService authorStatisticsService,
                                     IRootFolderService rootFolderService)
        {
            _authorService = authorService;
            _authorMetadataService = authorMetadataService;
            _bookService = bookService;
            _aniListService = aniListService;
            _mangaMetadataProvider = mangaMetadataProvider;
            _commandQueueManager = commandQueueManager;
            _coverMapper = coverMapper;
            _authorStatisticsService = authorStatisticsService;
            _rootFolderService = rootFolderService;
        }

        [HttpGet("{id:int}/matchcandidates")]
        public List<MatchCandidateResource> GetCandidates(int id, string term = null)
        {
            var author = _authorService.GetAuthor(id);
            var query = term.IsNullOrWhiteSpace() ? author.Name : term;

            return _aniListService.Candidates(query, author.Library)
                .Select(MatchCandidateResource.From)
                .ToList();
        }

        [HttpPut("{id:int}/match")]
        public ActionResult<AuthorResource> SetMatch(int id, [FromBody] MatchResource resource)
        {
            if (resource == null)
            {
                return BadRequest("aniListId is required (null to unbind)");
            }

            var author = _authorService.GetAuthor(id);

            if (resource.AniListId.HasValue && _aniListService.GetById(resource.AniListId.Value) == null)
            {
                // GetById is null for an unknown id and for a 429/transport failure alike.
                return BadRequest($"AniList has no series with id {resource.AniListId.Value}, or could not be reached — try again");
            }

            // Written from a copy for symmetry with the rebind pass; the cached AuthorService entry
            // is not involved (GetAuthor reads the repository directly), so a failed write leaves
            // this instance as it was either way.
            var metadata = author.Metadata.Value;
            var write = metadata.JsonClone();
            write.AniListId = resource.AniListId;
            _authorMetadataService.Upsert(write);
            metadata.AniListId = resource.AniListId;

            _mangaMetadataProvider.ForgetLookup(author.Name);
            _commandQueueManager.Push(new RefreshAuthorCommand(author.Id), trigger: CommandTrigger.Manual);

            return Accepted(Linked(author));
        }

        // Mirrors AuthorController.GetAuthorResource (private there): covers, statistics,
        // next/last book, root folder path.
        private AuthorResource Linked(NzbDrone.Core.Books.Author author)
        {
            var resource = author.ToResource();

            _coverMapper.ConvertToLocalUrls(resource.Id, MediaCoverEntity.Author, resource.Images);
            resource.Statistics = _authorStatisticsService.AuthorStatistics(author.Id).ToResource();
            resource.NextBook = _bookService.GetNextBooksByAuthorMetadataId(new[] { resource.AuthorMetadataId }).FirstOrDefault();
            resource.LastBook = _bookService.GetLastBooksByAuthorMetadataId(new[] { resource.AuthorMetadataId }).FirstOrDefault();
            resource.RootFolderPath = _rootFolderService.GetBestRootFolderPath(author.Path, _rootFolderService.All());

            return resource;
        }
    }
}
