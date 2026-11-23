using System;
using System.IO;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles.PdfConversion;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    // Page-count check (2026-11-07): a volume file that holds far fewer pages than the catalogue lists
    // for the volume is cut short -- a split archive imported without its other parts, a PDF whose
    // download stopped early -- and is refused, so the download waits for a person instead of filling
    // the slot with a truncated copy (manual import can still take it). "Far fewer" is under two thirds:
    // a good digital copy drops covers and adverts and stays well above that (179 pages of a 192-page
    // volume), the truncated copy that prompted this held 112. Nothing is refused on an unknown count:
    // no catalogue count, a PDF pdfinfo cannot count, a stand-in built from chapters (its pages are
    // whatever the chapters held), a file already in the library.
    //
    // The junk check (2026-11-14): a .cbz or .zip that is not a zip archive at all, or that holds no
    // page images, is never a usable volume and is refused outright, whatever the catalogue lists --
    // a usenet post whose "volume" was a 12-byte text file named .cbz was imported twice as a finished
    // volume under the earlier "unreadable means unknown" reading. A light-novel zip is allowed to hold
    // no page images (it wraps an ebook, not pages); a file already in the library is left alone.
    public class PageCountSpecification : IImportDecisionEngineSpecification<LocalBook>
    {
        public const int MinimumThirds = 2;

        private readonly IPdfImageExtractor _pdfImageExtractor;
        private readonly Logger _logger;

        public PageCountSpecification(IPdfImageExtractor pdfImageExtractor, Logger logger)
        {
            _pdfImageExtractor = pdfImageExtractor;
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalBook localBook, DownloadClientItem downloadClientItem)
        {
            if (localBook.ExistingFile)
            {
                return Decision.Accept();
            }

            var lightNovel = localBook.Author?.Library == LibraryType.LightNovel;
            var zip = IsZipArchive(localBook.Path);
            var held = zip ? ArchivePages.Count(localBook.Path) : null;

            if (zip)
            {
                if (held == null)
                {
                    _logger.Debug("'{0}' is not a readable archive", localBook.Path);
                    return Decision.Reject("Is not a readable archive");
                }

                if (held == 0 && !lightNovel)
                {
                    _logger.Debug("'{0}' holds no pages", localBook.Path);
                    return Decision.Reject("Holds no pages");
                }
            }

            var listed = localBook.Edition?.PageCount ?? 0;

            if (listed <= 0 ||
                lightNovel ||
                UpgradableSpecification.IsStandIn(localBook.Quality))
            {
                return Decision.Accept();
            }

            if (!zip)
            {
                held = PdfPages(localBook.Path);
            }

            if (held == null || held <= 0)
            {
                return Decision.Accept();
            }

            if (held.Value * 3 < listed * MinimumThirds)
            {
                _logger.Debug("'{0}' holds {1} pages; the catalogue lists {2} for {3}", localBook.Path, held, listed, localBook.Book);
                return Decision.Reject("Holds {0} pages; the catalogue lists {1} for this volume", held.Value, listed);
            }

            return Decision.Accept();
        }

        private static bool IsZipArchive(string path)
        {
            var extension = Path.GetExtension(path);

            return string.Equals(extension, ".cbz", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase);
        }

        private int? PdfPages(string path)
        {
            var extension = Path.GetExtension(path);

            if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return _pdfImageExtractor.GetPageCount(path);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Could not count the pages of '{0}'", path);
                    return null;
                }
            }

            return null;
        }
    }
}
