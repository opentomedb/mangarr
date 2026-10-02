using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // KR/CN piece 2 (2026-10-02, M4): a work's origin as AniList spells it and as OpenTome's medium implies it.
    [TestFixture]
    public class OriginFixture : CoreTest
    {
        [TestCase("manhwa", "KR")]
        [TestCase("Manhwa", "KR")]
        [TestCase("manhua", "CN")]
        [TestCase("manga", null)]
        [TestCase("light_novel", null)]
        [TestCase("novel", null)]
        [TestCase(null, null)]
        [TestCase("", null)]
        public void medium_implies_an_origin_only_for_manhwa_and_manhua(string medium, string expected)
        {
            Origin.OfMedium(medium).Should().Be(expected);
        }

        [TestCase("KR", "KR", true)]
        [TestCase("KR", "kr", true)]
        [TestCase("KR", "JP", false)]
        [TestCase("JP", "KR", false)]
        [TestCase("CN", "TW", true)]
        [TestCase("TW", "CN", true)]
        [TestCase("CN", "JP", false)]
        [TestCase(null, "JP", true)]
        [TestCase("KR", null, true)]
        [TestCase("", "JP", true)]
        public void origins_agree_when_either_is_unknown_or_both_are_the_same_market(string expected, string actual, bool agrees)
        {
            Origin.Agrees(expected, actual).Should().Be(agrees);
        }

        [TestCase("KR", true)]
        [TestCase("CN", true)]
        [TestCase("TW", true)]
        [TestCase("tw", true)]
        [TestCase("JP", false)]
        [TestCase(null, false)]
        public void korean_or_chinese(string origin, bool expected)
        {
            Origin.IsKoreanOrChinese(origin).Should().Be(expected);
        }
    }
}
