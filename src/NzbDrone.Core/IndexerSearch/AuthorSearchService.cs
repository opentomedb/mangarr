using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch
{
    public class AuthorSearchService : IExecute<AuthorSearchCommand>
    {
        private readonly ISearchForReleases _releaseSearchService;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly Logger _logger;

        public AuthorSearchService(ISearchForReleases releaseSearchService,
            IProcessDownloadDecisions processDownloadDecisions,
            Logger logger)
        {
            _releaseSearchService = releaseSearchService;
            _processDownloadDecisions = processDownloadDecisions;
            _logger = logger;
        }

        public void Execute(AuthorSearchCommand message)
        {
            var manual = message.Trigger == CommandTrigger.Manual;

            // A typed command (a light-novel re-search after a failed grab) searches that one
            // class; an untyped one is every monitored class -- the one Archive search for manga.
            var decisions = message.MediaType.HasValue
                ? _releaseSearchService.AuthorSearch(message.AuthorId, message.MediaType.Value, false, manual, false, true).GetAwaiter().GetResult()
                : _releaseSearchService.AuthorSearch(message.AuthorId, false, manual, false).GetAwaiter().GetResult();
            var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();

            _logger.ProgressInfo("Series search completed. {0} reports downloaded.", processed.Grabbed.Count);
        }
    }
}
