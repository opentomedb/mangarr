using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class BlockedIndexerSpecification : IDecisionEngineSpecification
    {
        private readonly IIndexerStatusService _indexerStatusService;
        private readonly Logger _logger;

        private readonly ICachedDictionary<IndexerStatus> _blockedIndexerCache;

        public BlockedIndexerSpecification(IIndexerStatusService indexerStatusService, ICacheManager cacheManager, Logger logger)
        {
            _indexerStatusService = indexerStatusService;
            _logger = logger;

            _blockedIndexerCache = cacheManager.GetCacheDictionary(GetType(), "blocked", FetchBlockedIndexer, TimeSpan.FromSeconds(15));
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Temporary;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            var status = _blockedIndexerCache.Find(subject.Release.IndexerId.ToString());
            if (status != null)
            {
                return Decision.Reject("Indexer {0} is blocked till {1} due to failures, cannot grab release.", subject.Release.Indexer, status.DisabledTill);
            }

            return Decision.Accept();
        }

        private IDictionary<string, IndexerStatus> FetchBlockedIndexer()
        {
            return _indexerStatusService.GetBlockedProviders().ToDictionary(v => v.ProviderId.ToString());
        }
    }
}
