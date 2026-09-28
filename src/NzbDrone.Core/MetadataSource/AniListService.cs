using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Manga;

namespace NzbDrone.Core.MetadataSource
{
    // Phase 3.5: AniList metadata provider. Supplies real series details for manga —
    // cover art, synopsis, romaji/english/native titles, ongoing/finished status, and
    // the authoritative FINAL volume count once a series is complete.
    //
    // AniList's volumes field is only populated for FINISHED series (verified: RELEASING
    // series like One Piece return null), so the caller must gate volume use on Status.
    // Mirrors MangaSearchService: convention-registered, fail-soft (returns null, never throws).
    //
    // Binding (2026-09-15, D1-D4, D7): the strict pass RANKS the page (AniListRanker) instead of
    // taking the first title-or-synonym hit, retries the catalogue's aliases, and a bound entry
    // is re-fetched by id. Every match decision logs at Info.
    public class AniListSeries
    {
        public int? Id { get; set; }            // AniList media id — what an entry binds to (D4)
        public string RomajiTitle { get; set; }
        public string EnglishTitle { get; set; }
        public string NativeTitle { get; set; }
        public List<string> Synonyms { get; set; }
        public string Description { get; set; }
        public string CoverImageUrl { get; set; }
        public string Status { get; set; }     // RELEASING | FINISHED | HIATUS | ...
        public string Format { get; set; }     // MANGA | NOVEL | ONE_SHOT
        public int? Volumes { get; set; }       // authoritative only when Status == FINISHED
        public int? AverageScore { get; set; }  // AniList 0-100 series score (real, series-level)
        public int? Popularity { get; set; }
        public int? StartYear { get; set; }     // JP serialization start — floor for backfilled per-volume dates
        public string MatchedVia { get; set; }  // primary | synonym | article | substring | ceiling | alias | relaxed | catalogue | id
    }

    // One search hit as the ranker sees it and as Fix Match lists it.
    public class AniListCandidate
    {
        public int Id { get; set; }
        public string TitleRomaji { get; set; }
        public string TitleEnglish { get; set; }
        public string TitleNative { get; set; }
        public List<string> Synonyms { get; set; } = new List<string>();
        public string Format { get; set; }
        public string Status { get; set; }
        public int? Volumes { get; set; }
        public int? Chapters { get; set; }
        public int? Popularity { get; set; }
        public int? AverageScore { get; set; }
        public int? StartYear { get; set; }
        public string CoverUrl { get; set; }
        public string Description { get; set; }

        public string DisplayTitle => TitleEnglish ?? TitleRomaji ?? TitleNative;

        public IEnumerable<string> PrimaryTitles
        {
            get
            {
                yield return TitleRomaji;
                yield return TitleEnglish;
                yield return TitleNative;
            }
        }

        public IEnumerable<string> AllTitles => PrimaryTitles.Concat(Synonyms ?? new List<string>());
    }

    public interface IAniListService
    {
        // Manga, no catalogue hints — kept so untouched callers and mocks compile.
        AniListSeries FindSeries(string title, bool relaxed = false);

        // The ranked strict pass (D1) over Candidates(title), then the alias retry (D3: one strict
        // pass per alias, in order), then — when relaxed — the search-box pass. MatchedVia says
        // which; null when nothing passed. titleIsLineName: the title IS the name of the catalogue
        // line catalogueVolumeCount came from (not an alias or arc title the line was found by) —
        // the ranker's R4 ceiling and R5 substring tiers read the count as the title's and run
        // only then.
        AniListSeries FindSeries(string title, LibraryType library, int? catalogueVolumeCount, IReadOnlyList<string> aliases, bool relaxed = false, bool titleIsLineName = false);

        // Media(id:). Null when AniList has no such entry or the call failed. MatchedVia = "id".
        AniListSeries GetById(int anilistId);

