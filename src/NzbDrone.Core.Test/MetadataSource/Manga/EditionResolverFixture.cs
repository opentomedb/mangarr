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
    }
}
