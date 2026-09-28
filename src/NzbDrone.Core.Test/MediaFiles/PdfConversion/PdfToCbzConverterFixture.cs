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

        // Fake extractor: reports N pages and drops N jpg files with known bytes into the work dir.
        private void GivenPdf(int pages, int imagesToExtract, byte[] pageBytes)
        {
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.GetPageCount(It.IsAny<string>())).Returns(pages);
            Mocker.GetMock<IPdfImageExtractor>()
                .Setup(e => e.ExtractImages(It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string>((pdf, dest) =>
                {
                    var files = new List<string>();
                    for (var i = 0; i < imagesToExtract; i++)
                    {
                        var p = Path.Combine(dest, $"page-{i:D3}.jpg");
                        File.WriteAllBytes(p, pageBytes.Concat(new[] { (byte)i }).ToArray());
                        files.Add(p);
                    }

                    return files;
                });
        }

        [Test]
        public void should_flip_page_order_for_a_reversed_pdf()
        {
            // Image-only reversed PDF: no text, but page 1 near-white (colophon), last page dark
            // (cover). page-000 carries byte 0, page-002 byte 2 (the cover).
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.GetPageCount(It.IsAny<string>())).Returns(3);
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.GetFirstPageText(It.IsAny<string>())).Returns((string)null);
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.GetPageBrightness(It.IsAny<string>(), 1)).Returns(0.96);
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.GetPageBrightness(It.IsAny<string>(), 3)).Returns(0.60);
            Mocker.GetMock<IPdfImageExtractor>()
                .Setup(e => e.ExtractImages(It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string>((pdf, dest) =>
                {
                    var files = new System.Collections.Generic.List<string>();
                    for (var i = 0; i < 3; i++)
                    {
                        var p = Path.Combine(dest, $"page-{i:D3}.jpg");
                        File.WriteAllBytes(p, new byte[] { (byte)i });
                        files.Add(p);
                    }

                    return files;
                });

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
            GivenPdf(pages: 3, imagesToExtract: 3, pageBytes: new byte[] { 0xFF, 0xD8, 0xFF });
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
            GivenPdf(2, 2, page);
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

        [Test]
        public void should_skip_when_image_count_does_not_match_page_count()
        {
            // 5 pages but only 3 images extracted -> not a clean 1:1 scan; refuse.
            GivenPdf(pages: 5, imagesToExtract: 3, pageBytes: new byte[] { 0xFF, 0xD8 });
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");
            var cbz = Path.Combine(_dir, "vol.cbz");

            var result = Subject.Convert(pdf, cbz);

            result.Status.Should().Be(PdfConvertStatus.Skipped);
            File.Exists(cbz).Should().BeFalse();     // nothing written
            File.Exists(pdf).Should().BeTrue();      // source untouched
        }

        [Test]
        public void should_not_clobber_an_existing_cbz()
        {
            GivenPdf(2, 2, new byte[] { 0xFF, 0xD8 });
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");
            var cbz = Path.Combine(_dir, "vol.cbz");
            File.WriteAllText(cbz, "existing cbz");

            var result = Subject.Convert(pdf, cbz);

            result.Status.Should().Be(PdfConvertStatus.Skipped);
            File.ReadAllText(cbz).Should().Be("existing cbz"); // untouched
        }

        [Test]
        public void should_leave_no_work_dir_or_temp_behind()
        {
            GivenPdf(2, 2, new byte[] { 0xFF, 0xD8 });
            var pdf = Path.Combine(_dir, "vol.pdf");
            File.WriteAllText(pdf, "pdf");
            Subject.Convert(pdf, Path.Combine(_dir, "vol.cbz"));

            Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName)
                .Should().NotContain(f => f.Contains("mangarr-pdfwork") || f.EndsWith(".mangarr-tmp"));
        }
    }
}
