using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Audible
{
    // Audiobook identity B1 (2026-09-17, D1): a canned Audible answers every title search with the
    // trimmed real answer for "Sword Art Online" (Files/Audible/sao-products.json — it also carries a
    // TBATE pack and a German SAO) unless a test swaps the body. The on-disk series cache sits behind a
    // mocked IDiskProvider: nothing is written unless a test captures it, nothing is read unless a test
    // supplies a file. The real CacheManager is in play (TestBase), so a "second process" is NewInstance().
    [TestFixture]
    public class AudibleCatalogServiceFixture : CoreTest<AudibleCatalogService>
    {
        private const string CatalogPath = "/1.0/catalog/products";
        private static readonly string[] NoAliases = Array.Empty<string>();

        private readonly List<string> _requests = new List<string>();
        private readonly List<string> _titles = new List<string>();
        private readonly Dictionary<string, string> _answers = new Dictionary<string, string>();
        private readonly HashSet<string> _unreachableTitles = new HashSet<string>();
        private string _body;
        private HttpStatusCode _status;
        private bool _unreachable;

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            _titles.Clear();
            _answers.Clear();
            _unreachableTitles.Clear();
            _body = ReadAllText("Files/Audible/sao-products.json");
            _status = HttpStatusCode.OK;
            _unreachable = false;

            WithTempAsAppPath();

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(Answer);
        }

        private HttpResponse Answer(HttpRequest request)
        {
            var url = request.Url;
            _requests.Add(url.Path + "?" + Uri.UnescapeDataString(url.Query ?? string.Empty));

            var title = (url.Query ?? string.Empty).Split('&')
                .Where(p => p.StartsWith("title="))
                .Select(p => Uri.UnescapeDataString(p.Substring("title=".Length)))
                .FirstOrDefault();
            _titles.Add(title);

            if (_unreachable || _unreachableTitles.Contains(title))
            {
                throw new WebException("connection refused");
            }

            // A title with its own canned answer gets it; every other title gets the shared body.
            var body = title != null && _answers.TryGetValue(title, out var answer) ? answer : _body;

            return new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, body, _status);
        }

        // The body Audible answers one exact title query with.
        private void GivenAnswer(string title, string body)
        {
            _answers[title] = body;
        }

        // One exact title query fails at the transport (the others still answer).
        private void GivenNoAnswer(string title)
        {
            _unreachableTitles.Add(title);
        }

        // The title query of every request, in order.
        private List<string> RequestedTitles()
        {
            return _titles;
        }

        private static string EmptyProducts()
        {
            return new { products = Array.Empty<object>(), total_results = 0 }.ToJson();
        }

        // Eight singles as Audible lists them, the series field as Audible spells it: it keys to the
        // bare name ("sololeveling"), not to the tagged display name nor to any alias the tests pass.
        private static string SoloLevelingProducts()
        {
            return new
            {
                products = Enumerable.Range(1, 8).Select(i => Product("B0SL" + i, "Solo Leveling, Vol. " + i, "english", ("Solo Leveling Series", i.ToString()))).ToArray(),
                total_results = 8
            }.ToJson();
        }

        private static string SaoProducts()
        {
            return new
            {
                products = new[] { Product("B0SAO1", "Sword Art Online 1: Aincrad", "english", ("Sword Art Online", "1")) },
                total_results = 1
            }.ToJson();
        }

        private string CachePath => System.IO.Path.Combine(TestFolderInfo.AppDataFolder, "metadata", "audible-series-cache.json");

        private void GivenCacheFile(string json)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(CachePath))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.ReadAllText(CachePath))
                  .Returns(json);
        }

        // One stored entry, written the way the service writes it.
        private void GivenOnDiskEntry(string key, DateTime fetchedAt, params string[] asins)
        {
            var store = new Dictionary<string, AudibleCatalogService.CacheEntry>
            {
                [key] = new AudibleCatalogService.CacheEntry
                {
                    FetchedAt = fetchedAt,
                    Products = asins.Select(a => new AudibleProduct { Asin = a, Title = "stored " + a, SeriesTitle = "Sword Art Online", Sequence = "1" }).ToList()
                }
            };
            GivenCacheFile(store.ToJson());
        }

        // A second process: the same disk and HTTP, an empty in-process cache.
        private AudibleCatalogService NewInstance()
        {
            return NewInstance(new CacheManager());
        }

        // The in-process layer as a mock, to see the Set calls (TestBase binds Subject to a real CacheManager).
        private AudibleCatalogService NewInstance(Mock<ICached<List<AudibleProduct>>> cache)
        {
            cache.Setup(c => c.Find(It.IsAny<string>())).Returns((List<AudibleProduct>)null);

            var cacheManager = new Mock<ICacheManager>();
            cacheManager.Setup(c => c.GetCache<List<AudibleProduct>>(typeof(AudibleCatalogService))).Returns(cache.Object);

            return NewInstance(cacheManager.Object);
        }

        private AudibleCatalogService NewInstance(ICacheManager cacheManager)
        {
            return new AudibleCatalogService(Mocker.GetMock<IHttpClient>().Object,
                                             cacheManager,
                                             Mocker.GetMock<IAppFolderInfo>().Object,
                                             Mocker.GetMock<IDiskProvider>().Object,
                                             TestLogger);
        }

        // A product as Audible shapes it; series = (title, sequence) pairs in Audible's order.
        private static object Product(string asin, string title, string language, params (string Title, string Sequence)[] series)
        {
            return new
            {
                asin,
                title,
                subtitle = (string)null,
                series = series.Select(s => new { asin = "S-" + s.Title, title = s.Title, sequence = s.Sequence }).ToArray(),
                runtime_length_min = 600,
                release_date = "2023-05-16",
                narrators = new[] { new { name = "Ryan Colt Levy" } },
                language,
                product_images = new Dictionary<string, string> { ["1024"] = "https://m.media-amazon.com/images/I/big.jpg", ["252"] = "https://m.media-amazon.com/images/I/small.jpg" }
            };
        }

        // ---- the sample answer ----

        [Test]
        public void keys_a_series_field_carrying_a_light_novel_parenthetical_to_the_bare_name()
        {
            // Audible's series field for Yen Audio's Rascal Does Not Dream is "Rascal Does Not Dream
            // (light novel)"; the entry is named without the tag. 2026-09-21: every product was
            // discarded by the key filter and the empty answer cached for a week.
            GivenAnswer("Rascal Does Not Dream", new
            {
                products = new[]
                {
                    Product("B0B1NMS6JS", "Rascal Does Not Dream of Bunny Girl Senpai", "english", ("Rascal Does Not Dream (light novel)", "1")),
                    Product("B0H6KH9744", "Rascal Does Not Dream of a Knapsack Kid", "english", ("Rascal Does Not Dream (light novel)", "16"))
                },
                total_results = 2
            }.ToJson());

            var products = Subject.GetSeries("Rascal Does Not Dream", NoAliases);

            products.Select(p => p.Asin).Should().BeEquivalentTo("B0B1NMS6JS", "B0H6KH9744");
            products.Should().OnlyContain(p => p.SeriesTitle == "Rascal Does Not Dream (light novel)");
        }

        [Test]
        public void parses_products_series_and_sequences()
        {
            var products = Subject.GetSeries("Sword Art Online", NoAliases);

            products.Should().NotBeNull();
            products.Select(p => p.Asin).Should().BeEquivalentTo("B0HFW954VF", "1975337182", "B0B29JDH9K");

            var aincrad = products.Single(p => p.Asin == "1975337182");
            aincrad.Title.Should().Be("Sword Art Online 1: Aincrad");
            aincrad.Subtitle.Should().BeNull();
            aincrad.SeriesTitle.Should().Be("Sword Art Online");
            aincrad.Sequence.Should().Be("1");
            aincrad.RuntimeMinutes.Should().Be(483);
            aincrad.ReleaseDate.Should().Be(new DateTime(2021, 8, 10));
            aincrad.Narrators.Should().Equal("Bryce Papenbrook");
            aincrad.Language.Should().Be("english");
            aincrad.ImageUrl.Should().Be("https://m.media-amazon.com/images/I/51yRFbmzBPL._SL500_.jpg");

            var bullet = products.Single(p => p.Asin == "B0B29JDH9K");
            bullet.Subtitle.Should().Be("Phantom Bullet");
            bullet.Sequence.Should().Be("6");
            bullet.Narrators.Should().Equal("Bryce Papenbrook", "Cherami Leigh");

            // The future volume: no images yet, release date carried.
            var unital = products.Single(p => p.Asin == "B0HFW954VF");
            unital.Subtitle.Should().Be("Unital Ring II");
            unital.Sequence.Should().Be("23");
            unital.ImageUrl.Should().BeNull();
            unital.ReleaseDate.Should().Be(new DateTime(2026, 11, 3));

            // The pack: sequence as Audible spells it.
            var pack = Subject.GetSeries("The Beginning After the End", NoAliases).Single();
            pack.Asin.Should().Be("1774241307");
            pack.Sequence.Should().Be("1-2");
            pack.Narrators.Should().Equal("Travis Baldree");

            _requests.Should().HaveCount(2);
            _requests[0].Should().StartWith(CatalogPath + "?");
            _requests[0].Should().Contain("title=Sword Art Online");
            _requests[0].Should().Contain("response_groups=series,product_desc,product_attrs,contributors,media");
            _requests[0].Should().Contain("num_results=50");
            _requests[0].Should().Contain("products_sort_by=Relevance");
        }

        [Test]
        public void non_english_products_are_dropped()
        {
            var products = Subject.GetSeries("Sword Art Online", NoAliases);

            products.Should().NotContain(p => p.Asin == "B0FAKEDE01");
            products.Should().OnlyContain(p => p.Language == "english");
        }

        [Test]
        public void products_of_another_series_are_dropped()
        {
            Subject.GetSeries("Sword Art Online", NoAliases).Should().NotContain(p => p.Asin == "1774241307");
            _requests.Should().HaveCount(1);

            // A different name is its own cache key: Audible is asked again, and the same canned
            // answer filters to nothing for it.
            var none = Subject.GetSeries("Overlord", NoAliases);

            none.Should().NotBeNull();
            none.Should().BeEmpty();
            _requests.Should().HaveCount(2);
            _requests[1].Should().Contain("title=Overlord");
        }

        [Test]
        public void series_field_matches_with_trailing_series_word_dropped()
        {
            _body = new
            {
                products = new[]
                {
                    Product("B0C1", "Solo Leveling, Vol. 1", "english", ("Solo Leveling Series", "1")),
                    Product("B0C2", "Solo Leveling, Vol. 2", "english", ("Some Omnibus Collection", "9"), ("Solo Leveling Series", "2")),
                    Product("B0C3", "Solo Leveling, Vol. 3 (Korean)", "korean", ("Solo Leveling Series", "3")),
                    Product("B0C4", "Only I Level Up 4", "english", ("Only I Level Up", "4")),
                    Product("B0C5", "Solo Leveling Fan Guide", "english")
                },
                total_results = 5
            }.ToJson();

            var products = Subject.GetSeries("Solo Leveling", NoAliases);

            products.Select(p => p.Asin).Should().Equal("B0C1", "B0C2");
            products[0].SeriesTitle.Should().Be("Solo Leveling Series");
            products[0].Sequence.Should().Be("1");
            products[0].ImageUrl.Should().Be("https://m.media-amazon.com/images/I/big.jpg");

            // Several series on one product: the first that matches the entry supplies the sequence.
            products[1].SeriesTitle.Should().Be("Solo Leveling Series");
            products[1].Sequence.Should().Be("2");

            // An alias widens the series match; the name is still what Audible is asked for.
            var byAlias = Subject.GetSeries("Na Honjaman Level Up", new[] { "Only I Level Up", "Solo Leveling" });

            byAlias.Select(p => p.Asin).Should().Equal("B0C1", "B0C2", "B0C4");
            byAlias[2].SeriesTitle.Should().Be("Only I Level Up");
            _requests.Last().Should().Contain("title=Na Honjaman Level Up");
        }

        // Fix wave (2026-09-17, Minor 3): a runtime of 0 is no runtime — null, so the ratchet keeps a
        // stored value instead of adopting the zero.
        [Test]
        public void a_zero_runtime_is_null()
        {
            _body = new
            {
                products = new[]
                {
                    new
                    {
                        asin = "B0C1",
                        title = "Solo Leveling, Vol. 1",
                        series = new[] { new { asin = "S-1", title = "Solo Leveling", sequence = "1" } },
                        runtime_length_min = 0,
                        language = "english"
                    }
                },
                total_results = 1
            }.ToJson();

            var product = Subject.GetSeries("Solo Leveling", NoAliases).Single();

            product.Asin.Should().Be("B0C1");
            product.RuntimeMinutes.Should().BeNull();
        }

        // ---- the bare-name / alias fallback (2026-09-17) ----

        [Test]
        public void the_bare_name_query_matches_the_series_field_without_the_edition_tag()
        {
            // The one query the fallback adds: its products carry "Solo Leveling Series", which keys to
            // the bare name and to nothing else the caller passes — so that key must be in the filter.
            GivenAnswer("Solo Leveling", SoloLevelingProducts());

            var products = Subject.GetSeries("Solo Leveling (Second edition)", new[] { "Na Honjaman Level Up", "Only I Level Up" });

            products.Should().HaveCount(8);
            products.Should().OnlyContain(p => p.SeriesTitle == "Solo Leveling Series");
            RequestedTitles().Should().Equal("Solo Leveling");
        }

        [Test]
        public void queries_the_bare_name_then_aliases_while_the_answer_is_empty()
        {
            // Solo Leveling (Second edition) -> 0 products; "Solo Leveling" -> the series. 2026-09-17 live: audio 0/8.
            GivenAnswer("Solo Leveling", EmptyProducts());                  // parenthetical stripped, still empty
            GivenAnswer("Na Honjaman Level Up", SoloLevelingProducts());    // an alias answers

            var products = Subject.GetSeries("Solo Leveling (Second edition)", new[] { "Na Honjaman Level Up", "Only I Level Up" });

            products.Should().HaveCount(8);
            RequestedTitles().Should().Equal("Solo Leveling", "Na Honjaman Level Up");   // stops at the first non-empty answer
        }

        [Test]
        public void a_non_empty_first_answer_asks_nothing_else()
        {
            GivenAnswer("Sword Art Online", SaoProducts());
            Subject.GetSeries("Sword Art Online", new[] { "SAO" });
            RequestedTitles().Should().Equal("Sword Art Online");
        }

        [Test]
        public void alias_fallback_is_capped_at_three_and_a_null_answer_stays_null()
        {
            GivenAnswer("X", EmptyProducts());
            GivenAnswer("A1", EmptyProducts());
            GivenAnswer("A2", EmptyProducts());
            GivenAnswer("A3", EmptyProducts());
            Subject.GetSeries("X", new[] { "A1", "A2", "A3", "A4" }).Should().BeEmpty();
            RequestedTitles().Should().Equal("X", "A1", "A2", "A3");

            // A transport failure on the name is null as before: no alias is tried.
            _unreachable = true;
            Subject.GetSeries("Y", new[] { "B1" }).Should().BeNull();
            RequestedTitles().Should().Equal("X", "A1", "A2", "A3", "Y");
        }

        [Test]
        public void a_transport_failure_on_an_alias_keeps_the_names_empty_answer()
        {
            GivenAnswer("Z", EmptyProducts());
            GivenNoAnswer("C1");
            GivenAnswer("C2", EmptyProducts());

            var products = Subject.GetSeries("Z", new[] { "C1", "C2" });

            products.Should().NotBeNull();
            products.Should().BeEmpty();
            RequestedTitles().Should().Equal("Z", "C1");   // the failed alias ends the walk; C2 is not asked

            // The empty answer is a cached answer, as for a name with no aliases.
            Subject.GetSeries("Z", new[] { "C1", "C2" }).Should().BeEmpty();
            RequestedTitles().Should().HaveCount(2);
        }

        // ---- fail-soft ----

        [Test]
        public void a_transport_failure_is_null_and_not_cached()
        {
            _unreachable = true;
            Subject.GetSeries("Sword Art Online", NoAliases).Should().BeNull();
            Subject.GetSeries("Sword Art Online", NoAliases).Should().BeNull();

            _unreachable = false;
            _status = HttpStatusCode.TooManyRequests;
            Subject.GetSeries("Sword Art Online", NoAliases).Should().BeNull();

            _status = HttpStatusCode.OK;
            _body = "<html>not json</html>";
            Subject.GetSeries("Sword Art Online", NoAliases).Should().BeNull();

            _body = ReadAllText("Files/Audible/sao-products.json");
            Subject.GetSeries("Sword Art Online", NoAliases).Should().HaveCount(3);

            _requests.Should().HaveCount(5);
            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        // ---- the caches ----

        [Test]
        public void an_answer_is_cached_in_process()
        {
            Subject.GetSeries("Sword Art Online", NoAliases).Should().HaveCount(3);
            Subject.GetSeries("sword art online", NoAliases).Should().HaveCount(3);
            Subject.GetSeries("Sword Art Online Series", NoAliases).Should().HaveCount(3);

            _requests.Should().HaveCount(1);
        }

        [Test]
        public void an_answer_is_cached_seven_days_on_disk()
        {
            string written = null;
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.WriteAllText(CachePath + ".tmp", It.IsAny<string>()))
                  .Callback<string, string>((path, text) => written = text);

            Subject.GetSeries("Sword Art Online", NoAliases).Should().HaveCount(3);

            written.Should().NotBeNull();
            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(CachePath + ".tmp", CachePath, true), Times.Once());

            GivenCacheFile(written);
            var again = NewInstance().GetSeries("Sword Art Online", NoAliases);

            again.Select(p => p.Asin).Should().BeEquivalentTo("B0HFW954VF", "1975337182", "B0B29JDH9K");
            again.Single(p => p.Asin == "1975337182").ReleaseDate.Should().Be(new DateTime(2021, 8, 10));
            again.Single(p => p.Asin == "1975337182").Narrators.Should().Equal("Bryce Papenbrook");
            _requests.Should().HaveCount(1);
        }

        [Test]
        public void a_fresh_on_disk_entry_is_served_without_http()
        {
            GivenOnDiskEntry("swordartonline", DateTime.UtcNow.AddDays(-6), "STORED");

            Subject.GetSeries("Sword Art Online", NoAliases).Select(p => p.Asin).Should().Equal("STORED");
            _requests.Should().BeEmpty();
        }

        [Test]
        public void an_on_disk_entry_older_than_seven_days_is_fetched_again()
        {
            GivenOnDiskEntry("swordartonline", DateTime.UtcNow.AddDays(-8), "STORED");

            Subject.GetSeries("Sword Art Online", NoAliases).Select(p => p.Asin).Should().NotContain("STORED");
            _requests.Should().HaveCount(1);
        }

        [Test]
        public void an_empty_answer_is_a_cached_answer()
        {
            GivenOnDiskEntry("overlord", DateTime.UtcNow.AddDays(-1));

            var none = Subject.GetSeries("Overlord", NoAliases);

            none.Should().NotBeNull();
            none.Should().BeEmpty();
            _requests.Should().BeEmpty();
        }

        // Fix round 1: the in-process lifetime is absolute — a hit is returned without a Set (else the 7
        // days slide with every refresh and each call arms another removal timer), and a disk hit is
        // cached in process for what is left of its 7 days. Seen through a mocked ICached.
        [Test]
        public void an_in_process_hit_does_not_reset_the_lifetime()
        {
            var cache = new Mock<ICached<List<AudibleProduct>>>();
            var service = NewInstance(cache);

            // A miss: fetched, stored, cached for the full lifetime.
            service.GetSeries("Sword Art Online", NoAliases).Should().HaveCount(3);
            cache.Verify(c => c.Set("swordartonline", It.Is<List<AudibleProduct>>(l => l.Count == 3), It.Is<TimeSpan?>(t => t == TimeSpan.FromDays(7))), Times.Once());

            // A hit: served as cached, no Set, no HTTP, no disk.
            cache.Setup(c => c.Find("swordartonline")).Returns(new List<AudibleProduct> { new AudibleProduct { Asin = "HIT" } });
            service.GetSeries("Sword Art Online", NoAliases).Select(p => p.Asin).Should().Equal("HIT");
            service.GetSeries("sword art online", NoAliases).Select(p => p.Asin).Should().Equal("HIT");

            cache.Verify(c => c.Set(It.IsAny<string>(), It.IsAny<List<AudibleProduct>>(), It.IsAny<TimeSpan?>()), Times.Once());
            _requests.Should().HaveCount(1);
            Mocker.GetMock<IDiskProvider>().Verify(d => d.ReadAllText(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_disk_hit_is_cached_in_process_for_the_remaining_lifetime()
        {
            GivenOnDiskEntry("swordartonline", DateTime.UtcNow.AddDays(-6), "STORED");
            var cache = new Mock<ICached<List<AudibleProduct>>>();
            var service = NewInstance(cache);

            service.GetSeries("Sword Art Online", NoAliases).Select(p => p.Asin).Should().Equal("STORED");

            // Six days into its seven: about one day left, never the full lifetime.
            cache.Verify(c => c.Set("swordartonline", It.Is<List<AudibleProduct>>(l => l.Count == 1 && l[0].Asin == "STORED"), It.Is<TimeSpan?>(t => t > TimeSpan.FromHours(23) && t <= TimeSpan.FromDays(1))), Times.Once());
            _requests.Should().BeEmpty();
        }

        [Test]
        public void clear_cache_keeps_the_on_disk_answer()
        {
            Subject.GetSeries("Sword Art Online", NoAliases).Should().HaveCount(3);
            Subject.ClearCache();
            Subject.GetSeries("Sword Art Online", NoAliases).Should().HaveCount(3);

            _requests.Should().HaveCount(1);
        }

        // ---- AudibleSequence ----

        [TestCase("7", 7, 7)]
        [TestCase("1-2", 1, 2)]
        [TestCase("3.5", 3.5, 3.5)]
        [TestCase("12 ", 12, 12)]
        [TestCase("1–2", 1, 2)]
        [TestCase("3 - 4", 3, 4)]
        public void sequence_parse(string sequence, double from, double to)
        {
            AudibleSequence.Parse(sequence).Should().Be((from, to));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("Book 3")]
        [TestCase("2-1")]
        public void sequence_parse_rejects_junk(string sequence)
        {
            AudibleSequence.Parse(sequence).Should().BeNull();
        }
    }
}
