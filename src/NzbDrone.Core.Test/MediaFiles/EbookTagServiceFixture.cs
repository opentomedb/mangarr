using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common.AutoMoq;
using VersOne.Epub.Schema;

namespace NzbDrone.Core.Test.MediaFiles.AudioTagServiceFixture
{
    [TestFixture]
    public class EbookTagServiceFixture : CoreTest<EBookTagService>
    {
        [Test]
        public void should_prefer_isbn13()
        {
            var ids = Builder<EpubMetadataIdentifier>
                .CreateListOfSize(2)
                .TheFirst(1)
                .With(x => x.Identifier = "4087738574")
                .TheNext(1)
                .With(x => x.Identifier = "9781455546176")
                .Build()
                .ToList();

            Subject.GetIsbn(ids).Should().Be("9781455546176");
        }

        [Test]
        public void write_tags_does_not_reach_calibre_for_a_file_without_a_calibre_id()
        {
            // Light novels (2026-09): EPUBs in a plain root folder have no Calibre id; the
            // Calibre integration stays unused for them (D7).
            var file = new BookFile { Path = "/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005.epub", CalibreId = 0 };

            Subject.WriteTags(file, true, true);

            Mocker.GetMock<IRootFolderService>().Verify(v => v.GetBestRootFolder(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.SetFields(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void read_tags_grades_an_epub_as_epub()
        {
            // Light novels (2026-09): the fork renumbered quality id 3 from EPUB to CBR and ReadEpub
            // kept seeding it, so every scanned EPUB was graded CBR -- a quality the Light Novel
            // EPUB profile does not allow (cutoff never met, endless re-grabs).
            var path = Path.Combine(TempFolder, "Overlord - Vol 005.epub");
            WriteMinimalEpub(path);

            var tags = Subject.ReadTags(Mocker.Resolve<IDiskProvider>(FileSystemType.Actual).GetFileInfo(path));

            tags.Quality.Quality.Should().Be(Quality.EPUB);
            tags.Quality.QualityDetectionSource.Should().Be(QualityDetectionSource.TagLib);
            tags.BookTitle.Should().Be("Overlord, Vol. 5");
            tags.Authors.Should().Equal("Kugane Maruyama");
        }

        // AZW3 (2026-09, id 7): the pure quality decision ReadAzw3 makes for the two extensions it
        // is ever called with (see ReadTags's switch). A real .azw3/.mobi is a binary MOBI
        // container with no lightweight fixture-file equivalent to WriteMinimalEpub, so the
        // decision is exercised directly rather than through a constructed file. Quality is not a
        // compile-time constant, so cases are (extension, version, expected id) triples.
        [TestCase(".azw3", 6u, 7)]
        [TestCase(".azw3", 8u, 7)]
        [TestCase(".AZW3", 8u, 7)]
        [TestCase(".mobi", 6u, 2)]
        [TestCase(".mobi", 8u, 4)]
        public void quality_of_kindle_file_grades_azw3_by_extension_and_mobi_by_legacy_version(string extension, uint version, int expectedQualityId)
        {
            EBookTagService.QualityOfKindleFile(extension, version).Id.Should().Be(expectedQualityId);
        }

        // The vendored reader needs only the container and a package with metadata.
        private static void WriteMinimalEpub(string path)
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("META-INF/container.xml").Open()))
                {
                    writer.Write("<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");
                }

                using (var writer = new StreamWriter(zip.CreateEntry("content.opf").Open()))
                {
                    writer.Write("<?xml version=\"1.0\"?><package version=\"3.0\" xmlns=\"http://www.idpf.org/2007/opf\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>Overlord, Vol. 5</dc:title><dc:creator>Kugane Maruyama</dc:creator><dc:language>en</dc:language></metadata></package>");
                }
            }
        }

        // One copy each (2026-09-20): a Calibre-homed row (a light-novel EPUB in calibre's own
        // library) is written through the config settings, never a root folder resolved from its
        // path (there is none under /books); an adopted original is never written into.
        private CalibreSettings _config;
        private CalibreSettings _rootSettings;

