using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.MetadataSource
{
    // Pillar 2 / #2: MangaUpdates volume authority. MangaUpdates is manga-specialized and exposes a
    // volume count + completion flag for BOTH complete and ONGOING series (AniList's volume count is
    // null while a series is releasing). The count is the original-language tankoubon count, so it
    // agrees with AniList for finished series; its real added value is a clean count for ongoing
    // series and coverage when AniList doesn't resolve. Convention-registered, fail-soft (returns
    // null, never throws).
    public class MangaUpdatesSeries
    {
        public bool Completed { get; set; }
        public int VolumeCount { get; set; }   // 0 when unknown / unparseable
    }

    public interface IMangaUpdatesService
    {
        MangaUpdatesSeries FindSeries(string title, bool relaxed = false);
    }

    public class MangaUpdatesService : IMangaUpdatesService
    {
        private const string SearchUrl = "https://api.mangaupdates.com/v1/series/search";
        private const string SeriesUrl = "https://api.mangaupdates.com/v1/series/";

        // The LEADING "N Volume(s)" of the status string ("24 Volumes (Complete)",
        // "2 Volumes (Ongoing)"). Anchored so "Part 1: 11 Volumes ..." sub-lines are ignored.
        private static readonly Regex VolumeCountRegex = new Regex(@"^(\d{1,4})\s+volumes?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public MangaUpdatesService(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        // Total tankoubon count from a MangaUpdates status string, or 0 if absent/unparseable
        // (null, "Complete" with no number, "Oneshot", etc. -> 0 so the caller falls through).
        public static int ParseVolumeCount(string status)
        {
            if (status.IsNullOrWhiteSpace())
            {
                return 0;
            }

            var match = VolumeCountRegex.Match(status.TrimStart());
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        public MangaUpdatesSeries FindSeries(string title, bool relaxed = false)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return null;
            }

            try
            {
                SeriesDetail bestDetail = null;
                var bestScore = 0m;

                // MangaUpdates ranks fuzzily and its primary titles are romanized, so validate each
                // candidate's detail record (title + associated names carry the licensed English
                // title) and take the first that actually matches the query. A wrong first hit
                // would attach another series' volume count — no data is safer than wrong data.
                // Relaxed (search-box) path: cap the crawl at 3 candidates — each detail is a
                // rate-limited round-trip and the search request is waiting on it.
                var candidateIds = SearchSeriesIds(title);

                foreach (var seriesId in relaxed ? candidateIds.Take(3) : candidateIds)
                {
                    var detailRequest = new HttpRequestBuilder(SeriesUrl + seriesId)
                        .WithRateLimit(1.0)
                        .Build();
                    detailRequest.SuppressHttpError = true;
                    detailRequest.RequestTimeout = TimeSpan.FromSeconds(15);

                    var detail = _httpClient.Get<SeriesDetail>(detailRequest)?.Resource;
                    if (detail == null)
                    {
                        continue;
                    }

                    var names = new List<string> { detail.Title };
                    if (detail.Associated != null)
                    {
                        names.AddRange(detail.Associated.Select(a => a.Title));
                    }

                    if (!Manga.TitleMatcher.Matches(title, names))
                    {
                        // SEARCH-path fallback (relaxed): remember the best token-similarity
                        // candidate at/above the floor (see TitleMatcher.SearchScore).
                        if (relaxed)
                        {
                            var score = Manga.TitleMatcher.SearchScore(title, names);

                            if (score >= Manga.TitleMatcher.SearchFloor && score > bestScore)
                            {
                                bestScore = score;
                                bestDetail = detail;
                            }
                        }

                        _logger.Debug("MangaUpdates: candidate '{0}' does not match query '{1}', skipping", detail.Title, title);
                        continue;
                    }

                    var result = new MangaUpdatesSeries
                    {
                        Completed = detail.Completed,
                        VolumeCount = ParseVolumeCount(detail.Status)
                    };

                    _logger.Debug("MangaUpdates '{0}': completed={1} volumes={2}", title, result.Completed, result.VolumeCount);
                    return result;
                }

                if (bestDetail != null)
                {
                    _logger.Debug("MangaUpdates: relaxed search match '{0}' for query '{1}'", bestDetail.Title, title);

                    return new MangaUpdatesSeries
                    {
                        Completed = bestDetail.Completed,
                        VolumeCount = ParseVolumeCount(bestDetail.Status)
                    };
                }

                _logger.Debug("MangaUpdates: no validated match for '{0}'", title);
                return null;
            }
            catch (Exception ex)
            {
                // Metadata-source failure must never break discovery — degrade to null.
                _logger.Debug(ex, "MangaUpdates lookup failed for '{0}'", title);
                return null;
            }
        }

        private List<long> SearchSeriesIds(string title)
        {
            var request = new HttpRequestBuilder(SearchUrl)
                .SetHeader("Content-Type", "application/json")
                .WithRateLimit(1.0)
                .Build();

            request.SetContent(new SearchRequest { Search = title, PerPage = 5 }.ToJson());
            request.SuppressHttpError = true;
            request.RequestTimeout = TimeSpan.FromSeconds(15);

            var response = _httpClient.Post<SearchResponse>(request)?.Resource;

            return response?.Results?
                .Where(r => r.Record != null)
                .Select(r => r.Record.SeriesId)
                .ToList() ?? new List<long>();
        }

        private class SearchRequest
        {
            [JsonProperty("search")]
            public string Search { get; set; }

            [JsonProperty("perpage")]
            public int PerPage { get; set; }
        }

        private class SearchResponse
        {
            [JsonProperty("results")]
            public List<SearchResult> Results { get; set; }
        }

        private class SearchResult
        {
            [JsonProperty("record")]
            public SearchRecord Record { get; set; }
        }

        private class SearchRecord
        {
            [JsonProperty("series_id")]
            public long SeriesId { get; set; }
        }

        private class SeriesDetail
        {
            [JsonProperty("title")]
            public string Title { get; set; }

            [JsonProperty("associated")]
            public List<AssociatedTitle> Associated { get; set; }

            [JsonProperty("status")]
            public string Status { get; set; }

            [JsonProperty("completed")]
            public bool Completed { get; set; }
        }

        private class AssociatedTitle
        {
            [JsonProperty("title")]
            public string Title { get; set; }
        }
    }
}
