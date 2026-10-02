using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    [TestFixture]
    public class MangaSeriesMetadataProviderFixture : CoreTest<MangaSeriesMetadataProvider>
    {
        // Staging 2026-09-15: AniList is queried manga-only (format_not: NOVEL), so for a light
        // novel its relaxed match can only name the manga of the work -- "Sword Art Online" came
        // back as "Sword Art Online Progressive" and bound the 9-volume Progressive novel line
        // instead of the 28-volume main line the raw name ranks to.
        private const string RawName = "Sword Art Online";
        private const string AniListTitle = "Sword Art Online Progressive";

        private static readonly GcdSeries MainLine = Line(1, "Sword Art Online", 28);
        private static readonly GcdSeries ProgressiveLine = Line(2, "Sword Art Online Progressive", 9);

        private static GcdSeries Line(int id, string name, int volumes)
        {
            return new GcdSeries
            {
                GcdSeriesId = id,
                Name = name,
                Language = "en",
                VolumeCount = volumes
            };
        }

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IGcdMetadataService>()
                  .SetupGet(s => s.Available)
                  .Returns(true);

            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetVolumes(It.IsAny<int>()))
                  .Returns(new List<GcdVolume>());

            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetAliases(It.IsAny<int>()))
                  .Returns(new List<string>());

            GivenAniList(new AniListSeries { EnglishTitle = AniListTitle });

            // Audiobook identity B1 (2026-09-17): Audible answers (with nothing) unless a test says
            // otherwise — a loose mock's null would be "did not answer" and Warn.
            Mocker.GetMock<IAudibleCatalogService>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                  .Returns(new List<AudibleProduct>());

            _log = new MemoryTarget("volume-metadata") { Layout = "${level}|${message}" };
            LogManager.Configuration.AddTarget(_log.Name, _log);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Info, _log));
            LogManager.ReconfigExistingLoggers();
        }

        private MemoryTarget _log;

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

        // The provider now calls the hinted overload (library, catalogue volume count, aliases).
        private void GivenAniList(AniListSeries series)
        {
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.FindSeries(It.IsAny<string>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>()))
                  .Returns(series);
        }

        private static AniListSeries Bound(int id, string via, string english, string format = "MANGA")
        {
            return new AniListSeries
            {
                Id = id,
                EnglishTitle = english,
                Format = format,
                MatchedVia = via,
                CoverImageUrl = $"https://s4.anilist.co/bx{id}.jpg",
                Description = "About " + english
            };
        }

        private void VerifyNoSearch()
        {
            Mocker.GetMock<IAniListService>()
                  .Verify(s => s.FindSeries(It.IsAny<string>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
        }

        // KR/CN piece 2 (2026-10-02, M4): the hint line's medium is the expected origin.
        private void VerifyExpectedOrigin(string origin)
        {
            Mocker.GetMock<IAniListService>()
                  .Verify(s => s.FindSeries(It.IsAny<string>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<bool>(), It.IsAny<bool>(), origin), Times.Once());
        }

        // KR/CN piece 2 (2026-10-02, M6): the live sources' counts for the total tests.
        private void GivenCounts(int mangaDexHighest, int mangaUpdatesVolumes, bool mangaUpdatesCompleted = false)
        {
            Mocker.GetMock<IMangaDexService>()
                  .Setup(s => s.Lookup(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(new MangaDexResult { HighestVolume = mangaDexHighest });
            Mocker.GetMock<IMangaUpdatesService>()
                  .Setup(s => s.FindSeries(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(mangaUpdatesVolumes > 0 ? new MangaUpdatesSeries { VolumeCount = mangaUpdatesVolumes, Completed = mangaUpdatesCompleted } : null);
        }

        private static GcdSeries ComicLine(int id, string name, int volumes, string medium)
        {
            var line = Line(id, name, volumes);
            line.Medium = medium;
            return line;
        }

        private void GivenCatalogueLine(string title, LibraryType library, GcdSeries line)
        {
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.FindSeriesByTitle(title, library))
                  .Returns(line);
        }

        [Test]
        public void light_novel_binds_the_raw_name_line_before_the_anilist_title()
        {
            GivenCatalogueLine(RawName, LibraryType.LightNovel, MainLine);
            GivenCatalogueLine(AniListTitle, LibraryType.LightNovel, ProgressiveLine);

            var series = Subject.GetSeries(RawName, 0, resolveVolumeDetails: false, LibraryType.LightNovel);

            series.NotInCatalogue.Should().BeFalse();
            series.VolumeCount.Should().Be(28);
            series.DisplayName.Should().Be("Sword Art Online");

            Mocker.GetMock<IGcdMetadataService>()
                  .Verify(s => s.FindSeriesByTitle(AniListTitle, LibraryType.LightNovel), Times.Never());
        }

        [Test]
        public void light_novel_falls_back_to_the_anilist_title_when_the_raw_name_misses()
        {
            GivenCatalogueLine(AniListTitle, LibraryType.LightNovel, ProgressiveLine);

            var series = Subject.GetSeries(RawName, 0, resolveVolumeDetails: false, LibraryType.LightNovel);

            series.NotInCatalogue.Should().BeFalse();
            series.VolumeCount.Should().Be(9);
            series.DisplayName.Should().Be(AniListTitle);

            Mocker.GetMock<IGcdMetadataService>()
                  .Verify(s => s.FindSeriesByTitle(RawName, LibraryType.LightNovel), Times.Once());
        }

        [Test]
        public void light_novel_display_name_drops_the_catalogue_qualifier()
        {
            // OpenTome disambiguates a novel line from the manga of the same name in its NAME
            // ("Overlord (novel series)"); the ~ln library already does that, so the qualifier must
            // not become the series name, the pin key or the folder on disk.
            GivenAniList(new AniListSeries { EnglishTitle = "Overlord" });
            GivenCatalogueLine("Overlord", LibraryType.LightNovel, Line(3, "Overlord (novel series)", 16));

            var series = Subject.GetSeries("Overlord", 0, resolveVolumeDetails: false, LibraryType.LightNovel);

            series.VolumeCount.Should().Be(16);
            series.DisplayName.Should().Be("Overlord");
        }

        [Test]
        public void light_novel_fallback_display_name_drops_the_catalogue_qualifier()
        {
            // Raw (folder-derived) name misses; AniList supplied only a romaji title, so the
            // catalogue name is adopted through the AniList-title fallback -- qualifier stripped.
            GivenAniList(new AniListSeries { RomajiTitle = "Overlord" });
            GivenCatalogueLine("Overlord", LibraryType.LightNovel, Line(3, "Overlord (novel series)", 16));

            var series = Subject.GetSeries("Overlord Light Novel", 0, resolveVolumeDetails: false, LibraryType.LightNovel);

            series.VolumeCount.Should().Be(16);
            series.DisplayName.Should().Be("Overlord");

            Mocker.GetMock<IGcdMetadataService>()
                  .Verify(s => s.FindSeriesByTitle("Overlord Light Novel", LibraryType.LightNovel), Times.Once());
        }

        [Test]
        public void light_novel_parent_name_drops_the_catalogue_qualifier()
        {
            // Collection membership: the child's ParentName must slug to the same id the parent
            // line mints for itself (local-overlord~ln), so it sheds the qualifier the same way.
            var child = Line(5, "Overlord: The Undead King Oh!", 3);
            child.ParentSeriesId = 3;
            GivenCatalogueLine("Overlord: The Undead King Oh!", LibraryType.LightNovel, child);
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.FindSeriesById(3))
                  .Returns(Line(3, "Overlord (novel series)", 16));

            var series = Subject.GetSeries("Overlord: The Undead King Oh!", 0, resolveVolumeDetails: false, LibraryType.LightNovel);

            series.ParentName.Should().Be("Overlord");
        }

        [Test]
        public void light_novel_keeps_a_parenthetical_that_is_part_of_the_title()
        {
            GivenCatalogueLine("Re:ZERO", LibraryType.LightNovel, Line(4, "Re:ZERO (Starting Life in Another World)", 26));

            var series = Subject.GetSeries("Re:ZERO", 0, resolveVolumeDetails: false, LibraryType.LightNovel);

            series.DisplayName.Should().Be("Re:ZERO (Starting Life in Another World)");
        }

        [TestCase("Overlord (novel series)", "Overlord")]
        [TestCase("Overlord (Light Novel)", "Overlord")]
        [TestCase("KonoSuba (light novel series)", "KonoSuba")]
        [TestCase("Spice and Wolf (novel)", "Spice and Wolf")]
        [TestCase("86 (novels)", "86")]
        [TestCase("Sword Art Online", "Sword Art Online")]
        [TestCase("Re:ZERO (Starting Life in Another World)", "Re:ZERO (Starting Life in Another World)")]
        [TestCase("Overlord (novel series) Side Stories", "Overlord (novel series) Side Stories")]
        public void strip_light_novel_qualifier(string name, string expected)
        {
            MangaSeriesMetadataProvider.StripLightNovelQualifier(name).Should().Be(expected);
        }

        [Test]
        public void light_novel_missing_on_both_lookups_is_not_in_catalogue()
        {
            var series = Subject.GetSeries(RawName, 0, resolveVolumeDetails: false, LibraryType.LightNovel);

            series.NotInCatalogue.Should().BeTrue();
            series.VolumeCount.Should().Be(0);
        }

        [Test]
        public void manga_binds_the_anilist_title_line_before_the_raw_name()
        {
            GivenCatalogueLine(RawName, LibraryType.Manga, MainLine);
            GivenCatalogueLine(AniListTitle, LibraryType.Manga, ProgressiveLine);

            var series = Subject.GetSeries(RawName, 0, resolveVolumeDetails: false, LibraryType.Manga);

            series.VolumeCount.Should().Be(9);
            series.DisplayName.Should().Be(AniListTitle);

            // The raw-name line is consulted once BEFORE the AniList call, as the ranking hint
            // (volume count, aliases, catalogue id); the structure still binds to the AniList-title line.
            Mocker.GetMock<IGcdMetadataService>()
                  .Verify(s => s.FindSeriesByTitle(RawName, LibraryType.Manga), Times.Once());
        }

        [Test]
        public void manga_keeps_the_anilist_title_when_the_raw_name_line_is_the_one_that_matched()
        {
            GivenCatalogueLine(RawName, LibraryType.Manga, MainLine);

            var series = Subject.GetSeries(RawName, 0, resolveVolumeDetails: false, LibraryType.Manga);

            series.VolumeCount.Should().Be(28);
            series.DisplayName.Should().Be(AniListTitle);
        }

        // Line safety (2026-09-28): Switch Line writes the line's name and AniList id and nothing else holds an
        // English series to a line -- the refresh resolves it again. Proven both ways and twice (a second
        // refresh must not drift back): a light novel binds the line its name finds, even when AniList's
        // (manga) title names the other line.
        [TestCase(Gcd.OtomeLines.MainName, Gcd.OtomeLines.MainId)]
        [TestCase(Gcd.OtomeLines.SpinOffName, Gcd.OtomeLines.SpinOffId)]
        public void a_switched_light_novel_binds_its_new_line_on_every_refresh(string name, string tomeLineId)
        {
            GivenCatalogueLine(Gcd.OtomeLines.MainName, LibraryType.LightNovel, Gcd.OtomeLines.MainLine());
            GivenCatalogueLine(Gcd.OtomeLines.SpinOffName, LibraryType.LightNovel, Gcd.OtomeLines.SpinOff());
            Mocker.GetMock<IAniListService>().Setup(s => s.GetById(It.IsAny<int>())).Returns(Bound(101, "id", Gcd.OtomeLines.SpinOffName));

            var first = Subject.GetSeries(name, 0, resolveVolumeDetails: false, LibraryType.LightNovel, anilistId: 101);
            var second = Subject.GetSeries(name, 0, resolveVolumeDetails: false, LibraryType.LightNovel, anilistId: 101);

            first.TomeLineId.Should().Be(tomeLineId);
            second.TomeLineId.Should().Be(tomeLineId);
        }

        // A manga series is resolved through its AniList title first: the switch writes the line's AniList id,
        // whose title finds the line.
        [Test]
        public void a_switched_manga_binds_its_new_line_through_the_lines_anilist_id()
        {
            var main = new GcdSeries { GcdSeriesId = 1, Name = "Kaiju No. 8", Language = "en", VolumeCount = 12, TomeId = "rl_kaiju" };
            var relax = new GcdSeries { GcdSeriesId = 2, Name = "Kaiju No. 8: Relax", Language = "en", VolumeCount = 2, TomeId = "rl_relax" };
            GivenCatalogueLine("Kaiju No. 8", LibraryType.Manga, main);
            GivenCatalogueLine("Kaiju No. 8: Relax", LibraryType.Manga, relax);
            Mocker.GetMock<IAniListService>().Setup(s => s.GetById(1002)).Returns(Bound(1002, "id", "Kaiju No. 8: Relax"));

            var first = Subject.GetSeries("Kaiju No. 8: Relax", 0, resolveVolumeDetails: false, LibraryType.Manga, anilistId: 1002);
            var second = Subject.GetSeries("Kaiju No. 8: Relax", 0, resolveVolumeDetails: false, LibraryType.Manga, anilistId: 1002);

            first.TomeLineId.Should().Be("rl_relax");
            second.TomeLineId.Should().Be("rl_relax");
        }

        // ---- binding (D1-D4) ----

        [Test]
        public void a_stored_id_is_fetched_as_is_and_the_search_is_skipped()
        {
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.GetById(30598))
                  .Returns(Bound(30598, "id", "Fairy Tail"));

            var series = Subject.GetSeries("Fairy Tail", 0, resolveVolumeDetails: true, LibraryType.Manga, anilistId: 30598);

            series.AniListId.Should().Be(30598);
            series.MatchedVia.Should().Be("id");
            series.DisplayName.Should().Be("Fairy Tail");
            series.CoverUrl.Should().Be("https://s4.anilist.co/bx30598.jpg");
            series.Overview.Should().Be("About Fairy Tail");
            VerifyNoSearch();
        }

        [Test]
        public void a_stored_id_that_does_not_resolve_keeps_the_entry_unbound_for_this_pass_and_never_searches()
        {
            var series = Subject.GetSeries("Fairy Tail", 0, resolveVolumeDetails: true, LibraryType.Manga, anilistId: 30598);

            series.AniListId.Should().BeNull();
            series.MatchedVia.Should().BeNull();
            series.DisplayName.Should().Be("Fairy Tail");
            VerifyNoSearch();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_stored_id_of_the_other_medium_is_kept_with_a_warning()
        {
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.GetById(51479))
                  .Returns(Bound(51479, "id", "Sword Art Online", "NOVEL"));

            var series = Subject.GetSeries(RawName, 0, resolveVolumeDetails: true, LibraryType.Manga, anilistId: 51479);

            series.AniListId.Should().Be(51479);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_catalogue_line_with_an_anilist_id_short_circuits_the_search()
        {
            var line = Line(7, "Fairy Tail", 63);
            line.AnilistId = 30598;
            GivenCatalogueLine("Fairy Tail", LibraryType.Manga, line);
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.GetById(30598))
                  .Returns(Bound(30598, "id", "Fairy Tail"));

            var series = Subject.GetSeries("Fairy Tail", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.AniListId.Should().Be(30598);
            series.MatchedVia.Should().Be("catalogue");
            series.VolumeCount.Should().Be(63);
            VerifyNoSearch();
        }

        [Test]
        public void a_catalogue_id_that_does_not_resolve_falls_through_to_the_search()
        {
            var line = Line(7, "Fairy Tail", 63);
            line.AnilistId = 30598;
            GivenCatalogueLine("Fairy Tail", LibraryType.Manga, line);
            GivenAniList(Bound(30598, "primary", "Fairy Tail"));

            var series = Subject.GetSeries("Fairy Tail", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.AniListId.Should().Be(30598);
            series.MatchedVia.Should().Be("primary");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void the_ranked_search_gets_the_catalogue_volume_count_then_the_id_name_then_the_aliases()
        {
            GivenCatalogueLine("Mushoku Tensei", LibraryType.Manga, Line(8, "Mushoku Tensei", 24));
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetAliases(8))
                  .Returns(new List<string> { "Mushoku Tensei: Jobless Reincarnation", "Jobless Reincarnation" });
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.FindSeries("Mushoku Tensei",
                                           LibraryType.Manga,
                                           24,
                                           It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "Mushoku Tensei Jobless Reincarnation", "Mushoku Tensei: Jobless Reincarnation", "Jobless Reincarnation" })),
                                           false, It.IsAny<bool>(), null))
                  .Returns(Bound(85564, "alias", "Mushoku Tensei: Jobless Reincarnation"));

            var series = Subject.GetSeries("Mushoku Tensei", 0, resolveVolumeDetails: true, LibraryType.Manga, null, "Mushoku Tensei Jobless Reincarnation");

            series.AniListId.Should().Be(85564);
            series.MatchedVia.Should().Be("alias");
            series.DisplayName.Should().Be("Mushoku Tensei: Jobless Reincarnation");
            series.VolumeCount.Should().Be(24);
        }

        [Test]
        public void no_catalogue_line_means_no_volume_count_and_only_the_id_name_as_alias()
        {
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.FindSeries("Kaiju No.8", LibraryType.Manga, null, It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "Kaiju No 8" })), false, It.IsAny<bool>(), It.IsAny<string>()))
                  .Returns(Bound(120760, "primary", "Kaiju No. 8"));

            var series = Subject.GetSeries("Kaiju No.8", 0, resolveVolumeDetails: true, LibraryType.Manga, null, "Kaiju No 8");

            series.AniListId.Should().Be(120760);
        }

        // AniListRanker's fallback tiers (OpenTome 2026-09-24) are strict rules: they bind.
        [TestCase("article")]
        [TestCase("substring")]
        [TestCase("ceiling")]
        public void a_fallback_tier_hit_is_pinned(string via)
        {
            GivenAniList(Bound(34010, via, "Ginga Legend Weed"));

            var series = Subject.GetSeries("Weed", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.AniListId.Should().Be(34010);
            series.MatchedVia.Should().Be(via);
        }

        [Test]
        public void the_edition_stripped_name_rides_between_the_id_name_and_the_catalogue_aliases()
        {
            // R6: "Inuyasha (VizBig edition)" -> "Inuyasha", after the de-slugged id, before the
            // line's series_alias rows.
            GivenCatalogueLine("Inuyasha (VizBig edition)", LibraryType.Manga, Line(12, "Inuyasha (VizBig edition)", 18));
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetAliases(12))
                  .Returns(new List<string> { "InuYasha: A Feudal Fairy Tale" });
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.FindSeries("Inuyasha (VizBig edition)",
                                           LibraryType.Manga,
                                           18,
                                           It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "Inuyasha Vizbig Edition", "Inuyasha", "InuYasha: A Feudal Fairy Tale" })),
                                           false, It.IsAny<bool>(), null))
                  .Returns(Bound(30676, "alias", "Inuyasha"));

            var pick = Subject.ResolveAniList("Inuyasha (VizBig edition)", LibraryType.Manga, "Inuyasha Vizbig Edition");

            pick.Id.Should().Be(30676);
        }

        // R4 / R5 run only when the entry name IS the hinted line's name: CatalogueHint also finds
        // a line by one of its aliases or by subtitle, and then the line's count is not the name's.
        [TestCase("Weed", "Weed", true)]
        [TestCase("weed", "WEED", true)]
        [TestCase("Overlord", "Overlord (novel series)", true)]
        [TestCase("Ascendance of a Bookworm (Part 2: Apprentice Shrine Maiden)", "Ascendance of a Bookworm (Part 2: Apprentice Shrine Maiden)", true)]
        [TestCase("Fushigi Yugi", "Fushigi Y\u00fbgi", true)] // TitleFold (2026-09-24): the accent-only spelling is the line's name
        [TestCase("Ranma 1/2", "Ranma \u00bd", true)]
        [TestCase("Ranma", "Ranma \u00bd", false)]         // before the fold "Ranma ½" keyed ranma, and R4/R5 ran for a bare "Ranma"
        [TestCase("Night Shift", "Yakin Shift", false)]
        [TestCase("Foo: The Arc", "Foo", false)]
        [TestCase("", "Foo", false)]
        public void the_line_name_test_compares_keys_with_and_without_the_light_novel_qualifier(string name, string lineName, bool expected)
        {
            MangaSeriesMetadataProvider.IsLineName(name, Line(1, lineName, 3)).Should().Be(expected);
        }

        [Test]
        public void no_hint_is_never_the_line_name()
        {
            MangaSeriesMetadataProvider.IsLineName("Weed", null).Should().BeFalse();
        }

        private void VerifyLineNameFlag(string name, LibraryType library, int count, bool expected)
        {
            Mocker.GetMock<IAniListService>()
                  .Verify(s => s.FindSeries(name, library, count, It.IsAny<IReadOnlyList<string>>(), false, expected, It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void a_hint_found_by_the_lines_own_name_lets_the_fallback_tiers_run()
        {
            GivenCatalogueLine("Weed", LibraryType.Manga, Line(20, "Weed", 3));

            Subject.ResolveAniList("Weed", LibraryType.Manga);

            VerifyLineNameFlag("Weed", LibraryType.Manga, 3, true);
        }

        [Test]
        public void a_light_novel_hint_found_by_its_name_without_the_qualifier_lets_the_fallback_tiers_run()
        {
            GivenCatalogueLine("Overlord", LibraryType.LightNovel, Line(21, "Overlord (novel series)", 16));

            Subject.ResolveAniList("Overlord", LibraryType.LightNovel);

            VerifyLineNameFlag("Overlord", LibraryType.LightNovel, 16, true);
        }

        [Test]
        public void a_hint_found_by_alias_keeps_the_fallback_tiers_off()
        {
            // FindSeriesByTitle matches series_alias: "Night Shift" finds the line "Yakin Shift".
            GivenCatalogueLine("Night Shift", LibraryType.Manga, Line(22, "Yakin Shift", 3));

            Subject.ResolveAniList("Night Shift", LibraryType.Manga);

            VerifyLineNameFlag("Night Shift", LibraryType.Manga, 3, false);
        }

        [Test]
        public void a_hint_found_by_subtitle_keeps_the_fallback_tiers_off()
        {
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.FindSeriesBySubtitle("Foo: The Arc", LibraryType.Manga))
                  .Returns(Line(23, "Foo", 5));

            Subject.ResolveAniList("Foo: The Arc", LibraryType.Manga);

            VerifyLineNameFlag("Foo: The Arc", LibraryType.Manga, 5, false);
        }

        [Test]
        public void a_relaxed_hit_is_never_pinned()
        {
            GivenAniList(Bound(99022, "relaxed", "The Apothecary Diaries"));

            var series = Subject.GetSeries("apothecary diaries", 0, resolveVolumeDetails: false, LibraryType.Manga);

            series.DisplayName.Should().Be("The Apothecary Diaries");
            series.AniListId.Should().BeNull();
            series.MatchedVia.Should().Be("relaxed");
        }

        [Test]
        public void a_light_novel_is_searched_in_the_light_novel_library_with_its_line_count()
        {
            GivenCatalogueLine(RawName, LibraryType.LightNovel, MainLine);
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.FindSeries(RawName, LibraryType.LightNovel, 28, It.IsAny<IReadOnlyList<string>>(), false, It.IsAny<bool>(), It.IsAny<string>()))
                  .Returns(Bound(51479, "primary", "Sword Art Online", "NOVEL"));

            var series = Subject.GetSeries(RawName, 0, resolveVolumeDetails: true, LibraryType.LightNovel);

            series.AniListId.Should().Be(51479);
            series.MatchedVia.Should().Be("primary");
            series.VolumeCount.Should().Be(28);
            series.DisplayName.Should().Be("Sword Art Online");
            series.CoverUrl.Should().Be("https://s4.anilist.co/bx51479.jpg");
        }

        [Test]
        public void a_right_manga_entry_resolves_to_the_same_id_and_line_as_before()
        {
            // D9 at the provider: the strict pick is the bound id, the structure binds to the
            // line the AniList English title names, and the presentation is the pick's.
            GivenCatalogueLine("Attack on Titan", LibraryType.Manga, Line(9, "Attack on Titan", 34));
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.FindSeries("Attack on Titan", LibraryType.Manga, 34, It.IsAny<IReadOnlyList<string>>(), false, It.IsAny<bool>(), It.IsAny<string>()))
                  .Returns(Bound(53390, "primary", "Attack on Titan"));

            var series = Subject.GetSeries("Attack on Titan", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.AniListId.Should().Be(53390);
            series.MatchedVia.Should().Be("primary");
            series.DisplayName.Should().Be("Attack on Titan");
            series.VolumeCount.Should().Be(34);
            series.CoverUrl.Should().Be("https://s4.anilist.co/bx53390.jpg");
            series.Overview.Should().Be("About Attack on Titan");
        }

        [Test]
        public void forget_lookup_drops_the_cached_search_box_answer()
        {
            GivenAniList(Bound(1, "relaxed", "First"));
            Subject.GetSeries("x", 0, resolveVolumeDetails: false, LibraryType.Manga).DisplayName.Should().Be("First");

            GivenAniList(Bound(2, "relaxed", "Second"));
            Subject.GetSeries("x", 0, resolveVolumeDetails: false, LibraryType.Manga).DisplayName.Should().Be("First");

            Subject.ForgetLookup("x");
            Subject.GetSeries("x", 0, resolveVolumeDetails: false, LibraryType.Manga).DisplayName.Should().Be("Second");
        }

        [Test]
        public void resolve_anilist_is_the_strict_binding_without_the_volume_work()
        {
            GivenCatalogueLine("Mushoku Tensei", LibraryType.Manga, Line(8, "Mushoku Tensei", 24));
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.FindSeries("Mushoku Tensei", LibraryType.Manga, 24, It.Is<IReadOnlyList<string>>(a => a.First() == "Mushoku Tensei Jobless Reincarnation"), false, It.IsAny<bool>(), It.IsAny<string>()))
                  .Returns(Bound(85564, "alias", "Mushoku Tensei: Jobless Reincarnation"));

            var pick = Subject.ResolveAniList("Mushoku Tensei", LibraryType.Manga, "Mushoku Tensei Jobless Reincarnation");

            pick.Id.Should().Be(85564);
            pick.MatchedVia.Should().Be("alias");
            Mocker.GetMock<IGoogleBooksService>()
                  .Verify(s => s.LookupVolume(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IMangaDexService>()
                  .Verify(s => s.Lookup(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        // ---- covers + descriptions (D1, D2, D4, D5, D10) ----

        private const string OpenLibraryCover = "https://covers.openlibrary.org/b/id/2405586-L.jpg";
        private const string MangaDexCover = "https://uploads.mangadex.org/covers/227e3f72/v1.jpg";
        private const string GoogleCover = "https://books.google.com/books/content?id=S7qCngEACAAJ&printsec=frontcover&img=1&zoom=4";
        private const string AniListCover = "https://s4.anilist.co/bx30598.jpg";
        private const string EnglishBlurb = "Created by manga-ka Hiro Mashima of Rave Master fame, FAIRY TAIL takes place in a unique magical world where a young mage joins a guild.";
        private const string JapaneseBlurb = "TV・CMなどで活躍中の人気アイドル木村好珠ちゃんとのコラボ写真集。";
        private const string NotebookBlurb = "**************Note: This is Notebook Not Story or Manga Volume. Lined pages for your own ideas.";

        private static GcdVolume Row(int number, string isbn13, string cover = null)
        {
            return new GcdVolume { VolumeNumber = number, Isbn13 = isbn13, CoverUrl = cover, ReleaseDate = "2021-05-04", PageCount = 192 };
        }

        // A bound Fairy Tail line with the given artifact rows; the display name is AniList's.
        private void GivenFairyTail(params GcdVolume[] rows)
        {
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.GetById(30598))
                  .Returns(Bound(30598, "id", "Fairy Tail"));
            GivenCatalogueLine("Fairy Tail", LibraryType.Manga, Line(7, "Fairy Tail", rows.Length));
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetVolumes(7))
                  .Returns(rows.ToList());
        }

        // coverSize: the imageLinks key the record declared — a scan ("large") unless a test says
        // otherwise; null when there is no cover. title: the record names the series unless a test
        // says otherwise (D5, 2026-09-17: a record that does not is given nothing).
        private void GivenIsbnRecord(string isbn13, string description, string cover, string language = "en", string coverSize = "large", string title = "Fairy Tail")
        {
            Mocker.GetMock<IGoogleBooksService>()
                  .Setup(s => s.LookupByIsbn(isbn13))
                  .Returns(new VolumeDetails { Isbn13 = isbn13, Title = title, Description = description, CoverUrl = cover, CoverSize = cover == null ? null : coverSize, Language = language, PageCount = 192, ReleaseDate = new DateTime(2021, 5, 4) });
        }

        private void GivenTitleHit(int number, string description, string cover, string language = "en", string isbn13 = null, string coverSize = "large")
        {
            Mocker.GetMock<IGoogleBooksService>()
                  .Setup(s => s.LookupVolume("Fairy Tail", number))
                  .Returns(new VolumeDetails { Isbn13 = isbn13, Description = description, CoverUrl = cover, CoverSize = cover == null ? null : coverSize, Language = language, PageCount = 192, ReleaseDate = new DateTime(2021, 5, 4) });
        }

        private void GivenIsbnQuotaExceeded(string isbn13)
        {
            Mocker.GetMock<IGoogleBooksService>()
                  .Setup(s => s.LookupByIsbn(isbn13))
                  .Throws<GoogleBooksQuotaException>();
        }

        private void GivenTitleQuotaExceeded(int number)
        {
            Mocker.GetMock<IGoogleBooksService>()
                  .Setup(s => s.LookupVolume("Fairy Tail", number))
                  .Throws<GoogleBooksQuotaException>();
        }

        private void GivenMangaDexCovers(params (int Volume, string Url)[] covers)
        {
            Mocker.GetMock<IMangaDexService>()
                  .Setup(s => s.GetEnglishCovers(30598, "Fairy Tail"))
                  .Returns(new MangaDexCovers { MangaId = "227e3f72", CoversByVolume = covers.ToDictionary(c => c.Volume, c => c.Url) });
        }

        private MangaSeriesMetadata RefreshFairyTail()
        {
            return Subject.GetSeries("Fairy Tail", 0, resolveVolumeDetails: true, LibraryType.Manga, anilistId: 30598);
        }

        [TestCase(true, true, true, OpenLibraryCover, "opentome")]
        [TestCase(false, true, true, MangaDexCover, "mangadex-en")]
        [TestCase(false, false, true, GoogleCover, "google")]
        [TestCase(false, false, false, null, "series")]
        public void a_volume_cover_is_the_first_english_source(bool artifact, bool mangaDex, bool google, string expectedUrl, string expectedSource)
        {
            GivenFairyTail(Row(1, "9780345501332", artifact ? OpenLibraryCover : null));
            GivenIsbnRecord("9780345501332", EnglishBlurb, google ? GoogleCover : null);
            if (mangaDex)
            {
                GivenMangaDexCovers((1, MangaDexCover));
            }

            var volume = RefreshFairyTail().Volumes.Single();

            volume.CoverUrl.Should().Be(expectedUrl);
            volume.CoverSource.Should().Be(expectedSource);

            // The title search never runs just to find a cover (spec §10): every row has an
            // accepted ISBN blurb, a page count, a date and an ISBN.
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.LookupVolume(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [TestCase(true, true, true, MangaDexCover, "mangadex-en")]
        [TestCase(false, true, true, OpenLibraryCover, "opentome")]
        [TestCase(false, false, true, GoogleCover, "google")]
        [TestCase(false, false, false, AniListCover, "anilist")]
        public void the_poster_is_the_english_volume_one_cover(bool mangaDex, bool artifact, bool google, string expectedUrl, string expectedSource)
        {
            GivenFairyTail(Row(1, "9780345501332", artifact ? OpenLibraryCover : null), Row(2, "9780345503305"));
            GivenIsbnRecord("9780345501332", EnglishBlurb, google ? GoogleCover : null);
            GivenIsbnRecord("9780345503305", EnglishBlurb, null);
            if (mangaDex)
            {
                GivenMangaDexCovers((1, MangaDexCover), (2, MangaDexCover.Replace("v1", "v2")));
            }

            var series = RefreshFairyTail();

            series.CoverUrl.Should().Be(expectedUrl);
            series.PosterSource.Should().Be(expectedSource);
            series.VolumeCoverUrl.Should().Be(expectedUrl);
            series.PosterFellBackOnMiss.Should().BeFalse();
        }

        // The two Google cover hosts as the dev server stores them (2026-09-24): a catalogue-only record
        // (Marriagetoxin Vol. 1) and a publisher's record (Mushoku Tensei LN Vol. 1). Both declare only
        // the 128 px thumbnail (783 of 925 cached records do).
        private const string CatalogueThumbnail = "https://books.google.com/books/content?id=2UcC0AEACAAJ&printsec=frontcover&img=1&zoom=1&imgtk=AFLRE7016Mm46RQK&source=gbs_api";
        private const string PublisherThumbnail = "https://books.google.com/books/publisher/content?id=9qGQEAAAQBAJ&printsec=frontcover&img=1&zoom=1&edge=curl&imgtk=AFLRE722sjn6wpyr&source=gbs_api";

        // A scan is the poster as declared. A thumbnail is the poster too (2026-09-24, reversing finding
        // #1 of the 2026-09-16 review): asked with fife=w800 it is the same English cover at the size
        // Google holds (300×450 catalogue, 800 px publisher) — no longer a 128 px blur, and it outranks
        // AniList's Japanese art. The volume row keeps the URL the record declared.
        [TestCase("small", "google", GoogleCover)]
        [TestCase("medium", "google", GoogleCover)]
        [TestCase("large", "google", GoogleCover)]
        [TestCase("thumbnail", "google-thumbnail", GoogleCover + "&fife=w800")]
        [TestCase("smallThumbnail", "google-thumbnail", GoogleCover + "&fife=w800")]
        public void the_google_volume_one_cover_makes_the_poster_before_anilist(string coverSize, string expectedSource, string expectedUrl)
        {
            GivenFairyTail(Row(1, "9780345501332"));
            GivenIsbnRecord("9780345501332", EnglishBlurb, GoogleCover, coverSize: coverSize);

            var series = RefreshFairyTail();

            series.PosterSource.Should().Be(expectedSource);
            series.CoverUrl.Should().Be(expectedUrl);
            series.PosterFellBackOnMiss.Should().BeFalse();
            series.Volumes.Single().CoverUrl.Should().Be(GoogleCover);
            series.Volumes.Single().CoverSource.Should().Be("google");
        }

        // The tier order with the thumbnail in it: every English source above it still wins, and
        // AniList is reached only when volume 1 has no English cover at all.
        [TestCase(true, false, false, MangaDexCover, "mangadex-en")]
        [TestCase(false, true, false, OpenLibraryCover, "opentome")]
        [TestCase(false, false, true, GoogleCover, "google")]
        public void a_higher_english_source_beats_the_volume_one_thumbnail(bool mangaDex, bool artifact, bool googleScan, string expectedUrl, string expectedSource)
        {
            GivenFairyTail(Row(1, "9780345501332", artifact ? OpenLibraryCover : null));
            GivenIsbnRecord("9780345501332", EnglishBlurb, googleScan ? GoogleCover : CatalogueThumbnail, coverSize: googleScan ? "large" : "thumbnail");
            if (mangaDex)
            {
                GivenMangaDexCovers((1, MangaDexCover));
            }

            var series = RefreshFairyTail();

            series.CoverUrl.Should().Be(expectedUrl);
            series.PosterSource.Should().Be(expectedSource);
        }

        [Test]
        public void a_pinned_volume_one_cover_beats_the_volume_one_thumbnail()
        {
            GivenPins(@"{ ""Fairy Tail"": { ""1"": { ""coverUrl"": """ + PinnedCover1 + @""" } } }");
            GivenFairyTail(Row(1, "9780345501332"));
            GivenIsbnRecord("9780345501332", EnglishBlurb, CatalogueThumbnail, coverSize: "thumbnail");

            var series = RefreshFairyTail();

            series.CoverUrl.Should().Be(PinnedCover1);
            series.PosterSource.Should().Be("pin");
        }

        [Test]
        public void a_catalogue_thumbnail_is_the_poster_over_anilist_and_the_summary_says_so()
        {
            GivenFairyTail(Row(1, "9780345501332"), Row(2, "9780345503305"));
            GivenIsbnRecord("9780345501332", EnglishBlurb, CatalogueThumbnail, coverSize: "thumbnail");
            GivenIsbnRecord("9780345503305", EnglishBlurb, null);

            var series = RefreshFairyTail();

            series.CoverUrl.Should().Be(CatalogueThumbnail + "&fife=w800");
            series.PosterSource.Should().Be("google-thumbnail");
            series.VolumeCoverUrl.Should().Be(CatalogueThumbnail + "&fife=w800");
            series.Volumes.Select(v => v.CoverUrl).Should().Equal(CatalogueThumbnail, null);
            series.PosterFellBackOnMiss.Should().BeFalse();
            _log.Logs.Should().Contain(l => l.StartsWith("Info|Volume metadata Fairy Tail:") && l.EndsWith("poster via google-thumbnail"));
        }

        // A volume without an ISBN of its own takes the validated title hit's ISBN and cover (the row
        // shows it); volume 1's is the poster before AniList like the record's.
        [Test]
        public void a_thumbnail_only_title_hit_makes_the_poster_before_anilist()
        {
            GivenFairyTail(Row(1, null));
            GivenTitleHit(1, EnglishBlurb, CatalogueThumbnail, isbn13: "9780345501332", coverSize: "thumbnail");

            var series = RefreshFairyTail();

            series.PosterSource.Should().Be("google-thumbnail");
            series.CoverUrl.Should().Be(CatalogueThumbnail + "&fife=w800");
            series.Volumes.Single().CoverSource.Should().Be("google");
        }

        // A volume-1 quota miss leaves no Google cover this pass: the poster falls to AniList only for
        // that reason, so BookInfoProxy keeps the stored (English) poster — unchanged by the new tier.
        [Test]
        public void a_volume_one_quota_miss_still_keeps_the_stored_poster()
        {
            GivenFairyTail(Row(1, "9780345501332"));
            GivenIsbnQuotaExceeded("9780345501332");

            var series = RefreshFairyTail();

            series.PosterSource.Should().Be("anilist");
            series.PosterFellBackOnMiss.Should().BeTrue();
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase(CatalogueThumbnail, CatalogueThumbnail + "&fife=w800")]
        [TestCase(PublisherThumbnail, "https://books.google.com/books/publisher/content?id=9qGQEAAAQBAJ&printsec=frontcover&img=1&zoom=1&imgtk=AFLRE722sjn6wpyr&source=gbs_api&fife=w800")]
        [TestCase("http://books.google.com/books/content?id=x&zoom=1", "http://books.google.com/books/content?id=x&zoom=1&fife=w800")]
        [TestCase(CatalogueThumbnail + "&fife=w800", CatalogueThumbnail + "&fife=w800")]
        [TestCase(OpenLibraryCover, OpenLibraryCover)]
        [TestCase("https://books.googleusercontent.com/books/content?id=x", "https://books.googleusercontent.com/books/content?id=x")]
        [TestCase(null, null)]
        public void a_google_thumbnail_is_asked_at_poster_size(string url, string expected)
        {
            MangaSeriesMetadataProvider.PosterGradeThumbnail(url).Should().Be(expected);
        }

        [Test]
        public void the_isbn_record_is_the_description_and_the_title_search_does_not_run()
        {
            GivenFairyTail(Row(1, "9780345501332", OpenLibraryCover));
            GivenIsbnRecord("9780345501332", EnglishBlurb, GoogleCover);

            var volume = RefreshFairyTail().Volumes.Single();

            volume.Overview.Should().Be(EnglishBlurb);
            volume.OverviewSource.Should().Be("isbn");
            Mocker.GetMock<IGoogleBooksService>()
                  .Verify(s => s.LookupVolume(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void a_rejected_isbn_description_falls_to_a_validated_title_hit()
        {
            GivenFairyTail(Row(1, "9780345501332", OpenLibraryCover));
            GivenIsbnRecord("9780345501332", JapaneseBlurb, GoogleCover);
            GivenTitleHit(1, EnglishBlurb, GoogleCover);

            var volume = RefreshFairyTail().Volumes.Single();

            volume.Overview.Should().Be(EnglishBlurb);
            volume.OverviewSource.Should().Be("title");
        }

        [Test]
        public void a_rejected_title_hit_leaves_the_description_empty_not_a_placeholder()
        {
            GivenFairyTail(Row(1, "9780345501332"));
            GivenIsbnRecord("9780345501332", null, null);
            GivenTitleHit(1, NotebookBlurb, GoogleCover);

            var volume = RefreshFairyTail().Volumes.Single();

            volume.Overview.Should().BeNull();
            volume.OverviewSource.Should().Be("none");
            volume.CoverUrl.Should().BeNull();
            volume.CoverSource.Should().Be("series");
        }

        [Test]
        public void a_non_english_isbn_record_is_rejected_by_its_language()
        {
            GivenFairyTail(Row(1, "9780345501332"));
            GivenIsbnRecord("9780345501332", EnglishBlurb, null, "pt");

            var volume = RefreshFairyTail().Volumes.Single();

            volume.OverviewSource.Should().Be("none");
            volume.Overview.Should().BeNull();
        }

        [Test]
        public void a_volume_without_an_isbn_takes_the_title_hits_isbn_and_cover()
        {
            GivenFairyTail(Row(1, null));
            GivenTitleHit(1, EnglishBlurb, GoogleCover, isbn13: "9780345501332");

            var volume = RefreshFairyTail().Volumes.Single();

            volume.Isbn13.Should().Be("9780345501332");
            volume.CoverUrl.Should().Be(GoogleCover);
            volume.CoverSource.Should().Be("google");
            volume.OverviewSource.Should().Be("title");
            Mocker.GetMock<IGoogleBooksService>()
                  .Verify(s => s.LookupByIsbn(It.IsAny<string>()), Times.Never());
        }

        // The unstubbed mock's null is the service's "did not answer" (5xx after the retry,
        // transport): an ISBN Google has no record for yields the ISBN-only record, not null.
        [Test]
        public void a_google_outage_is_warned_once_per_series_and_covers_fall_through()
        {
            GivenFairyTail(Row(1, "9780345501332"), Row(2, "9780345503305"), Row(3, "9780345503312", OpenLibraryCover));

            var series = RefreshFairyTail();

            series.Volumes.Select(v => v.OverviewSource).Should().OnlyContain(s => s == "none");
            series.Volumes.Select(v => v.CoverSource).Should().Equal("series", "series", "opentome");
            series.Volumes.Should().OnlyContain(v => v.GoogleMissed);
            series.PosterSource.Should().Be("anilist");
            series.PosterFellBackOnMiss.Should().BeTrue();
            ExceptionVerification.ExpectedWarns(1);
            _log.Logs.Should().Contain("Warn|Google Books did not answer for 3 of 3 volumes of \"Fairy Tail\" this pass (rate limit, quota or transport); descriptions kept local, covers fell through");
        }

        // ---- D5 (2026-09-17): the ISBN record must name the series ----

        // Fairy Tail 24 as the artifact carries it (ISBN only: no cover, date or page count) under a
        // 30-volume cap, with Google's record for that ISBN answering the given title; the other 29
        // volumes have no row and their title searches find nothing.
        private MangaVolumeMetadata ResolveFairyTail24WithGoogleRecord(string title, string cover, string description)
        {
            GivenFairyTail(new GcdVolume { VolumeNumber = 24, Isbn13 = "9781612622668" });
            GivenIsbnRecord("9781612622668", description, cover, title: title);

            var series = Subject.GetSeries("Fairy Tail", 30, resolveVolumeDetails: true, LibraryType.Manga, anilistId: 30598);

            return series.Volumes.Single(v => v.VolumeNumber == 24);
        }

        [Test]
        public void a_mismatched_isbn_record_gives_the_volume_nothing()
        {
            var vol = ResolveFairyTail24WithGoogleRecord(title: "Pantsu Agerune", cover: "https://books.google.com/books/publisher/content?id=WWWQEAAAQBAJ&zoom=4", description: new string('x', 200));

            vol.GoogleRejected.Should().BeTrue();
            vol.GoogleMissed.Should().BeFalse();    // a wrong answer is an answer: the poster is minted, the stored image goes
            vol.CoverSource.Should().Be("series");
            vol.CoverUrl.Should().BeNull();
            vol.OverviewSource.Should().Be("none");
            vol.Overview.Should().BeNull();
            vol.PageCount.Should().Be(0);          // no backfill from a mismatched record
            vol.ReleaseDate.Should().BeNull();
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.LookupVolume("Fairy Tail", 24), Times.Once());
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.MarkIsbnRecordRejected("9781612622668"), Times.Once());   // the on-disk copy keeps the 30-day lifetime
            _log.Logs.Should().NotContain(l => l.StartsWith("Warn|Google Books did not answer"));
        }

        [Test]
        public void a_matching_isbn_record_is_taken_as_before()
        {
            var vol = ResolveFairyTail24WithGoogleRecord(title: "Fairy Tail 24", cover: GoogleCover, description: new string('x', 200));

            vol.GoogleRejected.Should().BeFalse();
            vol.CoverSource.Should().Be("google");
            vol.CoverUrl.Should().Be(GoogleCover);
            vol.OverviewSource.Should().Be("isbn");
            vol.PageCount.Should().Be(192);
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.LookupVolume("Fairy Tail", 24), Times.Never());
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.MarkIsbnRecordRejected(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_record_naming_the_series_by_an_anilist_alias_is_taken()
        {
            GivenFairyTail(new GcdVolume { VolumeNumber = 1, Isbn13 = "9781612622668" });
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.GetById(30598))
                  .Returns(new AniListSeries { Id = 30598, EnglishTitle = "Fairy Tail", RomajiTitle = "Fearī Teiru", MatchedVia = "id" });
            GivenIsbnRecord("9781612622668", EnglishBlurb, GoogleCover, title: "Fearī Teiru 1");

            var vol = RefreshFairyTail().Volumes.Single();

            vol.GoogleRejected.Should().BeFalse();
            vol.OverviewSource.Should().Be("isbn");
        }

        [Test]
        public void a_record_naming_the_series_by_a_catalogue_alias_is_taken()
        {
            GivenFairyTail(new GcdVolume { VolumeNumber = 1, Isbn13 = "9781612622668" });
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetAliases(7))
                  .Returns(new List<string> { "Fearī Teiru" });
            GivenIsbnRecord("9781612622668", EnglishBlurb, GoogleCover, title: "Fearī Teiru 1");

            var vol = RefreshFairyTail().Volumes.Single();

            vol.GoogleRejected.Should().BeFalse();
            vol.OverviewSource.Should().Be("isbn");
        }

        // Google has no record for the ISBN: LookupByIsbn answers the ISBN-only record (everything
        // null but Isbn13), which is not a record to judge — today's path, not a rejection.
        [Test]
        public void an_isbn_only_answer_is_neither_a_rejection_nor_a_miss()
        {
            GivenFairyTail(new GcdVolume { VolumeNumber = 1, Isbn13 = "9781612622668" });
            Mocker.GetMock<IGoogleBooksService>()
                  .Setup(s => s.LookupByIsbn("9781612622668"))
                  .Returns(new VolumeDetails { Isbn13 = "9781612622668" });

            var vol = RefreshFairyTail().Volumes.Single();

            vol.GoogleRejected.Should().BeFalse();
            vol.GoogleMissed.Should().BeFalse();
            vol.CoverSource.Should().Be("series");
            vol.OverviewSource.Should().Be("none");
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.LookupVolume("Fairy Tail", 1), Times.Once());
        }

        [Test]
        public void a_rejected_record_is_named_in_the_per_volume_line_and_not_counted_as_a_miss()
        {
            GivenFairyTail(new GcdVolume { VolumeNumber = 1, Isbn13 = "9781612622668" });
            GivenIsbnRecord("9781612622668", EnglishBlurb, GoogleCover, title: "Pantsu Agerune");
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, _log));
            LogManager.ReconfigExistingLoggers();

            var series = RefreshFairyTail();

            series.Volumes.Single().GoogleRejected.Should().BeTrue();
            series.PosterFellBackOnMiss.Should().BeFalse();
            _log.Logs.Should().Contain("Debug|Volume Fairy Tail Vol. 1: description from none (isbn rejected: title mismatch 'Pantsu Agerune'), cover from series");
        }

        // ---- Cover pin (B3b, 2026-09-18): overrides.json's coverUrl is the operator's word ----

        // The real overrides service over a mocked pins file (the MetadataOverridesServiceFixture
        // pattern), so the key the provider looks up (LibraryTypes.PinKey) is exercised, not mocked.
        private void GivenPins(string json)
        {
            WithTempAsAppPath();
            Mocker.SetConstant<IMetadataOverridesService>(Mocker.Resolve<MetadataOverridesService>());

            var path = System.IO.Path.Combine(TestFolderInfo.AppDataFolder, "metadata", "overrides.json");

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(path)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(path)).Returns(new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.ReadAllText(path)).Returns(json);
        }

        private const string PinnedCover24 = "https://example.org/fairy-tail-24.jpg";
        private const string PinnedCover1 = "https://example.org/fairy-tail-1.jpg";

        // Fairy Tail 24 as Google titles it ("FAIRY TAIL 24"): D5 accepts it and takes its cover.
        [Test]
        public void a_record_titled_in_capitals_is_accepted_by_d5_and_its_cover_taken()
        {
            var vol = ResolveFairyTail24WithGoogleRecord(title: "FAIRY TAIL 24", cover: GoogleCover, description: new string('x', 200));

            vol.GoogleRejected.Should().BeFalse();
            vol.OverviewSource.Should().Be("isbn");
            vol.CoverUrl.Should().Be(GoogleCover);
            vol.CoverSource.Should().Be("google");
        }

        [Test]
        public void a_cover_pin_wins_over_the_cover_an_accepted_record_gave()
        {
            GivenPins(@"{ ""Fairy Tail"": { ""24"": { ""coverUrl"": """ + PinnedCover24 + @""" } } }");

            var vol = ResolveFairyTail24WithGoogleRecord(title: "FAIRY TAIL 24", cover: GoogleCover, description: new string('x', 200));

            vol.GoogleRejected.Should().BeFalse();
            vol.OverviewSource.Should().Be("isbn");
            vol.CoverUrl.Should().Be(PinnedCover24);
            vol.CoverSource.Should().Be("pin");
        }

        [Test]
        public void a_pinned_volume_one_cover_is_the_poster_and_the_summary_counts_it()
        {
            GivenPins(@"{ ""Fairy Tail"": { ""1"": { ""coverUrl"": """ + PinnedCover1 + @""" } } }");
            GivenFairyTail(Row(1, "9780345501332", OpenLibraryCover), Row(2, "9780345503305"));
            GivenIsbnRecord("9780345501332", EnglishBlurb, GoogleCover);
            GivenIsbnRecord("9780345503305", EnglishBlurb, GoogleCover);
            GivenMangaDexCovers((1, MangaDexCover), (2, MangaDexCover.Replace("v1", "v2")));

            var series = RefreshFairyTail();

            series.CoverUrl.Should().Be(PinnedCover1);
            series.PosterSource.Should().Be("pin");
            series.PosterFellBackOnMiss.Should().BeFalse();
            series.Volumes.Select(v => v.CoverUrl).Should().Equal(PinnedCover1, MangaDexCover.Replace("v1", "v2"));
            series.Volumes.Select(v => v.CoverSource).Should().Equal("pin", "mangadex-en");
            _log.Logs.Should().Contain("Info|Volume metadata Fairy Tail: descriptions 2/2 (isbn 2, title 0, none 0), covers 2/2 (opentome 0, mangadex-en 1, google 0, pin 1, series 0), poster via pin");
        }

        // A pin is above Google's tier: a volume-1 Google miss cannot have cost it, so the stored
        // poster is not kept over it (PosterFellBackOnMiss).
        [Test]
        public void a_pinned_poster_did_not_fall_back_on_a_volume_one_miss()
        {
            GivenPins(@"{ ""Fairy Tail"": { ""1"": { ""coverUrl"": """ + PinnedCover1 + @""" } } }");
            GivenFairyTail(Row(1, "9780345501332"));
            GivenIsbnQuotaExceeded("9780345501332");

            var series = RefreshFairyTail();

            series.PosterSource.Should().Be("pin");
            series.PosterFellBackOnMiss.Should().BeFalse();
            series.Volumes.Single().GoogleMissed.Should().BeTrue();
            ExceptionVerification.ExpectedWarns(1);
        }

        // ---- D5 re-admission (B3b, 2026-09-18): a record titled by the volume's own subtitle is not a mismatch ----

        private const string LnIsbn = "9780316371247";

        private static VolumeDetails LnRecord(string title)
        {
            return new VolumeDetails { Isbn13 = LnIsbn, Title = title, SeriesBookTitle = "Early Years", Description = EnglishBlurb, CoverUrl = GoogleCover, CoverSize = "large", Language = "en", PageCount = 250, ReleaseDate = new DateTime(2021, 5, 4) };
        }

        // Vol. 1 of the light-novel line, with an artifact row carrying only the ISBN (no date, pages or
        // cover), Google's record for it, and Audible's single for the volume.
        private void GivenLightNovelVolume1(VolumeDetails record, params AudibleProduct[] products)
        {
            GivenLightNovelLine(RawName, 1);
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetVolumes(9))
                  .Returns(new List<GcdVolume> { new GcdVolume { VolumeNumber = 1, Isbn13 = LnIsbn } });
            Mocker.GetMock<IGoogleBooksService>()
                  .Setup(s => s.LookupByIsbn(LnIsbn))
                  .Returns(record);
            GivenAudible(RawName, products);
        }

        private MangaSeriesMetadata RefreshLightNovel()
        {
            return Subject.GetSeries(RawName, 0, resolveVolumeDetails: true, LibraryType.LightNovel);
        }

        // Sentenced to Be a Hero / Mushoku Tensei (2026-09-24): the light-novel poster is the entry's first
        // ebook volume's English cover — its ISBN record's thumbnail asked at poster size — not AniList's art.
        [Test]
        public void a_light_novel_poster_is_its_first_volumes_thumbnail_not_anilist()
        {
            var record = LnRecord("Sword Art Online, Vol. 1 (light novel)");
            record.CoverUrl = PublisherThumbnail;
            record.CoverSize = "thumbnail";
            GivenLightNovelVolume1(record);
            GivenAniList(Bound(55058, "primary", "Sword Art Online"));

            var series = RefreshLightNovel();

            series.PosterSource.Should().Be("google-thumbnail");
            series.CoverUrl.Should().Be("https://books.google.com/books/publisher/content?id=9qGQEAAAQBAJ&printsec=frontcover&img=1&zoom=1&imgtk=AFLRE722sjn6wpyr&source=gbs_api&fife=w800");
            series.Volumes.Single().CoverUrl.Should().Be(PublisherThumbnail);
            series.Volumes.Single().CoverSource.Should().Be("google");
        }

        // The record's title is the volume's own when it is Audible's title, Audible's subtitle, or the
        // subtitle derived from Audible's title. The volume then ends as the accepted path leaves it —
        // and the record is the subtitle fallback it would have been (the first case: no subtitle from
        // Audible's bare title, so the record's shortSeriesBookTitle names it).
        // Fix round 3 (2026-09-24): Audible's subtitle field is the whole volume label, not a real
        // arc name -- Subtitles.Derive's IsJunk gate rejects it (null), and because there WAS
        // candidate text this pass, the volume is flagged SubtitleRejected so a stored bad value
        // gets cleared on refresh (Book.UseMetadataFrom) instead of kept by the ratchet.
        [Test]
        public void a_junk_audible_subtitle_is_rejected_and_flags_the_volume_as_rejected()
        {
            GivenLightNovelVolume1(LnRecord("Early Years"), new AudibleProduct { Asin = "B0JUNK", Title = "Sword Art Online 1", Subtitle = "Light Novel, Vol. 1", Sequence = "1" });
            GivenDebugLog();

            var vol = RefreshLightNovel().Volumes.Single();

            vol.Subtitle.Should().BeNull();
            vol.SubtitleRejected.Should().BeTrue();
        }

        [TestCase("Early Years", null, "Early Years")]
        [TestCase("Sword Art Online 1", "Early Years", "Early Years")]
        [TestCase("Sword Art Online 1: Early Years", null, "Early Years (light novel)")]
        public void a_title_rejected_record_is_re_admitted_when_its_title_is_the_volumes_own(string productTitle, string productSubtitle, string recordTitle)
        {
            GivenLightNovelVolume1(LnRecord(recordTitle), new AudibleProduct { Asin = "B0EARLY", Title = productTitle, Subtitle = productSubtitle, Sequence = "1" });
            GivenDebugLog();

            var series = RefreshLightNovel();
            var vol = series.Volumes.Single();

            vol.GoogleRejected.Should().BeFalse();
            vol.GoogleMissed.Should().BeFalse();
            vol.Overview.Should().Be(EnglishBlurb);
            vol.OverviewSource.Should().Be("isbn");
            vol.CoverUrl.Should().Be(GoogleCover);
            vol.CoverSource.Should().Be("google");
            vol.PageCount.Should().Be(250);
            vol.ReleaseDate.Should().Be(new DateTime(2021, 5, 4));
            vol.Subtitle.Should().Be("Early Years");
            vol.Audio.Asin.Should().Be("B0EARLY");

            // the re-admitted volume-1 record cover is the poster tier it would have been
            series.CoverUrl.Should().Be(GoogleCover);
            series.PosterSource.Should().Be("google");

            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.MarkIsbnRecordRejected(It.IsAny<string>()), Times.Never());
            _log.Logs.Should().Contain(l => l.StartsWith("Debug|Volume Sword Art Online Vol. 1: isbn re-admitted: title is the volume's own '" + recordTitle + "'"));
            _log.Logs.Should().Contain("Info|Volume metadata Sword Art Online: descriptions 1/1 (isbn 1, title 0, none 0), covers 1/1 (opentome 0, mangadex-en 0, google 1, pin 0, series 0), poster via google, audio 1/1");
        }

        // Fix round 1 (review, M1): the loop's title search ran before the record could be re-admitted.
        // A re-admitted volume is replayed from the record first — its pages, date and blurb — and the
        // title search's answer fills only what the record left, as the accepted path orders them.
        private const string OtherBlurb = "A different edition's blurb, long enough to pass the description gate of the provider and then some.";

        private static readonly AudibleProduct EarlyYears = new AudibleProduct { Asin = "B0EARLY", Title = "Early Years", Sequence = "1" };

        private void GivenTitleSearchAnswersAnotherEdition()
        {
            Mocker.GetMock<IGoogleBooksService>()
                  .Setup(s => s.LookupVolume(RawName, 1))
                  .Returns(new VolumeDetails { Isbn13 = "9780000000001", Description = OtherBlurb, CoverUrl = "https://books.google.com/other", CoverSize = "large", Language = "en", PageCount = 300, ReleaseDate = new DateTime(2019, 3, 12) });
        }

        [Test]
        public void a_re_admitted_record_outranks_the_title_search_for_pages_date_and_blurb()
        {
            GivenLightNovelVolume1(LnRecord("Early Years"), EarlyYears);
            GivenTitleSearchAnswersAnotherEdition();

            var vol = RefreshLightNovel().Volumes.Single();

            vol.GoogleRejected.Should().BeFalse();
            vol.GoogleMissed.Should().BeFalse();
            vol.PageCount.Should().Be(250);
            vol.ReleaseDate.Should().Be(new DateTime(2021, 5, 4));
            vol.Overview.Should().Be(EnglishBlurb);
            vol.OverviewSource.Should().Be("isbn");
            vol.Isbn13.Should().Be(LnIsbn);
            vol.CoverUrl.Should().Be(GoogleCover);
            vol.CoverSource.Should().Be("google");
        }

        [Test]
        public void a_record_that_stays_rejected_keeps_what_the_title_search_gave()
        {
            GivenLightNovelVolume1(LnRecord("Something Else"), EarlyYears);
            GivenTitleSearchAnswersAnotherEdition();

            var vol = RefreshLightNovel().Volumes.Single();

            vol.GoogleRejected.Should().BeTrue();
            vol.PageCount.Should().Be(300);
            vol.ReleaseDate.Should().Be(new DateTime(2019, 3, 12));
            vol.Overview.Should().Be(OtherBlurb);
            vol.OverviewSource.Should().Be("title");
            vol.Isbn13.Should().Be(LnIsbn);
            vol.CoverSource.Should().Be("series");
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.MarkIsbnRecordRejected(LnIsbn), Times.Once());
        }

        // The record answered in full, so the title search would not have run on the accepted path: its
        // 429 is not this volume's miss, not the series Warn's, not the poster ratchet's.
        [Test]
        public void a_title_search_429_on_a_re_admitted_volume_is_not_a_miss()
        {
            GivenLightNovelVolume1(LnRecord("Early Years"), EarlyYears);
            Mocker.GetMock<IGoogleBooksService>()
                  .Setup(s => s.LookupVolume(RawName, 1))
                  .Throws<GoogleBooksQuotaException>();

            var series = RefreshLightNovel();
            var vol = series.Volumes.Single();

            vol.GoogleRejected.Should().BeFalse();
            vol.GoogleMissed.Should().BeFalse();
            vol.OverviewSource.Should().Be("isbn");
            series.PosterSource.Should().Be("google");
            series.PosterFellBackOnMiss.Should().BeFalse();
            _log.Logs.Should().NotContain(l => l.StartsWith("Warn|Google Books did not answer"));
        }

        // Pins land after the re-admission pass: the operator's date wins over the re-admitted record's.
        [Test]
        public void a_pin_wins_over_a_re_admitted_record()
        {
            GivenPins(@"{ ""Sword Art Online (light novel)"": { ""1"": { ""releaseDate"": ""2020-01-15"", ""coverUrl"": """ + PinnedCover1 + @""" } } }");
            GivenLightNovelVolume1(LnRecord("Early Years"), EarlyYears);

            var series = RefreshLightNovel();
            var vol = series.Volumes.Single();

            vol.OverviewSource.Should().Be("isbn");
            vol.PageCount.Should().Be(250);
            vol.ReleaseDate.Should().Be(new DateTime(2020, 1, 15, 0, 0, 0, DateTimeKind.Local).ToUniversalTime());
            vol.CoverUrl.Should().Be(PinnedCover1);
            vol.CoverSource.Should().Be("pin");
            series.PosterSource.Should().Be("pin");
        }

        [Test]
        public void a_title_rejected_record_that_is_not_the_volumes_own_is_marked_after_the_audible_step()
        {
            var calls = new List<string>();
            var product = new AudibleProduct { Asin = "B0EARLY", Title = "Early Years", Sequence = "1" };

            GivenLightNovelVolume1(LnRecord("Something Else"), product);
            Mocker.GetMock<IAudibleCatalogService>()
                  .Setup(s => s.GetSeries(RawName, It.IsAny<IEnumerable<string>>()))
                  .Callback(() => calls.Add("audible"))
                  .Returns(new List<AudibleProduct> { product });
            Mocker.GetMock<IGoogleBooksService>()
                  .Setup(s => s.MarkIsbnRecordRejected(LnIsbn))
                  .Callback(() => calls.Add("mark"));
            GivenDebugLog();

            var vol = RefreshLightNovel().Volumes.Single();

            vol.GoogleRejected.Should().BeTrue();
            vol.Overview.Should().BeNull();
            vol.OverviewSource.Should().Be("none");
            vol.CoverUrl.Should().BeNull();
            vol.CoverSource.Should().Be("series");
            vol.PageCount.Should().Be(0);
            vol.Audio.Title.Should().Be("Early Years");

            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.MarkIsbnRecordRejected(LnIsbn), Times.Once());
            calls.Should().Equal("audible", "mark");
            _log.Logs.Should().NotContain(l => l.Contains("isbn re-admitted"));
        }

        [Test]
        public void a_held_record_is_marked_when_audible_did_not_answer()
        {
            GivenLightNovelVolume1(LnRecord("Early Years"), null);

            var vol = RefreshLightNovel().Volumes.Single();

            vol.GoogleRejected.Should().BeTrue();
            vol.CoverSource.Should().Be("series");
            vol.Audio.Should().BeNull();
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.MarkIsbnRecordRejected(LnIsbn), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        // A manga has no Audible step: a title-rejected record is marked rejected as before, the one call.
        [Test]
        public void a_manga_title_rejected_record_is_marked_rejected_without_an_audible_step()
        {
            GivenDebugLog();

            var vol = ResolveFairyTail24WithGoogleRecord(title: "Pantsu Agerune", cover: GoogleCover, description: new string('x', 200));

            vol.GoogleRejected.Should().BeTrue();
            vol.CoverSource.Should().Be("series");
            vol.OverviewSource.Should().Be("none");
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.MarkIsbnRecordRejected("9781612622668"), Times.Once());
            Mocker.GetMock<IAudibleCatalogService>().Verify(s => s.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
            _log.Logs.Should().NotContain(l => l.Contains("isbn re-admitted"));
        }

        // ---- finding #2: the miss flags (BookInfoProxy's cover + poster ratchet) ----

        [Test]
        public void a_volume_whose_record_answered_is_not_flagged_as_missed()
        {
            GivenFairyTail(Row(1, "9780345501332"), Row(2, "9780345503305"));
            GivenIsbnRecord("9780345501332", EnglishBlurb, null);

            var series = RefreshFairyTail();

            series.Volumes.Select(v => v.GoogleMissed).Should().Equal(false, true);
            series.Volumes.Select(v => v.CoverSource).Should().OnlyContain(s => s == "series");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_title_search_that_finds_nothing_is_not_a_miss()
        {
            GivenFairyTail(Row(1, null), Row(2, null));

            var series = RefreshFairyTail();

            series.Volumes.Should().OnlyContain(v => !v.GoogleMissed);
            series.PosterFellBackOnMiss.Should().BeFalse();
            _log.Logs.Should().NotContain(l => l.StartsWith("Warn|Google Books did not answer"));
        }

        [Test]
        public void the_poster_did_not_fall_back_on_a_miss_when_a_higher_tier_supplied_it()
        {
            GivenFairyTail(Row(1, "9780345501332", OpenLibraryCover));

            var series = RefreshFairyTail();

            series.Volumes.Single().GoogleMissed.Should().BeTrue();
            series.PosterSource.Should().Be("opentome");
            series.PosterFellBackOnMiss.Should().BeFalse();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void the_poster_did_not_fall_back_on_a_miss_when_only_a_later_volume_missed()
        {
            GivenFairyTail(Row(1, "9780345501332"), Row(2, "9780345503305"));
            GivenIsbnRecord("9780345501332", EnglishBlurb, null);

            var series = RefreshFairyTail();

            series.PosterSource.Should().Be("anilist");
            series.PosterFellBackOnMiss.Should().BeFalse();
            ExceptionVerification.ExpectedWarns(1);
        }

        // ---- finding #3: the per-pass quota breaker ----

        [Test]
        public void three_consecutive_429s_stop_the_google_calls_for_the_rest_of_the_pass()
        {
            GivenFairyTail(Row(1, "9780345501332"), Row(2, "9780345503305"), Row(3, "9780345503312"), Row(4, "9780345503329"));
            GivenIsbnQuotaExceeded("9780345501332");
            GivenTitleQuotaExceeded(1);
            GivenIsbnQuotaExceeded("9780345503305");
            GivenTitleQuotaExceeded(2);
            GivenIsbnRecord("9780345503312", EnglishBlurb, GoogleCover);
            GivenIsbnRecord("9780345503329", EnglishBlurb, GoogleCover);

            var series = RefreshFairyTail();

            // vol 1: isbn 429, title 429; vol 2: isbn 429 -> the third in a row opens the breaker
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.LookupVolume("Fairy Tail", 2), Times.Never());
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.LookupByIsbn("9780345503312"), Times.Never());
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.LookupByIsbn("9780345503329"), Times.Never());
            series.Volumes.Should().OnlyContain(v => v.GoogleMissed);
            series.Volumes.Select(v => v.CoverSource).Should().OnlyContain(s => s == "series");
            series.PosterFellBackOnMiss.Should().BeTrue();
            ExceptionVerification.ExpectedWarns(1);
            _log.Logs.Should().Contain("Warn|Google Books did not answer for 4 of 4 volumes of \"Fairy Tail\" this pass (rate limit, quota or transport); descriptions kept local, covers fell through");
        }

        [Test]
        public void an_answer_between_429s_resets_the_breaker()
        {
            GivenFairyTail(Row(1, "9780345501332"), Row(2, "9780345503305"), Row(3, "9780345503312"));
            GivenIsbnQuotaExceeded("9780345501332");
            GivenTitleHit(1, EnglishBlurb, GoogleCover);
            GivenIsbnQuotaExceeded("9780345503305");
            GivenTitleHit(2, EnglishBlurb, GoogleCover);
            GivenIsbnQuotaExceeded("9780345503312");
            GivenTitleHit(3, EnglishBlurb, GoogleCover);

            var series = RefreshFairyTail();

            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.LookupByIsbn("9780345503312"), Times.Once());
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.LookupVolume("Fairy Tail", 3), Times.Once());
            series.Volumes.Should().OnlyContain(v => v.GoogleMissed);
            series.Volumes.Select(v => v.OverviewSource).Should().OnlyContain(s => s == "title");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_light_novel_skips_mangadex_and_takes_the_artifact_then_google()
        {
            var line = Line(1, "Sword Art Online", 2);
            GivenCatalogueLine(RawName, LibraryType.LightNovel, line);
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetVolumes(1))
                  .Returns(new List<GcdVolume> { Row(1, "9780316371247"), Row(2, "9780316296427", OpenLibraryCover) });
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.GetById(51479))
                  .Returns(Bound(51479, "id", "Sword Art Online", "NOVEL"));
            GivenIsbnRecord("9780316371247", EnglishBlurb, GoogleCover, title: "Sword Art Online 1: Aincrad (light novel)");
            GivenIsbnRecord("9780316296427", EnglishBlurb, null, title: "Sword Art Online 2: Aincrad (light novel)");

            var series = Subject.GetSeries(RawName, 0, resolveVolumeDetails: true, LibraryType.LightNovel, anilistId: 51479);

            series.PosterSource.Should().Be("google");
            series.CoverUrl.Should().Be(GoogleCover);
            series.Volumes.Select(v => v.CoverSource).Should().Equal("google", "opentome");
            series.Volumes.Select(v => v.OverviewSource).Should().OnlyContain(s => s == "isbn");
            Mocker.GetMock<IMangaDexService>()
                  .Verify(s => s.GetEnglishCovers(It.IsAny<int>(), It.IsAny<string>()), Times.Never());

            // B1 (D3): no Audible product, so the subtitle comes from the accepted ISBN record's title.
            series.Volumes.Select(v => v.Subtitle).Should().Equal("Aincrad", "Aincrad");
            series.Volumes.Should().OnlyContain(v => v.Audio == null && v.CoveredByVolume == null);
        }

        // Fix wave (2026-09-17, Minor 4): a record without a title is never judged by D5, so it is
        // not a subtitle source either — its cover and blurb are still taken as before.
        [Test]
        public void a_blank_title_isbn_record_is_taken_but_names_no_subtitle()
        {
            GivenCatalogueLine(RawName, LibraryType.LightNovel, Line(1, "Sword Art Online", 1));
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetVolumes(1))
                  .Returns(new List<GcdVolume> { Row(1, "9780316371247") });
            Mocker.GetMock<IGoogleBooksService>()
                  .Setup(s => s.LookupByIsbn("9780316371247"))
                  .Returns(new VolumeDetails { Isbn13 = "9780316371247", Title = " ", Subtitle = "Aincrad", SeriesBookTitle = "Aincrad", Description = EnglishBlurb, CoverUrl = GoogleCover, CoverSize = "large", Language = "en" });

            var vol = Subject.GetSeries(RawName, 0, resolveVolumeDetails: true, LibraryType.LightNovel).Volumes.Single();

            vol.GoogleRejected.Should().BeFalse();
            vol.CoverSource.Should().Be("google");
            vol.OverviewSource.Should().Be("isbn");
            vol.Subtitle.Should().BeNull();
        }

        [Test]
        public void the_lookup_path_does_none_of_the_volume_work_and_keeps_the_anilist_poster()
        {
            GivenFairyTail(Row(1, "9780345501332", OpenLibraryCover));

            var series = Subject.GetSeries("Fairy Tail", 0, resolveVolumeDetails: false, LibraryType.Manga, anilistId: 30598);

            series.CoverUrl.Should().Be(AniListCover);
            series.PosterSource.Should().Be("anilist");
            series.Volumes.Single().CoverUrl.Should().BeNull();
            Mocker.GetMock<IMangaDexService>()
                  .Verify(s => s.GetEnglishCovers(It.IsAny<int>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IGoogleBooksService>()
                  .Verify(s => s.LookupByIsbn(It.IsAny<string>()), Times.Never());
            _log.Logs.Should().NotContain(l => l.StartsWith("Info|Volume metadata"));
        }

        [Test]
        public void the_summary_line_counts_every_source()
        {
            GivenFairyTail(Row(1, "9780345501332", OpenLibraryCover), Row(2, "9780345503305"), Row(3, null));
            GivenIsbnRecord("9780345501332", EnglishBlurb, GoogleCover);
            GivenIsbnRecord("9780345503305", null, GoogleCover);
            GivenTitleHit(2, EnglishBlurb, GoogleCover);

            RefreshFairyTail();

            _log.Logs.Should().Contain("Info|Volume metadata Fairy Tail: descriptions 2/3 (isbn 1, title 1, none 1), covers 2/3 (opentome 1, mangadex-en 0, google 1, pin 0, series 1), poster via opentome");
        }

        // ---- Audiobook identity B1 (2026-09-17, D1-D3): Audible products mapped onto the volumes ----

        private const string Tbate = "The Beginning After the End";

        // A light-novel line bound by its raw name, with the given number of ISBN-less volumes (no
        // Google ISBN call; the title search answers nothing).
        private void GivenLightNovelLine(string name, int volumes)
        {
            GivenCatalogueLine(name, LibraryType.LightNovel, Line(9, name, volumes));
        }

        private void GivenAudible(string name, params AudibleProduct[] products)
        {
            Mocker.GetMock<IAudibleCatalogService>()
                  .Setup(s => s.GetSeries(name, It.IsAny<IEnumerable<string>>()))
                  .Returns(products?.ToList());
        }

        // The sample's products (Files/Audible/sao-products.json), as the service hands them over.
        private static readonly AudibleProduct SaoVol1 = new AudibleProduct { Asin = "1975337182", Title = "Sword Art Online 1: Aincrad", Sequence = "1", RuntimeMinutes = 483, ReleaseDate = new DateTime(2021, 8, 10) };
        private static readonly AudibleProduct SaoVol6 = new AudibleProduct { Asin = "B0B29JDH9K", Title = "Sword Art Online 6", Subtitle = "Phantom Bullet", Sequence = "6", RuntimeMinutes = 635, ReleaseDate = new DateTime(2022, 8, 9) };
        private static readonly AudibleProduct SaoVol23 = new AudibleProduct { Asin = "B0HFW954VF", Title = "Sword Art Online 23 (light novel)", Subtitle = "Unital Ring II", Sequence = "23", RuntimeMinutes = 390, ReleaseDate = new DateTime(2026, 11, 3) };
        private static readonly AudibleProduct TbatePack = new AudibleProduct { Asin = "1774241307", Title = "The Beginning After the End: Publisher's Pack", Subtitle = "Books 1-2", Sequence = "1-2", RuntimeMinutes = 745, ReleaseDate = new DateTime(2019, 12, 3) };

        private void GivenDebugLog()
        {
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, _log));
            LogManager.ReconfigExistingLoggers();
        }

        [Test]
        public void a_light_novel_volume_takes_its_audiobook_identity_and_subtitle_from_audible()
        {
            GivenLightNovelLine(RawName, 6);
            GivenAudible(RawName, SaoVol1, SaoVol6, SaoVol23);

            var series = Subject.GetSeries(RawName, 0, resolveVolumeDetails: true, LibraryType.LightNovel);

            var vol1 = series.Volumes.Single(v => v.VolumeNumber == 1);
            vol1.Audio.Asin.Should().Be("1975337182");
            vol1.Audio.Title.Should().Be("Sword Art Online 1: Aincrad");
            vol1.Audio.Subtitle.Should().BeNull();
            vol1.Audio.RuntimeMinutes.Should().Be(483);
            vol1.Audio.ReleaseDate.Should().Be(new DateTime(2021, 8, 10));
            vol1.Audio.CoversFrom.Should().BeNull();
            vol1.Audio.CoversTo.Should().BeNull();
            vol1.Subtitle.Should().Be("Aincrad");

            var vol6 = series.Volumes.Single(v => v.VolumeNumber == 6);
            vol6.Audio.Asin.Should().Be("B0B29JDH9K");
            vol6.Audio.Subtitle.Should().Be("Phantom Bullet");
            vol6.Subtitle.Should().Be("Phantom Bullet");

            // Vol. 23 is past the line's count: dropped. The rest carry nothing.
            series.Volumes.Where(v => v.VolumeNumber != 1 && v.VolumeNumber != 6).Should().OnlyContain(v => v.Audio == null && v.Subtitle == null);
            series.Volumes.Should().OnlyContain(v => v.CoveredByVolume == null);

            Mocker.GetMock<IAudibleCatalogService>()
                  .Verify(s => s.GetSeries(RawName, It.IsAny<IEnumerable<string>>()), Times.Once());
            _log.Logs.Should().Contain("Info|Volume metadata Sword Art Online: descriptions 0/6 (isbn 0, title 0, none 6), covers 0/6 (opentome 0, mangadex-en 0, google 0, pin 0, series 6), poster via none, audio 2/6");
        }

        [Test]
        public void the_per_volume_line_names_the_product_or_none()
        {
            GivenLightNovelLine(RawName, 2);
            GivenAudible(RawName, SaoVol1);
            GivenDebugLog();

            Subject.GetSeries(RawName, 0, resolveVolumeDetails: true, LibraryType.LightNovel);

            _log.Logs.Should().Contain("Debug|Volume Sword Art Online Vol. 1: audio 1975337182 \"Sword Art Online 1: Aincrad\"");
            _log.Logs.Should().Contain("Debug|Volume Sword Art Online Vol. 2: audio none");
        }

        [Test]
        public void a_pack_is_carried_by_its_first_volume_and_covers_the_rest_of_its_range()
        {
            GivenLightNovelLine(Tbate, 3);
            GivenAudible(Tbate, TbatePack);
            GivenDebugLog();

            var series = Subject.GetSeries(Tbate, 0, resolveVolumeDetails: true, LibraryType.LightNovel);

            var vol1 = series.Volumes.Single(v => v.VolumeNumber == 1);
            vol1.Audio.Asin.Should().Be("1774241307");
            vol1.Audio.CoversFrom.Should().Be(1);
            vol1.Audio.CoversTo.Should().Be(2);
            vol1.CoveredByVolume.Should().BeNull();
            vol1.Subtitle.Should().BeNull();   // the pack's "Books 1-2" names the pack, not Vol. 1 (and there is no ISBN record to fall to)

            var vol2 = series.Volumes.Single(v => v.VolumeNumber == 2);
            vol2.Audio.Should().BeNull();
            vol2.CoveredByVolume.Should().Be(1);

            var vol3 = series.Volumes.Single(v => v.VolumeNumber == 3);
            vol3.Audio.Should().BeNull();
            vol3.CoveredByVolume.Should().BeNull();

            _log.Logs.Should().Contain("Debug|Volume The Beginning After the End Vol. 1: audio 1774241307 \"The Beginning After the End: Publisher's Pack\" covers 1-2");
            _log.Logs.Should().Contain("Debug|Volume The Beginning After the End Vol. 2: audio covered by Vol. 1");
            _log.Logs.Should().Contain("Debug|Volume The Beginning After the End Vol. 3: audio none");
        }

        [Test]
        public void a_volume_with_its_own_product_keeps_it_and_is_not_covered_by_a_pack()
        {
            var vol2Single = new AudibleProduct { Asin = "B0SINGLE02", Title = "The Beginning After the End 2", Sequence = "2" };
            GivenLightNovelLine(Tbate, 2);
            GivenAudible(Tbate, TbatePack, vol2Single);   // the pack lists first: order must not matter

            var series = Subject.GetSeries(Tbate, 0, resolveVolumeDetails: true, LibraryType.LightNovel);

            series.Volumes.Single(v => v.VolumeNumber == 1).Audio.Asin.Should().Be("1774241307");
            var vol2 = series.Volumes.Single(v => v.VolumeNumber == 2);
            vol2.Audio.Asin.Should().Be("B0SINGLE02");
            vol2.Audio.CoversFrom.Should().BeNull();
            vol2.CoveredByVolume.Should().BeNull();
        }

        // Fix wave (2026-09-17, Minor 1): a volume already covered by one pack is never the carrier
        // of another — no edition holds both an ASIN and CoveredByVolume; the next uncovered volume
        // in the second pack's range carries it.
        [Test]
        public void a_volume_covered_by_one_pack_is_not_the_carrier_of_an_overlapping_pack()
        {
            var pack2 = new AudibleProduct { Asin = "B0PACK0203", Title = "The Beginning After the End: Publisher's Pack 2", Subtitle = "Books 2-3", Sequence = "2-3" };
            GivenLightNovelLine(Tbate, 3);
            GivenAudible(Tbate, TbatePack, pack2);
            GivenDebugLog();

            var series = Subject.GetSeries(Tbate, 0, resolveVolumeDetails: true, LibraryType.LightNovel);

            var vol1 = series.Volumes.Single(v => v.VolumeNumber == 1);
            vol1.Audio.Asin.Should().Be("1774241307");
            vol1.Audio.CoversFrom.Should().Be(1);
            vol1.Audio.CoversTo.Should().Be(2);
            vol1.CoveredByVolume.Should().BeNull();

            var vol2 = series.Volumes.Single(v => v.VolumeNumber == 2);
            vol2.Audio.Should().BeNull();
            vol2.CoveredByVolume.Should().Be(1);

            var vol3 = series.Volumes.Single(v => v.VolumeNumber == 3);
            vol3.Audio.Asin.Should().Be("B0PACK0203");
            vol3.Audio.CoversFrom.Should().Be(2);
            vol3.Audio.CoversTo.Should().Be(3);
            vol3.CoveredByVolume.Should().BeNull();

            _log.Logs.Should().Contain("Debug|Volume The Beginning After the End Vol. 2: already covered by Vol. 1, not carrier for pack B0PACK0203 \"The Beginning After the End: Publisher's Pack 2\" 2-3");
        }

        [Test]
        public void a_fractional_sequence_needs_a_volume_with_that_exact_number()
        {
            var sideStory = new AudibleProduct { Asin = "B0SIDE0035", Title = "Sword Art Online 3.5", Sequence = "3.5" };
            GivenLightNovelLine(RawName, 4);
            GivenAudible(RawName, sideStory, SaoVol1);

            var series = Subject.GetSeries(RawName, 0, resolveVolumeDetails: true, LibraryType.LightNovel);

            series.Volumes.Single(v => v.VolumeNumber == 1).Audio.Asin.Should().Be("1975337182");
            series.Volumes.Where(v => v.VolumeNumber != 1).Should().OnlyContain(v => v.Audio == null && v.CoveredByVolume == null);
        }

        [Test]
        public void audible_not_answering_is_warned_once_and_changes_nothing_else()
        {
            var line = Line(1, "Sword Art Online", 2);
            GivenCatalogueLine(RawName, LibraryType.LightNovel, line);
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetVolumes(1))
                  .Returns(new List<GcdVolume> { Row(1, "9780316371247"), Row(2, "9780316296427", OpenLibraryCover) });
            GivenIsbnRecord("9780316371247", EnglishBlurb, GoogleCover, title: "Sword Art Online 1: Aincrad (light novel)");
            GivenIsbnRecord("9780316296427", EnglishBlurb, null, title: "Sword Art Online 2: Aincrad (light novel)");
            GivenAudible(RawName, null);

            var series = Subject.GetSeries(RawName, 0, resolveVolumeDetails: true, LibraryType.LightNovel);

            series.Volumes.Should().OnlyContain(v => v.Audio == null && v.CoveredByVolume == null);
            series.Volumes.Select(v => v.Subtitle).Should().Equal("Aincrad", "Aincrad");
            series.Volumes.Select(v => v.CoverSource).Should().Equal("google", "opentome");
            series.Volumes.Select(v => v.OverviewSource).Should().OnlyContain(s => s == "isbn");
            series.PosterSource.Should().Be("google");
            _log.Logs.Count(l => l == "Warn|Audible did not answer for \"Sword Art Online\" this pass").Should().Be(1);
            _log.Logs.Should().Contain(l => l.StartsWith("Info|Volume metadata Sword Art Online:") && l.EndsWith(", audio 0/2"));
            ExceptionVerification.ExpectedWarns(1);
        }

        // B2 (2026-09-17, D4a): a pass Audible answered — even with nothing — makes the volumes'
        // covered marks assertions (BookInfoProxy reads the flag); a pass it did not answer does not.
        [Test]
        public void a_pass_audible_answered_is_flagged_even_when_it_answered_with_nothing()
        {
            GivenLightNovelLine(RawName, 2);
            GivenAudible(RawName);

            Subject.GetSeries(RawName, 0, resolveVolumeDetails: true, LibraryType.LightNovel).AudibleAnswered.Should().BeTrue();
        }

        [Test]
        public void a_pass_audible_did_not_answer_is_not_flagged()
        {
            GivenLightNovelLine(RawName, 2);
            GivenAudible(RawName, null);

            Subject.GetSeries(RawName, 0, resolveVolumeDetails: true, LibraryType.LightNovel).AudibleAnswered.Should().BeFalse();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_manga_never_asks_audible()
        {
            GivenFairyTail(Row(1, "9780345501332", OpenLibraryCover));
            GivenIsbnRecord("9780345501332", EnglishBlurb, GoogleCover);

            var series = RefreshFairyTail();

            series.Volumes.Should().OnlyContain(v => v.Audio == null && v.CoveredByVolume == null && v.Subtitle == null);
            Mocker.GetMock<IAudibleCatalogService>()
                  .Verify(s => s.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
            _log.Logs.Should().Contain("Info|Volume metadata Fairy Tail: descriptions 1/1 (isbn 1, title 0, none 0), covers 1/1 (opentome 1, mangadex-en 0, google 0, pin 0, series 0), poster via opentome");
        }

        [Test]
        public void the_light_novel_lookup_path_does_not_ask_audible()
        {
            GivenLightNovelLine(RawName, 2);

            Subject.GetSeries(RawName, 0, resolveVolumeDetails: false, LibraryType.LightNovel);

            Mocker.GetMock<IAudibleCatalogService>()
                  .Verify(s => s.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Test]
        public void artifact_dates_are_local_midnight_in_utc_so_a_stored_row_compares_equal()
        {
            // 2026-09-17: the DB converter writes an Unspecified value with ToUniversalTime() (local ->
            // UTC) and reads it back as Utc; a remote parsed to midnight Unspecified never equals it, so
            // every dated volume was "Updated" on every pass. The parser now produces the stored value.
            var expected = new DateTime(2015, 8, 18, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();

            var parsed = MangaSeriesMetadataProvider.ParseDate("2015-08-18");

            parsed.Should().Be(expected);
            parsed.Value.Kind.Should().Be(DateTimeKind.Utc);
            parsed.Value.ToLocalTime().Date.Should().Be(new DateTime(2015, 8, 18));   // still the same calendar day, whatever the process TZ
        }
    
        // 2026-09-22: OpenTome's per-line status "stalled" reaches the series as Stalled.
        [Test]
        public void a_stalled_catalogue_line_reads_stalled()
        {
            var line = Line(1, "Sword Art Online", 2);
            line.Status = "stalled";
            GivenCatalogueLine(RawName, LibraryType.LightNovel, line);
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetVolumes(1))
                  .Returns(new List<GcdVolume> { Row(1, "9780316371247"), Row(2, "9780316296427") });

            var series = Subject.GetSeries(RawName, 0, resolveVolumeDetails: false, LibraryType.LightNovel);

            series.Status.Should().Be(AuthorStatusType.Stalled);
        }

        // 2026-09-22: the catalogue's own volume title outranks the Google record's.
        [Test]
        public void the_catalogue_volume_title_is_the_subtitle_before_the_isbn_record()
        {
            GivenCatalogueLine(RawName, LibraryType.LightNovel, Line(1, "Sword Art Online", 1));
            var row = Row(1, "9780316371247");
            row.Title = "Aincrad";
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetVolumes(1))
                  .Returns(new List<GcdVolume> { row });
            GivenIsbnRecord("9780316371247", EnglishBlurb, GoogleCover, title: "Sword Art Online 1: Something Else (light novel)");

            var vol = Subject.GetSeries(RawName, 0, resolveVolumeDetails: true, LibraryType.LightNovel).Volumes.Single();

            vol.Subtitle.Should().Be("Aincrad");
        }

        [TestCase("Aincrad", "Aincrad")]
        [TestCase("Sword Art Online 1", null)]
        [TestCase("Vol. 1", null)]
        [TestCase(null, null)]
        public void a_catalogue_title_that_only_names_the_series_is_not_a_subtitle(string artifactTitle, string expected)
        {
            MangaSeriesMetadataProvider.SubtitleOf(artifactTitle, null, null, "Sword Art Online", new List<string>(), 1).Should().Be(expected);
        }

        // Aincrad subtitle (2026-09-23, controller ruling): SAO's catalogue alias list carries
        // "Aincrad" itself, so the alias gate used to reject the artifact title too -- it now trusts
        // the artifact candidate specifically.
        [Test]
        public void an_artifact_title_that_matches_one_of_the_entrys_own_aliases_is_still_the_subtitle()
        {
            MangaSeriesMetadataProvider.SubtitleOf("Aincrad", null, null, "Sword Art Online", new List<string> { "Aincrad" }, 1).Should().Be("Aincrad");
        }

        // Full-title subtitles (2026-09-24): Rascal vols 1 and 15 never stored a subtitle because
        // their catalogue titles were also entries in the line's alias list (the opentome-2026-09-22
        // artifact carried both "…of Bunny Girl Senpai" and "…of a Dear Friend" as aliases), and
        // since 558 the contains-the-series-name junk rule rejected them too. With the trusted
        // artifact candidate and the full-title exception, both come through, and so does vol 16.
        [TestCase("Rascal Does Not Dream of Bunny Girl Senpai", 1, "Rascal Does Not Dream of Bunny Girl Senpai")]
        [TestCase("Rascal Does Not Dream of Petite Devil Kohai", 2, "Rascal Does Not Dream of Petite Devil Kohai")]
        [TestCase("Rascal Does Not Dream of a Dear Friend", 15, "Rascal Does Not Dream of a Dear Friend")]
        [TestCase("Rascal Does Not Dream of a Beach Queen + (light novel)", 16, "Rascal Does Not Dream of a Beach Queen +")]
        public void a_rascal_catalogue_title_is_the_subtitle_even_when_it_is_one_of_the_lines_aliases(string artifactTitle, double volumeNumber, string expected)
        {
            var aliases = new List<string>
            {
                "Aobuta",
                "Bunny Girl Senpai",
                "Rascal Does Not Dream",
                "Rascal Does Not Dream (novel series)",
                "Rascal Does Not Dream of Bunny Girl Senpai",
                "Rascal Does Not Dream of a Dear Friend",
                "Seishun Buta Yarou"
            };

            MangaSeriesMetadataProvider.SubtitleOf(artifactTitle, null, null, "Rascal Does Not Dream", aliases, volumeNumber).Should().Be(expected);
        }

        [Test]
        public void a_google_subtitle_that_matches_one_of_the_entrys_own_aliases_is_still_rejected()
        {
            var record = new VolumeDetails { Subtitle = "Aincrad" };

            MangaSeriesMetadataProvider.SubtitleOf(null, null, record, "Sword Art Online", new List<string> { "Aincrad" }, 1).Should().BeNull();
        }

        // HadSubtitleCandidate (fix round 3, 2026-09-24): distinguishes "no candidate text this pass"
        // (the ratchet should keep whatever subtitle we already stored) from "candidate text existed
        // and Subtitles.Derive rejected it" (the ratchet must clear the stored value instead).
        [Test]
        public void had_subtitle_candidate_is_true_for_an_artifact_title()
        {
            MangaSeriesMetadataProvider.HadSubtitleCandidate("Sword Art Online 1", null, null).Should().BeTrue();
        }

        [Test]
        public void had_subtitle_candidate_is_true_for_an_audible_subtitle_or_title()
        {
            MangaSeriesMetadataProvider.HadSubtitleCandidate(null, new AudiobookIdentity { Subtitle = "Light Novel, Vol. 1" }, null).Should().BeTrue();
            MangaSeriesMetadataProvider.HadSubtitleCandidate(null, new AudiobookIdentity { Title = "Sword Art Online 1" }, null).Should().BeTrue();
        }

        [Test]
        public void had_subtitle_candidate_is_true_for_a_google_record_field()
        {
            MangaSeriesMetadataProvider.HadSubtitleCandidate(null, null, new VolumeDetails { Title = "Sword Art Online 1" }).Should().BeTrue();
            MangaSeriesMetadataProvider.HadSubtitleCandidate(null, null, new VolumeDetails { Subtitle = "Light Novel, Vol. 1" }).Should().BeTrue();
            MangaSeriesMetadataProvider.HadSubtitleCandidate(null, null, new VolumeDetails { SeriesBookTitle = "Light Novel, Vol. 1" }).Should().BeTrue();
        }

        [Test]
        public void had_subtitle_candidate_is_false_when_every_source_is_blank()
        {
            MangaSeriesMetadataProvider.HadSubtitleCandidate(null, new AudiobookIdentity(), new VolumeDetails()).Should().BeFalse();
            MangaSeriesMetadataProvider.HadSubtitleCandidate(" ", null, null).Should().BeFalse();
        }

        // ---- display fallback (OpenTome display_anilist_id, 2026-09-24) ----

        private const int DisplayId = 85737;
        private const string DisplayCover = "https://s4.anilist.co/bx85737.jpg";
        private const string DisplayBlurb = "About the parent work.";

        private static GcdSeries DisplayLine(int id, string name, int volumes, int? displayId = DisplayId, string medium = "manga")
        {
            var line = Line(id, name, volumes);
            line.Medium = medium;
            line.DisplayAnilistId = displayId;
            line.DisplayAnilistVia = displayId.HasValue ? "parent" : null;
            return line;
        }

        // Rich on purpose: none of this but the cover and the description may reach the entry.
        private void GivenDisplayMedia()
        {
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.GetById(DisplayId))
                  .Returns(new AniListSeries
                  {
                      Id = DisplayId,
                      EnglishTitle = "The Parent Work",
                      RomajiTitle = "Oya no Sakuhin",
                      Synonyms = new List<string> { "Parent Work Alt" },
                      Format = "MANGA",
                      Status = "FINISHED",
                      Volumes = 40,
                      AverageScore = 80,
                      StartYear = 1990,
                      CoverImageUrl = DisplayCover,
                      Description = DisplayBlurb,
                      MatchedVia = "id"
                  });
        }

        private void VerifyNoDisplayFetch()
        {
            Mocker.GetMock<IAniListService>().Verify(s => s.GetById(DisplayId), Times.Never());
        }

        [Test]
        public void an_artifact_without_display_ids_changes_nothing()
        {
            GivenAniList(null);
            GivenCatalogueLine("Weed", LibraryType.Manga, DisplayLine(40, "Weed", 3, displayId: null));

            var series = Subject.GetSeries("Weed", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.CoverUrl.Should().BeNull();
            series.PosterSource.Should().Be("none");
            series.Overview.Should().BeEmpty();
            series.AniListId.Should().BeNull();
            series.DisplayFetchFailed.Should().BeFalse();
            Mocker.GetMock<IAniListService>().Verify(s => s.GetById(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void a_display_id_fills_only_the_poster_and_overview_of_an_unbound_entry()
        {
            GivenAniList(null);
            GivenCatalogueLine("Weed", LibraryType.Manga, DisplayLine(40, "Weed", 3));
            GivenDisplayMedia();
            GivenDebugLog();

            var series = Subject.GetSeries("Weed", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.CoverUrl.Should().Be(DisplayCover);
            series.PosterSource.Should().Be("anilist-display");
            series.Overview.Should().Be(DisplayBlurb);
            series.DisplayFetchFailed.Should().BeFalse();

            // Never a binding, and nothing else of the display entry is taken.
            series.AniListId.Should().BeNull();
            series.MatchedVia.Should().BeNull();
            series.DisplayName.Should().Be("Weed");
            series.AltTitles.Should().BeEmpty();
            series.RatingValue.Should().Be(0m);
            series.VolumeCount.Should().Be(3);
            series.OriginTotal.Should().Be(3);
            series.Status.Should().Be(AuthorStatusType.Continuing);

            // Only the series poster: a volume row without art does not show another work's picture.
            series.VolumeCoverUrl.Should().BeNull();
            series.Volumes.Should().OnlyContain(v => v.CoverUrl == null);
            Mocker.GetMock<IMangaDexService>()
                  .Verify(s => s.GetEnglishCovers(It.IsAny<int>(), It.IsAny<string>()), Times.Never());

            _log.Logs.Should().Contain("Debug|Display fallback for Weed: AniList 85737 via parent (cover|overview)");
            _log.Logs.Should().Contain(l => l.StartsWith("Info|Volume metadata Weed:") && l.Contains("poster via anilist-display"));
            _log.Logs.Should().NotContain(l => l.StartsWith("Info|AniList match"));
        }

        [Test]
        public void an_entry_with_its_own_poster_takes_only_the_overview()
        {
            GivenAniList(null);
            GivenCatalogueLine("Weed", LibraryType.Manga, DisplayLine(40, "Weed", 1));
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetVolumes(40))
                  .Returns(new List<GcdVolume> { new GcdVolume { VolumeNumber = 1, CoverUrl = OpenLibraryCover } });
            GivenDisplayMedia();
            GivenDebugLog();

            var series = Subject.GetSeries("Weed", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.CoverUrl.Should().Be(OpenLibraryCover);
            series.PosterSource.Should().Be("opentome");
            series.VolumeCoverUrl.Should().Be(OpenLibraryCover);
            series.Overview.Should().Be(DisplayBlurb);
            series.AniListId.Should().BeNull();
            _log.Logs.Should().Contain("Debug|Display fallback for Weed: AniList 85737 via parent (overview)");
        }

        [Test]
        public void an_entry_with_its_own_poster_and_overview_is_untouched()
        {
            // The search box's relaxed hit supplies both (never pinned, so the entry is unbound).
            GivenAniList(Bound(99022, "relaxed", "Weed"));
            GivenCatalogueLine("Weed", LibraryType.Manga, DisplayLine(40, "Weed", 3));
            GivenDisplayMedia();

            var series = Subject.GetSeries("Weed", 0, resolveVolumeDetails: false, LibraryType.Manga);

            series.CoverUrl.Should().Be("https://s4.anilist.co/bx99022.jpg");
            series.PosterSource.Should().Be("anilist");
            series.Overview.Should().Be("About Weed");
            series.AniListId.Should().BeNull();
            VerifyNoDisplayFetch();
        }

        [Test]
        public void a_stored_binding_ignores_the_display_id()
        {
            GivenCatalogueLine("Weed", LibraryType.Manga, DisplayLine(40, "Weed", 3));
            GivenDisplayMedia();
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.GetById(30598))
                  .Returns(Bound(30598, "id", "Weed"));

            var series = Subject.GetSeries("Weed", 0, resolveVolumeDetails: true, LibraryType.Manga, anilistId: 30598);

            series.AniListId.Should().Be(30598);
            series.CoverUrl.Should().Be(AniListCover);
            series.Overview.Should().Be("About Weed");
            VerifyNoDisplayFetch();
        }

        [Test]
        public void a_stored_binding_that_did_not_resolve_still_ignores_the_display_id()
        {
            GivenCatalogueLine("Weed", LibraryType.Manga, DisplayLine(40, "Weed", 3));
            GivenDisplayMedia();

            var series = Subject.GetSeries("Weed", 0, resolveVolumeDetails: true, LibraryType.Manga, anilistId: 30598);

            series.AniListId.Should().BeNull();
            series.CoverUrl.Should().BeNull();
            VerifyNoDisplayFetch();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_pinned_search_match_ignores_the_display_id()
        {
            GivenAniList(Bound(30598, "primary", "Weed"));
            GivenCatalogueLine("Weed", LibraryType.Manga, DisplayLine(40, "Weed", 3));
            GivenDisplayMedia();

            var series = Subject.GetSeries("Weed", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.AniListId.Should().Be(30598);
            series.CoverUrl.Should().Be(AniListCover);
            VerifyNoDisplayFetch();
        }

        [Test]
        public void a_catalogue_anilist_id_wins_over_the_display_id()
        {
            // Shouldn't happen (OpenTome sets the display id only where anilist_id is null), but
            // the real id wins.
            var line = DisplayLine(40, "Weed", 3);
            line.AnilistId = 30598;
            GivenCatalogueLine("Weed", LibraryType.Manga, line);
            GivenDisplayMedia();
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.GetById(30598))
                  .Returns(Bound(30598, "id", "Weed"));

            var series = Subject.GetSeries("Weed", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.AniListId.Should().Be(30598);
            series.MatchedVia.Should().Be("catalogue");
            series.CoverUrl.Should().Be(AniListCover);
            VerifyNoDisplayFetch();
        }

        [Test]
        public void a_catalogue_anilist_id_that_did_not_resolve_still_wins_over_the_display_id()
        {
            var line = DisplayLine(40, "Weed", 3);
            line.AnilistId = 30598;
            GivenCatalogueLine("Weed", LibraryType.Manga, line);
            GivenAniList(null);
            GivenDisplayMedia();

            var series = Subject.GetSeries("Weed", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.AniListId.Should().BeNull();
            series.CoverUrl.Should().BeNull();
            VerifyNoDisplayFetch();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_display_id_that_does_not_resolve_leaves_the_gaps_quietly()
        {
            GivenAniList(null);
            GivenCatalogueLine("Weed", LibraryType.Manga, DisplayLine(40, "Weed", 3));

            var series = Subject.GetSeries("Weed", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.CoverUrl.Should().BeNull();
            series.PosterSource.Should().Be("none");
            series.Overview.Should().BeEmpty();
            series.DisplayFetchFailed.Should().BeTrue();
            Mocker.GetMock<IAniListService>().Verify(s => s.GetById(DisplayId), Times.Once());
        }

        [Test]
        public void the_search_box_asks_for_the_display_entry_once_per_cached_lookup()
        {
            GivenAniList(null);
            GivenCatalogueLine("Weed", LibraryType.Manga, DisplayLine(40, "Weed", 3));
            GivenDisplayMedia();

            Subject.GetSeries("Weed", 0, resolveVolumeDetails: false, LibraryType.Manga).CoverUrl.Should().Be(DisplayCover);
            Subject.GetSeries("Weed", 0, resolveVolumeDetails: false, LibraryType.Manga).CoverUrl.Should().Be(DisplayCover);

            Mocker.GetMock<IAniListService>().Verify(s => s.GetById(DisplayId), Times.Once());
        }

        [Test]
        public void a_light_novel_found_by_its_name_takes_the_display_fallback()
        {
            GivenAniList(null);
            GivenCatalogueLine("Overlord", LibraryType.LightNovel, DisplayLine(41, "Overlord (novel series)", 2, medium: "light_novel"));
            GivenDisplayMedia();

            var series = Subject.GetSeries("Overlord", 0, resolveVolumeDetails: true, LibraryType.LightNovel);

            series.CoverUrl.Should().Be(DisplayCover);
            series.Overview.Should().Be(DisplayBlurb);
            series.AniListId.Should().BeNull();
        }

        [Test]
        public void an_alias_hint_on_a_line_of_the_other_library_gets_no_display_fallback()
        {
            // "Night Shift" finds the novel line "Yakin Shift" by alias (Rank's manga fallback).
            GivenAniList(null);
            GivenCatalogueLine("Night Shift", LibraryType.Manga, DisplayLine(42, "Yakin Shift", 3, medium: "light_novel"));
            GivenDisplayMedia();

            var series = Subject.GetSeries("Night Shift", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.CoverUrl.Should().BeNull();
            series.Overview.Should().BeEmpty();
            VerifyNoDisplayFetch();
        }

        [Test]
        public void an_alias_hint_on_a_line_of_the_same_library_gets_the_display_fallback()
        {
            GivenAniList(null);
            GivenCatalogueLine("Night Shift", LibraryType.Manga, DisplayLine(42, "Yakin Shift", 3));
            GivenDisplayMedia();

            var series = Subject.GetSeries("Night Shift", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.CoverUrl.Should().Be(DisplayCover);
            series.AniListId.Should().BeNull();
        }

        // The gate alone: (name, entry library, stored id, pinned id, line name, line medium,
        // catalogue id, display id) -> allowed.
        [TestCase("Weed", LibraryType.Manga, null, null, "Weed", "manga", null, DisplayId, true)]
        [TestCase("Weed", LibraryType.Manga, null, null, "Weed", "light_novel", null, DisplayId, true)]      // by name: any medium
        [TestCase("Night Shift", LibraryType.Manga, null, null, "Yakin Shift", "manga", null, DisplayId, true)]
        [TestCase("Night Shift", LibraryType.Manga, null, null, "Yakin Shift", null, null, DisplayId, true)]  // null medium = manga
        [TestCase("Night Shift", LibraryType.Manga, null, null, "Yakin Shift", "light_novel", null, DisplayId, false)]
        [TestCase("Night Shift", LibraryType.LightNovel, null, null, "Yakin Shift", "novel", null, DisplayId, true)]
        [TestCase("Night Shift", LibraryType.LightNovel, null, null, "Yakin Shift", "manga", null, DisplayId, false)]
        [TestCase("Weed", LibraryType.Manga, 30598, null, "Weed", "manga", null, DisplayId, false)]         // stored binding
        [TestCase("Weed", LibraryType.Manga, null, 30598, "Weed", "manga", null, DisplayId, false)]         // pinned this pass
        [TestCase("Weed", LibraryType.Manga, null, null, "Weed", "manga", 30598, DisplayId, false)]         // catalogue id
        [TestCase("Weed", LibraryType.Manga, null, null, "Weed", "manga", null, null, false)]              // no display id
        public void the_display_fallback_gate(string name, LibraryType library, int? stored, int? pinned, string lineName, string medium, int? catalogueId, int? displayId, bool expected)
        {
            var line = DisplayLine(50, lineName, 3, displayId, medium);
            line.AnilistId = catalogueId;

            MangaSeriesMetadataProvider.DisplayFallbackAllowed(name, library, stored, pinned, line).Should().Be(expected);
        }

        [Test]
        public void no_hint_gets_no_display_fallback()
        {
            MangaSeriesMetadataProvider.DisplayFallbackAllowed("Weed", LibraryType.Manga, null, null, null).Should().BeFalse();
        }

        [Test]
        public void a_manhwa_hint_line_expects_a_korean_origin()
        {
            var line = Line(50, "Solo Leveling", 14);
            line.Medium = "manhwa";
            GivenCatalogueLine("Solo Leveling", LibraryType.Manga, line);

            Subject.GetSeries("Solo Leveling", 0, resolveVolumeDetails: false, LibraryType.Manga);

            VerifyExpectedOrigin("KR");
        }

        [Test]
        public void a_manga_hint_line_expects_no_origin()
        {
            var line = Line(51, "Attack on Titan", 34);
            line.Medium = "manga";
            GivenCatalogueLine("Attack on Titan", LibraryType.Manga, line);

            Subject.GetSeries("Attack on Titan", 0, resolveVolumeDetails: false, LibraryType.Manga);

            VerifyExpectedOrigin(null);
        }

        [Test]
        public void a_series_bound_by_id_on_a_manhwa_line_keeps_its_entry_whatever_its_origin()
        {
            // Review Focus 5: Fix Match / a stored binding goes through GetById, which has no guard.
            var line = Line(52, "Solo Leveling", 14);
            line.Medium = "manhwa";
            GivenCatalogueLine("Solo Leveling", LibraryType.Manga, line);
            var japanese = Bound(1, "id", "Solo Leveling");
            japanese.CountryOfOrigin = "JP";
            Mocker.GetMock<IAniListService>().Setup(s => s.GetById(1)).Returns(japanese);

            var series = Subject.GetSeries("Solo Leveling", 0, resolveVolumeDetails: false, LibraryType.Manga, anilistId: 1);

            series.AniListId.Should().Be(1);
            VerifyNoSearch();
        }

        // ---- KR/CN piece 2 (2026-10-02, M6): the origin total ----

        [Test]
        public void a_manhwa_lines_total_ignores_the_mangadex_aggregate()
        {
            GivenAniList(new AniListSeries { Id = 105398, EnglishTitle = "Solo Leveling", Status = "RELEASING", Volumes = null, CountryOfOrigin = "KR", MatchedVia = "primary" });
            GivenCatalogueLine("Solo Leveling", LibraryType.Manga, ComicLine(60, "Solo Leveling", 15, "manhwa"));
            GivenCounts(mangaDexHighest: 120, mangaUpdatesVolumes: 0);

            var series = Subject.GetSeries("Solo Leveling", 0, resolveVolumeDetails: true, LibraryType.Manga);

            series.VolumeCount.Should().Be(15);
            series.OriginTotal.Should().Be(15);
        }

        [Test]
        public void a_manhwa_lines_total_takes_the_finished_print_count()
        {
            // The English line is still ongoing (a null status would read AniList's FINISHED as Ended and skip the ladder).
            GivenAniList(new AniListSeries { Id = 105398, EnglishTitle = "Solo Leveling", Status = "FINISHED", Volumes = 14, CountryOfOrigin = "KR", MatchedVia = "primary" });
            var line = ComicLine(61, "Solo Leveling", 11, "manhwa");
            line.Status = "ongoing";
            GivenCatalogueLine("Solo Leveling", LibraryType.Manga, line);
            GivenCounts(mangaDexHighest: 120, mangaUpdatesVolumes: 0);

            Subject.GetSeries("Solo Leveling", 0, resolveVolumeDetails: true, LibraryType.Manga).OriginTotal.Should().Be(14);
        }

        [Test]
        public void a_manhwa_lines_total_takes_mangaupdates_print_count_alone()
        {
            GivenAniList(new AniListSeries { Id = 105398, EnglishTitle = "Solo Leveling", Status = "RELEASING", Volumes = null, CountryOfOrigin = "KR", MatchedVia = "primary" });
            GivenCatalogueLine("Solo Leveling", LibraryType.Manga, ComicLine(62, "Solo Leveling", 11, "manhwa"));
            GivenCounts(mangaDexHighest: 120, mangaUpdatesVolumes: 14);

            Subject.GetSeries("Solo Leveling", 0, resolveVolumeDetails: true, LibraryType.Manga).OriginTotal.Should().Be(14);
        }

        [Test]
        public void a_manhwa_line_without_an_anilist_answer_reads_its_medium()
        {
            GivenAniList(null);
            GivenCatalogueLine("Solo Leveling", LibraryType.Manga, ComicLine(63, "Solo Leveling", 10, "manhwa"));
            GivenCounts(mangaDexHighest: 120, mangaUpdatesVolumes: 0);

            Subject.GetSeries("Solo Leveling", 0, resolveVolumeDetails: true, LibraryType.Manga).OriginTotal.Should().Be(10);
        }

        [Test]
        public void a_japanese_line_keeps_the_mangadex_aggregate()
        {
            // Today's behaviour, pinned: Japanese origin still reads MangaDex's highest volume.
            GivenAniList(new AniListSeries { Id = 1, EnglishTitle = "Attack on Titan", Status = "RELEASING", Volumes = null, CountryOfOrigin = "JP", MatchedVia = "primary" });
            GivenCatalogueLine("Attack on Titan", LibraryType.Manga, ComicLine(64, "Attack on Titan", 10, "manga"));
            GivenCounts(mangaDexHighest: 30, mangaUpdatesVolumes: 0);

            Subject.GetSeries("Attack on Titan", 0, resolveVolumeDetails: true, LibraryType.Manga).OriginTotal.Should().Be(30);
        }

        [Test]
        public void an_ended_manhwa_line_keeps_its_own_count()
        {
            // Review Focus 6: the ended / collected gate is untouched.
            GivenAniList(new AniListSeries { Id = 105398, EnglishTitle = "Solo Leveling", Status = "FINISHED", Volumes = 14, CountryOfOrigin = "KR", MatchedVia = "primary" });
            var line = ComicLine(65, "Solo Leveling", 11, "manhwa");
            line.Status = "ended";
            GivenCatalogueLine("Solo Leveling", LibraryType.Manga, line);
            GivenCounts(mangaDexHighest: 120, mangaUpdatesVolumes: 14);

            Subject.GetSeries("Solo Leveling", 0, resolveVolumeDetails: true, LibraryType.Manga).OriginTotal.Should().Be(11);
        }
    }
}
