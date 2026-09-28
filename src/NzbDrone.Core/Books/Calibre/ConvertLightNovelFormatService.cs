using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Books.Calibre
{
    // Design D3 (backfill on request, never automatic): changing the delivery-format setting
    // affects new imports only. This command converts what already exists -- one book at a time,
    // each conversion followed to the end (up to ConversionTimeout) before the next starts (final
    // review I2), skipping a book that already carries the format.
    // Audio files and manga files are never in scope: only a light-novel ebook BookFile that
    // calibre already owns (Home == Calibre, CalibreId > 0) is ever touched.
    // A PDF-tracked book is skipped (LN PDF, 2026-09-22).
    public class ConvertLightNovelFormatService : IExecute<ConvertLightNovelFormatCommand>
    {
        private static readonly TimeSpan ConversionTimeout = TimeSpan.FromMinutes(10);

        private readonly IAuthorService _authorService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IConfigService _configService;
        private readonly ICalibreProxy _calibreProxy;
        private readonly ILightNovelCalibreSettings _calibreSettings;
        private readonly ILightNovelStorage _storage;
        private readonly Logger _logger;

        public ConvertLightNovelFormatService(IAuthorService authorService,
                                              IMediaFileService mediaFileService,
                                              IConfigService configService,
                                              ICalibreProxy calibreProxy,
                                              ILightNovelCalibreSettings calibreSettings,
                                              ILightNovelStorage storage,
                                              Logger logger)
        {
            _authorService = authorService;
            _mediaFileService = mediaFileService;
            _configService = configService;
            _calibreProxy = calibreProxy;
            _calibreSettings = calibreSettings;
            _storage = storage;
            _logger = logger;
        }

        public void Execute(ConvertLightNovelFormatCommand message)
        {
            // Conversion is calibre's (light-novel storage, 2026-09-22): with ebooks going to the entry
            // folder there is no calibre book to add a format to.
            if (_storage.EbookHome != LightNovelHome.Calibre)
            {
                _logger.Info("Ebooks go to the entry folder — conversion needs a calibre library");
                message.ResultMessage = "Ebooks go to the entry folder — conversion needs a calibre library";
                return;
            }

            var output = PreferredFormats.LightNovelOutputFormat(_configService.PreferredLightNovelFormat);

            if (output == null)
            {
                _logger.Info("Delivery format is EPUB — nothing to convert");
                message.ResultMessage = "Delivery format is EPUB — nothing to convert";
                return;
            }

            var authors = (message.AuthorId.HasValue
                    ? new List<Author> { _authorService.GetAuthor(message.AuthorId.Value) }
                    : _authorService.GetAllAuthors())
                .Where(a => a != null && a.Library == LibraryType.LightNovel)
                .ToList();

            var settings = _calibreSettings.ForConfig();

            var converted = 0;
            var alreadyHadIt = 0;
            var pdfOnly = 0;
            var failed = 0;

            // Two BookFile rows can point at the same calibre book (e.g. a pack's parts, or a
            // duplicate row); a second row for a calibre id already handled this run is skipped
            // outright -- never a second, redundant conversion of the same book.
            var processedCalibreIds = new HashSet<int>();

            foreach (var author in authors)
            {
                var files = _mediaFileService.GetFilesByAuthor(author.Id)
                    .Where(f => f.Home == FileHome.Calibre && f.CalibreId > 0 && f.Edition.Value.MediaType == MediaType.Ebook)
                    .ToList();

                foreach (var file in files)
                {
                    if (!processedCalibreIds.Add(file.CalibreId))
                    {
                        continue;
                    }

                    try
                    {
                        var book = _calibreProxy.GetBook(file.CalibreId, settings);

                        if (book.Formats != null && book.Formats.Keys.Any(k => string.Equals(k, output, StringComparison.OrdinalIgnoreCase)))
                        {
                            alreadyHadIt++;
                            continue;
                        }

                        // LN PDF (2026-09-22): calibre's PDF input makes poor ebooks -- a book tracked
                        // by its PDF (no EPUB or AZW3 in calibre) is skipped, not converted and not failed.
                        var tracked = CalibreFormats.TrackedLightNovelFormat(book.Formats?.Keys);

                        if (string.Equals(tracked, "PDF", StringComparison.OrdinalIgnoreCase))
                        {
                            pdfOnly++;
                            continue;
                        }

                        // Final review round (2026-09-22): calibre holds none of EPUB/AZW3/PDF for
                        // this book -- there is nothing to convert FROM, so this fails outright
                        // instead of reaching ConvertToFormatAndWait with a null input format.
                        if (tracked == null)
                        {
                            _logger.Warn("Light-novel format conversion: calibre book {0} holds none of EPUB/AZW3/PDF, nothing to convert from", file.CalibreId);
                            failed++;
                            continue;
                        }

                        // Fix round 1 (2026-09-22): the input format is what calibre is actually
                        // tracked by, not the BookFile row's own (possibly stale) extension -- a row
                        // still pointing at a .pdf while calibre holds EPUB+PDF must convert from the
                        // EPUB, never the PDF.
                        var inputFormat = tracked.ToUpperInvariant();

                        if (_calibreProxy.ConvertToFormatAndWait(file.CalibreId, inputFormat, output, settings, ConversionTimeout))
                        {
                            converted++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Light-novel format conversion to {0} failed for {1} (calibre id {2})", output, file.Path, file.CalibreId);
                        failed++;
                    }
                }
            }

            _logger.Info("Light-novel format conversion: {0} converted, {1} already had {2}, {3} skipped (PDF only), {4} failed", converted, alreadyHadIt, output, pdfOnly, failed);

            // Server messages (2026-09-26): two whole templates instead of an optional tail, so each one has a key.
            if (pdfOnly > 0)
            {
                message.SetResultMessage(new ServerText("Converted {0} book(s) to {1}, {2} already had it, {3} skipped (PDF only), {4} failed", converted, output, alreadyHadIt, pdfOnly, failed));
            }
            else
            {
                message.SetResultMessage(new ServerText("Converted {0} book(s) to {1}, {2} already had it, {3} failed", converted, output, alreadyHadIt, failed));
            }
        }
    }
}
