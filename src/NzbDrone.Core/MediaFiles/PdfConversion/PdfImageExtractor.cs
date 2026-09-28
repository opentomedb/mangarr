using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;

namespace NzbDrone.Core.MediaFiles.PdfConversion
{
    public interface IPdfImageExtractor
    {
        bool IsAvailable();
        int GetPageCount(string pdfPath);
        string GetFirstPageText(string pdfPath);
        double GetPageBrightness(string pdfPath, int pageNumber);
        double GetPageSaturation(string pdfPath, int pageNumber);
        List<string> ExtractImages(string pdfPath, string destinationDir);
    }

    // Extracts a manga PDF's page images LOSSLESSLY via poppler's pdfimages. Manga PDFs store one
    // full-resolution JPEG per page; `pdfimages -all` copies each embedded stream out verbatim (JPEG
    // stays JPEG — no re-encode), so the resulting CBZ is bit-for-bit the same artwork. Page COUNT
    // comes from pdfinfo; the caller refuses to convert when the image count doesn't match 1:1.
    public class PopplerPdfImageExtractor : IPdfImageExtractor
    {
        private const string PdfImages = "pdfimages";
        private const string PdfInfo = "pdfinfo";

        private readonly IProcessProvider _processProvider;
        private readonly Logger _logger;

        public PopplerPdfImageExtractor(IProcessProvider processProvider, Logger logger)
        {
            _processProvider = processProvider;
            _logger = logger;
        }

        public bool IsAvailable()
        {
            // Probe through the shell first: `pdfimages -v` prints its version banner on stderr, and the
            // process runner logs every stderr line at Error -- three false errors per conversion run.
            try
            {
                var probe = _processProvider.StartAndCapture("sh", "-c \"command -v " + PdfImages + "\"");
                if (probe.ExitCode == 0 && probe.Standard.Any(l => l.Content.IsNotNullOrWhiteSpace()))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "sh probe for pdfimages unavailable, trying the binary");
            }

