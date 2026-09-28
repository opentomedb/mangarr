using System.Linq;
using System.Net;
using System.Reflection;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    // Beta readiness (2026-09-28, E5): nothing Mangarr builds a request with targets Readarr's retired
    // service host (readarr.servarr.com): the proxy probe goes to the catalogue manifest instead.
    [TestFixture]
    public class ProxyCheckFixture : CoreTest<ProxyCheck>
    {
        private HttpRequest _sent;

        [SetUp]
        public void Setup()
        {
            _sent = null;

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Failed to test proxy: {0}");

            Mocker.GetMock<IConfigService>().SetupGet(s => s.ProxyEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.ProxyHostname).Returns("localhost");

            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => _sent = r)
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.Found));
        }

        [Test]
        public void should_probe_the_default_catalogue_manifest()
        {
            Subject.Check().ShouldBeOk();

            _sent.Should().NotBeNull();
            _sent.Url.ToString().Should().Be(MetadataUpdateService.DefaultManifestUrl);
            _sent.Url.Host.Should().NotEndWith("servarr.com");
        }

        [Test]
        public void should_probe_a_configured_catalogue_manifest()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.MetadataManifestUrl).Returns(" https://catalogue.example.org/version.json ");

            Subject.Check().ShouldBeOk();

            _sent.Url.Host.Should().Be("catalogue.example.org");
        }

        [Test]
        public void should_report_a_bad_request_through_the_proxy()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.BadRequest));

            Subject.Check().ShouldBeError();
        }

        [Test]
        public void should_not_probe_without_a_proxy()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.ProxyEnabled).Returns(false);

            Subject.Check().ShouldBeOk();

            Mocker.GetMock<IHttpClient>().Verify(s => s.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void no_cloud_request_builder_targets_readarr_servarr_com()
        {
            var cloud = new ReadarrCloudRequestBuilder();
            var factories = typeof(IReadarrCloudRequestBuilder).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(IHttpRequestBuilderFactory))
                .Select(p => (IHttpRequestBuilderFactory)p.GetValue(cloud))
                .ToList();

            factories.Should().NotBeEmpty();
            factories.Select(f => f.Create().BaseUrl.Host).Should().NotContain(h => h.EndsWith("servarr.com"));
        }
    }
}
