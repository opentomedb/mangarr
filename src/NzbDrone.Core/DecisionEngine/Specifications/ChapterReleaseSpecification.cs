using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Chapters;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.History;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // Volumes built from chapter packs (2026-10-21): a release with a parsed chapter range is grabbed
    // only for a manga series with the feature on (globally and for the series), only when official
    // ("(Digital)" and no scan marker), and only when it completes at least one volume on its own
    // (ParsingService maps it to exactly those volumes). Every other release is not this spec's business.
    public class ChapterReleaseSpecification : IDecisionEngineSpecification
    {
        private readonly IConfigService _configService;
        private readonly IHistoryService _historyService;
        private readonly Logger _logger;

        public ChapterReleaseSpecification(IConfigService configService, IHistoryService historyService, Logger logger)
        {
            _configService = configService;
            _historyService = historyService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            var range = subject.ParsedBookInfo?.ChapterRange;

            if (range == null)
            {
                return Decision.Accept();
            }

            if (subject.Author?.Library != LibraryType.Manga)
            {
                _logger.Debug("Chapter release '{0}' is not for a manga series, rejecting.", subject.Release?.Title);
                return Decision.Reject("Chapter releases build manga volumes only");
            }

            if (!ChapterVolumePlanner.IsEnabled(subject.Author, _configService))
            {
                _logger.Debug("Building volumes from chapters is off for '{0}', rejecting '{1}'.", subject.Author, subject.Release?.Title);
                return Decision.Reject("Building volumes from chapters is turned off");
            }

            if (!range.IsDigital)
            {
                _logger.Debug("Chapter release '{0}' is not marked (Digital), rejecting.", subject.Release?.Title);
                return Decision.Reject("Chapter release is not marked (Digital)");
            }

            if (range.IsScanlation)
            {
                _logger.Debug("Chapter release '{0}' looks like a scanlation, rejecting.", subject.Release?.Title);
                return Decision.Reject("Chapter release looks like a scanlation");
            }

            if (subject.Books == null || !subject.Books.Any())
            {
                _logger.Debug("Chapter release '{0}' ({1}) completes no volume, rejecting.", subject.Release?.Title, range);
                return Decision.Reject("Chapter release completes no volume");
            }

            // A release this series already built volumes from (a grab whose download imported) maps here only
            // to the volumes it skipped: the same files cannot complete them, so it is never fetched again.
            // The earlier download is left as it is (it may still be seeding).
            if (AlreadyBuilt(subject))
            {
                _logger.Debug("Chapter release '{0}' already built the volumes it completes, rejecting.", subject.Release?.Title);
                return Decision.Reject("Chapter release already built the volumes it completes");
            }

            return Decision.Accept();
        }

        private bool AlreadyBuilt(RemoteBook subject)
        {
            var title = subject.Release?.Title;

            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var history = _historyService.GetByAuthor(subject.Author.Id, null);
            var grabs = history
                .Where(h => h.EventType == EntityHistoryEventType.Grabbed &&
                            !string.IsNullOrWhiteSpace(h.DownloadId) &&
                            string.Equals(h.SourceTitle, title, StringComparison.OrdinalIgnoreCase))
                .Select(h => h.DownloadId)
                .ToHashSet();

            return history.Any(h => h.EventType == EntityHistoryEventType.BookFileImported && grabs.Contains(h.DownloadId));
        }
    }
}
