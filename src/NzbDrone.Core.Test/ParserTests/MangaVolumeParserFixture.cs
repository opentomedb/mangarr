using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class MangaVolumeParserFixture : CoreTest
    {
        private static ParsedBookInfo Parse(string title)
        {
            var info = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(title, info);
            return info;
        }

        [TestCase("Dandadan v01 (2022) (Digital) (XRA9276)", 1)]
        [TestCase("Chainsaw Man Vol. 5 [CBZ]", 5)]
        [TestCase("Spy x Family Volume 12 (2023)", 12)]
        [TestCase("Berserk v38", 38)]
        [TestCase("One Piece Vol.103", 103)]
        [TestCase("Solo Leveling Book 4", 4)]
        [TestCase("Solo Leveling Book 10", 10)]
        public void should_parse_single_volume(string title, int expected)
        {
            var info = Parse(title);
            info.VolumeNumber.Should().Be(expected);
            info.VolumeStart.Should().BeNull();
            info.VolumeEnd.Should().BeNull();
        }

        [TestCase("Dandadan Volumes 1 to 13 (2023) (Digital)", 1, 13)]
        [TestCase("Chainsaw Man Vol. 1-11 [CBZ]", 1, 11)]
        [TestCase("Spy x Family v01-v12 Complete", 1, 12)]
        [TestCase("Berserk Volumes 1-41", 1, 41)]
        [TestCase("Solo Leveling Books 1-5 (Comic)", 1, 5)]
        [TestCase("Solo Leveling Books 1 to 5", 1, 5)]
        [TestCase("Chainsaw Man v01―02", 1, 2)]   // U+2015 horizontal bar: any Unicode dash joins a range
        public void should_parse_volume_range(string title, int start, int end)
        {
            var info = Parse(title);
            info.VolumeStart.Should().Be(start);
            info.VolumeEnd.Should().Be(end);
            info.VolumeNumber.Should().BeNull();
        }

        [TestCase("第5巻 ダンダダン")]
        public void should_parse_cjk_volume(string title)
        {
            Parse(title).VolumeNumber.Should().Be(5);
        }

        [TestCase("Some Random Book Title (2020)")]
        [TestCase("Author Name - A Novel")]
        [TestCase("The Manga Cookbook 2 (2020)")]
        [TestCase("Booklet 5 of the Collection")]
        public void should_leave_volume_null_when_absent(string title)
        {
            var info = Parse(title);
            info.VolumeNumber.Should().BeNull();
            info.VolumeStart.Should().BeNull();
            info.VolumeEnd.Should().BeNull();
        }

        [TestCase("Chainsaw Man - Vol 001", "Chainsaw Man", 1)]
        [TestCase("Chainsaw Man - Vol 016", "Chainsaw Man", 16)]
        [TestCase("Dandadan v03", "Dandadan", 3)]
        [TestCase("Spy x Family Volume 12", "Spy x Family", 12)]
        [TestCase("Chainsaw_Man_v01", "Chainsaw Man", 1)]
        [TestCase("Berserk.v38", "Berserk", 38)]
        [TestCase("Chainsaw Man, Vol. 2 by Tatsuki Fujimoto", "Chainsaw Man", 2)]
        [TestCase("Gyo Vol.1 - Vol.2 (2003-2004) (VIZ Media LLC) by Junji Ito [ENG / CBZ]", "Gyo", 1)]
        [TestCase("Solo Leveling Book 4", "Solo Leveling", 4)]
        [TestCase("Solo Leveling Book 10", "Solo Leveling", 10)]
        public void should_parse_series_and_volume(string fileName, string expectedSeries, int expectedVolume)
        {
            MangaVolumeParser.TryParseSeriesVolume(fileName, out var series, out var volume).Should().BeTrue();
            series.Should().Be(expectedSeries);
            volume.Should().Be(expectedVolume);
        }

        [TestCase("Some Random Book Title")]
        [TestCase("Author Name - A Novel")]
        public void should_not_parse_series_volume_when_absent(string fileName)
        {
            MangaVolumeParser.TryParseSeriesVolume(fileName, out _, out _).Should().BeFalse();
        }

        [TestCase("Chainsaw Man Vol. 3", 3)]
        [TestCase("Berserk v38", 38)]
        [TestCase("Solo Leveling Book 5", 5)]
        [TestCase("Some Novel", 0)]
        [TestCase("The Manga Cookbook 2", 0)]
        [TestCase("", 0)]
        public void should_parse_single_volume_number(string text, int expected)
        {
            MangaVolumeParser.ParseSingleVolume(text).Should().Be(expected);
        }

        [TestCase("Berserk Vol. 3.5 [CBZ]", 3.5)]
        [TestCase("Spy x Family v07.5 (2023)", 7.5)]
        [TestCase("One Piece Volume 10.5", 10.5)]
        public void should_parse_fractional_single_volume(string title, double expected)
        {
            var info = Parse(title);
            info.VolumeNumber.Should().Be(expected);
            info.VolumeStart.Should().BeNull();
            info.VolumeEnd.Should().BeNull();
        }

        // The .5 side-story volume must survive the file-name path (separator normalization must not
        // strip the decimal point) and must NOT collapse onto the whole volume (the old truncation bug).
        [TestCase("Berserk - Vol 3.5", "Berserk", 3.5)]
        [TestCase("Chainsaw Man v07.5", "Chainsaw Man", 7.5)]
        [TestCase("Vinland_Saga_v10.5", "Vinland Saga", 10.5)]
        public void should_parse_fractional_series_volume(string fileName, string expectedSeries, double expectedVolume)
        {
            MangaVolumeParser.TryParseSeriesVolume(fileName, out var series, out var volume).Should().BeTrue();
            series.Should().Be(expectedSeries);
            volume.Should().Be(expectedVolume);
        }

        [Test]
        public void fractional_and_whole_volume_are_distinct()
        {
            MangaVolumeParser.ParseSingleVolume("Berserk Vol. 3").Should().Be(3);
            MangaVolumeParser.ParseSingleVolume("Berserk Vol. 3.5").Should().Be(3.5);

            MangaVolumeParser.TryParseSeriesVolume("Berserk - Vol 3", out _, out var whole).Should().BeTrue();
            MangaVolumeParser.TryParseSeriesVolume("Berserk - Vol 3.5", out _, out var half).Should().BeTrue();
            whole.Should().Be(3);
            half.Should().Be(3.5);
        }

        // Publisher/format parentheticals must be stripped before matching a release's parsed
        // series to a library author, so "... (Light Novel)" still attributes to the series.
        [TestCase("Mushoku Tensei: Jobless Reincarnation (Light Novel)", "Mushoku Tensei: Jobless Reincarnation")]
        [TestCase("Jujutsu Kaisen (Digital)", "Jujutsu Kaisen")]
        [TestCase("Spy x Family (Omnibus) (2023)", "Spy x Family")]
        [TestCase("Chainsaw Man", "Chainsaw Man")]
        public void should_strip_parentheticals_for_matching(string input, string expected)
        {
            MangaVolumeParser.StripParentheticals(input).Should().Be(expected);
        }

        // TryParseSeries recognises the series name from BOTH single volumes and pack ranges,
        // so a whole-series pack release can be attributed to its series (the single-volume-only
        // TryParseSeriesVolume can't, which is why packs used to be rejected at the door).
        [TestCase("Dandadan v03", "Dandadan")]
        [TestCase("Chainsaw Man Vol. 3", "Chainsaw Man")]
        [TestCase("Dandadan Volumes 1 to 13 (2023) (Digital)", "Dandadan")]
        [TestCase("Chainsaw Man Vol. 1-11 [CBZ]", "Chainsaw Man")]
        [TestCase("Spy x Family v01-v12 Complete", "Spy x Family")]
        [TestCase("Berserk Volumes 1-41", "Berserk")]
        [TestCase("Solo Leveling Books 1 to 5", "Solo Leveling")]
        // Leading scene/format tags must be stripped so they don't poison the series name.
        [TestCase("[Manga] Tokyo Ghoul v01", "Tokyo Ghoul")]
        [TestCase("[danke-Empire] Berserk Vol. 10", "Berserk")]
        public void should_parse_series_from_single_or_pack(string title, string expectedSeries)
        {
            MangaVolumeParser.TryParseSeries(title, out var series).Should().BeTrue();
            series.Should().Be(expectedSeries);
        }

        [TestCase("Some Random Book Title")]
        [TestCase("Author Name - A Novel")]
        public void should_not_parse_series_when_no_volume_token(string title)
        {
            MangaVolumeParser.TryParseSeries(title, out _).Should().BeFalse();
        }

        // HasVolumeToken is the boolean used by the quality parser to default manga to CBZ; it
        // must be true for single volumes, fractional volumes, CJK, AND pack ranges, but false
        // for releases that carry no volume token at all.
        [TestCase("Jujutsu Kaisen v25 (2025)")]
        [TestCase("Demon Slayer Vol. 23 (2021)")]
        [TestCase("Berserk Vol. 3.5 [CBZ]")]
        [TestCase("第5巻 ダンダダン")]
        [TestCase("Dandadan Volumes 1 to 13 (2023)")]
        [TestCase("Spy x Family v01-v12 Complete")]
        [TestCase("Solo Leveling Book 4")]
        public void should_detect_volume_token(string title)
        {
            MangaVolumeParser.HasVolumeToken(title).Should().BeTrue();
        }

        [TestCase("Some Random Audiobook Without Format Tag (2016)")]
        [TestCase("The Manga Cookbook 2 (2020)")]
        [TestCase("")]
        public void should_not_detect_volume_token_when_absent(string title)
        {
            MangaVolumeParser.HasVolumeToken(title).Should().BeFalse();
        }
        [TestCase("Tensei Shitara Slime Datta Ken / That Time I Got Reincarnated as a Slime", "Tensei Shitara Slime Datta Ken", "That Time I Got Reincarnated as a Slime")]
        [TestCase("Kekkon suru tte, Hontou desu ka | 365 Days to the Wedding", "Kekkon suru tte, Hontou desu ka", "365 Days to the Wedding")]
        public void should_expand_dual_titles_to_both_halves(string series, string first, string second)
        {
            var readings = MangaVolumeParser.ExpandDualTitles(series);

            readings.Should().ContainInOrder(series, first, second);
        }

        [TestCase("Tokyo Ghoul - re")]
        [TestCase("Spy x Family")]
        [TestCase("Re:ZERO -Starting Life in Another World-")]
        public void should_not_split_ordinary_series_names(string series)
        {
            MangaVolumeParser.ExpandDualTitles(series).Should().Equal(series);
        }

        [Test]
        public void should_offer_leading_bracket_contents_as_last_series_candidate()
        {
            var candidates = MangaVolumeParser.GetSeriesCandidates("[Manga] Tokyo Ghoul v01 (2015)");
            candidates.First().Should().Be("Tokyo Ghoul");
            candidates.Last().Should().Be("Manga");

            MangaVolumeParser.GetSeriesCandidates("[Oshi No Ko] v13 (2026) (Digital) (ShelfLife)")
                .Should().Equal("Oshi No Ko");
        }

        [Test]
        public void should_offer_dual_title_halves_for_tokenless_batches()
        {
            MangaVolumeParser.GetBatchSeriesCandidates("Tensei Shitara Slime Datta Ken / That Time I Got Reincarnated as a Slime (Digital) (danke-Empire)")
                .Should().Equal(
                    "Tensei Shitara Slime Datta Ken / That Time I Got Reincarnated as a Slime",
                    "Tensei Shitara Slime Datta Ken",
                    "That Time I Got Reincarnated as a Slime");
        }
    }
}
