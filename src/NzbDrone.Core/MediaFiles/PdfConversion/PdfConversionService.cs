using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Extras.Metadata.ComicInfo;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.MediaFiles.PdfConversion
{
    // Executes ConvertPdfToCbzCommand. For every PDF volume: convert losslessly to CBZ, repoint the
    // library record at the new file, embed ComicInfo, then move the original PDF into a holding
    // folder (never deleted — the Books share has no snapshots). The CBZ is only ever placed after a
    // verified conversion, so a failure leaves the PDF exactly as it was.
    public class PdfConversionService : IExecute<ConvertPdfToCbzCommand>, IExecute<ConvertBookPdfToCbzCommand>
    {
        private const string PopplerMissingMessage = "PDF conversion unavailable: poppler (pdfimages) is not installed";
        private const string MangaOnlyMessage = "PDF to CBZ conversion is for manga only";

        private readonly IPdfToCbzConverter _converter;
        private readonly IComicInfoWriter _comicInfoWriter;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IDiskProvider _diskProvider;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public PdfConversionService(IPdfToCbzConverter converter,
                                    IComicInfoWriter comicInfoWriter,
                                    IAuthorService authorService,
                                    IBookService bookService,
                                    IMediaFileService mediaFileService,
                                    IDiskProvider diskProvider,
                                    IAppFolderInfo appFolderInfo,
                                    IConfigService configService,
                                    Logger logger)
        {
            _configService = configService;
            _converter = converter;
            _comicInfoWriter = comicInfoWriter;
            _authorService = authorService;
            _bookService = bookService;
            _mediaFileService = mediaFileService;
            _diskProvider = diskProvider;
            _appFolderInfo = appFolderInfo;
            _logger = logger;
        }

        public void Execute(ConvertPdfToCbzCommand message)
        {
            if (!_converter.IsAvailable())
            {
                _logger.Error("PDF conversion unavailable: poppler (pdfimages) is not installed in the container");
                message.ResultMessage = PopplerMissingMessage;
                return;
            }

            // LN PDF (2026-09-22): the conversion is a manga tool -- a light novel's PDF is its ebook
            // (Ebook PDF). A per-author run for a light novel does nothing; the all-authors run (the
            // nightly sweep, the index button) walks manga entries only.
            List<Author> authors;

            if (message.AuthorId.HasValue)
            {
                var author = _authorService.GetAuthor(message.AuthorId.Value);

                if (author != null && author.Library != LibraryType.Manga)
                {
                    _logger.Info("{0} is a light novel; PDF to CBZ conversion is for manga only", author.Name);
                    message.ResultMessage = MangaOnlyMessage;
                    return;
                }

                authors = new List<Author> { author };
            }
            else
            {
                authors = _authorService.GetAllAuthors().Where(a => a != null && a.Library == LibraryType.Manga).ToList();
            }

            var converted = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var author in authors.Where(a => a != null))
            {
                foreach (var book in _bookService.GetBooksByAuthor(author.Id))
                {
                    ConvertBookFiles(author, book, ref converted, ref skipped, ref failed);
                }
            }

            _logger.Info("PDF->CBZ conversion complete: {0} converted, {1} skipped, {2} failed", converted, skipped, failed);

            SetResult(message, converted, failed);
        }

        // Book Details action: same per-file pipeline, scoped to one volume.
        public void Execute(ConvertBookPdfToCbzCommand message)
        {
            if (!_converter.IsAvailable())
            {
                _logger.Error("PDF conversion unavailable: poppler (pdfimages) is not installed in the container");
                message.ResultMessage = PopplerMissingMessage;
                return;
            }

            Book book;

            try
            {
                book = _bookService.GetBook(message.BookId);
            }
            catch (ModelNotFoundException)
            {
                book = null;
            }

            var author = book == null ? null : _authorService.GetAuthorByMetadataId(book.AuthorMetadataId);

            if (author == null)
            {
                _logger.Warn("Cannot convert book {0}: book or author not found", message.BookId);
                message.ResultMessage = "Volume not found";
                return;
            }

            if (author.Library != LibraryType.Manga)
            {
                _logger.Info("{0} is a light novel; PDF to CBZ conversion is for manga only", author.Name);
                message.ResultMessage = MangaOnlyMessage;
                return;
            }

            var converted = 0;
            var skipped = 0;
            var failed = 0;

            ConvertBookFiles(author, book, ref converted, ref skipped, ref failed);

            _logger.Info("PDF->CBZ conversion complete for {0}: {1} converted, {2} skipped, {3} failed", book.Title, converted, skipped, failed);

            SetResult(message, converted, failed);
        }

        private void ConvertBookFiles(Author author, Book book, ref int converted, ref int skipped, ref int failed)
        {
            foreach (var file in _mediaFileService.GetFilesByBook(book.Id))
            {
                if (Path.GetExtension(file.Path ?? string.Empty).ToLowerInvariant() != ".pdf")
                {
                    continue;
                }

                var cbzPath = Path.ChangeExtension(file.Path, ".cbz");
                var result = _converter.Convert(file.Path, cbzPath);

                if (result.Status != PdfConvertStatus.Converted)
                {
                    if (result.Status == PdfConvertStatus.Skipped)
                    {
                        skipped++;
                    }
                    else
                    {
                        failed++;
                    }

                    continue;
                }

                var pdfPath = file.Path;

                // A concurrent download import may have upgraded this book while the
                // conversion ran; re-check the record before repointing the library.
                var currentFiles = _mediaFileService.GetFilesByBook(book.Id);
                var current = currentFiles.FirstOrDefault(f => f.Id == file.Id);
                if (current == null || current.Path != pdfPath)
                {
                    _logger.Warn("Book file for {0} changed during conversion; discarding converted CBZ", pdfPath);

                    // The import's destination for a .cbz upgrade is the SAME path as
                    // cbzPath (naming scheme + extension), so only delete the converted
                    // file if no current record for the book now claims that path.
                    var claimed = currentFiles.Any(f => cbzPath.PathEquals(f.Path));
                    if (!claimed && _diskProvider.FileExists(cbzPath))
                    {
                        _diskProvider.DeleteFile(cbzPath);
                    }

                    skipped++;
                    continue;
                }

                // Point the library at the CBZ before the PDF leaves the folder. The record
                // deliberately KEEPS its PDF quality: the CBZ container makes it readable
                // now, but staying below the CBZ cutoff lets a native scene CBZ
                // upgrade-replace it later — so a converted file (and any reversed-order
                // mistake inside it) is always temporary.
                file.Path = cbzPath;
                file.Size = _diskProvider.GetFileSize(cbzPath);
                _mediaFileService.Update(file);

                // Beta readiness (2026-09-28, review M3): Write ComicInfo To = Never means no ComicInfo from
                // any automatic path; the converted CBZ is Mangarr's own new file, so New Downloads and All
                // Imports both embed as before.
                if (_configService.WriteComicInfo != WriteComicInfoType.Never)
                {
                    _comicInfoWriter.WriteForFile(author, book, file);
                }

                MoveToHolding(author, pdfPath);
                converted++;
            }
        }

        // Originals are moved, never deleted — recoverable under <appdata>/pdf-originals/<series>/.
        private void MoveToHolding(Author author, string pdfPath)
        {
            var holdingDir = Path.Combine(_appFolderInfo.AppDataFolder, "pdf-originals", FileNameBuilder.CleanFileName(author.Name));
            _diskProvider.CreateFolder(holdingDir);

            var target = Path.Combine(holdingDir, Path.GetFileName(pdfPath));
            _diskProvider.MoveFile(pdfPath, target, true);
        }

        // What the toolbar button reports back. Skipped volumes (already CBZ, or upgraded by an
        // import while the conversion ran) are noise to the person who pressed the button — the
        // log lines keep them.
        //
        // A run that did nothing at all sets no result, so the executor falls back to the command's
        // "Completed". ConvertPdfToCbzCommand is also the nightly scheduled sweep and
        // PdfImportConversionService pushes it per-author after an import; neither suppresses
        // client messages, so reporting "Converted 0 PDF volume(s) to CBZ, 0 failed" would toast
        // a no-op at every open tab every night.
        //
        // Server messages (2026-09-26): set through SetResultMessage, so the toast reads in the UI language.
        private static void SetResult(Command message, int converted, int failed)
        {
            if (converted == 0 && failed == 0)
            {
                return;
            }

            message.SetResultMessage(new ServerText("Converted {0} PDF volume(s) to CBZ, {1} failed", converted, failed));
        }
    }
}
