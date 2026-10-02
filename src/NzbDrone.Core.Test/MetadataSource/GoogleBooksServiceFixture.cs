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
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // D1/D2 (2026-09-16): the ISBN lookup fetches the record itself (GET /volumes/{id}) — its own full
    // description, language and the largest cover it declares — and every description is validated.
    // A canned Google Books answers the list query, the record query and Open Library. The real
    // CacheManager is in play (TestBase), so a test that needs two different answers uses two ISBNs.
    // The on-disk ISBN cache sits behind a mocked IDiskProvider: nothing is written unless a test
    // captures it, and nothing is read unless a test supplies a file.
    [TestFixture]
    public class GoogleBooksServiceFixture : CoreTest<GoogleBooksService>
    {
        private const string Blurb = "Created by manga-ka Hiro Mashima of Rave Master fame, FAIRY TAIL takes place in a unique magical world. Seventeen-year-old Lucy, mage-in-training, seeks to join a magicians guild.";
        private const string Abridged = "Lucy seeks to join a magicians guild and finds herself teaming up with Natsu, a crazy fire wizard.";
        private const string ListPath = "/books/v1/volumes";
        private const string RecordPath = "/books/v1/volumes/";

        private readonly Dictionary<string, object[]> _lists = new Dictionary<string, object[]>(StringComparer.Ordinal);   // q -> items
        private readonly Dictionary<string, object> _records = new Dictionary<string, object>(StringComparer.Ordinal);    // id -> record
        private readonly List<string> _requests = new List<string>();
        private HttpStatusCode _recordStatus = HttpStatusCode.OK;
        private HttpStatusCode _listStatus = HttpStatusCode.OK;
        private int _recordFailuresLeft = -1;   // -1: every record fetch answers _recordStatus; n: only the first n do, then OK

        [SetUp]
        public void Setup()
        {
            _lists.Clear();
            _records.Clear();
            _requests.Clear();
            _recordStatus = HttpStatusCode.OK;
            _listStatus = HttpStatusCode.OK;
            _recordFailuresLeft = -1;

            WithTempAsAppPath();

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(Answer);
        }

        private string CachePath => System.IO.Path.Combine(TestFolderInfo.AppDataFolder, "metadata", "googlebooks-isbn-cache.json");

        private void GivenCacheFile(string json)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(CachePath))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.ReadAllText(CachePath))
                  .Returns(json);
        }

        // A second process: the same disk and HTTP, an empty in-process cache.
        private GoogleBooksService NewInstance()
        {
            return new GoogleBooksService(Mocker.GetMock<IHttpClient>().Object,
                                          new CacheManager(),
                                          Mocker.GetMock<IConfigService>().Object,
                                          Mocker.GetMock<IAppFolderInfo>().Object,
                                          Mocker.GetMock<IDiskProvider>().Object,
                                          TestLogger);
        }

        // ---- the canned Google Books ----

        // A list item or a record: the same shape. sizes = the imageLinks keys the record declares;
        // subtitle / seriesBookTitle = volumeInfo.subtitle and seriesInfo.shortSeriesBookTitle when the
        // record carries them (null = the key is absent, as ToJson drops nulls).
        private static object Item(string id, string title, string language, string description, string[] sizes, int? pages = 192, string date = "2008-03-25", string isbn13 = null, string subtitle = null, string seriesBookTitle = null)
        {
            var links = new Dictionary<string, string>();

            foreach (var size in sizes ?? new string[0])
            {
                var zoom = size == "thumbnail" ? 1 : size == "smallThumbnail" ? 5 : size == "small" ? 2 : size == "medium" ? 3 : size == "large" ? 4 : 6;
                links[size] = $"http://books.google.com/books/content?id={id}&printsec=frontcover&img=1&zoom={zoom}&edge=curl&source=gbs_api";
            }

            return new
            {
                id,
                volumeInfo = new
                {
                    title,
                    subtitle,
                    seriesInfo = seriesBookTitle == null ? null : new { shortSeriesBookTitle = seriesBookTitle },
                    publishedDate = date,
                    description,
                    pageCount = pages,
                    language,
                    industryIdentifiers = isbn13 == null ? null : new[] { new { type = "ISBN_13", identifier = isbn13 } },
                    imageLinks = links.Count == 0 ? null : links
                }
            };
        }

        private static readonly string[] Thumbnails = { "smallThumbnail", "thumbnail" };
        private static readonly string[] Scanned = { "smallThumbnail", "thumbnail", "small", "medium", "large", "extraLarge" };

        private HttpResponse Answer(HttpRequest request)
        {
            var url = request.Url;
            var query = Uri.UnescapeDataString(url.Query ?? string.Empty);
            _requests.Add(url.Path + "?" + query);

            if (url.Host == "openlibrary.org")
            {
                return Reply(request, new { });
            }

            if (url.Path.StartsWith(RecordPath))
            {
                if (_recordStatus != HttpStatusCode.OK && (_recordFailuresLeft < 0 || _recordFailuresLeft-- > 0))
                {
                    return new HttpResponse(request, new HttpHeader(), string.Empty, _recordStatus);
                }

                return Reply(request, _records.TryGetValue(url.Path.Substring(RecordPath.Length), out var record) ? record : new { });
            }

            if (_listStatus != HttpStatusCode.OK)
            {
                return new HttpResponse(request, new HttpHeader(), string.Empty, _listStatus);
            }

            var q = query.Split('&').Select(p => p.Split(new[] { '=' }, 2)).First(p => p[0] == "q")[1];
            return Reply(request, new { items = _lists.TryGetValue(q, out var items) ? items : null });
        }

        private static HttpResponse Reply(HttpRequest request, object body)
        {
            return new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, body.ToJson());
        }

        private int Requests(string pathPrefix)
        {
            return _requests.Count(r => r.StartsWith(pathPrefix));
        }

        // The list answer (one item) and the record itself, both carrying the title fields as Google
        // spells them for that ISBN.
        private void GivenIsbnRecord(string isbn, string title, string subtitle = null, string seriesBookTitle = null)
        {
            var id = "rec-" + isbn;
            _lists["isbn:" + isbn] = new[] { Item(id, title, "en", Abridged, Thumbnails, subtitle: subtitle, seriesBookTitle: seriesBookTitle) };
            _records[id] = Item(id, title, "en", Blurb, Scanned, 192, "2008-03-25", isbn, subtitle, seriesBookTitle);
        }

        // One stored entry, written the way StoreIsbnRecord writes it. coverSize null = no cover at all;
        // title null = a record written before the title fields existed; rejected = the provider
        // marked it (D5 x D6, 2026-09-17).
        private void GivenOnDiskRecord(string isbn, DateTime fetchedAt, string title, string coverSize, string description, string language, bool rejected = false)
        {
            var store = new Dictionary<string, GoogleBooksService.IsbnCacheEntry>
            {
                [isbn] = new GoogleBooksService.IsbnCacheEntry
                {
                    FetchedAt = fetchedAt,
                    Rejected = rejected,
                    Details = new VolumeDetails
                    {
                        Isbn13 = isbn,
                        Title = title,
                        CoverSize = coverSize,
                        CoverUrl = coverSize == null ? null : $"https://books.google.com/books/content?id=rec-{isbn}&printsec=frontcover&img=1&zoom=3",
                        Description = description,
                        Language = language
                    }
                }
            };
            GivenCacheFile(store.ToJson());
        }

        // ---- D2 ----

        [TestCase("The three notebooks in Eren's basement hold the truth about the Titans and the world beyond the walls.", "en", true, null)]
        [TestCase("The three notebooks in Eren's basement hold the truth about the Titans and the world beyond the walls.", null, true, null)]
        [TestCase("The three notebooks in Eren's basement hold the truth about the Titans and the world beyond the walls.", "ja", false, "language ja")]
        [TestCase("TV・CMなどで活躍中の人気アイドル木村好珠ちゃんと木嶋ゆりちゃんとのコラボ写真集。", "en", false, "cjk")]
        [TestCase("**************Note: This is Notebook Not Story or Manga Volume. Lined pages for your own ideas.", "en", false, "product note 'Note: This is'")]
        [TestCase("Fire Force Notebook Cover Arts: 120 lined pages, 6 x 9 inches, matte softcover finish.", "en", false, "product note 'Notebook'")]
        [TestCase("Black Clover Coloring Book: relax with 50 original illustrations of Asta and the Black Bulls.", "en", false, "product note 'Coloring Book'")]
        [TestCase("Too short to be a blurb.", "en", false, "short (24 chars)")]
        [TestCase("", "en", false, "empty")]
        [TestCase(null, "en", false, "empty")]
        public void description_validation(string text, string language, bool accepted, string expectedReason)
        {
            GoogleBooksService.IsAcceptableDescription(text, language, out var reason).Should().Be(accepted);
            reason.Should().Be(expectedReason);
        }

        // Preferred Edition (2026-09-24, spec §2.2 Descriptions): the gate takes the EXPECTED language --
        // a French blurb is fine for a French edition, a Japanese one (CJK) for a Japanese edition.
        [TestCase("Kafka Hibino rêve d'intégrer les Forces de défense, mais une étrange créature change tout.", "fr", "fr", true)]
        [TestCase("Kafka Hibino rêve d'intégrer les Forces de défense, mais une étrange créature change tout.", "fr", "en", false)]
        [TestCase("日比野カフカは防衛隊への入隊を夢見ていたが、謎の生物がすべてを変えてしまう。そして物語が始まる。", "ja", "ja", true)]
        [TestCase("日比野カフカは防衛隊への入隊を夢見ていたが、謎の生物がすべてを変えてしまう。そして物語が始まる。", "ja", "en", false)]
        [TestCase("Kafka Hibino dreams of joining the Defense Force, but a strange creature changes everything.", "en", "fr", false)]
        public void the_description_gate_takes_the_expected_language(string text, string language, string expected, bool ok)
        {
            GoogleBooksService.IsAcceptableDescription(text, language, expected, out _).Should().Be(ok);
        }

        // M6b pre-review fix (2026-09-24): CJK script is the own script of ja, ko and every zh variant --
        // accepted for that edition, still rejected for an English request (with or without a language).
        [TestCase("괴수 8호의 힘을 얻은 카프카는 방위대에 입대하기 위해 다시 한번 시험에 도전하게 되는데, 그 앞에 새로운 위협이 나타난다.", "ko", "ko", true, null)]
        [TestCase("괴수 8호의 힘을 얻은 카프카는 방위대에 입대하기 위해 다시 한번 시험에 도전하게 되는데, 그 앞에 새로운 위협이 나타난다.", "ko", "en", false, "language ko")]
        [TestCase("괴수 8호의 힘을 얻은 카프카는 방위대에 입대하기 위해 다시 한번 시험에 도전하게 되는데, 그 앞에 새로운 위협이 나타난다.", null, "en", false, "cjk")]
        [TestCase("日比野卡夫卡夢想加入防衛隊，但一隻神秘的生物改變了一切。他的人生從此走上了完全不同的道路，故事就此展開。", "zh-TW", "zh-TW", true, null)]
        [TestCase("日比野卡夫卡夢想加入防衛隊，但一隻神秘的生物改變了一切。他的人生從此走上了完全不同的道路，故事就此展開。", "zh", "zh", true, null)]
        [TestCase("日比野卡夫卡夢想加入防衛隊，但一隻神秘的生物改變了一切。他的人生從此走上了完全不同的道路，故事就此展開。", null, "fr", false, "cjk")]
        public void cjk_script_is_accepted_only_for_its_own_language(string text, string language, string expected, bool ok, string expectedReason)
        {
            GoogleBooksService.IsAcceptableDescription(text, language, expected, out var reason).Should().Be(ok);
            reason.Should().Be(expectedReason);
        }

        // M6b pre-review fix (2026-09-24): the product-listing words are English -- a French blurb that
        // says "journal" (a diary) is a blurb; the same word still rejects an English one.
        [TestCase("Dans son journal, Kafka raconte comment une étrange créature a bouleversé sa vie de nettoyeur.", "fr", "fr", true, null)]
        [TestCase("Dans son journal, Kafka raconte comment une étrange créature a bouleversé sa vie de nettoyeur.", null, "en", false, "product note 'journal'")]
        [TestCase("Kaiju No. 8 Journal: 120 lined pages for your notes, sketches and ideas, 6 x 9 inches.", "en", "en", false, "product note 'Journal'")]
        // Fix round 1 (Minor 1): only "journal" is exempt outside English -- a Notebook listing is junk in
        // every edition language.
        [TestCase("L'Attaque des Titans Notebook: 120 lined pages for your own ideas, 6 x 9 inches.", null, "fr", false, "product note 'Notebook'")]
        [TestCase("**************Note: This is Notebook Not Story or Manga Volume. Lignes pour vos idées.", "fr", "fr", false, "product note 'Note: This is'")]
        [TestCase("Angriff auf Titan Coloring Book: 50 Motive von Eren, Mikasa und Armin zum Ausmalen.", null, "de", false, "product note 'Coloring Book'")]
        [TestCase("Dans son journal intime, Kafka note ce qu'il voit: une créature étrange et un notebook oublié.", "fr", "fr", false, "product note 'notebook'")]
        public void the_product_listing_words_reject_english_blurbs_only(string text, string language, string expected, bool ok, string expectedReason)
        {
            GoogleBooksService.IsAcceptableDescription(text, language, expected, out var reason).Should().Be(ok);
            reason.Should().Be(expectedReason);
        }

        [Test]
        public void a_french_title_search_restricts_to_french_and_reads_the_tome_label()
        {
            const string frBlurb = "Kafka Hibino rêve d'intégrer les Forces de défense, mais une étrange créature change tout.";
            _lists["intitle:\"L'Attaque des Titans\" \"Tome 5\""] = new[] { Item("fr5", "L'Attaque des Titans T05", "fr", frBlurb, Thumbnails, 192, "2014-02-05", "9782811611705") };
            _records["fr5"] = Item("fr5", "L'Attaque des Titans T05", "fr", frBlurb, Thumbnails, 192, "2014-02-05", "9782811611705");

            var details = Subject.LookupVolume("L'Attaque des Titans", 5, "fr");

            details.Should().NotBeNull();
            details.Isbn13.Should().Be("9782811611705");
            _requests.Should().Contain(r => r.Contains("langRestrict=fr"));
        }

        // Preferred Edition (2026-09-24): a French hit with a good French blurb is the answer -- no
        // "ebooks" retry spent on it (the English gate would reject the blurb and pay a query per volume).
        [Test]
        public void a_french_hit_with_a_french_blurb_spends_no_ebook_retry()
        {
            const string frBlurb = "Kafka Hibino rêve d'intégrer les Forces de défense, mais une étrange créature change tout.";
            _lists["intitle:\"L'Attaque des Titans\" \"Tome 5\""] = new[] { Item("fr5", "L'Attaque des Titans T05", "fr", frBlurb, Thumbnails, 192, "2014-02-05", "9782811611705") };
            _records["fr5"] = Item("fr5", "L'Attaque des Titans T05", "fr", frBlurb, Thumbnails, 192, "2014-02-05", "9782811611705");

            Subject.LookupVolume("L'Attaque des Titans", 5, "fr");

            _requests.Should().NotContain(r => r.Contains("filter=ebooks"));
            Requests(ListPath + "?").Should().Be(1);
        }

        [Test]
        public void an_english_language_is_the_english_title_search()
        {
            _lists["intitle:\"Fairy Tail, Vol. 1\""] = new[] { Item("en1", "Fairy Tail, Vol. 1", "en", Blurb, Thumbnails, 192, "2008-03-25", "9780345501332") };
            _records["en1"] = Item("en1", "Fairy Tail, Vol. 1", "en", Blurb, Thumbnails, 192, "2008-03-25", "9780345501332");

            Subject.LookupVolume("Fairy Tail", 1, "en").Isbn13.Should().Be("9780345501332");

            _requests.Should().Contain(r => r.Contains("langRestrict=en"));
        }

        // Preferred Edition (2026-09-24): the edition's own volume label identifies the volume.
        [TestCase("L'Attaque des Titans T05", "fr", true)]
        [TestCase("L'Attaque des Titans Tome 5", "fr", true)]
        [TestCase("L'Attaque des Titans Tome 15", "fr", false)]
        [TestCase("Angriff auf Titan Band 5", "de", true)]
        [TestCase("Angriff auf Titan Bd. 05", "de", true)]
        [TestCase("進撃の巨人 第5巻", "ja", true)]
        [TestCase("進撃の巨人 5巻", "ja", true)]
        [TestCase("進撃の巨人 15巻", "ja", false)]
        [TestCase("Attack on Titan Tome 5 (Pika)", "en", false)]

        // KR/CN piece 2 (2026-10-02, M5): Korean and Chinese labels, with zh-TW on the zh rule.
        [TestCase("나 혼자만 레벨업 5권", "ko", true)]
        [TestCase("나 혼자만 레벨업 제5권", "ko", true)]
        [TestCase("나 혼자만 레벨업 15권", "ko", false)]
        [TestCase("斗破苍穹 第5卷", "zh", true)]
        [TestCase("斗破苍穹 5卷", "zh", true)]
        [TestCase("斗破苍穹 第15卷", "zh", false)]
        [TestCase("霹靂神州 第5集", "zh-TW", true)]
        [TestCase("斗破苍穹 第5卷 (Pika)", "ja", false)]

        // Fix round 1 (Minor 2): a label followed by the publisher tag defeats the English trailing-number
        // form, so only the edition's own branch matches -- and the other language's branch does not.
        [TestCase("Angriff auf Titan Bd. 5 (Carlsen)", "de", true)]
        [TestCase("L'Attaque des Titans Tome 5 (Pika)", "fr", true)]
        [TestCase("Angriff auf Titan Bd. 5 (Carlsen)", "fr", false)]
        [TestCase("L'Attaque des Titans Tome 5 (Pika)", "de", false)]
        [TestCase("Angriff auf Titan Band 15 (Carlsen)", "de", false)]
        public void an_editions_volume_label_identifies_the_volume(string title, string language, bool right)
        {
            var list = "intitle:\"X\" \"" + EditionLabel(language) + "\"";
            _lists[language == "en" ? "intitle:\"X, Vol. 5\"" : list] = new[] { Item("v5", title, language, null, Thumbnails, 192, "2014-02-05", "9782811611705") };
            _records["v5"] = Item("v5", title, language, null, Thumbnails, 192, "2014-02-05", "9782811611705");

            var details = Subject.LookupVolume("X", 5, language);

            (details != null).Should().Be(right);
        }

        private static string EditionLabel(string language)
        {
            return NzbDrone.Core.Books.EditionLanguages.VolumeLabel(language, "5");
        }

        // ---- the ISBN record (D1) ----

        [Test]
        public void isbn_lookup_takes_the_records_own_description_language_and_largest_cover()
        {
            _lists["isbn:9780345501332"] = new[] { Item("S7qCngEACAAJ", "Fairy Tail 1", "en", Abridged, Thumbnails) };
            _records["S7qCngEACAAJ"] = Item("S7qCngEACAAJ", "Fairy Tail 1", "en", Blurb, Scanned, 208, "2008-03-25", "9780345501332");

            var details = Subject.LookupByIsbn("9780345501332");

            details.Description.Should().Be(Blurb);
            details.Language.Should().Be("en");
            details.CoverUrl.Should().Be("https://books.google.com/books/content?id=S7qCngEACAAJ&printsec=frontcover&img=1&zoom=4");
            details.CoverSize.Should().Be("large");
            details.PageCount.Should().Be(208);
            details.ReleaseDate.Should().Be(new DateTime(2008, 3, 25));
            details.Isbn13.Should().Be("9780345501332");
            Requests(RecordPath + "S7qCngEACAAJ?").Should().Be(1);
        }

        [Test]
        public void a_records_bare_br_line_breaks_become_spaces()
        {
            _lists["isbn:9781646515738"] = new[] { Item("ApHhzwEACAAJ", "Fairy Tail 48", "en", null, Thumbnails) };
            _records["ApHhzwEACAAJ"] = Item("ApHhzwEACAAJ", "Fairy Tail 48", "en", "TWO DRAGONS, THREE SLAYERS<br><br> With her curse magic, Kyoka has deprived Erza of all her senses and left her to be tortured.", Thumbnails);

            Subject.LookupByIsbn("9781646515738").Description.Should().Be("TWO DRAGONS, THREE SLAYERS   With her curse magic, Kyoka has deprived Erza of all her senses and left her to be tortured.");
        }

        [Test]
        public void a_catalogue_only_record_keeps_the_thumbnail()
        {
            _lists["isbn:9780345510396"] = new[] { Item("Rk81zgEACAAJ", "Fairy Tail 7", "en", null, Thumbnails) };
            _records["Rk81zgEACAAJ"] = Item("Rk81zgEACAAJ", "Fairy Tail 7", "en", Blurb, Thumbnails);

            var details = Subject.LookupByIsbn("9780345510396");

            details.CoverUrl.Should().Be("https://books.google.com/books/content?id=Rk81zgEACAAJ&printsec=frontcover&img=1&zoom=1");
            details.CoverSize.Should().Be("thumbnail");
        }

        [Test]
        public void a_record_without_a_description_yields_none_even_when_the_list_had_an_abridged_one()
        {
            _lists["isbn:9781974727155"] = new[] { Item("VPcM0gEACAAJ", "My Hero Academia 30", "en", Abridged, Thumbnails) };
            _records["VPcM0gEACAAJ"] = Item("VPcM0gEACAAJ", "My Hero Academia 30", "en", null, Thumbnails);

            var details = Subject.LookupByIsbn("9781974727155");

            details.Description.Should().BeNull();
            details.CoverUrl.Should().NotBeNull();
        }

        [Test]
        public void a_record_that_answers_no_body_falls_back_to_the_list_item()
        {
            _lists["isbn:9781612624099"] = new[] { Item("r2WQEAAAQBAJ", "Fairy Tail 32", "en", Abridged, Thumbnails, 200, "2013-11-19") };

            var details = Subject.LookupByIsbn("9781612624099");

            details.Description.Should().Be(Abridged);
            details.PageCount.Should().Be(200);
        }

        [Test]
        public void the_isbn_record_is_fetched_once_and_then_cached()
        {
            _lists["isbn:9781974700523"] = new[] { Item("a1", "Demon Slayer 1", "en", Abridged, Thumbnails) };
            _records["a1"] = Item("a1", "Demon Slayer 1", "en", Blurb, Thumbnails);

            Subject.LookupByIsbn("9781974700523").Description.Should().Be(Blurb);
            Subject.LookupByIsbn("9781974700523").Description.Should().Be(Blurb);

            Requests(ListPath + "?").Should().Be(1);
            Requests(RecordPath).Should().Be(1);
        }

        [Test]
        public void a_single_503_on_the_record_fetch_is_retried_once_and_succeeds()
        {
            _lists["isbn:9781975363178"] = new[] { Item("b1", "Oshi no Ko 1", "en", Abridged, Thumbnails) };
            _records["b1"] = Item("b1", "Oshi no Ko 1", "en", Blurb, Thumbnails);

            _recordStatus = HttpStatusCode.ServiceUnavailable;
            _recordFailuresLeft = 1;

            Subject.LookupByIsbn("9781975363178").Description.Should().Be(Blurb);

            Requests(RecordPath + "b1?").Should().Be(2);
            Requests(ListPath + "?").Should().Be(1);
        }

        // A 429 is the daily quota: not retried (a 2 s pause recovers nothing), not cached, and
        // surfaced as GoogleBooksQuotaException so the provider's breaker can count it.
        [Test]
        public void a_429_on_the_record_fetch_is_not_retried_and_throws_the_quota_exception()
        {
            _lists["isbn:9781975363178"] = new[] { Item("b1", "Oshi no Ko 1", "en", Abridged, Thumbnails) };
            _records["b1"] = Item("b1", "Oshi no Ko 1", "en", Blurb, Thumbnails);

            _recordStatus = HttpStatusCode.TooManyRequests;
            Assert.Throws<GoogleBooksQuotaException>(() => Subject.LookupByIsbn("9781975363178"));
            Requests(RecordPath + "b1?").Should().Be(1);

            _recordStatus = HttpStatusCode.OK;
            Subject.LookupByIsbn("9781975363178").Description.Should().Be(Blurb);

            Requests(ListPath + "?").Should().Be(2);
        }

        [Test]
        public void a_429_on_the_title_search_throws_the_quota_exception()
        {
            _listStatus = HttpStatusCode.TooManyRequests;

            Assert.Throws<GoogleBooksQuotaException>(() => Subject.LookupVolume("Fairy Tail", 1));
            Requests(ListPath + "?").Should().Be(1);
        }

        [Test]
        public void a_503_on_the_title_search_is_a_null_not_the_quota_exception()
        {
            _listStatus = HttpStatusCode.ServiceUnavailable;

            Subject.LookupVolume("Fairy Tail", 1).Should().BeNull();
        }

        [Test]
        public void an_unknown_isbn_still_yields_the_isbn_only_record()
        {
            var details = Subject.LookupByIsbn("9790000000000");

            details.Should().NotBeNull();
            details.Isbn13.Should().Be("9790000000000");
            details.Description.Should().BeNull();
            Requests(RecordPath).Should().Be(0);
        }

        // ---- the title search (D2 in the ranking) ----

        [Test]
        public void title_search_skips_non_english_records_and_prefers_an_acceptable_blurb()
        {
            _lists["intitle:\"Fairy Tail, Vol. 1\""] = new[]
            {
                Item("photobook", "Fairy Tail Vol. 1", "en", "TV・CMなどで活躍中の人気アイドル木村好珠ちゃんとのコラボ写真集。第1巻。", Thumbnails),
                Item("notebook", "Fairy Tail, Vol. 1", "en", "**************Note: This is Notebook Not Story or Manga Volume. Lined pages.", Thumbnails),
                Item("japanese", "Fairy Tail, Vol. 1", "ja", Blurb, Thumbnails),
                Item("real", "Fairy Tail, Vol. 1", "en", Abridged, Thumbnails, 208, "2008-03-25", "9780345501332")
            };
            _records["real"] = Item("real", "Fairy Tail, Vol. 1", "en", Blurb, Thumbnails, 208, "2008-03-25", "9780345501332");

            var details = Subject.LookupVolume("Fairy Tail", 1);

            details.Description.Should().Be(Blurb);
            details.Isbn13.Should().Be("9780345501332");
            Requests(RecordPath + "real?").Should().Be(1);
            Requests(RecordPath + "photobook?").Should().Be(0);
        }

        [Test]
        public void title_search_with_only_non_english_hits_finds_nothing()
        {
            _lists["intitle:\"Fairy Tail, Vol. 7\""] = new[] { Item("japanese", "Fairy Tail, Vol. 7", "ja", Blurb, Thumbnails) };

            Subject.LookupVolume("Fairy Tail", 7).Should().BeNull();
            Requests(RecordPath).Should().Be(0);
        }

        [Test]
        public void title_search_reports_the_records_language()
        {
            _lists["intitle:\"Berserk, Vol. 17\""] = new[] { Item("berserk17", "Berserk, Vol. 17", "en", Abridged, Thumbnails) };
            _records["berserk17"] = Item("berserk17", "Berserk, Vol. 17", "en", Blurb, Scanned);

            var details = Subject.LookupVolume("Berserk", 17);

            details.Language.Should().Be("en");
            details.CoverUrl.Should().EndWith("zoom=4");
        }

        // No language field = unknown, accepted (the reading the description validator gives null).
        [Test]
        public void title_search_keeps_a_record_with_no_language_field()
        {
            _lists["intitle:\"Berserk, Vol. 18\""] = new[] { Item("berserk18", "Berserk, Vol. 18", null, Abridged, Thumbnails, 232, "2007-08-22", "9781593077433") };
            _records["berserk18"] = Item("berserk18", "Berserk, Vol. 18", null, Blurb, Thumbnails, 232, "2007-08-22", "9781593077433");

            var details = Subject.LookupVolume("Berserk", 18);

            details.Should().NotBeNull();
            details.Isbn13.Should().Be("9781593077433");
            details.Language.Should().BeNull();
        }

        // ---- the on-disk ISBN cache ----

        [Test]
        public void an_isbn_record_is_written_through_to_disk_and_read_back_by_a_new_process_without_http()
        {
            _lists["isbn:9781974700523"] = new[] { Item("a1", "Demon Slayer 1", "en", Abridged, Thumbnails) };
            _records["a1"] = Item("a1", "Demon Slayer 1", "en", Blurb, Scanned, 192, "2018-07-03");

            string written = null;
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.WriteAllText(CachePath + ".tmp", It.IsAny<string>()))
                  .Callback<string, string>((path, text) => written = text);

            Subject.LookupByIsbn("9781974700523").Description.Should().Be(Blurb);

            written.Should().NotBeNull();
            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(CachePath + ".tmp", CachePath, true), Times.Once());

            GivenCacheFile(written);
            var again = NewInstance().LookupByIsbn("9781974700523");

            again.Description.Should().Be(Blurb);
            again.CoverUrl.Should().EndWith("zoom=4");
            again.CoverSize.Should().Be("large");
            again.PageCount.Should().Be(192);
            again.ReleaseDate.Should().Be(new DateTime(2018, 7, 3));
            Requests(ListPath + "?").Should().Be(1);
            Requests(RecordPath).Should().Be(1);
        }

        [Test]
        public void an_expired_on_disk_record_is_ignored_and_fetched_again()
        {
            _lists["isbn:9781974700523"] = new[] { Item("a1", "Demon Slayer 1", "en", Abridged, Thumbnails) };
            _records["a1"] = Item("a1", "Demon Slayer 1", "en", Blurb, Thumbnails);

            var stale = new Dictionary<string, GoogleBooksService.IsbnCacheEntry>
            {
                ["9781974700523"] = new GoogleBooksService.IsbnCacheEntry
                {
                    FetchedAt = DateTime.UtcNow.AddDays(-31),
                    Details = new VolumeDetails { Isbn13 = "9781974700523", Title = "Demon Slayer 1", Description = "An old answer that has since expired and must not be served." }
                }
            };
            GivenCacheFile(stale.ToJson());

            Subject.LookupByIsbn("9781974700523").Description.Should().Be(Blurb);
            Requests(ListPath + "?").Should().Be(1);
        }

        [Test]
        public void a_fresh_on_disk_record_is_served_without_http()
        {
            var fresh = new Dictionary<string, GoogleBooksService.IsbnCacheEntry>
            {
                ["9781974700523"] = new GoogleBooksService.IsbnCacheEntry
                {
                    FetchedAt = DateTime.UtcNow.AddDays(-29),
                    Details = new VolumeDetails { Isbn13 = "9781974700523", Title = "Fairy Tail 1", Description = Blurb, Language = "en" }
                }
            };
            GivenCacheFile(fresh.ToJson());

            Subject.LookupByIsbn("9781974700523").Description.Should().Be(Blurb);
            Requests(ListPath + "?").Should().Be(0);
        }

        [Test]
        public void a_failed_lookup_is_not_written_to_disk()
        {
            _lists["isbn:9781975363178"] = new[] { Item("b1", "Oshi no Ko 1", "en", Abridged, Thumbnails) };
            _records["b1"] = Item("b1", "Oshi no Ko 1", "en", Blurb, Thumbnails);

            _recordStatus = HttpStatusCode.ServiceUnavailable;
            Subject.LookupByIsbn("9781975363178").Should().BeNull();

            _recordStatus = HttpStatusCode.TooManyRequests;
            Assert.Throws<GoogleBooksQuotaException>(() => Subject.LookupByIsbn("9781975363178"));

            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void clear_cache_keeps_the_on_disk_records()
        {
            _lists["isbn:9781974700523"] = new[] { Item("a1", "Demon Slayer 1", "en", Abridged, Thumbnails) };
            _records["a1"] = Item("a1", "Demon Slayer 1", "en", Blurb, Thumbnails);

            Subject.LookupByIsbn("9781974700523").Description.Should().Be(Blurb);
            Subject.ClearCache();
            Subject.LookupByIsbn("9781974700523").Description.Should().Be(Blurb);

            Requests(ListPath + "?").Should().Be(1);
        }

        // ---- the record's title fields + complete records never expire (D6) ----

        [Test]
        public void isbn_record_carries_title_subtitle_and_series_book_title()
        {
            // record: title "Sword Art Online 21 (light novel)", subtitle "Unital Ring I",
            // seriesInfo.shortSeriesBookTitle "Unital Ring I" (the real Google answer for 9781975315962)
            GivenIsbnRecord("9781975315962", title: "Sword Art Online 21 (light novel)", subtitle: "Unital Ring I", seriesBookTitle: "Unital Ring I");

            var details = Subject.LookupByIsbn("9781975315962");

            details.Title.Should().Be("Sword Art Online 21 (light novel)");
            details.Subtitle.Should().Be("Unital Ring I");
            details.SeriesBookTitle.Should().Be("Unital Ring I");
        }

        [Test]
        public void a_record_without_subtitle_fields_leaves_them_null()
        {
            GivenIsbnRecord("9780316315302", title: "Re:ZERO -Starting Life in Another World-, Vol. 1 (light novel)");

            var details = Subject.LookupByIsbn("9780316315302");

            details.Title.Should().Be("Re:ZERO -Starting Life in Another World-, Vol. 1 (light novel)");
            details.Subtitle.Should().BeNull();
            details.SeriesBookTitle.Should().BeNull();
        }

        [Test]
        public void a_complete_on_disk_record_is_served_after_the_lifetime_without_http()
        {
            // complete = acceptable English description, poster-grade cover, title present
            GivenOnDiskRecord("9780316376815", fetchedAt: DateTime.UtcNow.AddDays(-45), title: "Sword Art Online 2: Aincrad (light novel)", coverSize: "medium", description: new string('x', 120), language: "en");

            var details = Subject.LookupByIsbn("9780316376815");

            details.Title.Should().Be("Sword Art Online 2: Aincrad (light novel)");
            Requests(ListPath + "?").Should().Be(0);
        }

        [Test]
        public void an_incomplete_on_disk_record_still_expires()
        {
            // no cover: incomplete -> the 30-day lifetime applies and it is fetched again
            GivenOnDiskRecord("9780316376815", fetchedAt: DateTime.UtcNow.AddDays(-45), title: "Sword Art Online 2: Aincrad (light novel)", coverSize: null, description: new string('x', 120), language: "en");
            GivenIsbnRecord("9780316376815", title: "Sword Art Online 2: Aincrad (light novel)");

            Subject.LookupByIsbn("9780316376815");

            Requests(ListPath + "?").Should().Be(1);
        }

        [Test]
        public void a_pre_build_on_disk_record_without_a_title_is_incomplete()
        {
            GivenOnDiskRecord("9780316376815", fetchedAt: DateTime.UtcNow.AddDays(-45), title: null, coverSize: "large", description: new string('x', 120), language: "en");
            GivenIsbnRecord("9780316376815", title: "Sword Art Online 2: Aincrad (light novel)");

            Subject.LookupByIsbn("9780316376815");

            Requests(ListPath + "?").Should().Be(1);
        }

        // D6: a record the live image wrote before the title fields existed is re-fetched on its
        // first read regardless of age — served as-is, D5 would reject it (a null title names no
        // series) for the rest of its 30 days.
        [Test]
        public void a_pre_build_on_disk_record_is_re_fetched_even_when_fresh()
        {
            GivenOnDiskRecord("9780316376815", fetchedAt: DateTime.UtcNow.AddDays(-1), title: null, coverSize: "large", description: new string('x', 120), language: "en");
            GivenIsbnRecord("9780316376815", title: "Sword Art Online 2: Aincrad (light novel)");

            var details = Subject.LookupByIsbn("9780316376815");

            Requests(ListPath + "?").Should().Be(1);
            details.Title.Should().Be("Sword Art Online 2: Aincrad (light novel)");
        }

        // ---- D5 x D6 (2026-09-17): a rejected record is never permanent ----

        // Complete by every measure, but the provider rejected it (the title does not name the
        // series): the 30-day lifetime applies so a Google-side correction can arrive.
        [Test]
        public void a_rejected_complete_on_disk_record_expires_after_the_lifetime()
        {
            GivenOnDiskRecord("9781612622668", fetchedAt: DateTime.UtcNow.AddDays(-45), title: "Pantsu Agerune", coverSize: "large", description: new string('x', 120), language: "en", rejected: true);
            GivenIsbnRecord("9781612622668", title: "Fairy Tail 24");

            var details = Subject.LookupByIsbn("9781612622668");

            Requests(ListPath + "?").Should().Be(1);
            details.Title.Should().Be("Fairy Tail 24");
        }

        [Test]
        public void a_rejected_complete_on_disk_record_within_the_lifetime_is_served()
        {
            GivenOnDiskRecord("9781612622668", fetchedAt: DateTime.UtcNow.AddDays(-10), title: "Pantsu Agerune", coverSize: "large", description: new string('x', 120), language: "en", rejected: true);

            var details = Subject.LookupByIsbn("9781612622668");

            Requests(ListPath + "?").Should().Be(0);
            details.Title.Should().Be("Pantsu Agerune");
        }

        [Test]
        public void marking_a_record_rejected_writes_the_flag()
        {
            GivenOnDiskRecord("9781612622668", fetchedAt: DateTime.UtcNow.AddDays(-1), title: "Pantsu Agerune", coverSize: "large", description: new string('x', 120), language: "en");

            string written = null;
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.WriteAllText(CachePath + ".tmp", It.IsAny<string>()))
                  .Callback<string, string>((path, text) => written = text);

            Subject.MarkIsbnRecordRejected("9781612622668");

            written.Should().NotBeNull();
            Json.Deserialize<Dictionary<string, GoogleBooksService.IsbnCacheEntry>>(written)["9781612622668"].Rejected.Should().BeTrue();
            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(CachePath + ".tmp", CachePath, true), Times.Once());

            // Every series pass re-rejects the same record from cache: a second mark is a no-op, not a rewrite.
            Subject.MarkIsbnRecordRejected("9781612622668");

            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void marking_an_unknown_isbn_is_a_no_op()
        {
            GivenOnDiskRecord("9781612622668", fetchedAt: DateTime.UtcNow.AddDays(-1), title: "Pantsu Agerune", coverSize: "large", description: new string('x', 120), language: "en");

            Subject.MarkIsbnRecordRejected("9780316376815");

            Mocker.GetMock<IDiskProvider>().Verify(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [TestCase("d", "medium", "T", true)]
        [TestCase("d", "thumbnail", "T", false)]
        [TestCase("d", "medium", null, false)]
        [TestCase(null, "medium", "T", false)]
        public void completeness_rule(string description, string coverSize, string title, bool complete)
        {
            var d = new VolumeDetails { Description = description == null ? null : new string('x', 120), Language = "en", CoverSize = coverSize, CoverUrl = coverSize == null ? null : "https://x/y.jpg", Title = title };

            GoogleBooksService.IsComplete(d).Should().Be(complete);
        }

        // Preferred Edition (2026-09-24, ruling S10): a non-English record is complete when its blurb is
        // acceptable in its OWN language -- else every French record re-fetches every 30 days against
        // the daily quota. English (and language-less) records keep the English rule above.
        [TestCase("fr", "Kafka Hibino rêve d'intégrer les Forces de défense, mais une étrange créature change tout.", true)]
        [TestCase("ja", "日比野カフカは防衛隊への入隊を夢見ていたが、謎の生物がすべてを変えてしまう。そして物語が始まる。", true)]
        [TestCase("en", "日比野カフカは防衛隊への入隊を夢見ていたが、謎の生物がすべてを変えてしまう。そして物語が始まる。", false)]
        [TestCase(null, "日比野カフカは防衛隊への入隊を夢見ていたが、謎の生物がすべてを変えてしまう。そして物語が始まる。", false)]
        [TestCase("fr", "Trop court.", false)]
        [TestCase("ko", "괴수 8호의 힘을 얻은 카프카는 방위대에 입대하기 위해 다시 한번 시험에 도전하게 되는데, 그 앞에 새로운 위협이 나타난다.", true)]
        public void completeness_follows_the_records_own_language(string language, string description, bool complete)
        {
            var d = new VolumeDetails { Description = description, Language = language, CoverSize = "medium", CoverUrl = "https://x/y.jpg", Title = "T" };

            GoogleBooksService.IsComplete(d).Should().Be(complete);
        }
    }
}
