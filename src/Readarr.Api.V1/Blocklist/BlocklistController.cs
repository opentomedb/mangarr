using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Localization;
using NzbDrone.Http.REST.Attributes;
using Readarr.Http;
using Readarr.Http.Extensions;

namespace Readarr.Api.V1.Blocklist
{
    [V1ApiController]
    public class BlocklistController : Controller
    {
        private readonly IBlocklistService _blocklistService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IServerMessageLocalizer _messages;

        public BlocklistController(IBlocklistService blocklistService,
                                   ICustomFormatCalculationService formatCalculator,
                                   IServerMessageLocalizer messages)
        {
            _blocklistService = blocklistService;
            _formatCalculator = formatCalculator;
            _messages = messages;
        }

        [HttpGet]
        [Produces("application/json")]
        public PagingResource<BlocklistResource> GetBlocklist([FromQuery] PagingRequestResource paging)
        {
            var pagingResource = new PagingResource<BlocklistResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<BlocklistResource, NzbDrone.Core.Blocklisting.Blocklist>("date", SortDirection.Descending);

            return pagingSpec.ApplyToPage(_blocklistService.Paged, model => BlocklistResourceMapper.MapToResource(model, _formatCalculator, _messages));
        }

        [RestDeleteById]
        public void DeleteBlocklist(int id)
        {
            _blocklistService.Delete(id);
        }

        [HttpDelete("bulk")]
        public object Remove([FromBody] BlocklistBulkResource resource)
        {
            _blocklistService.Delete(resource.Ids);

            return new { };
        }
    }
}
