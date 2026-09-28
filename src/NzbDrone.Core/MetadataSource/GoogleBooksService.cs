using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource
{
    // Phase 3.5c: per-VOLUME metadata. Each manga volume is a published book with its own
    // ISBN, so its real (English) release date and page count live in a book database, not
    // the series databases (AniList/MangaDex). Google Books has authoritative per-volume
    // release dates + ISBN_13; page count is inconsistent on brand-new releases, so we
    // backfill from Open Library by ISBN. Free, optional API key, fail-soft.
    public class VolumeDetails
    {
        public DateTime? ReleaseDate { get; set; }
        public int? PageCount { get; set; }
        public string Isbn13 { get; set; }
        public string CoverUrl { get; set; }    // the largest cover the record declares (large > medium > small > thumbnail); null when absent
        public string CoverSize { get; set; }   // the imageLinks key CoverUrl came from (large | medium | small | thumbnail | smallThumbnail); null when absent
        public string Description { get; set; } // the record's own blurb (GET /volumes/{id}); null when absent. The caller validates it (D2)
        public string Language { get; set; }    // volumeInfo.language ("en", "ja", ...); null when the record carries none
        public string Title { get; set; }           // volumeInfo.title, as Google spells it ("Sword Art Online 2: Aincrad (light novel)")
        public string Subtitle { get; set; }        // volumeInfo.subtitle when Google splits it out ("Unital Ring I"); null otherwise
        public string SeriesBookTitle { get; set; } // volumeInfo.seriesInfo.shortSeriesBookTitle when present; null otherwise
    }

    public interface IGoogleBooksService
    {
        // seriesTitle like "One Piece", volumeNumber like 114. Null = no match or a transient
        // failure (5xx, transport); throws GoogleBooksQuotaException on a 429.
        VolumeDetails LookupVolume(string seriesTitle, int volumeNumber);

        // Preferred Edition (2026-09-24): the title search for a non-English edition -- restricted to that
        // language, reading its volume label ("Tome 5", "Band 5", "第5巻"). Cached apart from English.
        VolumeDetails LookupVolume(string seriesTitle, int volumeNumber, string language);

        // Drops every cached lookup so a re-resolve gets fresh provider answers instead
        // of the 7-day cache re-supplying the same wrong edition. The on-disk ISBN records
        // survive (an ISBN answer is exact; they expire on their own after 30 days).
        void ClearCache();

        // Precise lookup by a known ISBN-13 (the catalogue's) — the Google Books record for THAT
        // edition: its own description and language, the largest cover it declares, page count +
        // publish date (backfilled by Open Library). Since 2026-09-16 it runs for every ISBN volume
        // (D1) and is cached 30 days, in process and on disk. Null = a transient failure (5xx,
        // transport); throws GoogleBooksQuotaException on a 429.
        VolumeDetails LookupByIsbn(string isbn13);

        // D5 x D6 (2026-09-17): the provider rejected this ISBN's record (its title does not name the
        // series). The on-disk copy keeps the 30-day lifetime instead of becoming permanent, so a
        // Google-side correction can arrive; only an accepted complete record is kept forever.
        void MarkIsbnRecordRejected(string isbn13);
    }

    // Google answered 429: the keyed project's daily quota ("Queries per day") is spent and stays
    // spent until the Pacific-midnight reset — no retry helps. The provider counts these and
    // stops calling Google for the rest of the series pass after a few in a row.
    public class GoogleBooksQuotaException : NzbDroneException
    {
        public GoogleBooksQuotaException()
            : base("Google Books answered 429 (daily quota exceeded)")
        {
        }
    }

    public class GoogleBooksService : IGoogleBooksService
    {
        private const string BooksUrl = "https://www.googleapis.com/books/v1/volumes";

        // The /isbn/{isbn}.json endpoint 302-redirects to /books/{olid}.json and our HTTP client
        // does not follow it, so use the read API instead — it returns the data inline with no
        // redirect, and carries both number_of_pages and publish_date.
        private const string OpenLibraryUrl = "https://openlibrary.org/api/books";

        // Box-set / collection titles that an intitle search can return instead of the single volume.
        private static readonly string[] RejectTitleTokens = { "box set", "omnibus", "collection", "3-in-1", "boxed", "deluxe", "book edition" };

        private readonly IHttpClient _httpClient;
        private readonly ICached<VolumeDetails> _cache;
        private readonly Logger _logger;
        private readonly IConfigService _configService;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly string _envApiKey;

        // The ISBN records, on disk: <appdata>/metadata/googlebooks-isbn-cache.json, one entry per
        // ISBN with the time it was fetched. A published volume's record does not change, and the
        // keyed project's 1,000 queries/day cover ~60% of the library — so a cache that dies with
        // the process (every container recreate, every re-resolve) restarts a two-day fill from
        // the alphabetical top. Loaded once at first use, written through on every new answer
        // (temp file + move, so a crash mid-write leaves the previous file), entries expire after
        // 30 days for incomplete records; complete ones (IsComplete) the provider accepted are kept.
        // Only a definite answer is stored: a 429/5xx never reaches the file. The in-process ICached
        // stays the hot layer in front of it.
        private const string IsbnCacheFileName = "googlebooks-isbn-cache.json";
        private static readonly TimeSpan IsbnCacheLifetime = TimeSpan.FromDays(30);

        private readonly object _isbnStoreMutex = new object();
        private Dictionary<string, IsbnCacheEntry> _isbnStore;

        public class IsbnCacheEntry
        {
            public DateTime FetchedAt { get; set; }
            public VolumeDetails Details { get; set; }

            // D5 x D6 (2026-09-17): set by the provider when it rejected the record; never permanent.
            public bool Rejected { get; set; }
        }

        // The Settings -> Metadata key wins when set; the GOOGLE_BOOKS_API_KEY container variable
        // is the fallback. Blank everywhere = anonymous quota (fail-soft, just throttled harder).
        public static string ResolveApiKey(string configKey, string envKey)
        {
            return configKey.IsNotNullOrWhiteSpace() ? configKey : envKey;
        }

        public const string ApiKeyMask = "********";

        // Display form of the stored key: the real key never leaves the server — a set key is
        // presented as the fixed mask, a blank one as empty.
        public static string MaskApiKey(string configKey)
        {
            return configKey.IsNotNullOrWhiteSpace() ? ApiKeyMask : string.Empty;
        }

        // Save form of the incoming key: the mask echoing back unchanged means "keep what's
        // stored" (null = skipped by the config save); a new key or an explicit clear ("")
        // passes through.
        public static string SanitizeIncomingApiKey(string incoming)
        {
            return incoming == ApiKeyMask ? null : incoming;
        }

        // Which key is in effect, for the UI's presence indicator.
        public static string ResolveApiKeySource(string configKey, string envKey)
        {
            if (configKey.IsNotNullOrWhiteSpace())
            {
                return "settings";
            }

            return envKey.IsNotNullOrWhiteSpace() ? "environment" : "none";
        }

        // D2 (2026-09-16): what counts as a usable description. Language "en" when the record says
        // one (absent = unknown, accepted); no Japanese / Chinese / Korean script; not a product
        // listing ("Notebook", "Coloring Book", "Journal", "Note: This is" — the notebooks and
        // colouring books Google files under the series title); at least 40 characters. The reason
        // is the D10 log's text. Pure + static: the provider gates with it, the title search ranks
        // with it.
        public const int MinDescriptionLength = 40;

        private static readonly Regex CjkScript = new Regex(@"\p{IsHiragana}|\p{IsKatakana}|\p{IsCJKUnifiedIdeographs}|\p{IsHangulSyllables}", RegexOptions.Compiled);
        private static readonly Regex ProductNote = new Regex(@"\b(notebook|coloring book|colouring book|journal)\b|note: this is", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsAcceptableDescription(string text, string language, out string reason)
        {
            return IsAcceptableDescription(text, language, "en", out reason);
        }

        // Preferred Edition (2026-09-24, spec §2.2 Descriptions): the language the edition's blurb must be
        // in. CJK script is rejected unless that language writes in it (ja, ko, any zh variant); "journal"
        // rejects an English blurb only. No machine translation.
        public static bool IsAcceptableDescription(string text, string language, string expected, out string reason)
        {
            if (text.IsNullOrWhiteSpace())
            {
                reason = "empty";
                return false;
            }

            var want = expected.IsNullOrWhiteSpace() ? "en" : expected.Trim();

            if (language.IsNotNullOrWhiteSpace() && !language.Trim().Equals(want, StringComparison.OrdinalIgnoreCase))
            {
                reason = "language " + language.Trim();
                return false;
            }

            if (!WritesInCjk(want) && CjkScript.IsMatch(text))
            {
                reason = "cjk";
                return false;
            }

            // "journal" is an ordinary word outside English (a diary, a newspaper): a non-English blurb is
            // exempt from it alone; the listing words and "Note: This is" reject in every language.
            var english = EditionLanguages.IsEnglish(want);
            var note = ProductNote.Matches(text).FirstOrDefault(m => english || !m.Value.Equals("journal", StringComparison.OrdinalIgnoreCase));
            if (note != null)
            {
                reason = "product note '" + note.Value + "'";
                return false;
            }

            if (text.Trim().Length < MinDescriptionLength)
            {
                reason = "short (" + text.Trim().Length + " chars)";
                return false;
            }

            reason = null;
            return true;
        }

        // Preferred Edition (2026-09-24, pre-review fix): the languages whose own script is CJK -- Japanese,
        // Korean and Chinese in every regional form ("zh", "zh-TW", "zh-HK").
        private static readonly HashSet<string> CjkLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ja", "ko", "zh" };

        private static bool WritesInCjk(string code)
        {
            return CjkLanguages.Contains(code.Split('-')[0].Trim());
        }

        // D6 (2026-09-17): a record that already answers everything the provider asks of it — an
        // acceptable English description, a poster-grade cover and its title — does not change,
        // so the on-disk copy never expires once the provider accepted it (IsPermanent). Anything
        // less keeps the 30-day lifetime (a scan may arrive, a blurb may be added). Records written
        // before this build carry no Title and so count as incomplete: fetched once more, then kept.
        // Preferred Edition (2026-09-24, ruling S10): a non-English record's blurb is judged in its own
        // language (a French edition's French record is complete, not re-fetched monthly against the
        // quota); an English or language-less record keeps the English rule exactly.
        public static bool IsComplete(VolumeDetails d)
        {
            return d != null
                && d.Title.IsNotNullOrWhiteSpace()
                && IsAcceptableDescription(d.Description, d.Language, EditionLanguages.IsEnglish(d.Language) ? "en" : d.Language.Trim(), out _)
                && d.CoverUrl.IsNotNullOrWhiteSpace()
                && PosterGradeCoverSizes.Contains(d.CoverSize ?? string.Empty);
        }

        private static readonly HashSet<string> PosterGradeCoverSizes = new HashSet<string> { "small", "medium", "large" };

        // D5 x D6 (2026-09-17): a record the provider rejected keeps the 30-day lifetime — re-fetched
        // monthly, cheaply, so a Google-side correction can arrive; an accepted complete one is permanent.
        private static bool IsPermanent(IsbnCacheEntry entry)
        {
            return IsComplete(entry.Details) && !entry.Rejected;
        }

        public GoogleBooksService(IHttpClient httpClient, ICacheManager cacheManager, IConfigService configService, IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, Logger logger)
        {
            _httpClient = httpClient;
            _cache = cacheManager.GetCache<VolumeDetails>(GetType());
            _configService = configService;
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _logger = logger;

            // Optional personal API key (never committed). The anonymous quota 429s on bulk
            // per-volume lookups (a 24-114 volume series = that many calls); a project key raises
            // the cap. The env var is read once; the config value is read per use so a key entered
            // in the UI takes effect without a restart.
            _envApiKey = Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY");
        }

        private string ApiKey => ResolveApiKey(_configService.GoogleBooksApiKey, _envApiKey);

        public void ClearCache()
        {
            _cache.Clear();
        }

        public VolumeDetails LookupVolume(string seriesTitle, int volumeNumber)
        {
            if (seriesTitle.IsNullOrWhiteSpace() || volumeNumber <= 0)
            {
                return null;
            }

            // Book facts (page count, publish date) don't change — cache so a full-library refresh
            // fetches each volume once instead of re-hitting the exhaustible Google Books quota.
            // A 429 (GoogleBooksQuotaException) passes through uncached to the provider's breaker.
            try
            {
                return _cache.Get($"vol:{seriesTitle}:{volumeNumber}", () => LookupVolumeUncached(seriesTitle, volumeNumber), TimeSpan.FromDays(7));
            }
            catch (TransientLookupException)
            {
                // A 5xx must not poison the 7-day cache with a null that suppresses this volume's
                // metadata for a week. Fail soft now; retry on the next refresh.
                _logger.Debug("GoogleBooks: transient failure for '{0}' v{1}, not caching", seriesTitle, volumeNumber);
                return null;
            }
        }

        public VolumeDetails LookupVolume(string seriesTitle, int volumeNumber, string language)
        {
            if (EditionLanguages.IsEnglish(language))
            {
                return LookupVolume(seriesTitle, volumeNumber);
            }

            if (seriesTitle.IsNullOrWhiteSpace() || volumeNumber <= 0)
            {
                return null;
            }

            var code = language.Trim();

            // Same shape as the English call: a 429 passes through to the provider's breaker; a 5xx is
            // not cached. The key carries the language, so an edition never reads an English answer.
            try
            {
                return _cache.Get($"vol:{code}:{seriesTitle}:{volumeNumber}", () => LookupVolumeUncached(seriesTitle, volumeNumber, code), TimeSpan.FromDays(7));
            }
            catch (TransientLookupException)
            {
                _logger.Debug("GoogleBooks: transient failure for '{0}' v{1} ({2}), not caching", seriesTitle, volumeNumber, code);
                return null;
            }
        }

        private VolumeDetails LookupVolumeUncached(string seriesTitle, int volumeNumber, string language = "en")
        {
            try
            {
                // Viz canonical title form is "<Series>, Vol. <N>" — a precise exact-phrase selector
                // that naturally excludes box sets/omnibus. Preferred Edition (2026-09-24): another
                // language's edition is asked by its own label ("Tome 5"); the loose fallback below still runs.
                var strictQuery = language == "en"
                    ? $"intitle:\"{seriesTitle}, Vol. {volumeNumber}\""
                    : $"intitle:\"{seriesTitle}\" \"{EditionLanguages.VolumeLabel(language, volumeNumber.ToString(CultureInfo.InvariantCulture))}\"";

                var match = QueryBestVolume(strictQuery, volumeNumber, null, language: language);

                // A bulk refresh fires ~one request/sec; under that burst Google Books often hands back
                // only the metadata-only catalog edition (no cover/blurb), while an isolated request gets
                // the rich ebook. When the chosen match lacks art or a usable description, retry
                // restricted to Google eBooks — which reliably carry both — and prefer that edition.
                // Additive: a transient failure or empty ebook result keeps the original match, never loses it.
                // (A 429 is not a blip — GoogleBooksQuotaException is not caught here and ends the lookup.)
                if (match == null || !HasThumbnail(match) || !HasAcceptableBlurb(match, language))
                {
                    try
                    {
                        var ebook = QueryBestVolume(strictQuery, volumeNumber, "ebooks", language: language);
                        if (ebook != null && HasThumbnail(ebook))
                        {
                            match = ebook;
                        }
                    }
                    catch (TransientLookupException)
                    {
                        // A fallback blip must not nullify a good primary match — keep what we have.
                    }
                }

                // Some publishers don't use the "<Series>, Vol. N" title form on Google Books (e.g.
                // "Witch Hat Atelier 10", "<Series>, Volume 10"), so the strict exact phrase finds
                // nothing. Drop the rigid suffix: search the series title plus the bare volume number and
                // let IsRightVolume validate the result back to volume N (it accepts Vol./Volume/#/
                // trailing "0N"). A wider result pool helps the right volume surface for long series.
                if (match == null || !HasThumbnail(match))
                {
                    try
                    {
                        var loose = QueryBestVolume($"intitle:\"{seriesTitle}\" {volumeNumber}", volumeNumber, null, 20, language);
                        if (loose != null && HasThumbnail(loose))
                        {
                            match = loose;
                        }
                    }
                    catch (TransientLookupException)
                    {
                        // Keep the existing match on a fallback blip.
                    }
                }

                // Subtitle-heavy arc titles ("Re:ZERO ..., Chapter 1: A Day in the Capital") exceed
                // Google Books' exact-phrase matching — both full-title queries return nothing even
                // though the volumes exist. Split the query: anchor intitle on the first token, add
                // the distinctive final subtitle as a phrase, and let IsRightVolume validate the
                // volume number as usual.
                if (match == null)
                {
                    var lastColon = seriesTitle.LastIndexOf(':');
                    var subtitle = lastColon > 0 && lastColon < seriesTitle.Length - 1 ? seriesTitle.Substring(lastColon + 1).Trim() : null;
                    var firstToken = seriesTitle.Split(' ')[0];

                    if (subtitle != null && subtitle.Length >= 4 && subtitle.Length < seriesTitle.Length - 4)
                    {
                        try
                        {
                            match = QueryBestVolume($"intitle:\"{firstToken}\" \"{subtitle}\" {volumeNumber}", volumeNumber, null, 20, language);
                        }
                        catch (TransientLookupException)
                        {
                            // Keep the null; the outer handler treats this volume as transient next cycle.
                        }
                    }
                }

                if (match == null)
                {
                    _logger.Debug("GoogleBooks: no match for '{0}' v{1}", seriesTitle, volumeNumber);
                    return null;
                }

                // The list projection's description is another edition's abridged text as often as the
                // record's own (Fairy Tail Vol. 1: 210 chars in the list, 600 by id) and its imageLinks
                // stop at the 128 px thumbnail; the record itself carries both in full (2026-09-16).
                var details = DetailsFrom(FetchVolumeById((string)match["id"]) ?? match, null);

                // Google Books pageCount/date are often missing on new releases — backfill by ISBN.
                BackfillFromOpenLibrary(details);

                _logger.Debug("GoogleBooks '{0}' v{1}: date={2} pages={3} isbn={4} lang={5} desc={6} chars",
                    seriesTitle,
                    volumeNumber,
                    details.ReleaseDate,
                    details.PageCount,
                    details.Isbn13,
                    details.Language,
                    details.Description?.Length ?? 0);
                return details;
            }
            catch (TransientLookupException)
            {
                throw;
            }
            catch (GoogleBooksQuotaException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "GoogleBooks lookup failed for '{0}' v{1}", seriesTitle, volumeNumber);
                return null;
            }
        }

        // Runs one Google Books query (caller supplies the full q expression) and returns the richest
        // IsRightVolume match — the list ITEM (id + volumeInfo), so the caller can fetch the record by
        // id — or null. English records only (D2: a title-search hit must say "en"; langRestrict is a
        // hint, not a guarantee). Throws TransientLookupException on a non-200 so the caller can skip
        // caching a blip. filter is an optional Google Books filter (e.g. "ebooks"); maxResults widens
        // the pool so the right edition surfaces alongside stubs. Preferred Edition (2026-09-24): language
        // is the edition's ("en" everywhere on the English path) -- the restriction, the record filter and
        // the blurb gate all follow it.
        private JToken QueryBestVolume(string queryString, int volumeNumber, string filter, int maxResults = 10, string language = "en")
        {
            var builder = new HttpRequestBuilder(BooksUrl)
                .AddQueryParam("q", queryString)
                .AddQueryParam("country", "US")   // mandatory — omitting it returns HTTP 403
                .AddQueryParam("langRestrict", language) // dates must be the EDITION's adaptation's, never another market's
                .AddQueryParam("maxResults", maxResults.ToString())
                .WithRateLimit(1.0);

            if (filter.IsNotNullOrWhiteSpace())
            {
                builder.AddQueryParam("filter", filter);
            }

            if (ApiKey.IsNotNullOrWhiteSpace())
            {
                builder.AddQueryParam("key", ApiKey);
            }

            var request = builder.Build();
            request.SuppressHttpError = true;
            request.RequestTimeout = TimeSpan.FromSeconds(15);

            // Non-200 after one retry (429 rate-limit, 5xx, ...) — TransientLookupException signals
            // "do not cache" so a blip doesn't suppress this volume for the whole 7-day cache lifetime.
            var response = GetWithOneRetry(request);

            if (response.Content == null)
            {
                return null;
            }

            var items = JObject.Parse(response.Content)["items"] as JArray;
            if (items == null || items.Count == 0)
            {
                return null;
            }

            var rightVolumes = items
                .Where(x => x["volumeInfo"] != null &&
                    IsRightVolume($"{(string)x["volumeInfo"]["title"]} {(string)x["volumeInfo"]["subtitle"]}", volumeNumber, language) &&
                    IsLanguage(x, language))
                .ToList();

            // Google Books returns BOTH a metadata-only catalog stub (no cover/blurb) and the real
            // ebook edition, in unstable order. Prefer the richest: art + a blurb that passes D2 (a
            // "Notebook" listing with art ranks below a real edition with art), then art, then any match.
            return rightVolumes.FirstOrDefault(x => HasThumbnail(x) && HasAcceptableBlurb(x, language))
                ?? rightVolumes.FirstOrDefault(HasThumbnail)
                ?? rightVolumes.FirstOrDefault();
        }

        // The asked language ("en" on the English path), or no language field at all (unknown, accepted —
        // the same reading the description validator gives a null language; the langRestrict hint already
        // filtered the pool).
        private static bool IsLanguage(JToken item, string language)
        {
            var itemLanguage = (string)item["volumeInfo"]?["language"];

            return itemLanguage.IsNullOrWhiteSpace() || itemLanguage.Trim().Equals(language, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasThumbnail(JToken item)
        {
            return item["volumeInfo"]?["imageLinks"]?["thumbnail"] != null;
        }

        private static bool HasAcceptableBlurb(JToken item, string language = "en")
        {
            var volumeInfo = item["volumeInfo"];

            return volumeInfo != null && IsAcceptableDescription((string)volumeInfo["description"], (string)volumeInfo["language"], language, out _);
        }

        public VolumeDetails LookupByIsbn(string isbn13)
        {
            if (isbn13.IsNullOrWhiteSpace())
            {
                return null;
            }

            try
            {
                // A published volume's record does not change: 30 days in process (the title search
                // stays at 7 — its answer is a ranking that can improve as Google adds editions),
                // behind it the on-disk store, and only then Google. A 429 propagates uncached.
                return _cache.Get($"isbn:{isbn13}", () => FindStoredIsbnRecord(isbn13) ?? StoreIsbnRecord(isbn13, LookupByIsbnUncached(isbn13)), IsbnCacheLifetime);
            }
            catch (TransientLookupException)
            {
                _logger.Debug("GoogleBooks: transient failure for isbn {0}, not caching", isbn13);
                return null;
            }
        }

        public void MarkIsbnRecordRejected(string isbn13)
        {
            lock (_isbnStoreMutex)
            {
                // Every series pass re-rejects the same record from cache: only the first mark
                // rewrites the file. An ISBN the store never took (a failed lookup) is nothing to mark.
                var store = LoadIsbnStore();

                if (!store.TryGetValue(isbn13, out var entry) || entry.Rejected)
                {
                    return;
                }

                entry.Rejected = true;
                WriteIsbnStore(store);
            }
        }

        // ---- the on-disk ISBN store ----

        private string IsbnCachePath => Path.Combine(_appFolderInfo.AppDataFolder, "metadata", IsbnCacheFileName);

        private VolumeDetails FindStoredIsbnRecord(string isbn13)
        {
            lock (_isbnStoreMutex)
            {
                var store = LoadIsbnStore();

                if (!store.TryGetValue(isbn13, out var entry))
                {
                    return null;
                }

                // D6: a record written before this build carries no Title and is re-fetched once,
                // whatever its age — a 429 on that re-fetch is a miss for the pass, the ratchet keeps everything.
                if (entry.Details == null || entry.Details.Title.IsNullOrWhiteSpace() || (!IsPermanent(entry) && entry.FetchedAt + IsbnCacheLifetime < DateTime.UtcNow))
                {
                    store.Remove(isbn13);
                    return null;
                }

                return entry.Details;
            }
        }

        // Null (a failed lookup) is handed back untouched and never written.
        private VolumeDetails StoreIsbnRecord(string isbn13, VolumeDetails details)
        {
            if (details == null)
            {
                return null;
            }

            lock (_isbnStoreMutex)
            {
                var store = LoadIsbnStore();
                store[isbn13] = new IsbnCacheEntry { FetchedAt = DateTime.UtcNow, Details = details };
                WriteIsbnStore(store);
            }

            return details;
        }

        // Caller holds _isbnStoreMutex.
        private void WriteIsbnStore(Dictionary<string, IsbnCacheEntry> store)
        {
            try
            {
                var path = IsbnCachePath;
                var dir = Path.GetDirectoryName(path);

                if (!_diskProvider.FolderExists(dir))
                {
                    _diskProvider.CreateFolder(dir);
                }

                var temp = path + ".tmp";
                _diskProvider.WriteAllText(temp, store.ToJson(Formatting.None));
                _diskProvider.MoveFile(temp, path, true);
            }
            catch (Exception ex)
            {
                // The store is an optimisation: a write failure costs the record its next
                // process, not this lookup.
                _logger.Debug(ex, "GoogleBooks: could not write the ISBN cache");
            }
        }

        // Read once per process; expired and malformed entries are dropped on load. A file that
        // does not parse is treated as empty (and overwritten by the next answer).
        private Dictionary<string, IsbnCacheEntry> LoadIsbnStore()
        {
            if (_isbnStore != null)
            {
                return _isbnStore;
            }

            var store = new Dictionary<string, IsbnCacheEntry>();

            try
            {
                var path = IsbnCachePath;

                if (_diskProvider.FileExists(path))
                {
                    var raw = Json.Deserialize<Dictionary<string, IsbnCacheEntry>>(_diskProvider.ReadAllText(path)) ?? new Dictionary<string, IsbnCacheEntry>();
                    var cutoff = DateTime.UtcNow - IsbnCacheLifetime;

                    // D6: a Title-less (pre-build) record is expired whatever its age — see FindStoredIsbnRecord.
                    foreach (var pair in raw.Where(p => p.Value?.Details != null && p.Value.Details.Title.IsNotNullOrWhiteSpace() && (IsPermanent(p.Value) || p.Value.FetchedAt > cutoff)))
                    {
                        store[pair.Key] = pair.Value;
                    }

                    _logger.Debug("GoogleBooks: loaded {0} ISBN records from the cache ({1} expired)", store.Count, raw.Count - store.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "GoogleBooks: could not read the ISBN cache, starting empty");
            }

            _isbnStore = store;
            return _isbnStore;
        }

        private VolumeDetails LookupByIsbnUncached(string isbn13)
        {
            try
            {
                var builder = new HttpRequestBuilder(BooksUrl)
                    .AddQueryParam("q", $"isbn:{isbn13}")
                    .AddQueryParam("country", "US")
                    .AddQueryParam("maxResults", "1")
                    .WithRateLimit(1.0);

                if (ApiKey.IsNotNullOrWhiteSpace())
                {
                    builder.AddQueryParam("key", ApiKey);
                }

                var request = builder.Build();
                request.SuppressHttpError = true;
                request.RequestTimeout = TimeSpan.FromSeconds(15);

                // Non-200 after one retry (429/5xx) — don't cache a partial/empty result for 30 days.
                var response = GetWithOneRetry(request);

                VolumeDetails details = null;

                if (response.Content != null)
                {
                    var item = (JObject.Parse(response.Content)["items"] as JArray)?
                        .FirstOrDefault(x => x["volumeInfo"] != null);

                    if (item != null)
                    {
                        // The record itself (see LookupVolumeUncached): the list projection's blurb and
                        // 128 px thumbnail are not what this ISBN's page shows.
                        details = DetailsFrom(FetchVolumeById((string)item["id"]) ?? item, isbn13);
                    }
                }

                details ??= new VolumeDetails { Isbn13 = isbn13 };
                BackfillFromOpenLibrary(details);

                _logger.Debug("GoogleBooks isbn:{0}: date={1} pages={2} lang={3} desc={4} chars cover={5}",
                    isbn13,
                    details.ReleaseDate,
                    details.PageCount,
                    details.Language,
                    details.Description?.Length ?? 0,
                    details.CoverUrl != null);
                return details;
            }
            catch (TransientLookupException)
            {
                throw;
            }
            catch (GoogleBooksQuotaException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "GoogleBooks ISBN lookup failed for {0}", isbn13);
                return null;
            }
        }

        // GET /volumes/{id}: the record itself — full description, language, every imageLinks size the
        // scan has. Null when there is no id or the answer carries no volumeInfo (the caller keeps the
        // list item); throws TransientLookupException on a non-200 so a rate-limit blip is retried,
        // not cached.
        private JToken FetchVolumeById(string volumeId)
        {
            if (volumeId.IsNullOrWhiteSpace())
            {
                return null;
            }

            var builder = new HttpRequestBuilder(BooksUrl + "/{id}")
                .SetSegment("id", volumeId)
                .AddQueryParam("country", "US")
                .WithRateLimit(1.0);

            if (ApiKey.IsNotNullOrWhiteSpace())
            {
                builder.AddQueryParam("key", ApiKey);
            }

            var request = builder.Build();
            request.SuppressHttpError = true;
            request.RequestTimeout = TimeSpan.FromSeconds(15);

            var response = GetWithOneRetry(request);

            if (response.Content == null)
            {
                return null;
            }

            var record = JObject.Parse(response.Content);
            return record["volumeInfo"] == null ? null : record;
        }

        // One record (list item or GET /volumes/{id} body) -> VolumeDetails. knownIsbn13 is the ISBN the
        // caller looked up (kept as-is, as before); the title search extracts it from the record.
        // Google's blurbs carry raw <br> line breaks (no slash); the page strips tags but only turns
        // "<br/>" into a space, so a bare "<br>" would glue the words on either side together.
        private static readonly Regex LineBreakTag = new Regex(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static string NormalizeLineBreaks(string text)
        {
            return text == null ? null : LineBreakTag.Replace(text, " ");
        }

        private static VolumeDetails DetailsFrom(JToken item, string knownIsbn13)
        {
            var volumeInfo = item["volumeInfo"] ?? new JObject();
            var coverUrl = ExtractCover(volumeInfo["imageLinks"], out var coverSize);

            return new VolumeDetails
            {
                ReleaseDate = ParseDate((string)volumeInfo["publishedDate"]),
                PageCount = (int?)volumeInfo["pageCount"],
                Isbn13 = knownIsbn13 ?? ExtractIsbn13(volumeInfo["industryIdentifiers"] as JArray),
                CoverUrl = coverUrl,
                CoverSize = coverSize,
                Description = NormalizeLineBreaks((string)volumeInfo["description"]),
                Language = (string)volumeInfo["language"],
                Title = (string)volumeInfo["title"],
                Subtitle = (string)volumeInfo["subtitle"],
                SeriesBookTitle = (string)volumeInfo["seriesInfo"]?["shortSeriesBookTitle"]
            };
        }

        // Fill a missing page count and/or release date from Open Library by ISBN.
        private void BackfillFromOpenLibrary(VolumeDetails details)
        {
            if (details == null ||
                details.Isbn13.IsNullOrWhiteSpace() ||
                ((details.PageCount ?? 0) > 0 && details.ReleaseDate != null))
            {
                return;
            }

            var ol = OpenLibraryByIsbn(details.Isbn13);
            if (ol == null)
            {
                return;
            }

            if ((details.PageCount ?? 0) <= 0)
            {
                details.PageCount = ol.PageCount;
            }

            if (details.ReleaseDate == null)
            {
                details.ReleaseDate = ol.ReleaseDate;
            }
        }

        private static bool IsRightVolume(string title, int volumeNumber, string language = "en")
        {
            if (title.IsNullOrWhiteSpace())
            {
                return false;
            }

            var lower = title.ToLowerInvariant();

            if (RejectTitleTokens.Any(t => lower.Contains(t)))
            {
                return false;
            }

            // Positively identify volume N in a recognized form (avoid "Vol. 11" matching "Vol. 1").
            // A result whose title carries no volume marker (a bare series title) is rejected: trusting
            // search ranking there would risk attaching the wrong volume's page/date.
            var n = volumeNumber;
            if (Regex.IsMatch(lower, $@"\bvol\.?\s*0*{n}\b") ||      // "Vol. N" / "vol N"
                Regex.IsMatch(lower, $@"\bvolume\s*0*{n}\b") ||      // "Volume N"
                Regex.IsMatch(lower, $@"#\s*0*{n}\b") ||             // "#N"
                Regex.IsMatch(lower, $@"(^|\s)0*{n}\s*$"))           // trailing "01"/"12" (e.g. "Fire Force 01")
            {
                return true;
            }

            // Preferred Edition (2026-09-24): an edition's own volume label.
            if (language == "fr" && Regex.IsMatch(lower, $@"\b(?:tome|t)\.?\s*0*{n}\b"))
            {
                return true;
            }

            if (language == "de" && Regex.IsMatch(lower, $@"\b(?:band|bd)\.?\s*0*{n}\b"))
            {
                return true;
            }

            if (language == "ja" && Regex.IsMatch(lower, $@"第\s*0*{n}\s*巻|(?<!\d)0*{n}\s*巻"))
            {
                return true;
            }

            return false;
        }

        private static DateTime? ParseDate(string raw)
        {
            if (raw.IsNullOrWhiteSpace())
            {
                return null;
            }

            // Google Books dates are "yyyy-MM-dd" or "yyyy-MM" or "yyyy". Month- and
            // year-precision strings would parse to a fabricated day-1 date that every
            // plausibility guard downstream treats as day-precise (96 library volumes
            // carried fake first-of-month dates) — only accept full dates; partial
            // precision stays null (honest TBA, re-resolvable).
            foreach (var fmt in new[] { "yyyy-MM-dd" })
            {
                if (DateTime.TryParseExact(raw, fmt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
                {
                    return d;
                }
            }

            return null;
        }

        // Open Library publish_date is free-form ("September 7, 2021", "Sep 2021", "2021", ...).
        private static DateTime? ParseOpenLibraryDate(string raw)
        {
            if (raw.IsNullOrWhiteSpace())
            {
                return null;
            }

            // Require an explicit 4-digit year. Without one, DateTime.TryParse fills in the CURRENT
            // year, which can fabricate a future date and wrongly flag the volume "Not Available"
            // (suppressing it from search). No usable year -> leave the date null.
            if (!System.Text.RegularExpressions.Regex.IsMatch(raw, @"\b\d{4}\b"))
            {
                return null;
            }

            var iso = ParseDate(raw);
            if (iso != null)
            {
                return iso;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
            {
                return d;
            }

            return null;
        }

        private static string ExtractIsbn13(JArray identifiers)
        {
            if (identifiers == null)
            {
                return null;
            }

            return identifiers
                .FirstOrDefault(i => (string)i["type"] == "ISBN_13")?["identifier"]?.ToString();
        }

        // The largest cover the record declares: a scanned record lists small/medium/large/extraLarge
        // (GET /volumes/{id}; the list projection stops at thumbnail), a catalogue-only record only the
        // 128 px thumbnail/smallThumbnail. Never a synthesized zoom: for a record without a scan Google
        // answers zoom=2..6 with a grey "image not available" PNG (200 OK), and extraLarge is the raw
        // multi-MB scan — large (~800×1200) is the ceiling. Normalized to https with no page-curl
        // overlay (everything from &edge=curl on is dropped; the bare URL serves the same image).
        // size reports which key won, so the caller can tell a scan from a 128 px thumbnail (the
        // series poster wants the former; a volume row takes either).
        private static readonly string[] CoverSizePreference = { "large", "medium", "small", "thumbnail", "smallThumbnail" };

        private static string ExtractCover(JToken imageLinks, out string size)
        {
            size = null;

            if (imageLinks == null)
            {
                return null;
            }

            size = CoverSizePreference.FirstOrDefault(s => ((string)imageLinks[s]).IsNotNullOrWhiteSpace());

            if (size == null)
            {
                return null;
            }

            var url = ((string)imageLinks[size]).Replace("http://", "https://");

            var idx = url.IndexOf("&edge=curl", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? url.Substring(0, idx) : url;
        }

        private VolumeDetails OpenLibraryByIsbn(string isbn13)
        {
            try
            {
                var request = new HttpRequestBuilder(OpenLibraryUrl)
                    .AddQueryParam("bibkeys", $"ISBN:{isbn13}")
                    .AddQueryParam("jscmd", "data")
                    .AddQueryParam("format", "json")
                    .SetHeader("Accept", "application/json")
                    .WithRateLimit(1.0)
                    .Build();
                request.SuppressHttpError = true;
                request.RequestTimeout = TimeSpan.FromSeconds(15);

                var response = _httpClient.Get(request);
                if (response?.Content == null || !response.Content.TrimStart().StartsWith("{"))
                {
                    return null;
                }

                var entry = JObject.Parse(response.Content)[$"ISBN:{isbn13}"];
                if (entry == null)
                {
                    return null;
                }

                var pages = (int?)entry["number_of_pages"];

                return new VolumeDetails
                {
                    Isbn13 = isbn13,
                    PageCount = pages > 0 ? pages : null,
                    ReleaseDate = ParseOpenLibraryDate((string)entry["publish_date"])
                };
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "OpenLibrary backfill failed for isbn {0}", isbn13);
                return null;
            }
        }

        // Signals a transient HTTP failure (5xx, transport) so the cache layer skips storing the
        // resulting null/partial — otherwise one blip suppresses a volume's metadata for 7 days.
        // One retry on a 5xx after a short pause: Google's Books backend answers 503
        // "backendFailed" intermittently (about one call in five during the 2026-09-16 staging
        // pass), and a single blip must not cost a volume its whole refresh. A 429 is the daily
        // quota (the live one-shot: "rateLimitExceeded 'Queries per day'"), which no 2 s pause
        // recovers — it is GoogleBooksQuotaException at once, no retry. Anything else still
        // failing is a TransientLookupException — the caller nulls the lookup and caches nothing.
        private HttpResponse GetWithOneRetry(HttpRequest request)
        {
            var response = _httpClient.Get(request);

            if (response != null && !response.HasHttpError)
            {
                return response;
            }

            var status = response == null ? 0 : (int)response.StatusCode;
            if (status == 429)
            {
                _logger.Debug("GoogleBooks: 429 from {0}; daily quota exceeded, not retrying", request.Url.Path);
                throw new GoogleBooksQuotaException();
            }

            if (status >= 500)
            {
                _logger.Debug("GoogleBooks: {0} from {1}; retrying once", status, request.Url.Path);
                System.Threading.Thread.Sleep(RetryDelay);
                response = _httpClient.Get(request);
            }

            if (response == null || response.HasHttpError)
            {
                if (response != null && (int)response.StatusCode == 429)
                {
                    throw new GoogleBooksQuotaException();
                }

                throw new TransientLookupException();
            }

            return response;
        }

        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

        private sealed class TransientLookupException : Exception
        {
        }
    }
}