        // The raw search page in AniList's order: manga queries format_not: NOVEL, light novels
        // format: NOVEL (D2). Empty (never null) on failure.
        IReadOnlyList<AniListCandidate> Candidates(string term, LibraryType library, int perPage = 10);
    }

    public class AniListService : IAniListService
    {
        private const string ApiUrl = "https://graphql.anilist.co";
        private const int DefaultPerPage = 10;

        // Alias retries that may cost a fresh AniList request (rate cap 30/min; refreshes run in
        // ~30-series bursts). Every alias is first ranked against the page the title fetched.
        public const int MaxAliasSearches = 3;

        private const string MediaFields =
            "id format status volumes chapters averageScore popularity description(asHtml: false) synonyms " +
            "startDate { year } title { romaji english native } coverImage { extraLarge large }";

        // Page of candidates rather than the single top hit: AniList ranks fuzzily, and for
        // subtitle-heavy arc queries the first result is often a DIFFERENT entry in the franchise.
        private const string MangaSearchQuery =
            "query ($search: String, $perPage: Int) { Page(perPage: $perPage) { media(search: $search, type: MANGA, format_not: NOVEL) { " + MediaFields + " } } }";

        private const string NovelSearchQuery =
            "query ($search: String, $perPage: Int) { Page(perPage: $perPage) { media(search: $search, type: MANGA, format: NOVEL) { " + MediaFields + " } } }";

        private const string ByIdQuery =
            "query ($id: Int) { Media(id: $id, type: MANGA) { " + MediaFields + " } }";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public AniListService(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public AniListSeries FindSeries(string title, bool relaxed = false)
        {
            return FindSeries(title, LibraryType.Manga, null, Array.Empty<string>(), relaxed);
        }

        public AniListSeries FindSeries(string title, LibraryType library, int? catalogueVolumeCount, IReadOnlyList<string> aliases, bool relaxed = false, bool titleIsLineName = false)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return null;
            }

