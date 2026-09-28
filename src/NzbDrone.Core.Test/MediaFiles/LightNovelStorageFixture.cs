using System.Collections.Generic;
using System.Net.Http;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class LightNovelStorageFixture : CoreTest<LightNovelStorage>
    {
        private Mock<IConfigService> Config => Mocker.GetMock<IConfigService>();

        private void GivenAudiobookshelf(string url, string libraryId, string remote = "", string local = "")
        {
            Config.SetupGet(c => c.AudiobookshelfUrl).Returns(url);
            Config.SetupGet(c => c.AudiobookshelfApiKey).Returns("abs-key");
            Config.SetupGet(c => c.AudiobookshelfLibraryId).Returns(libraryId);
            Config.SetupGet(c => c.AudiobookshelfRemotePath).Returns(remote);
            Config.SetupGet(c => c.AudiobookshelfLocalPath).Returns(local);
        }

        private void GivenLibraries(params AudiobookshelfLibrary[] libraries)
        {
            Mocker.GetMock<IAudiobookshelfClient>()
                  .Setup(c => c.GetLibraries("http://abs.test", "abs-key"))
                  .Returns(new List<AudiobookshelfLibrary>(libraries));
        }

        [TestCase(null, "entry")]
        [TestCase("", "entry")]
        [TestCase("entry", "entry")]
        [TestCase("calibre", "calibre")]
        [TestCase(" Calibre ", "calibre")]
        [TestCase("audiobookshelf", "entry")]
        [TestCase("bogus", "entry")]
        public void the_ebook_home_is_calibre_only_when_set_to_calibre(string stored, string expected)
        {
            Config.SetupGet(c => c.LightNovelEbookHome).Returns(stored);

            Subject.EbookHome.Should().Be(expected);
        }

        [TestCase(null, "entry")]
        [TestCase("entry", "entry")]
        [TestCase("audiobookshelf", "audiobookshelf")]
        [TestCase("AudioBookShelf", "audiobookshelf")]
        [TestCase("calibre", "entry")]
        public void the_audio_home_is_audiobookshelf_only_when_set_to_audiobookshelf(string stored, string expected)
        {
            Config.SetupGet(c => c.LightNovelAudioHome).Returns(stored);

            Subject.AudioHome.Should().Be(expected);
        }

        [Test]
        public void the_connections_are_the_config_values()
        {
            Config.SetupGet(c => c.CalibreContentServerUrl).Returns("http://calibre.test:8081");
            Config.SetupGet(c => c.CalibreLibrary).Returns("books");
            Config.SetupGet(c => c.CalibreUsername).Returns("reader");
            Config.SetupGet(c => c.CalibrePassword).Returns("secret");
            Config.SetupGet(c => c.CalibreRemotePath).Returns("/calibre/");
            Config.SetupGet(c => c.CalibreLocalPath).Returns("/srv/calibre/");
            GivenAudiobookshelf("http://abs.test", "lib-1", "/audiobooks/", "/srv/audiobooks/");

            Subject.Calibre.Should().BeEquivalentTo(new CalibreConnection
            {
                Url = "http://calibre.test:8081", Library = "books", Username = "reader", Password = "secret", RemotePath = "/calibre/", LocalPath = "/srv/calibre/"
            });
            Subject.Audiobookshelf.Should().BeEquivalentTo(new AudiobookshelfConnection
            {
                Url = "http://abs.test", ApiKey = "abs-key", LibraryId = "lib-1", RemotePath = "/audiobooks/", LocalPath = "/srv/audiobooks/"
            });
        }

        [Test]
        public void the_mappers_use_each_services_own_pair_both_ways()
        {
            Config.SetupGet(c => c.CalibreRemotePath).Returns("/calibre/");
            Config.SetupGet(c => c.CalibreLocalPath).Returns("/srv/calibre/");
            GivenAudiobookshelf("http://abs.test", "lib-1", "/audiobooks/", "/srv/audiobooks/");

            Subject.MapFromCalibre("/calibre/A/B.epub").Should().Be("/srv/calibre/A/B.epub");
            Subject.MapToCalibre("/srv/calibre/A/B.epub").Should().Be("/calibre/A/B.epub");
            Subject.MapFromAudiobookshelf("/audiobooks/A/B.m4b").Should().Be("/srv/audiobooks/A/B.m4b");
            Subject.MapToAudiobookshelf("/srv/audiobooks/A/B.m4b").Should().Be("/audiobooks/A/B.m4b");
        }

        // Mapped (the dev server): the Mangarr side of the pair, no network call.
        [Test]
        public void a_mapped_audiobookshelf_root_is_the_local_path_without_asking_audiobookshelf()
        {
            GivenAudiobookshelf("http://abs.test", "lib-1", "/audiobooks/", "/srv/audiobooks/");

            Subject.AudiobookshelfRoot().Should().Be("/srv/audiobooks");

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.GetLibraries(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        // Blank mapping: the library's first folder as ABS reports it, asked once and cached.
        [Test]
        public void an_unmapped_audiobookshelf_root_is_the_librarys_first_folder_asked_once()
        {
            GivenAudiobookshelf("http://abs.test", "lib-1");
            GivenLibraries(
                new AudiobookshelfLibrary { Id = "lib-0", Folders = new List<string> { "/podcasts" } },
                new AudiobookshelfLibrary { Id = "lib-1", Folders = new List<string> { "/audiobooks/", "/more" } });

            Subject.AudiobookshelfRoot().Should().Be("/audiobooks");
            Subject.AudiobookshelfRoot().Should().Be("/audiobooks");

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.GetLibraries("http://abs.test", "abs-key"), Times.Once());
        }

        [Test]
        public void an_unmapped_root_is_unknown_when_audiobookshelf_does_not_answer()
        {
            GivenAudiobookshelf("http://abs.test", "lib-1");
            Mocker.GetMock<IAudiobookshelfClient>()
                  .Setup(c => c.GetLibraries(It.IsAny<string>(), It.IsAny<string>()))
                  .Throws(new HttpRequestException("Connection refused"));

            Subject.AudiobookshelfRoot().Should().BeNull();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void an_unmapped_root_is_unknown_when_the_library_is_gone()
        {
            GivenAudiobookshelf("http://abs.test", "lib-9");
            GivenLibraries(new AudiobookshelfLibrary { Id = "lib-1", Folders = new List<string> { "/audiobooks" } });

            Subject.AudiobookshelfRoot().Should().BeNull();

            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase("", "lib-1")]
        [TestCase("http://abs.test", "")]
        public void an_unmapped_root_is_unknown_without_an_address_or_a_library(string url, string libraryId)
        {
            GivenAudiobookshelf(url, libraryId);

            Subject.AudiobookshelfRoot().Should().BeNull();

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.GetLibraries(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        // The Test and library buttons send what the form holds: an untouched secret is still the mask,
        // which means the stored value (the same rule as the save); "" is an explicit clear.
        [TestCase("********", "stored")]
        [TestCase(null, "stored")]
        [TestCase("typed", "typed")]
        [TestCase("", "")]
        public void unmask_keeps_the_stored_secret_behind_the_mask(string incoming, string expected)
        {
            LightNovelStorage.Unmask(incoming, "stored").Should().Be(expected);
        }

        // Fix round 1 (2026-09-22): the Test/library buttons send whatever host is in the box right
        // now, not necessarily the saved one. A secret is only substituted -- mask or an omitted
        // field alike -- when that host, trimmed and trailing-slash-insensitive, still matches the
        // one the stored secret belongs to; otherwise it goes out empty rather than to a new host. A
        // typed secret always passes through, on any host.
        [TestCase("********", "http://calibre.test:8081", "http://calibre.test:8081", "stored")]
        [TestCase("********", "http://calibre.test:8081/", "http://calibre.test:8081", "stored")]
        [TestCase("********", " http://calibre.test:8081 ", "http://calibre.test:8081", "stored")]
        [TestCase(null, "http://calibre.test:8081", "http://calibre.test:8081", "stored")]
        [TestCase("********", "http://other.test", "http://calibre.test:8081", "")]
        [TestCase(null, "http://other.test", "http://calibre.test:8081", "")]
        [TestCase("typed", "http://other.test", "http://calibre.test:8081", "typed")]
        [TestCase("", "http://other.test", "http://calibre.test:8081", "")]
        public void unmask_for_host_only_trusts_the_mask_or_a_blank_field_when_the_host_still_matches(string incoming, string incomingUrl, string storedUrl, string expected)
        {
            LightNovelStorage.UnmaskForHost(incoming, "stored", incomingUrl, storedUrl).Should().Be(expected);
        }
    }
}
