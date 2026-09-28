using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MetadataSource.Audible
{
    // Audiobook identity B1 (2026-09-17, D1): the English audiobook products of a series as Audible's
    // public catalogue lists them — asin, title/subtitle, the series field with its sequence (packs
    // carry "1-2"), runtime, release date (future volumes included), narrators, cover. Unauthenticated,
    // read-only, one title search per series at 1 request/s: the endpoint Audiobookshelf's provider
    // uses. Fail-soft: a call Audible did not answer is null and never cached, so a consumer keeps
    // today's behaviour; an answered series is cached 7 days in process and on disk.
    public class AudibleProduct
    {
        public string Asin { get; set; }
        public string Title { get; set; }           // "Sword Art Online 1: Aincrad"
        public string Subtitle { get; set; }        // "Phantom Bullet" or null
        public string SeriesTitle { get; set; }     // the matched series entry's title as Audible spells it ("Solo Leveling Series")
        public string Sequence { get; set; }        // that entry's sequence: "7", "1-2", "3.5"
        public int? RuntimeMinutes { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public List<string> Narrators { get; set; } = new List<string>();
        public string Language { get; set; }
        public string ImageUrl { get; set; }        // product_images["500"] or the largest key present, else null
    }

    public interface IAudibleCatalogService
    {
        // English products of the series named `name` (with or without a parenthetical edition tag, or
        // any alias), by Audible's own series field (normalised, trailing " Series" dropped). Null =
        // Audible did not answer (transport / non-200);
        // an empty list = it answered and has nothing. Cached 7 days per normalised name, in process
        // and on disk (answers only).
        List<AudibleProduct> GetSeries(string name, IEnumerable<string> aliases);
        void ClearCache();
    }

    public static class AudibleSequence
    {
        // Audible writes packs with a plain or en/em dash; anything else in the field is not a number.
        private static readonly Regex SequenceRegex = new Regex(@"^(?<from>\d+(?:\.\d+)?)(?:\s*[-–—]\s*(?<to>\d+(?:\.\d+)?))?$", RegexOptions.Compiled);

        // "7" -> (7, 7); "1-2" -> (1, 2); "3.5" -> (3.5, 3.5); "" / junk -> null
        public static (double From, double To)? Parse(string sequence)
        {
            if (sequence.IsNullOrWhiteSpace())
            {
                return null;
            }

            var match = SequenceRegex.Match(sequence.Trim());

            if (!match.Success)
            {
                return null;
            }

            var from = double.Parse(match.Groups["from"].Value, CultureInfo.InvariantCulture);
            var to = match.Groups["to"].Success ? double.Parse(match.Groups["to"].Value, CultureInfo.InvariantCulture) : from;

            return to < from ? null : (from, to);
        }
    }

    public class AudibleCatalogService : IAudibleCatalogService
    {
        private const string CatalogUrl = "https://api.audible.com/1.0/catalog/products";
        private const string ResponseGroups = "series,product_desc,product_attrs,contributors,media";

        // The answers, on disk: <appdata>/metadata/audible-series-cache.json, one entry per normalised
        // series name with the time it was fetched — the ISBN store's shape (GoogleBooksService). A
        // refresh runs in ~30-series bursts at 1 request/s, so a cache that dies with the process would
        // re-ask Audible for the whole library on every container recreate. Loaded once at first use,
        // written through on every new answer (temp file + move), entries expire after 7 days. Only a
        // definite answer is stored: a failed call never reaches the file or the in-process cache.
        private const string CacheFileName = "audible-series-cache.json";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

        // Audible's series names end in " Series" where ours do not ("Solo Leveling Series"); the copy-in
        // (ImportExistingLightNovelsService) drops the word the same way.
        private static readonly Regex TrailingSeriesRegex = new Regex(@"\s+Series$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IHttpClient _httpClient;
        private readonly ICached<List<AudibleProduct>> _cache;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        private readonly object _storeMutex = new object();
        private Dictionary<string, CacheEntry> _store;

        public class CacheEntry
        {
            public DateTime FetchedAt { get; set; }
            public List<AudibleProduct> Products { get; set; }
        }

        public AudibleCatalogService(IHttpClient httpClient, ICacheManager cacheManager, IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, Logger logger)
        {
            _httpClient = httpClient;
            _cache = cacheManager.GetCache<List<AudibleProduct>>(GetType());
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        // In-process only, as GoogleBooksService.ClearCache: the on-disk answers keep their 7 days.
        public void ClearCache()
        {
            _cache.Clear();
        }

        // The list handed back is a copy (a consumer may sort or trim its own) but its products are the
        // cached instances: callers read them, never mutate them.
        public List<AudibleProduct> GetSeries(string name, IEnumerable<string> aliases)
        {
            if (name.IsNullOrWhiteSpace())
            {
                return new List<AudibleProduct>();
            }

            var key = SeriesKey(name);

            // Find + Set rather than Get(key, fn): Get would cache a failed call's null for 7 days.
            // The lifetime is absolute (fix round 1): a hit is returned without a Set, or the 7 days
            // would slide with every refresh and each call would arm another removal timer.
            var hit = _cache.Find(key);

            if (hit != null)
            {
                return new List<AudibleProduct>(hit);
            }

            // A disk hit is cached in process for what is left of its 7 days, so both layers expire together.
            var stored = FindStored(key);

            if (stored != null)
            {
                var remaining = CacheLifetime - (DateTime.UtcNow - stored.FetchedAt);

                if (remaining > TimeSpan.Zero)
                {
                    _cache.Set(key, stored.Products, remaining);
                }

                return new List<AudibleProduct>(stored.Products);
            }

            // 2026-09-17: the display name may carry an edition tag ("Solo Leveling (Second edition)")
            // Audible does not know; ask the bare name, then up to three aliases while the answer is
            // an empty list. A null (no answer) on the name is returned as before; on an alias, the
            // name's empty answer stands. The bare name's key is in the series-field filter too: the
            // tag is what Audible's series field ("Solo Leveling Series") lacks, so without it the
            // bare-name query's own products would be discarded. StripParentheticals also collapses
            // whitespace and trims edge -–,: (CleanSeries), so "Re:ZERO -Starting Life in Another
            // World-" is asked without its trailing dash — harmless: the title search is fuzzy and
            // the key filter normalises punctuation away.
            var bareName = (MangaVolumeParser.StripParentheticals(name) ?? name).Trim();
            var keys = new HashSet<string>((aliases ?? Array.Empty<string>()).Where(a => a.IsNotNullOrWhiteSpace()).Select(SeriesKey)) { key, SeriesKey(bareName) };

            var queries = new List<string> { bareName };
            queries.AddRange((aliases ?? Array.Empty<string>())
                .Where(a => a.IsNotNullOrWhiteSpace())
                .Select(a => a.Trim())
                .Where(a => SeriesKey(a) != SeriesKey(bareName))
                .DistinctBy(SeriesKey)
                .Take(3));

            List<AudibleProduct> products = null;

            foreach (var query in queries)
            {
                var answer = Fetch(query, keys);

                if (answer == null)
                {
                    if (products == null)
                    {
                        return null;
                    }

                    break;
                }

                products = answer;

                if (products.Any())
                {
                    break;
                }
            }

            Store(key, products);
            _cache.Set(key, products, CacheLifetime);

            return new List<AudibleProduct>(products);
        }

        // One title search for `name` (the bare display name, or one alias — see GetSeries); the keys
        // (display name + every alias) decide which series field counts as ours. Null when Audible did
        // not answer — a transport failure, a non-200 (429/5xx), an empty or non-JSON body — logged at
        // Debug; the consumer decides what to tell the user.
        private List<AudibleProduct> Fetch(string name, HashSet<string> keys)
        {
            try
            {
                var request = new HttpRequestBuilder(CatalogUrl)
                    .AddQueryParam("title", name.Trim())
                    .AddQueryParam("response_groups", ResponseGroups)
                    .AddQueryParam("num_results", 50)
                    .AddQueryParam("products_sort_by", "Relevance")
                    .WithRateLimit(1.0)
                    .Build();

                request.SuppressHttpError = true;
                request.RequestTimeout = TimeSpan.FromSeconds(15);

                var response = _httpClient.Get(request);

                if (response == null || response.StatusCode != HttpStatusCode.OK || response.Content.IsNullOrWhiteSpace())
                {
                    _logger.Debug("Audible returned {0} for '{1}'", response == null ? "nothing" : ((int)response.StatusCode).ToString(), name);
                    return null;
                }

                var products = JObject.Parse(response.Content)["products"] as JArray ?? new JArray();
                var result = new List<AudibleProduct>();

                foreach (var product in products.OfType<JObject>())
                {
                    if (!string.Equals((string)product["language"], "english", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // A product may list several series (an omnibus line and the main one): the first
                    // whose title is ours supplies the sequence; a product in none of them is not ours.
                    var series = (product["series"] as JArray)?.OfType<JObject>().FirstOrDefault(s => keys.Contains(SeriesKey((string)s["title"])));

                    if (series != null)
                    {
                        result.Add(ToProduct(product, series));
                    }
                }

                _logger.Debug("Audible answered '{0}' with {1} product(s), {2} in the series", name, products.Count, result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Audible request failed for '{0}'", name);
                return null;
            }
        }

        // Audible tags some series fields with a parenthetical -- "Rascal Does Not Dream (light novel)"
        // (2026-09-21: every product missed and the empty answer was cached) -- so the key drops
        // parentheticals the same way the bare query name does, then the trailing " Series".
        private static string SeriesKey(string title)
        {
            var bare = MangaVolumeParser.StripParentheticals(title?.Trim() ?? string.Empty) ?? string.Empty;

            return TitleMatcher.Normalize(TrailingSeriesRegex.Replace(bare, string.Empty));
        }

        private static AudibleProduct ToProduct(JObject product, JObject series)
        {
            // Fix wave (2026-09-17, Minor 3): a runtime of 0 (or less) is no runtime — null, so the
            // edition ratchet keeps a stored value instead of adopting the zero.
            var runtime = (int?)product["runtime_length_min"];

            return new AudibleProduct
            {
                Asin = (string)product["asin"],
                Title = (string)product["title"],
                Subtitle = (string)product["subtitle"],
                SeriesTitle = (string)series["title"],
                Sequence = (string)series["sequence"],
                RuntimeMinutes = runtime > 0 ? runtime : null,
                ReleaseDate = ParseDate((string)product["release_date"]),
                Narrators = (product["narrators"] as JArray)?.OfType<JObject>().Select(n => (string)n["name"]).Where(n => n.IsNotNullOrWhiteSpace()).ToList() ?? new List<string>(),
                Language = (string)product["language"],
                ImageUrl = LargestImage(product["product_images"] as JObject)
            };
        }

        // "2021-08-10" as a date, kept UTC so the on-disk round trip (DateTimeZoneHandling.Utc) is a no-op.
        private static DateTime? ParseDate(string value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : (DateTime?)null;
        }

        // product_images is keyed by pixel size; 500 is what Audible serves for every product that has
        // art at all, so it is the stable choice and the largest key the fallback.
        private static string LargestImage(JObject images)
        {
            if (images == null)
            {
                return null;
            }

            var url = (string)images["500"];

            if (url.IsNotNullOrWhiteSpace())
            {
                return url;
            }

            return images.Properties()
                .Where(p => p.Value.Type == JTokenType.String && int.TryParse(p.Name, out _))
                .OrderByDescending(p => int.Parse(p.Name))
                .Select(p => (string)p.Value)
                .FirstOrDefault(u => u.IsNotNullOrWhiteSpace());
        }

        // ---- the on-disk store ----

        private string CachePath => Path.Combine(_appFolderInfo.AppDataFolder, "metadata", CacheFileName);

        // The entry (its FetchedAt sizes the in-process lifetime), or null when absent or expired.
        private CacheEntry FindStored(string key)
        {
            lock (_storeMutex)
            {
                var store = LoadStore();

                if (!store.TryGetValue(key, out var entry))
                {
                    return null;
                }

                if (entry.FetchedAt + CacheLifetime < DateTime.UtcNow)
                {
                    store.Remove(key);
                    return null;
                }

                return entry;
            }
        }

        private void Store(string key, List<AudibleProduct> products)
        {
            lock (_storeMutex)
            {
                var store = LoadStore();
                store[key] = new CacheEntry { FetchedAt = DateTime.UtcNow, Products = products };

                try
                {
                    var path = CachePath;
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
                    // The store is an optimisation: a write failure costs the answer its next process, not this lookup.
                    _logger.Debug(ex, "Audible: could not write the series cache");
                }
            }
        }

        // Read once per process; expired and malformed entries are dropped on load (an empty Products
        // list is a real answer and stays). A file that does not parse is treated as empty.
        private Dictionary<string, CacheEntry> LoadStore()
        {
            if (_store != null)
            {
                return _store;
            }

            var store = new Dictionary<string, CacheEntry>();

            try
            {
                var path = CachePath;

                if (_diskProvider.FileExists(path))
                {
                    var raw = Json.Deserialize<Dictionary<string, CacheEntry>>(_diskProvider.ReadAllText(path)) ?? new Dictionary<string, CacheEntry>();
                    var cutoff = DateTime.UtcNow - CacheLifetime;

                    foreach (var pair in raw.Where(p => p.Value?.Products != null && p.Value.FetchedAt > cutoff))
                    {
                        store[pair.Key] = pair.Value;
                    }

                    _logger.Debug("Audible: loaded {0} series from the cache ({1} expired)", store.Count, raw.Count - store.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Audible: could not read the series cache, starting empty");
            }

            _store = store;
            return _store;
        }
    }
}
