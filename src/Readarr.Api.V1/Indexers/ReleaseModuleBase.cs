using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Localization;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Indexers
{
    public abstract class ReleaseControllerBase : RestController<ReleaseResource>
    {
        private readonly IServerMessageLocalizer _messages;

        // Server messages (2026-09-26): Interactive Search rejection reasons in the UI language.
        protected ReleaseControllerBase(IServerMessageLocalizer messages)
        {
            _messages = messages;
        }

        [NonAction]
        public override ActionResult<ReleaseResource> GetResourceByIdWithErrorHandler(int id)
        {
            return base.GetResourceByIdWithErrorHandler(id);
        }

        protected override ReleaseResource GetResourceById(int id)
        {
            throw new NotImplementedException();
        }

        protected virtual List<ReleaseResource> MapDecisions(IEnumerable<DownloadDecision> decisions)
        {
            var result = new List<ReleaseResource>();

            foreach (var downloadDecision in decisions)
            {
                var release = MapDecision(downloadDecision, result.Count);

                result.Add(release);
            }

            return result;
        }

        protected virtual ReleaseResource MapDecision(DownloadDecision decision, int initialWeight)
        {
            var release = decision.ToResource(_messages);

            release.ReleaseWeight = initialWeight;

            if (decision.RemoteBook.Author != null)
            {
                release.QualityWeight = decision.RemoteBook
                                                .Author
                                                .QualityProfile.Value.GetIndex(release.Quality.Quality).Index * 100;
            }

            release.QualityWeight += release.Quality.Revision.Real * 10;
            release.QualityWeight += release.Quality.Revision.Version;

            return release;
        }
    }
}
