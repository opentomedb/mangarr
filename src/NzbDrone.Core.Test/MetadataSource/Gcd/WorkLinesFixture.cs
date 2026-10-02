using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Gcd
{
    // Line safety (2026-09-28): the one rule A (Add results), B (Switch Line) and C (import guard) share.
    [TestFixture]
    public class WorkLinesFixture : TestBase
    {
        [Test]
        public void the_spin_off_names_its_main_line()
        {
            var main = WorkLines.MainLine(OtomeLines.SpinOff(), OtomeLines.Work());

            main.TomeId.Should().Be(OtomeLines.MainId);
        }

        [Test]
        public void the_main_line_has_no_main_line_above_it()
        {
            WorkLines.MainLine(OtomeLines.MainLine(), OtomeLines.Work()).Should().BeNull();
        }

        // The manga line is main too, but another medium: it never labels a light novel.
        [Test]
        public void a_main_line_of_another_medium_or_market_does_not_count()
        {
            var work = OtomeLines.Work().Where(l => l.TomeId != OtomeLines.MainId).ToList();

            WorkLines.MainLine(OtomeLines.SpinOff(), work).Should().BeNull();
            WorkLines.Siblings(OtomeLines.SpinOff(), work).Should().BeEmpty();
        }

        [Test]
        public void siblings_are_the_same_market_and_class_only()
        {
            WorkLines.Siblings(OtomeLines.SpinOff(), OtomeLines.Work()).Select(l => l.TomeId).Should().Equal(OtomeLines.MainId);
        }

        // An artifact before tome_work_id (or a line without a language) has no siblings to reason about.
        [Test]
        public void an_older_artifact_has_no_siblings()
        {
            var spinOff = OtomeLines.SpinOff();
            spinOff.TomeWorkId = null;

            WorkLines.Siblings(spinOff, OtomeLines.Work()).Should().BeEmpty();
            WorkLines.MainLine(spinOff, OtomeLines.Work()).Should().BeNull();
            WorkLines.Of(null, spinOff).Should().BeEmpty();
        }

        [Test]
        public void facts_carry_count_publisher_and_the_spin_off_label()
        {
            var facts = WorkLines.Facts(OtomeLines.SpinOff(), OtomeLines.Work(), LibraryType.LightNovel);

            facts.VolumeCount.Should().Be(6);
            facts.Publisher.Should().Be("Seven Seas Entertainment");
            facts.SpinOffOf.Should().Be(OtomeLines.MainName);
        }

        [Test]
        public void the_main_lines_facts_have_no_label_and_a_blank_publisher_is_none()
        {
            var main = OtomeLines.MainLine();
            main.Publisher = " ";

            var facts = WorkLines.Facts(main, OtomeLines.Work(), LibraryType.LightNovel);

            facts.VolumeCount.Should().Be(13);
            facts.Publisher.Should().BeNull();
            facts.SpinOffOf.Should().BeNull();
        }

        [Test]
        public void a_light_novel_line_is_shown_without_its_qualifier_and_a_local_line_by_its_local_name()
        {
            WorkLines.DisplayName(OtomeLines.MainLine(), LibraryType.LightNovel).Should().Be(OtomeLines.MainName);
            WorkLines.DisplayName(OtomeLines.French(), LibraryType.LightNovel).Should().Be("Otome Game Sekai wa Mob ni Kibishii Sekai desu");
        }

        // Review fixes (I6): the same run in another edition is not a spin-off. The review's live examples.
        private static List<GcdSeries> MangaWork(string mainName, string lineName, string language = "en", int? mainOrig = 10, int? lineOrig = 20, bool omnibus = false)
        {
            return new List<GcdSeries>
            {
                new GcdSeries { GcdSeriesId = 1, Name = mainName, Language = language, Medium = "manga", VolumeCount = 56, IsMain = true, TomeId = "rl_main", TomeWorkId = "w", OrigSeriesId = mainOrig },
                new GcdSeries { GcdSeriesId = 2, Name = lineName, Language = language, Medium = "manga", VolumeCount = 18, IsMain = false, TomeId = "rl_line", TomeWorkId = "w", OrigSeriesId = lineOrig, IsOmnibus = omnibus }
            };
        }

        [TestCase("Inuyasha", "Inuyasha (VizBig edition)")]
        [TestCase("Marmalade Boy", "Marmalade Boy (Collector's edition)")]
        [TestCase("Solo Leveling", "Solo Leveling (Second edition)", "ko")]
        [TestCase("Fruits Basket", "Fruits Basket Collector's Edition")]
        [TestCase("One Piece", "One Piece 3-in-1 Edition")]
        [TestCase("Vagabond", "Vagabond VIZBIG")]
        [TestCase("Rurouni Kenshin", "Rurouni Kenshin Kanzenban")]
        [TestCase("Slam Dunk", "Slam Dunk Shinsōban")]
        [TestCase("Berserk", "Berserk Deluxe")]
        [TestCase("Nana", "Nana 2in1")]
        [TestCase("Blame!", "Blame! (Première édition)")]
        public void an_edition_of_the_main_run_is_not_labelled_a_spin_off(string main, string line, string language = "en")
        {
            var work = MangaWork(main, line, language);

            WorkLines.SpinOffOf(work[1], work, LibraryType.Manga).Should().BeNull();
        }

        // Ported from mangarrbot/spinoff.py's tests: rule 2 only counts a MEDIUM word when it is the
        // whole last parenthetical -- these name nothing else there, so they are an edition/format, not
        // a spin-off.
        [TestCase("Solo Leveling", "Solo Leveling (Roman web)", "ko")]
        [TestCase("Solo Leveling", "Solo Leveling (Médias)", "ko")]
        [TestCase("Goblin Slayer", "Goblin Slayer (Roman illustré)")]
        [TestCase("Yurikuma Arashi", "Yurikuma Arashi (Printed media)")]
        [TestCase("Mushoku Tensei", "Mushoku Tensei (Web novel)")]
        public void a_medium_only_last_parenthetical_is_not_labelled_a_spin_off(string main, string line, string language = "en")
        {
            var work = MangaWork(main, line, language);

            WorkLines.SpinOffOf(work[1], work, LibraryType.Manga).Should().BeNull();
        }

        // A medium word short of naming the WHOLE last parenthetical -- or not in the last parenthetical
        // at all -- leaves the line a real spin-off, unlabelled as an edition/format.
        [TestCase("86", "86 (novel series) (Alter)")]
        [TestCase("Rail Wars!", "Rail Wars! (Light novel - side story)")]
        [TestCase("Goblin Slayer", "Goblin Slayer (Illustrated)")]
        [TestCase("Goblin Slayer", "Goblin Slayer (Printed)")]
        public void a_medium_word_short_of_the_whole_last_parenthetical_stays_a_spin_off(string main, string line)
        {
            var work = MangaWork(main, line);

            WorkLines.SpinOffOf(work[1], work, LibraryType.Manga).Should().Be(main);
        }

        // The 2026-09-28 incident line: its parenthetical repeats the shared prefix and names no
        // edition/medium word, so it must keep its spin-off label.
        [Test]
        public void the_otome_spin_off_stays_labelled()
        {
            WorkLines.SpinOffOf(OtomeLines.SpinOff(), OtomeLines.Work(), LibraryType.LightNovel).Should().Be(OtomeLines.MainName);
        }

        [Test]
        public void a_counterpart_of_the_main_line_is_not_a_spin_off()
        {
            var sameOrigin = MangaWork("Monster", "Monster: Another Story", mainOrig: 10, lineOrig: 10);
            var lineIsOrigin = MangaWork("Monster", "Monster Remaster", mainOrig: 2, lineOrig: null);

            WorkLines.SpinOffOf(sameOrigin[1], sameOrigin, LibraryType.Manga).Should().BeNull();
            WorkLines.SpinOffOf(lineIsOrigin[1], lineIsOrigin, LibraryType.Manga).Should().BeNull();
        }

        [Test]
        public void an_omnibus_line_is_not_a_spin_off()
        {
            var work = MangaWork("Monster", "Monster Book", omnibus: true);

            WorkLines.SpinOffOf(work[1], work, LibraryType.Manga).Should().BeNull();
        }

        // Whole words only, and a real spin-off of another origin keeps its label.
        [Test]
        public void a_spin_off_stays_labelled()
        {
            var spinOff = OtomeLines.SpinOff();
            spinOff.OrigSeriesId = 9002;
            var work = OtomeLines.Work();
            work[0].OrigSeriesId = 9001;
            work[1] = spinOff;

            WorkLines.SpinOffOf(spinOff, work, LibraryType.LightNovel).Should().Be(OtomeLines.MainName);

            var releasing = MangaWork("Naruto", "Boruto: Naruto Next Generations Released");
            WorkLines.SpinOffOf(releasing[1], releasing, LibraryType.Manga).Should().Be("Naruto");
        }

        // KR/CN consumer (2026-09-29): the main German line of a Korean work with no original line is not a spin-off.
        [Test]
        public void the_main_german_line_of_a_korean_work_is_not_labelled_a_spin_off()
        {
            var en = new GcdSeries { GcdSeriesId = 5002, Name = "Overgeared", Language = "en", VolumeCount = 11, IsMain = true, Medium = "manhwa", TomeWorkId = "w_og" };
            var de = new GcdSeries { GcdSeriesId = 5001, Name = "Overgeared", Language = "de", VolumeCount = 10, IsMain = true, Medium = "manhwa", TomeWorkId = "w_og" };

            WorkLines.SpinOffOf(de, new List<GcdSeries> { en, de }, LibraryType.Manga).Should().BeNull();
        }

        // KR/CN consumer (2026-09-29, spec §3.4): a series bound to a Japanese line finds its collection root.
        [Test]
        public void a_bound_series_is_found_by_its_line_before_its_title()
        {
            var ja = new GcdSeries { GcdSeriesId = 8001, Name = "Stand Up Start", Language = "ja", TomeId = "rl_ja_sus" };
            var gcd = new Mock<IGcdMetadataService>();
            gcd.Setup(s => s.FindSeriesByTomeId("rl_ja_sus")).Returns(ja);
            gcd.Setup(s => s.FindSeriesByTitle("Stand Up Start", LibraryType.Manga)).Returns((GcdSeries)null);

            WorkLines.LibraryLine(gcd.Object, "rl_ja_sus", "Stand Up Start", LibraryType.Manga).Should().BeSameAs(ja);
            WorkLines.LibraryLine(gcd.Object, null, "Stand Up Start", LibraryType.Manga).Should().BeNull();
        }
    }
}
