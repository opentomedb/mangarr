using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // One copy each (2026-09-20): a file Mangarr merely adopted (an original already in calibre's
    // library or Audiobookshelf's tree) is managed outside Mangarr and never upgraded, so no
    // release for that edition is grabbed. Same edition lookup as UpgradeDiskSpecification: only
    // the edition of the release's media type counts.
    public class AdoptedFileSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public AdoptedFileSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            var adopted = subject.Books
                .SelectMany(b => b.EditionOf(subject.MediaType)?.BookFiles.Value ?? new List<BookFile>())
                .Any(f => f.Adopted);

            if (adopted)
            {
                _logger.Debug("An adopted original is on disk for {0}, rejecting '{1}'", subject.Author?.Name, subject.Release?.Title);
                return Decision.Reject("Adopted original — manage it outside Mangarr");
            }

            return Decision.Accept();
        }
    }
}
