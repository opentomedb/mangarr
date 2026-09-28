using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource
{
    // Phase 3.5b: MangaDex live-count provider. Fills the gap AniList/MAL leave —
    // an authoritative volume count for ONGOING series (AniList returns null while a
    // series is still releasing). MangaDex's /aggregate endpoint maps chapters to
    // volumes and exposes the highest currently-published volume, which grows over time.
    //
    // Two-step: resolve the series UUID by title, then read the aggregate's highest
    // numbered volume key. EN track pinned (the volume set is language-dependent).
    // Fail-soft like the other providers: returns 0 / null, never throws into discovery.
    public class MangaDexResult
    {
        public int HighestVolume { get; set; }   // 0 if unknown
        public string CoverUrl { get; set; }      // optional cover fallback
    }

    // D6 (2026-09-16): one series' English-locale cover art, keyed by integer volume number. The
    // series is the MangaDex entry whose attributes.links.al equals the stored AniList id — never a
    // title match while any candidate carries a link (the audit's two title picks were both wrong).
    public class MangaDexCovers
    {
        public string MangaId { get; set; }
        public Dictionary<int, string> CoversByVolume { get; set; } = new Dictionary<int, string>();

        // The English volume-1 cover — the series poster's first candidate (D4). Null when absent.
        public string Volume1 => CoversByVolume.TryGetValue(1, out var url) ? url : null;
    }

    public interface IMangaDexService
    {
        MangaDexResult Lookup(string seriesTitle, bool relaxed = false);

        // en-locale covers by volume for the series bound to aniListId (title = the search term for
        // /manga; equality on it is the fallback only when no candidate carries links.al). Cached 7
        // days per AniList id once resolved (zero en covers included); null — not cached — when the
        // entry is not found, MangaDex answers a non-200, or anything throws. Fail-soft.
        MangaDexCovers GetEnglishCovers(int aniListId, string title);

        // Preferred Edition (2026-09-24): the same join, MangaDex's cover art for another locale ("fr",
        // "de", "ja"). Cached apart from the English answer.
        MangaDexCovers GetLocaleCovers(int aniListId, string title, string locale);
    }

    public class MangaDexService : IMangaDexService
    {
        private const string ApiUrl = "https://api.mangadex.org";
        private const string UploadsUrl = "https://uploads.mangadex.org";

        private readonly IHttpClient _httpClient;
        private readonly ICached<MangaDexCovers> _coverCache;
        private readonly Logger _logger;

        public MangaDexService(IHttpClient httpClient, ICacheManager cacheManager, Logger logger)
        {
            _httpClient = httpClient;
            _coverCache = cacheManager.GetCache<MangaDexCovers>(GetType(), "englishCovers");
            _logger = logger;
        }

        public MangaDexResult Lookup(string seriesTitle, bool relaxed = false)
        {
            if (seriesTitle.IsNullOrWhiteSpace())
            {
                return null;
            }

            try
            {
                var mangaId = ResolveId(seriesTitle, relaxed, out var coverFileName);
                if (mangaId.IsNullOrWhiteSpace())
                {
                    _logger.Debug("MangaDex: no match for '{0}'", seriesTitle);
                    return null;
                }

                var result = new MangaDexResult
                {
                    HighestVolume = HighestVolumeFor(mangaId)
                };

                if (coverFileName.IsNotNullOrWhiteSpace())
                {
                    // MangaDex cover art URL form: /covers/{mangaId}/{fileName}
                    result.CoverUrl = $"{UploadsUrl}/covers/{mangaId}/{coverFileName}";
                }

                _logger.Debug("MangaDex '{0}': id={1} highestVolume={2}", seriesTitle, mangaId, result.HighestVolume);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "MangaDex lookup failed for '{0}'", seriesTitle);
                return null;
            }
        }

        // GET /manga?title=...&limit=8&includes[]=cover_art -> first VALIDATED match id + cover
        // filename. MangaDex ranks fuzzily; taking an unvalidated first hit for a subtitle-heavy
        // query can attach a different franchise entry's volume count. A candidate counts as a
        // match only when one of its titles/altTitles equals the query (normalized).
        private string ResolveId(string title, bool relaxed, out string coverFileName)
        {
            coverFileName = null;

            var request = new HttpRequestBuilder(ApiUrl + "/manga")
                .AddQueryParam("title", title)
                .AddQueryParam("limit", "8")
                .AddQueryParam("contentRating[]", "safe")
                .AddQueryParam("contentRating[]", "suggestive")
                .AddQueryParam("contentRating[]", "erotica")
                .AddQueryParam("includes[]", "cover_art")
                .WithRateLimit(0.3)
                .Build();
            request.SuppressHttpError = true;
            request.RequestTimeout = TimeSpan.FromSeconds(15);

            var response = _httpClient.Get(request);
            if (response?.Content == null)
            {
                return null;
            }

            var json = JObject.Parse(response.Content);
            var data = json["data"] ?? new JArray();
            var match = data.FirstOrDefault(m => Manga.TitleMatcher.Matches(title, CandidateTitles(m)));

            // SEARCH-path fallback (relaxed): best token-similarity candidate at/above the
            // floor when nothing matched exactly (see TitleMatcher.SearchScore).
            if (match == null && relaxed)
            {
                match = data
                    .Select(m => new { Token = m, Score = Manga.TitleMatcher.SearchScore(title, CandidateTitles(m)) })
                    .Where(x => x.Score >= Manga.TitleMatcher.SearchFloor)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault()?.Token;
            }

            if (match == null)
            {
                return null;
            }

            coverFileName = match["relationships"]?
                .FirstOrDefault(r => (string)r["type"] == "cover_art")?["attributes"]?["fileName"]?.ToString();

            return match["id"]?.ToString();
        }

        public MangaDexCovers GetEnglishCovers(int aniListId, string title)
        {
            return GetCovers(aniListId, title, "en", aniListId.ToString(CultureInfo.InvariantCulture));
        }

        public MangaDexCovers GetLocaleCovers(int aniListId, string title, string locale)
        {
            if (locale.IsNullOrWhiteSpace())
            {
                return null;
            }

            var code = locale.Trim().ToLowerInvariant();

            return GetCovers(aniListId, title, code, aniListId.ToString(CultureInfo.InvariantCulture) + "|" + code);
        }

        // Preferred Edition (2026-09-24): GetEnglishCovers' body, the locale and cache key passed in. The
        // English key (the bare AniList id) and request are unchanged.
        private MangaDexCovers GetCovers(int aniListId, string title, string locale, string key)
        {
            if (aniListId <= 0 || title.IsNullOrWhiteSpace())
            {
                return null;
            }

            var cached = _coverCache.Find(key);
            if (cached != null)
            {
                return cached;
            }

            try
            {
                var mangaId = ResolveIdByAniListId(aniListId, title);
                if (mangaId.IsNullOrWhiteSpace())
                {
                    _logger.Debug("MangaDex: no entry with links.al={0} for '{1}'", aniListId, title);
                    return null;
                }

                var covers = FetchCovers(mangaId, locale);
                if (covers == null)
                {
                    // A non-200 on a cover page (429 under a refresh burst): not cached, next source.
                    _logger.Debug("MangaDex: cover pages for {0} ('{1}') did not answer", mangaId, title);
                    return null;
                }

                var result = new MangaDexCovers { MangaId = mangaId, CoversByVolume = covers };
                _coverCache.Set(key, result, TimeSpan.FromDays(7));

                _logger.Debug("MangaDex {0} covers for '{1}' (al {2} -> {3}): {4} volumes, volume 1 {5}", locale, title, aniListId, mangaId, covers.Count, result.Volume1 != null ? "yes" : "no");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "MangaDex {0} covers failed for '{1}' (al {2})", locale, title, aniListId);
                return null;
            }
        }

        // GET /manga?title=… -> the candidate whose attributes.links.al is the AniList id. limit=10 (the
        // series lookup above uses 8): the English title of a popular series can be drowned by
        // doujinshi in the top 5. Title equality (the same matcher Lookup uses) only when NO candidate
        // carries an AniList link at all. Null on a non-200 or no match.
        private string ResolveIdByAniListId(int aniListId, string title)
        {
            var request = new HttpRequestBuilder(ApiUrl + "/manga")
                .AddQueryParam("title", title)
                .AddQueryParam("limit", "10")
                .AddQueryParam("contentRating[]", "safe")
                .AddQueryParam("contentRating[]", "suggestive")
                .AddQueryParam("contentRating[]", "erotica")
                .WithRateLimit(0.3)
                .Build();
            request.SuppressHttpError = true;
            request.RequestTimeout = TimeSpan.FromSeconds(15);

            var response = _httpClient.Get(request);
            if (response == null || response.HasHttpError || response.Content == null)
            {
                return null;
            }

            var data = JObject.Parse(response.Content)["data"] as JArray ?? new JArray();
            var linked = data.Where(m => AniListLink(m).HasValue).ToList();
            var match = linked.FirstOrDefault(m => AniListLink(m) == aniListId);

            if (match == null && linked.Count == 0)
            {
                match = data.FirstOrDefault(m => Manga.TitleMatcher.Matches(title, CandidateTitles(m)));
            }

            return match?["id"]?.ToString();
        }

        // attributes.links.al is the AniList id as a string ("30598"); absent on many entries.
        private static int? AniListLink(JToken manga)
        {
            var raw = (string)manga["attributes"]?["links"]?["al"];

            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (int?)null;
        }

        // GET /cover?manga[]=<id>&locales[]=<locale>&limit=100&offset=<n>&order[volume]=asc, paginated on
        // total. attributes.volume is a string ("1", "1.1", "12", null): integer volumes only, the first
        // cover per volume wins (order[volume]=asc keeps MangaDex's own order within a volume). Null on
        // any non-200 page so a 429 is never cached as "no covers". Preferred Edition (2026-09-24,
        // ruling B3): the locale is the caller's ("en" for the English path), in the query and the filter.
        private Dictionary<int, string> FetchCovers(string mangaId, string locale)
        {
            var covers = new Dictionary<int, string>();
            var offset = 0;

            while (true)
            {
                var request = new HttpRequestBuilder(ApiUrl + "/cover")
                    .AddQueryParam("manga[]", mangaId)
                    .AddQueryParam("locales[]", locale)
                    .AddQueryParam("limit", "100")
                    .AddQueryParam("offset", offset.ToString(CultureInfo.InvariantCulture))
                    .AddQueryParam("order[volume]", "asc")
                    .WithRateLimit(0.3)
                    .Build();
                request.SuppressHttpError = true;
                request.RequestTimeout = TimeSpan.FromSeconds(15);

                var response = _httpClient.Get(request);
                if (response == null || response.HasHttpError || response.Content == null)
                {
                    return null;
                }

                var json = JObject.Parse(response.Content);
                var page = json["data"] as JArray ?? new JArray();

                foreach (var cover in page)
                {
                    var attributes = cover["attributes"];
                    var fileName = (string)attributes?["fileName"];

                    if (fileName.IsNullOrWhiteSpace() || !string.Equals((string)attributes["locale"], locale, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!decimal.TryParse((string)attributes["volume"], NumberStyles.Number, CultureInfo.InvariantCulture, out var volume) || volume <= 0 || volume != Math.Floor(volume))
                    {
                        continue;
                    }

                    var number = (int)volume;
                    if (!covers.ContainsKey(number))
                    {
                        covers[number] = $"{UploadsUrl}/covers/{mangaId}/{fileName}";
                    }
                }

                var total = (int?)json["total"] ?? 0;
                offset += page.Count;

                if (page.Count == 0 || offset >= total)
                {
                    return covers;
                }
            }
        }

        // Every title string a MangaDex manga carries: the language-keyed title map plus each
        // language-keyed altTitle map.
        private static IEnumerable<string> CandidateTitles(JToken manga)
        {
            var attributes = manga["attributes"];
            if (attributes == null)
            {
                yield break;
            }

            if (attributes["title"] is JObject titleMap)
            {
                foreach (var prop in titleMap.Properties())
                {
                    yield return (string)prop.Value;
                }
            }

            if (attributes["altTitles"] is JArray altTitles)
            {
                foreach (var alt in altTitles.OfType<JObject>())
                {
                    foreach (var prop in alt.Properties())
                    {
                        yield return (string)prop.Value;
                    }
                }
            }
        }

        // GET /manga/{id}/aggregate -> highest numbered volume key.
        // NO language filter on purpose: for licensed series the EN-only aggregate collapses
        // (English chapters are external/untagged, e.g. One Piece EN highest=1) while the
        // all-languages view gives the true published volume count (One Piece highest=114).
        // For a volume COUNT, all-languages is the most complete signal; the language gap only
        // matters for per-chapter un-volumized tracking, which we don't do.
        // Ignores the "none" bucket (un-volumized bleeding-edge chapters) for the count.
        private int HighestVolumeFor(string mangaId)
        {
            var request = new HttpRequestBuilder(ApiUrl + "/manga/{id}/aggregate")
                .SetSegment("id", mangaId)
                .WithRateLimit(0.3)
                .Build();
            request.SuppressHttpError = true;
            request.RequestTimeout = TimeSpan.FromSeconds(15);

            var response = _httpClient.Get(request);
            if (response?.Content == null)
            {
                return 0;
            }

            var volumes = JObject.Parse(response.Content)["volumes"] as JObject;
            if (volumes == null)
            {
                return 0;
            }

            var highest = 0;
            foreach (var prop in volumes.Properties())
            {
                if (int.TryParse(prop.Name, out var n) && n > highest)
                {
                    highest = n;
                }
            }

            return highest;
        }
    }
}
