using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    // Light novels (2026-09): .epub and the audio formats are importable now, so this is what
    // keeps a file on the edition of ITS class and out of the wrong library: a manga entry takes
    // archives only, a light-novel entry never takes an archive, and a file's class must be its
    // edition's media type (an EPUB matched to the Audio edition is wrong, not an upgrade).
    public class MediaTypeMatchesEditionSpecification : IImportDecisionEngineSpecification<LocalBook>
    {
        private readonly Logger _logger;

        public MediaTypeMatchesEditionSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalBook localBook, DownloadClientItem downloadClientItem)
        {
            // LN PDF (2026-09-22): a light novel's .pdf is its ebook, not an archive (MediaTypes.OfFile).
            var library = localBook.Author?.Library;
            var fileType = MediaTypes.OfFile(localBook.Path, library);

            if (library == LibraryType.Manga && fileType != MediaType.Archive)
            {
                _logger.Debug("'{0}' is {1} but {2} is a manga entry", localBook.Path, fileType, localBook.Author);
                // Server messages (2026-09-26): one whole sentence per class, never the class word as an
                // argument, so each sentence translates with its own grammar.
                return fileType == MediaType.Audio
                    ? Decision.Reject("audio file in a manga entry")
                    : Decision.Reject("ebook file in a manga entry");
            }

            if (library == LibraryType.LightNovel && fileType == MediaType.Archive)
            {
                _logger.Debug("'{0}' is an archive but {1} is a light-novel entry", localBook.Path, localBook.Author);
                return Decision.Reject("Archive file in a light-novel entry");
            }

            if (localBook.Edition != null && localBook.Edition.MediaType != fileType)
            {
                _logger.Debug("'{0}' is {1} but edition {2} is {3}", localBook.Path, fileType, localBook.Edition, localBook.Edition.MediaType);
                return WrongEdition(fileType, localBook.Edition.MediaType);
            }

            return Decision.Accept();
        }

        // Server messages (2026-09-26): the file's class and the edition's (never the same here) as one whole
        // sentence per pair -- the same English the "{0} file matched to the {1} edition" template gave.
        private static Decision WrongEdition(MediaType fileType, MediaType editionType)
        {
            switch (fileType)
            {
                case MediaType.Archive:
                    return editionType == MediaType.Ebook
                        ? Decision.Reject("archive file matched to the ebook edition")
                        : Decision.Reject("archive file matched to the audio edition");
                case MediaType.Ebook:
                    return editionType == MediaType.Archive
                        ? Decision.Reject("ebook file matched to the archive edition")
                        : Decision.Reject("ebook file matched to the audio edition");
                default:
                    return editionType == MediaType.Archive
                        ? Decision.Reject("audio file matched to the archive edition")
                        : Decision.Reject("audio file matched to the ebook edition");
            }
        }
    }
}
