using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // Copy-in hold (2026-09-16, D4c): while a light-novel entry's owned files are still being
    // copied in, no release for it may be grabbed -- this is the gate RSS goes through, and the
    // reason an interactive table shows during the hold. Manga entries are never pending.
    public class CopyInPendingSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public CopyInPendingSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            if (subject.Author?.CopyInPending == true)
            {
                _logger.Debug("Copy-in pending for {0}, rejecting '{1}'", subject.Author.Name, subject.Release?.Title);
                return Decision.Reject("Copy-in pending for this series");
            }

            return Decision.Accept();
        }
    }
}
