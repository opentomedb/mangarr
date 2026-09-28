using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    // D5 (2026-09-17): a Google ISBN record is used only when its title names the series. Fairy
    // Tail 24/26/28/31's ISBNs answer an unrelated adult title on Google; its cover and blurb were
    // shown as the volume's. Prefix, not contains: the volume number is the ISBN's job.
    [TestFixture]
    public class GoogleRecordHygieneFixture : CoreTest
    {
        [TestCase("Fairy Tail 24", "Fairy Tail", true)]
        [TestCase("Fairy Tail, Vol. 24 (Fairy Tail)", "Fairy Tail", true)]
        [TestCase("Sword Art Online 2: Aincrad (light novel)", "Sword Art Online", true)]
        [TestCase("Re:ZERO -Starting Life in Another World-, Vol. 1 (light novel)", "Re:Zero", true)]   // on the display name: "rezero" is its prefix
        [TestCase("Re:Zero kara Hajimeru Isekai Seikatsu 1", "Re:ZERO -Starting Life in Another World-", true)] // via the romaji alias
        [TestCase("K-ON!, Vol. 1", "K-ON!", true)]                                                    // the display name is exempt from the 4-char floor
        [TestCase("Sword Art Online Progressive Vol. 1", "Sword Art Online", true)]                   // prefix: the volume number check is the ISBN's job
        [TestCase("Fushigi Y\u00fbgi, Vol. 3", "Fushigi Yugi", true)]                                  // TitleFold (2026-09-24): an accented record names the plain series
        [TestCase("Fushigi Yugi, Vol. 3", "Fushigi Y\u00fbgi", true)]                                  // ... and the other way round
        [TestCase("Ranma 1/2, Vol. 3", "Ranma \u00bd", true)]                                          // the numeric fold: both key ranma12
        [TestCase("Ranmaru, Vol. 3", "Ranma \u00bd", false)]                                           // before the fold "Ranma ½" keyed ranma, a prefix of ranmaru
        [TestCase("ぱんつあげるね", "Fairy Tail", false)]
        [TestCase("Pantsu Agerune", "Fairy Tail", false)]
        [TestCase("The Fairy Tail Companion", "Fairy Tail", false)]                                   // contains, not prefix
        [TestCase("", "Fairy Tail", false)]
        [TestCase(null, "Fairy Tail", false)]
        public void record_names_series(string title, string series, bool expected)
        {
            var aliases = new[] { "Re:ZERO -Starting Life in Another World-", "Re:Zero kara Hajimeru Isekai Seikatsu" };
            MangaSeriesMetadataProvider.RecordNamesSeries(new VolumeDetails { Title = title }, series, aliases).Should().Be(expected);
        }

        [Test]
        public void a_short_alias_is_not_a_key()
        {
            // "Re:Z" normalises to three characters: too short to name anything.
            MangaSeriesMetadataProvider.RecordNamesSeries(new VolumeDetails { Title = "Rez Vol. 1" }, "Re:ZERO -Starting Life in Another World-", new[] { "Re:Z" }).Should().BeFalse();
        }

        [Test]
        public void a_null_record_is_a_mismatch()
        {
            MangaSeriesMetadataProvider.RecordNamesSeries(null, "Fairy Tail", null).Should().BeFalse();
        }

        [Test]
        public void a_null_alias_list_still_matches_on_the_display_name()
        {
            MangaSeriesMetadataProvider.RecordNamesSeries(new VolumeDetails { Title = "Fairy Tail 24" }, "Fairy Tail", null).Should().BeTrue();
        }
    }
}
