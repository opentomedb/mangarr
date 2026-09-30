using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Beta polish (2026-09-28): a notice, never an updater. Reads Mangarr's latest GitHub releases
    // (prereleases included, unauthenticated) at most every 12 hours and warns when one carries a build
    // number above the running one. The number comes from the release body's "Build: 10.0.0.N" line,
    // which docker.yml's push job writes; a release without that line is ignored. Any failure (network,
    // non-200, rate limit, bad JSON) is a Debug line and no health entry, and the attempt still counts
    // toward the 12 hours (the last good answer is kept). Settings -> General -> Check for New Releases off = no request at all.
    [CheckOn(typeof(ConfigSavedEvent))]
    public class NewReleaseCheck : HealthCheckBase
    {
        public const string ReleasesUrl = "https://api.github.com/repos/opentomedb/mangarr/releases?per_page=5";
        public const string ReleasePageUrl = "https://github.com/opentomedb/mangarr/releases/tag/";

        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);
        private static readonly Regex BuildLineRegex = new Regex(@"^[ \t]*Build:[ \t]*(?<build>\d+\.\d+\.\d+\.\d+)[ \t]*\r?$",
                                                                 RegexOptions.Multiline | RegexOptions.Compiled);

        private readonly IConfigService _configService;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly object _lock = new object();

        private DateTime _lastAttempt = DateTime.MinValue;
        private AvailableRelease _latest;

        public NewReleaseCheck(IConfigService configService,
                               IHttpClient httpClient,
                               ILocalizationService localizationService,
                               Logger logger)
            : base(localizationService)
        {
            _configService = configService;
            _httpClient = httpClient;
            _logger = logger;
        }

        public override HealthCheck Check()
        {
            if (!_configService.CheckForNewReleases)
            {
                return new HealthCheck(GetType());
            }

            var latest = Latest();

            if (latest == null || latest.Build <= BuildInfo.Version)
            {
                return new HealthCheck(GetType());
            }

            return new HealthCheck(GetType(),
                                   HealthCheckResult.Warning,
                                   _localizationService.GetLocalizedString("NewReleaseCheckMessage", new Dictionary<string, object>
                                   {
                                       { "release", latest.Name },
                                       { "version", BuildInfo.Version.ToString() }
                                   }),
                                   latest.Url);
        }

        // The build number a release body declares, or null when it has no "Build: a.b.c.d" line.
        public static Version ParseBuild(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return null;
            }

            var match = BuildLineRegex.Match(body);

            return match.Success && Version.TryParse(match.Groups["build"].Value, out var build) ? build : null;
        }

        private AvailableRelease Latest()
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _lastAttempt < CheckInterval)
                {
                    return _latest;
                }

                _lastAttempt = DateTime.UtcNow;

                // A failed fetch keeps the last answer; a good one replaces it, even with "nothing", so a
                // pulled release stops being announced.
                if (TryFetch(out var latest))
                {
                    _latest = latest;
                }

                return _latest;
            }
        }

        private bool TryFetch(out AvailableRelease latest)
        {
            latest = null;

            try
            {
                var request = new HttpRequestBuilder(ReleasesUrl).Accept(HttpAccept.Json).Build();
                request.SuppressHttpError = true;
                request.RequestTimeout = TimeSpan.FromSeconds(15);

                var response = _httpClient.Get(request);

                if (response == null || response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Debug("Release check: {0} answered {1}", ReleasesUrl, response?.StatusCode);
                    return false;
                }

                var releases = JsonConvert.DeserializeObject<List<GitHubRelease>>(response.Content);

                if (releases == null)
                {
                    _logger.Debug("Release check: {0} answered no release list", ReleasesUrl);
                    return false;
                }

                latest = releases.Where(r => r != null && !r.Draft && !string.IsNullOrWhiteSpace(r.TagName))
                               .Select(r => new { Release = r, Build = ParseBuild(r.Body) })
                               .Where(r => r.Build != null)
                               .OrderByDescending(r => r.Build)
                               .Select(r => new AvailableRelease
                               {
                                   Name = r.Release.TagName.TrimStart('v'),
                                   Build = r.Build,
                                   Url = ReleasePageUrl + Uri.EscapeDataString(r.Release.TagName)
                               })
                               .FirstOrDefault();

                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Release check: {0} failed", ReleasesUrl);
                return false;
            }
        }

        private class GitHubRelease
        {
            [JsonProperty("tag_name")]
            public string TagName { get; set; }

            [JsonProperty("body")]
            public string Body { get; set; }

            [JsonProperty("draft")]
            public bool Draft { get; set; }
        }

        private class AvailableRelease
        {
            public string Name { get; set; }
            public Version Build { get; set; }
            public string Url { get; set; }
        }
    }
}
