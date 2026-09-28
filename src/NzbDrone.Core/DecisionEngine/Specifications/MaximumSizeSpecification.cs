using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class MaximumSizeSpecification : IDecisionEngineSpecification
    {
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public MaximumSizeSpecification(IConfigService configService, Logger logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            var size = subject.Release.Size;
            var maximumSize = _configService.MaximumSize.Megabytes();

            if (maximumSize == 0)
            {
                _logger.Debug("Maximum size is not set.");
                return Decision.Accept();
            }

            if (subject.Release.Size == 0)
            {
                _logger.Debug("Release has unknown size, skipping size check.");
                return Decision.Accept();
            }

            _logger.Debug("Checking if release meets maximum size requirements. {0}", size.SizeSuffix());

            if (size > maximumSize)
            {
                var reason = new ServerText("{0} is too big, maximum size is {1} (Settings->Indexers->Maximum Size)", size.SizeSuffix(), maximumSize.SizeSuffix());

                _logger.Debug(reason.English);
                return Decision.Reject(reason);
            }

            return Decision.Accept();
        }
    }
}
