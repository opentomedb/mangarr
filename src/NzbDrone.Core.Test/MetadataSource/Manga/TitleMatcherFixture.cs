using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    [TestFixture]
    public class TitleMatcherFixture : CoreTest
    {
        [TestCase("Kaiju No.8", "Kaiju No. 8")]
        [TestCase("Frieren: Beyond Journey's End", "Frieren: Beyond Journey’s End")]
        [TestCase("Re:ZERO -Starting Life in Another World-, Chapter 3: Truth of Zero", "Re:ZERO - Starting Life in Another World - Chapter 3: Truth of Zero")]
        [TestCase("My Hero Academia: Vigilantes", "MY HERO ACADEMIA: VIGILANTES")]
        [TestCase("Solo Leveling", "solo leveling")]
        public void should_match_punctuation_case_and_spacing_variants(string query, string candidate)
        {
            TitleMatcher.Matches(query, candidate).Should().BeTrue();
        }

        [TestCase("Black Clover", "Black Clover Gaiden: Quartet Knights")]
        [TestCase("Re:ZERO -Starting Life in Another World-, Chapter 3: Truth of Zero", "Re:ZERO -Starting Life in Another World-, Chapter 4: The Sanctuary and the Witch of Greed")]
        [TestCase("Re:ZERO -Starting Life in Another World-, Chapter 3: Truth of Zero", "Re:ZERO -Starting Life in Another World-")]
        [TestCase("Kaiju No. 8", "Kaiju No. 8: Relax")]
        [TestCase("Tokyo Ghoul", "Tokyo Ghoul:re")]
        public void should_reject_different_series_in_same_franchise(string query, string candidate)
        {
            TitleMatcher.Matches(query, candidate).Should().BeFalse();
        }

        // TitleFold (2026-09-24, OpenTome's fold()): an accent on a Latin letter and a numeric symbol
        // are spelling, not identity -- two titles differing ONLY in them are one series, as they
        // are in OpenTome (binding is by id first). The 2026-09-24 catalogue scan found only
        // market lines of one work newly sharing a key (Détective Conan / Detective Conan,
        // Rurôni / Rurōni Kenshin, Sōkoku no Garō, Ōkami-san). Kana voicing marks still count.
        [TestCase("\u014coku", "Ooku")]
        [TestCase("Fushigi Y\u00fbgi", "Fushigi Yugi")]
        [TestCase("Fushigi Y\u00fbgi", "Fushigi Y\u016bgi")]
        [TestCase("D\u00e9tective Conan", "Detective Conan")]
        [TestCase("Ranma \u00bd", "Ranma 1/2")]
        [TestCase("DNA\u00b2", "DNA2")]
        public void should_match_accent_and_numeric_symbol_variants(string query, string candidate)
        {
            TitleMatcher.Matches(query, candidate).Should().BeTrue();
        }

        [TestCase("\u30ac\u30eb\u30d0", "\u30ab\u30eb\u30d0")]
        [TestCase("Ranma \u00bd", "Ranma")]
        [TestCase("DNA\u00b2", "DNA")]
        [TestCase("\u0439", "\u0438")]
        public void should_still_tell_apart_what_the_fold_keeps(string query, string candidate)
        {
            TitleMatcher.Matches(query, candidate).Should().BeFalse();
        }

        [Test]
        public void search_score_tokens_are_folded_too()
        {
            TitleMatcher.SearchScore("ubel blatt manga", new[] { "\u00dcbel Blatt" }).Should().Be(0.8m);
        }

        [Test]
        public void should_match_when_any_candidate_matches()
        {
            TitleMatcher.Matches(
                "Mushoku Tensei: Jobless Reincarnation",
                "Mushoku Tensei - Isekai Ittara Honki Dasu",
                "Mushoku Tensei: Jobless Reincarnation").Should().BeTrue();
        }

        [Test]
        public void should_not_match_null_or_empty()
        {
            TitleMatcher.Matches(null, "Black Clover").Should().BeFalse();
            TitleMatcher.Matches("", "Black Clover").Should().BeFalse();
            TitleMatcher.Matches("Black Clover", (string)null).Should().BeFalse();
            TitleMatcher.Matches("Black Clover", (System.Collections.Generic.IEnumerable<string>)null).Should().BeFalse();
        }

        [Test]
        public void should_normalize_to_letters_and_digits_only()
        {
            TitleMatcher.Normalize("Re:ZERO -Starting Life in Another World-").Should().Be("rezerostartinglifeinanotherworld");
            TitleMatcher.Normalize("  ").Should().Be("");
            TitleMatcher.Normalize(null).Should().Be("");
        }

        // SearchScore cases use the real AniList candidate lists that surfaced the bug:
        // user-typed queries that are near-misses of the canonical title returned zero
        // results because exact matching rejected every fuzzy provider hit.
        [Test]
        public void search_score_should_accept_leading_article_variant()
        {
            TitleMatcher.SearchScore("apothecary diaries", new[] { "The Apothecary Diaries", "Kusuriya no Hitorigoto" })
                .Should().BeGreaterOrEqualTo(TitleMatcher.SearchFloor);
        }

        [Test]
        public void search_score_should_rank_main_series_above_spinoff()
        {
            var query = "That Time I Was reincarnated";
            var main = TitleMatcher.SearchScore(query, new[] { "That Time I Got Reincarnated as a Slime" });
            var spinoff = TitleMatcher.SearchScore(query, new[] { "That Time I Got Reincarnated as a Slime: Trinity in Tempest" });

            main.Should().BeGreaterOrEqualTo(TitleMatcher.SearchFloor);
            main.Should().BeGreaterThan(spinoff);
        }

        [Test]
        public void search_score_should_be_one_for_normalized_equality()
        {
            TitleMatcher.SearchScore("Kusuriya no Hitorigoto", new[] { "Kusuriya no  Hitorigoto!" }).Should().Be(1.0m);
        }

        [Test]
        public void search_score_should_reject_unrelated_series()
        {
            TitleMatcher.SearchScore("apothecary diaries", new[] { "Berserk", "Tensei Shitara Spreadsheet Datta Ken" })
                .Should().BeLessThan(TitleMatcher.SearchFloor);
        }

        [Test]
        public void search_score_should_take_best_across_candidate_titles()
        {
            TitleMatcher.SearchScore("apothecary diaries", new[] { "Kusuriya no Hitorigoto", "The Apothecary Diaries" })
                .Should().BeGreaterOrEqualTo(TitleMatcher.SearchFloor);
        }

        [Test]
        public void search_score_should_be_zero_for_null_or_empty()
        {
            TitleMatcher.SearchScore(null, new[] { "Berserk" }).Should().Be(0m);
            TitleMatcher.SearchScore("Berserk", null).Should().Be(0m);
            TitleMatcher.SearchScore("", new[] { "Berserk" }).Should().Be(0m);
            TitleMatcher.SearchScore("Berserk", new string[] { null }).Should().Be(0m);
        }
    }
}
