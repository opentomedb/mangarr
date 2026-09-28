using System.Linq;
using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    // One copy each (2026-09-20): a file is never imported over an edition whose file Mangarr merely
    // adopted -- the original is managed outside Mangarr. The release-side twin is
    // AdoptedFileSpecification; this one catches what arrives anyway (manual import, a grab that
    // predates the adoption).
    public class AdoptedEditionSpecification : IImportDecisionEngineSpecification<LocalBook>
    {
        private readonly Logger _logger;

        public AdoptedEditionSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalBook localBook, DownloadClientItem downloadClientItem)
        {
            if (localBook.Edition?.BookFiles?.Value?.Any(f => f.Adopted) == true)
            {
                _logger.Debug("'{0}' would replace an adopted original of {1}", localBook.Path, localBook.Edition);
                return Decision.Reject("Adopted original — manage it outside Mangarr");
            }

            return Decision.Accept();
        }
    }
}
