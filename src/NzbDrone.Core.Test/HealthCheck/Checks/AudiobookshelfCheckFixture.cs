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
    [TestFixture]
    public class AudiobookshelfCheckFixture : CoreTest<AudiobookshelfCheck>
    {
        private AudiobookshelfConnection _connection;

        [SetUp]
        public void Setup()
        {
            _connection = new AudiobookshelfConnection { Url = "http://abs.test", LibraryId = "lib-1" };

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString("LightNovelStorageAudiobookshelfCheck"))
                  .Returns("Light-novel audiobooks cannot reach Audiobookshelf: {0}");

            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.AudioHome).Returns(LightNovelHome.Audiobookshelf);
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.Audiobookshelf).Returns(_connection);
            Mocker.GetMock<ILightNovelStorageProbe>().Setup(p => p.TestAudiobookshelf(_connection)).Returns(new List<string>());

            GivenAuthors(new Author { Id = 3, Name = "Overlord", Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord~ln" } });
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
                  .Setup(p => p.TestAudiobookshelf(_connection))
                  .Returns(new List<string> { "Audiobookshelf rejected the API key" });

            Subject.Check().ShouldBeError("Light-novel audiobooks cannot reach Audiobookshelf: Audiobookshelf rejected the API key", "light-novel-storage-audiobookshelf");
        }

        [Test]
        public void should_be_silent_when_audiobooks_go_to_the_entry_folder()
        {
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.AudioHome).Returns(LightNovelHome.Entry);

            Subject.Check().ShouldBeOk();

            Mocker.GetMock<ILightNovelStorageProbe>().Verify(v => v.TestAudiobookshelf(It.IsAny<AudiobookshelfConnection>()), Times.Never());
        }

        [Test]
        public void should_be_silent_when_no_light_novel_entry_exists()
        {
            GivenAuthors(new Author { Id = 4, Name = "Overlord", Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord" } });

            Subject.Check().ShouldBeOk();

            Mocker.GetMock<ILightNovelStorageProbe>().Verify(v => v.TestAudiobookshelf(It.IsAny<AudiobookshelfConnection>()), Times.Never());
        }
    }
}
