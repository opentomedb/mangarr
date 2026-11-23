using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Localization;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class AcceptableSizeSpecification : IDecisionEngineSpecification
    {
        private readonly IQualityDefinitionService _qualityDefinitionService;
        private readonly Logger _logger;

        public AcceptableSizeSpecification(IQualityDefinitionService qualityDefinitionService, Logger logger)
        {
            _qualityDefinitionService = qualityDefinitionService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            // The size floor (2026-11-14): the quality definition's MinSize / MaxSize (MB, the Quality
            // Definitions sliders) were inert since the fork -- Readarr left this rule stubbed. A 5 MB
            // "volume" post with nothing but an .nfo in it was grabbed, sat in the queue as import
            // pending and blocked the real copy. Unknown size is still accepted; 0 means no limit.
            var size = subject.Release.Size;

            if (size == 0)
            {
                _logger.Debug("Release has unknown size, skipping size check");
                return Decision.Accept();
            }

            var quality = subject.ParsedBookInfo.Quality.Quality;
            var qualityDefinition = _qualityDefinitionService.Get(quality);

            if (qualityDefinition.MinSize.HasValue && qualityDefinition.MinSize.Value > 0)
            {
                var minSize = qualityDefinition.MinSize.Value.Megabytes();

                if (size < minSize)
                {
                    var reason = new ServerText("{0} is smaller than the minimum for {1} ({2}) (Settings->Quality)", size.SizeSuffix(), quality.Name, minSize.SizeSuffix());
                    _logger.Debug(reason.English);
                    return Decision.Reject(reason);
                }
            }

            if (qualityDefinition.MaxSize.HasValue && qualityDefinition.MaxSize.Value > 0)
            {
                var maxSize = qualityDefinition.MaxSize.Value.Megabytes();

                if (size > maxSize)
                {
                    var reason = new ServerText("{0} is larger than the maximum for {1} ({2}) (Settings->Quality)", size.SizeSuffix(), quality.Name, maxSize.SizeSuffix());
                    _logger.Debug(reason.English);
                    return Decision.Reject(reason);
                }
            }

            _logger.Debug("Item: {0}, meets size constraints", subject);
            return Decision.Accept();
        }
    }
}
