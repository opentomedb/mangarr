using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using Readarr.Api.V1.Books;
using Readarr.Http;

namespace NzbDrone.Api.V1.Editions
{
    [V1ApiController]
    public class EditionController : Controller
    {
        private readonly IEditionService _editionService;

        public EditionController(IEditionService editionService)
        {
            _editionService = editionService;
        }

        [HttpGet]
        public List<EditionResource> GetEditions([FromQuery]List<int> bookId)
        {
            var editions = _editionService.GetEditionsByBook(bookId);

            return editions.ToResource();
        }

        // PUT /api/v1/edition/{id}  { "monitored": true }
        // Monitoring an edition unmonitors only its same-media-type siblings (EditionRepository);
        // unmonitoring touches that edition alone. The book is announced as edited either way.
        [HttpPut("{id:int}")]
        public ActionResult<EditionResource> SetMonitored(int id, [FromBody] EditionMonitoredResource resource)
        {
            if (resource == null)
            {
                return BadRequest("monitored is required");
            }

            var edition = _editionService.SetMonitored(id, resource.Monitored);

            return Accepted(edition.ToResource());
        }
    }
}
