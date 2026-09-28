using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // Covered volumes (2026-09-17, D4): a light-novel volume whose audiobook is inside another
    // volume's file is not grabbed for audio -- the gate RSS goes through. Only the Audio edition
    // is covered; an EPUB release for the same volume is unaffected. Manga releases are Archive.
    //
    // A pack that carries the covering volume itself (Vol. 5-6 when 6 is covered by 5) is not a
    // grab of the covered volume but an upgrade of its carrier: it passes here and the usual
    // upgrade/cutoff specifications decide. The rejection is for a release that would bring the
    // covered volume without its carrier.
    public class CoveredVolumeSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public CoveredVolumeSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            if (subject.MediaType != MediaType.Audio)
            {
                return Decision.Accept();
            }

            foreach (var book in subject.Books)
            {
                var audio = book.EditionOf(MediaType.Audio);

                if (audio?.CoveredByVolume == null)
                {
                    continue;
                }

                // the carrier is in the release: a pack upgrade of the carrier, not a grab of this volume
                if (subject.Books.Any(b => b.Id != book.Id && b.VolumeNumber == audio.CoveredByVolume.Value))
                {
                    continue;
                }

                _logger.Debug("{0} audio is covered by Vol. {1}, rejecting '{2}'", book.Title, audio.CoveredByVolume, subject.Release?.Title);
                return Decision.Reject("Audiobook covered by Vol. {0}", audio.CoveredByVolume);
            }

            return Decision.Accept();
        }
    }
}
