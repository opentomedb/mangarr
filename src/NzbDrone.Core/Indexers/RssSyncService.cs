using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MonthlyPacks;

namespace NzbDrone.Core.Indexers
{
    public class RssSyncService : IExecute<RssSyncCommand>
    {
        private readonly IIndexerStatusService _indexerStatusService;
        private readonly IIndexerFactory _indexerFactory;
        private readonly IFetchAndParseRss _rssFetcherAndParser;
        private readonly IMakeDownloadDecision _downloadDecisionMaker;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly IPendingReleaseService _pendingReleaseService;
        private readonly IBookService _bookService;
        private readonly IConfigService _configService;
        private readonly IMonthlyPackService _monthlyPackService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public RssSyncService(IIndexerStatusService indexerStatusService,
                              IIndexerFactory indexerFactory,
                              IFetchAndParseRss rssFetcherAndParser,
                              IMakeDownloadDecision downloadDecisionMaker,
                              IProcessDownloadDecisions processDownloadDecisions,
                              IPendingReleaseService pendingReleaseService,
                              IBookService bookService,
                              IConfigService configService,
                              IMonthlyPackService monthlyPackService,
                              IEventAggregator eventAggregator,
                              Logger logger)
        {
            _indexerStatusService = indexerStatusService;
            _indexerFactory = indexerFactory;
            _rssFetcherAndParser = rssFetcherAndParser;
            _downloadDecisionMaker = downloadDecisionMaker;
            _processDownloadDecisions = processDownloadDecisions;
            _pendingReleaseService = pendingReleaseService;
            _bookService = bookService;
            _configService = configService;
            _monthlyPackService = monthlyPackService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        private async Task<ProcessedDecisions> Sync()
        {
            _logger.ProgressInfo("Starting RSS Sync");

            var rssReleases = await _rssFetcherAndParser.Fetch();
            var pendingReleases = _pendingReleaseService.GetPending();

            var reports = rssReleases.Concat(pendingReleases).ToList();
            var decisions = _downloadDecisionMaker.GetRssDecision(reports);
            var processed = await _processDownloadDecisions.ProcessDecisions(decisions);

            // Monthly volume packs (2026-10-21): the same fetched releases, once, against the missing volumes a
            // single release did not just take.
            if (_configService.UseMonthlyVolumePacks && rssReleases.Any(r => MonthlyPackService.LooksLikePack(r.Title)))
            {
                var grabbed = processed.Grabbed.Concat(processed.Pending).SelectMany(d => d.RemoteBook?.Books ?? new List<Book>()).Select(b => b.Id).ToHashSet();
                var pagingSpec = new PagingSpec<Book>
                {
                    Page = 1,
                    PageSize = 100000,
                    SortDirection = SortDirection.Ascending,
                    SortKey = "Id"
                };

                pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Author.Value.Monitored == true);

                var missing = _bookService.BooksWithoutFiles(pagingSpec, LibraryType.Manga).Records.Where(b => !grabbed.Contains(b.Id)).ToList();

                if (missing.Any())
                {
                    _monthlyPackService.ProcessReleases(rssReleases, missing);
                }
            }

            // Server messages (2026-09-26): two whole templates instead of an appended tail, so each one has a key.
            if (processed.Pending.Any())
            {
                _logger.ProgressInfo("RSS Sync Completed. Reports found: {0}, Reports grabbed: {1}, Reports pending: {2}", reports.Count, processed.Grabbed.Count, processed.Pending.Count);
            }
            else
            {
                _logger.ProgressInfo("RSS Sync Completed. Reports found: {0}, Reports grabbed: {1}", reports.Count, processed.Grabbed.Count);
            }

            return processed;
        }

        public void Execute(RssSyncCommand message)
        {
            var processed = Sync().GetAwaiter().GetResult();
            var grabbedOrPending = processed.Grabbed.Concat(processed.Pending).ToList();

            _eventAggregator.PublishEvent(new RssSyncCompleteEvent(processed));
        }
    }
}
