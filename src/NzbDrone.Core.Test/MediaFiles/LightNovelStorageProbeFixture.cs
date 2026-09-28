using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles
{
    // Light-novel storage (2026-09-22): the Test buttons, the library lists and both health checks ask
    // the same questions in the same order and answer with the en.json key (here echoed with its first
    // argument, so the assertions read the key).
    [TestFixture]
    public class LightNovelStorageProbeFixture : CoreTest<LightNovelStorageProbe>
    {
        private CalibreConnection _calibre;
        private AudiobookshelfConnection _abs;

        [SetUp]
        public void Setup()
        {
            _calibre = new CalibreConnection { Url = "http://calibre.test:8081", Library = string.Empty, Username = string.Empty, Password = string.Empty, RemotePath = string.Empty, LocalPath = string.Empty };
            _abs = new AudiobookshelfConnection { Url = "http://abs.test", ApiKey = "abs-key", LibraryId = "lib-1", RemotePath = string.Empty, LocalPath = string.Empty };

            Mocker.GetMock<ILocalizationService>()
                .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                .Returns<string>(key => key + " {0}");

            Mocker.GetMock<ICalibreProxy>()
                .Setup(p => p.GetLibraryInfo(It.IsAny<CalibreSettings>()))
                .Returns(new CalibreLibraryInfo
                {
                    DefaultLibrary = "books",
                    LibraryMap = new Dictionary<string, string> { { "books", "books" }, { "lightnovels", "Light Novels" } }
                });
            Mocker.GetMock<ICalibreProxy>().Setup(p => p.HasWriteAccess(It.IsAny<CalibreSettings>())).Returns(true);

            Mocker.GetMock<IAudiobookshelfClient>()
                .Setup(c => c.GetLibraries("http://abs.test", "abs-key"))
                .Returns(new List<AudiobookshelfLibrary>
                {
                    new AudiobookshelfLibrary { Id = "lib-1", Name = "Audiobooks", MediaType = "book", Folders = new List<string> { "/audiobooks" } },
                    new AudiobookshelfLibrary { Id = "lib-2", Name = "Podcasts", MediaType = "podcast", Folders = new List<string> { "/podcasts" } }
                });

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns(true);

            GivenRenameVolumes(true);
        }

        private void GivenRenameVolumes(bool on)
        {
            Mocker.GetMock<INamingConfigService>().Setup(s => s.GetConfig()).Returns(new NamingConfig { RenameBooks = on });
        }

        private static HttpException HttpError(string url, HttpStatusCode status)
        {
            return new HttpException(new HttpRequest(url), new HttpResponse(new HttpRequest(url), new HttpHeader(), string.Empty, status));
        }

        [Test]
        public void a_reachable_writable_calibre_on_its_default_library_has_no_problems()
        {
            Subject.TestCalibre(_calibre).Should().BeEmpty();

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.HasWriteAccess(It.Is<CalibreSettings>(s => s.Library == "books")), Times.Once());
        }

        [Test]
        public void a_blank_calibre_url_is_the_only_problem()
        {
            _calibre.Url = string.Empty;

            Subject.TestCalibre(_calibre).Should().Equal("LightNovelStorageCalibreUrlMissing {0}");

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetLibraryInfo(It.IsAny<CalibreSettings>()), Times.Never());
        }

        [Test]
        public void a_non_http_calibre_url_is_named()
        {
            _calibre.Url = "calibre:8081";

            Subject.TestCalibre(_calibre).Should().Equal("LightNovelStorageCalibreUrlInvalid calibre:8081");
        }

        [Test]
        public void an_unreachable_calibre_is_named_with_its_address()
        {
            Mocker.GetMock<ICalibreProxy>().Setup(p => p.GetLibraryInfo(It.IsAny<CalibreSettings>())).Throws(new HttpRequestException("Connection refused"));

            Subject.TestCalibre(_calibre).Should().Equal("LightNovelStorageCalibreUnreachable http://calibre.test:8081");
        }

        [Test]
        public void a_rejected_calibre_login_is_named()
        {
            Mocker.GetMock<ICalibreProxy>().Setup(p => p.GetLibraryInfo(It.IsAny<CalibreSettings>())).Throws(HttpError("http://calibre.test:8081/ajax/library-info", HttpStatusCode.Unauthorized));

            Subject.TestCalibre(_calibre).Should().Equal("LightNovelStorageCalibreLoginRejected {0}");
        }

        [Test]
        public void the_login_is_sent_with_the_probe()
        {
            _calibre.Username = "reader";
            _calibre.Password = "secret";

            Subject.TestCalibre(_calibre);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetLibraryInfo(It.Is<CalibreSettings>(s => s.Username == "reader" && s.Password == "secret")), Times.Once());
        }

        [Test]
        public void a_missing_calibre_library_is_named_and_no_write_is_probed()
        {
            _calibre.Library = "manga";

            Subject.TestCalibre(_calibre).Should().Equal("LightNovelStorageCalibreLibraryMissing manga");

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.HasWriteAccess(It.IsAny<CalibreSettings>()), Times.Never());
        }

        [Test]
        public void a_calibre_refusing_writes_is_named()
        {
            Mocker.GetMock<ICalibreProxy>().Setup(p => p.HasWriteAccess(It.IsAny<CalibreSettings>())).Returns(false);

            Subject.TestCalibre(_calibre).Should().Equal("LightNovelStorageCalibreNoWriteAccess {0}");
        }

        [Test]
        public void a_missing_mapped_calibre_folder_is_named()
        {
            _calibre.RemotePath = "/calibre/";
            _calibre.LocalPath = "/srv/calibre/";
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists("/srv/calibre/")).Returns(false);

            Subject.TestCalibre(_calibre).Should().Equal("LightNovelStorageCalibrePathMissing /srv/calibre/");
        }

        [Test]
        public void a_blank_calibre_mapping_checks_no_folder()
        {
            Subject.TestCalibre(_calibre).Should().BeEmpty();

            Mocker.GetMock<IDiskProvider>().Verify(v => v.FolderExists(It.IsAny<string>()), Times.Never());
        }

        // A half-set pair (light-novel storage fix round 1): a validator refuses to save it, and the
        // settings validator treats it as unset -- the probe follows suit rather than checking a
        // LocalPath calibre's RemotePath was never told about.
        [Test]
        public void a_half_set_calibre_mapping_checks_no_folder()
        {
            _calibre.LocalPath = "/srv/calibre/";

            Subject.TestCalibre(_calibre).Should().BeEmpty();

            Mocker.GetMock<IDiskProvider>().Verify(v => v.FolderExists(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void calibre_libraries_lists_the_servers_libraries_and_its_default()
        {
            var list = Subject.CalibreLibraries(_calibre);

            list.Problems.Should().BeEmpty();
            list.DefaultLibrary.Should().Be("books");
            list.Libraries.Should().BeEquivalentTo(new[]
            {
                new LightNovelLibraryOption { Id = "books", Name = "books" },
                new LightNovelLibraryOption { Id = "lightnovels", Name = "Light Novels" }
            });
        }

        [Test]
        public void calibre_libraries_on_an_unreachable_server_is_a_problem_not_a_throw()
        {
            Mocker.GetMock<ICalibreProxy>().Setup(p => p.GetLibraryInfo(It.IsAny<CalibreSettings>())).Throws(new HttpRequestException("Connection refused"));

            var list = Subject.CalibreLibraries(_calibre);

            list.Libraries.Should().BeEmpty();
            list.Problems.Should().Equal("LightNovelStorageCalibreUnreachable http://calibre.test:8081");
        }

        [Test]
        public void an_unmapped_audiobookshelf_library_whose_folder_exists_has_no_problems()
        {
            Subject.TestAudiobookshelf(_abs).Should().BeEmpty();

            Mocker.GetMock<IDiskProvider>().Verify(v => v.FolderExists("/audiobooks"), Times.Once());
        }

        [Test]
        public void a_mapped_audiobookshelf_library_checks_the_mangarr_side()
        {
            _abs.RemotePath = "/audiobooks/";
            _abs.LocalPath = "/srv/audiobooks/";

            Subject.TestAudiobookshelf(_abs).Should().BeEmpty();

            Mocker.GetMock<IDiskProvider>().Verify(v => v.FolderExists("/srv/audiobooks"), Times.Once());
        }

        // Beta readiness (2026-09-28, F6): the Audiobookshelf layout needs Rename Volumes; the Test button and
        // the health check name it, after the connection's own problems and whatever those are.
        [Test]
        public void rename_volumes_off_is_named_for_a_working_audiobookshelf()
        {
            GivenRenameVolumes(false);

            Subject.TestAudiobookshelf(_abs).Should().Equal("LightNovelStorageAudiobookshelfRenameOff {0}");
        }

        [Test]
        public void rename_volumes_off_is_named_after_a_connection_problem()
        {
            GivenRenameVolumes(false);
            _abs.Url = string.Empty;

            Subject.TestAudiobookshelf(_abs).Should().Equal("LightNovelStorageAudiobookshelfUrlMissing {0}", "LightNovelStorageAudiobookshelfRenameOff {0}");
        }

        [Test]
        public void calibre_does_not_care_about_rename_volumes()
        {
            GivenRenameVolumes(false);

            Subject.TestCalibre(_calibre).Should().BeEmpty();
        }

        [Test]
        public void a_blank_audiobookshelf_url_is_the_only_problem()
        {
            _abs.Url = string.Empty;

            Subject.TestAudiobookshelf(_abs).Should().Equal("LightNovelStorageAudiobookshelfUrlMissing {0}");
        }

        [Test]
        public void an_unreachable_audiobookshelf_is_named_with_its_address()
        {
            Mocker.GetMock<IAudiobookshelfClient>().Setup(c => c.GetLibraries(It.IsAny<string>(), It.IsAny<string>())).Throws(new HttpRequestException("Connection refused"));

            Subject.TestAudiobookshelf(_abs).Should().Equal("LightNovelStorageAudiobookshelfUnreachable http://abs.test");
        }

        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.Forbidden)]
        public void a_rejected_audiobookshelf_key_is_named(HttpStatusCode status)
        {
            Mocker.GetMock<IAudiobookshelfClient>().Setup(c => c.GetLibraries(It.IsAny<string>(), It.IsAny<string>())).Throws(HttpError("http://abs.test/api/libraries", status));

            Subject.TestAudiobookshelf(_abs).Should().Equal("LightNovelStorageAudiobookshelfKeyRejected {0}");
        }

        [Test]
        public void no_chosen_audiobookshelf_library_is_named()
        {
            _abs.LibraryId = string.Empty;

            Subject.TestAudiobookshelf(_abs).Should().Equal("LightNovelStorageAudiobookshelfLibraryNotChosen {0}");
        }

        [Test]
        public void a_missing_audiobookshelf_library_is_named()
        {
            _abs.LibraryId = "lib-9";

            Subject.TestAudiobookshelf(_abs).Should().Equal("LightNovelStorageAudiobookshelfLibraryMissing lib-9");
        }

        [Test]
        public void a_mapping_outside_the_librarys_folder_is_named()
        {
            _abs.RemotePath = "/data/";
            _abs.LocalPath = "/srv/data/";

            Subject.TestAudiobookshelf(_abs).Should().Equal("LightNovelStorageAudiobookshelfFolderNotMapped /data/");
        }

        [Test]
        public void a_missing_audiobookshelf_folder_is_named()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists("/audiobooks")).Returns(false);

            Subject.TestAudiobookshelf(_abs).Should().Equal("LightNovelStorageAudiobookshelfPathMissing /audiobooks");
        }

        [Test]
        public void audiobookshelf_libraries_lists_only_book_libraries()
        {
            var list = Subject.AudiobookshelfLibraries(_abs);

            list.Problems.Should().BeEmpty();
            list.Libraries.Should().BeEquivalentTo(new[] { new LightNovelLibraryOption { Id = "lib-1", Name = "Audiobooks" } });
        }
    }
}
