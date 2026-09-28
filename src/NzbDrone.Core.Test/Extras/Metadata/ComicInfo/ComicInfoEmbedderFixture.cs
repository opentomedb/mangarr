using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Metadata.ComicInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Extras.Metadata.ComicInfo
{
    [TestFixture]
    public class ComicInfoEmbedderFixture : CoreTest<ComicInfoEmbedder>
    {
        private string _dir;

        // Smallest valid PNG (1x1). Real binary so the byte-preservation assertions mean something.
        private static readonly byte[] Png =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        };

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "mangarr-comicinfo-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);

            // Delegate the disk primitives the embedder uses to the real filesystem.
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }

        private string MakeCbz(string name, string existingComicInfo = null)
        {
            var path = Path.Combine(_dir, name);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

            // A couple of pages, one in a subfolder, to mirror real scanlation archives.
            foreach (var page in new[] { "001.png", "002.png", "pages/003.png" })
            {
                var e = zip.CreateEntry(page, CompressionLevel.NoCompression);
                using var s = e.Open();
                s.Write(Png, 0, Png.Length);
            }

            if (existingComicInfo != null)
            {
                var e = zip.CreateEntry("ComicInfo.xml");
                using var s = e.Open();
                var b = Encoding.UTF8.GetBytes(existingComicInfo);
                s.Write(b, 0, b.Length);
            }

            return path;
        }

        private ComicInfoFields Fields() => new ComicInfoFields
        {
            Series = "Chainsaw Man", Title = "Chainsaw Man Vol. 16", Number = "16",
            Summary = "Denji vs the future.", Year = 2024, Month = 5, Day = 7, PageCount = 192, LanguageIso = "en"
        };

        [Test]
        public void should_preserve_every_image_byte_for_byte()
        {
            var path = MakeCbz("Chainsaw Man - Vol 016.cbz");
            var xml = ComicInfoBuilder.Merge(null, Fields());

            var result = Subject.Embed(path, xml);

            result.Status.Should().Be(ComicInfoEmbedStatus.Written);

            using var zip = ZipFile.OpenRead(path);
            foreach (var page in new[] { "001.png", "002.png", "pages/003.png" })
            {
                var e = zip.GetEntry(page);
                e.Should().NotBeNull($"page {page} must survive");
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                ms.ToArray().Should().Equal(Png, $"page {page} bytes must be unchanged");
            }
        }

        [Test]
        public void should_write_readable_comicinfo()
        {
            var path = MakeCbz("v1.cbz");
            var xml = ComicInfoBuilder.Merge(null, Fields());

            Subject.Embed(path, xml);

            Subject.ReadExisting(path).Should().Contain("<Summary>Denji vs the future.</Summary>");
        }

        [Test]
        public void should_replace_not_duplicate_an_existing_comicinfo()
        {
            var path = MakeCbz("v1.cbz", "<ComicInfo><Manga>YesAndRightToLeft</Manga><Title>Old</Title></ComicInfo>");
            var merged = ComicInfoBuilder.Merge(Subject.ReadExisting(path), Fields());

            Subject.Embed(path, merged);

            using var zip = ZipFile.OpenRead(path);
            zip.Entries.Count(e => e.FullName.Equals("ComicInfo.xml", System.StringComparison.OrdinalIgnoreCase))
               .Should().Be(1);
            var read = Subject.ReadExisting(path);
            read.Should().Contain("YesAndRightToLeft");        // preserved
            read.Should().Contain("Chainsaw Man Vol. 16");     // updated
        }

        [Test]
        public void should_leave_no_temp_or_backup_files_behind()
        {
            var path = MakeCbz("v1.cbz");
            Subject.Embed(path, ComicInfoBuilder.Merge(null, Fields()));

            Directory.GetFiles(_dir).Select(Path.GetFileName)
                .Should().NotContain(f => f.EndsWith(".mangarr-tmp") || f.EndsWith(".mangarr-bak"));
        }

        [Test]
        public void should_skip_non_zip_formats()
        {
            var path = Path.Combine(_dir, "vol.cbr");
            File.WriteAllText(path, "not a zip");

            Subject.Embed(path, "<ComicInfo/>").Status.Should().Be(ComicInfoEmbedStatus.Skipped);
            File.ReadAllText(path).Should().Be("not a zip"); // untouched
        }

        [Test]
        public void should_leave_original_intact_when_source_is_corrupt()
        {
            var path = Path.Combine(_dir, "broken.cbz");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 }); // not a valid zip
            var before = File.ReadAllBytes(path);

            var result = Subject.Embed(path, "<ComicInfo/>");

            result.Status.Should().Be(ComicInfoEmbedStatus.Failed);
            File.ReadAllBytes(path).Should().Equal(before); // original untouched
            Directory.GetFiles(_dir).Should().HaveCount(1); // no temp/backup residue
        }
    }
}
