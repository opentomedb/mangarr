using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles.PdfConversion;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.PdfConversion
{
    [TestFixture]
    public class PdfToCbzConverterFixture : CoreTest<PdfToCbzConverter>
    {
        private string _dir;

        [SetUp]
        public void Setup()
        {
            _renders.Clear();
            _dir = Path.Combine(Path.GetTempPath(), "mangarr-pdf-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns<string>(Directory.Exists);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.CreateFolder(It.IsAny<string>())).Callback<string>(p => Directory.CreateDirectory(p));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>())).Callback<string, bool>((p, r) => Directory.Delete(p, r));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }

        // Fake extractor: N pages, one jpg per page (known bytes, last byte = 0-based page index),
        // each listed as the page's single image.
        private void GivenPdf(int pages, byte[] pageBytes)
        {
            GivenPages(pages, (dest, page) => new[] { WriteImage(dest, page, 0, ".jpg", pageBytes.Concat(new[] { (byte)(page - 1) }).ToArray()) },
                       Enumerable.Range(1, pages).Select(p => Image(p, "image", "icc", 8)).ToArray());
        }

        private void GivenPages(int pages, System.Func<string, int, string[]> filesForPage, params PdfImageInfo[] listed)
        {
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.GetPageCount(It.IsAny<string>())).Returns(pages);
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.PageDifference(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>())).Returns(9.0);
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.ListImages(It.IsAny<string>())).Returns(listed.ToList());
            Mocker.GetMock<IPdfImageExtractor>()
                .Setup(e => e.ExtractImagesByPage(It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string>((pdf, dest) => Enumerable.Range(1, pages)
                    .Select(p => (p, files: filesForPage(dest, p)))
                    .Where(x => x.files.Length > 0)
                    .ToDictionary(x => x.p, x => x.files.ToList()));
        }

        private static string WriteImage(string dest, int page, int index, string extension, byte[] bytes)
        {
            var path = Path.Combine(dest, $"page-{page:D3}-{index:D3}{extension}");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static PdfImageInfo Image(int page, string type, string color, int bpc, int xPpi = 1200)
        {
            return new PdfImageInfo { Page = page, Type = type, Color = color, Bpc = bpc, XPpi = xPpi };
        }

        // RenderPage writes <dest>/render-<page>.<jpg|png> holding (byte)(100 + page) and records the call.
        private readonly List<(int Page, int Dpi, PdfRenderMode Mode)> _renders = new List<(int, int, PdfRenderMode)>();

        private void GivenRenderer()
        {
            Mocker.GetMock<IPdfImageExtractor>()
                .Setup(e => e.RenderPage(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<PdfRenderMode>(), It.IsAny<string>()))
                .Returns<string, int, int, PdfRenderMode, string>((pdf, page, dpi, mode, dest) =>
                {
                    _renders.Add((page, dpi, mode));
                    var path = Path.Combine(dest, $"render-{page:D3}" + (mode == PdfRenderMode.Line ? ".png" : ".jpg"));
                    File.WriteAllBytes(path, new[] { (byte)(100 + page) });
                    return path;
                });
        }

        private static byte FirstByte(ZipArchive zip, string entry)
        {
            using var s = zip.GetEntry(entry).Open();
            return (byte)s.ReadByte();
        }

        [Test]
        public void should_flip_page_order_for_a_reversed_pdf()
        {
            // Image-only reversed PDF: no text, but page 1 near-white (colophon), last page dark
            // (cover). Page 1 carries byte 0, page 3 byte 2 (the cover).
            GivenPdf(3, new byte[0]);
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.GetFirstPageText(It.IsAny<string>())).Returns((string)null);
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.GetPageBrightness(It.IsAny<string>(), 1)).Returns(0.96);
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.GetPageBrightness(It.IsAny<string>(), 3)).Returns(0.60);

            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");
            var cbz = Path.Combine(_dir, "vol.cbz");

            var result = Subject.Convert(pdf, cbz);

            result.Reversed.Should().BeTrue();
            using var zip = ZipFile.OpenRead(cbz);

            // 0001.jpg must now be the PDF's LAST extracted image (byte 2 = the cover).
            using var s = zip.GetEntry("0001.jpg").Open();
            s.ReadByte().Should().Be(2);
        }

        [Test]
        public void should_convert_a_clean_one_image_per_page_pdf()
        {
            GivenPdf(pages: 3, pageBytes: new byte[] { 0xFF, 0xD8, 0xFF });
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");
            var cbz = Path.Combine(_dir, "vol.cbz");

            var result = Subject.Convert(pdf, cbz);

            result.Status.Should().Be(PdfConvertStatus.Converted);
            result.PageCount.Should().Be(3);

            using var zip = ZipFile.OpenRead(cbz);
            zip.Entries.Should().HaveCount(3);
            zip.Entries.Select(e => e.FullName).Should().Equal("0001.jpg", "0002.jpg", "0003.jpg"); // padded, ordered
        }

        [Test]
        public void should_preserve_extracted_image_bytes()
        {
            var page = new byte[] { 0xFF, 0xD8, 0xFF, 0x11, 0x22 };
            GivenPdf(2, page);
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");
            var cbz = Path.Combine(_dir, "vol.cbz");

            Subject.Convert(pdf, cbz);

            using var zip = ZipFile.OpenRead(cbz);
            using var s = zip.GetEntry("0001.jpg").Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.ToArray().Should().Equal(page.Concat(new byte[] { 0 }).ToArray()); // byte-for-byte
        }

        // Berserk 43 (2026-10-19): 216 pages, 232 images. A page with one JPEG is lifted byte-for-byte;
        // a layered page (two images, an image + soft mask) is rendered as the reader would draw it, and
        // so is a 1-bit stencil page (pdfimages writes those inverted), at the volume's resolution <= 600.
        [Test]
        public void should_lift_single_image_pages_and_render_layered_ones()
        {
            GivenRenderer();
            GivenPages(4,
                (dest, page) => page switch
                {
                    1 => new[] { WriteImage(dest, 1, 0, ".jpg", new byte[] { 1 }) },
                    2 => new[] { WriteImage(dest, 2, 0, ".jpg", new byte[] { 2 }), WriteImage(dest, 2, 1, ".jpg", new byte[] { 2 }) },
                    3 => new[] { WriteImage(dest, 3, 0, ".jpg", new byte[] { 3 }), WriteImage(dest, 3, 1, ".jpg", new byte[] { 3 }) },
                    _ => new[] { WriteImage(dest, 4, 0, ".png", new byte[] { 4 }) }
                },
                Image(1, "image", "icc", 8),
                Image(2, "image", "icc", 8), Image(2, "image", "icc", 8),
                Image(3, "image", "gray", 8), Image(3, "smask", "gray", 8),
                Image(4, "stencil", "-", 1));
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");
            var cbz = Path.Combine(_dir, "vol.cbz");

            var result = Subject.Convert(pdf, cbz);

            result.Status.Should().Be(PdfConvertStatus.Converted);
            result.PageCount.Should().Be(4);
            _renders.Should().Equal((2, 600, PdfRenderMode.Color), (3, 600, PdfRenderMode.Gray), (4, 600, PdfRenderMode.Line));

            using var zip = ZipFile.OpenRead(cbz);
            zip.Entries.Select(e => e.FullName).Should().Equal("0001.jpg", "0002.jpg", "0003.jpg", "0004.png");
            FirstByte(zip, "0001.jpg").Should().Be(1);
            FirstByte(zip, "0002.jpg").Should().Be(102);
            FirstByte(zip, "0003.jpg").Should().Be(103);
            FirstByte(zip, "0004.png").Should().Be(104);
        }

        // A page with no image (text or vector art) is rendered in colour; one whose only images are
        // 1-bit line art is rendered as a lossless PNG.
        [Test]
        public void should_render_an_imageless_page_in_colour_and_layered_line_art_as_png()
        {
            GivenRenderer();
            GivenPages(3,
                (dest, page) => page switch
                {
                    1 => new[] { WriteImage(dest, 1, 0, ".jpg", new byte[] { 1 }) },
                    2 => new string[0],
                    _ => new[] { WriteImage(dest, 3, 0, ".png", new byte[] { 3 }), WriteImage(dest, 3, 1, ".png", new byte[] { 3 }) }
                },
                Image(1, "image", "gray", 8, 600),
                Image(3, "stencil", "-", 1, 600), Image(3, "stencil", "-", 1, 600));
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");

            Subject.Convert(pdf, Path.Combine(_dir, "vol.cbz")).Status.Should().Be(PdfConvertStatus.Converted);

            _renders.Should().Equal((2, 600, PdfRenderMode.Color), (3, 600, PdfRenderMode.Line));
        }

        // A single non-JPEG image (PNG from flate, JPEG 2000, CCITT, JBIG2) is rendered, not lifted:
        // pdfimages ignores Decode arrays and stencil semantics, so its output can be inverted.
        [TestCase(".jp2")]
        [TestCase(".png")]
        public void should_render_a_single_non_jpeg_image(string extension)
        {
            GivenRenderer();
            GivenPages(1, (dest, page) => new[] { WriteImage(dest, 1, 0, extension, new byte[] { 1 }) }, Image(1, "image", "icc", 8, 300));
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");

            Subject.Convert(pdf, Path.Combine(_dir, "vol.cbz")).Status.Should().Be(PdfConvertStatus.Converted);

            _renders.Should().Equal((1, 300, PdfRenderMode.Color));
        }

        // Berserk 43 (2026-10-19): page 184 is a JPEG with the English lettering drawn over it as vector
        // shapes, page 212 a JPEG the page clips and covers. The lone JPEG is lifted only when the page,
        // drawn small, looks like it (difference <= 20 of 255; matching pages measured 9-15, those 30-60).
        [TestCase(15.0, false)]
        [TestCase(20.0, false)]
        [TestCase(35.0, true)]
        [TestCase(-1.0, true)]
        public void a_lone_jpeg_is_lifted_only_when_the_page_looks_like_it(double difference, bool rendered)
        {
            GivenRenderer();
            GivenPages(1, (dest, page) => new[] { WriteImage(dest, 1, 0, ".jpg", new byte[] { 1 }) }, Image(1, "image", "gray", 8, 1200));
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.PageDifference(It.IsAny<string>(), 1, It.IsAny<string>())).Returns(difference);
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");
            var cbz = Path.Combine(_dir, "vol.cbz");

            Subject.Convert(pdf, cbz).Status.Should().Be(PdfConvertStatus.Converted);

            _renders.Should().Equal(rendered ? new[] { (1, 600, PdfRenderMode.Gray) } : new (int, int, PdfRenderMode)[0]);
            using var zip = ZipFile.OpenRead(cbz);
            FirstByte(zip, "0001.jpg").Should().Be(rendered ? (byte)101 : (byte)1);
        }

        [TestCase(300, 300)]
        [TestCase(1200, 600)]
        [TestCase(40, 72)]
        public void the_render_resolution_is_the_median_image_ppi_within_bounds(int ppi, int expected)
        {
            PdfToCbzConverter.RenderDpi(new List<PdfImageInfo> { Image(1, "image", "icc", 8, ppi), Image(2, "smask", "gray", 8, 72) }).Should().Be(expected);
        }

        [Test]
        public void the_render_resolution_falls_back_to_300_when_no_page_has_an_image()
        {
            PdfToCbzConverter.RenderDpi(new List<PdfImageInfo>()).Should().Be(300);
        }

        [Test]
        public void a_pdf_with_no_pages_is_not_convertible()
        {
            GivenPages(0, (dest, page) => new string[0]);
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");

            var result = Subject.Convert(pdf, Path.Combine(_dir, "vol.cbz"));

            result.Status.Should().Be(PdfConvertStatus.Skipped);
            result.NotConvertible.Should().BeTrue();
        }

        [Test]
        public void should_not_clobber_an_existing_cbz()
        {
            GivenPdf(2, new byte[] { 0xFF, 0xD8 });
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");
            var cbz = Path.Combine(_dir, "vol.cbz");
            File.WriteAllText(cbz, "existing cbz");

            var result = Subject.Convert(pdf, cbz);

            result.Status.Should().Be(PdfConvertStatus.Skipped);
            File.ReadAllText(cbz).Should().Be("existing cbz"); // untouched
        }

        // Dandadan Vol. 19 (2026-10-17): a PDF tracked beside its own CBZ was extracted and rebuilt in full every
        // night before the existing CBZ was noticed. The existing CBZ is checked before any extraction.
        [Test]
        public void should_not_extract_anything_when_the_cbz_already_exists()
        {
            GivenPdf(2, new byte[] { 0xFF, 0xD8 });
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");
            var cbz = Path.Combine(_dir, "vol.cbz");
            File.WriteAllText(cbz, "existing cbz");

            Subject.Convert(pdf, cbz).Status.Should().Be(PdfConvertStatus.Skipped);

            Mocker.GetMock<IPdfImageExtractor>().Verify(e => e.ExtractImagesByPage(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_leave_no_work_dir_or_temp_behind()
        {
            GivenPdf(2, new byte[] { 0xFF, 0xD8 });
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");
            Subject.Convert(pdf, Path.Combine(_dir, "vol.cbz"));

            Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName)
                .Should().NotContain(f => f.Contains("mangarr-pdfwork") || f.EndsWith(".mangarr-tmp"));
        }
    }
}