            try
            {
                var candidates = FetchPage(title, library, DefaultPerPage);

                // A failed request (Execute has warned) is not a "no match" — the blindness the
                // audit was about — so no decision is logged for it.
                if (candidates == null)
                {
                    return null;
                }

                // Strict pass (D1): ranked equality, never the first hit. The ranker's fallback
                // tiers (R7 article, R5 substring, R4 ceiling — OpenTome 2026-09-24) are strict
                // rules and run here on every call, the search box's included, before the relaxed
                // pass below; the relaxed pass itself is unchanged and only sees what they left.
                // R4 and R5 need a catalogue count AND titleIsLineName.
                var strict = AniListRanker.Pick(candidates, TitleNormalizer.ForSearch(title), catalogueVolumeCount, termIsLineName: titleIsLineName);

                if (strict.Pick != null)
                {
                    return Matched(title, strict.Pick, strict.Via, relaxed);
                }

                // Alias retry (D3): each alias is ranked against the page the title already fetched
                // (the audit's two unbound-by-shape entries sit at rank 0 there), then — for the first
                // MaxAliasSearches aliases only — against a fresh search for the alias itself. An
                // alias is not the line's own name, so the volume ceiling always holds (R3). Any
                // alias whose key equals the title's (or an earlier alias's) is dropped and never
                // ranked or searched — that includes the de-slugged id whenever it keys like the
                // name, which is the usual case (OpenTome always searches its de-slugged form).
                // The seed also holds the name's ASCII-only keys (OpenTome's alias_terms seed,
                // 2026-09-24): the de-slugged id and the catalogue's lower-case ASCII alias row drop
                // what TitleFold keeps ("Ranma ½" keys ranma12, its "ranma" row keys ranma), and a
                // stored copy of the name must not burn one of MaxAliasSearches' searches.
                var seen = new HashSet<string> { TitleMatcher.Normalize(title) };
                seen.UnionWith(new[] { AsciiKey(title), AsciiKey(TitleNormalizer.ForSearch(title)) }.Where(k => k.Length > 0));
                var searches = 0;

                foreach (var alias in (aliases ?? Array.Empty<string>()).Where(a => a.IsNotNullOrWhiteSpace()))
                {
                    if (!seen.Add(TitleMatcher.Normalize(alias)))
                    {
                        continue;
                    }

                    var query = TitleNormalizer.ForSearch(alias);
                    var byAlias = AniListRanker.Pick(candidates, query, catalogueVolumeCount, ownName: false);

                    // The fresh search is for add/refresh; the search box keeps today's latency.
                    if (byAlias.Pick == null && !relaxed && searches < MaxAliasSearches)
                    {
                        searches++;
                        byAlias = AniListRanker.Pick(Candidates(alias, library), query, catalogueVolumeCount, ownName: false);
                    }

                    if (byAlias.Pick != null)
                    {
                        _logger.Debug("AniList: '{0}' matched through alias '{1}'", title, alias);
                        return Matched(title, byAlias.Pick, "alias", relaxed);
                    }
                }

                // SEARCH-path fallback (relaxed): nothing matched exactly, so take the candidate
                // whose titles score best against the typed query — a near-miss like "apothecary
                // diaries manga" resolves to "The Apothecary Diaries" instead of returning nothing
                // (the bare "apothecary diaries" is an article-only difference, bound strictly by R7).
                // The rank blends lexical score with log-scaled popularity: pure closeness picks
                // obscure parodies whose fan-title mirrors the query ("That Time I was
                // Reincarnated Into Google Spreadsheets", popularity 24, out-scores the Slime
                // main series at 59k), while the blend still lets a decisively closer title win
                // (typing a spinoff's own subtitle beats the more popular main series).
                // ONE_SHOT stays out here too (D1).
                if (relaxed)
                {
                    var loose = candidates
                        .Where(c => c.Format != AniListRanker.OneShot)
                        .Select(c => new { Candidate = c, Score = TitleMatcher.SearchScore(title, c.AllTitles) })
                        .Where(x => x.Score >= TitleMatcher.SearchFloor)
                        .OrderByDescending(x => x.Score + (0.3m * PopularityWeight(x.Candidate.Popularity)))
                        .FirstOrDefault();

                    if (loose != null)
                    {
                        return Matched(title, loose.Candidate, "relaxed", relaxed);
                    }
                }

                // The decision is visible at Info on the add/refresh path (D7); the search box's
                // debounced queries would make it noise, so the relaxed path stays at Debug.
                var decisionLevel = relaxed ? LogLevel.Debug : LogLevel.Info;

                if (strict.Rejections.Any())
                {
                    _logger.Log(decisionLevel, "AniList rejected \"{0}\": {1} candidate(s) — {2}", title, strict.Rejections.Count, string.Join("; ", strict.Rejections));
                }
                else
                {
                    _logger.Log(decisionLevel, "AniList no match \"{0}\"", title);
                }

                return null;
            }
            catch (Exception ex)
            {
                // Discovery must never break on a metadata-source failure — degrade to stub.
                _logger.Debug(ex, "AniList lookup failed for '{0}'", title);
                return null;
            }
        }

