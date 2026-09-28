using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    // Light-novel storage (2026-09-22): the check speaks only while ebooks go to calibre and a
    // light-novel entry exists; its message is the storage probe's, wrapped.
    [TestFixture]
    public class CalibreWriteAccessCheckFixture : CoreTest<CalibreWriteAccessCheck>
    {
        private CalibreConnection _connection;

        [SetUp]
        public void Setup()
        {
            _connection = new CalibreConnection { Url = "http://calibre.test:8081" };

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString("LightNovelStorageCalibreCheck"))
                  .Returns("Light-novel ebooks cannot reach calibre: {0}");

            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Calibre);
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.Calibre).Returns(_connection);
            Mocker.GetMock<ILightNovelStorageProbe>().Setup(p => p.TestCalibre(_connection)).Returns(new List<string>());

            GivenAuthors(LightNovelEntry());
        }

        private static Author LightNovelEntry()
        {
            return new Author
            {
                Id = 3,
                Name = "Overlord",
                Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord~ln" }
            };
        }

        private static Author MangaEntry()
        {
            return new Author
            {
                Id = 4,
                Name = "Overlord",
                Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord" }
            };
        }

        private void GivenAuthors(params Author[] authors)
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAllAuthors()).Returns(new List<Author>(authors));
        }

        [Test]
        public void should_be_ok_when_everything_checks_out()
        {
            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_be_error_with_every_problem_when_the_probe_finds_some()
        {
            Mocker.GetMock<ILightNovelStorageProbe>()
                  .Setup(p => p.TestCalibre(_connection))
                  .Returns(new List<string> { "calibre refuses writes from Mangarr", "/srv/calibre/ does not exist inside Mangarr" });

            Subject.Check().ShouldBeError("Light-novel ebooks cannot reach calibre: calibre refuses writes from Mangarr; /srv/calibre/ does not exist inside Mangarr", "light-novel-storage-calibre");
        }

        [Test]
        public void should_be_silent_when_ebooks_go_to_the_entry_folder()
        {
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Entry);

            Subject.Check().ShouldBeOk();

            Mocker.GetMock<ILightNovelStorageProbe>().Verify(v => v.TestCalibre(It.IsAny<CalibreConnection>()), Times.Never());
        }

        [Test]
        public void should_be_silent_when_no_light_novel_entry_exists()
        {
            GivenAuthors(MangaEntry());

            Subject.Check().ShouldBeOk();

            Mocker.GetMock<ILightNovelStorageProbe>().Verify(v => v.TestCalibre(It.IsAny<CalibreConnection>()), Times.Never());
        }
    }
}
