using System;
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
        Skipped,   // not a 1:1 image PDF, or already cbz — left alone, flagged
        Failed     // nothing written; original untouched
    }

    public class PdfConvertResult
    {
        public PdfConvertStatus Status { get; set; }
        public int PageCount { get; set; }
        public bool Reversed { get; set; }
        public string Reason { get; set; }
    }

    public interface IPdfToCbzConverter
    {
        bool IsAvailable();
        PdfConvertResult Convert(string pdfPath, string outputCbzPath);
    }

    // Turns a manga PDF into a CBZ without quality loss. Extracts the embedded page images verbatim,
    // then REFUSES unless the image count matches the PDF's page count exactly (1:1) — a mismatch
    // means the PDF isn't a clean one-image-per-page scan (vector/text pages, spreads, ads), which
    // would produce a wrong or lossy CBZ, so it's skipped for manual handling rather than guessed.
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

            var workDir = outputCbzPath + ".mangarr-pdfwork";
            var tempCbz = outputCbzPath + ".mangarr-tmp";

            try
            {
                Cleanup(workDir, tempCbz);
                _diskProvider.CreateFolder(workDir);

                var pageCount = _extractor.GetPageCount(pdfPath);
                var images = _extractor.ExtractImages(pdfPath, workDir);

                // The 1:1 guard: refuse anything that isn't a clean one-image-per-page manga scan.
                if (pageCount <= 0 || images.Count != pageCount)
                {
                    _logger.Warn("Skipping {0}: {1} pages but {2} extracted images (not a 1:1 image PDF)", pdfPath, pageCount, images.Count);
                    return new PdfConvertResult { Status = PdfConvertStatus.Skipped, PageCount = pageCount, Reason = $"{pageCount} pages vs {images.Count} images" };
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

                _logger.Info("Converted {0} -> {1} ({2} pages, lossless{3})", Path.GetFileName(pdfPath), Path.GetFileName(outputCbzPath), pageCount, reversed ? ", page order flipped" : "");
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

        private void BuildCbz(System.Collections.Generic.List<string> images, string tempCbz)
        {
            using var outStream = File.Create(tempCbz);
            using var zip = new ZipArchive(outStream, ZipArchiveMode.Create);

            var index = 1;
            foreach (var image in images)
            {
                // Rename to a clean zero-padded scheme so every reader shows correct page order,
                // preserving the native extension (and therefore the untouched image bytes).
                var name = index.ToString("D4") + Path.GetExtension(image).ToLowerInvariant();
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);

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
