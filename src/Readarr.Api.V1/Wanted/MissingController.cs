using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.SignalR;
using Readarr.Api.V1.Books;
using Readarr.Http;
using Readarr.Http.Extensions;

namespace Readarr.Api.V1.Wanted
{
    [V1ApiController("wanted/missing")]
    public class MissingController : BookControllerWithSignalR
    {
        public MissingController(IBookService bookService,
                             ISeriesBookLinkService seriesBookLinkService,
                             IAuthorStatisticsService authorStatisticsService,
                             IMapCoversToLocal coverMapper,
                             IUpgradableSpecification upgradableSpecification,
                             IMediaFileService mediaFileService,
                             IBroadcastSignalRMessage signalRBroadcaster)
        : base(bookService, seriesBookLinkService, authorStatisticsService, coverMapper, upgradableSpecification, mediaFileService, signalRBroadcaster)
        {
        }

        // library / mediaType (2026-09-21) back the Wanted filter presets: "manga"|"lightNovel" and
        // "ebook"|"audio"|"both". Both are optional, and omitting them is the unfiltered page the
        // Monitored / Unmonitored filters have always asked for.
        [HttpGet]
        public PagingResource<BookResource> GetMissingBooks([FromQuery] PagingRequestResource paging, bool includeAuthor = false, bool monitored = true, LibraryType? library = null, WantedMediaTypeFilter? mediaType = null)
        {
            var pagingResource = new PagingResource<BookResource>(paging);
            var pagingSpec = new PagingSpec<Book>
            {
                Page = pagingResource.Page,
                PageSize = pagingResource.PageSize,
                SortKey = pagingResource.SortKey,
                SortDirection = pagingResource.SortDirection
            };

            if (monitored)
            {
                pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Author.Value.Monitored == true);
            }
            else
            {
                pagingSpec.FilterExpressions.Add(v => v.Monitored == false || v.Author.Value.Monitored == false);
            }

            return pagingSpec.ApplyToPage(spec => _bookService.BooksWithoutFiles(spec, library, mediaType), v => MapToResource(v, includeAuthor));
        }
    }
}
