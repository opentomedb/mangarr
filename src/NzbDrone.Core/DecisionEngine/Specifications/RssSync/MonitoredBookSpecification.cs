using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications.RssSync
{
    public class MonitoredBookSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public MonitoredBookSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            if (searchCriteria != null)
            {
                if (!searchCriteria.MonitoredBooksOnly)
                {
                    _logger.Debug("Skipping monitored check during search");
                    return Decision.Accept();
                }
            }

            if (!subject.Author.Monitored)
            {
                _logger.Debug("{0} is present in the DB but not tracked. Rejecting.", subject.Author);
                return Decision.Reject("Series is not monitored");
            }

            // Best-effort audio (D5): while a light novel has no audio file yet its audio editions
            // are wanted but PENDING -- RSS leaves them alone. The weekly probe and user-invoked
            // searches carry a searchCriteria and returned above, so they reach pending audio.
            if (subject.MediaType == MediaType.Audio && !subject.Author.AudioAvailable)
            {
                _logger.Debug("Audio is not available yet for {0}, rejecting RSS release", subject.Author);
                return Decision.Reject("Audiobook not available yet for this series");
            }

            // Light novels (2026-09): "is this volume monitored" is per media type -- the audio
            // edition of a volume can be off while the EPUB edition is on. A manga volume has one
            // Archive edition, monitored whenever the volume is, so the manga answer is unchanged.
            var monitoredCount = subject.Books.Count(book => book.Monitored && book.EditionOf(subject.MediaType)?.Monitored == true);
            if (monitoredCount == subject.Books.Count)
            {
                return Decision.Accept();
            }

            if (subject.Books.Count == 1)
            {
                _logger.Debug("Book is not monitored. Rejecting", monitoredCount, subject.Books.Count);
                return Decision.Reject("Volume is not monitored");
            }

            if (monitoredCount == 0)
            {
                _logger.Debug("No books in the release are monitored. Rejecting", monitoredCount, subject.Books.Count);
            }
            else
            {
                _logger.Debug("Only {0}/{1} books in the release are monitored. Rejecting", monitoredCount, subject.Books.Count);
            }

            return Decision.Reject("Volume is not monitored");
        }
    }
}
