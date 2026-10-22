using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles.PdfConversion
{
    public enum PdfConvertStatus
    {
        Converted,
        Skipped,   // no pages, or already cbz — left alone
        Failed     // nothing written; original untouched
    }

    public class PdfConvertResult
    {
        public PdfConvertStatus Status { get; set; }
        public int PageCount { get; set; }
        public bool Reversed { get; set; }
        public string Reason { get; set; }

        // The file itself can never convert (no pages): trying it again unchanged gives the same answer.
        public bool NotConvertible { get; set; }
    }

    public interface IPdfToCbzConverter
    {
        bool IsAvailable();
        PdfConvertResult Convert(string pdfPath, string outputCbzPath);
    }

    // Turns a manga PDF into a CBZ, one entry per PDF page. A page drawn from exactly one JPEG, and that
    // looks like that JPEG when drawn (no lettering or art over it, nothing clipped), keeps the JPEG
    // verbatim -- no quality loss. Any other page (layered images, a soft mask or overlay,
    // vector art, text, 1-bit or flate images) is rendered as the reader would draw it, at the volume's
    // own resolution up to 600 dpi (Berserk 43, 2026-10-19: 13 of 216 pages layered, refused whole by the
    // old one-image-per-page rule; and its 1-bit stencil pages come out of pdfimages INVERTED, so a
    // non-JPEG image is never lifted -- pdfimages ignores stencil and Decode semantics).
    // The output is built in a temp file and verified before it is placed; the source PDF is never
    // touched here (the caller moves it to a holding dir only after this succeeds).
    public class PdfToCbzConverter : IPdfToCbzConverter
    {
        private readonly IPdfImageExtractor _extractor;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public PdfToCbzConverter(IPdfImageExtractor extractor, IDiskProvider diskProvider, Logger logger)
        {
            _extractor = extractor;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public bool IsAvailable() => _extractor.IsAvailable();

        public PdfConvertResult Convert(string pdfPath, string outputCbzPath)
        {
            if (Path.GetExtension(pdfPath).ToLowerInvariant() != ".pdf")
            {
                return new PdfConvertResult { Status = PdfConvertStatus.Skipped, Reason = "Not a PDF" };
            }

            // Checked before any extraction (Dandadan Vol. 19, 2026-10-17: a PDF tracked beside its own CBZ was
            // rebuilt in full every night only to be thrown away here). Checked again after the build below.
            if (_diskProvider.FileExists(outputCbzPath))
            {
                return new PdfConvertResult { Status = PdfConvertStatus.Skipped, Reason = "Target CBZ already exists" };
            }

            var workDir = outputCbzPath + ".mangarr-pdfwork";
            var tempCbz = outputCbzPath + ".mangarr-tmp";

            try
            {
                Cleanup(workDir, tempCbz);
                _diskProvider.CreateFolder(workDir);

                var pageCount = _extractor.GetPageCount(pdfPath);

                if (pageCount <= 0)
                {
                    _logger.Warn("Skipping {0}: pdfinfo found no pages", pdfPath);
                    return new PdfConvertResult { Status = PdfConvertStatus.Skipped, Reason = "No pages", NotConvertible = true };
                }

                var extracted = _extractor.ExtractImagesByPage(pdfPath, workDir);
                var listed = _extractor.ListImages(pdfPath);
                var dpi = RenderDpi(listed);
                var images = new List<string>();
                var rendered = 0;

                for (var page = 1; page <= pageCount; page++)
                {
                    var files = extracted.TryGetValue(page, out var f) ? f : new List<string>();
                    var drawn = listed.Where(i => i.Page == page).ToList();

                    if (files.Count == 1 && drawn.Count == 1 && drawn[0].Type == "image" && LiftedExtensions.Contains(Path.GetExtension(files[0]).ToLowerInvariant()) &&
                        LooksLikeThePage(_extractor.PageDifference(pdfPath, page, files[0])))
                    {
                        images.Add(files[0]);
                        continue;
                    }

                    images.Add(_extractor.RenderPage(pdfPath, page, dpi, RenderModeOf(drawn), workDir));
                    rendered++;
                }

                // Reversed-layout PDFs (cover last, colophon first) read back-to-front; flip so the
                // cover becomes page 1 and the story runs forward. Uses page text when present, and
                // brightness + saturation comparisons (colophon near-white/colorless vs colorful
                // cover) for image-only PDFs — saturation catches bright covers the brightness
                // bands miss.
                var reversed = PageOrderNormalizer.IsReversed(
                    _extractor.GetFirstPageText(pdfPath),
                    _extractor.GetPageBrightness(pdfPath, 1),
                    _extractor.GetPageBrightness(pdfPath, pageCount),
                    _extractor.GetPageSaturation(pdfPath, 1),
                    _extractor.GetPageSaturation(pdfPath, pageCount));
                if (reversed)
                {
                    images.Reverse();
                    _logger.Info("Detected reversed page order in {0}; flipping to cover-first", Path.GetFileName(pdfPath));
                }

                BuildCbz(images, tempCbz);
                VerifyCbz(tempCbz, pageCount);

                if (_diskProvider.FileExists(outputCbzPath))
                {
                    // A cbz of this volume already exists next to the pdf — don't clobber it.
                    return new PdfConvertResult { Status = PdfConvertStatus.Skipped, PageCount = pageCount, Reason = "Target CBZ already exists" };
                }

                File.Move(tempCbz, outputCbzPath);

                _logger.Info("Converted {0} -> {1} ({2} pages: {3} lossless, {4} rendered at {5} dpi{6})", Path.GetFileName(pdfPath), Path.GetFileName(outputCbzPath), pageCount, pageCount - rendered, rendered, dpi, reversed ? ", page order flipped" : "");
                return new PdfConvertResult { Status = PdfConvertStatus.Converted, PageCount = pageCount, Reversed = reversed };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to convert {0}; source PDF left untouched", pdfPath);
                return new PdfConvertResult { Status = PdfConvertStatus.Failed, Reason = ex.Message };
            }
            finally
            {
                Cleanup(workDir, tempCbz);
            }
        }

        // The page drawn small and its lone JPEG agree (see PageDifference); -1 (couldn't compare) renders.
        private static bool LooksLikeThePage(double difference)
        {
            return difference >= 0 && difference <= 20;
        }

        private static readonly HashSet<string> LiftedExtensions = new HashSet<string> { ".jpg", ".jpeg" };
        private static readonly HashSet<string> PageImageTypes = new HashSet<string> { "image", "stencil" };

        // The resolution a rendered page is drawn at: the median of the volume's own page images
        // (masks excluded), so it matches its neighbours; 72-600 dpi (600 is a 3000 x 4400 px page,
        // past any reader's screen; Berserk 43's 1200 ppi line art made 5.5 MB pages), 300 when no
        // page has an image.
        internal static int RenderDpi(List<PdfImageInfo> listed)
        {
            var ppis = listed.Where(i => PageImageTypes.Contains(i.Type) && i.XPpi > 0).Select(i => i.XPpi).OrderBy(p => p).ToList();

            if (ppis.Count == 0)
            {
                return 300;
            }

            return Math.Clamp(ppis[ppis.Count / 2], 72, 600);
        }

        // Line: every image on the page is 1-bit line art. Gray: every one is grayscale or 1-bit.
        // Color otherwise -- including a page with no image at all (vector art or text).
        private static PdfRenderMode RenderModeOf(List<PdfImageInfo> drawn)
        {
            if (drawn.Count == 0)
            {
                return PdfRenderMode.Color;
            }

            if (drawn.All(i => i.Bpc == 1))
            {
                return PdfRenderMode.Line;
            }

            return drawn.All(i => i.Bpc == 1 || i.Color == "gray") ? PdfRenderMode.Gray : PdfRenderMode.Color;
        }

        private void BuildCbz(List<string> images, string tempCbz)
        {
            using var outStream = File.Create(tempCbz);
            using var zip = new ZipArchive(outStream, ZipArchiveMode.Create);

            var index = 1;
            foreach (var image in images)
            {
                // Rename to a clean zero-padded scheme so every reader shows correct page order,
                // preserving the native extension (and therefore the untouched image bytes).
                var name = index.ToString("D4") + Path.GetExtension(image).ToLowerInvariant();
                var entry = zip.CreateFileEntry(name, CompressionLevel.NoCompression);

                using var input = File.OpenRead(image);
                using var output = entry.Open();
                input.CopyTo(output);
                index++;
            }
        }

        private void VerifyCbz(string cbzPath, int expectedPages)
        {
            using var zip = ZipFile.OpenRead(cbzPath);
            var entries = zip.Entries.Count(e => !e.FullName.EndsWith("/"));

            if (entries != expectedPages)
            {
                throw new InvalidDataException($"Rebuilt CBZ has {entries} entries, expected {expectedPages}");
            }
        }

        private void Cleanup(string workDir, string tempCbz)
        {
            if (_diskProvider.FolderExists(workDir))
            {
                _diskProvider.DeleteFolder(workDir, true);
            }

            if (_diskProvider.FileExists(tempCbz))
            {
                _diskProvider.DeleteFile(tempCbz);
            }
        }
    }
}
