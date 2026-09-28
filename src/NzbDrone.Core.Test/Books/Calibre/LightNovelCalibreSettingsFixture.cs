using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests.Calibre
{
    // One copy each (2026-09-20): calibre settings for light-novel EPUBs come from the content
    // server named in Settings -> Media Management -> Light novel storage, not from a root folder;
    // other files keep their root folder's settings.
    [TestFixture]
    public class LightNovelCalibreSettingsFixture : CoreTest<LightNovelCalibreSettings>
    {
        private CalibreConnection _connection;

        [SetUp]
        public void Setup()
        {
            _connection = new CalibreConnection { Url = "http://calibre.test:8081", Library = "books", RemotePath = string.Empty, LocalPath = string.Empty };

            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.Calibre).Returns(() => _connection);
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Calibre);
        }

        private void GivenConfigUrl(string url)
        {
            _connection.Url = url;
        }

        private void GivenPreferredFormat(string format)
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PreferredLightNovelFormat).Returns(format);
        }

        private void GivenRootFolder(string path, CalibreSettings calibreSettings)
        {
            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder(path))
                .Returns(new RootFolder { Path = "/lightnovels", IsCalibreLibrary = calibreSettings != null, CalibreSettings = calibreSettings });
        }

        [Test]
        public void for_config_should_parse_a_plain_http_url()
        {
            GivenConfigUrl("http://calibre.test:8081");

            var settings = Subject.ForConfig();

            settings.Host.Should().Be("calibre.test");
            settings.Port.Should().Be(8081);
            settings.UseSsl.Should().BeFalse();
            settings.UrlBase.Should().Be(string.Empty);
            settings.Library.Should().Be("books");
            settings.Username.Should().BeNull();
            settings.Password.Should().BeNull();
            settings.OutputFormat.Should().BeNull();
            settings.OutputProfile.Should().Be((int)CalibreProfile.@default);
        }

        // The output format is the calibre delivery conversion the PreferredLightNovelFormat config
        // setting chooses (Task 3): EPUB stays no-conversion, AZW3/KEPUB get added to the book.
        [TestCase(null, null)]
        [TestCase("epub", null)]
        [TestCase("EPUB", null)]
        [TestCase("azw3", "AZW3")]
        [TestCase("kepub", "KEPUB")]
        public void for_config_should_set_the_output_format_from_the_preferred_light_novel_format(string preferred, string expected)
        {
            GivenConfigUrl("http://calibre.test:8081");
            GivenPreferredFormat(preferred);

            var settings = Subject.ForConfig();

            settings.OutputFormat.Should().Be(expected);
        }

        [Test]
        public void for_config_should_parse_an_https_url_with_a_url_base()
        {
            GivenConfigUrl("https://calibre.lan/cal/");

            var settings = Subject.ForConfig();

            settings.Host.Should().Be("calibre.lan");
            settings.Port.Should().Be(443);
            settings.UseSsl.Should().BeTrue();
            settings.UrlBase.Should().Be("cal");
            settings.Library.Should().Be("books");
        }

        // The proxy assembles its URLs with BuildBaseUrl, so the parse must land back on the config URL
        // (the default port comes out explicit and the trailing slash goes; both are equivalent to calibre).
        [TestCase("http://calibre.test:8081", "http://calibre.test:8081")]
        [TestCase("https://calibre.lan/cal/", "https://calibre.lan:443/cal")]
        public void for_config_should_round_trip_through_the_proxy_base_url(string configUrl, string expected)
        {
            GivenConfigUrl(configUrl);

            var settings = Subject.ForConfig();

            HttpRequestBuilder.BuildBaseUrl(settings.UseSsl, settings.Host, settings.Port, settings.UrlBase).Should().Be(expected);
        }

        [Test]
        public void for_should_return_config_settings_for_a_calibre_homed_file()
        {
            GivenConfigUrl("http://calibre.test:8081");
            var file = new BookFile { Path = "/books/Author/Title (12)/Title - Author.epub", Home = FileHome.Calibre };

            var settings = Subject.For(file);

            settings.Host.Should().Be("calibre.test");
            settings.Library.Should().Be("books");
            Mocker.GetMock<IRootFolderService>().Verify(v => v.GetBestRootFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void for_should_return_the_root_folders_settings_for_an_entry_file_in_a_calibre_root()
        {
            var rootSettings = new CalibreSettings { Host = "root.test", Port = 8082, Library = "manga" };
            var file = new BookFile { Path = "/lightnovels/Overlord/Overlord - Vol 001.epub", Home = FileHome.Entry };
            GivenRootFolder(file.Path, rootSettings);

            Subject.For(file).Should().BeSameAs(rootSettings);
        }

        [Test]
        public void for_should_return_null_for_an_entry_file_in_a_plain_root()
        {
            var file = new BookFile { Path = "/lightnovels/Overlord/Overlord - Vol 001.epub", Home = FileHome.Entry };
            GivenRootFolder(file.Path, null);

            Subject.For(file).Should().BeNull();
        }

        // Light-novel storage (2026-09-22): the login from Settings (optional; a password without a
        // username is not a login), the library, and the path pair riding on the subclass.
        [Test]
        public void for_config_sends_the_configured_login()
        {
            _connection.Username = "reader";
            _connection.Password = "secret";

            var settings = Subject.ForConfig();

            settings.Username.Should().Be("reader");
            settings.Password.Should().Be("secret");
        }

        [Test]
        public void a_password_without_a_username_is_no_login()
        {
            _connection.Password = "secret";

            var settings = Subject.ForConfig();

            settings.Username.Should().BeNull();
            settings.Password.Should().BeNull();
        }

        [Test]
        public void for_config_carries_the_path_pair_on_the_light_novel_subclass()
        {
            _connection.RemotePath = "/calibre/";
            _connection.LocalPath = "/srv/calibre/";

            var settings = Subject.ForConfig().Should().BeOfType<LightNovelCalibreServerSettings>().Which;

            settings.RemotePath.Should().Be("/calibre/");
            settings.LocalPath.Should().Be("/srv/calibre/");
        }

        [Test]
        public void a_blank_library_is_the_servers_default_asked_once()
        {
            _connection.Library = string.Empty;
            Mocker.GetMock<ICalibreProxy>()
                .Setup(p => p.GetLibraryInfo(It.IsAny<CalibreSettings>()))
                .Returns(new CalibreLibraryInfo { DefaultLibrary = "Calibre Library", LibraryMap = new Dictionary<string, string> { { "Calibre Library", "Calibre Library" } } });

            Subject.ForConfig().Library.Should().Be("Calibre Library");
            Subject.ForConfig().Library.Should().Be("Calibre Library");

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetLibraryInfo(It.IsAny<CalibreSettings>()), Times.Once());
        }

        [TestCase("")]
        [TestCase(null)]
        public void a_blank_url_is_a_calibre_error_naming_the_setting(string url)
        {
            GivenConfigUrl(url);

            Assert.Throws<CalibreException>(() => Subject.ForConfig()).Message.Should().Contain("Light Novel Storage → Ebooks");
        }

        [TestCase("calibre:8081")]
        [TestCase("not a url")]
        [TestCase("ftp://calibre.test")]
        public void a_non_http_url_is_a_calibre_error_naming_the_setting(string url)
        {
            GivenConfigUrl(url);

            Assert.Throws<CalibreException>(() => Subject.ForConfig()).Message.Should().Contain("Calibre Content Server URL");
        }

        // Conversion is calibre's: with ebooks going to the entry folder, ForConfig never asks for one.
        [Test]
        public void no_output_format_when_ebooks_go_to_the_entry_folder()
        {
            GivenPreferredFormat("azw3");
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Entry);

            Subject.ForConfig().OutputFormat.Should().BeNull();
        }
    }
}