            try
            {
                _processProvider.StartAndCapture(PdfImages, "-v");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "pdfimages not available");
                return false;
            }
        }

        public int GetPageCount(string pdfPath)
        {
            var output = _processProvider.StartAndCapture(PdfInfo, $"\"{pdfPath}\"");

            var line = output.Lines
                .Select(l => l.Content ?? string.Empty)
                .FirstOrDefault(c => c.StartsWith("Pages:", StringComparison.OrdinalIgnoreCase));

            if (line != null && int.TryParse(line.Split(':').Last().Trim(), out var pages))
            {
                return pages;
            }

            return 0;
        }

        // Text of page 1 only, used to detect a reversed (colophon-first) manga PDF.
        public string GetFirstPageText(string pdfPath)
        {
            try
            {
                var output = _processProvider.StartAndCapture("pdftotext", $"-f 1 -l 1 \"{pdfPath}\" -");
                return string.Join("\n", output.Lines.Select(l => l.Content));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not read first-page text of {0}", pdfPath);
                return null;
            }
        }

        // Mean luminance (0..1) of one page, rendered tiny via pdftoppm to a raw grayscale PGM.
        // Used to spot a reversed layout when the PDF has no text layer: a colophon page is
        // near-white (~0.95), a cover illustration is much darker/mid-toned. Returns -1 on failure
        // (treated as "unknown", never triggers a flip). No image-decode dependency — PGM is raw.
        public double GetPageBrightness(string pdfPath, int pageNumber)
        {
            var dir = Path.Combine(Path.GetTempPath(), "mangarr-bright-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(dir);
                var prefix = Path.Combine(dir, "p");
                _processProvider.StartAndCapture("pdftoppm", $"-gray -r 6 -f {pageNumber} -l {pageNumber} \"{pdfPath}\" \"{prefix}\"");

                var pgm = Directory.GetFiles(dir, "*.pgm").FirstOrDefault();
                return pgm == null ? -1 : MeanBrightness(File.ReadAllBytes(pgm));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not measure brightness of page {0} in {1}", pageNumber, pdfPath);
                return -1;
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        // Mean saturation (0..1) of one page, rendered tiny via pdftoppm to a raw color PPM — the
        // color counterpart of GetPageBrightness. A cover is saturated; endpapers, colophons and
        // B&W interiors are near zero, which spots a reversed layout even when the cover is too
        // bright for the brightness bands. Returns -1 on failure (treated as "unknown", never
        // triggers a flip).
        public double GetPageSaturation(string pdfPath, int pageNumber)
        {
            var dir = Path.Combine(Path.GetTempPath(), "mangarr-sat-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(dir);
                var prefix = Path.Combine(dir, "p");
                _processProvider.StartAndCapture("pdftoppm", $"-r 6 -f {pageNumber} -l {pageNumber} \"{pdfPath}\" \"{prefix}\"");

                var ppm = Directory.GetFiles(dir, "*.ppm").FirstOrDefault();
                return ppm == null ? -1 : MeanSaturation(File.ReadAllBytes(ppm));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not measure saturation of page {0} in {1}", pageNumber, pdfPath);
                return -1;
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        // Parse a binary PPM (P6): "P6" <ws> width <ws> height <ws> maxval <single ws> then
        // width*height RGB triples. Returns the mean per-pixel saturation ((max-min)/max, black
        // counts as 0) in 0..1, or -1 when the bytes are not a P6 PPM.
        internal static double MeanSaturation(byte[] ppm)
        {
            var pos = 0;
            string Token()
            {
                while (pos < ppm.Length && (char.IsWhiteSpace((char)ppm[pos]) || ppm[pos] == '#'))
                {
                    if (ppm[pos] == '#')
                    {
                        while (pos < ppm.Length && ppm[pos] != '\n')
                        {
                            pos++;
                        }
                    }
                    else
                    {
                        pos++;
                    }
                }

                var start = pos;
                while (pos < ppm.Length && !char.IsWhiteSpace((char)ppm[pos]))
                {
                    pos++;
                }

                return System.Text.Encoding.ASCII.GetString(ppm, start, pos - start);
            }

            if (Token() != "P6")
            {
                return -1;
            }

            var width = int.Parse(Token());
            var height = int.Parse(Token());
            var maxval = int.Parse(Token());
            pos++; // the single whitespace after maxval

            double total = 0;
            var count = 0;
            for (; pos + 2 < ppm.Length && count < width * height; pos += 3, count++)
            {
                int r = ppm[pos];
                int g = ppm[pos + 1];
                int b = ppm[pos + 2];
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                total += max == 0 ? 0 : (max - min) / (double)max;
            }

            return count == 0 || maxval == 0 ? -1 : total / count;
        }

        // Parse a binary PGM (P5): "P5" <ws> width <ws> height <ws> maxval <single ws> then
        // width*height bytes. Returns mean pixel value / maxval in 0..1.
        internal static double MeanBrightness(byte[] pgm)
        {
            var pos = 0;
            string Token()
            {
                while (pos < pgm.Length && (char.IsWhiteSpace((char)pgm[pos]) || pgm[pos] == '#'))
                {
                    if (pgm[pos] == '#')
                    {
                        while (pos < pgm.Length && pgm[pos] != '\n')
                        {
                            pos++;
                        }
                    }
                    else
                    {
                        pos++;
                    }
                }

                var start = pos;
                while (pos < pgm.Length && !char.IsWhiteSpace((char)pgm[pos]))
                {
                    pos++;
                }

                return System.Text.Encoding.ASCII.GetString(pgm, start, pos - start);
            }

            if (Token() != "P5")
            {
                return -1;
            }

            var width = int.Parse(Token());
            var height = int.Parse(Token());
            var maxval = int.Parse(Token());
            pos++; // the single whitespace after maxval

            long sum = 0;
            var count = 0;
            for (; pos < pgm.Length && count < width * height; pos++, count++)
            {
                sum += pgm[pos];
            }

            return count == 0 || maxval == 0 ? -1 : (double)sum / count / maxval;
        }

        public List<string> ExtractImages(string pdfPath, string destinationDir)
        {
            // -all keeps each image in its native encoding (jpg for DCT, png/tiff otherwise), so
            // nothing is transcoded. Output is <dir>/page-000.jpg, page-001.jpg, ... (zero-padded,
            // so lexical sort == page order).
            var prefix = Path.Combine(destinationDir, "page");
            var output = _processProvider.StartAndCapture(PdfImages, $"-all \"{pdfPath}\" \"{prefix}\"");

            if (output.ExitCode != 0)
            {
                throw new InvalidOperationException($"pdfimages exited {output.ExitCode} for {pdfPath}");
            }

            return Directory.GetFiles(destinationDir, "page-*")
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }
    }
}
