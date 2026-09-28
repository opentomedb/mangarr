using System;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Gcd
{
    [TestFixture]
    public class MetadataUpdateServiceFixture : CoreTest<MetadataUpdateService>
    {
        private string _overridePath;

        // Manifest values used across tests.
        private const string RemoteVersion = "2026-06-01.sql";
        private const string OlderVersion  = "2026-05-01.sql";
        private const string SameLabel     = "opentome-2026-09-18";

        [SetUp]
        public void Setup()
        {
            _overridePath = Path.Combine(TempFolder, "metadata", "manga-metadata.sqlite");

            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.MetadataAutoUpdate)
                .Returns(true);

            Mocker.GetMock<IGcdMetadataService>()
                .SetupGet(s => s.OverrideArtifactPath)
                .Returns(_overridePath);
        }

        // ── helpers ────────────────────────────────────────────────────────────

        private static string BuildManifestJson(string version, string sha256, string artifactUrl)
        {
            return JsonConvert.SerializeObject(new
            {
                gcd_dump    = version,
                sha256      = sha256,
                size        = 1024,
                artifact_url = artifactUrl
            });
        }

        private static byte[] BuildArtifactBytes(string version)
        {
            var path = Path.GetTempFileName();
            try
            {
                var cs = new SQLiteConnectionStringBuilder { DataSource = path }.ConnectionString;
                using (var conn = new SQLiteConnection(cs))
                {
                    conn.Open();
                    conn.Execute("CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT)");
                    conn.Execute("INSERT INTO meta (key, value) VALUES ('gcd_dump', @v)", new { v = version });
                }

                return File.ReadAllBytes(path);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        // Returns an HttpResponse carrying JSON content.
        private static HttpResponse JsonResponse(HttpRequest req, string json)
        {
            return new HttpResponse(req, new HttpHeader(), Encoding.UTF8.GetBytes(json));
        }

        // Returns an HttpResponse carrying raw bytes (for artifact download).
        private static HttpResponse BytesResponse(HttpRequest req, byte[] data)
        {
            return new HttpResponse(req, new HttpHeader(), data);
        }

        private void GivenManifest(string version, string sha256, string artifactUrl = "https://example.com/artifact")
        {
            var json = BuildManifestJson(version, sha256, artifactUrl);
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("version.json"))))
                .Returns<HttpRequest>(r => JsonResponse(r, json));
        }

        private void GivenArtifactDownload(byte[] data)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("artifact"))))
                .Returns<HttpRequest>(r => BytesResponse(r, data));
        }

        // The artifact the service has loaded (what ArtifactInfo().Path reports); by default the
        // override path, which is also where the updater writes.
        private void GivenLocalArtifact(byte[] data, string path = null)
        {
            path = path ?? _overridePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);

            Mocker.GetMock<IGcdMetadataService>()
                .Setup(s => s.ArtifactInfo())
                .Returns(new GcdArtifactInfo { Available = true, Path = path });
        }

        // ── tests: version comparison ──────────────────────────────────────────

        [Test]
        public void remote_newer_triggers_download_and_reload()
        {
            // Local is older; remote is newer → should download and reload.
            Mocker.GetMock<IGcdMetadataService>()
                .Setup(s => s.LocalDumpVersion())
                .Returns(OlderVersion);

            var artifact = BuildArtifactBytes(RemoteVersion);
            var sha256   = Sha256Hex(artifact);

            GivenManifest(RemoteVersion, sha256);
            GivenArtifactDownload(artifact);

            Subject.CheckForUpdate();

            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Once);
            File.Exists(_overridePath).Should().BeTrue();
        }

        [Test]
        public void same_version_skips_download()
        {
            Mocker.GetMock<IGcdMetadataService>()
                .Setup(s => s.LocalDumpVersion())
                .Returns(RemoteVersion);

            GivenManifest(RemoteVersion, "doesnotmatter");

            Subject.CheckForUpdate();

            // No artifact download should occur; no Reload.
            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("artifact"))), Times.Never);
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Never);
        }

        [Test]
        public void older_remote_version_skips_download()
        {
            // Remote is behind local → no-op.
            Mocker.GetMock<IGcdMetadataService>()
                .Setup(s => s.LocalDumpVersion())
                .Returns(RemoteVersion);

            GivenManifest(OlderVersion, "doesnotmatter");

            Subject.CheckForUpdate();

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("artifact"))), Times.Never);
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Never);
        }

        [Test]
        public void no_local_version_downloads_when_remote_available()
        {
            // No local artifact at all (null) → should always download.
            Mocker.GetMock<IGcdMetadataService>()
                .Setup(s => s.LocalDumpVersion())
                .Returns((string)null);

            var artifact = BuildArtifactBytes(RemoteVersion);
            var sha256   = Sha256Hex(artifact);

            GivenManifest(RemoteVersion, sha256);
            GivenArtifactDownload(artifact);

            Subject.CheckForUpdate();

            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Once);
        }

        // ── tests: sha256 mismatch ─────────────────────────────────────────────

        [Test]
        public void sha256_mismatch_aborts_without_replacing()
        {
            Mocker.GetMock<IGcdMetadataService>()
                .Setup(s => s.LocalDumpVersion())
                .Returns(OlderVersion);

            var artifact = BuildArtifactBytes(RemoteVersion);

            // Intentionally wrong sha256.
            GivenManifest(RemoteVersion, "0000000000000000000000000000000000000000000000000000000000000000");
            GivenArtifactDownload(artifact);

            Subject.CheckForUpdate();

            // Override artifact must NOT exist — the temp file was discarded.
            File.Exists(_overridePath).Should().BeFalse();
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Never);

            // The sha256 mismatch is reported as a Warn by design (fail-soft path).
            ExceptionVerification.ExpectedWarns(1);
        }

        // ── tests: config gate ─────────────────────────────────────────────────

        [Test]
        public void auto_update_disabled_skips_all_http_calls()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.MetadataAutoUpdate)
                .Returns(false);
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(true);

            Subject.CheckForUpdate();

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Get(It.IsAny<HttpRequest>()), Times.Never);
        }

        // Beta readiness fix round (2026-09-28, F1): without a catalogue the check is the initial download and
        // runs with auto-update off -- the startup push and the hourly retry both go through CheckForUpdate().
        [Test]
        public void auto_update_disabled_still_downloads_a_missing_catalogue()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.MetadataAutoUpdate)
                .Returns(false);
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(false);

            var artifact = BuildArtifactBytes(RemoteVersion);
            GivenManifest(RemoteVersion, Sha256Hex(artifact));
            GivenArtifactDownload(artifact);

            Subject.CheckForUpdate();

            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Once());
            File.Exists(_overridePath).Should().BeTrue();
        }

        [Test]
        public void startup_without_a_catalogue_queues_a_fetch_with_auto_update_off()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.MetadataAutoUpdate).Returns(false);
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(false);

            Subject.HandleAsync(new ApplicationStartedEvent());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(It.IsAny<MetadataUpdateCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        // ── tests: fail-soft ───────────────────────────────────────────────────

        [Test]
        public void manifest_fetch_failure_does_not_throw()
        {
            Mocker.GetMock<IGcdMetadataService>()
                .Setup(s => s.LocalDumpVersion())
                .Returns(OlderVersion);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Throws(new Exception("network error"));

            // Should swallow the exception.
            Action act = () => Subject.CheckForUpdate();
            act.Should().NotThrow();

            // ...and log it as a Warn, which is the whole point of fail-soft.
            ExceptionVerification.ExpectedWarns(1);
        }

        // ── tests: first start without a catalogue (beta readiness F1) ─────────

        [Test]
        public void startup_without_a_catalogue_queues_a_metadata_update()
        {
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(false);

            Subject.HandleAsync(new ApplicationStartedEvent());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(It.IsAny<MetadataUpdateCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void startup_with_a_catalogue_queues_nothing()
        {
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(true);

            Subject.HandleAsync(new ApplicationStartedEvent());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(It.IsAny<MetadataUpdateCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void hourly_retries_without_a_catalogue_warn_once()
        {
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(false);
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.ServiceUnavailable));

            Subject.CheckForUpdate();
            Subject.CheckForUpdate();
            Subject.CheckForUpdate();

            ExceptionVerification.ExpectedWarns(1);
        }

        // Review M7: a manual run is a user asking; it always warns.
        [Test]
        public void a_forced_check_without_a_catalogue_always_warns()
        {
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(false);
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.ServiceUnavailable));

            Subject.CheckForUpdate();
            Subject.CheckForUpdate(force: true);
            Subject.CheckForUpdate(force: true);
            Subject.CheckForUpdate();

            ExceptionVerification.ExpectedWarns(3);
        }

        [Test]
        public void failures_with_a_catalogue_warn_every_time()
        {
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(true);
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.LocalDumpVersion()).Returns(OlderVersion);
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.ServiceUnavailable));

            Subject.CheckForUpdate();
            Subject.CheckForUpdate();

            ExceptionVerification.ExpectedWarns(2);
        }

        // ── tests: the stable manifest URL with the GitHub fallback (E2) ───────
        // Both URLs end in version.json, so these mocks key on the exact URL.

        private void GivenManifestAt(string url, Func<HttpRequest, HttpResponse> respond)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri == url)))
                .Returns(respond);
        }

        private byte[] GivenNewerCatalogueAt(string url)
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.LocalDumpVersion()).Returns(OlderVersion);

            var artifact = BuildArtifactBytes(RemoteVersion);
            var json = BuildManifestJson(RemoteVersion, Sha256Hex(artifact), "https://example.com/artifact");
            GivenManifestAt(url, r => JsonResponse(r, json));
            GivenArtifactDownload(artifact);

            return artifact;
        }

        private void VerifyManifestFetched(string url, Times times)
        {
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri == url)), times);
        }

        [Test]
        public void the_primary_manifest_is_used_and_the_fallback_never_asked()
        {
            var artifact = GivenNewerCatalogueAt(MetadataUpdateService.DefaultManifestUrl);

            Subject.CheckForUpdate();

            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Once);
            File.ReadAllBytes(_overridePath).Should().Equal(artifact);
            VerifyManifestFetched(MetadataUpdateService.FallbackManifestUrl, Times.Never());
        }

        [Test]
        public void the_primary_manifest_is_asked_with_a_timeout_of_at_most_15_seconds()
        {
            HttpRequest sent = null;
            GivenManifestAt(MetadataUpdateService.DefaultManifestUrl, r =>
            {
                sent = r;
                return new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.NotFound);
            });

            Subject.CheckForUpdate();

            sent.Should().NotBeNull();
            sent.RequestTimeout.Should().BeGreaterThan(TimeSpan.Zero);
            sent.RequestTimeout.Should().BeLessOrEqualTo(TimeSpan.FromSeconds(15));
            sent.AllowAutoRedirect.Should().BeTrue();

            // The unmocked fallback answers nothing: today's failure, one Warn.
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_primary_404_falls_back_to_the_github_manifest_quietly()
        {
            GivenManifestAt(MetadataUpdateService.DefaultManifestUrl, r => new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.NotFound));
            var artifact = GivenNewerCatalogueAt(MetadataUpdateService.FallbackManifestUrl);

            Subject.CheckForUpdate();

            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Once);
            File.ReadAllBytes(_overridePath).Should().Equal(artifact);
            VerifyManifestFetched(MetadataUpdateService.DefaultManifestUrl, Times.Once());
            VerifyManifestFetched(MetadataUpdateService.FallbackManifestUrl, Times.Once());
            Mocker.GetMock<IConfigService>()
                .VerifySet(c => c.MetadataLastCheckResult = It.Is<string>(v => v.Contains("updated")), Times.Once);
        }

        [TestCase("<html>not json</html>")]
        [TestCase("{\"gcd_dump\": \"2026-06-01.sql\"}")]
        public void a_primary_that_is_not_a_complete_manifest_falls_back(string body)
        {
            GivenManifestAt(MetadataUpdateService.DefaultManifestUrl, r => JsonResponse(r, body));
            GivenNewerCatalogueAt(MetadataUpdateService.FallbackManifestUrl);

            Subject.CheckForUpdate();

            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Once);
            VerifyManifestFetched(MetadataUpdateService.FallbackManifestUrl, Times.Once());
        }

        [Test]
        public void a_primary_timeout_falls_back()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri == MetadataUpdateService.DefaultManifestUrl)))
                .Throws(new WebException("The operation has timed out.", WebExceptionStatus.Timeout));
            GivenNewerCatalogueAt(MetadataUpdateService.FallbackManifestUrl);

            Subject.CheckForUpdate();

            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Once);
        }

        [Test]
        public void both_manifests_failing_is_todays_failure_one_warn_and_the_fallback_recorded()
        {
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(true);
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.LocalDumpVersion()).Returns(OlderVersion);
            GivenManifestAt(MetadataUpdateService.DefaultManifestUrl, r => new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.NotFound));
            GivenManifestAt(MetadataUpdateService.FallbackManifestUrl, r => new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.ServiceUnavailable));

            Subject.CheckForUpdate();

            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Never);
            Mocker.GetMock<IConfigService>()
                .VerifySet(c => c.MetadataLastCheckResult = It.Is<string>(v => v.Contains("manifest fetch failed (status ServiceUnavailable) from " + MetadataUpdateService.FallbackManifestUrl)), Times.Once);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_configured_manifest_url_is_the_only_one_asked()
        {
            const string configured = "https://catalogue.example.org/version.json";
            Mocker.GetMock<IConfigService>().SetupGet(c => c.MetadataManifestUrl).Returns(configured);
            GivenManifestAt(configured, r => new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.NotFound));

            Subject.CheckForUpdate();

            VerifyManifestFetched(configured, Times.Once());
            VerifyManifestFetched(MetadataUpdateService.DefaultManifestUrl, Times.Never());
            VerifyManifestFetched(MetadataUpdateService.FallbackManifestUrl, Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        // ── tests: same label, different sha256 (D5) ───────────────────────────
        // A republish under the same per-build label is visible only through the manifest's
        // sha256. The local file is the one LocalDumpVersion() read (ArtifactInfo().Path), not
        // necessarily OverrideArtifactPath; it is never SQLite-validated, only the download is.

        [Test]
        public void same_label_with_different_sha256_downloads_and_reloads()
        {
            Mocker.GetMock<IGcdMetadataService>()
                .Setup(s => s.LocalDumpVersion())
                .Returns(RemoteVersion);

            GivenLocalArtifact(Encoding.UTF8.GetBytes("stale build published under the same label"));

            var artifact = BuildArtifactBytes(RemoteVersion);
            GivenManifest(RemoteVersion, Sha256Hex(artifact));
            GivenArtifactDownload(artifact);

            Subject.CheckForUpdate();

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("artifact"))), Times.Once);
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Once);
            Mocker.GetMock<IConfigService>()
                .VerifySet(c => c.MetadataLastCheckResult = It.Is<string>(v => v.Contains("same label, different sha256")), Times.Once);
            File.ReadAllBytes(_overridePath).Should().Equal(artifact);
        }

        [Test]
        public void same_label_with_equal_sha256_records_up_to_date()
        {
            Mocker.GetMock<IGcdMetadataService>()
                .Setup(s => s.LocalDumpVersion())
                .Returns(RemoteVersion);

            var artifact = BuildArtifactBytes(RemoteVersion);
            GivenLocalArtifact(artifact);

            // Case must not matter: publish.sh writes lowercase hex, but nothing guarantees it.
            GivenManifest(RemoteVersion, Sha256Hex(artifact).ToUpperInvariant());

            Subject.CheckForUpdate();

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("artifact"))), Times.Never);
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Never);
            Mocker.GetMock<IConfigService>()
                .VerifySet(c => c.MetadataLastCheckResult = It.Is<string>(v => v.Contains("up to date")), Times.Once);
        }

        [Test]
        public void same_label_with_different_sha256_hashes_the_loaded_artifact_not_the_override_path()
        {
            // The loaded artifact is the baked-in copy: ArtifactInfo().Path is elsewhere and nothing
            // exists at OverrideArtifactPath. Hashing the override path would find no file and miss
            // the republish.
            Mocker.GetMock<IGcdMetadataService>()
                .Setup(s => s.LocalDumpVersion())
                .Returns(RemoteVersion);

            var bakedPath = Path.Combine(TempFolder, "baked", "manga-metadata.sqlite");
            GivenLocalArtifact(Encoding.UTF8.GetBytes("baked-in build under the same label"), bakedPath);

            var artifact = BuildArtifactBytes(RemoteVersion);
            GivenManifest(RemoteVersion, Sha256Hex(artifact));
            GivenArtifactDownload(artifact);

            Subject.CheckForUpdate();

            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Reload(), Times.Once);
            File.ReadAllBytes(_overridePath).Should().Equal(artifact);
            File.ReadAllText(bakedPath).Should().Be("baked-in build under the same label");
        }
    
        // ---- IsNewer: version labels are opaque, not lexicographically sortable ----
        // The previous implementation was string.Compare(remote, local, Ordinal) <= 0.
        // These cases are exactly the ones it got wrong.

        [TestCase("2026-06-01.sql", "2026-05-01.sql", true,  Description = "later dump is newer")]
        [TestCase("2026-05-01.sql", "2026-06-01.sql", false, Description = "earlier dump is not")]
        [TestCase("2026-06-01.sql", "2026-06-01.sql", false, Description = "same version")]
        [TestCase("opentome-2026-08-27", "opentome-2026-08-20", true,  Description = "dated labels compare by date")]
        [TestCase("opentome-2026-08-20", "opentome-2026-08-27", false, Description = "older dated label rejected")]
        // Ordinal compare said "2026-06-01.sql" > "opentome-..." is FALSE ('2' < 'o'),
        // so a switch to the new publisher would have been refused forever.
        [TestCase("opentome-2026-08-27", "2026-06-01.sql", true,  Description = "cross-publisher switch by date")]
        [TestCase("bundle-alpha", "bundle-zulu", true,  Description = "undated + different => take manifest")]
        [TestCase("bundle-alpha", "bundle-alpha", false, Description = "undated + identical => no update")]
        [TestCase("", "anything", false, Description = "empty remote never updates")]
        public void IsNewer_compares_versions_correctly(string remote, string local, bool expected)
        {
            MetadataUpdateService.IsNewer(remote, local).Should().Be(expected);
        }

        // ---- IsNewer + sha256 (D5): equal labels compare hashes; the local hash is a factory ----
        // invoked only on the equal-label path and at most once, because it reads the whole file.

        [Test]
        public void IsNewer_same_label_and_different_sha256_is_newer()
        {
            var calls = 0;

            MetadataUpdateService.IsNewer(SameLabel, SameLabel, "aaaa", () => { calls++; return "bbbb"; })
                .Should().BeTrue();

            calls.Should().Be(1);
        }

        [Test]
        public void IsNewer_same_label_and_equal_sha256_is_not_newer_ignoring_case()
        {
            var calls = 0;

            MetadataUpdateService.IsNewer(SameLabel, SameLabel, "ABCDEF", () => { calls++; return "abcdef"; })
                .Should().BeFalse();

            calls.Should().Be(1);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void IsNewer_same_label_and_blank_remote_sha256_is_not_newer_without_hashing(string remoteSha256)
        {
            var calls = 0;

            MetadataUpdateService.IsNewer(SameLabel, SameLabel, remoteSha256, () => { calls++; return "bbbb"; })
                .Should().BeFalse();

            calls.Should().Be(0);
        }

        [Test]
        public void IsNewer_same_label_and_no_local_artifact_is_not_newer()
        {
            // No file to hash (the factory returns null) is "cannot tell", not "different".
            MetadataUpdateService.IsNewer(SameLabel, SameLabel, "aaaa", () => null).Should().BeFalse();
        }

        [Test]
        public void IsNewer_two_argument_overload_treats_same_label_as_not_newer()
        {
            MetadataUpdateService.IsNewer(SameLabel, SameLabel).Should().BeFalse();
        }

        [TestCase("opentome-2026-09-18", "opentome-2026-09-17", true,  Description = "later date wins, sha not consulted")]
        [TestCase("opentome-2026-09-17", "opentome-2026-09-18", false, Description = "earlier date loses, sha not consulted")]
        [TestCase("opentome-2026-08-27", "2026-06-01.sql", true, Description = "cross-publisher switch by date")]
        [TestCase("bundle-alpha", "bundle-zulu", true, Description = "undated + different label => take manifest")]
        public void IsNewer_different_labels_use_the_date_rule_and_never_hash(string remote, string local, bool expected)
        {
            var calls = 0;

            // The factory would report the hashes as equal; the date rule must not care.
            MetadataUpdateService.IsNewer(remote, local, "aaaa", () => { calls++; return "aaaa"; })
                .Should().Be(expected);

            calls.Should().Be(0);
        }
}
}
