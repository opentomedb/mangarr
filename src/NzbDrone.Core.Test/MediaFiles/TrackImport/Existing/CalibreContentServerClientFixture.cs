using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Existing
{
    // Light-novel storage (2026-09-22): the read-only client asks the same server, library and login
    // as every other calibre call (ForConfig) -- no URL of its own, no second library constant.
    [TestFixture]
    public class CalibreContentServerClientFixture : CoreTest<CalibreContentServerClient>
    {
        private HttpRequest _sent;

        private void GivenSettings(string username)
        {
            Mocker.GetMock<ILightNovelCalibreSettings>()
                .Setup(s => s.ForConfig())
                .Returns(new CalibreSettings { Host = "calibre.test", Port = 8081, UrlBase = "cal", Library = "lightnovels", Username = username, Password = "secret" });

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => _sent = r)
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), "{\"total_num\":1,\"book_ids\":[7]}"));
        }

        [Test]
        public void a_search_goes_to_the_configured_server_and_library_with_the_login()
        {
            GivenSettings("reader");

            Subject.SearchSeries("Overlord").Should().Equal(7);

            _sent.Url.FullUri.Should().StartWith("http://calibre.test:8081/cal/ajax/search/lightnovels?");
            _sent.Credentials.Should().BeOfType<NetworkCredential>().Which.UserName.Should().Be("reader");
        }

        [Test]
        public void no_username_sends_no_credentials()
        {
            GivenSettings(null);

            Subject.SearchAuthor("Kugane Maruyama");

            _sent.Url.FullUri.Should().Contain("/ajax/search/lightnovels");
            _sent.Credentials.Should().BeNull();
        }
    }
}
