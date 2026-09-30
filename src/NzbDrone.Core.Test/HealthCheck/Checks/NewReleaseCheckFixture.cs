using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    // Beta polish (2026-09-28): the new-release notice. Versions are derived from the running build, so
    // "newer" and "same" hold whatever number the test assembly carries.
    [TestFixture]
    public class NewReleaseCheckFixture : CoreTest<NewReleaseCheck>
    {
        private int _requests;

        private static string Newer => new Version(BuildInfo.Version.Major, BuildInfo.Version.Minor, BuildInfo.Version.Build, BuildInfo.Version.Revision + 1).ToString();

        // The body docker.yml's push job writes: the CHANGELOG section, then the build line.
        private static string BodyFor(string build)
        {
            return "The first public beta. Everything below is new compared with Readarr.\n\n" +
                   "### Libraries\n\n- **Manga library.** A series has volumes.\n\n" +
                   "Build: " + build + "\n";
        }

        private static object Release(string tag, string body, bool prerelease = true)
        {
            return new { tag_name = tag, name = "Mangarr " + tag.TrimStart('v'), body, prerelease, draft = false, html_url = "https://github.com/opentomedb/mangarr/releases/tag/" + tag };
        }

        private void GivenResponse(string content, HttpStatusCode status = HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Get(It.IsAny<HttpRequest>()))
                  .Callback(() => _requests++)
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), content, status));
        }

        private void GivenReleases(params object[] releases)
        {
            GivenResponse(JsonConvert.SerializeObject(releases));
        }

        [SetUp]
        public void Setup()
        {
            _requests = 0;

            Mocker.GetMock<IConfigService>().SetupGet(s => s.CheckForNewReleases).Returns(true);

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString("NewReleaseCheckMessage", It.IsAny<Dictionary<string, object>>()))
                  .Returns<string, Dictionary<string, object>>((k, t) => $"Mangarr {t["release"]} is available (you run {t["version"]})");
        }

        [Test]
        public void a_newer_release_shows_the_notice_with_its_page()
        {
            GivenReleases(Release("v0.1.0-beta.2", BodyFor(Newer)));

            var result = Subject.Check();

            result.ShouldBeWarning($"Mangarr 0.1.0-beta.2 is available (you run {BuildInfo.Version})");
            result.WikiUrl.FullUri.Should().Be("https://github.com/opentomedb/mangarr/releases/tag/v0.1.0-beta.2");
        }

        [Test]
        public void the_request_goes_to_the_releases_api_with_prereleases()
        {
            HttpRequest sent = null;
            GivenReleases(Release("v0.1.0-beta.2", BodyFor(Newer)));
            Mocker.GetMock<IHttpClient>().Setup(s => s.Get(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => sent = r)
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), "[]", HttpStatusCode.OK));

            Subject.Check();

            sent.Url.FullUri.Should().Be("https://api.github.com/repos/opentomedb/mangarr/releases?per_page=5");
            sent.SuppressHttpError.Should().BeTrue();
        }

        [Test]
        public void the_same_build_shows_nothing()
        {
            GivenReleases(Release("v0.1.0-beta.1", BodyFor(BuildInfo.Version.ToString())));

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void an_older_build_shows_nothing()
        {
            GivenReleases(Release("v0.0.9-beta.1", BodyFor("9.0.0.1")));

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void the_highest_build_of_the_page_wins()
        {
            GivenReleases(Release("v0.1.0-beta.1", BodyFor("9.0.0.1")),
                          Release("v0.1.0-beta.3", BodyFor(Newer)),
                          Release("v0.1.0-beta.2", "No build line here."));

            Subject.Check().ShouldBeWarning("Mangarr 0.1.0-beta.3 is available");
        }

        [TestCase("")]
        [TestCase("Build: soon")]
        [TestCase("The build is 10.0.0.99999 somewhere in a sentence.")]
        [TestCase(null)]
        public void a_release_without_a_build_line_is_ignored(string body)
        {
            GivenReleases(Release("v9.9.9", body));

            Subject.Check().ShouldBeOk();
        }

        [TestCase("not json")]
        [TestCase("{\"message\":\"API rate limit exceeded\"}")]
        [TestCase("")]
        public void a_malformed_answer_shows_nothing(string content)
        {
            GivenResponse(content);

            Subject.Check().ShouldBeOk();
        }

        [TestCase(HttpStatusCode.Forbidden)]
        [TestCase(HttpStatusCode.NotFound)]
        [TestCase(HttpStatusCode.InternalServerError)]
        public void an_http_error_shows_nothing(HttpStatusCode status)
        {
            GivenResponse(JsonConvert.SerializeObject(new[] { Release("v9.9.9", BodyFor(Newer)) }), status);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void a_network_error_shows_nothing_and_logs_no_warning()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Get(It.IsAny<HttpRequest>()))
                  .Throws(new WebException("Name or service not known", WebExceptionStatus.NameResolutionFailure));

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void setting_off_sends_no_request()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.CheckForNewReleases).Returns(false);
            GivenReleases(Release("v0.1.0-beta.2", BodyFor(Newer)));

            Subject.Check().ShouldBeOk();

            Mocker.GetMock<IHttpClient>().Verify(s => s.Get(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void the_answer_is_cached_between_checks()
        {
            GivenReleases(Release("v0.1.0-beta.2", BodyFor(Newer)));

            Subject.Check().ShouldBeWarning();
            Subject.Check().ShouldBeWarning();
            Subject.Check().ShouldBeWarning();

            _requests.Should().Be(1);
        }

        [Test]
        public void a_failed_attempt_is_not_retried_before_the_interval()
        {
            GivenResponse("oops", HttpStatusCode.BadGateway);

            Subject.Check().ShouldBeOk();
            Subject.Check().ShouldBeOk();

            _requests.Should().Be(1);
        }

        [TestCase("Build: 10.0.0.812", "10.0.0.812")]
        [TestCase("notes\r\nBuild: 10.0.0.812\r\n", "10.0.0.812")]
        [TestCase("  Build:   10.0.0.9  \n", "10.0.0.9")]
        [TestCase("Build: 10.0.0", null)]
        [TestCase("Build 10.0.0.812", null)]
        public void parse_build(string body, string expected)
        {
            NewReleaseCheck.ParseBuild(body)?.ToString().Should().Be(expected);

            if (expected == null)
            {
                NewReleaseCheck.ParseBuild(body).Should().BeNull();
            }
        }
    }
}
