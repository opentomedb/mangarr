using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    // LN PDF fix round 1 (2026-09-22): the profile opt-in has to hold at import time too, not only
    // on the release regrade -- something upstream (a tracked grab's regrade, a stale queue item, a
    // rescan of a file dropped in by hand) can hand this a LocalBook already graded Ebook PDF even
    // though the author's ebook profile still ships id 8 unticked (the default). Every other
    // quality is judged by QualityAllowedByProfileSpecification on the release before a file is ever
    // read; this is the one file-level gate for the one quality that check can't always catch.
    public class EbookPdfAllowedSpecification : IImportDecisionEngineSpecification<LocalBook>
    {
        private readonly Logger _logger;

        public EbookPdfAllowedSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalBook item, DownloadClientItem downloadClientItem)
        {
            if (item.Quality?.Quality != Quality.EbookPdf)
            {
                return Decision.Accept();
            }

            // No profile yet (e.g. a brand-new author mid-add): nothing to gate against, so let it
            // through, the same defensive read UpgradeSpecification uses.
            var profile = item.Author?.QualityProfileFor(MediaType.Ebook);

            if (profile != null && !profile.Allows(Quality.EbookPdf))
            {
                _logger.Debug("'{0}' is Ebook PDF but the light-novel profile doesn't want it", item.Path);
                return Decision.Reject("Ebook PDF is not wanted in the light-novel profile");
            }

            return Decision.Accept();
        }
    }
}
