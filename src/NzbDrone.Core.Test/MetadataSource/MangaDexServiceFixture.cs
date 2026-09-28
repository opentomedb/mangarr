using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // D6 (2026-09-16): the English-locale covers of a series, joined by AniList id. A canned MangaDex
    // answers /manga (one candidate page) and /cover (pages keyed by offset). The real CacheManager is
    // in play (TestBase): a test that needs two answers for one AniList id changes the canned pages
    // between calls only where the first answer was NOT cached (null).
    [TestFixture]
    public class MangaDexServiceFixture : CoreTest<MangaDexService>
    {
        private const string Right = "227e3f72-863f-46f9-bafe-c43104ca29ee";
        private const string Wrong = "ffffffff-0000-4000-8000-000000000000";
        private const string Uploads = "https://uploads.mangadex.org/covers/";

        private readonly List<object> _manga = new List<object>();
        private readonly Dictionary<int, object> _coverPages = new Dictionary<int, object>();   // offset -> page body
        private readonly List<string> _requests = new List<string>();
        private HttpStatusCode _coverStatus = HttpStatusCode.OK;

        [SetUp]
        public void Setup()
        {
            _manga.Clear();
            _coverPages.Clear();
            _requests.Clear();
            _coverStatus = HttpStatusCode.OK;

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(Answer);
        }

        // ---- the canned MangaDex ----

        private static object Manga(string id, string title, int? aniListId, string altTitle = null)
        {
            return new
            {
                id,
                type = "manga",
                attributes = new
                {
                    title = new Dictionary<string, string> { { "en", title } },
                    altTitles = altTitle == null ? new object[0] : new object[] { new Dictionary<string, string> { { "ja-ro", altTitle } } },
                    links = aniListId.HasValue ? new Dictionary<string, string> { { "al", aniListId.Value.ToString() } } : null
                }
            };
        }

        private static object Cover(string fileName, string volume, string locale = "en")
        {
            return new { id = Guid.NewGuid().ToString(), type = "cover_art", attributes = new { fileName, volume, locale } };
        }

        // Pages in MangaDex's shape: limit 100, offset = page index * 100, one total for all pages.
        private void GivenCoverPages(int total, params object[][] pages)
        {
            for (var i = 0; i < pages.Length; i++)
            {
                _coverPages[i * 100] = new { data = pages[i], limit = 100, offset = i * 100, total };
            }
        }

        private HttpResponse Answer(HttpRequest request)
        {
            var query = Uri.UnescapeDataString(request.Url.Query ?? string.Empty);
            _requests.Add(request.Url.Path + "?" + query);

            if (request.Url.Path == "/manga")
            {
                return Reply(request, new { data = _manga });
            }

            if (_coverStatus != HttpStatusCode.OK)
            {
                return new HttpResponse(request, new HttpHeader(), string.Empty, _coverStatus);
            }

            var offset = int.Parse(query.Split('&').Select(p => p.Split('=')).First(p => p[0] == "offset")[1]);
            return Reply(request, _coverPages.TryGetValue(offset, out var page) ? page : new { data = new object[0], limit = 100, offset, total = 0 });
        }

        private static HttpResponse Reply(HttpRequest request, object body)
        {
            return new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, body.ToJson());
        }

        private int Requests(string path)
        {
            return _requests.Count(r => r.StartsWith(path + "?"));
        }

        // ---- the join ----

        [Test]
        public void joins_by_the_anilist_link_not_the_title()
        {
            _manga.Add(Manga(Wrong, "Fairy Tail", 128087));
            _manga.Add(Manga(Right, "FAIRY TAIL", 30598));
            GivenCoverPages(1, new[] { Cover("v1.jpg", "1") });

            var covers = Subject.GetEnglishCovers(30598, "Fairy Tail");

            covers.MangaId.Should().Be(Right);
            covers.Volume1.Should().Be(Uploads + Right + "/v1.jpg");
            _requests.Should().Contain("/cover?manga[]=" + Right + "&locales[]=en&limit=100&offset=0&order[volume]=asc");
        }

        [Test]
        public void title_equality_is_the_fallback_only_when_no_candidate_carries_a_link()
        {
            _manga.Add(Manga("zero", "Fairy Tail Zero", null));
            _manga.Add(Manga(Right, "Fairy Tail", null));
            GivenCoverPages(1, new[] { Cover("v1.jpg", "1") });

            Subject.GetEnglishCovers(30598, "Fairy Tail").MangaId.Should().Be(Right);
        }

        [Test]
        public void a_title_match_with_another_anilist_link_is_not_the_series()
        {
            _manga.Add(Manga(Wrong, "Fairy Tail", 128087));

            Subject.GetEnglishCovers(30598, "Fairy Tail").Should().BeNull();
            Requests("/cover").Should().Be(0);
        }

        // ---- the covers ----

        [Test]
        public void covers_are_keyed_by_integer_volume_across_pages()
        {
            _manga.Add(Manga(Right, "Attack on Titan", 53390));
            var first = Enumerable.Range(1, 100).Select(n => Cover($"v{n}.jpg", n.ToString())).ToArray();
            var second = new[] { Cover("v101.jpg", "101"), Cover("side.jpg", "1.5"), Cover("none.jpg", null) };
            GivenCoverPages(103, first, second);

            var covers = Subject.GetEnglishCovers(53390, "Attack on Titan");

            covers.CoversByVolume.Should().HaveCount(101);
            covers.CoversByVolume[101].Should().Be(Uploads + Right + "/v101.jpg");
            covers.CoversByVolume.Keys.Should().NotContain(0);
            Requests("/cover").Should().Be(2);
            _requests.Should().Contain(r => r.StartsWith("/cover?") && r.Contains("&offset=100&"));
        }

        [Test]
        public void the_first_cover_of_a_volume_wins_and_other_locales_are_skipped()
        {
            _manga.Add(Manga(Right, "Chainsaw Man", 105778));
            GivenCoverPages(3, new[] { Cover("a.jpg", "3"), Cover("b.jpg", "3"), Cover("ja.jpg", "4", "ja") });

            var covers = Subject.GetEnglishCovers(105778, "Chainsaw Man");

            covers.CoversByVolume.Should().Equal(new Dictionary<int, string> { { 3, Uploads + Right + "/a.jpg" } });
            covers.Volume1.Should().BeNull();
        }

        [Test]
        public void a_resolved_series_with_no_english_covers_is_a_cached_answer()
        {
            _manga.Add(Manga(Right, "Berserk", 30002));
            GivenCoverPages(0, new object[0]);

            Subject.GetEnglishCovers(30002, "Berserk").CoversByVolume.Should().BeEmpty();
            Subject.GetEnglishCovers(30002, "Berserk").CoversByVolume.Should().BeEmpty();

            Requests("/manga").Should().Be(1);
            Requests("/cover").Should().Be(1);
        }

        [Test]
        public void a_rate_limited_cover_page_is_not_cached()
        {
            _manga.Add(Manga(Right, "Horimiya", 72451));
            GivenCoverPages(1, new[] { Cover("v1.jpg", "1") });

            _coverStatus = HttpStatusCode.TooManyRequests;
            Subject.GetEnglishCovers(72451, "Horimiya").Should().BeNull();

            _coverStatus = HttpStatusCode.OK;
            Subject.GetEnglishCovers(72451, "Horimiya").Volume1.Should().EndWith("/v1.jpg");

            Requests("/manga").Should().Be(2);
        }

        [Test]
        public void a_missing_entry_is_not_cached_either()
        {
            Subject.GetEnglishCovers(99999, "Nothing Here").Should().BeNull();

            _manga.Add(Manga(Right, "Nothing Here", 99999));
            GivenCoverPages(1, new[] { Cover("v1.jpg", "1") });
            Subject.GetEnglishCovers(99999, "Nothing Here").Should().NotBeNull();

            Requests("/manga").Should().Be(2);
        }

        [Test]
        public void a_blank_title_or_id_asks_nothing()
        {
            Subject.GetEnglishCovers(0, "Fairy Tail").Should().BeNull();
            Subject.GetEnglishCovers(30598, " ").Should().BeNull();

            _requests.Should().BeEmpty();
        }

        // Preferred Edition (2026-09-24): a French/German/Japanese series asks MangaDex for ITS locale's
        // art (spec §2.2 Covers) and caches it apart from the English answer.
        [Test]
        public void locale_covers_ask_that_locale_and_cache_apart_from_english()
        {
            _manga.Add(Manga(Right, "Attack on Titan", 53390));
            GivenCoverPages(1, new[] { Cover("fr1.jpg", "1", "fr") });

            Subject.GetLocaleCovers(53390, "Attack on Titan", "fr").Volume1.Should().EndWith("/fr1.jpg");
            _requests.Should().Contain("/cover?manga[]=" + Right + "&locales[]=fr&limit=100&offset=0&order[volume]=asc");

            Subject.GetEnglishCovers(53390, "Attack on Titan");
            _requests.Should().Contain("/cover?manga[]=" + Right + "&locales[]=en&limit=100&offset=0&order[volume]=asc");
        }

        // Preferred Edition (2026-09-24, ruling B3): the page filter keeps the requested locale only --
        // an English cover on a French page is not French art.
        [Test]
        public void locale_covers_keep_only_that_locale()
        {
            _manga.Add(Manga(Right, "Attack on Titan", 53390));
            GivenCoverPages(2, new[] { Cover("en1.jpg", "1"), Cover("fr2.jpg", "2", "fr") });

            var covers = Subject.GetLocaleCovers(53390, "Attack on Titan", "fr");

            covers.Volume1.Should().BeNull();
            covers.CoversByVolume.Should().ContainKey(2).WhichValue.Should().EndWith("/fr2.jpg");
        }

        [Test]
        public void a_blank_locale_asks_nothing()
        {
            Subject.GetLocaleCovers(53390, "Attack on Titan", " ").Should().BeNull();

            _requests.Should().BeEmpty();
        }
    }
}