        // The lower-case ASCII letters and digits of a title -- the key of OpenTome's
        // ascii_normalize() (of the name) and deslug() (of its for_search() form).
        private static string AsciiKey(string title)
        {
            return new string(title.ToLowerInvariant().Where(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')).ToArray());
        }

        public AniListSeries GetById(int anilistId)
        {
            var media = Execute(ByIdQuery, new AniListVariables { Id = anilistId })?.Data?.Media;

            if (media == null)
            {
                _logger.Debug("AniList id {0} did not resolve", anilistId);
                return null;
            }

            return ToSeries(ToCandidate(media), "id");
        }

        public IReadOnlyList<AniListCandidate> Candidates(string term, LibraryType library, int perPage = DefaultPerPage)
        {
            return FetchPage(term, library, perPage) ?? Array.Empty<AniListCandidate>();
        }

        // The page as AniList returned it (empty when the search has no hits), or null when the
        // request failed — FindSeries must not read a 429/5xx or a transport failure as "no match".
        private IReadOnlyList<AniListCandidate> FetchPage(string term, LibraryType library, int perPage)
        {
            if (term.IsNullOrWhiteSpace())
            {
                return Array.Empty<AniListCandidate>();
            }

            var query = library == LibraryType.LightNovel ? NovelSearchQuery : MangaSearchQuery;
            var variables = new AniListVariables { Search = TitleNormalizer.ForSearch(term), PerPage = perPage };
            var response = Execute(query, variables);
            var media = response?.Data?.Page?.Media;

            if (media == null)
            {
                // Execute has already warned when the request failed; a 200 without a page is
                // AniList's GraphQL-error shape ({"data":null,"errors":[…]}) and must not be silent.
                if (response != null)
                {
                    _logger.Debug("AniList answered '{0}' without a page", Describe(variables));
                }

                return null;
            }

            return media.Select(ToCandidate).ToList();
        }

        private AniListResponse Execute(string query, AniListVariables variables)
        {
            try
            {
                var body = new AniListRequest
                {
                    Query = query,
                    Variables = variables
                };

                // Inline builder for the single fixed endpoint. WithRateLimit(2.0) = 30/min,
                // honoring AniList's degraded cap (bucket keyed on host).
                var httpRequest = new HttpRequestBuilder(ApiUrl)
                    .SetHeader("Content-Type", "application/json")
                    .WithRateLimit(2.0)
                    .Build();

                httpRequest.SetContent(body.ToJson());
                httpRequest.SuppressHttpError = true;

                // Bound the call so a slow/hung provider can't stall a lookup for the 100s default —
                // the search box waits on this, and the source is best-effort (fail-soft to null).
                httpRequest.RequestTimeout = TimeSpan.FromSeconds(15);

                // Plain Post + own deserialisation (not Post<T>): the DTOs are private, and a
                // fixture answers Post(HttpRequest) with canned JSON per query.
                var response = _httpClient.Post(httpRequest);

                if (response == null)
                {
                    return null;
                }

                // SuppressHttpError keeps a 429/5xx from throwing, so it would otherwise read as
                // an honest "no match" — the blindness the audit was about. A 404 is only ever a
                // Media(id:) miss (a search with no hits is a 200 with an empty page).
                if (response.HasHttpError)
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        _logger.Debug("AniList returned 404 for '{0}'", Describe(variables));
                    }
                    else
                    {
                        _logger.Warn("AniList returned {0} for '{1}'; keeping local data", (int)response.StatusCode, Describe(variables));
                    }

                    return null;
                }

                if (response.Content.IsNullOrWhiteSpace())
                {
                    return null;
                }

                return Json.Deserialize<AniListResponse>(response.Content);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "AniList request failed for '{0}'; keeping local data", Describe(variables));
                return null;
            }
        }

        private static string Describe(AniListVariables variables)
        {
            return variables?.Search ?? (variables?.Id.HasValue == true ? "id " + variables.Id : "?");
        }

        private AniListSeries Matched(string title, AniListCandidate candidate, string via, bool relaxed)
        {
            _logger.Log(relaxed ? LogLevel.Debug : LogLevel.Info,
                "AniList match \"{0}\" -> {1} \"{2}\" ({3}, {4} vols, pop {5}) via {6}",
                title,
                candidate.Id,
                candidate.DisplayTitle,
                candidate.Format,
                candidate.Volumes?.ToString() ?? "?",
                candidate.Popularity?.ToString() ?? "?",
                via);

            return ToSeries(candidate, via);
        }

        private static AniListSeries ToSeries(AniListCandidate c, string via)
        {
            return new AniListSeries
            {
                Id = c.Id,
                RomajiTitle = c.TitleRomaji,
                EnglishTitle = c.TitleEnglish,
                NativeTitle = c.TitleNative,
                Synonyms = c.Synonyms,
                Description = c.Description,
                CoverImageUrl = c.CoverUrl,
                Status = c.Status,
                Format = c.Format,
                Volumes = c.Volumes,
                AverageScore = c.AverageScore,
                Popularity = c.Popularity,
                StartYear = c.StartYear,
                MatchedVia = via
            };
        }

        private static AniListCandidate ToCandidate(AniListMediaResource m)
        {
            return new AniListCandidate
            {
                Id = m.Id,
                TitleRomaji = m.Title?.Romaji,
                TitleEnglish = m.Title?.English,
                TitleNative = m.Title?.Native,
                Synonyms = m.Synonyms ?? new List<string>(),
                Format = m.Format,
                Status = m.Status,
                Volumes = m.Volumes,
                Chapters = m.Chapters,
                Popularity = m.Popularity,
                AverageScore = m.AverageScore,
                StartYear = m.StartDate?.Year,
                CoverUrl = m.CoverImage?.ExtraLarge ?? m.CoverImage?.Large,
                Description = StripHtml(m.Description)
            };
        }

        // Log-scaled 0..1: AniList popularity saturates at 100k+ members.
        private static decimal PopularityWeight(int? popularity)
        {
            var p = popularity ?? 0;

            return p <= 0 ? 0m : (decimal)Math.Min(Math.Log10(p + 1) / 5.0, 1.0);
        }

        // AniList descriptions carry <br> and light HTML even with asHtml:false.
        private static string StripHtml(string input)
        {
            if (input.IsNullOrWhiteSpace())
            {
                return input;
            }

            var text = input
                .Replace("<br>", "\n")
                .Replace("<br/>", "\n")
                .Replace("<br />", "\n");

            return System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", string.Empty).Trim();
        }

        // ---- GraphQL request/response DTOs (Newtonsoft) ----
        private class AniListRequest
        {
            [JsonProperty("query")]
            public string Query { get; set; }

            [JsonProperty("variables")]
            public AniListVariables Variables { get; set; }
        }

        private class AniListVariables
        {
            [JsonProperty("search")]
            public string Search { get; set; }

            [JsonProperty("perPage")]
            public int? PerPage { get; set; }

            [JsonProperty("id")]
            public int? Id { get; set; }
        }

        private class AniListResponse
        {
            [JsonProperty("data")]
            public AniListData Data { get; set; }
        }

        private class AniListData
        {
            [JsonProperty("Page")]
            public AniListPage Page { get; set; }

            [JsonProperty("Media")]
            public AniListMediaResource Media { get; set; }
        }

        private class AniListPage
        {
            [JsonProperty("media")]
            public List<AniListMediaResource> Media { get; set; }
        }

        private class AniListMediaResource
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("format")]
            public string Format { get; set; }

            [JsonProperty("status")]
            public string Status { get; set; }

            [JsonProperty("synonyms")]
            public List<string> Synonyms { get; set; }

            [JsonProperty("volumes")]
            public int? Volumes { get; set; }

            [JsonProperty("chapters")]
            public int? Chapters { get; set; }

            [JsonProperty("averageScore")]
            public int? AverageScore { get; set; }

            [JsonProperty("popularity")]
            public int? Popularity { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("title")]
            public AniListTitle Title { get; set; }

            [JsonProperty("coverImage")]
            public AniListCover CoverImage { get; set; }

            [JsonProperty("startDate")]
            public AniListStartDate StartDate { get; set; }
        }

        private class AniListStartDate
        {
            [JsonProperty("year")]
            public int? Year { get; set; }
        }

        private class AniListTitle
        {
            [JsonProperty("romaji")]
            public string Romaji { get; set; }

            [JsonProperty("english")]
            public string English { get; set; }

            [JsonProperty("native")]
            public string Native { get; set; }
        }

        private class AniListCover
        {
            [JsonProperty("extraLarge")]
            public string ExtraLarge { get; set; }

            [JsonProperty("large")]
            public string Large { get; set; }
        }
    }
}
