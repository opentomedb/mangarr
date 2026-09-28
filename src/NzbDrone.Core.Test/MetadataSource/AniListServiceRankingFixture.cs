using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource
{
    // The audit of 2026-09-15 (docs/superpowers/specs/2026-09-15-manga-metadata-audit.md) as
    // table tests: a canned AniList answers every search term with the page the audit reproduced
    // (or a realistic stand-in), the seven broken entries must land on their audited ids, and the
    // entries the audit found right must keep theirs (D9).
    [TestFixture]
    public class AniListServiceRankingFixture : CoreTest<AniListService>
    {
        private const string Manga = "MANGA";
        private const string Novel = "NOVEL";
        private const string OneShot = "ONE_SHOT";
        private const string Finished = "FINISHED";
        private const string Releasing = "RELEASING";

        private static readonly IReadOnlyList<string> NoAliases = Array.Empty<string>();

        private readonly Dictionary<string, List<Media>> _search = new Dictionary<string, List<Media>>(StringComparer.Ordinal);
        private readonly Dictionary<int, Media> _byId = new Dictionary<int, Media>();
        private readonly List<Probe> _requests = new List<Probe>();
        private MemoryTarget _log;

        [SetUp]
        public void Setup()
        {
            _search.Clear();
            _byId.Clear();
            _requests.Clear();

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Post(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(Answer);

            _log = new MemoryTarget("anilist-ranking") { Layout = "${level}|${message}" };
            LogManager.Configuration.AddTarget(_log.Name, _log);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Info, _log));
            LogManager.ReconfigExistingLoggers();
        }

        [TearDown]
        public void ReleaseLog()
        {
            foreach (var rule in LogManager.Configuration.LoggingRules.Where(r => r.Targets.Contains(_log)).ToList())
            {
                LogManager.Configuration.LoggingRules.Remove(rule);
            }

            LogManager.Configuration.RemoveTarget(_log.Name);
            LogManager.ReconfigExistingLoggers();
        }

        // ---- the canned AniList ----

        private static Media M(int id, string romaji, string english, string format, string status, int? volumes, int popularity, params string[] synonyms)
        {
            return new Media
            {
                Id = id,
                Format = format,
                Status = status,
                Volumes = volumes,
                Popularity = popularity,
                Description = "About " + (english ?? romaji) + "<br>Second line.",
                Synonyms = synonyms.ToList(),
                Title = new MediaTitle { Romaji = romaji, English = english },
                CoverImage = new MediaCover { ExtraLarge = Cover(id), Large = Cover(id).Replace("-xl", "-l") }
            };
        }

        private static string Cover(int id)
        {
            return $"https://s4.anilist.co/file/anilistcdn/media/manga/cover/large/bx{id}-xl.jpg";
        }

        // Keyed by the FOLDED term — what the service actually sends (D2).
        private void Given(string term, params Media[] page)
        {
            _search[TitleNormalizer.ForSearch(term)] = page.ToList();

            foreach (var m in page)
            {
                _byId[m.Id] = m;
            }
        }

        // The request body's variables pick the answer. Unknown terms answer an empty page —
        // what AniList did for the typographic apostrophe.
        private HttpResponse Answer(HttpRequest request)
        {
            var probe = Json.Deserialize<Probe>(Encoding.UTF8.GetString(request.ContentData));
            _requests.Add(probe);

            object data;

            if (probe.Variables.Id.HasValue)
            {
                data = new { Media = _byId.TryGetValue(probe.Variables.Id.Value, out var one) ? one : null };
            }
            else
            {
                data = new { Page = new { Media = _search.TryGetValue(probe.Variables.Search ?? string.Empty, out var page) ? page : new List<Media>() } };
            }

            return new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, Json.ToJson(new { Data = data }));
        }

        private void GivenFairyTailPage()
        {
            // Audit §3: rank 0 is "Pantsu Agerune" (MANGA, 1 volume, popularity 39), a hentai
            // anthology whose synonym list is its chapter titles — one of them "Fairy Tail".
            Given("Fairy Tail",
                M(128087, "Pantsu Agerune", null, Manga, Finished, 1, 39, "Fairy Tail", "Kenkyuu tte Muzukashii", "Ayaka wa Goukakuken"),
                M(30598, "FAIRY TAIL", "Fairy Tail", Manga, Finished, 63, 57000),
                M(80035, "FAIRY TAIL ZERO", "Fairy Tail Zero", Manga, Finished, 1, 8000),
                M(103551, "FAIRY TAIL: 100 Years Quest", "Fairy Tail: 100 Years Quest", Manga, Releasing, null, 20000));
        }

        // ---- the seven audited entries ----

        [Test]
        public void fairy_tail_binds_the_serial_not_the_anthology_carrying_its_name_as_a_synonym()
        {
            GivenFairyTailPage();

            var series = Subject.FindSeries("Fairy Tail", LibraryType.Manga, 63, NoAliases);

            series.Id.Should().Be(30598);
            series.MatchedVia.Should().Be("primary");
            series.EnglishTitle.Should().Be("Fairy Tail");
            series.Volumes.Should().Be(63);
            series.CoverImageUrl.Should().Be(Cover(30598));
            series.Description.Should().Be("About Fairy Tail\nSecond line.");
        }

        [Test]
        public void fairy_tail_still_binds_the_serial_when_the_volume_rule_cannot_run()
        {
            // No catalogue count: the anthology survives the size guard and must still lose on
            // primary-over-synonym, however AniList ranks it.
            GivenFairyTailPage();

            Subject.FindSeries("Fairy Tail", LibraryType.Manga, null, NoAliases).Id.Should().Be(30598);
        }

        [Test]
        public void black_clover_binds_the_serial_not_the_one_shot_pilot()
        {
            // Audit §3: the 2014 Jump NEXT!! prototype (ONE_SHOT, romaji "Black Clover") is rank 0
            // in the app's single-query shape; the serial is rank 1.
            Given("Black Clover",
                M(114652, "Black Clover", "Black Clover", OneShot, Finished, 1, 3982),
                M(86123, "Black Clover", "Black Clover", Manga, Releasing, null, 107312),
                M(97865, "Black Clover Gaiden: Quartet Knights", "Black Clover: Quartet Knights", Manga, Finished, 7, 2000));

            var series = Subject.FindSeries("Black Clover", LibraryType.Manga, 37, NoAliases);

            series.Id.Should().Be(86123);
            series.MatchedVia.Should().Be("primary");
            series.Status.Should().Be(Releasing);
        }

        [Test]
        public void blue_box_binds_the_serial_not_the_one_shot()
        {
            // Audit §3: the "Ao no Hako" one-shot (synonym "Blue Box", popularity 8,511) is rank 0.
            Given("Blue Box",
                M(122342, "Ao no Hako", null, OneShot, Finished, null, 8511, "Blue Box"),
                M(132182, "Ao no Hako", "Blue Box", Manga, Releasing, null, 55252));

            var series = Subject.FindSeries("Blue Box", LibraryType.Manga, 22, NoAliases);

            series.Id.Should().Be(132182);
            series.MatchedVia.Should().Be("primary");
        }

        [Test]
        public void a_typographic_apostrophe_is_folded_before_the_search()
        {
            // Audit §3: the stored name with U+2019 returned ZERO candidates; the ASCII form finds it.
            Given("Let's Do It Already!",
                M(120768, "Hayaku Shitai Futari", "Let's Do It Already!", Manga, Releasing, null, 6000));

            var series = Subject.FindSeries("Let’s Do It Already!", LibraryType.Manga, 9, NoAliases);

            series.Id.Should().Be(120768);
            series.MatchedVia.Should().Be("primary");
            _requests.Single().Variables.Search.Should().Be("Let's Do It Already!");
        }

        [Test]
        public void mushoku_tensei_binds_through_the_catalogue_alias_without_a_second_request()
        {
            // Audit §3: the stored name lacks the subtitle; rank 0 IS the right entry but is not
            // strict-equal. OpenTome's line 799116509 carries "Mushoku Tensei: Jobless
            // Reincarnation" as an alias (series_alias, read 2026-09-15); the provider puts the
            // de-slugged id first. Either strict-equals the entry already on the page.
            Given("Mushoku Tensei",
                M(85564, "Mushoku Tensei: Isekai Ittara Honki Dasu", "Mushoku Tensei: Jobless Reincarnation", Manga, Releasing, null, 60000, "Jobless Reincarnation"),
                M(104724, "Mushoku Tensei: Roxy datte Honki desu", "Mushoku Tensei: Roxy Gets Serious", Manga, Finished, 11, 4007),
                M(110000, "Mushoku Tensei: Isekai Ittara Honki Dasu - Eris the Goblin Slayer", null, Manga, Finished, 1, 800));

            var aliases = new[]
            {
                "Mushoku Tensei Jobless Reincarnation",      // the de-slugged foreign id
                "Mushoku Tensei",                             // OpenTome alias == the name (skipped)
                "Jobless Reincarnation",
                "Mushoku Tensei: Jobless Reincarnation"
            };

            var series = Subject.FindSeries("Mushoku Tensei", LibraryType.Manga, 24, aliases);

            series.Id.Should().Be(85564);
            series.MatchedVia.Should().Be("alias");
            _requests.Should().HaveCount(1);
        }

        [Test]
        public void re_zero_chapter_4_binds_through_the_de_slugged_id()
        {
            // Audit §3: OpenTome's parenthetical arc form vs AniList's "Chapter 4:" form; rank 0
            // IS the right entry. The line's series_alias rows (read 2026-09-15) carry no
            // "Chapter 4" form — only "The Sanctuary and the Witch of Greed", "Re:Zero", "Memory
            // Snow", "List of Re" … — so it is the entry's own de-slugged id that strict-equals.
            Given("Re:Zero (The Sanctuary and the Witch of Greed)",
                M(112218, "Re:Zero kara Hajimeru Isekai Seikatsu: Daiyonshou - Seiiki to Gouyoku no Majo", "Re:ZERO -Starting Life in Another World-, Chapter 4: The Sanctuary and the Witch of Greed", Manga, Finished, 11, 3000),
                M(85736, "Re:Zero kara Hajimeru Isekai Seikatsu: Daiisshou - Outo no Ichinichi-hen", "Re:ZERO -Starting Life in Another World- Chapter 1: A Day in the Capital", Manga, Finished, 2, 8412),
                M(118370, "Re:Zero kara Hajimeru Isekai Seikatsu: Hyouketsu no Kizuna", "Re:ZERO -Starting Life in Another World-, The Frozen Bond", Manga, Finished, 3, 1632));

            var aliases = new[]
            {
                "Rezero Starting Life In Another World Chapter 4 The Sanctuary And The Witch Of Greed",
                "The Sanctuary and the Witch of Greed",
                "List of Re:Zero volumes",
                "Re:Zero",
                "Memory Snow"
            };

            var series = Subject.FindSeries("Re:Zero (The Sanctuary and the Witch of Greed)", LibraryType.Manga, 11, aliases);

            series.Id.Should().Be(112218);
            series.MatchedVia.Should().Be("alias");
            _requests.Should().HaveCount(1);
        }

        [Test]
        public void a_light_novel_queries_novels_and_binds_the_novel_entry()
        {
            // Audit §3: the novel 51479 is unreachable under format_not: NOVEL; the light-novel
            // library queries format: NOVEL (D2) and the main line outranks Progressive.
            Given("Sword Art Online",
                M(51479, "Sword Art Online", "Sword Art Online", Novel, Releasing, null, 40000),
                M(87540, "Sword Art Online: Progressive", "Sword Art Online Progressive", Novel, Finished, 9, 9000));

            var series = Subject.FindSeries("Sword Art Online", LibraryType.LightNovel, 28, NoAliases);

            series.Id.Should().Be(51479);
            series.Format.Should().Be(Novel);
            series.MatchedVia.Should().Be("primary");
            _requests.Single().Query.Should().Contain("format: NOVEL");
            _requests.Single().Query.Should().NotContain("format_not");
        }

        [Test]
        public void a_manga_query_still_excludes_novels()
        {
            GivenFairyTailPage();

            Subject.FindSeries("Fairy Tail", LibraryType.Manga, 63, NoAliases);

            _requests.Single().Query.Should().Contain("type: MANGA, format_not: NOVEL");
            _requests.Single().Variables.PerPage.Should().Be(10);
        }

        // ---- D9: the entries the audit found RIGHT keep their id ----

        // Bound id + AniList title + popularity from the audit table; AniList volumes as AniList
        // carries them (null while RELEASING); the catalogue count from the OpenTome line column
        // as-is — Erased's 5 EN 2-in-1 books against AniList's 9, Vinland Saga's 15 against 29 are
        // the two collected-edition lines the one-sided rule exists for.
        // The page also carries the decoys the ranking exists for: a same-titled ONE_SHOT at
        // rank 0, a more popular anthology with the name as a synonym, and a subtitled spin-off.
        [TestCase("Attack on Titan", 53390, "Shingeki no Kyojin", "Attack on Titan", Finished, 34, 226876, 34)]
        [TestCase("Berserk", 30002, "Berserk", "Berserk", Releasing, null, 244979, 43)]
        [TestCase("Chainsaw Man", 105778, "Chainsaw Man", "Chainsaw Man", Releasing, null, 332163, 22)]
        [TestCase("Demon Slayer: Kimetsu no Yaiba", 87216, "Kimetsu no Yaiba", "Demon Slayer: Kimetsu no Yaiba", Finished, 23, 210127, 23)]
        [TestCase("Erased", 69325, "Boku dake ga Inai Machi", "Erased", Finished, 9, 29480, 5)]
        [TestCase("Frieren: Beyond Journey’s End", 118586, "Sousou no Frieren", "Frieren: Beyond Journey’s End", Releasing, null, 85291, 15)]
        [TestCase("Jujutsu Kaisen", 101517, "Jujutsu Kaisen", "Jujutsu Kaisen", Finished, 30, 252923, 30)]
        [TestCase("Kaiju No.8", 120760, "Kaijuu 8-gou", "Kaiju No. 8", Releasing, null, 106340, 16)]
        [TestCase("Kaiju No. 8: Relax", 177806, "Kaijuu 8-gou: Relax", "Kaiju No. 8: Relax", Finished, 3, 1088, 3)]
        [TestCase("My Hero Academia", 85486, "Boku no Hero Academia", "My Hero Academia", Finished, 42, 178102, 42)]
        [TestCase("Re:ZERO -Starting Life in Another World-, Chapter 3: Truth of Zero", 87259, "Re:Zero kara Hajimeru Isekai Seikatsu: Daisanshou - Truth of Zero", "Re:ZERO -Starting Life in Another World- Chapter 3: Truth of Zero", Finished, 11, 5413, 11)]
        [TestCase("Solo Leveling", 105398, "Na Honjaman Level Up", "Solo Leveling", Finished, 14, 284806, 15)]
        [TestCase("Tokyo Ghoul", 63327, "Tokyo Ghoul", "Tokyo Ghoul", Finished, 14, 196440, 14)]
        [TestCase("Vinland Saga", 30642, "Vinland Saga", "Vinland Saga", Finished, 29, 138273, 15)]
        [TestCase("[Oshi no Ko]", 117195, "[Oshi no Ko]", "[Oshi no Ko]", Finished, 16, 106680, 14)]
        [TestCase("your name.", 97337, "Kimi no Na wa.", "your name.", Finished, 3, 20186, 3)]
        public void an_entry_the_audit_found_right_keeps_its_id(string name, int id, string romaji, string english, string status, int? volumes, int popularity, int catalogueVolumes)
        {
            Given(name,
                M(id + 1, romaji, english, OneShot, Finished, 1, 50),
                M(id + 2, "Anthology " + id, null, Manga, Finished, null, popularity + 1000, name),
                M(id, romaji, english, Manga, status, volumes, popularity),
                M(id + 3, romaji + ": Gaiden", english + ": Side Story", Manga, Finished, 2, 500));

            var series = Subject.FindSeries(name, LibraryType.Manga, catalogueVolumes, NoAliases);

            series.Id.Should().Be(id);
            series.MatchedVia.Should().Be("primary");
            series.EnglishTitle.Should().Be(english);
            series.CoverImageUrl.Should().Be(Cover(id));
            series.Volumes.Should().Be(volumes);
            series.Status.Should().Be(status);
        }

        [Test]
        public void the_old_overload_is_the_manga_search_without_hints()
        {
            GivenFairyTailPage();

            var series = Subject.FindSeries("Fairy Tail");

            series.Id.Should().Be(30598);
            series.MatchedVia.Should().Be("primary");
        }

        // ---- the other rules ----

        // "apothecary diaries" alone is an article-only difference from "The Apothecary Diaries",
        // which R7 binds strictly (below); an extra word is the near-miss only the relaxed pass takes.
        [Test]
        public void the_strict_pass_never_takes_a_near_miss()
        {
            Given("apothecary diaries manga",
                M(99022, "Kusuriya no Hitorigoto", "The Apothecary Diaries", Manga, Releasing, null, 46645));

            Subject.FindSeries("apothecary diaries manga", LibraryType.Manga, null, NoAliases).Should().BeNull();
        }

        [Test]
        public void the_relaxed_pass_still_rejects_one_shots()
        {
            Given("apothecary diaries manga",
                M(1, "Kusuriya no Hitorigoto", "The Apothecary Diaries", OneShot, Finished, 1, 999999),
                M(99022, "Kusuriya no Hitorigoto", "The Apothecary Diaries", Manga, Releasing, null, 46645));

            var series = Subject.FindSeries("apothecary diaries manga", LibraryType.Manga, null, NoAliases, relaxed: true);

            series.Id.Should().Be(99022);
            series.MatchedVia.Should().Be("relaxed");
        }

        [Test]
        public void a_leading_article_is_a_strict_bind_before_the_relaxed_pass()
        {
            // R7 (OpenTome 2026-09-24): the ranker's fallback tiers run inside the strict pass on
            // every call, the search box's included; the relaxed pass only sees what they leave.
            Given("apothecary diaries",
                M(1, "Kusuriya no Hitorigoto", "The Apothecary Diaries", OneShot, Finished, 1, 999999),
                M(99022, "Kusuriya no Hitorigoto", "The Apothecary Diaries", Manga, Releasing, null, 46645));

            Subject.FindSeries("apothecary diaries", LibraryType.Manga, null, NoAliases).MatchedVia.Should().Be("article");
            Subject.FindSeries("apothecary diaries", LibraryType.Manga, null, NoAliases, relaxed: true).MatchedVia.Should().Be("article");
            _requests.Should().HaveCount(2);
        }

        // ---- OpenTome 2026-09-24 tiers through FindSeries (recorded pages, opentome
        // export/fixtures/anilist/) ----

        [Test]
        public void weed_binds_the_full_serial_through_the_ceiling_tier_before_any_alias_search()
        {
            Given("Weed",
                M(34010, "Ginga Densetsu WEED", "Ginga Legend Weed", Manga, Finished, 60, 630, "Silver Fang Legend Weed", "WEED"),
                M(36414, "Boku no Inu: Boku no Weed", null, Manga, Finished, 1, 77, "My Dog, My Weed"),
                M(48607, "Ginga Densetsu Weed Gaiden", null, Manga, Finished, 1, 124),
                M(45785, "Ginga Densetsu Weed: Orion", null, Manga, Finished, 30, 215, "Ginga Densetsu Wiido: Orion"));

            var series = Subject.FindSeries("Weed", LibraryType.Manga, 3, new[] { "Weed", "Ginga Dendetsu Weed", "Ginga Densetsu WEED" }, titleIsLineName: true);

            series.Id.Should().Be(34010);
            series.MatchedVia.Should().Be("ceiling");
            _requests.Should().HaveCount(1);
            _log.Logs.Should().Contain("Info|AniList match \"Weed\" -> 34010 \"Ginga Legend Weed\" (MANGA, 60 vols, pop 630) via ceiling");
        }

        [Test]
        public void hollow_regalia_binds_the_novel_through_the_article_tier()
        {
            Given("Hollow Regalia",
                M(133016, "Utsuro Naru Regalia", "The Hollow Regalia", Novel, Releasing, null, 270, "Corpse Reviewer"));

            var series = Subject.FindSeries("Hollow Regalia", LibraryType.LightNovel, 6, NoAliases);

            series.Id.Should().Be(133016);
            series.MatchedVia.Should().Be("article");
        }

        [Test]
        public void bookworm_part_2_binds_the_shorter_part_2_entry_through_the_substring_tier()
        {
            const string name = "Ascendance of a Bookworm (Part 2: Apprentice Shrine Maiden)";
            Given(name,
                M(110800, "Honzuki no Gekokujou: Shisho ni Naru Tame ni wa Shudan wo Erandeiraremasen Dai 2-bu - Shinden no Miko Minarai", "Ascendance of a Bookworm: Part 2", Novel, Finished, 4, 2916, "Honzuki no Gekokujou Part 2"));

            var series = Subject.FindSeries(name, LibraryType.LightNovel, 4, NoAliases, titleIsLineName: true);

            series.Id.Should().Be(110800);
            series.MatchedVia.Should().Be("substring");

            // Not in the catalogue: no count, nothing to be exact against.
            Subject.FindSeries(name, LibraryType.LightNovel, null, NoAliases, titleIsLineName: true).Should().BeNull();
        }

        // R4 / R5 read the catalogue count as the title's own: the provider says whether the title
        // IS the hinted line's name (CatalogueHint also finds a line by alias or subtitle).
        [Test]
        public void r5_never_binds_when_the_title_is_only_an_alias_of_the_hinted_line()
        {
            // Line "Yakin Shift" (3 volumes, alias "Night Shift"); the entry is "Night Shift".
            Given("Night Shift",
                M(50, "Night Shift: Tale of a Vampire", null, Manga, Finished, 3, 500));

            Subject.FindSeries("Night Shift", LibraryType.Manga, 3, NoAliases, titleIsLineName: false).Should().BeNull();
            Subject.FindSeries("Night Shift", LibraryType.Manga, 3, NoAliases, titleIsLineName: true).MatchedVia.Should().Be("substring");
        }

        [Test]
        public void r4_never_binds_when_the_title_is_only_an_alias_of_the_hinted_line()
        {
            // "Foo" is an alias of a 3-volume line; the 60-volume "Foo" is not that line's work.
            Given("Foo",
                M(2, "Foo", null, Manga, Finished, 60, 500));

            Subject.FindSeries("Foo", LibraryType.Manga, 3, NoAliases, titleIsLineName: false).Should().BeNull();
            Subject.FindSeries("Foo", LibraryType.Manga, 3, NoAliases, titleIsLineName: true).MatchedVia.Should().Be("ceiling");
        }

        [Test]
        public void the_default_is_not_the_line_name()
        {
            Given("Foo",
                M(2, "Foo", null, Manga, Finished, 60, 500));

            Subject.FindSeries("Foo", LibraryType.Manga, 3, NoAliases).Should().BeNull();
        }

        [Test]
        public void the_edition_stripped_name_takes_one_alias_search_slot_when_it_needs_a_fresh_search()
        {
            // R6 shares MaxAliasSearches (3) with the aliases: its fresh search leaves two, so the
            // third junk alias's page is never fetched and "Real" is never reached.
            Given("Real",
                M(9, "Real", null, Manga, Finished, 18, 500));

            Subject.FindSeries("Foo (VizBig edition)", LibraryType.Manga, 18, new[] { "Foo", "junk one", "junk two", "Real" }).Should().BeNull();
            _requests.Select(r => r.Variables.Search).Should().Equal("Foo (VizBig edition)", "Foo", "junk one", "junk two");

            // Without the stripped name the same aliases reach "Real" on the third search.
            _requests.Clear();
            Subject.FindSeries("Foo (VizBig edition)", LibraryType.Manga, 18, new[] { "junk one", "junk two", "Real" }).Id.Should().Be(9);
            _requests.Select(r => r.Variables.Search).Should().Equal("Foo (VizBig edition)", "junk one", "junk two", "Real");
        }

        [Test]
        public void the_edition_stripped_name_takes_no_search_when_it_binds_on_the_name_page()
        {
            Given("Foo (VizBig edition)",
                M(30676, "Foo", "Foo", Manga, Finished, 56, 30000));

            var series = Subject.FindSeries("Foo (VizBig edition)", LibraryType.Manga, 18, new[] { "Foo", "junk one" });

            series.Id.Should().Be(30676);
            series.MatchedVia.Should().Be("alias");
            _requests.Select(r => r.Variables.Search).Should().Equal("Foo (VizBig edition)");
        }

        [Test]
        public void the_edition_stripped_name_binds_like_an_alias()
        {
            // R6 as the provider hands it over: [de-slugged id, edition-stripped name, aliases…].
            // The de-slugged id normalizes to the name (no request); the stripped name misses the
            // name page and is searched for itself.
            Given("Inuyasha",
                M(30676, "InuYasha", "Inuyasha", Manga, Finished, 56, 30000));

            var series = Subject.FindSeries("Inuyasha (VizBig edition)", LibraryType.Manga, 18, new[] { "Inuyasha Vizbig Edition", "Inuyasha" });

            series.Id.Should().Be(30676);
            series.MatchedVia.Should().Be("alias");
            _requests.Select(r => r.Variables.Search).Should().Equal("Inuyasha (VizBig edition)", "Inuyasha");
        }

        [Test]
        public void the_edition_stripped_name_keeps_the_ceiling_so_short_stories_never_bind_the_serial()
        {
            // "Sailor Moon (Shinsōban short stories)", 2 volumes: its own entry is "Sailor Moon
            // Short Stories"; the bare "Sailor Moon" must not bind the 18-volume serial (R3). The
            // name is searched folded, and its de-slugged ASCII alias now keys like it (TitleFold,
            // 2026-09-24), so it is never searched.
            Given("Sailor Moon",
                M(30092, "Bishoujo Senshi Sailor Moon", "Sailor Moon", Manga, Finished, 18, 40000),
                M(38552, "Bishoujo Senshi Sailor Moon: Short Stories", "Sailor Moon Short Stories", Manga, Finished, 2, 3000));

            Subject.FindSeries("Sailor Moon (Shinsōban short stories)", LibraryType.Manga, 2, new[] { "Sailor Moon Shinsoban Short Stories", "Sailor Moon" }).Should().BeNull();
            _requests.Select(r => r.Variables.Search).Should().Equal("Sailor Moon (Shinsoban short stories)", "Sailor Moon");
        }

        // TitleFold (2026-09-24, OpenTome's fold()): "Ranma ½" is asked as AniList's own "Ranma 1/2"
        // and keys ranma12 on both sides, so the name binds on its own page.
        [Test]
        public void a_numeric_symbol_is_searched_and_matched_folded()
        {
            Given("Ranma 1/2",
                M(87227, "Ranma ½", "Ranma 1/2", Manga, Finished, 38, 20000));

            var series = Subject.FindSeries("Ranma ½", LibraryType.Manga, 38, NoAliases);

            series.Id.Should().Be(87227);
            _requests.Select(r => r.Variables.Search).Should().Equal("Ranma 1/2");
        }

        [Test]
        public void an_accented_name_is_searched_and_matched_folded()
        {
            Given("Fushigi Yugi",
                M(30468, "Fushigi Yûgi", "Fushigi Yugi: The Mysterious Play", Manga, Finished, 18, 9000));

            var series = Subject.FindSeries("Fushigi Yûgi", LibraryType.Manga, 18, NoAliases);

            series.Id.Should().Be(30468);
            _requests.Select(r => r.Variables.Search).Should().Equal("Fushigi Yugi");
        }

        // OpenTome's alias_terms seed (2026-09-24): the catalogue's lower-case ASCII alias row of the
        // name ("fushigi y gi", "ranma") and the name's de-slugged form are the name, never a fresh
        // search -- without it the numeric fold would turn "Ranma ½"'s "ranma" row into one.
        [Test]
        public void a_stored_ascii_copy_of_the_name_never_burns_an_alias_search()
        {
            Subject.FindSeries("Fushigi Yûgi", LibraryType.Manga, 18, new[] { "fushigi y gi", "fushigi yugi", "Fushigi Yūgi", "Curious Play" }).Should().BeNull();
            _requests.Select(r => r.Variables.Search).Should().Equal("Fushigi Yugi", "Curious Play");

            _requests.Clear();

            Subject.FindSeries("Ranma ½", LibraryType.Manga, 38, new[] { "ranma", "ranma 1 2", "Ranma Nibun-no-Ichi" }).Should().BeNull();
            _requests.Select(r => r.Variables.Search).Should().Equal("Ranma 1/2", "Ranma Nibun-no-Ichi");
        }

        [Test]
        public void an_alias_that_matches_nothing_on_the_page_is_searched_for_itself_up_to_the_cap()
        {
            Given("Hayaku Shitai Futari",
                M(120768, "Hayaku Shitai Futari", "Let's Do It Already!", Manga, Releasing, null, 6000));

            // Three junk aliases spend the cap before the real one is reached.
            Subject.FindSeries("Warum warten?", LibraryType.Manga, 9, new[] { "junk one", "junk two", "junk three", "Hayaku Shitai Futari" }).Should().BeNull();
            _requests.Should().HaveCount(1 + AniListService.MaxAliasSearches);

            _requests.Clear();

            var series = Subject.FindSeries("Warum warten?", LibraryType.Manga, 9, new[] { "Hayaku Shitai Futari" });

            series.Id.Should().Be(120768);
            series.MatchedVia.Should().Be("alias");
            _requests.Select(r => r.Variables.Search).Should().Equal("Warum warten?", "Hayaku Shitai Futari");
        }

        [Test]
        public void an_alias_retry_keeps_the_4x_ceiling_for_a_tiny_line()
        {
            // R3: the 2-volume spin-off line must not bind the 34-volume main serial through the
            // bare franchise alias — neither against the name's page nor the alias's own search —
            // while the same serial binds a 2-volume line whose OWN name it equals.
            var serial = M(53390, "Shingeki no Kyojin", "Attack on Titan", Manga, Finished, 34, 226876);
            Given("Attack on Titan: Harsh Mistress of the City", serial);
            Given("Shingeki no Kyojin", serial);

            Subject.FindSeries("Attack on Titan: Harsh Mistress of the City", LibraryType.Manga, 2, new[] { "Shingeki no Kyojin" }).Should().BeNull();
            _requests.Select(r => r.Variables.Search).Should().Equal("Attack on Titan: Harsh Mistress of the City", "Shingeki no Kyojin");

            Subject.FindSeries("Shingeki no Kyojin", LibraryType.Manga, 2, NoAliases).Id.Should().Be(53390);
        }

        [Test]
        public void get_by_id_fetches_the_entry_as_is()
        {
            GivenFairyTailPage();

            var series = Subject.GetById(30598);

            series.Id.Should().Be(30598);
            series.MatchedVia.Should().Be("id");
            series.EnglishTitle.Should().Be("Fairy Tail");
            series.Volumes.Should().Be(63);
            series.Popularity.Should().Be(57000);
            _requests.Single().Variables.Id.Should().Be(30598);
            _requests.Single().Query.Should().StartWith("query ($id: Int) { Media(id: $id, type: MANGA)");
        }

        [Test]
        public void get_by_id_returns_null_for_an_id_anilist_does_not_know()
        {
            Subject.GetById(1).Should().BeNull();
        }

        [Test]
        public void candidates_are_the_raw_page_in_anilist_order()
        {
            Given("Blue Box",
                M(122342, "Ao no Hako", null, OneShot, Finished, null, 8511, "Blue Box"),
                M(132182, "Ao no Hako", "Blue Box", Manga, Releasing, null, 55252));

            var page = Subject.Candidates("Blue Box", LibraryType.Manga);

            page.Select(c => c.Id).Should().Equal(122342, 132182);
            page[0].Format.Should().Be(OneShot);
            page[1].DisplayTitle.Should().Be("Blue Box");
            page[1].Description.Should().Be("About Blue Box\nSecond line.");
            _requests.Single().Variables.PerPage.Should().Be(10);
        }

        [Test]
        public void a_transport_failure_is_fail_soft_everywhere_and_warns()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Post(It.IsAny<HttpRequest>()))
                  .Throws(new WebException("boom"));

            Subject.FindSeries("Fairy Tail", LibraryType.Manga, 63, NoAliases).Should().BeNull();
            Subject.GetById(30598).Should().BeNull();
            Subject.Candidates("Fairy Tail", LibraryType.Manga).Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(3);
        }

        [Test]
        public void a_rate_limit_answer_is_a_warn_not_a_miss()
        {
            // SuppressHttpError means a 429 never throws; it must not read as "no match".
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Post(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, "{\"errors\":[{\"message\":\"Too Many Requests\",\"status\":429}]}", HttpStatusCode.TooManyRequests));

            Subject.FindSeries("Fairy Tail", LibraryType.Manga, 63, NoAliases).Should().BeNull();

            _log.Logs.Should().Contain("Warn|AniList returned 429 for 'Fairy Tail'; keeping local data");
            _log.Logs.Should().NotContain(l => l.Contains("no match"));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void an_unknown_id_is_a_404_and_only_a_debug_line()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Post(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, "{\"data\":{\"Media\":null},\"errors\":[{\"message\":\"Not Found.\",\"status\":404}]}", HttpStatusCode.NotFound));

            Subject.GetById(1).Should().BeNull();
        }

        // ---- D7: the decisions are visible at Info ----

        [Test]
        public void a_match_logs_the_decision_at_info()
        {
            GivenFairyTailPage();

            Subject.FindSeries("Fairy Tail", LibraryType.Manga, 63, NoAliases);

            _log.Logs.Should().Contain("Info|AniList match \"Fairy Tail\" -> 30598 \"Fairy Tail\" (MANGA, 63 vols, pop 57000) via primary");
        }

        [Test]
        public void a_page_with_nothing_eligible_logs_every_rejection_at_info()
        {
            Given("Fairy Tail",
                M(128087, "Pantsu Agerune", null, Manga, Finished, 1, 39, "Fairy Tail"),
                M(80035, "FAIRY TAIL ZERO", "Fairy Tail Zero", Manga, Finished, 1, 8000));

            Subject.FindSeries("Fairy Tail", LibraryType.Manga, 63, NoAliases).Should().BeNull();

            _log.Logs.Should().Contain("Info|AniList rejected \"Fairy Tail\": 2 candidate(s) — 128087 \"Pantsu Agerune\": 1 vols vs catalogue 63; 80035 \"Fairy Tail Zero\": 1 vols vs catalogue 63");
        }

        [Test]
        public void an_empty_page_logs_no_match_at_info()
        {
            Subject.FindSeries("Nothing Here", LibraryType.Manga, null, NoAliases).Should().BeNull();

            _log.Logs.Should().Contain("Info|AniList no match \"Nothing Here\"");
        }

        [Test]
        public void the_relaxed_pass_logs_its_decisions_below_info()
        {
            GivenFairyTailPage();
            Given("apothecary diaries manga",
                M(99022, "Kusuriya no Hitorigoto", "The Apothecary Diaries", Manga, Releasing, null, 46645));
            Given("Lone Shot",
                M(1, "Lone Shot", null, OneShot, Finished, 1, 10));

            // A strict hit, a relaxed hit, a page with only rejections, and an empty page.
            Subject.FindSeries("Fairy Tail", LibraryType.Manga, 63, NoAliases, relaxed: true).Id.Should().Be(30598);
            Subject.FindSeries("apothecary diaries manga", LibraryType.Manga, null, NoAliases, relaxed: true).MatchedVia.Should().Be("relaxed");
            Subject.FindSeries("Lone Shot", LibraryType.Manga, null, NoAliases, relaxed: true).Should().BeNull();
            Subject.FindSeries("Nothing Here", LibraryType.Manga, null, NoAliases, relaxed: true).Should().BeNull();

            _log.Logs.Should().NotContain(line => line.StartsWith("Info|AniList"));
        }

        // ---- wire shapes (serialised camelCase by Json.ToJson; the service's DTOs read them) ----

        public class Media
        {
            public int Id { get; set; }
            public string Format { get; set; }
            public string Status { get; set; }
            public int? Volumes { get; set; }
            public int? Chapters { get; set; }
            public int? AverageScore { get; set; } = 75;
            public int? Popularity { get; set; }
            public string Description { get; set; }
            public List<string> Synonyms { get; set; } = new List<string>();
            public MediaDate StartDate { get; set; } = new MediaDate { Year = 2010 };
            public MediaTitle Title { get; set; }
            public MediaCover CoverImage { get; set; }
        }

        public class MediaDate
        {
            public int? Year { get; set; }
        }

        public class MediaTitle
        {
            public string Romaji { get; set; }
            public string English { get; set; }
            public string Native { get; set; }
        }

        public class MediaCover
        {
            public string ExtraLarge { get; set; }
            public string Large { get; set; }
        }

        private class Probe
        {
            public string Query { get; set; }
            public ProbeVariables Variables { get; set; }
        }

        private class ProbeVariables
        {
            public string Search { get; set; }
            public int? PerPage { get; set; }
            public int? Id { get; set; }
        }
    }
}
