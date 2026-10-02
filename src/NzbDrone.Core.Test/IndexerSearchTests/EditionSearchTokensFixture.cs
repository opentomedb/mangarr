using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    // KR/CN piece 2 (2026-10-02, M5): the query tokens per market. fr/de/ja are pinned as they were.
    [TestFixture]
    public class EditionSearchTokensFixture : CoreTest
    {
        [TestCase("fr", true)]
        [TestCase("de", true)]
        [TestCase("ja", true)]
        [TestCase("ko", true)]
        [TestCase("zh", true)]
        [TestCase("zh-TW", true)]
        [TestCase("en", false)]
        [TestCase("it", false)]
        [TestCase(null, false)]
        public void has(string language, bool expected)
        {
            EditionSearchTokens.Has(language).Should().Be(expected);
        }

        [TestCase("fr", "05", true, "T05")]
        [TestCase("fr", "5", false, "Tome+5")]
        [TestCase("de", "05", true, "Band+05")]
        [TestCase("de", "5", false, "Bd+5")]
        [TestCase("ja", "05", true, "第05巻")]
        [TestCase("ko", "05", true, "05권")]
        [TestCase("ko", "5", false, "제5권")]
        [TestCase("zh", "05", true, "第05卷")]
        [TestCase("zh", "5", false, "5卷")]
        [TestCase("zh-TW", "05", true, "第05卷")]
        [TestCase("en", "05", true, "v05")]
        public void token(string language, string number, bool padded, string expected)
        {
            EditionSearchTokens.Token(language, number, padded).Should().Be(expected);
        }

        [Test]
        public void korean_and_chinese_have_no_function_words()
        {
            EditionSearchTokens.FunctionWords("ko").Should().BeEmpty();
            EditionSearchTokens.FunctionWords("zh").Should().BeEmpty();
            EditionSearchTokens.FunctionWords("fr").Should().Contain("les");
        }
    }
}