        private BookFile GivenCalibreHomedFile(bool adopted, int calibreId = 12)
        {
            _config = new CalibreSettings { Host = "calibre.test", Port = 8081, Library = "books" };

            var file = GivenLinkedFile(calibreId, FileHome.Calibre, adopted, "/books/Reki Kawahara/Sword Art Online 2 (12)/Sword Art Online 2 - Reki Kawahara.epub");

            Mocker.GetMock<ILightNovelCalibreSettings>()
                .Setup(x => x.For(file))
                .Returns(_config);

            Mocker.GetMock<ILightNovelCalibreSettings>()
                .Setup(x => x.ForConfig())
                .Returns(_config);

            return file;
        }

        private BookFile GivenStockCalibreRootFile(int calibreId = 5)
        {
            _rootSettings = new CalibreSettings { Host = "stock.test", Port = 8080, Library = "library" };

            var file = GivenLinkedFile(calibreId, FileHome.Entry, false, "/calibre/Author/Book (5)/Book - Author.epub");

            Mocker.GetMock<ILightNovelCalibreSettings>()
                .Setup(x => x.For(file))
                .Returns(_rootSettings);

            Mocker.GetMock<IRootFolderService>()
                .Setup(x => x.GetBestRootFolder(file.Path))
                .Returns(new RootFolder { Path = "/calibre", IsCalibreLibrary = true, CalibreSettings = _rootSettings });

            return file;
        }

        // enough of the edition / book / author graph for the retag preview to diff
        private BookFile GivenLinkedFile(int calibreId, FileHome home, bool adopted, string path)
        {
            var author = new Author { Id = 1, Name = "Sword Art Online" };
            var book = new Book { Id = 2, Title = "Sword Art Online, Vol. 2", SeriesLinks = new List<SeriesBookLink>() };
            var edition = new Edition { Id = 3, Title = "Sword Art Online, Vol. 2", Book = book, Language = "eng" };

            return new BookFile
            {
                Id = calibreId * 10,
                Path = path,
                CalibreId = calibreId,
                Home = home,
                Adopted = adopted,
                Author = author,
                Edition = edition
            };
        }

        private static CalibreBook GivenCalibreBook(int id)
        {
            return new CalibreBook
            {
                Id = id,
                Title = "old title",
                Authors = new List<string> { "Reki Kawahara" },
                Languages = new List<string> { "eng" },
                Tags = new List<string>(),
                Identifiers = new Dictionary<string, string>()
            };
        }

