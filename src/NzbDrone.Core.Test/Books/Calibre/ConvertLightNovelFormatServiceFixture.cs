using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.MediaFiles;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.BookTests.Calibre
{
    // Design D3 (backfill on request, never automatic): the command converts what already exists to
    // the configured delivery format, skipping a book that already has it and never touching audio
    // or manga files -- only a light-novel ebook BookFile calibre already owns.
    [TestFixture]
    public class ConvertLightNovelFormatServiceFixture : CoreTest<ConvertLightNovelFormatService>
    {
        private CalibreSettings _settings;

        [SetUp]
        public void Setup()
        {
            LightNovelStorageMock.Setup(Mocker.GetMock<ILightNovelStorage>());

            _settings = new CalibreSettings { Host = "calibre.test", Port = 8081, Library = "books" };

            Mocker.GetMock<ILightNovelCalibreSettings>()
                .Setup(s => s.ForConfig())
                .Returns(_settings);

            Mocker.GetMock<ICalibreProxy>()
                .Setup(c => c.ConvertToFormatAndWait(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CalibreSettings>(), It.IsAny<TimeSpan>()))
                .Returns(true);
        }

        private void GivenPreferredFormat(string format)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.PreferredLightNovelFormat).Returns(format);
        }

        private static Author GivenAuthor(int id, LibraryType library)
        {
            var foreignId = library == LibraryType.LightNovel ? "local-konosuba~ln" : "local-konosuba";

            return new Author
            {
                Id = id,
                Metadata = new AuthorMetadata { ForeignAuthorId = foreignId }
            };
        }

        private static BookFile GivenEbookFile(int id, string path, int calibreId, FileHome home = FileHome.Calibre)
        {
            return new BookFile
            {
                Id = id,
                Path = path,
                CalibreId = calibreId,
                Home = home,
                Quality = new QualityModel(Quality.EPUB),
                Edition = new Edition { MediaType = MediaType.Ebook }
            };
        }

        private static BookFile GivenAudioFile(int id, string path)
        {
            return new BookFile
            {
                Id = id,
                Path = path,
                CalibreId = 0,
                Home = FileHome.Audiobooks,
                Quality = new QualityModel(Quality.M4B),
                Edition = new Edition { MediaType = MediaType.Audio }
            };
        }

        private void GivenBookFormats(int calibreId, params string[] formats)
        {
            var formatMetadata = new Dictionary<string, CalibreBookFormat>();

            foreach (var format in formats)
            {
                formatMetadata[format] = new CalibreBookFormat { Path = $"/books/x.{format.ToLowerInvariant()}" };
            }

            Mocker.GetMock<ICalibreProxy>()
                .Setup(c => c.GetBook(calibreId, _settings))
                .Returns(new CalibreBook { Id = calibreId, Formats = formatMetadata });
        }

        [Test]
        public void should_not_call_calibre_when_the_setting_is_epub()
        {
            GivenPreferredFormat("epub");

            var command = new ConvertLightNovelFormatCommand();

            Subject.Execute(command);

            command.ResultMessage.Should().Be("Delivery format is EPUB — nothing to convert");

            Mocker.GetMock<IAuthorService>().Verify(a => a.GetAllAuthors(), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(c => c.GetBook(It.IsAny<int>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(c => c.ConvertToFormatAndWait(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CalibreSettings>(), It.IsAny<TimeSpan>()), Times.Never());
        }

        [Test]
        public void should_skip_a_book_that_already_has_the_format()
        {
            GivenPreferredFormat("azw3");

            var author = GivenAuthor(5, LibraryType.LightNovel);
            var file = GivenEbookFile(1, "/books/Konosuba/Vol 1.epub", 7);

            Mocker.GetMock<IAuthorService>().Setup(a => a.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesByAuthor(5)).Returns(new List<BookFile> { file });

            GivenBookFormats(7, "EPUB", "AZW3");

            var command = new ConvertLightNovelFormatCommand();

            Subject.Execute(command);

            Mocker.GetMock<ICalibreProxy>().Verify(c => c.ConvertToFormatAndWait(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CalibreSettings>(), It.IsAny<TimeSpan>()), Times.Never());
            command.ResultMessage.Should().Be("Converted 0 book(s) to AZW3, 1 already had it, 0 failed");
        }

        [Test]
        public void should_convert_a_book_without_the_format_using_the_tracked_format_as_input()
        {
            GivenPreferredFormat("azw3");

            var author = GivenAuthor(5, LibraryType.LightNovel);
            var file = GivenEbookFile(1, "/books/Konosuba/Vol 1.epub", 7);

            Mocker.GetMock<IAuthorService>().Setup(a => a.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesByAuthor(5)).Returns(new List<BookFile> { file });

            GivenBookFormats(7, "EPUB");

            var command = new ConvertLightNovelFormatCommand();

            Subject.Execute(command);

            Mocker.GetMock<ICalibreProxy>().Verify(c => c.ConvertToFormatAndWait(7, "EPUB", "AZW3", _settings, TimeSpan.FromMinutes(10)), Times.Once());
            command.ResultMessage.Should().Be("Converted 1 book(s) to AZW3, 0 already had it, 0 failed");
        }

        // Fix round 1 (2026-09-22): the input format comes from what calibre currently tracks the
        // book by, not the BookFile row's own (possibly stale) extension -- a row still pointing at
        // a .pdf while calibre already holds an EPUB (and the discarded PDF beside it) converts from
        // the EPUB, never the PDF.
        [Test]
        public void should_convert_from_the_tracked_format_not_the_stale_row_extension()
        {
            GivenPreferredFormat("azw3");

            var author = GivenAuthor(5, LibraryType.LightNovel);
            var file = GivenEbookFile(1, "/books/Konosuba/Vol 1.pdf", 7);

            Mocker.GetMock<IAuthorService>().Setup(a => a.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesByAuthor(5)).Returns(new List<BookFile> { file });

            GivenBookFormats(7, "EPUB", "PDF");

            var command = new ConvertLightNovelFormatCommand();

            Subject.Execute(command);

            Mocker.GetMock<ICalibreProxy>().Verify(c => c.ConvertToFormatAndWait(7, "EPUB", "AZW3", _settings, TimeSpan.FromMinutes(10)), Times.Once());
            command.ResultMessage.Should().Be("Converted 1 book(s) to AZW3, 0 already had it, 0 failed");
        }

        // Two BookFile rows sharing a calibre id (a pack's parts, a duplicate row) must not queue
        // the same async conversion job twice while the first is still running.
        [Test]
        public void should_convert_a_calibre_book_shared_by_two_files_only_once()
        {
            GivenPreferredFormat("azw3");

            var author = GivenAuthor(5, LibraryType.LightNovel);
            var firstFile = GivenEbookFile(1, "/books/Konosuba/Vol 1a.epub", 7);
            var secondFile = GivenEbookFile(2, "/books/Konosuba/Vol 1b.epub", 7);

            Mocker.GetMock<IAuthorService>().Setup(a => a.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesByAuthor(5)).Returns(new List<BookFile> { firstFile, secondFile });

            GivenBookFormats(7, "EPUB");

            var command = new ConvertLightNovelFormatCommand();

            Subject.Execute(command);

            Mocker.GetMock<ICalibreProxy>().Verify(c => c.GetBook(7, _settings), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(c => c.ConvertToFormatAndWait(7, "EPUB", "AZW3", _settings, TimeSpan.FromMinutes(10)), Times.Once());
            command.ResultMessage.Should().Be("Converted 1 book(s) to AZW3, 0 already had it, 0 failed");
        }

        [Test]
        public void should_never_touch_audio_or_manga_files()
        {
            GivenPreferredFormat("azw3");

            var lightNovelAuthor = GivenAuthor(5, LibraryType.LightNovel);
            var mangaAuthor = GivenAuthor(6, LibraryType.Manga);

            var ebookFile = GivenEbookFile(1, "/books/Konosuba/Vol 1.epub", 7);
            var audioFile = GivenAudioFile(2, "/srv/audiobooks/Konosuba/Vol 1.m4b");
            var mangaFile = GivenEbookFile(3, "/manga/Overlord/Vol 1.cbz", 9);

            Mocker.GetMock<IAuthorService>().Setup(a => a.GetAllAuthors()).Returns(new List<Author> { lightNovelAuthor, mangaAuthor });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesByAuthor(5)).Returns(new List<BookFile> { ebookFile, audioFile });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesByAuthor(6)).Returns(new List<BookFile> { mangaFile });

            GivenBookFormats(7, "EPUB");

            var command = new ConvertLightNovelFormatCommand();

            Subject.Execute(command);

            // Only the light-novel ebook file's calibre book is ever looked up or converted.
            Mocker.GetMock<ICalibreProxy>().Verify(c => c.GetBook(7, _settings), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(c => c.GetBook(It.IsAny<int>(), It.IsAny<CalibreSettings>()), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(c => c.ConvertToFormatAndWait(7, "EPUB", "AZW3", _settings, TimeSpan.FromMinutes(10)), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(c => c.ConvertToFormatAndWait(9, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CalibreSettings>(), It.IsAny<TimeSpan>()), Times.Never());
            command.ResultMessage.Should().Be("Converted 1 book(s) to AZW3, 0 already had it, 0 failed");
        }

        [Test]
        public void should_count_a_throwing_conversion_and_continue_the_loop()
        {
            GivenPreferredFormat("azw3");

            var author = GivenAuthor(5, LibraryType.LightNovel);
            var failingFile = GivenEbookFile(1, "/books/Konosuba/Vol 1.epub", 7);
            var okFile = GivenEbookFile(2, "/books/Konosuba/Vol 2.epub", 8);

            Mocker.GetMock<IAuthorService>().Setup(a => a.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesByAuthor(5)).Returns(new List<BookFile> { failingFile, okFile });

            Mocker.GetMock<ICalibreProxy>()
                .Setup(c => c.GetBook(7, _settings))
                .Throws(new CalibreException("boom"));

            GivenBookFormats(8, "EPUB");

            var command = new ConvertLightNovelFormatCommand();

            Subject.Execute(command);

            Mocker.GetMock<ICalibreProxy>().Verify(c => c.ConvertToFormatAndWait(8, "EPUB", "AZW3", _settings, TimeSpan.FromMinutes(10)), Times.Once());
            command.ResultMessage.Should().Be("Converted 1 book(s) to AZW3, 0 already had it, 1 failed");
            ExceptionVerification.ExpectedWarns(1);
        }

        // Final review I2 (2026-09-22): the backfill runs calibre's jobs one at a time -- each book
        // through the waiting call, which returns only once calibre has finished (or given up on)
        // that book, in file order; never the import's fire-and-forget conversion.
        [Test]
        public void should_convert_one_book_at_a_time_through_the_waiting_call()
        {
            GivenPreferredFormat("kepub");

            var author = GivenAuthor(5, LibraryType.LightNovel);
            var files = new List<BookFile>
            {
                GivenEbookFile(1, "/books/Konosuba/Vol 1.epub", 7),
                GivenEbookFile(2, "/books/Konosuba/Vol 2.azw3", 8),
                GivenEbookFile(3, "/books/Konosuba/Vol 3.epub", 9)
            };

            Mocker.GetMock<IAuthorService>().Setup(a => a.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesByAuthor(5)).Returns(files);

            GivenBookFormats(7, "EPUB");
            GivenBookFormats(8, "AZW3", "KEPUB");
            GivenBookFormats(9, "EPUB");

            var calls = new List<string>();
            var running = 0;

            Mocker.GetMock<ICalibreProxy>()
                .Setup(c => c.ConvertToFormatAndWait(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CalibreSettings>(), It.IsAny<TimeSpan>()))
                .Callback<int, string, string, CalibreSettings, TimeSpan>((id, input, output, settings, timeout) =>
                {
                    running++;
                    running.Should().Be(1, "a conversion starts only after the previous one returned");
                    calls.Add($"{id}:{input}->{output}");
                    running--;
                })
                .Returns(true);

            var command = new ConvertLightNovelFormatCommand();

            Subject.Execute(command);

            calls.Should().Equal("7:EPUB->KEPUB", "9:EPUB->KEPUB");
            command.ResultMessage.Should().Be("Converted 2 book(s) to KEPUB, 1 already had it, 0 failed");
        }

        [Test]
        public void should_count_a_conversion_calibre_did_not_finish_as_failed_and_move_on()
        {
            GivenPreferredFormat("azw3");

            var author = GivenAuthor(5, LibraryType.LightNovel);
            var slowFile = GivenEbookFile(1, "/books/Konosuba/Vol 1.epub", 7);
            var okFile = GivenEbookFile(2, "/books/Konosuba/Vol 2.epub", 8);

            Mocker.GetMock<IAuthorService>().Setup(a => a.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesByAuthor(5)).Returns(new List<BookFile> { slowFile, okFile });

            GivenBookFormats(7, "EPUB");
            GivenBookFormats(8, "EPUB");

            Mocker.GetMock<ICalibreProxy>()
                .Setup(c => c.ConvertToFormatAndWait(7, "EPUB", "AZW3", _settings, It.IsAny<TimeSpan>()))
                .Returns(false);

            var command = new ConvertLightNovelFormatCommand();

            Subject.Execute(command);

            Mocker.GetMock<ICalibreProxy>().Verify(c => c.ConvertToFormatAndWait(8, "EPUB", "AZW3", _settings, TimeSpan.FromMinutes(10)), Times.Once());
            command.ResultMessage.Should().Be("Converted 1 book(s) to AZW3, 0 already had it, 1 failed");
        }

        [Test]
        public void nothing_converts_while_ebooks_go_to_the_entry_folder()
        {
            GivenPreferredFormat("azw3");
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Entry);
            var command = new ConvertLightNovelFormatCommand();

            Subject.Execute(command);

            command.ResultMessage.Should().Contain("entry folder");
            Mocker.GetMock<ILightNovelCalibreSettings>().Verify(v => v.ForConfig(), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.ConvertToFormatAndWait(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CalibreSettings>(), It.IsAny<TimeSpan>()), Times.Never());
        }

        // LN PDF (2026-09-22): calibre's PDF conversion makes poor ebooks -- a book tracked by its
        // PDF (no EPUB or AZW3 in calibre) is skipped and reported as skipped, not failed.
        [Test]
        public void should_skip_a_book_tracked_by_its_pdf()
        {
            GivenPreferredFormat("azw3");

            var author = GivenAuthor(5, LibraryType.LightNovel);
            var file = GivenEbookFile(1, "/books/Konosuba/Vol 1.pdf", 7);

            Mocker.GetMock<IAuthorService>().Setup(a => a.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesByAuthor(5)).Returns(new List<BookFile> { file });

            GivenBookFormats(7, "PDF");

            var command = new ConvertLightNovelFormatCommand();

            Subject.Execute(command);

            Mocker.GetMock<ICalibreProxy>().Verify(c => c.ConvertToFormatAndWait(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CalibreSettings>(), It.IsAny<TimeSpan>()), Times.Never());
            command.ResultMessage.Should().Be("Converted 0 book(s) to AZW3, 0 already had it, 1 skipped (PDF only), 0 failed");
        }

        // Final review round (2026-09-22): calibre holds none of EPUB/AZW3/PDF for this book (say,
        // only MOBI) -- there is nothing to convert FROM, so it fails outright and never reaches
        // ConvertToFormatAndWait with a null input format.
        [Test]
        public void should_fail_a_book_calibre_holds_none_of_epub_azw3_or_pdf_for()
        {
            GivenPreferredFormat("azw3");

            var author = GivenAuthor(5, LibraryType.LightNovel);
            var file = GivenEbookFile(1, "/books/Konosuba/Vol 1.mobi", 7);

            Mocker.GetMock<IAuthorService>().Setup(a => a.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IMediaFileService>().Setup(m => m.GetFilesByAuthor(5)).Returns(new List<BookFile> { file });

            GivenBookFormats(7, "MOBI");

            var command = new ConvertLightNovelFormatCommand();

            Subject.Execute(command);

            Mocker.GetMock<ICalibreProxy>().Verify(c => c.ConvertToFormatAndWait(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CalibreSettings>(), It.IsAny<TimeSpan>()), Times.Never());
            command.ResultMessage.Should().Be("Converted 0 book(s) to AZW3, 0 already had it, 1 failed");
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
