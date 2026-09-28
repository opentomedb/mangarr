using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    [TestFixture]
    public class TitleNormalizerFixture : CoreTest
    {
        // D2: the stored (curated) name is folded to ASCII punctuation before it is sent to a live
        // source. AniList returned ZERO candidates for the typographic apostrophe (audit §3).
        [TestCase("Let’s Do It Already!", "Let's Do It Already!")]
        [TestCase("Frieren: Beyond Journey’s End", "Frieren: Beyond Journey's End")]
        [TestCase("‘Oshi no Ko’", "'Oshi no Ko'")]
        [TestCase("“Oshi no Ko”", "\"Oshi no Ko\"")]
        [TestCase("Re:ZERO –Starting Life in Another World–", "Re:ZERO -Starting Life in Another World-")]
        [TestCase("Chainsaw Man — Part 2", "Chainsaw Man - Part 2")]
        [TestCase("Kaiju\u00A0No.\u00A08", "Kaiju No. 8")]
        [TestCase("  Solo   Leveling  ", "Solo Leveling")]
        [TestCase("Fairy Tail", "Fairy Tail")]
        [TestCase("[Oshi no Ko]", "[Oshi no Ko]")]
        [TestCase("Fushigi Y\u00fbgi", "Fushigi Yugi")]                  // TitleFold (2026-09-24)
        [TestCase("Ranma \u00bd", "Ranma 1/2")]
        [TestCase("\u3010\u63a8\u3057\u306e\u5b50\u3011", "\u3010\u63a8\u3057\u306e\u5b50\u3011")]
        public void should_fold_typographic_punctuation_and_collapse_spaces(string stored, string expected)
        {
            TitleNormalizer.ForSearch(stored).Should().Be(expected);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void should_return_empty_for_blank(string stored)
        {
            TitleNormalizer.ForSearch(stored).Should().Be(string.Empty);
        }

        // The comparison side already ignores punctuation, so the folded query still equals a
        // candidate AniList titles with the curly form.
        [Test]
        public void folded_query_still_matches_the_curly_candidate()
        {
            TitleMatcher.Matches(TitleNormalizer.ForSearch("Frieren: Beyond Journey’s End"), "Frieren: Beyond Journey’s End").Should().BeTrue();
        }
    }
}
