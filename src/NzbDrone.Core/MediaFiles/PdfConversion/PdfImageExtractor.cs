using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NzbDrone.Core.MediaFiles.PdfConversion
{
    public interface IPdfImageExtractor
    {
        bool IsAvailable();
        int GetPageCount(string pdfPath);
        string GetFirstPageText(string pdfPath);
        double GetPageBrightness(string pdfPath, int pageNumber);
        double GetPageSaturation(string pdfPath, int pageNumber);
        Dictionary<int, List<string>> ExtractImagesByPage(string pdfPath, string destinationDir);
        List<PdfImageInfo> ListImages(string pdfPath);
        string RenderPage(string pdfPath, int pageNumber, int dpi, PdfRenderMode mode, string destinationDir);
        double PageDifference(string pdfPath, int pageNumber, string imagePath);
    }

    // One row of `pdfimages -list`: the page it is drawn on, its kind (image, smask, stencil, mask),
    // its color space ("-" for a 1-bit stencil), bits per component and horizontal resolution.
    public class PdfImageInfo
    {
        public int Page { get; set; }
        public string Type { get; set; }
        public string Color { get; set; }
        public int Bpc { get; set; }
        public int XPpi { get; set; }
    }

    // How a rendered page is written: Color and Gray as JPEG (photographic tones), Line (1-bit line
    // art only) as a grayscale PNG, which keeps its edges clean.
    public enum PdfRenderMode
    {
        Color,
        Gray,
        Line
    }

    // Extracts a manga PDF's page images LOSSLESSLY via poppler's pdfimages. Most manga PDFs store one
    // full-resolution image per page; `pdfimages -all` copies each embedded stream out verbatim (JPEG
    // stays JPEG — no re-encode), so those pages are bit-for-bit the same artwork. Page COUNT comes from
    // pdfinfo; a page that is not one plain image is drawn by pdftoppm instead (RenderPage, 2026-10-19).
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

        public Dictionary<int, List<string>> ExtractImagesByPage(string pdfPath, string destinationDir)
        {
            // -all keeps each image in its native encoding (jpg for DCT, png for 1-bit and flate, ...), so
            // nothing is transcoded; -p puts the page in the name: <dir>/page-<page>-<n>.<ext>.
            var prefix = Path.Combine(destinationDir, "page");
            var output = _processProvider.StartAndCapture(PdfImages, $"-all -p \"{pdfPath}\" \"{prefix}\"");

            if (output.ExitCode != 0)
            {
                throw new InvalidOperationException($"pdfimages exited {output.ExitCode} for {pdfPath}");
            }

            return Directory.GetFiles(destinationDir, "page-*")
                .Select(f => (file: f, parts: Path.GetFileNameWithoutExtension(f).Split('-')))
                .Where(x => x.parts.Length == 3 && int.TryParse(x.parts[1], out _))
                .GroupBy(x => int.Parse(x.parts[1]))
                .ToDictionary(g => g.Key, g => g.Select(x => x.file).OrderBy(f => f, StringComparer.Ordinal).ToList());
        }

        public List<PdfImageInfo> ListImages(string pdfPath)
        {
            var output = _processProvider.StartAndCapture(PdfImages, $"-list \"{pdfPath}\"");

            return ParseImageList(output.Standard.Select(l => l.Content));
        }

        // Rows only: page num type width height color comp bpc enc interp object ID x-ppi ...
        internal static List<PdfImageInfo> ParseImageList(IEnumerable<string> lines)
        {
            var images = new List<PdfImageInfo>();

            foreach (var line in lines ?? Enumerable.Empty<string>())
            {
                var cols = (line ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (cols.Length < 13 || !int.TryParse(cols[0], out var page) || !int.TryParse(cols[7], out var bpc) || !int.TryParse(cols[12], out var ppi))
                {
                    continue;
                }

                images.Add(new PdfImageInfo { Page = page, Type = cols[2], Color = cols[5], Bpc = bpc, XPpi = ppi });
            }

            return images;
        }

        public string RenderPage(string pdfPath, int pageNumber, int dpi, PdfRenderMode mode, string destinationDir)
        {
            var prefix = Path.Combine(destinationDir, $"render-{pageNumber:D4}");
            var format = mode == PdfRenderMode.Line ? "-png -gray" : mode == PdfRenderMode.Gray ? "-jpeg -jpegopt quality=92 -gray" : "-jpeg -jpegopt quality=92";
            var output = _processProvider.StartAndCapture("pdftoppm", $"{format} -r {dpi} -f {pageNumber} -l {pageNumber} -singlefile \"{pdfPath}\" \"{prefix}\"");
            var path = prefix + (mode == PdfRenderMode.Line ? ".png" : ".jpg");

            if (output.ExitCode != 0 || !File.Exists(path))
            {
                throw new InvalidOperationException($"pdftoppm could not render page {pageNumber} of {pdfPath} (exit {output.ExitCode})");
            }

            return path;
        }

        // How unlike the page as drawn an image is: both at ~200 px in grayscale, mean absolute pixel
        // difference 0-255, or -1 when either can't be read. Berserk 43 (2026-10-19): a page can be one
        // JPEG plus lettering drawn as vector shapes, or a JPEG the page clips and covers -- pdfimages sees
        // only the JPEG. A page that matches its image scores ~9-15, those pages 30-60.
        public double PageDifference(string pdfPath, int pageNumber, string imagePath)
        {
            var dir = Path.Combine(Path.GetTempPath(), "mangarr-diff-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(dir);
                var prefix = Path.Combine(dir, "p");
                _processProvider.StartAndCapture("pdftoppm", $"-gray -scale-to 200 -f {pageNumber} -l {pageNumber} -singlefile \"{pdfPath}\" \"{prefix}\"");

                var page = File.Exists(prefix + ".pgm") ? ParsePgm(File.ReadAllBytes(prefix + ".pgm")) : null;

                if (page == null)
                {
                    return -1;
                }

                var options = new DecoderOptions { TargetSize = new Size(page.Width, page.Height) };
                using var image = Image.Load<L8>(options, imagePath);
                image.Mutate(x => x.Resize(page.Width, page.Height));

                var pixels = new byte[page.Width * page.Height];
                image.CopyPixelDataTo(pixels);

                return MeanAbsoluteDifference(page.Pixels, pixels);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not compare page {0} of {1} with {2}", pageNumber, pdfPath, imagePath);
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

        internal class Pgm
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public byte[] Pixels { get; set; }
        }

        // A binary 8-bit PGM (P5) as pdftoppm writes it: "P5" width height maxval, one whitespace, pixels.
        internal static Pgm ParsePgm(byte[] pgm)
        {
            var pos = 0;
            string Token()
            {
                while (pos < pgm.Length && char.IsWhiteSpace((char)pgm[pos]))
                {
                    pos++;
                }

                var start = pos;
                while (pos < pgm.Length && !char.IsWhiteSpace((char)pgm[pos]))
                {
                    pos++;
                }

                return System.Text.Encoding.ASCII.GetString(pgm, start, pos - start);
            }

            if (Token() != "P5" || !int.TryParse(Token(), out var width) || !int.TryParse(Token(), out var height) || Token() != "255")
            {
                return null;
            }

            pos++;

            if (pgm.Length - pos < width * height)
            {
                return null;
            }

            return new Pgm { Width = width, Height = height, Pixels = pgm.Skip(pos).Take(width * height).ToArray() };
        }

        internal static double MeanAbsoluteDifference(byte[] a, byte[] b)
        {
            long sum = 0;
            for (var i = 0; i < a.Length; i++)
            {
                sum += Math.Abs(a[i] - b[i]);
            }

            return a.Length == 0 ? -1 : (double)sum / a.Length;
        }
    }
}
