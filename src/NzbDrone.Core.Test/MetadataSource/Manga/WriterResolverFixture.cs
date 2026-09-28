using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.MediaFiles;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    // One copy each (2026-09-20): the real-author ladder for a light novel. Pin, then the author of
    // a calibre-homed file the entry already tracks, then the catalogue's author (in calibre's own
    // spelling when calibre knows the person), else null. Every calibre call is mocked.
    [TestFixture]
    public class WriterResolverFixture : CoreTest<WriterResolver>
    {
        private const string PinKey = "Mushoku Tensei: Jobless Reincarnation (light novel)";

        private CalibreSettings _settings;
        private Author _entry;

        [SetUp]
        public void Setup()
        {
            LightNovelStorageMock.Setup(Mocker.GetMock<ILightNovelStorage>());

            _settings = new CalibreSettings { Host = "calibre.test", Port = 8081, Library = "books" };
            Mocker.GetMock<ILightNovelCalibreSettings>().Setup(s => s.ForConfig()).Returns(_settings);

            _entry = new Author
            {
                Id = 5,
                Metadata = new AuthorMetadata { Name = "Mushoku Tensei: Jobless Reincarnation", ForeignAuthorId = "local-mushoku-tensei~ln" }
            };

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByAuthor(_entry.Id))
                  .Returns(new List<BookFile>());

            Mocker.GetMock<ICalibreContentServerClient>()
                  .Setup(s => s.SearchAuthor(It.IsAny<string>()))
                  .Returns(new List<int>());
        }

        private void GivenPin(string author)
        {
            Mocker.GetMock<IMetadataOverridesService>()
                  .Setup(s => s.GetSeriesAuthor(PinKey))
                  .Returns(author);
        }

        private void GivenCalibreFile(int calibreId, params string[] authors)
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByAuthor(_entry.Id))
                  .Returns(new List<BookFile>
                  {
                      new BookFile { Id = 1, Home = FileHome.Entry },
                      new BookFile { Id = 2, Home = FileHome.Calibre, CalibreId = calibreId }
                  });

            Mocker.GetMock<ICalibreProxy>()
                  .Setup(s => s.GetBook(calibreId, _settings))
                  .Returns(new CalibreBook { Id = calibreId, Authors = new List<string>(authors) });
        }

        private void GivenCalibreKnowsAuthor(string query, int calibreId, string spelledAs)
        {
            Mocker.GetMock<ICalibreContentServerClient>()
                  .Setup(s => s.SearchAuthor(query))
                  .Returns(new List<int> { calibreId });

            Mocker.GetMock<ICalibreProxy>()
                  .Setup(s => s.GetBook(calibreId, _settings))
                  .Returns(new CalibreBook { Id = calibreId, Authors = new List<string> { spelledAs } });
        }

        [Test]
        public void a_pin_wins_over_the_calibre_file_and_the_catalogue()
        {
            GivenPin("Rifujin na Magonote");
            GivenCalibreFile(42, "Someone Else");

            Subject.Resolve(_entry, PinKey, "another name").Should().Be("Rifujin na Magonote");

            Mocker.GetMock<ICalibreProxy>().Verify(s => s.GetBook(It.IsAny<int>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<ICalibreContentServerClient>().Verify(s => s.SearchAuthor(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void without_a_pin_the_calibre_file_s_first_author_wins_over_the_catalogue()
        {
            GivenCalibreFile(42, "Rifujin na Magonote", "Shirotaka");

            Subject.Resolve(_entry, PinKey, "another name").Should().Be("Rifujin na Magonote");

            Mocker.GetMock<ICalibreContentServerClient>().Verify(s => s.SearchAuthor(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void the_catalogue_author_takes_calibre_s_spelling_when_calibre_knows_the_person()
        {
            GivenCalibreKnowsAuthor("rifujin na magonote", 7, "Rifujin na Magonote");

            Subject.Resolve(_entry, PinKey, "rifujin na magonote").Should().Be("Rifujin na Magonote");
        }

        [Test]
        public void the_catalogue_author_is_used_as_given_when_calibre_has_no_hit()
        {
            Subject.Resolve(_entry, PinKey, " Rifujin na Magonote ").Should().Be("Rifujin na Magonote");
        }

        [Test]
        public void a_calibre_failure_on_the_file_rung_falls_through_to_the_catalogue()
        {
            GivenCalibreFile(42, "Someone Else");
            Mocker.GetMock<ICalibreProxy>()
                  .Setup(s => s.GetBook(42, _settings))
                  .Throws(new CalibreException("Unable to connect to Calibre library"));

            Subject.Resolve(_entry, PinKey, "Rifujin na Magonote").Should().Be("Rifujin na Magonote");

            ExceptionVerification.IgnoreWarns();
        }

        // Keep-on-failure (2026-09-20 review): when the file rung cannot answer at all, the Writer an
        // earlier pass stored survives the outage rather than being replaced by the catalogue's or by
        // null (the refresh upserts the whole row). An answer still replaces it.
        [Test]
        public void a_calibre_outage_on_the_file_rung_keeps_the_stored_writer()
        {
            _entry.Metadata.Value.Writer = "Rifujin na Magonote";
            GivenCalibreFile(42, "Someone Else");
            Mocker.GetMock<ICalibreProxy>()
                  .Setup(s => s.GetBook(42, _settings))
                  .Throws(new CalibreException("Unable to connect to Calibre library"));

            Subject.Resolve(_entry, PinKey, null).Should().Be("Rifujin na Magonote");

            ExceptionVerification.IgnoreWarns();
        }

        // Final review I2: an EPUB added while no writer was known is filed under the series name;
        // reading that back is the fallback, not an answer -- the catalogue is still consulted.
        [TestCase("Mushoku Tensei: Jobless Reincarnation")]
        [TestCase("mushoku tensei: jobless reincarnation")]
        [TestCase("Mushoku Tensei: Jobless Reincarnation (light novel)")]
        public void the_series_name_read_back_from_calibre_is_not_a_writer(string filedUnder)
        {
            GivenCalibreFile(42, filedUnder);
            GivenCalibreKnowsAuthor("Rifujin na Magonote", 7, "Rifujin na Magonote");

            Subject.Resolve(_entry, PinKey, "Rifujin na Magonote").Should().Be("Rifujin na Magonote");

            Mocker.GetMock<ICalibreContentServerClient>().Verify(s => s.SearchAuthor("Rifujin na Magonote"), Times.Once());
        }

        [Test]
        public void the_series_name_read_back_with_nothing_else_known_is_null()
        {
            GivenCalibreFile(42, "Mushoku Tensei: Jobless Reincarnation");

            Subject.Resolve(_entry, PinKey, null).Should().BeNull();
        }

        [Test]
        public void a_file_rung_answer_replaces_a_different_stored_writer()
        {
            _entry.Metadata.Value.Writer = "Stored Name";
            GivenCalibreFile(42, "Rifujin na Magonote");

            Subject.Resolve(_entry, PinKey, null).Should().Be("Rifujin na Magonote");
        }

        // The catalogue rung answers with the name either way: a calibre outage must not downgrade
        // a known author to "file under the series name".
        [Test]
        public void a_calibre_failure_on_the_catalogue_rung_keeps_the_catalogue_spelling()
        {
            Mocker.GetMock<ICalibreContentServerClient>()
                  .Setup(s => s.SearchAuthor(It.IsAny<string>()))
                  .Throws(new CalibreException("Unable to connect to Calibre library"));

            Subject.Resolve(_entry, PinKey, " Rifujin na Magonote ").Should().Be("Rifujin na Magonote");

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void nothing_known_returns_null()
        {
            Subject.Resolve(_entry, PinKey, null).Should().BeNull();
            Subject.Resolve(null, PinKey, string.Empty).Should().BeNull();
        }

        // calibre's spelling is only asked while calibre is the ebook home.
        [Test]
        public void the_catalogue_author_is_used_as_given_while_ebooks_go_to_the_entry_folder()
        {
            GivenCalibreKnowsAuthor("rifujin na magonote", 7, "Rifujin na Magonote");
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Entry);

            Subject.Resolve(_entry, PinKey, "rifujin na magonote").Should().Be("rifujin na magonote");

            Mocker.GetMock<ICalibreContentServerClient>().Verify(s => s.SearchAuthor(It.IsAny<string>()), Times.Never());
        }
    }
}
