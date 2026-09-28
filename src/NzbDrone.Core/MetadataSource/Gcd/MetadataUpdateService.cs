using System;
using System.Data.SQLite;
using System.IO;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Dapper;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MetadataSource.Gcd
{
    // Scheduled in-app updater: checks a public GitHub Release for a newer GCD metadata artifact and
    // swaps it in atomically if the sha256 and SQLite integrity checks pass. Fail-soft: exceptions
    // inside CheckForUpdate are caught and logged, never re-thrown. Convention-registered.
    public interface IMetadataUpdateService
    {
        // force: a manual "Check now" runs even when auto-update is off.
        void CheckForUpdate(bool force = false);
    }

    public class MetadataUpdateService : IMetadataUpdateService, IExecute<MetadataUpdateCommand>, IHandleAsync<ApplicationStartedEvent>
    {
        // The built-in manifest (beta docs 2026-09-28, E2): a copy of the catalogue's version.json on
        // opentomedb.com, a URL that survives a GitHub account rename, tried first; the public GitHub
        // release it mirrors is the fallback on any failure (non-200, bad JSON, timeout). The artifact
        // itself is always the manifest's artifact_url (the GitHub release asset). A configured
        // MetadataManifestUrl (Settings -> Metadata Source) replaces both, with no fallback, which is
        // how a privately hosted OpenTome release reaches the app without publishing anything.
        public const string DefaultManifestUrl = "https://opentomedb.com/catalogue/version.json";

        public const string FallbackManifestUrl =
            "https://github.com/opentomedb/mangarr-metadata/releases/download/metadata/version.json";

        private static readonly TimeSpan PrimaryManifestTimeout = TimeSpan.FromSeconds(15);

        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly IGcdMetadataService _gcdMetadataService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        // Beta readiness (2026-09-28, F1): while no catalogue is loaded the check retries hourly
        // (TaskManager); only the first failure of such a streak is a Warn, the rest Debug.
        private bool _quietWhileAbsent;

        // Review M7: a manual run (System -> Tasks -> Run) always warns; the de-spam is for scheduled retries.
        private bool _forced;

        public MetadataUpdateService(
            IHttpClient httpClient,
            IConfigService configService,
            IGcdMetadataService gcdMetadataService,
            IManageCommandQueue commandQueueManager,
            Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _gcdMetadataService = gcdMetadataService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Execute(MetadataUpdateCommand message)
        {
            CheckForUpdate(force: message.Trigger == CommandTrigger.Manual);
        }

        // Beta readiness (2026-09-28, F1): the image bakes no catalogue, and the daily task's first run
        // is 24 h after a new install's first start -- until then every light-novel add is refused
        // (NotInCatalogue). A start without a catalogue checks right away. Async, so it runs after
        // CommandQueueManager's own startup handler has requeued the stored commands.
        public void HandleAsync(ApplicationStartedEvent message)
        {
            if (_gcdMetadataService.Available)
            {
                return;
            }

            _logger.Info("No metadata catalogue is loaded; checking for one now (System -> Tasks -> Metadata Update)");
            _commandQueueManager.Push(new MetadataUpdateCommand());
        }

        // Warn for a failure, except a repeat while the catalogue is still absent (hourly retries).
        private void LogFailure(Exception ex, string message, params object[] args)
        {
            var absent = !_gcdMetadataService.Available;

            if (absent && _quietWhileAbsent && !_forced)
            {
                _logger.Debug(ex, message, args);
                return;
            }

            _logger.Warn(ex, message, args);
            _quietWhileAbsent = absent;
        }

        public void CheckForUpdate(bool force = false)
        {
            try
            {
                _forced = force;
                DoCheckForUpdate(force);
            }
            catch (Exception ex)
            {
                LogFailure(ex, "GCD metadata update check failed unexpectedly");
                Record("check failed: " + ex.Message);
            }
        }

        // The manifest to poll: the configured URL, else the built-in default.
        public string ManifestUrl => ResolveManifestUrl(_configService);

        // Shared with ProxyCheck, which probes the proxy against the same host (beta readiness, E5).
        public static string ResolveManifestUrl(IConfigService configService)
        {
            var configured = configService.MetadataManifestUrl;
            return configured.IsNullOrWhiteSpace() ? DefaultManifestUrl : configured.Trim();
        }

        // Last-check outcome for the settings page. Best-effort: a failure to record must never
        // turn a successful check into a failed one.
        private void Record(string result)
        {
            try
            {
                _configService.MetadataLastCheckResult = DateTime.UtcNow.ToString("u") + ": " + result;
            }
            catch (Exception)
            {
            }
        }

        private void DoCheckForUpdate(bool force)
        {
            // Beta readiness fix round (2026-09-28, F1): with no catalogue loaded this is the initial download,
            // not an update -- the startup check and the hourly retry run whatever MetadataAutoUpdate says. Once a
            // catalogue exists, auto-update governs as before.
            if (!force && !_configService.MetadataAutoUpdate && _gcdMetadataService.Available)
            {
                _logger.Debug("GCD metadata auto-update is disabled; skipping check");
                return;
            }

            // Fetch the manifest: the built-in primary first (E2), then the URL the check has always used --
            // the GitHub fallback, or the configured URL alone -- with today's failure handling.
            var manifestUrl = ManifestUrl;
            MetadataManifest manifest = null;

            if (_configService.MetadataManifestUrl.IsNullOrWhiteSpace())
            {
                manifest = TryFetchPrimaryManifest();
                manifestUrl = manifest == null ? FallbackManifestUrl : DefaultManifestUrl;
            }

            if (manifest == null)
            {
                var manifestResponse = _httpClient.Get(BuildManifestRequest(manifestUrl, TimeSpan.FromSeconds(30)));
                if (manifestResponse == null || manifestResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    LogFailure(null, "GCD metadata manifest fetch failed (status {0})", manifestResponse?.StatusCode);
                    Record($"manifest fetch failed (status {manifestResponse?.StatusCode.ToString() ?? "none"}) from {manifestUrl}");
                    return;
                }

                manifest = JsonConvert.DeserializeObject<MetadataManifest>(manifestResponse.Content);
                if (!IsComplete(manifest))
                {
                    LogFailure(null, "GCD metadata manifest is malformed; skipping update");
                    Record("manifest is malformed at " + manifestUrl);
                    return;
                }
            }

            // Compare remote version against the local artifact.
            var localVersion = _gcdMetadataService.LocalDumpVersion();
            if (localVersion != null && !IsNewer(manifest.GcdDump, localVersion, manifest.Sha256, LocalArtifactSha256))
            {
                _logger.Debug("GCD metadata is up to date (local={0} remote={1})", localVersion, manifest.GcdDump);
                Record($"up to date (local {localVersion}, manifest offers {manifest.GcdDump})");
                return;
            }

            // Only the sha256 branch of IsNewer lets an equal label through (D5).
            if (string.Equals(manifest.GcdDump, localVersion, StringComparison.Ordinal))
            {
                _logger.Debug("GCD metadata: same label {0}, different sha256 — treating as newer", manifest.GcdDump);
                Record("same label, different sha256 — treating as newer");
            }

            // A retry after a failed first download stays quiet too (F1).
            _logger.Log(localVersion == null && _quietWhileAbsent && !_forced ? LogLevel.Debug : LogLevel.Info,
                "GCD metadata update available: {0} → {1}; downloading", localVersion ?? "(none)", manifest.GcdDump);

            var targetPath = _gcdMetadataService.OverrideArtifactPath;
            var overrideDir = Path.GetDirectoryName(targetPath);

            // Ensure the override directory exists so we can write to it.
            if (!Directory.Exists(overrideDir))
            {
                Directory.CreateDirectory(overrideDir);
            }

            var tempPath = targetPath + ".tmp";

            try
            {
                // Download to a temp file.
                DownloadToFile(manifest.ArtifactUrl, tempPath);

                // Verify sha256.
                var actualHash = ComputeSha256(tempPath);
                if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    LogFailure(
                        null,
                        "GCD metadata download sha256 mismatch (expected={0} actual={1}); discarding",
                        manifest.Sha256, actualHash);
                    SafeDelete(tempPath);
                    Record("download of " + manifest.GcdDump + " discarded: sha256 mismatch");
                    return;
                }

                // Verify it opens as a valid SQLite DB with a meta table and gcd_dump row.
                if (!ValidateSqlite(tempPath, manifest.GcdDump))
                {
                    LogFailure(null, "GCD metadata download failed SQLite validation; discarding");
                    SafeDelete(tempPath);
                    Record("download of " + manifest.GcdDump + " discarded: not a valid artifact");
                    return;
                }

                // Atomically replace the artifact and reload.
                File.Move(tempPath, targetPath, overwrite: true);
                _gcdMetadataService.Reload();
                _logger.Info("Updated GCD metadata to {0}", manifest.GcdDump);
                _quietWhileAbsent = false;
                Record($"updated {localVersion ?? "(none)"} -> {manifest.GcdDump}");
            }
            catch (Exception ex)
            {
                LogFailure(ex, "GCD metadata download or verification failed");
                SafeDelete(tempPath);
                Record("download or verification failed: " + ex.Message);
            }
        }

        private static HttpRequest BuildManifestRequest(string url, TimeSpan timeout)
        {
            var request = new HttpRequestBuilder(url)
                .WithRateLimit(2.0)
                .Build();
            request.SuppressHttpError = true;
            request.RequestTimeout = timeout;

            // GitHub release asset URLs 302-redirect to the object CDN; HttpRequestBuilder defaults
            // AllowAutoRedirect to false, so we must opt in or the fetch returns the redirect itself.
            request.AllowAutoRedirect = true;

            return request;
        }

        private static bool IsComplete(MetadataManifest manifest)
        {
            return manifest != null && !string.IsNullOrWhiteSpace(manifest.GcdDump) &&
                   !string.IsNullOrWhiteSpace(manifest.ArtifactUrl) && !string.IsNullOrWhiteSpace(manifest.Sha256);
        }

        // The primary manifest (E2), or null on any failure -- a non-200, a body that is not a complete
        // manifest, a timeout or a network error -- so the caller falls back. Debug only: the site copy
        // may not exist yet, and a real outage is reported by the fallback's own failure.
        private MetadataManifest TryFetchPrimaryManifest()
        {
            try
            {
                var response = _httpClient.Get(BuildManifestRequest(DefaultManifestUrl, PrimaryManifestTimeout));
                if (response == null || response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Debug("Catalogue manifest {0} answered {1}; trying {2}", DefaultManifestUrl, response?.StatusCode, FallbackManifestUrl);
                    return null;
                }

                var manifest = JsonConvert.DeserializeObject<MetadataManifest>(response.Content);
                if (!IsComplete(manifest))
                {
                    _logger.Debug("Catalogue manifest {0} is malformed; trying {1}", DefaultManifestUrl, FallbackManifestUrl);
                    return null;
                }

                return manifest;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Catalogue manifest {0} failed; trying {1}", DefaultManifestUrl, FallbackManifestUrl);
                return null;
            }
        }

        private void DownloadToFile(string url, string destPath)
        {
            var request = new HttpRequestBuilder(url)
                .WithRateLimit(2.0)
                .Build();
            request.SuppressHttpError = true;
            request.RequestTimeout = TimeSpan.FromSeconds(300);

            // Follow the GitHub release asset 302 → object CDN (see manifest fetch above).
            request.AllowAutoRedirect = true;

            var response = _httpClient.Get(request);
            if (response == null || response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception($"Artifact download failed with status {response?.StatusCode}");
            }

            File.WriteAllBytes(destPath, response.ResponseData);
        }

        // sha256 of the artifact LocalDumpVersion() read from — the resolved path, which may be the
        // baked-in copy or a fallback *.sqlite rather than OverrideArtifactPath. Null when there is
        // nothing to hash, so a missing file is "cannot tell", never "different".
        private string LocalArtifactSha256()
        {
            var path = _gcdMetadataService.ArtifactInfo()?.Path;
            return path.IsNotNullOrWhiteSpace() && File.Exists(path) ? ComputeSha256(path) : null;
        }

        private static string ComputeSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static bool ValidateSqlite(string filePath, string expectedVersion)
        {
            try
            {
                var connectionString = new SQLiteConnectionStringBuilder
                {
                    DataSource = filePath,
                    ReadOnly = true,
                    FailIfMissing = true
                }.ConnectionString;

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    var version = conn.QueryFirstOrDefault<string>(
                        "SELECT value FROM meta WHERE key = 'gcd_dump' LIMIT 1");
                    return string.Equals(version, expectedVersion, StringComparison.Ordinal);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Version strings are opaque publisher-chosen labels, not a sortable sequence.
        // This used to be string.Compare(..., Ordinal) <= 0, which is correct only when
        // the labels happen to sort lexicographically by recency.
        //
        // To be precise about the risk: the current "2026-06-01.sql" -> "opentome-..."
        // transition DOES work under ordinal compare, by luck ('o' > '2'). The hazard is
        // that any future label sorting below the installed one is then refused forever
        // -- "OpenTome-2027-01-01" (capital O, 0x4F) sorts BELOW "opentome-..." (0x6F)
        // and would never install. The failure is silent, because a stale artifact keeps
        // answering every query perfectly well.
        //
        // Prefer a real date when both labels contain one; otherwise treat any difference
        // from the manifest as an update, because the manifest is the publisher's
        // statement of what is current.
        //
        // Equal labels compare the manifest's sha256 with the local artifact's (D5): a
        // republish under the same per-build label replaces the assets without renaming
        // them, and used to be invisible here. The local hash reads the whole file, so it is
        // a factory — invoked only on the equal-label path and at most once per check. Blank
        // on either side is "cannot tell" and keeps the old answer (equal label = not newer).
        internal static bool IsNewer(string remote, string local)
        {
            return IsNewer(remote, local, null, null);
        }

        internal static bool IsNewer(string remote, string local, string remoteSha256, Func<string> localSha256)
        {
            if (string.IsNullOrWhiteSpace(remote))
            {
                return false;
            }

            if (string.Equals(remote, local, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(remoteSha256) || localSha256 == null)
                {
                    return false;
                }

                var localHash = localSha256();
                return localHash.IsNotNullOrWhiteSpace() &&
                       !string.Equals(remoteSha256, localHash, StringComparison.OrdinalIgnoreCase);
            }

            if (TryExtractDate(remote, out var remoteDate) && TryExtractDate(local, out var localDate))
            {
                return remoteDate > localDate;
            }

            return true;
        }

        private static bool TryExtractDate(string value, out DateTime date)
        {
            date = default;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var match = Regex.Match(value, @"(20\d{2})[-_]?(\d{2})[-_]?(\d{2})");
            if (!match.Success)
            {
                return false;
            }

            return DateTime.TryParseExact(
                match.Groups[1].Value + match.Groups[2].Value + match.Groups[3].Value,
                "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out date);
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Best-effort cleanup; ignore errors.
            }
        }

        private class MetadataManifest
        {
            [JsonProperty("gcd_dump")]
            public string GcdDump { get; set; }

            [JsonProperty("sha256")]
            public string Sha256 { get; set; }

            [JsonProperty("size")]
            public long Size { get; set; }

            [JsonProperty("artifact_url")]
            public string ArtifactUrl { get; set; }
        }
    }
}