        [Test]
        public void write_tags_never_writes_into_an_adopted_calibre_file()
        {
            var file = GivenCalibreHomedFile(adopted: true);

            Subject.WriteTags(file, true, true);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.SetFields(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IRootFolderService>().Verify(v => v.GetBestRootFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void write_tags_writes_a_grabbed_calibre_file_through_the_config_settings_without_the_author()
        {
            Mocker.GetMock<IConfigService>().Setup(x => x.UpdateCovers).Returns(true);
            Mocker.GetMock<IConfigService>().Setup(x => x.EmbedMetadata).Returns(false);

            var file = GivenCalibreHomedFile(adopted: false);

            Subject.WriteTags(file, true, true);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.SetFields(file, _config, true, false, false), Times.Once());
            Mocker.GetMock<IRootFolderService>().Verify(v => v.GetBestRootFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void write_tags_writes_a_stock_calibre_root_file_through_its_root_folder_settings_with_the_author()
        {
            var file = GivenStockCalibreRootFile();

            Subject.WriteTags(file, true, true);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.SetFields(file, _rootSettings, It.IsAny<bool>(), It.IsAny<bool>(), true), Times.Once());
        }

        [Test]
        public void write_tags_throws_for_a_calibre_id_with_no_calibre_settings()
        {
            var file = GivenLinkedFile(5, FileHome.Entry, false, "/elsewhere/Book.epub");

            Mocker.GetMock<ILightNovelCalibreSettings>()
                .Setup(x => x.For(file))
                .Returns((CalibreSettings)null);

            Assert.Throws<Exception>(() => Subject.WriteTags(file, true, true));

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.SetFields(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void retag_previews_read_calibre_homed_rows_through_the_config_settings()
        {
            var homed = GivenCalibreHomedFile(adopted: false, calibreId: 12);
            var stock = GivenStockCalibreRootFile(calibreId: 5);

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(1))
                .Returns(new List<BookFile> { homed, stock });

            Mocker.GetMock<ICalibreProxy>()
                .Setup(x => x.GetBooks(It.Is<List<int>>(l => l.SequenceEqual(new[] { 12 })), _config))
                .Returns(new List<CalibreBook> { GivenCalibreBook(12) });

            Mocker.GetMock<ICalibreProxy>()
                .Setup(x => x.GetBooks(It.Is<List<int>>(l => l.SequenceEqual(new[] { 5 })), _rootSettings))
                .Returns(new List<CalibreBook> { GivenCalibreBook(5) });

            var previews = Subject.GetRetagPreviewsByAuthor(1);

            previews.Select(x => x.BookFileId).Should().BeEquivalentTo(new[] { homed.Id, stock.Id });
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBooks(It.IsAny<List<int>>(), _config), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBooks(It.IsAny<List<int>>(), _rootSettings), Times.Once());
            Mocker.GetMock<IRootFolderService>().Verify(v => v.GetBestRootFolder(homed.Path), Times.Never());
        }

        // Final review (T5 tied): the write never re-sends a Calibre-homed row's author, so the
        // preview shows no Author line for it; a stock calibre root's row still does.
        [Test]
        public void retag_previews_show_no_author_line_for_a_calibre_homed_row()
        {
            var homed = GivenCalibreHomedFile(adopted: false, calibreId: 12);
            var stock = GivenStockCalibreRootFile(calibreId: 5);

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(1))
                .Returns(new List<BookFile> { homed, stock });

            Mocker.GetMock<ICalibreProxy>()
                .Setup(x => x.GetBooks(It.Is<List<int>>(l => l.SequenceEqual(new[] { 12 })), _config))
                .Returns(new List<CalibreBook> { GivenCalibreBook(12) });

            Mocker.GetMock<ICalibreProxy>()
                .Setup(x => x.GetBooks(It.Is<List<int>>(l => l.SequenceEqual(new[] { 5 })), _rootSettings))
                .Returns(new List<CalibreBook> { GivenCalibreBook(5) });

            var previews = Subject.GetRetagPreviewsByAuthor(1);

            previews.Single(p => p.BookFileId == homed.Id).Changes.Should().NotContainKey("Author");
            previews.Single(p => p.BookFileId == stock.Id).Changes.Should().ContainKey("Author");
        }

        // ... and mirrors the series the write sends for a light novel (CalibreProxy.SetFields,
        // final review I1): the entry name and the volume number, not the empty series links.
        [Test]
        public void retag_previews_show_the_entry_name_and_volume_number_as_the_series_of_a_light_novel()
        {
            var homed = GivenCalibreHomedFile(adopted: false, calibreId: 12);
            homed.Author.Value.Metadata.Value.ForeignAuthorId = "local-sword-art-online~ln";
            homed.Edition.Value.Book.Value.VolumeNumber = 2;

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(1))
                .Returns(new List<BookFile> { homed });

            var inCalibre = GivenCalibreBook(12);
            inCalibre.Series = "Sword Art Online";
            inCalibre.Position = 1;

            Mocker.GetMock<ICalibreProxy>()
                .Setup(x => x.GetBooks(It.Is<List<int>>(l => l.SequenceEqual(new[] { 12 })), _config))
                .Returns(new List<CalibreBook> { inCalibre });

            var changes = Subject.GetRetagPreviewsByAuthor(1).Single().Changes;

            changes.Should().NotContainKey("Series");
            changes["Series Index"].Item2.Should().Be("2");
        }

        [Test]
        public void retag_previews_leave_out_adopted_calibre_rows()
        {
            var adopted = GivenCalibreHomedFile(adopted: true, calibreId: 13);
            var grabbed = GivenCalibreHomedFile(adopted: false, calibreId: 12);

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(1))
                .Returns(new List<BookFile> { adopted, grabbed });

            Mocker.GetMock<ICalibreProxy>()
                .Setup(x => x.GetBooks(It.Is<List<int>>(l => l.SequenceEqual(new[] { 12 })), _config))
                .Returns(new List<CalibreBook> { GivenCalibreBook(12) });

            var previews = Subject.GetRetagPreviewsByAuthor(1);

            previews.Select(x => x.BookFileId).Should().BeEquivalentTo(new[] { grabbed.Id });
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBooks(It.Is<List<int>>(l => l.Contains(13)), It.IsAny<CalibreSettings>()), Times.Never());
        }
    }
}
