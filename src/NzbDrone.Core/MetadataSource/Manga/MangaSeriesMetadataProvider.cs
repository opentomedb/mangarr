using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MetadataSource.Manga
{
    // Resolves a manga series' volume structure + presentation, composing the bundled GCD spine
    // (English/omnibus-aware per-volume data) with the live sources as fallback. This is the logic
    // that used to be inlined in BookInfoProxy.BuildFakeAuthor; extracting it keeps BookInfoProxy a
    // thin coordinator and gives GCD and the live chain a single composition seam.
    //
    // When no GCD artifact is present (the default until one is generated), the GCD branch is skipped
    // entirely and behaviour is identical to the previous AniList/MangaUpdates/MangaDex/Google Books
    // chain. Convention-registered (DryIoc RegisterMany).
    public interface IMangaSeriesMetadataProvider
    {
        // resolveVolumeDetails=false is the lightweight LOOKUP path: resolve the series + volume
        // STRUCTURE (count/status/presentation) but skip every per-volume external call (Google Books
        // loop) and the indexer volume probe. Those run only on add/refresh, where it's worth the wait.
        // anilistId: the entry's stored binding (D4) — fetched as-is, no search. idName: the
        // entry's own de-slugged id, tried as the first alias when the name strict-matches
        // nothing (D3); on refresh it differs from the curated name.
        MangaSeriesMetadata GetSeries(string name, int maxVolumeCap, bool resolveVolumeDetails = true, LibraryType library = LibraryType.Manga, int? anilistId = null, string idName = null);

        // Preferred Edition (2026-09-24): the edition path. BookInfoProxy calls it only for a
        // non-English request; null behaves exactly like the six-argument call.
        MangaSeriesMetadata GetSeries(string name, int maxVolumeCap, bool resolveVolumeDetails, LibraryType library, int? anilistId, string idName, EditionRequest edition);

        // Per-volume metadata for a single volume OUTSIDE the series' metadata count — the
        // disk-discovered extras (fractional side stories, volumes beyond a stale count). Same
        // Google Books path the GetSeries loop uses, so a file-backed volume no source predicted
        // still gets a real date/page count instead of a permanent TBA.
        MangaVolumeMetadata ResolveVolume(string displayName, double volumeNumber, bool resolveVolumeDetails = true);

        // Preferred Edition (2026-09-24): ResolveVolume for a non-English edition's disk extras.
        MangaVolumeMetadata ResolveVolume(string displayName, double volumeNumber, bool resolveVolumeDetails, string language);

        // The AniList binding alone (D1-D4: catalogue id -> ranked strict search -> alias retry;
        // never relaxed, never cached, none of the per-volume work): what a refresh WOULD bind
        // the name to. The rebind pass computes every series' pick with this and writes only
        // the ones that moved. Null when nothing passes.
        AniListSeries ResolveAniList(string name, LibraryType library, string idName = null);

        // Drop the search-box cache entry for a name (both libraries) after its binding changed.
        void ForgetLookup(string name);

        // Preferred Edition (2026-09-24): the languages the line's work has a line in -- the Add form's
        // Edition picker and Edit Series. Empty without a tome id (older artifact / never bound).
        List<EditionOption> EditionOptions(string tomeLineId, LibraryType library);
    }

    public class MangaSeriesMetadataProvider : IMangaSeriesMetadataProvider
    {
        // Preferred Edition (2026-09-24, M13 pre-review fix, ruling (c)): the stored name an ENGLISH resolve
        // keys its pins by, for the one entry state that needs it -- a series localized and then changed back
        // to English without Rename (Name "L'Attaque des Titans", AnchorName "Attack on Titan"). Its pins
        // live under the stored name (MetadataPinController writes there), but the English call's display
        // name is the anchor. Carried as a scope, not an argument: the six-argument GetSeries is mocked
        // with six arguments across the suite (a new optional parameter would not compile in those
        // expression trees), and the seven-argument EditionRequest is the gate that keeps the resolver off
        // English series. BookInfoProxy opens the scope only when AnchorName is set; unset (every
        // never-localized series) nothing is set and the key is today's.
        private static readonly AsyncLocal<string> EnglishPinName = new AsyncLocal<string>();

        internal static string ScopedEnglishPinName => EnglishPinName.Value;

        public static IDisposable PinsUnder(string storedName)
        {
            return new EnglishPinScope(storedName);
        }

        private sealed class EnglishPinScope : IDisposable
        {
            private readonly string _previous;

            public EnglishPinScope(string storedName)
            {
                _previous = EnglishPinName.Value;
                EnglishPinName.Value = storedName;
            }

            public void Dispose()
            {
                EnglishPinName.Value = _previous;
            }
        }

        private readonly IMetadataOverridesService _metadataOverrides;
        private readonly IGcdMetadataService _gcdMetadataService;
        private readonly IAniListService _aniListService;
        private readonly IMangaDexService _mangaDexService;
        private readonly IMangaUpdatesService _mangaUpdatesService;
        private readonly IGoogleBooksService _googleBooksService;
        private readonly IMangaSearchService _mangaSearchService;
        private readonly IAudibleCatalogService _audibleCatalogService;
        private readonly IEditionResolver _editionResolver;
        private readonly ICached<ProviderLookup> _lookupCache;
        private readonly Logger _logger;

        public MangaSeriesMetadataProvider(IGcdMetadataService gcdMetadataService,
                                           IAniListService aniListService,
                                           IMangaDexService mangaDexService,
                                           IMangaUpdatesService mangaUpdatesService,
                                           IGoogleBooksService googleBooksService,
                                           IMangaSearchService mangaSearchService,
                                           IAudibleCatalogService audibleCatalogService,
                                           IEditionResolver editionResolver,
                                           IMetadataOverridesService metadataOverrides,
                                           ICacheManager cacheManager,
                                           Logger logger)
        {
            _metadataOverrides = metadataOverrides;
            _gcdMetadataService = gcdMetadataService;
            _aniListService = aniListService;
            _mangaDexService = mangaDexService;
            _mangaUpdatesService = mangaUpdatesService;
            _googleBooksService = googleBooksService;
            _mangaSearchService = mangaSearchService;
            _audibleCatalogService = audibleCatalogService;
            _editionResolver = editionResolver;
            _lookupCache = cacheManager.GetCache<ProviderLookup>(GetType());
            _logger = logger;
        }

        // Status-aware volume count. Precedence (no max()-of-sources, no fabrication):
        //  1. an explicit user MaxVolume cap always wins;
        //  2. both volume authorities (AniList-FINISHED + MangaUpdates) know a finished count ->
        //     prefer the LOWER on disagreement (English releases are often fewer-volume omnibuses);
        //  3. AniList's FINISHED count alone;
        //  4. MangaUpdates' count (ongoing series, where AniList withholds, and AniList misses);
        //  5. highest live published volume (MangaDex aggregate / indexer search);
        //  6. nothing known -> 0 (never invent volumes; ShouldDelete keeps any file-backed volume).
        // getIndexerHighest is a thunk so the expensive indexer search only runs when needed.
        public static int DetermineVolumeCount(int maxVolumeCap, string aniStatus, int? aniVolumes, int mangaUpdatesVolumes, int mangadexHighest, Func<int> getIndexerHighest)
        {
            if (maxVolumeCap > 0)
            {
                return maxVolumeCap;
            }

            var aniFinal = (aniStatus == "FINISHED" && aniVolumes.HasValue && aniVolumes.Value > 0) ? aniVolumes.Value : 0;

            if (aniFinal > 0 && mangaUpdatesVolumes > 0)
            {
                // Prefer the lower count — an English omnibus genuinely collapses volumes (a 3-in-1
                // turns 42 -> 14). But only when the gap is omnibus-plausible: a 4x+ gap (AniList=1
                // vs MangaUpdates=11) is almost always bad AniList data, not an omnibus, and trusting
                // the low value silently drops the missing volumes from discovery. Past that ratio,
                // trust MangaUpdates (the manga-specialised volume authority).
                var lower = Math.Min(aniFinal, mangaUpdatesVolumes);
                var higher = Math.Max(aniFinal, mangaUpdatesVolumes);
                return lower * 4 >= higher ? lower : higher;
            }

            if (aniFinal > 0)
            {
                return aniFinal;
            }

            if (mangaUpdatesVolumes > 0)
            {
                return mangaUpdatesVolumes;
            }

            return Math.Max(mangadexHighest, getIndexerHighest());
        }

        // Mirror of the ingest-side outlier pass, applied to the fully assembled volume list so
        // a wrong-edition date arriving from the LIVE backfill (Google Books matching a 2025
        // deluxe reprint for a 2015 volume) is rejected the same way artifact junk is: a date
        // disagreeing by >365 days with BOTH flanking dated volumes, when those two agree with
        // each other (90-day slack), is edition-variance junk — nulled (TBA, re-resolvable)
        // rather than persisted. Comparisons use the pre-pass dates (single pass, no cascade);
        // edge volumes are never touched.
        // Title-search page counts carry no edition guarantee: collected editions run
        // 400-720pp and placeholder rows carry 1pp. Outside the plausible tankobon
        // window the value is wrong-edition junk — keep 0 (unknown, re-resolvable).
        private static int SanitizeBackfillPages(int pages)
        {
            return pages >= 80 && pages <= 400 ? pages : 0;
        }

        // Batch re-release tell: 3+ volumes sharing one identical date is a bulk digital
        // drop (Berserk: 23 volumes stamped with one 2017 date over 2006-2007 originals) —
        // but ONLY when it conflicts with chronology, so legitimate multi-volume day-one
        // launches survive. Nulled dates are honest TBA, re-resolvable.
        public static void NullBatchStampDates(List<MangaVolumeMetadata> volumes)
        {
            var dated = volumes.Where(v => v.ReleaseDate.HasValue).OrderBy(v => v.VolumeNumber).ToList();

            // Snapshot pre-pass dates (same discipline as NullDateOutliers): nulling one
            // batch group must not break the boundary comparisons of a second group.
            var original = dated.Select(v => v.ReleaseDate.Value).ToList();
            var slack = TimeSpan.FromDays(90);

            foreach (var group in dated.GroupBy(v => v.ReleaseDate.Value.Date).Where(g => g.Count() >= 3).ToList())
            {
                var run = new HashSet<MangaVolumeMetadata>(group);
                var conflicts = false;

                for (var k = 0; k < dated.Count - 1; k++)
                {
                    if (run.Contains(dated[k]) == run.Contains(dated[k + 1]))
                    {
                        continue;
                    }

                    if (original[k] > original[k + 1] + slack)
                    {
                        conflicts = true;
                        break;
                    }
                }

                if (conflicts)
                {
                    foreach (var v in group)
                    {
                        v.ReleaseDate = null;
                    }
                }
            }
        }

        public static void NullDateOutliers(List<MangaVolumeMetadata> volumes)
        {
            var dated = volumes.Where(v => v.ReleaseDate.HasValue).OrderBy(v => v.VolumeNumber).ToList();
            var original = dated.Select(v => v.ReleaseDate.Value).ToList();
            var tolerance = TimeSpan.FromDays(365);
            var slack = TimeSpan.FromDays(90);

            for (var k = 1; k < dated.Count - 1; k++)
            {
                var prev = original[k - 1];
                var next = original[k + 1];

                if (next >= prev - slack && (original[k] < prev - tolerance || original[k] > next + tolerance))
                {
                    dated[k].ReleaseDate = null;
                }
            }
        }

        // Collected-edition tell for a GCD line the name-based is_omnibus flag missed ("Book
        // Edition"-style 2-in-1 lines like Vinland Saga carry no omnibus keyword): explicit
        // per-volume composition data, or a collected-edition page count — deluxe 2-in-1 books
        // run ~400-480pp where singles run ~190-230pp, so a median ≥320pp (3+ known counts)
        // separates them cleanly.
        public static bool IsCollectedEdition(bool isOmnibusFlag, ICollection<GcdVolume> volumes)
        {
            if (isOmnibusFlag)
            {
                return true;
            }

            if (volumes.Any(v => v.Composition != null && v.Composition.Count > 1))
            {
                return true;
            }

            var pageCounts = volumes
                .Where(v => (v.PageCount ?? 0) > 0)
                .Select(v => v.PageCount.Value)
                .OrderBy(p => p)
                .ToList();

            return pageCounts.Count >= 3 && pageCounts[pageCounts.Count / 2] >= 320;
        }

        // Latin-usable alternate titles for indexer search recall and for the light-novel copy-in:
        // releases are often named by the romaji title ("Kaijuu 8-gou") or a licensed variant rather
        // than the display title, and the copy-in matches Calibre / Audiobookshelf series names
        // against these — AniList's English title is the name Calibre most often carries, so it goes
        // first. Non-latin synonyms are useless as indexer query terms and are dropped; duplicates of
        // the display name (mere punctuation/case variants) add nothing; capped so a synonym-heavy
        // AniList entry can't balloon every search.
        public static List<string> BuildAltTitles(string displayName, AniListSeries ani)
        {
            var result = new List<string>();

            if (ani == null)
            {
                return result;
            }

            var seen = new HashSet<string> { TitleMatcher.Normalize(displayName) };

            // The parser's own key (CleanAuthorName) is not TitleFold's: "Ranma 1/2" and "Ranma ½"
            // share TitleMatcher's key (ranma12) but not the parser's (ranma12 / ranma), so a
            // candidate is only a duplicate when BOTH keys have been seen (review F3, 2026-09-24).
            var cleanSeen = new HashSet<string> { displayName.CleanAuthorName() };

            var candidates = new List<string>();
            if (ani.EnglishTitle.IsNotNullOrWhiteSpace())
            {
                candidates.Add(ani.EnglishTitle);
            }

            if (ani.RomajiTitle.IsNotNullOrWhiteSpace())
            {
                candidates.Add(ani.RomajiTitle);
            }

            if (ani.Synonyms != null)
            {
                candidates.AddRange(ani.Synonyms);
            }

            foreach (var candidate in candidates)
            {
                if (!TryAddAltTitle(candidate, seen, cleanSeen, false, out var title))
                {
                    continue;
                }

                result.Add(title);

                if (result.Count >= 4)
                {
                    break;
                }
            }

            return result;
        }

        // Preferred Edition (2026-09-24, ruling S2): the one candidate test both alt-title builders share.
        // Trimmed, 4-80 characters, mostly Latin unless allowNative (a Japanese edition's native title),
        // and new by at least one of the two keys (review F3: TitleMatcher's and the parser's). Both
        // keys are always recorded -- the second add must run even when the first is new.
        private static bool TryAddAltTitle(string candidate, HashSet<string> seen, HashSet<string> cleanSeen, bool allowNative, out string title)
        {
            title = null;

            if (candidate.IsNullOrWhiteSpace())
            {
                return false;
            }

            var trimmed = candidate.Trim();

            if (!IsAltTitleShape(trimmed, allowNative))
            {
                return false;
            }

            var newKey = seen.Add(TitleMatcher.Normalize(trimmed));
            var newCleanKey = cleanSeen.Add(trimmed.CleanAuthorName());

            if (!newKey && !newCleanKey)
            {
                return false;
            }

            title = trimmed;
            return true;
        }

        private static bool IsAltTitleShape(string trimmed, bool allowNative)
        {
            return trimmed.Length >= 4 && trimmed.Length <= 80 && (allowNative || IsMostlyLatin(trimmed));
        }

        // Preferred Edition (2026-09-24, spec §2.2 Aliases): a non-English series' alternate titles. The
        // edition line's rows in its own language first (official, then romanized, then the rest), then
        // AniList's English / romaji (/ native for a Japanese edition) / synonyms, then the English
        // anchor; cap 6. Native script only for a Japanese edition (the parser matches it; queries still
        // skip non-ASCII -- BookSearchCriteria.IsSearchableAlias). A raw list-article row ("Liste des
        // chapitres de ...") is an OpenTome matching aid, never a title.
        // Controller ruling (2026-09-24): OpenTome tags a same-spelled title with the alphabetically first
        // of its languages, so a row's language orders it and never excludes it -- the rows of other (or
        // no) language follow the anchor, in the same kind order, inside the cap.
        // M6b fix round 1 (2026-09-24, I1): the anchor is the name an English-titled "[FR]" release
        // carries, and the parser accepts only Name + Aliases -- so while the anchor would still be a new
        // title (not both of its keys seen), everything before it stops one short of the cap.
        private static readonly Regex ListArticle = new Regex(@"^(?:list(?:e)?|chronologie)\s+(?:(?:of|des?|du)\b|d['’])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<string> BuildEditionAltTitles(string displayName, string identityName, IEnumerable<GcdAlias> editionAliases, string editionLanguage, AniListSeries ani)
        {
            var result = new List<string>();
            var seen = new HashSet<string> { TitleMatcher.Normalize(displayName) };
            var cleanSeen = new HashSet<string> { displayName.CleanAuthorName() };
            var native = editionLanguage == "ja";
            var anchor = identityName?.Trim();
            var anchorIsTitle = anchor.IsNotNullOrWhiteSpace() && IsAltTitleShape(anchor, native) && !ListArticle.IsMatch(anchor);
            var anchorsTurn = false;

            bool AnchorPending() => anchorIsTitle && !anchorsTurn &&
                                    !(seen.Contains(TitleMatcher.Normalize(anchor)) && cleanSeen.Contains(anchor.CleanAuthorName()));

            void Add(string candidate)
            {
                var cap = AnchorPending() ? 5 : 6;

                if (result.Count >= cap || (candidate.IsNotNullOrWhiteSpace() && ListArticle.IsMatch(candidate.Trim())))
                {
                    return;
                }

                if (TryAddAltTitle(candidate, seen, cleanSeen, native, out var title))
                {
                    result.Add(title);
                }
            }

            void AddRows(IEnumerable<GcdAlias> rows)
            {
                var list = rows.ToList();

                foreach (var row in list.Where(a => a.Kind == "official"))
                {
                    Add(row.Alias);
                }

                foreach (var row in list.Where(a => a.Kind == "romanized"))
                {
                    Add(row.Alias);
                }

                foreach (var row in list.Where(a => a.Kind != "official" && a.Kind != "romanized"))
                {
                    Add(row.Alias);
                }
            }

            var all = (editionAliases ?? Enumerable.Empty<GcdAlias>()).Where(a => a != null).ToList();
            bool Own(GcdAlias a) => string.Equals(a.Language?.Trim(), editionLanguage, StringComparison.OrdinalIgnoreCase);

            AddRows(all.Where(Own));

            if (ani != null)
            {
                Add(ani.EnglishTitle);
                Add(ani.RomajiTitle);

                if (native)
                {
                    Add(ani.NativeTitle);
                }

                foreach (var synonym in ani.Synonyms ?? new List<string>())
                {
                    Add(synonym);
                }
            }

            anchorsTurn = true;   // its own turn: the full cap
            Add(identityName);

            AddRows(all.Where(a => !Own(a)));

            return result;
        }

        // D5 (2026-09-17): Google's ISBN records are trusted for cover, blurb, pages and date, and
        // some are mis-catalogued (Fairy Tail 24's ISBN answered an unrelated adult title). A record
        // is used only when its normalised title starts with the series name or one of its aliases.
        // Prefix, not contains: the volume number is the ISBN's job. The display name is always a
        // key ("K-ON!" normalises to three characters); an alias must normalise to at least four,
        // so a junk catalogue alias cannot let a record through on a few letters. The trailing
        // edition tag Google appends ("(light novel)") is stripped only to keep the compared title
        // readable — a suffix cannot change a prefix test. (Subtitles.cs keeps its own copy of this
        // regex for the D3 rule; the two are the same pattern.)
        private static readonly Regex TrailingEditionTag = new Regex(@"\s*\((?:light novel|manga|novel)\)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool RecordNamesSeries(VolumeDetails record, string displayName, IEnumerable<string> aliases)
        {
            var title = TitleMatcher.Normalize(TrailingEditionTag.Replace(record?.Title ?? string.Empty, string.Empty));

            if (title.Length == 0)
            {
                return false;
            }

            var keys = new[] { TitleMatcher.Normalize(displayName) }
                .Where(k => k.Length > 0)
                .Concat((aliases ?? Enumerable.Empty<string>()).Select(TitleMatcher.Normalize).Where(k => k.Length >= 4));

            return keys.Any(k => title.StartsWith(k, StringComparison.Ordinal));
        }

        private static bool IsMostlyLatin(string title)
        {
            var latin = title.Count(c => c < 0x250);
            return latin >= title.Length * 0.9;
        }

        // OpenTome disambiguates a novel line from the manga of the same name in the line's NAME
        // ("Overlord (novel series)"). The ~ln library already tells the two apart, so the
        // qualifier would only leak into the series name, the pin key and the folder on disk.
        // Trailing only, case-insensitive: a parenthetical that is part of the title
        // ("Re:ZERO (Starting Life in Another World)") is not a qualifier and stays.
        private static readonly string[] LightNovelQualifiers =
        {
            " (light novel series)", " (novel series)", " (light novel)", " (novels)", " (novel)"
        };

        public static string StripLightNovelQualifier(string name)
        {
            if (name.IsNullOrWhiteSpace())
            {
                return name;
            }

            var trimmed = name.TrimEnd();

            foreach (var qualifier in LightNovelQualifiers)
            {
                if (trimmed.EndsWith(qualifier, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(0, trimmed.Length - qualifier.Length).TrimEnd();
                }
            }

            return name;
        }

        // Preferred Edition (2026-09-24, D8): an edition line is 1:1 with the English line when it is the
        // English line's counterpart -- another line of the same original, or (fix round 1, I2) the
        // original itself: OpenTome exports no orig_series_id for an origin-market line, so a Japanese
        // edition is matched by its own id (EditionResolver.IsCounterpart) -- and neither is a collected
        // edition. Only then are the English audiobook's volumes this edition's volumes.
        private bool IsOneToOne(GcdSeries editionLine, GcdSeries englishLine)
        {
            return englishLine != null &&
                   EditionResolver.IsCounterpart(englishLine, editionLine) &&
                   !_editionResolver.IsCollected(editionLine) &&
                   !_editionResolver.IsCollected(englishLine);
        }

        // Preferred Edition (2026-09-24, D3/D7): a new non-English series' name. Japanese -> AniList's
        // romaji title (OpenTome's ja local name is native script and has no romanized rows yet); other
        // languages -> the line's local name (OpenTome v0), light-novel qualifier stripped; none -> the
        // anchor's name.
        internal static string EditionDisplayName(GcdSeries editionLine, string editionLanguage, AniListSeries ani, string identityName, LibraryType library)
        {
            if (editionLanguage == "ja")
            {
                return ani?.RomajiTitle.IsNotNullOrWhiteSpace() == true ? ani.RomajiTitle : identityName;
            }

            if (editionLine.LocalName.IsNullOrWhiteSpace())
            {
                return identityName;
            }

            return library == LibraryType.LightNovel ? StripLightNovelQualifier(editionLine.LocalName) : editionLine.LocalName;
        }

        // Completion status drives the scheduled-refresh cadence (Continuing re-checks ~every 2 days
        // for new volumes; Ended ~every 30). AniList's status wins when present (don't let
        // MangaUpdates' "completed" override AniList "still releasing"); fall back to MangaUpdates
        // only when AniList didn't resolve. Biased to Continuing: a wrong "Ended" silently stops
        // refreshes ~30 days, while a wrong "Continuing" only costs a 2-day refresh.
        public static AuthorStatusType DetermineStatus(string aniStatus, bool mangaUpdatesCompleted)
        {
            if (aniStatus.IsNotNullOrWhiteSpace())
            {
                return aniStatus == "FINISHED" ? AuthorStatusType.Ended : AuthorStatusType.Continuing;
            }

            return mangaUpdatesCompleted ? AuthorStatusType.Ended : AuthorStatusType.Continuing;
        }

        public MangaSeriesMetadata GetSeries(string name, int maxVolumeCap, bool resolveVolumeDetails = true, LibraryType library = LibraryType.Manga, int? anilistId = null, string idName = null)
        {
            return GetSeries(name, maxVolumeCap, resolveVolumeDetails, library, anilistId, idName, null);
        }

        public MangaSeriesMetadata GetSeries(string name, int maxVolumeCap, bool resolveVolumeDetails, LibraryType library, int? anilistId, string idName, EditionRequest edition)
        {
            // Phase 3.5: real series metadata from AniList (covers, synopsis, titles, status, and the
            // authoritative final volume count when finished). MangaDex: live count for ONGOING series
            // (AniList withholds until finished) + a cover fallback. MangaUpdates: manga-specialized
            // volume authority for ongoing series and coverage when AniList misses. All fail soft.
            // On the lightweight LOOKUP path (search box) the three providers run concurrently and the
            // combined result is cached briefly, so a repeated or rapidly-typed query doesn't re-pay
            // three sequential network round-trips (which made the search box appear frozen). Add/refresh
            // (resolveVolumeDetails) always fetches fresh so stored metadata is never stale.
            // Relaxed provider matching on the search-box path only: a typed query is allowed to
            // resolve to its best fuzzy candidate ("apothecary diaries" -> "The Apothecary
            // Diaries"). Add/refresh stays strict — stored names must match exactly or the
            // wrong franchise entry's data would silently attach.
            // The catalogue line named what was asked for is looked up FIRST, as a hint for the
            // AniList pick (D1-D4): its volume_count bounds the ranking, its aliases feed the
            // retry, its anilist_id (when OpenTome carries one) skips the search. It is the same
            // lookup the structure binding below falls back to, so nothing is queried twice and
            // manga still binds its structure to the AniList-title line first (unchanged order).
            var hint = CatalogueHint(name, library);

            var lookup = resolveVolumeDetails
                ? FetchProviders(name, relaxed: false, library, anilistId, idName, hint)
                : _lookupCache.Get(LookupKey(name, library), () => FetchProviders(name, relaxed: true, library, anilistId, idName, hint), TimeSpan.FromMinutes(10));

            var ani = lookup.AniList;
            var mdx = lookup.MangaDex;
            var mu = lookup.MangaUpdates;

            var displayName = ani?.EnglishTitle ?? ani?.RomajiTitle ?? name;

            // AniList averageScore (0-100 -> 0-5). Series-level, applies to every volume.
            var ratingValue = ani?.AverageScore.HasValue == true
                ? Math.Round(ani.AverageScore.Value / 20m, 2)
                : 0m;

            // AniList's art is Japanese volume-1 art: since 2026-09-16 it is the poster's LAST resort
            // (D4) — the pick is made after the volume loop, which supplies the English candidates.
            var aniCoverUrl = ani?.CoverImageUrl.IsNotNullOrWhiteSpace() == true ? ani.CoverImageUrl : null;
            var overview = ani?.Description.IsNotNullOrWhiteSpace() == true ? ani.Description : string.Empty;

            var muVolumes = mu?.VolumeCount ?? 0;

            // GCD spine: when a bundled artifact is present and matches, it governs the volume
            // STRUCTURE (count, status, per-volume dates/ISBNs) — English/omnibus-aware. AniList still
            // supplies presentation (name/cover/overview/rating). Null when no artifact (today's
            // default) -> the live fallback below runs unchanged.
            // Match GCD on the AniList English title first — it aligns with GCD's English series
            // names far better than a folder-derived search term — then fall back to the raw name.
            // Third try: the arc title of a franchise-prefixed folder name (Re:ZERO's per-arc manga).
            // Light novels look up the RAW name first: AniList is queried manga-only (format_not:
            // NOVEL), so for a novel its title can only name the manga of the work — "Sword Art
            // Online" came back as "Sword Art Online Progressive" and bound the 9-volume Progressive
            // novel line instead of the 28-volume main line the raw name ranks to. A raw-name hit
            // also names the entry after the catalogue line (below); manga keeps today's order.
            GcdSeries gcd = null;
            var gcdByRawName = false;

            if (_gcdMetadataService.Available)
            {
                if (library == LibraryType.LightNovel)
                {
                    gcd = hint;
                    gcdByRawName = gcd != null;
                    gcd ??= _gcdMetadataService.FindSeriesByTitle(displayName, library)
                            ?? _gcdMetadataService.FindSeriesBySubtitle(name, library);
                }
                else
                {
                    gcd = _gcdMetadataService.FindSeriesByTitle(displayName, library) ?? hint;
                }
            }

            // Preferred Edition (2026-09-24, spec §2.1): the edition line for a non-English request.
            // editionLine == null keeps every line below on today's English path. A request that names
            // a language (bound or chosen) and finds no line of it stops here: a series is never handed
            // English volumes in place of its edition's (EditionUnavailableException leaves it alone).
            GcdSeries editionLine = null;
            string editionLanguage = null;

            if (edition != null)
            {
                var resolution = _gcdMetadataService.Available ? _editionResolver.Resolve(gcd, edition, library, name) : null;

                if (resolution?.Line != null && !EditionLanguages.IsEnglish(resolution.Language))
                {
                    editionLine = resolution.Line;
                    editionLanguage = resolution.Language;
                }
                else if (!EditionLanguages.IsEnglish(edition.Language))
                {
                    throw new EditionUnavailableException(name, edition.Language, edition.TomeLineId);
                }
            }

            // Light novels are catalogue-only (D11): no English novel line (or no artifact at all)
            // -> no live-source fallback, no empty shell. The caller refuses and points at OpenTome.
            // Preferred Edition: an edition line found for a novel with no English line is not refused.
            if (library == LibraryType.LightNovel && gcd == null && editionLine == null)
            {
                _logger.Debug("No light-novel catalogue line for '{0}'", name);

                return new MangaSeriesMetadata
                {
                    DisplayName = displayName,
                    NotInCatalogue = true,
                    Status = AuthorStatusType.Continuing
                };
            }

            // When AniList didn't supply an English title, adopt GCD's curated English series name so a
            // mangled folder-derived name (the Re:Zero arcs, "Jujustu Kaisen") becomes the correct title
            // for both display and the per-volume Google Books title search. A light novel found by
            // its raw name is named after that catalogue line too — AniList's manga title is not it.
            // A light novel's catalogue name always sheds the "(novel series)"-style disambiguation.
            if (gcd != null && gcd.Name.IsNotNullOrWhiteSpace() && (gcdByRawName || ani?.EnglishTitle.IsNotNullOrWhiteSpace() != true))
            {
                displayName = library == LibraryType.LightNovel ? StripLightNovelQualifier(gcd.Name) : gcd.Name;
            }

            // Preferred Edition (D3/D7): the anchor's name stays the series' identity (id slug, source
            // search terms); a non-English edition's display name is the line's own title -- AniList's
            // romaji for Japanese, whose local name is native script (kept as an alias, M6b).
            var identityName = displayName;

            if (editionLine != null)
            {
                displayName = EditionDisplayName(editionLine, editionLanguage, ani, identityName, library);
            }

            // The line the volume STRUCTURE comes from: the edition's, else the anchor (today).
            var line = editionLine ?? gcd;

            int volumeCount;
            int japaneseTotal;
            AuthorStatusType status;
            Dictionary<int, GcdVolume> gcdVolumes = null;
            var lineCollected = false;

            // Collection membership: the parent line's name, so the series can say what it is part of.
            // Stripped like the display name, so the child's parent id matches what the parent mints.
            var parentName = (gcd ?? line)?.ParentSeriesId != null
                ? _gcdMetadataService.FindSeriesById((gcd ?? line).ParentSeriesId.Value)?.Name
                : null;

            if (library == LibraryType.LightNovel)
            {
                parentName = StripLightNovelQualifier(parentName);
            }

            if (line != null)
            {
                volumeCount = maxVolumeCap > 0 ? maxVolumeCap : line.VolumeCount;
                status = MapGcdStatus(line.Status) ?? DetermineStatus(ani?.Status, mu?.Completed ?? false);

                // Dedup-safe: a duplicate volume_number in the artifact (data-quality blip) must not
                // throw and break the refresh — keep the first, stay fail-soft like the rest of GCD.
                // Preferred Edition (2026-09-24, D6, ruling S5): an edition line reads its coarse dates too;
                // the English path keeps the one-argument read and its SQL, unchanged.
                gcdVolumes = (editionLine != null
                        ? _gcdMetadataService.GetVolumes(line.GcdSeriesId, includeEditionDates: true)
                        : _gcdMetadataService.GetVolumes(line.GcdSeriesId))
                    .GroupBy(v => v.VolumeNumber)
                    .ToDictionary(g => g.Key, g => g.First());
                _logger.Debug("GCD spine for '{0}' (id {1}): {2} volumes", name, line.GcdSeriesId, volumeCount);
                // The page-count tell is tuned for manga (deluxe ~400pp vs singles ~200pp); light-novel
                // volumes run past 320pp on their own, so a light novel is collected only by its omnibus
                // flag or composition data.
                lineCollected = library == LibraryType.LightNovel
                    ? line.IsOmnibus || gcdVolumes.Values.Any(v => v.Composition != null && v.Composition.Count > 1)
                    : IsCollectedEdition(line.IsOmnibus, gcdVolumes.Values);

                // English release is the grabbable count; the full Japanese tankoubon total (when
                // higher) is shown alongside as "X of Y" and drives the Coming Soon placeholder
                // rows. Only surface it when the JP tankoubon numbering plausibly extends THIS
                // line's own numbering: an ended English line will never grow (Erased: 5 EN 2-in-1
                // books, 9 JP — all long released), and a collected-edition line numbers a
                // different unit entirely (Vinland Saga: 14 deluxe 2-in-1 books vs 29 JP →
                // 15 phantom "Coming Soon" rows for content released years ago). The name-based
                // is_omnibus flag misses "Book Edition"-style lines, so also treat per-volume
                // composition data or a collected-edition page count (median ≥320pp vs ~200pp
                // singles) as the tell. The 0 indexer thunk skips the expensive search —
                // AniList/MangaUpdates supply the total.
                if (status != AuthorStatusType.Ended && !IsCollectedEdition(line.IsOmnibus, gcdVolumes.Values))
                {
                    japaneseTotal = Math.Max(volumeCount,
                        DetermineVolumeCount(0, ani?.Status, ani?.Volumes, muVolumes, mdx?.HighestVolume ?? 0, () => 0));
                }
                else
                {
                    japaneseTotal = volumeCount;
                }
            }
            else
            {
                volumeCount = DetermineVolumeCount(maxVolumeCap,
                    ani?.Status,
                    ani?.Volumes,
                    muVolumes,
                    mdx?.HighestVolume ?? 0,
                    () => resolveVolumeDetails ? _mangaSearchService.FindHighestVolume(name) : 0);
                status = DetermineStatus(ani?.Status, mu?.Completed ?? false);

                // A finished series whose two authorities disagree is the JP-vs-EN tell — surface it.
                if (ani?.Status == "FINISHED" && ani.Volumes.HasValue && ani.Volumes.Value > 0 &&
                    muVolumes > 0 && ani.Volumes.Value != muVolumes)
                {
                    _logger.Warn("Volume count disagreement for '{0}': AniList={1}, MangaUpdates={2} -> using {3}",
                        name,
                        ani.Volumes.Value,
                        muVolumes,
                        volumeCount);
                }

                // No separate English-vs-JP split without GCD: total equals the available count.
                japaneseTotal = volumeCount;
            }

            // Plausibility window for BACKFILLED dates only. The ISBN/title backfills occasionally
            // match a wrong book entirely (Fire Force vol 1 came back 1790; a 2024 series' vol 6
            // came back 2018), and Google Books year-only precision ("2019") parses to a fake
            // Jan 1 — an English volume can't predate the series' own start (GCD line year, else
            // the JP serialization start) or land far in the future, and no EN manga volume
            // releases on New Year's Day. Out-of-window dates become null (TBA), re-resolvable;
            // never persisted junk. Artifact dates BYPASS this window — they're curated (incl.
            // DATE_OVERRIDES), and a line's year_began describes the matched edition, not the
            // first English release (Fairy Tail's Kodansha line began 2011; the true Del Rey
            // dates for vols 1-9 are 2008-2009 and must not be windowed away).
            var floorYear = Math.Max(1950, (line?.YearBegan ?? ani?.StartYear ?? 1950) - 1);
            var ceilingYear = DateTime.UtcNow.Year + 3;

            DateTime? SanitizeBackfill(DateTime? d, int volumeNumber)
            {
                if (d.HasValue && (d.Value.Year < floorYear ||
                                   d.Value.Year > ceilingYear ||
                                   (d.Value.Month == 1 && d.Value.Day == 1)))
                {
                    _logger.Debug("Implausible backfill date {0:yyyy-MM-dd} for '{1}' Vol. {2} (window {3}-{4}) — dropping",
                        d.Value,
                        displayName,
                        volumeNumber,
                        floorYear,
                        ceilingYear);
                    return null;
                }

                return d;
            }

            // D6: one series-level MangaDex fetch of the English-locale cover art, joined by AniList
            // id (never by title alone) — the id this pass resolved, else the stored binding. Manga
            // only: MangaDex has no light-novel entries. Null (unresolved, rate-limited, or the
            // lookup path) sends every volume to the next source.
            MangaDexCovers mangaDexCovers = null;
            var mangaDexAniListId = ani?.Id ?? anilistId;

            // Preferred Edition (2026-09-24, spec §2.2 Covers): a non-English edition asks MangaDex for its
            // own locale's art, joined by the anchor's English title (the id search's term).
            var mangaDexSource = editionLine == null ? "mangadex-en" : "mangadex-" + editionLanguage.ToLowerInvariant();

            if (resolveVolumeDetails && library == LibraryType.Manga && volumeCount > 0 && mangaDexAniListId.HasValue)
            {
                mangaDexCovers = editionLine == null
                    ? _mangaDexService.GetEnglishCovers(mangaDexAniListId.Value, displayName)
                    : _mangaDexService.GetLocaleCovers(mangaDexAniListId.Value, identityName, editionLanguage);
            }

            // D5 (2026-09-17): the names an ISBN record may answer to — the display name, AniList's
            // Latin titles, the entry's own id name and the bound line's aliases (what the AniList
            // retry gets from the hint line; recomputed here rather than threaded out of it, which
            // runs in a task and not at all for a bound entry). One catalogue read per pass.
            // Preferred Edition (2026-09-24, spec §2.2 Aliases): an edition's alternate titles lead with its
            // own language's rows -- read once, for the record keys here and the answer's AltTitles. Its
            // blurbs are judged in its language.
            var editionAliases = editionLine == null ? null : _gcdMetadataService.GetAliasRows(editionLine.GcdSeriesId);
            var recordKeys = editionLine == null
                ? BuildAltTitles(displayName, ani)
                : BuildEditionAltTitles(displayName, identityName, editionAliases, editionLanguage, ani);
            var descriptionLanguage = editionLanguage ?? "en";

            if (resolveVolumeDetails && line != null)
            {
                if (idName.IsNotNullOrWhiteSpace())
                {
                    recordKeys.Add(idName);
                }

                // The catalogue name of the line whose ISBNs are checked (the display name may be
                // AniList's, or the stripped form of it).
                if (line.Name.IsNotNullOrWhiteSpace())
                {
                    recordKeys.Add(line.Name);
                }

                recordKeys.AddRange(_gcdMetadataService.GetAliases(line.GcdSeriesId) ?? new List<string>());

                // Preferred Edition: an edition record may name the series by its English anchor or the
                // line's local title as well as by the display name.
                if (editionLine != null)
                {
                    recordKeys.Add(identityName);

                    if (editionLine.LocalName.IsNotNullOrWhiteSpace())
                    {
                        recordKeys.Add(editionLine.LocalName);
                    }
                }
            }

            var volumes = new List<MangaVolumeMetadata>();
            var googleMisses = 0;
            var volume1Missed = false;
            string artifactVolume1Cover = null;
            string googleVolume1Cover = null;
            string googleVolume1CoverSize = null;

            // Audiobook identity B1 (2026-09-17, D3): the accepted ISBN records by volume number — their
            // title fields are the subtitle's fallback after the Audible pass below the loop.
            var isbnRecords = new Dictionary<double, VolumeDetails>();

            // D5 re-admission (B3b, 2026-09-18): the records the loop rejected by title, held with the
            // volume they answered for until after the Audible step — a record titled by the volume's
            // own subtitle ("Early Years") is the volume's, not a mismatch. Marked rejected below
            // otherwise; the mark is deferred, never lost. Each holds the volume as the loop's title
            // search found it and that search's answer, so a re-admitted volume can be replayed from
            // the record first, the title search second — the accepted path's order.
            var heldRecords = new List<HeldRecord>();

            // What the loop takes from an accepted ISBN record, in one place so the re-admission pass
            // cannot drift from it: the record is the subtitle fallback (not without a title),
            // its page count and date fill a gap, its description passes D2, and its cover is taken when
            // no higher source (artifact, MangaDex en) already gave one — the volume's cover rule; a pin
            // lands on top of all of it, after the pass. The cover the record declared is handed back
            // for the volume-1 poster tier.
            void TakeIsbnRecord(MangaVolumeMetadata volume, VolumeDetails record, List<string> rejections, out string recordCover, out string recordCoverSize)
            {
                var i = (int)volume.VolumeNumber;

                if (record.Title.IsNotNullOrWhiteSpace())
                {
                    isbnRecords[volume.VolumeNumber] = record;
                }

                if (volume.PageCount <= 0)
                {
                    volume.PageCount = record.PageCount ?? 0;
                }

                if (volume.ReleaseDate == null)
                {
                    volume.ReleaseDate = SanitizeBackfill(record.ReleaseDate, i);
                }

                recordCover = record.CoverUrl;
                recordCoverSize = record.CoverSize;

                // D2: every description is validated; a rejected one is logged and treated as absent.
                if (record.Description != null)
                {
                    if (GoogleBooksService.IsAcceptableDescription(record.Description, record.Language, descriptionLanguage, out var isbnReason))
                    {
                        volume.Overview = record.Description;
                        volume.OverviewSource = "isbn";
                    }
                    else
                    {
                        rejections.Add("isbn rejected: " + isbnReason);
                    }
                }

                TakeGoogleCover(volume, recordCover);
            }

            // What the loop takes from a title-search hit, likewise in one place: page count and date
            // where still missing, the ISBN and its cover only for a volume with no ISBN of its own, the
            // description through D2 only where the record gave none.
            void TakeTitleHit(MangaVolumeMetadata volume, VolumeDetails byTitle, List<string> rejections, ref string googleCover, ref string googleCoverSize)
            {
                var i = (int)volume.VolumeNumber;

                if (volume.PageCount <= 0)
                {
                    volume.PageCount = SanitizeBackfillPages(byTitle.PageCount ?? 0);
                }

                if (volume.ReleaseDate == null)
                {
                    volume.ReleaseDate = SanitizeBackfill(byTitle.ReleaseDate, i);
                }

                if (volume.Isbn13.IsNullOrWhiteSpace())
                {
                    volume.Isbn13 = byTitle.Isbn13;
                    googleCover = byTitle.CoverUrl;
                    googleCoverSize = byTitle.CoverSize;
                    TakeGoogleCover(volume, googleCover);
                }

                if (volume.Overview == null && byTitle.Description != null)
                {
                    if (GoogleBooksService.IsAcceptableDescription(byTitle.Description, byTitle.Language, descriptionLanguage, out var titleReason))
                    {
                        volume.Overview = byTitle.Description;
                        volume.OverviewSource = "title";
                    }
                    else
                    {
                        rejections.Add("title rejected: " + titleReason);
                    }
                }
            }

            // Quota breaker, per pass: a 429 is the keyed project's daily quota, gone until the
            // Pacific-midnight reset, so after three in a row the remaining volumes skip Google
            // (each still counted as a miss for the Warn below) instead of each paying two rejected
            // calls. A 5xx/transport null neither counts nor resets; a real answer resets.
            var consecutiveQuotaHits = 0;
            var googleOut = false;
            var currentVolume = 0;

            VolumeDetails Google(Func<VolumeDetails> lookup, out bool quotaMiss)
            {
                quotaMiss = googleOut;

                if (googleOut)
                {
                    return null;
                }

                try
                {
                    var answer = lookup();

                    if (answer != null)
                    {
                        consecutiveQuotaHits = 0;
                    }

                    return answer;
                }
                catch (GoogleBooksQuotaException)
                {
                    quotaMiss = true;

                    if (++consecutiveQuotaHits >= QuotaBreakerThreshold)
                    {
                        googleOut = true;
                        _logger.Debug("Google Books quota: skipping the remaining {0} volumes of \"{1}\" this pass", volumeCount - currentVolume, displayName);
                    }

                    return null;
                }
            }

            // Preferred Edition (2026-09-24, D6): the catalogue's build date dates a current-year deposit
            // copy. Read only on the edition path.
            DateTime? artifactGeneratedAt = null;

            if (editionLine != null &&
                DateTime.TryParse(_gcdMetadataService.ArtifactInfo()?.GeneratedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var generated))
            {
                artifactGeneratedAt = generated;
            }

            for (var i = 1; i <= volumeCount; i++)
            {
                // Lightweight LOOKUP path: emit the volume STRUCTURE only (number) and skip every
                // per-volume external call. Date/ISBN/page/cover/overview are resolved on add/refresh.
                if (!resolveVolumeDetails)
                {
                    volumes.Add(new MangaVolumeMetadata { VolumeNumber = i });
                    continue;
                }

                currentVolume = i;

                string artifactCover = null;
                string mangaDexCover = null;
                string googleCover = null;
                string googleCoverSize = null;
                var googleMissed = false;
                var googleRejected = false;
                var rejections = new List<string>();
                VolumeDetails byIsbn = null;
                HeldRecord held = null;
                GcdVolume gv = null;
                var hasArtifactVolume = gcdVolumes != null && gcdVolumes.TryGetValue(i, out gv);

                if (hasArtifactVolume)
                {
                    // Per-volume real data from the GCD artifact (date / ISBN / page count), and the
                    // cover the artifact looked up by THIS edition's ISBN (OpenLibrary / openBD) — it
                    // outranks every other cover: the others describe *an* edition of *something*
                    // with this name.
                    artifactCover = gv.CoverUrl.IsNotNullOrWhiteSpace() ? gv.CoverUrl : null;
                }

                // D5: MangaDex's English-locale cover for this volume number (the series fetch above).
                if (mangaDexCovers != null && mangaDexCovers.CoversByVolume.TryGetValue(i, out var mangaDexVolumeCover))
                {
                    mangaDexCover = mangaDexVolumeCover;
                }

                // The volume as the artifact leaves it; the providers below fill what it lacks. D5: the
                // first English cover — the artifact's ISBN cover, MangaDex en, Google's record cover
                // (TakeGoogleCover, once a record or the title search declares one); none -> the series
                // poster (BookInfoProxy applies VolumeCoverUrl).
                // Preferred Edition (2026-09-24, D6): an edition volume keeps a coarse date with its
                // precision; the English path reads day-precision dates only, exactly as before.
                var (artifactDate, artifactPrecision) = !hasArtifactVolume ? ((DateTime?)null, (string)null)
                    : editionLine != null ? EditionDates.Resolve(gv, artifactGeneratedAt, DateTime.UtcNow)
                    : (ParseDate(gv.ReleaseDate), (string)null);

                var volume = new MangaVolumeMetadata
                {
                    VolumeNumber = i,
                    ReleaseDate = artifactDate,
                    ReleaseDatePrecision = artifactPrecision,
                    Isbn13 = hasArtifactVolume ? gv.Isbn13 : null,
                    PageCount = hasArtifactVolume ? gv.PageCount ?? 0 : 0,
                    CoverUrl = artifactCover ?? mangaDexCover,
                    CoverSource = artifactCover != null ? "opentome" : mangaDexCover != null ? mangaDexSource : "series",
                    OverviewSource = "none",
                    ArtifactTitle = hasArtifactVolume ? gv.Title : null
                };

                // D1: the Google Books record for THIS ISBN, always when there is one — its description
                // and the largest cover it declares. Page count / release date fill a gap only (the
                // trigger they had when they gated the call: missing from the artifact). Null = the
                // lookup did not answer (429 / 5xx / transport, or the breaker): the volume is a miss —
                // counted, warned once per series below, and flagged so BookInfoProxy substitutes no
                // poster for the cover it could not fetch (the ratchet keeps the local one).
                if (volume.Isbn13.IsNotNullOrWhiteSpace())
                {
                    var isbn13 = volume.Isbn13;

                    byIsbn = Google(() => _googleBooksService.LookupByIsbn(isbn13), out _);

                    if (byIsbn == null)
                    {
                        googleMissed = true;
                    }
                    else if (byIsbn.Title.IsNotNullOrWhiteSpace() && !RecordNamesSeries(byIsbn, displayName, recordKeys))
                    {
                        // D5 (2026-09-17): an answer, but not this series' — treated as no record:
                        // nothing below reads it and the title search runs as for a volume whose
                        // record gave nothing. Not a miss: the poster is minted in place of its cover.
                        // An ISBN-only answer (Google has no record) is not a record to judge: it
                        // takes the branch below, where its null fields already yield nothing.
                        // Held, not marked (B3b): the Audible step may yet show the title is the volume's own.
                        googleRejected = true;
                        rejections.Add($"isbn rejected: title mismatch '{byIsbn.Title}'");
                        held = new HeldRecord(volume, byIsbn, isbn13);
                        heldRecords.Add(held);
                        byIsbn = null;
                    }
                    else
                    {
                        TakeIsbnRecord(volume, byIsbn, rejections, out googleCover, out googleCoverSize);
                    }
                }

                // The title search (D1): page/date backfill on its current trigger (still missing after
                // the ISBN record), the description only when the volume has no ISBN or the ISBN record
                // gave no usable one, the ISBN and the cover only when the volume has no ISBN of its own.
                // Validated to the exact volume number and to English records (GoogleBooksService).
                if (NeedsTitleSearch(volume))
                {
                    // A 429 here is a miss like the ISBN one; the search's own null (no match, 5xx) is not.
                    // Preferred Edition (2026-09-24): an edition asks in its own language and volume label.
                    var byTitle = Google(() => editionLine == null
                        ? _googleBooksService.LookupVolume(displayName, i)
                        : _googleBooksService.LookupVolume(displayName, i, editionLanguage), out var titleQuotaMiss);
                    googleMissed |= titleQuotaMiss;

                    if (held != null)
                    {
                        held.ByTitle = byTitle;
                        held.TitleQuotaMiss = titleQuotaMiss;
                    }

                    if (byTitle != null)
                    {
                        TakeTitleHit(volume, byTitle, rejections, ref googleCover, ref googleCoverSize);
                    }
                }

                if (i == 1)
                {
                    artifactVolume1Cover = artifactCover;
                    googleVolume1Cover = googleCover;
                    googleVolume1CoverSize = googleCoverSize;
                    volume1Missed = googleMissed;
                }

                if (googleMissed)
                {
                    googleMisses++;
                }

                _logger.Debug("Volume {0} Vol. {1}: description from {2}{3}, cover from {4}",
                    displayName,
                    i,
                    volume.OverviewSource,
                    rejections.Any() ? " (" + string.Join("; ", rejections) + ")" : string.Empty,
                    volume.CoverSource);

                volume.GoogleMissed = googleMissed;
                volume.GoogleRejected = googleRejected;
                volumes.Add(volume);
            }

            // Audiobook identity B1 (2026-09-17, D1): light novels on the refresh path only (D7) — a
            // manga series never asks Audible.
            //
            // Preferred Edition (2026-09-24, D8; M14 fix round 1, I1): a non-English edition takes the English
            // audiobook only when its line is 1:1 with the English line (the same work's counterpart, neither
            // a collected edition). That is decided from the catalogue alone and on EVERY pass -- the add and
            // the first refresh run without volume details, and the Audio editions they mint are the ones
            // stored (UseDbFieldsFrom keeps Monitored), so they must already be minted unmonitored
            // (BookInfoProxy). Only the Audible call itself stays on the refresh path, asked by the anchor's
            // English name (ruling S4: the query only -- the subtitles keep the display name). English:
            // unchanged, and IsOneToOne is never asked.
            var audioSkipped = library == LibraryType.LightNovel && editionLine != null && !IsOneToOne(editionLine, gcd);
            var audibleAnswered = false;
            var audibleStepRan = library == LibraryType.LightNovel && resolveVolumeDetails;
            if (audibleStepRan)
            {
                audibleAnswered = ApplyAudiobookIdentity(displayName, recordKeys, volumes, isbnRecords, !audioSkipped, editionLanguage, editionLine == null ? displayName : identityName);
            }

            // D5 re-admission (B3b, 2026-09-18): a held record whose title is the volume's own — Audible's
            // title or subtitle for it, or the subtitle derived from them — is taken now exactly as the
            // loop takes an accepted record: the volume goes back to what the loop's title search found,
            // the record is taken (TakeIsbnRecord: the same D2 gate, the same cover rule), then the title
            // search's answer — the loop already spent that call — fills what the record left, on the
            // accepted path's own trigger (TakeTitleHit); the volume-1 poster tier and the miss flag
            // follow. The date guards and the pins run below, after this pass, so a re-admitted date
            // meets them as any other. Anything else is marked rejected here, after the Audible step.
            // Without that step (a manga) everything held is marked, as the loop did before the deferral.
            foreach (var held in heldRecords)
            {
                var volume = held.Volume;

                if (audibleStepRan && TitleIsVolumesOwn(held.Record, volume))
                {
                    var rejections = new List<string>();
                    var loopMissed = volume.GoogleMissed;
                    var googleMissed = false;   // the record answered

                    held.Restore();

                    // The isbnRecords entry TakeIsbnRecord writes is inert on this path: its only reader,
                    // ApplyAudiobookIdentity, has run; the subtitle is re-derived from the record below.
                    TakeIsbnRecord(volume, held.Record, rejections, out var recordCover, out var recordCoverSize);

                    if (NeedsTitleSearch(volume))
                    {
                        googleMissed |= held.TitleQuotaMiss;

                        if (held.ByTitle != null)
                        {
                            TakeTitleHit(volume, held.ByTitle, rejections, ref recordCover, ref recordCoverSize);
                        }
                    }

                    googleMisses += (googleMissed ? 1 : 0) - (loopMissed ? 1 : 0);
                    volume.GoogleMissed = googleMissed;
                    volume.GoogleRejected = false;
                    volume.Subtitle = SubtitleOf(volume.ArtifactTitle, volume.Audio, held.Record, displayName, recordKeys, volume.VolumeNumber, editionLanguage);
                    volume.SubtitleRejected = volume.Subtitle == null && HadSubtitleCandidate(volume.ArtifactTitle, volume.Audio, held.Record);

                    if (volume.VolumeNumber == 1)
                    {
                        googleVolume1Cover = recordCover;
                        googleVolume1CoverSize = recordCoverSize;
                        volume1Missed = googleMissed;
                    }

                    _logger.Debug("Volume {0} Vol. {1}: isbn re-admitted: title is the volume's own '{2}'; description from {3}{4}, cover from {5}",
                        displayName,
                        volume.VolumeNumber,
                        held.Record.Title,
                        volume.OverviewSource,
                        rejections.Any() ? " (" + string.Join("; ", rejections) + ")" : string.Empty,
                        volume.CoverSource);
                }
                else
                {
                    _googleBooksService.MarkIsbnRecordRejected(held.Isbn13);   // never permanent on disk: re-fetched monthly
                }
            }

            // Preferred Edition (2026-09-24, D6): a year/month-precision date is a deliberate stand-in
            // (Dec 31, a month's end); volumes sharing it are no batch stamp and it is no outlier. Edition
            // path only -- the English list is passed whole, as before.
            var dateChecked = editionLine == null ? volumes : volumes.Where(v => v.ReleaseDatePrecision == null).ToList();
            NullBatchStampDates(dateChecked);
            NullDateOutliers(dateChecked);

            // Local pins (overrides.json) win over every provider and guard. Keyed per library
            // (LibraryTypes.PinKey): a light novel's pins never touch the manga of the same name.
            // Preferred Edition (2026-09-24, ruling S1): an edition resolve keys them by the entry's
            // stored name -- a re-resolve that kept its name must not orphan its pins.
            // Ruling (c): an English resolve inside BookInfoProxy's scope keys them by the stored name too.
            var pinName = edition != null ? edition.StoredName : EnglishPinName.Value;
            _metadataOverrides.Apply(LibraryTypes.PinKey(pinName ?? displayName, library), volumes);

            // D4: the series poster is the English volume-1 cover — MangaDex's en-locale volume 1
            // (manga only), else the artifact's volume-1 cover, else Google's volume-1 record cover
            // when it is a scan (small/medium/large), else Google's volume-1 thumbnail asked at the
            // size Google holds (2026-09-24: most records declare only the thumbnail — 783 of 925 on
            // the dev server — so the scan-only rule left 14 of 55 entries on AniList's Japanese art; see
            // PosterGradeThumbnail), else AniList's art (Japanese: the last English-less resort), else
            // MangaDex's search cover, else none. On the lookup path only the last three exist.
            string posterUrl;
            string posterSource;
            var pinnedVolume1Cover = resolveVolumeDetails ? volumes.FirstOrDefault(v => v.VolumeNumber == 1 && v.CoverSource == "pin")?.CoverUrl : null;

            // A pinned volume-1 cover is the operator's word (B3b, 2026-09-18): it is the poster before any provider.
            if (pinnedVolume1Cover != null)
            {
                posterUrl = pinnedVolume1Cover;
                posterSource = "pin";
            }
            else if (editionLine != null && artifactVolume1Cover != null)
            {
                // Preferred Edition (2026-09-24, spec §2.2 Covers): for a non-English edition the artifact's
                // cover -- looked up by THIS edition's ISBN -- outranks MangaDex's locale art.
                posterUrl = artifactVolume1Cover;
                posterSource = "opentome";
            }
            else if (mangaDexCovers?.Volume1 != null)
            {
                posterUrl = mangaDexCovers.Volume1;
                posterSource = mangaDexSource;
            }
            else if (artifactVolume1Cover != null)
            {
                posterUrl = artifactVolume1Cover;
                posterSource = "opentome";
            }
            else if (googleVolume1Cover != null && PosterGradeCoverSizes.Contains(googleVolume1CoverSize))
            {
                posterUrl = googleVolume1Cover;
                posterSource = "google";
            }
            else if (googleVolume1Cover != null && ThumbnailCoverSizes.Contains(googleVolume1CoverSize ?? string.Empty))
            {
                posterUrl = PosterGradeThumbnail(googleVolume1Cover);
                posterSource = "google-thumbnail";
            }
            else if (aniCoverUrl != null)
            {
                posterUrl = aniCoverUrl;
                posterSource = "anilist";
            }
            else if (mdx?.CoverUrl.IsNotNullOrWhiteSpace() == true)
            {
                posterUrl = mdx.CoverUrl;
                posterSource = "mangadex";
            }
            else
            {
                posterUrl = null;
                posterSource = "none";
            }

            // The volume rows' fallback art is the entry's OWN poster, fixed before the display
            // fallback below: a parent work's or other medium's picture is the series poster only.
            var volumeCoverUrl = posterUrl;

            // What this pass binds (AuthorMetadata.AniListId): a strict/catalogue/id match only.
            var pinnedId = ani != null && PinnedVia.Contains(ani.MatchedVia) ? ani.Id : null;
            var displayFetchFailed = false;

            // Display fallback (OpenTome 2026-09-24): an entry that ends this pass with NO AniList id
            // borrows the catalogue line's display-only AniList entry for the poster and the overview
            // it has no source for. Display only: the id is never returned, never pinned, and nothing
            // else is read from that entry.
            if (DisplayFallbackAllowed(name, library, anilistId, pinnedId, hint) && (posterUrl == null || overview.IsNullOrWhiteSpace()))
            {
                var display = DisplayMedia(lookup, hint.DisplayAnilistId.Value);
                var used = new List<string>();

                // No answer (429 / transport / gone): the gaps it would have filled stay gaps this
                // pass, and BookInfoProxy keeps the stored poster/overview instead of blanking them.
                displayFetchFailed = display == null;

                if (posterUrl == null && display?.CoverImageUrl.IsNotNullOrWhiteSpace() == true)
                {
                    posterUrl = display.CoverImageUrl;
                    posterSource = "anilist-display";
                    used.Add("cover");
                }

                if (overview.IsNullOrWhiteSpace() && display?.Description.IsNotNullOrWhiteSpace() == true)
                {
                    overview = display.Description;
                    used.Add("overview");
                }

                if (used.Any())
                {
                    _logger.Debug("Display fallback for {0}: AniList {1} via {2} ({3})",
                        displayName,
                        hint.DisplayAnilistId.Value,
                        hint.DisplayAnilistVia ?? "?",
                        string.Join("|", used));
                }
            }

            if (resolveVolumeDetails)
            {
                var total = volumes.Count;
                var fromIsbn = volumes.Count(v => v.OverviewSource == "isbn");
                var fromTitle = volumes.Count(v => v.OverviewSource == "title");
                var fromArtifact = volumes.Count(v => v.CoverSource == "opentome");
                var fromMangaDex = volumes.Count(v => v.CoverSource == mangaDexSource);
                var fromGoogle = volumes.Count(v => v.CoverSource == "google");
                var fromPin = volumes.Count(v => v.CoverSource == "pin");
                var withAudio = volumes.Count(v => v.Audio != null);

                // D10: one line per series says what the refresh found. A light novel's line ends with
                // its audio count (volumes carrying an Audible product of their own); a manga's line is
                // unchanged (D7).
                // Preferred Edition (2026-09-24): the MangaDex count is labelled by its locale ("mangadex-fr").
                _logger.Info("Volume metadata {0}: descriptions {1}/{2} (isbn {3}, title {4}, none {5}), covers {6}/{2} (opentome {7}, {14} {8}, google {9}, pin {10}, series {11}), poster via {12}{13}",
                    displayName,
                    fromIsbn + fromTitle,
                    total,
                    fromIsbn,
                    fromTitle,
                    total - fromIsbn - fromTitle,
                    fromArtifact + fromMangaDex + fromGoogle + fromPin,
                    fromArtifact,
                    fromMangaDex,
                    fromGoogle,
                    fromPin,
                    total - fromArtifact - fromMangaDex - fromGoogle - fromPin,
                    posterSource,
                    library == LibraryType.LightNovel ? $", audio {withAudio}/{total}" : string.Empty,
                    mangaDexSource);

                // Spec §5: Google Books 429/5xx/unreachable — descriptions absent for this pass (the
                // ratchet keeps local ones), covers fell through; say so once per series, not per volume.
                if (googleMisses > 0)
                {
                    _logger.Warn("Google Books did not answer for {0} of {1} volumes of \"{2}\" this pass (rate limit, quota or transport); descriptions kept local, covers fell through",
                        googleMisses,
                        total,
                        displayName);
                }
            }

            return new MangaSeriesMetadata
            {
                DisplayName = displayName,
                Status = status,
                VolumeCount = volumeCount,
                Overview = overview,
                CoverUrl = posterUrl,
                PosterSource = posterSource,

                // Volume 1's Google lookup did not answer and the poster landed below Google's tier:
                // it may have fallen only because of the miss (a thumbnail-only record is a definite
                // answer, not a miss). BookInfoProxy keeps the stored poster for this pass.
                PosterFellBackOnMiss = volume1Missed && !PosterSourcesAboveGoogle.Contains(posterSource) && posterSource != mangaDexSource,
                VolumeCoverUrl = volumeCoverUrl,
                RatingValue = ratingValue,
                Volumes = volumes,
                JapaneseTotal = japaneseTotal,
                AltTitles = editionLine == null ? BuildAltTitles(displayName, ani) : BuildEditionAltTitles(displayName, identityName, editionAliases, editionLanguage, ani),
                ParentName = parentName,
                AniListId = pinnedId,
                DisplayFetchFailed = displayFetchFailed,
                MatchedVia = ani?.MatchedVia,
                AudibleAnswered = audibleAnswered,
                AudioSkipped = audioSkipped,
                Writer = library == LibraryType.LightNovel ? (gcd ?? line)?.Author : null,
                IdentityName = identityName,
                EditionLanguage = editionLanguage,
                TomeLineId = line?.TomeId,
                EditionCollected = lineCollected
            };
        }

        // Google's record cover makes the series poster only as a scan (D4 after finding #1 of the
        // 2026-09-16 review): thumbnail/smallThumbnail are the 128 px catalogue images.
        private static readonly HashSet<string> PosterGradeCoverSizes = new HashSet<string> { "small", "medium", "large" };

        // The imageLinks keys of a catalogue-only record (128 px): the poster's tier below a scan.
        private static readonly HashSet<string> ThumbnailCoverSizes = new HashSet<string> { "thumbnail", "smallThumbnail" };

        // The D4 tiers a Google volume-1 miss cannot have cost: a poster from these was chosen
        // over Google's regardless.
        private static readonly HashSet<string> PosterSourcesAboveGoogle = new HashSet<string> { "pin", "mangadex-en", "opentome", "google", "google-thumbnail" };

        private static readonly Regex GoogleContentUrl = new Regex(@"^https?://books\.google\.com/books/(?:publisher/)?content\?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // A Google thumbnail URL asked at poster size (2026-09-24): books/content's `fife=w800` returns the
        // SAME picture scaled to at most the stored image — 300×450 for a catalogue-only record, 800 px
        // wide for a publisher's scan — never the grey "image not available" PNG that a synthesized zoom
        // (zoom=2..6) gives a catalogue record (30 of 30 stored thumbnail URLs checked live, imgtk kept).
        // `edge=curl` (the page-curl overlay) is dropped. Any other URL, or one already sized, is returned as is.
        public static string PosterGradeThumbnail(string url)
        {
            if (url.IsNullOrWhiteSpace() || !GoogleContentUrl.IsMatch(url) || url.Contains("fife=", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            return url.Replace("&edge=curl", string.Empty) + "&fife=w800";
        }

        // The title search's trigger (D1): something the artifact and the ISBN record left missing.
        private static bool NeedsTitleSearch(MangaVolumeMetadata volume)
        {
            return volume.PageCount <= 0 || volume.ReleaseDate == null || volume.Isbn13.IsNullOrWhiteSpace() || volume.Overview == null;
        }

        // A record the loop rejected by title (D5), held for the re-admission pass with the volume as the
        // loop's title search found it (the artifact's page count and date, no description) and that
        // search's answer, so the replay runs the record first and the title search second.
        private sealed class HeldRecord
        {
            public HeldRecord(MangaVolumeMetadata volume, VolumeDetails record, string isbn13)
            {
                Volume = volume;
                Record = record;
                Isbn13 = isbn13;
                _pageCount = volume.PageCount;
                _releaseDate = volume.ReleaseDate;
                _overview = volume.Overview;
                _overviewSource = volume.OverviewSource;
            }

            public MangaVolumeMetadata Volume { get; }
            public VolumeDetails Record { get; }
            public string Isbn13 { get; }
            public VolumeDetails ByTitle { get; set; }
            public bool TitleQuotaMiss { get; set; }

            private readonly int _pageCount;
            private readonly DateTime? _releaseDate;
            private readonly string _overview;
            private readonly string _overviewSource;

            // The volume as the loop's title search left it, before the record's replay.
            public void Restore()
            {
                Volume.PageCount = _pageCount;
                Volume.ReleaseDate = _releaseDate;
                Volume.Overview = _overview;
                Volume.OverviewSource = _overviewSource;
            }
        }

        // D5: Google's cover is the volume's only below the artifact's, MangaDex's and a pin's — the one
        // cover rule, applied wherever a Google cover (an ISBN record's, the title search's) arrives.
        private static void TakeGoogleCover(MangaVolumeMetadata volume, string googleCover)
        {
            if (googleCover != null && volume.CoverSource == "series")
            {
                volume.CoverUrl = googleCover;
                volume.CoverSource = "google";
            }
        }

        // D5 re-admission (B3b, 2026-09-18): the held record's title is the volume's own when it equals,
        // normalised, Audible's title or subtitle for the volume or the subtitle derived from them —
        // any non-blank one. The trailing edition tag Google appends is ignored as RecordNamesSeries
        // ignores it; a blank never matches a blank.
        private static bool TitleIsVolumesOwn(VolumeDetails record, MangaVolumeMetadata volume)
        {
            var title = TitleMatcher.Normalize(TrailingEditionTag.Replace(record.Title ?? string.Empty, string.Empty));

            return title.Length > 0 && new[] { volume.Audio?.Title, volume.Audio?.Subtitle, volume.Subtitle }
                .Where(candidate => candidate.IsNotNullOrWhiteSpace())
                .Any(candidate => TitleMatcher.Normalize(candidate) == title);
        }

        // Consecutive 429s that open the per-pass quota breaker (see GetSeries).
        private const int QuotaBreakerThreshold = 3;

        // Audiobook identity B1 (2026-09-17, D1-D3): the series' Audible products, mapped onto the
        // volumes by Audible's own sequence field. Singles first ("7" -> Vol. 7; "3.5" -> a volume
        // numbered 3.5, and GetSeries' volumes are the integers 1..count, so it is dropped), then packs
        // ("1-2"): the first volume in the range without a product of its own (and not covered by an
        // earlier pack) carries the pack and the other volumes in range are covered by it; a volume
        // with its own single keeps it and is not covered — the single wins. A volume beyond the count
        // gets nothing. Then every volume's subtitle: the one D3 rule on its own single product first,
        // else on its accepted ISBN record. A null answer (Audible did not answer) is warned once per
        // series and gives no identity; the subtitle still falls back to Google, so nothing else about
        // the pass changes. Returns whether Audible answered (D4a): an answer, even an empty one,
        // makes the volumes' CoveredByVolume assertions BookInfoProxy marks on the Audio editions.
        //
        // Preferred Edition (2026-09-24, D8 + ruling S4): askAudible false (an edition not 1:1 with the
        // English line) gets no audio -- the subtitle pass below still runs; Audible is neither asked nor
        // counted as answering. audibleName is the Audible query only (the anchor's English name for an
        // edition, the display name for English); the subtitles and logs keep displayName.
        private bool ApplyAudiobookIdentity(string displayName, List<string> recordKeys, List<MangaVolumeMetadata> volumes, Dictionary<double, VolumeDetails> isbnRecords, bool askAudible, string editionLanguage, string audibleName)
        {
            var products = askAudible ? _audibleCatalogService.GetSeries(audibleName, recordKeys) : new List<AudibleProduct>();

            if (askAudible && products == null)
            {
                _logger.Warn("Audible did not answer for \"{0}\" this pass", audibleName);
            }
            else
            {
                var byNumber = volumes.ToDictionary(v => v.VolumeNumber);
                var sequenced = products
                    .Select(p => (Product: p, Range: AudibleSequence.Parse(p.Sequence)))
                    .Where(x => x.Range.HasValue)
                    .Select(x => (x.Product, Start: x.Range.Value.From, End: x.Range.Value.To))
                    .ToList();

                foreach (var single in sequenced.Where(x => x.Start == x.End))
                {
                    if (byNumber.TryGetValue(single.Start, out var volume) && volume.Audio == null)
                    {
                        volume.Audio = Identity(single.Product, null, null);
                    }
                }

                foreach (var pack in sequenced.Where(x => x.Start < x.End))
                {
                    MangaVolumeMetadata carrier = null;

                    foreach (var volume in volumes.Where(v => v.VolumeNumber >= pack.Start && v.VolumeNumber <= pack.End).OrderBy(v => v.VolumeNumber))
                    {
                        if (volume.Audio != null)
                        {
                            _logger.Debug("Volume {0} Vol. {1}: keeps audio {2}, not covered by pack {3} \"{4}\" {5}-{6}",
                                displayName,
                                volume.VolumeNumber,
                                volume.Audio.Asin,
                                pack.Product.Asin,
                                pack.Product.Title,
                                pack.Start,
                                pack.End);
                        }
                        else if (volume.CoveredByVolume != null)
                        {
                            // Fix wave (2026-09-17, Minor 1): overlapping packs ("1-2" then "2-3") — a
                            // volume one pack covers is never another's carrier, so no edition holds
                            // both an ASIN and CoveredByVolume; it keeps its first coverage.
                            _logger.Debug("Volume {0} Vol. {1}: already covered by Vol. {2}, not carrier for pack {3} \"{4}\" {5}-{6}",
                                displayName,
                                volume.VolumeNumber,
                                volume.CoveredByVolume,
                                pack.Product.Asin,
                                pack.Product.Title,
                                pack.Start,
                                pack.End);
                        }
                        else if (carrier == null)
                        {
                            carrier = volume;
                            volume.Audio = Identity(pack.Product, pack.Start, pack.End);
                        }
                        else
                        {
                            volume.CoveredByVolume = carrier.VolumeNumber;
                        }
                    }
                }
            }

            foreach (var volume in volumes)
            {
                var audio = volume.Audio;
                isbnRecords.TryGetValue(volume.VolumeNumber, out var record);

                volume.Subtitle = SubtitleOf(volume.ArtifactTitle, audio, record, displayName, recordKeys, volume.VolumeNumber, editionLanguage);
                volume.SubtitleRejected = volume.Subtitle == null && HadSubtitleCandidate(volume.ArtifactTitle, audio, record);

                if (audio == null && volume.CoveredByVolume.HasValue)
                {
                    _logger.Debug("Volume {0} Vol. {1}: audio covered by Vol. {2}", displayName, volume.VolumeNumber, volume.CoveredByVolume);
                }
                else if (audio == null)
                {
                    _logger.Debug("Volume {0} Vol. {1}: audio none", displayName, volume.VolumeNumber);
                }
                else
                {
                    _logger.Debug("Volume {0} Vol. {1}: audio {2} \"{3}\"{4}",
                        displayName,
                        volume.VolumeNumber,
                        audio.Asin,
                        audio.Title,
                        audio.CoversFrom.HasValue ? $" covers {audio.CoversFrom}-{audio.CoversTo}" : string.Empty);
                }
            }

            return askAudible && products != null;
        }

        // The one D3 rule for a volume's subtitle: on its own single product first, else on its accepted
        // ISBN record. A pack's title/subtitle describe the pack ("Publisher's Pack" / "Books 1-2"), not
        // the volume that opens it: only a single product's fields can name one volume's subtitle
        // (Book.UseMetadataFrom keeps a stored subtitle on null, so a wrong one would stick). Shared
        // with the D5 re-admission pass, which re-derives it once the record is the volume's.
        // 2026-09-22: the catalogue's own volume title comes first (OpenTome exports the English
        // line's title from the volume list, already stripped of the series prefix), so a new entry
        // reads "Vol. 1 · Aincrad" on its first refresh instead of after the quota-bound Google pass.
        // Preferred Edition (2026-09-24): editionLanguage = the series' edition, so its own volume labels
        // and edition words are no subtitle (Subtitles.IsJunk); null is today's filter.
        internal static string SubtitleOf(string artifactTitle, AudiobookIdentity audio, VolumeDetails record, string displayName, List<string> recordKeys, double volumeNumber, string editionLanguage = null)
        {
            var single = audio?.CoversFrom == null ? audio : null;

            return Subtitles.Derive(null, artifactTitle, null, displayName, recordKeys, volumeNumber, trustedArtifactCandidate: true, editionLanguage: editionLanguage)
                ?? Subtitles.Derive(single?.Title, single?.Subtitle, null, displayName, recordKeys, volumeNumber, editionLanguage: editionLanguage)
                ?? Subtitles.Derive(record?.Title, record?.Subtitle, record?.SeriesBookTitle, displayName, recordKeys, volumeNumber, editionLanguage: editionLanguage);
        }

        // Fix round 3 (2026-09-24): distinguishes "SubtitleOf found nothing to work with this pass"
        // (a Google quota miss, an unmatched Audible product, no catalogue artifact title -- the
        // stored subtitle is kept, the existing ratchet) from "a candidate existed and was rejected"
        // (Subtitles.IsJunk, or the series-name/volume-label/edition-label gates) -- the caller sets
        // MangaVolumeMetadata.SubtitleRejected from this so Book.UseMetadataFrom clears instead.
        internal static bool HadSubtitleCandidate(string artifactTitle, AudiobookIdentity audio, VolumeDetails record)
        {
            return artifactTitle.IsNotNullOrWhiteSpace() ||
                   audio?.Title.IsNotNullOrWhiteSpace() == true ||
                   audio?.Subtitle.IsNotNullOrWhiteSpace() == true ||
                   record?.Title.IsNotNullOrWhiteSpace() == true ||
                   record?.Subtitle.IsNotNullOrWhiteSpace() == true ||
                   record?.SeriesBookTitle.IsNotNullOrWhiteSpace() == true;
        }

        private static AudiobookIdentity Identity(AudibleProduct product, double? coversFrom, double? coversTo)
        {
            return new AudiobookIdentity
            {
                Asin = product.Asin,
                Title = product.Title,
                Subtitle = product.Subtitle,
                RuntimeMinutes = product.RuntimeMinutes,
                ReleaseDate = product.ReleaseDate,
                CoversFrom = coversFrom,
                CoversTo = coversTo
            };
        }

        public MangaVolumeMetadata ResolveVolume(string displayName, double volumeNumber, bool resolveVolumeDetails = true)
        {
            return ResolveVolume(displayName, volumeNumber, resolveVolumeDetails, null);
        }

        // Preferred Edition (2026-09-24): language = the series' edition (null/"en" = today's English call).
        public MangaVolumeMetadata ResolveVolume(string displayName, double volumeNumber, bool resolveVolumeDetails, string language)
        {
            var bare = new MangaVolumeMetadata { VolumeNumber = volumeNumber };

            // Fractional side-story volumes (3.5) have no searchable "Vol. N" form on Google Books.
            if (!resolveVolumeDetails || volumeNumber <= 0 || volumeNumber % 1 != 0)
            {
                return bare;
            }

            var english = EditionLanguages.IsEnglish(language);
            VolumeDetails details;

            try
            {
                details = english
                    ? _googleBooksService.LookupVolume(displayName, (int)volumeNumber)
                    : _googleBooksService.LookupVolume(displayName, (int)volumeNumber, language);
            }
            catch (GoogleBooksQuotaException)
            {
                // The daily quota: no cover this pass, and no poster in its place (BookInfoProxy).
                bare.GoogleMissed = true;
                return bare;
            }

            if (details == null)
            {
                return bare;
            }

            // No series context here — apply the absolute plausibility window only (see GetSeries).
            var releaseDate = details.ReleaseDate;
            if (releaseDate.HasValue && (releaseDate.Value.Year < 1950 ||
                                         releaseDate.Value.Year > DateTime.UtcNow.Year + 3 ||
                                         (releaseDate.Value.Month == 1 && releaseDate.Value.Day == 1)))
            {
                _logger.Debug("Implausible date {0:yyyy-MM-dd} for '{1}' Vol. {2} — dropping", releaseDate.Value, displayName, volumeNumber);
                releaseDate = null;
            }

            // D2 for a title-only extra: the same gate the GetSeries loop applies (in the edition's language).
            var overview = details.Description;
            var overviewSource = overview != null ? "title" : "none";

            if (overview != null && !GoogleBooksService.IsAcceptableDescription(overview, details.Language, english ? "en" : language.Trim(), out var reason))
            {
                _logger.Debug("Volume {0} Vol. {1}: description from none (title rejected: {2}), cover from {3}", displayName, volumeNumber, reason, details.CoverUrl != null ? "google" : "series");
                overview = null;
                overviewSource = "none";
            }

            return new MangaVolumeMetadata
            {
                VolumeNumber = volumeNumber,
                ReleaseDate = releaseDate,
                Isbn13 = details.Isbn13,
                PageCount = details.PageCount ?? 0,
                CoverUrl = details.CoverUrl,
                CoverSource = details.CoverUrl != null ? "google" : "series",
                Overview = overview,
                OverviewSource = overviewSource
            };
        }

        // Runs the three independent providers concurrently (they share no state) so a lookup costs the
        // slowest single provider, not the sum of all three. Each provider is already fail-soft (catches
        // its own exceptions and returns null), so Task.WaitAll never throws here. MangaDex and
        // MangaUpdates still get the raw name; only the AniList leg is the ranked binding.
        private ProviderLookup FetchProviders(string name, bool relaxed, LibraryType library, int? anilistId, string idName, GcdSeries hint)
        {
            var aniTask = Task.Run(() => ResolveAniList(name, library, anilistId, idName, hint, relaxed));
            var mdxTask = Task.Run(() => _mangaDexService.Lookup(name, relaxed));
            var muTask = Task.Run(() => _mangaUpdatesService.FindSeries(name, relaxed));

            Task.WaitAll(aniTask, mdxTask, muTask);

            return new ProviderLookup
            {
                AniList = aniTask.Result,
                MangaDex = mdxTask.Result,
                MangaUpdates = muTask.Result
            };
        }

        // A match found these ways binds the entry (AuthorMetadata.AniListId); a relaxed
        // (search-box) hit never does, so a later refresh can still improve it. article /
        // substring / ceiling are AniListRanker's fallback tiers (R7 / R5 / R4, OpenTome
        // 2026-09-24): strict rules, so they bind like primary / synonym.
        private static readonly HashSet<string> PinnedVia = new HashSet<string> { "primary", "synonym", "article", "substring", "ceiling", "alias", "catalogue", "id" };

        public AniListSeries ResolveAniList(string name, LibraryType library, string idName = null)
        {
            return ResolveAniList(name, library, null, idName, CatalogueHint(name, library), relaxed: false);
        }

        // The AniList binding (D1-D4). Fail-soft: null keeps the entry unbound for this pass.
        private AniListSeries ResolveAniList(string name, LibraryType library, int? anilistId, string idName, GcdSeries hint, bool relaxed)
        {
            // Bound (D4): the stored id is fetched as-is. A failure keeps the stored data (the
            // caller carries the id forward) and never falls back to the search.
            if (anilistId.HasValue)
            {
                return FetchById(name, library, anilistId.Value, "id");
            }

            // Catalogue (D4): OpenTome's anilist_id when the line carries one — null on every
            // line today; OpenTome's resolve_anilist.py fills it (D8). Unreachable -> the search.
            if (hint?.AnilistId > 0)
            {
                var fromCatalogue = FetchById(name, library, hint.AnilistId.Value, "catalogue");

                if (fromCatalogue != null)
                {
                    return fromCatalogue;
                }
            }

            // Search (D1) bounded by the line's volume count, then the alias retry (D3): the
            // entry's own de-slugged id first — its original name, the most specific term there
            // is — then the line's series_alias rows, which carry Wikipedia-redirect junk
            // ("Re:Zero", "Memory Snow", "List of Re" on the Re:Zero ch.4 line) and so go last.
            // Between them (R6, OpenTome 2026-09-24) the name without a trailing edition
            // qualifier ("Inuyasha (VizBig edition)" -> "Inuyasha"): ranked like an alias (R3
            // keeps the ceiling, no fallback tiers), and it shares MaxAliasSearches with them.
            var aliases = new List<string>();

            if (idName.IsNotNullOrWhiteSpace())
            {
                aliases.Add(idName);
            }

            var editionStripped = AniListRanker.EditionStripped(name);

            if (editionStripped != null)
            {
                aliases.Add(editionStripped);
            }

            if (hint != null)
            {
                aliases.AddRange(_gcdMetadataService.GetAliases(hint.GcdSeriesId) ?? new List<string>());
            }

            return _aniListService.FindSeries(name, library, hint?.VolumeCount > 0 ? hint.VolumeCount : (int?)null, aliases, relaxed, IsLineName(name, hint));
        }

        // The ranker's R4 ceiling and R5 substring tiers read the line's volume count as the
        // searched name's own count, which holds only when the name IS the line's name (or that
        // name without its light-novel qualifier). CatalogueHint also finds a line by one of its
        // aliases (FindSeriesByTitle reads series_alias) or by subtitle (FindSeriesBySubtitle), and
        // then the entry name is another work's or an arc's title and the count is not its own.
        public static bool IsLineName(string name, GcdSeries hint)
        {
            if (hint == null)
            {
                return false;
            }

            var key = TitleMatcher.Normalize(name);

            return key.Length > 0 &&
                   (key == TitleMatcher.Normalize(hint.Name) || key == TitleMatcher.Normalize(StripLightNovelQualifier(hint.Name)));
        }

        // The display fallback's gate. The entry must end the pass with no AniList id from any rung:
        // no stored binding (a stored id that failed to fetch is still a binding), no catalogue
        // anilist_id (a real id wins even when its fetch failed), nothing pinned by the search. The
        // hint must carry a display id. Library rule: a hint found by the line's own name (IsLineName)
        // is allowed as-is; a hint found only by alias or subtitle is allowed only when the line is in
        // the entry's library (a novel-type medium = light novel, anything else incl. null = manga,
        // as GcdMetadataService.Rank reads it).
        internal static bool DisplayFallbackAllowed(string name, LibraryType library, int? storedAniListId, int? pinnedId, GcdSeries hint)
        {
            if (storedAniListId.HasValue || pinnedId.HasValue || hint == null || hint.AnilistId > 0 || !(hint.DisplayAnilistId > 0))
            {
                return false;
            }

            if (IsLineName(name, hint))
            {
                return true;
            }

            var hintLibrary = GcdMetadataService.IsLightNovel(hint.Medium) ? LibraryType.LightNovel : LibraryType.Manga;

            return hintLibrary == library;
        }

        // The display entry by id through AniListService.GetById -- never FetchById, which names the
        // match a binding. Fetched at most once per ProviderLookup: the search box's lookup is cached
        // for ten minutes, so a repeated query does not re-ask AniList (a null answer included); an
        // add/refresh lookup is fresh each pass.
        private AniListSeries DisplayMedia(ProviderLookup lookup, int displayAniListId)
        {
            lock (lookup)
            {
                if (!lookup.DisplayFetched)
                {
                    lookup.Display = _aniListService.GetById(displayAniListId);
                    lookup.DisplayFetched = true;

                    if (lookup.Display == null)
                    {
                        _logger.Debug("Display AniList id {0} did not resolve", displayAniListId);
                    }
                }

                return lookup.Display;
            }
        }

        private AniListSeries FetchById(string name, LibraryType library, int anilistId, string via)
        {
            var ani = _aniListService.GetById(anilistId);

            if (ani == null)
            {
                _logger.Warn("AniList id {0} for \"{1}\" did not resolve; keeping the stored data", anilistId, name);
                return null;
            }

            // The id points at the other medium (a NOVEL for a manga entry, or the reverse): the
            // user or the catalogue chose it — keep it, say so.
            if ((ani.Format == "NOVEL") != (library == LibraryType.LightNovel))
            {
                _logger.Warn("AniList id {0} for \"{1}\" is a {2} entry in the {3} library; keeping it", anilistId, name, ani.Format, library);
            }

            ani.MatchedVia = via;
            _logger.Info("AniList match \"{0}\" -> {1} \"{2}\" ({3}, {4} vols, pop {5}) via {6}",
                name,
                ani.Id,
                ani.EnglishTitle ?? ani.RomajiTitle,
                ani.Format,
                ani.Volumes?.ToString() ?? "?",
                ani.Popularity?.ToString() ?? "?",
                via);

            return ani;
        }

        // The catalogue line named what was asked for: a light novel's raw-name line (the one
        // GetSeries binds first anyway); for manga the raw-name line, else the arc-title line —
        // the two lookups GetSeries falls back to after the AniList-title line, in that order.
        private GcdSeries CatalogueHint(string name, LibraryType library)
        {
            if (!_gcdMetadataService.Available)
            {
                return null;
            }

            var byName = _gcdMetadataService.FindSeriesByTitle(name, library);

            return library == LibraryType.LightNovel
                ? byName
                : byName ?? _gcdMetadataService.FindSeriesBySubtitle(name, library);
        }

        // Search-box cache key. The library changes the query shape (D2), so it is part of the key.
        private static string LookupKey(string name, LibraryType library)
        {
            return library + "|" + (name ?? string.Empty).ToLowerInvariant().Trim();
        }

        public void ForgetLookup(string name)
        {
            _lookupCache.Remove(LookupKey(name, LibraryType.Manga));
            _lookupCache.Remove(LookupKey(name, LibraryType.LightNovel));
        }

        public List<EditionOption> EditionOptions(string tomeLineId, LibraryType library)
        {
            if (tomeLineId.IsNullOrWhiteSpace() || !_gcdMetadataService.Available)
            {
                return new List<EditionOption>();
            }

            return _editionResolver.Options(_gcdMetadataService.FindSeriesByTomeId(tomeLineId), library) ?? new List<EditionOption>();
        }

        private class ProviderLookup
        {
            public AniListSeries AniList { get; set; }
            public MangaDexResult MangaDex { get; set; }
            public MangaUpdatesSeries MangaUpdates { get; set; }

            // The display fallback's AniList entry, fetched on first need (DisplayMedia).
            public bool DisplayFetched { get; set; }
            public AniListSeries Display { get; set; }
        }

        private static AuthorStatusType? MapGcdStatus(string gcdStatus)
        {
            if (gcdStatus.IsNullOrWhiteSpace())
            {
                return null;
            }

            switch (gcdStatus.Trim().ToLowerInvariant())
            {
                case "completed":
                case "complete":
                case "ended":
                    return AuthorStatusType.Ended;
                case "ongoing":
                case "continuing":
                case "releasing":
                    return AuthorStatusType.Continuing;
                case "stalled":
                    return AuthorStatusType.Stalled;
                default:
                    return null;
            }
        }

        // Artifact dates are ISO day-precision or nothing. DateTime.TryParse would happily turn a
        // year-only "2019" into 1 January 2019 — the year-only tell this app has chased at every
        // layer — so only the exact form is accepted; anything coarser is TBA. Stored as local
        // midnight in UTC (2026-09-17): the DB converter has always written the Unspecified value
        // with ToUniversalTime(), so a re-read row is Utc 05:00Z (Chicago); producing that value here
        // makes the stored edition compare equal to the remote — before, every dated volume was
        // "Updated" on every pass.
        internal static DateTime? ParseDate(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return null;
            }

            return DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Local).ToUniversalTime()
                : (DateTime?)null;
        }
    }
}
