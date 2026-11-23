using System.IO;
using System.IO.Compression;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.MediaFiles.PdfConversion;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    // Page-count check (2026-11-07): a cut-short volume file is refused; an unknown count never is.
    // The junk check (2026-11-14): a .cbz/.zip that is not a zip, or holds no pages, is refused outright.
    [TestFixture]
    public class PageCountSpecificationFixture : CoreTest<PageCountSpecification>
    {
        private string _dir;

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "mangarr-pagecount-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_dir, true);
        }

        private string Cbz(int pages, string name = "Sample Saga v01.cbz")
        {
            var path = Path.Combine(_dir, name);

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                for (var i = 1; i <= pages; i++)
                {
                    using var entry = zip.CreateEntry($"{i:000}.jpg").Open();
                    entry.WriteByte(0xFF);
                }

                zip.CreateEntry("ComicInfo.xml");
                zip.CreateEntry("__MACOSX/._001.jpg");
                zip.CreateEntry("000.jpg"); // zero-length: not a page
            }

            return path;
        }

        private static LocalBook Local(string path, int listed, LibraryType library = LibraryType.Manga, Quality quality = null, bool existing = false)
        {
            return new LocalBook
            {
                Path = path,
                Author = new Author { Metadata = new AuthorMetadata { Name = "Sample Saga", ForeignAuthorId = library == LibraryType.LightNovel ? "local-x~ln" : "local-x" } },
                Book = new Book { VolumeNumber = 1, Title = "Sample Saga Vol. 1" },
                Edition = new Edition { PageCount = listed, MediaType = MediaType.Archive },
                Quality = new QualityModel(quality ?? Quality.CBZ),
                ExistingFile = existing
            };
        }

        [Test]
        public void a_cut_short_archive_is_refused()
        {
            var decision = Subject.IsSatisfiedBy(Local(Cbz(112), 192), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("112").And.Contain("192");
        }

        [TestCase(179, 192)]
        [TestCase(128, 192)]
        [TestCase(200, 192)]
        [TestCase(20, 30)]
        public void an_archive_near_the_listed_count_passes(int held, int listed)
        {
            Subject.IsSatisfiedBy(Local(Cbz(held), listed), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void two_thirds_exactly_passes_and_one_page_fewer_does_not()
        {
            Subject.IsSatisfiedBy(Local(Cbz(128, "Sample Saga v01.cbz"), 192), null).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(Local(Cbz(127, "Sample Saga v02.cbz"), 192), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void no_catalogue_count_passes()
        {
            Subject.IsSatisfiedBy(Local(Cbz(10), 0), null).Accepted.Should().BeTrue();
        }

        // The junk check (2026-11-14): a .cbz or .zip that is not a zip archive at all, or holds no page
        // images, is never a usable volume and is refused whatever the catalogue lists.
        [Test]
        public void a_cbz_that_is_not_a_zip_is_refused()
        {
            var path = Path.Combine(_dir, "Sample Saga v01.cbz");
            File.WriteAllText(path, "not a zip");

            var decision = Subject.IsSatisfiedBy(Local(path, 192), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("not a readable archive");
        }

        [Test]
        public void an_archive_without_pages_is_refused()
        {
            var decision = Subject.IsSatisfiedBy(Local(Cbz(0), 192), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("no pages");
        }

        [Test]
        public void junk_is_refused_even_without_a_catalogue_count()
        {
            var path = Path.Combine(_dir, "Sample Saga v01.cbz");
            File.WriteAllText(path, "~^WtF[nZb]~");

            Subject.IsSatisfiedBy(Local(path, 0), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void junk_already_in_the_library_passes()
        {
            var path = Path.Combine(_dir, "Sample Saga v01.cbz");
            File.WriteAllText(path, "not a zip");

            Subject.IsSatisfiedBy(Local(path, 192, existing: true), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_light_novel_zip_without_pages_passes()
        {
            Subject.IsSatisfiedBy(Local(Cbz(0, "Sample Saga v01.zip"), 192, LibraryType.LightNovel, Quality.ZIP), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_light_novel_cbz_that_is_not_a_zip_is_refused()
        {
            var path = Path.Combine(_dir, "Sample Saga v01.cbz");
            File.WriteAllText(path, "not a zip");

            Subject.IsSatisfiedBy(Local(path, 192, LibraryType.LightNovel), null).Accepted.Should().BeFalse();
        }

        [TestCase(9)]
        [TestCase(14)]
        public void a_stand_in_passes(int qualityId)
        {
            Subject.IsSatisfiedBy(Local(Cbz(10), 192, quality: Quality.FindById(qualityId)), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_file_already_in_the_library_passes()
        {
            Subject.IsSatisfiedBy(Local(Cbz(10), 192, existing: true), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_light_novel_file_passes()
        {
            Subject.IsSatisfiedBy(Local(Cbz(10), 192, LibraryType.LightNovel), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_cut_short_pdf_is_refused_by_its_page_count()
        {
            var path = Path.Combine(_dir, "Sample Saga v01.pdf");
            File.WriteAllText(path, "%PDF-");
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.GetPageCount(path)).Returns(112);

            Subject.IsSatisfiedBy(Local(path, 192), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void a_pdf_that_cannot_be_counted_passes()
        {
            var path = Path.Combine(_dir, "Sample Saga v01.pdf");
            File.WriteAllText(path, "%PDF-");
            Mocker.GetMock<IPdfImageExtractor>().Setup(e => e.GetPageCount(path)).Returns(0);

            Subject.IsSatisfiedBy(Local(path, 192), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void another_archive_kind_is_not_counted()
        {
            var path = Path.Combine(_dir, "Sample Saga v01.cbr");
            File.WriteAllText(path, "Rar!");

            Subject.IsSatisfiedBy(Local(path, 192), null).Accepted.Should().BeTrue();
            Mocker.GetMock<IPdfImageExtractor>().Verify(e => e.GetPageCount(It.IsAny<string>()), Times.Never());
        }
    }
}
