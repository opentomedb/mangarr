using System.IO;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles.PdfConversion
{
    // Auto-converts grabbed PDFs: when a download import lands a PDF volume, queue the existing
    // per-author ConvertPdfToCbzCommand so the file becomes a first-class CBZ (with ComicInfo)
    // without the manual Library toolbar button. Library-rescan imports (NewDownload == false,
    // e.g. PDFs restored from the holding folder) never trigger it.
    public class PdfImportConversionService : IHandle<TrackImportedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public PdfImportConversionService(IManageCommandQueue commandQueueManager, Logger logger)
        {
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Handle(TrackImportedEvent message)
        {
            if (!message.NewDownload)
            {
                return;
            }

            var path = message.ImportedBook?.Path;

            if (path.IsNullOrWhiteSpace() || Path.GetExtension(path).ToLowerInvariant() != ".pdf")
            {
                return;
            }

            var author = message.BookInfo?.Author;

            if (author == null || author.Id == 0)
            {
                return;
            }

            // LN PDF (2026-09-22): the CBZ conversion is a manga tool. A light novel's PDF is its
            // ebook (quality Ebook PDF) and stays a PDF.
            if (author.Library != LibraryType.Manga)
            {
                _logger.Debug("Imported PDF '{0}' belongs to a light novel — no CBZ conversion", Path.GetFileName(path));
                return;
            }

            _logger.Info("Imported PDF '{0}' — queueing automatic CBZ conversion", Path.GetFileName(path));

            // Per-author scope; the command queue dedupes an identical pending command, so a
            // multi-PDF import batch converts the author once.
            _commandQueueManager.Push(new ConvertPdfToCbzCommand(author.Id));
        }
    }
}
