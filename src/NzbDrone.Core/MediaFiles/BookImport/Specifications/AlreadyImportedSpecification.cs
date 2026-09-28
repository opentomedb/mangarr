using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class AlreadyImportedSpecification : IImportDecisionEngineSpecification<LocalEdition>
    {
        private readonly IHistoryService _historyService;
        private readonly Logger _logger;

        public AlreadyImportedSpecification(IHistoryService historyService,
                                            Logger logger)
        {
            _historyService = historyService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;

        public const string RejectionMessagePrefix = "Book already imported";

        // Matches this spec's rejection and ImportApprovedBooks' same-batch duplicate
        // message ("Book has already been imported"). The duplicate flavor is safe to treat
        // as imported: it is only emitted when an earlier file for the same book/part exists
        // in importResults, and if that earlier file had errored its own non-matching message
        // would also be present, failing the all-messages check.
        public static bool IsAlreadyImportedRejection(string message)
        {
            return message != null &&
                   (message.StartsWith(RejectionMessagePrefix, StringComparison.OrdinalIgnoreCase) ||
                    message.StartsWith("Book has already been imported", StringComparison.OrdinalIgnoreCase));
        }

        public Decision IsSatisfiedBy(LocalEdition localBookRelease, DownloadClientItem downloadClientItem)
        {
            if (downloadClientItem == null)
            {
                _logger.Debug("No download client information is available, skipping");
                return Decision.Accept();
            }

            var bookRelease = localBookRelease.Edition;

            if ((!bookRelease.BookFiles?.Value?.Any()) ?? true)
            {
                _logger.Debug("Skipping already imported check for book without files");
                return Decision.Accept();
            }

            var bookHistory = _historyService.GetByBook(bookRelease.BookId, null);
            var lastImported = bookHistory.FirstOrDefault(h => h.EventType == EntityHistoryEventType.BookFileImported);
            var lastGrabbed = bookHistory.FirstOrDefault(h => h.EventType == EntityHistoryEventType.Grabbed);

            if (lastImported == null)
            {
                _logger.Trace("Book file has not been imported");
                return Decision.Accept();
            }

            if (lastGrabbed != null && lastGrabbed.Date.After(lastImported.Date))
            {
                _logger.Trace("Book file was grabbed again after importing");
                return Decision.Accept();
            }

            if (lastImported.DownloadId == downloadClientItem.DownloadId)
            {
                _logger.Debug("Book previously imported at {0}", lastImported.Date);
                // Server messages (2026-09-26): RejectionMessagePrefix's text inlined, so the template is one
                // literal the guard can key; the constant stays for IsAlreadyImportedRejection's StartsWith.
                return Decision.Reject("Book already imported at {0}", lastImported.Date);
            }

            return Decision.Accept();
        }
    }
}
