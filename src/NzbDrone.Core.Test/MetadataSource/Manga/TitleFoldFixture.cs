using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    // TitleFold mirrors OpenTome's export/resolve_anilist.py fold() (2026-09-24, main e7435fe). The
    // first tests are OpenTome's own fold cases from export/test_resolve_anilist.py, one for one:
    // TitleMatcher.Normalize is key(), TitleNormalizer.ForSearch is for_search(), TitleFold.Fold is
    // fold().
    [TestFixture]
    public class TitleFoldFixture : CoreTest
    {
        [Test]
        public void key_ignores_punctuation_case_and_spacing()
        {
            TitleMatcher.Normalize("Re:ZERO -Starting Life-").Should().Be("rezerostartinglife");
        }

        [Test]
        public void for_search_folds_typographic_punctuation()
        {
            TitleNormalizer.ForSearch("Let’s Do It Already!").Should().Be("Let's Do It Already!");
            TitleNormalizer.ForSearch("A – B  — C").Should().Be("A - B - C");
        }

        [TestCase("Fushigi Yûgi", "Fushigi Yugi")]
        [TestCase("Übel Blatt", "Ubel Blatt")]
        [TestCase("Saintia Shō", "Saintia Sho")]
        [TestCase("Ōoku", "Ooku")]
        [TestCase("BakéGyamon", "BakeGyamon")]
        [TestCase("Wāqwāq", "Waqwaq")]
        [TestCase("Việt", "Viet")]
        [TestCase("ûx", "ux")]
        public void for_search_strips_the_latin_accent(string raw, string expected)
        {
            TitleNormalizer.ForSearch(raw).Should().Be(expected);
        }

        // Kana voicing marks (ゲ, パ), Hangul, Cyrillic й, a mark on a digit or a space, and the
        // compatibility ideograph U+F900 (which an unconditional NFC would turn into U+8C48).
        [TestCase("ゲーム")]
        [TestCase("パン")]
        [TestCase("한국어")]
        [TestCase("й")]
        [TestCase("1̂")]
        [TestCase(" ̂x")]
        [TestCase("豈")]
        public void fold_leaves_a_non_latin_base_marks_alone(string raw)
        {
            TitleFold.Fold(raw).Should().Be(raw);
        }

        [Test]
        public void an_accented_title_and_its_ascii_spelling_are_one_key()
        {
            TitleMatcher.Normalize("Fushigi Yûgi").Should().Be(TitleMatcher.Normalize("Fushigi Yugi"));
        }

        [Test]
        public void kana_voicing_marks_still_count()
        {
            TitleMatcher.Normalize("ガ").Should().NotBe(TitleMatcher.Normalize("カ"));
        }

        [TestCase("Ranma ½", "Ranma 1/2")]
        [TestCase("Ranma ¹⁄₂", "Ranma 1/2")]
        [TestCase("Ⅱ", "II")]
        [TestCase("x²", "x2")]
        [TestCase("①", "1")]
        public void for_search_folds_the_numeric_symbol(string raw, string expected)
        {
            TitleNormalizer.ForSearch(raw).Should().Be(expected);
        }

        // The pre-existing key difference the ranker review found: Python's isalnum kept the digits
        // of No / Nl characters, char.IsLetterOrDigit dropped them ("Ranma ½" keyed ranma, not ranma12).
        [Test]
        public void ranma_half_ranma_1_2_and_the_superscript_spelling_are_one_key()
        {
            new[] { "Ranma ½", "Ranma 1/2", "Ranma ¹⁄₂", "ranma12" }
                .Select(TitleMatcher.Normalize)
                .Distinct()
                .Should().Equal("ranma12");
        }

        [TestCase("Ｒａｎｍａ")]
        [TestCase("ﬁ")]
        public void fold_leaves_full_width_and_ligatures_alone(string raw)
        {
            TitleFold.Fold(raw).Should().Be(raw);
        }

        // ---- Mangarr-side cases beyond OpenTome's table

        // A No / Nl character with no NFKC form stays in the key, as Python's isalnum keeps it:
        // the ideographic zero (Nl) and a dingbat circled digit (No).
        [TestCase("〇〇", "〇〇")]
        [TestCase("玉狛第2➁", "玉狛第2➁")]
        [TestCase("玉狛第2②", "玉狛第22")]
        public void a_numeric_character_without_a_compatibility_form_stays_in_the_key(string raw, string expected)
        {
            TitleMatcher.Normalize(raw).Should().Be(expected);
        }

        // Deliberate divergence (see TitleFold's table): Python calls full-width letters LATIN, the
        // table does not, so a mark on one is kept here.
        [Test]
        public void a_mark_on_a_full_width_letter_is_kept()
        {
            TitleFold.Fold("Ａ́").Should().Be("Ａ́");
        }

        [TestCase(0x0041, true)]
        [TestCase(0x007A, true)]
        [TestCase(0x00E9, true)]
        [TestCase(0x017F, true)]
        [TestCase(0x0250, true)]
        [TestCase(0x02AF, true)]
        [TestCase(0x1E9E, true)]
        [TestCase(0x2C60, true)]
        [TestCase(0xA7FF, true)]
        [TestCase(0xAB68, true)]
        [TestCase(0x00AA, false)] // ª: its name has no LATIN
        [TestCase(0x00B5, false)] // µ
        [TestCase(0x00BA, false)] // º
        [TestCase(0x00D7, false)] // ×
        [TestCase(0x00F7, false)] // ÷
        [TestCase(0x02B0, false)] // ʰ, a modifier letter
        [TestCase(0x2C7D, false)] // modifier letter capital V
        [TestCase(0xA770, false)] // modifier letter us
        [TestCase(0xAB5C, false)] // modifier letter small heng
        [TestCase(0x0439, false)] // й
        [TestCase(0x30AB, false)] // カ
        [TestCase(0x1D00, false)] // Phonetic Extensions: a documented divergence
        [TestCase(0xFF21, false)] // full-width A: a documented divergence
        public void latin_table(int codePoint, bool latin)
        {
            TitleFold.IsLatin(codePoint).Should().Be(latin);
        }

        [Test]
        public void an_ill_formed_title_is_compared_unfolded_instead_of_throwing()
        {
            Action act = () => TitleMatcher.Normalize("Ranma \ud800 ½");

            act.Should().NotThrow();
            TitleNormalizer.ForSearch("Yûgi \ud800").Should().Be("Yûgi \ud800");
        }

        [Test]
        public void an_ascii_title_comes_back_as_it_was()
        {
            const string title = "Kaiju No. 8";

            TitleFold.Fold(title).Should().BeSameAs(title);
        }

        // Every non-ASCII catalogue name / alias whose key or search term the fold changes (2,767),
        // plus 400 sampled unchanged ones (300 that NFD-decompose -- kana voicing, Hangul -- and 100
        // others), with OpenTome's key() and for_search() on each, generated from the OpenTome
        // artifact of 2026-09-24 (build/manga-metadata.sqlite.new) with resolve_anilist.py at e7435fe.
        // Offline, a Python model of this C# matched key() on all 90,575 catalogue strings.
        [Test]
        public void matches_opentome_on_the_catalogue_strings()
        {
            var rows = JsonConvert.DeserializeObject<List<string[]>>(ReadAllText("Files/TitleFold/opentome-fold-parity.json"));
            var misses = rows
                .Where(r => TitleMatcher.Normalize(r[0]) != r[1] || TitleNormalizer.ForSearch(r[0]) != r[2])
                .Select(r => $"{r[0]} -> {TitleMatcher.Normalize(r[0])} / {TitleNormalizer.ForSearch(r[0])} (OpenTome {r[1]} / {r[2]})")
                .ToList();

            rows.Count.Should().BeGreaterThan(3000);
            misses.Should().BeEmpty();
        }
    }
}
