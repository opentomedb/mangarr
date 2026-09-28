using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class QualityAllowedByProfileSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public QualityAllowedByProfileSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            _logger.Debug("Checking if report meets quality requirements. {0}", subject.ParsedBookInfo.Quality);

            // Light novels (2026-09): the audio profile for an audio release, the main profile for
            // everything else (which is every manga release).
            var profile = subject.Author.QualityProfileFor(subject.MediaType);
            var qualityIndex = profile.GetIndex(subject.ParsedBookInfo.Quality.Quality);
            var qualityOrGroup = profile.Items[qualityIndex.Index];

            if (!qualityOrGroup.Allowed)
            {
                _logger.Debug("Quality {0} rejected by Author's quality profile", subject.ParsedBookInfo.Quality);
                return Decision.Reject("{0} is not wanted in profile", subject.ParsedBookInfo.Quality.Quality);
            }

            return Decision.Accept();
        }
    }
}
