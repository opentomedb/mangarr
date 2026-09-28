using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // Light novels (2026-09): a release targets the edition of its media type (archive / ebook /
    // audio). A volume with no edition of that type can never hold the file -- an audiobook for a
    // manga volume, an archive for a light-novel volume -- so say so, in addition to the profile
    // rejection. Manga volumes have an Archive edition and manga releases are Archive: accepted.
    public class MediaTypeSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public MediaTypeSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            var missing = subject.Books.FirstOrDefault(book => book.EditionOf(subject.MediaType) == null);

            if (missing != null)
            {
                _logger.Debug("{0} has no {1} edition, rejecting '{2}'", missing, subject.MediaType, subject.Release?.Title);

                // Server messages (2026-09-26): one whole sentence per media type, so each language can
                // inflect it; the English is what "No {0} edition" rendered with the lower-cased type.
                switch (subject.MediaType)
                {
                    case MediaType.Audio:
                        return Decision.Reject("No audio edition on this volume");
                    case MediaType.Ebook:
                        return Decision.Reject("No ebook edition on this volume");
                    default:
                        return Decision.Reject("No archive edition on this volume");
                }
            }

            // A search for one light-novel class never takes the other (2026-09-21): the audio leg
            // reaching book-only indexers found an EPUB batch and grabbed it. Manga searches are
            // untyped (Archive on both sides); an interactive override still goes through.
            var searched = searchCriteria?.MediaType;

            if (searched.HasValue && searched.Value != MediaType.Archive && subject.MediaType != MediaType.Archive && subject.MediaType != searched.Value)
            {
                _logger.Debug("Search asked for {0}, '{1}' is {2}, rejecting", searched.Value, subject.Release?.Title, subject.MediaType);

                // Server messages (2026-09-26): only these two pairs reach here (neither side is Archive and
                // they differ), each a whole sentence.
                return searched.Value == MediaType.Audio
                    ? Decision.Reject("Search asked for an audiobook, release is an ebook")
                    : Decision.Reject("Search asked for an ebook, release is an audiobook");
            }

            return Decision.Accept();
        }
    }
}
