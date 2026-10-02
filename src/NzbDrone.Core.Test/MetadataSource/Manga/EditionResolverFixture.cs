using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    // Preferred Edition (2026-09-24, spec §2.1 step 3): from the English anchor to the edition line of
    // the same work -- counterpart, then is_main, then not a collected edition, then dated / volume
    // count / id; the first chain language with a line of >= 1 volume wins.
    [TestFixture]
    public class EditionResolverFixture : CoreTest<EditionResolver>
    {
        private static GcdSeries Line(int id, string language, int volumes, int? orig = null, bool main = true, bool omnibus = false, string medium = "manga", int dated = 0, string tomeId = null)
        {
            return new GcdSeries
            {
                GcdSeriesId = id,
                Name = "Attack on Titan",
                Language = language,
                VolumeCount = volumes,
                OrigSeriesId = orig,
                IsMain = main,
                IsOmnibus = omnibus,
                Medium = medium,
                DatedCount = dated,
                TomeId = tomeId ?? "rl_" + id,
                TomeWorkId = "w_aot"
            };
        }

        private static readonly GcdSeries JaOrigin = Line(1003, "ja", 34);
        private static readonly GcdSeries Anchor = Line(1001, "en", 34, orig: 1003);

        private void GivenWork(params GcdSeries[] lines)
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetWorkLines("w_aot")).Returns(lines.ToList());
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetVolumes(It.IsAny<int>())).Returns(new List<GcdVolume>());
        }

        private EditionResolution Resolve(params string[] chain)
        {
            return Subject.Resolve(Anchor, new EditionRequest { Chain = chain }, LibraryType.Manga, "Attack on Titan");
        }

        [Test]
        public void the_french_counterpart_beats_a_french_omnibus()
        {
            // Both are main counterparts, so the collected-edition check decides, not is_main.
            GivenWork(JaOrigin, Anchor, Line(2001, "fr", 12, orig: 1003, omnibus: true, dated: 12), Line(2002, "fr", 34, orig: 1003));

            var r = Resolve("fr", "en");

            r.Line.GcdSeriesId.Should().Be(2002);
            r.Language.Should().Be("fr");
            r.FromBinding.Should().BeFalse();
        }

        [Test]
        public void a_regular_german_band_line_beats_the_carlsen_omnibus_even_with_more_dates()
        {
            GivenWork(JaOrigin, Anchor, Line(3001, "de", 17, orig: 1003, omnibus: true, dated: 17), Line(3002, "de", 34, orig: 1003, dated: 10));

            Resolve("de").Line.GcdSeriesId.Should().Be(3002);
        }

        [Test]
        public void a_book_edition_line_counts_as_collected_by_its_page_counts()
        {
            GivenWork(JaOrigin, Anchor, Line(3001, "de", 17, orig: 1003, dated: 17), Line(3002, "de", 34, orig: 1003, dated: 10));
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetVolumes(3001)).Returns(new List<GcdVolume>
            {
                new GcdVolume { VolumeNumber = 1, PageCount = 420 },
                new GcdVolume { VolumeNumber = 2, PageCount = 430 },
                new GcdVolume { VolumeNumber = 3, PageCount = 410 }
            });

            Resolve("de").Line.GcdSeriesId.Should().Be(3002);
        }

        [Test]
        public void the_counterpart_beats_a_line_of_another_origin_with_more_dates_and_volumes()
        {
            GivenWork(JaOrigin, Anchor, Line(2003, "fr", 40, orig: 9003, dated: 40), Line(2002, "fr", 34, orig: 1003));

            Resolve("fr").Line.GcdSeriesId.Should().Be(2002);
        }

        [Test]
        public void a_market_whose_only_line_is_an_omnibus_still_resolves_to_it()
        {
            GivenWork(JaOrigin, Anchor, Line(3001, "de", 17, orig: 1003, omnibus: true, dated: 17));

            var r = Resolve("de", "en");

            r.Line.GcdSeriesId.Should().Be(3001);
            r.Language.Should().Be("de");
        }

        [Test]
        public void the_first_chain_language_with_a_line_wins()
        {
            GivenWork(JaOrigin, Anchor, Line(2002, "fr", 34, orig: 1003));

            var r = Resolve("it", "fr", "en");

            r.Language.Should().Be("fr");
            r.Line.GcdSeriesId.Should().Be(2002);
        }

        [Test]
        public void a_zero_volume_line_never_wins()
        {
            GivenWork(JaOrigin, Anchor, Line(2002, "fr", 0, orig: 1003), Line(3002, "de", 34, orig: 1003));

            Resolve("fr", "de").Language.Should().Be("de");
        }

        [Test]
        public void english_in_the_chain_returns_the_anchor()
        {
            GivenWork(JaOrigin, Anchor, Line(2002, "fr", 34, orig: 1003));

            var r = Resolve("en", "fr");

            r.Line.Should().BeSameAs(Anchor);
            r.Language.Should().Be("en");
        }

        [Test]
        public void a_bound_line_is_fetched_by_id_without_the_hop()
        {
            var bound = Line(2002, "fr", 34, orig: 1003, tomeId: "rl_fr");
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_fr")).Returns(bound);

            var r = Subject.Resolve(Anchor, new EditionRequest { Language = "fr", TomeLineId = "rl_fr" }, LibraryType.Manga, "Attack on Titan");

            r.Line.Should().BeSameAs(bound);
            r.FromBinding.Should().BeTrue();
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.GetWorkLines(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_vanished_bound_line_resolves_the_same_language_again_and_never_another()
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_fr")).Returns((GcdSeries)null);
            GivenWork(JaOrigin, Anchor, Line(3002, "de", 34, orig: 1003));

            // Final fix round I3 (2026-09-24): no French successor -> EditionUnavailableException (the entry is
            // left as stored), never the German line.
            var ex = Assert.Throws<EditionUnavailableException>(() =>
                Subject.Resolve(Anchor, new EditionRequest { Language = "fr", TomeLineId = "rl_fr" }, LibraryType.Manga, "Attack on Titan"));

            ex.Language.Should().Be("fr");
            ex.TomeLineId.Should().Be("rl_fr");
            ExceptionVerification.ExpectedWarns(1);
        }

        // Final fix round I3 (2026-09-24): a vanished binding is never silently moved to a collected edition --
        // DE "Band" gone and only the Carlsen "Massiv" omnibus left would renumber the series (D9). The NEW
        // add of a collected-only market still resolves to it (a_market_whose_only_line_is_an_omnibus_...).
        [Test]
        public void a_vanished_band_line_with_only_the_massiv_omnibus_left_is_unavailable()
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_de_band")).Returns((GcdSeries)null);
            GivenWork(JaOrigin, Anchor, Line(3001, "de", 17, orig: 1003, omnibus: true, dated: 17));

            var ex = Assert.Throws<EditionUnavailableException>(() =>
                Subject.Resolve(Anchor, new EditionRequest { Language = "de", TomeLineId = "rl_de_band" }, LibraryType.Manga, "Attack on Titan"));

            ex.Language.Should().Be("de");
            ExceptionVerification.ExpectedWarns(1);
        }

        // Final fix round I3: a regular line of another origin is not the same edition either.
        [Test]
        public void a_vanished_line_with_only_a_line_of_another_origin_left_is_unavailable()
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_de_band")).Returns((GcdSeries)null);
            GivenWork(JaOrigin, Anchor, Line(3003, "de", 40, orig: 9003, dated: 40));

            Assert.Throws<EditionUnavailableException>(() =>
                Subject.Resolve(Anchor, new EditionRequest { Language = "de", TomeLineId = "rl_de_band" }, LibraryType.Manga, "Attack on Titan"));

            ExceptionVerification.ExpectedWarns(1);
        }

        // Final fix round I3: the regular counterpart is still taken over the omnibus, with the Warn, as before.
        [Test]
        public void a_vanished_band_line_rebinds_to_the_regular_counterpart_with_a_warn()
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_de_band")).Returns((GcdSeries)null);
            GivenWork(JaOrigin, Anchor, Line(3001, "de", 17, orig: 1003, omnibus: true, dated: 17), Line(3002, "de", 34, orig: 1003, dated: 10));

            var r = Subject.Resolve(Anchor, new EditionRequest { Language = "de", TomeLineId = "rl_de_band" }, LibraryType.Manga, "Attack on Titan");

            r.Line.GcdSeriesId.Should().Be(3002);
            r.Language.Should().Be("de");
            r.FromBinding.Should().BeFalse();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_vanished_bound_line_rebinds_to_the_same_language_successor()
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_fr")).Returns((GcdSeries)null);
            GivenWork(JaOrigin, Anchor, Line(2009, "fr", 34, orig: 1003));

            var r = Subject.Resolve(Anchor, new EditionRequest { Language = "fr", TomeLineId = "rl_fr" }, LibraryType.Manga, "Attack on Titan");

            r.Line.GcdSeriesId.Should().Be(2009);
            r.FromBinding.Should().BeFalse();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void no_english_line_ranks_the_title_in_the_chain_languages()
        {
            var fr = Line(2002, "fr", 12);
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.FindSeriesByTitle("Les Gouttes de Dieu", LibraryType.Manga, It.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "fr" }))))
                  .Returns(fr);

            var r = Subject.Resolve(null, new EditionRequest { Chain = new[] { "fr", "en" } }, LibraryType.Manga, "Les Gouttes de Dieu");

            r.Line.Should().BeSameAs(fr);
            r.Language.Should().Be("fr");
        }

        [Test]
        public void no_english_line_skips_a_zero_volume_stub_for_the_next_chain_language()
        {
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.FindSeriesByTitle("Les Gouttes de Dieu", LibraryType.Manga, It.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "fr" }))))
                  .Returns(Line(2002, "fr", 0));
            var de = Line(3002, "de", 12);
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.FindSeriesByTitle("Les Gouttes de Dieu", LibraryType.Manga, It.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "de" }))))
                  .Returns(de);

            var r = Subject.Resolve(null, new EditionRequest { Chain = new[] { "fr", "de", "en" } }, LibraryType.Manga, "Les Gouttes de Dieu");

            r.Line.Should().BeSameAs(de);
            r.Language.Should().Be("de");
        }

        [Test]
        public void a_bound_line_in_another_language_warns_and_resolves_the_requested_language()
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_fr")).Returns(Line(3002, "de", 34, orig: 1003));
            GivenWork(JaOrigin, Anchor, Line(2009, "fr", 34, orig: 1003));

            var r = Subject.Resolve(Anchor, new EditionRequest { Language = " fr ", TomeLineId = "rl_fr" }, LibraryType.Manga, "Attack on Titan");

            r.Line.GcdSeriesId.Should().Be(2009);
            r.FromBinding.Should().BeFalse();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_bound_line_matches_its_language_after_trimming()
        {
            var bound = Line(2002, "fr", 34, orig: 1003, tomeId: "rl_fr");
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_fr")).Returns(bound);

            var r = Subject.Resolve(Anchor, new EditionRequest { Language = "fr ", TomeLineId = "rl_fr" }, LibraryType.Manga, "Attack on Titan");

            r.Line.Should().BeSameAs(bound);
            r.FromBinding.Should().BeTrue();
        }

        [Test]
        public void an_anchor_without_a_work_id_resolves_nothing()
        {
            var old = new GcdSeries { GcdSeriesId = 1001, Name = "Attack on Titan", Language = "en", VolumeCount = 34 };

            Subject.Resolve(old, new EditionRequest { Chain = new[] { "fr" } }, LibraryType.Manga, "Attack on Titan").Should().BeNull();
        }

        [Test]
        public void a_light_novel_anchor_ignores_manga_lines()
        {
            var lnAnchor = Line(5001, "en", 3, orig: 5003, medium: "light_novel");
            GivenWork(lnAnchor, Line(2002, "fr", 34, orig: 1003), Line(5002, "fr", 3, orig: 5003, medium: "light_novel"));

            Subject.Resolve(lnAnchor, new EditionRequest { Chain = new[] { "fr" } }, LibraryType.LightNovel, "Attack on Titan").Line.GcdSeriesId.Should().Be(5002);
        }

        [Test]
        public void the_japanese_origin_line_is_the_counterpart()
        {
            GivenWork(JaOrigin, Anchor, Line(4002, "ja", 40, main: true, dated: 40));

            Resolve("ja").Line.GcdSeriesId.Should().Be(1003);
        }

        [Test]
        public void options_list_each_language_once_with_its_line()
        {
            GivenWork(JaOrigin, Anchor, Line(2002, "fr", 20, orig: 1003), Line(2001, "fr", 12, orig: 1003, omnibus: true), Line(3002, "de", 34, orig: 1003));

            var options = Subject.Options(Anchor, LibraryType.Manga);

            options.Select(o => o.Language).Should().Equal("en", "de", "fr", "ja");
            options.Single(o => o.Language == "fr").VolumeCount.Should().Be(20);
            options.Single(o => o.Language == "fr").Name.Should().Be("French");
        }

        // KR/CN consumer (2026-09-29, spec §3.3): a work with no Korean original line in the catalogue.
        private static GcdSeries Kr(int id, string language, bool main = true, string work = "w_og")
        {
            return new GcdSeries { GcdSeriesId = id, Name = "Overgeared", Language = language, VolumeCount = 10, IsMain = main, Medium = "manhwa", TomeId = "rl_" + id, TomeWorkId = work };
        }

        [Test]
        public void main_lines_of_a_work_without_an_original_are_counterparts()
        {
            EditionResolver.IsCounterpart(Kr(5002, "en"), Kr(5001, "de")).Should().BeTrue();
            EditionResolver.IsCounterpart(Kr(5001, "de"), Kr(5004, "fr")).Should().BeTrue();
        }

        [Test]
        public void a_side_line_is_not_a_counterpart_of_the_main_line()
        {
            EditionResolver.IsCounterpart(Kr(5002, "en"), Kr(5003, "de", main: false)).Should().BeFalse();
        }

        [Test]
        public void main_lines_of_different_works_are_not_counterparts()
        {
            EditionResolver.IsCounterpart(Kr(5002, "en"), Kr(7001, "de", work: "w_other")).Should().BeFalse();
        }

        [Test]
        public void a_japanese_work_keeps_the_orig_series_rule()
        {
            // Anchor has orig 1003; a main German line pointing elsewhere is not a counterpart even though both are main.
            EditionResolver.IsCounterpart(Anchor, Line(3009, "de", 10, orig: 9999)).Should().BeFalse();
            EditionResolver.IsCounterpart(Anchor, Line(3002, "de", 34, orig: 1003)).Should().BeTrue();
        }

        [Test]
        public void resolving_german_for_a_korean_work_picks_the_main_german_line()
        {
            var en = Kr(5002, "en");
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetWorkLines("w_og")).Returns(new List<GcdSeries> { en, Kr(5001, "de"), Kr(5003, "de", main: false) });
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetVolumes(It.IsAny<int>())).Returns(new List<GcdVolume>());

            Subject.Resolve(en, new EditionRequest { Chain = new[] { "de" } }, LibraryType.Manga, "Overgeared").Line.GcdSeriesId.Should().Be(5001);
        }

        // KR/CN consumer (2026-09-29, spec §3.2): the line a new entry falls back to.
        private static GcdSeries W(int id, string language, int volumes = 10, int? orig = null, bool main = true, string medium = "manga", string work = "w_x")
        {
            return new GcdSeries { GcdSeriesId = id, Name = "X", Language = language, VolumeCount = volumes, OrigSeriesId = orig, IsMain = main, Medium = medium, TomeId = "rl_" + id, TomeWorkId = work };
        }

        private void GivenLines(params GcdSeries[] lines)
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetWorkLines("w_x")).Returns(lines.ToList());
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetVolumes(It.IsAny<int>())).Returns(new List<GcdVolume>());
        }

        [Test]
        public void a_japanese_only_work_falls_back_to_its_japanese_line()
        {
            var ja = W(1, "ja");
            GivenLines(ja);

            var r = Subject.ResolveFallback(ja, new[] { "en" }, LibraryType.Manga);

            r.Line.GcdSeriesId.Should().Be(1);
            r.Language.Should().Be("ja");
        }

        [Test]
        public void the_original_beats_a_french_edition_for_an_english_user()
        {
            var ja = W(1, "ja");
            var fr = W(2, "fr", orig: 1);
            GivenLines(ja, fr);

            Subject.ResolveFallback(fr, new[] { "en" }, LibraryType.Manga).Line.GcdSeriesId.Should().Be(1);
        }

        [Test]
        public void a_chain_language_line_the_title_missed_comes_first()
        {
            var ja = W(1, "ja");
            var fr = W(2, "fr", orig: 1);
            GivenLines(ja, fr);

            Subject.ResolveFallback(ja, new[] { "fr", "en" }, LibraryType.Manga).Language.Should().Be("fr");
        }

        [Test]
        public void an_english_line_found_by_id_is_taken_as_english()
        {
            var en = W(3, "en");
            var de = W(4, "de");
            GivenLines(en, de);

            Subject.ResolveFallback(de, new[] { "en" }, LibraryType.Manga).Line.GcdSeriesId.Should().Be(3);
        }

        [Test]
        public void a_german_french_work_without_an_original_follows_rank_order()
        {
            // Final fix wave C1: the found line itself would win, so it is an empty stub here (not a candidate)
            // and its two counterparts are ranked.
            var de = W(4, "de", volumes: 12);
            var it = W(6, "it", volumes: 8);
            var fr = W(5, "fr", volumes: 0);
            GivenLines(de, it, fr);

            Subject.ResolveFallback(fr, new[] { "en" }, LibraryType.Manga).Line.GcdSeriesId.Should().Be(4);
        }

        [Test]
        public void a_german_french_work_without_an_original_keeps_the_found_line()
        {
            var de = W(4, "de", volumes: 12);
            var fr = W(5, "fr", volumes: 8);
            GivenLines(de, fr);

            Subject.ResolveFallback(fr, new[] { "en" }, LibraryType.Manga).Line.GcdSeriesId.Should().Be(5);
        }

        // Final fix wave C1: a search that found a spin-off binds the spin-off, never its work's main line.
        [Test]
        public void a_spin_off_hit_of_a_japanese_only_work_binds_the_spin_off()
        {
            var main = W(1, "ja", volumes: 30);
            var spin = W(2, "ja", volumes: 3, main: false);
            GivenLines(main, spin);

            Subject.ResolveFallback(spin, new[] { "en" }, LibraryType.Manga).Line.GcdSeriesId.Should().Be(2);
        }

        [Test]
        public void a_spin_off_hit_never_binds_the_licensed_english_main_line()
        {
            var jaMain = W(1, "ja", volumes: 30);
            var enMain = W(3, "en", volumes: 25, orig: 1);
            var spin = W(2, "ja", volumes: 3, main: false);
            GivenLines(jaMain, enMain, spin);

            var r = Subject.ResolveFallback(spin, new[] { "en" }, LibraryType.Manga);

            r.Line.GcdSeriesId.Should().Be(2);
            r.Language.Should().Be("ja");
        }

        [Test]
        public void the_chain_order_decides_between_two_chain_counterparts()
        {
            var ja = W(1, "ja");
            var de = W(4, "de", volumes: 20, orig: 1);
            var fr = W(5, "fr", volumes: 5, orig: 1);
            GivenLines(ja, de, fr);

            Subject.ResolveFallback(ja, new[] { "fr", "de" }, LibraryType.Manga).Line.GcdSeriesId.Should().Be(5);
        }

        [Test]
        public void the_line_others_point_at_wins_among_original_language_lines()
        {
            var jaMain = W(1, "ja", volumes: 20);
            var jaOther = W(6, "ja", volumes: 30, main: false);
            var fr = W(2, "fr", orig: 1);
            GivenLines(jaOther, jaMain, fr);

            Subject.ResolveFallback(fr, new[] { "en" }, LibraryType.Manga).Line.GcdSeriesId.Should().Be(1);
        }

        [Test]
        public void wrong_library_class_and_empty_lines_are_skipped()
        {
            var jaNovel = W(1, "ja", medium: "light_novel");
            var jaEmpty = W(7, "ja", volumes: 0);
            GivenLines(jaNovel, jaEmpty);

            Subject.ResolveFallback(jaEmpty, new[] { "en" }, LibraryType.Manga).Should().BeNull();
        }

        [Test]
        public void a_line_without_a_work_is_its_own_work()
        {
            var lone = new GcdSeries { GcdSeriesId = 9, Name = "Lone", Language = "ko", VolumeCount = 3, IsMain = true, Medium = "manhwa" };
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetVolumes(It.IsAny<int>())).Returns(new List<GcdVolume>());

            Subject.ResolveFallback(lone, new[] { "en" }, LibraryType.Manga).Line.GcdSeriesId.Should().Be(9);
        }
    }
}
