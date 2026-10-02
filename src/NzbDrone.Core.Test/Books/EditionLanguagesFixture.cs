using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class EditionLanguagesFixture : CoreTest
    {
        [TestCase(null, true)]
        [TestCase("", true)]
        [TestCase("en", true)]
        [TestCase("EN", true)]
        [TestCase("fr", false)]
        [TestCase("ja", false)]
        public void is_english(string code, bool expected)
        {
            EditionLanguages.IsEnglish(code).Should().Be(expected);
        }

        [TestCase(null, new[] { "en" })]
        [TestCase("", new[] { "en" })]
        [TestCase("fr,en,ja", new[] { "fr", "en", "ja" })]
        [TestCase(" fr , en ", new[] { "fr", "en" })]
        [TestCase("fr,fr,en", new[] { "fr", "en" })]
        [TestCase("pt-BR,en", new[] { "pt-BR", "en" })]
        [TestCase("french,en", new[] { "en" })]
        public void parse_chain(string csv, string[] expected)
        {
            EditionLanguages.ParseChain(csv).Should().Equal(expected);
        }

        [TestCase("en", true)]
        [TestCase("fr,en,ja", true)]
        [TestCase("pt-BR", true)]
        [TestCase("", false)]
        [TestCase("fr,,en", false)]
        [TestCase("fr,fr", false)]
        [TestCase("French", false)]
        public void is_valid_chain(string csv, bool expected)
        {
            EditionLanguages.IsValidChain(csv).Should().Be(expected);
        }

        [TestCase(null, "eng")]
        [TestCase("en", "eng")]
        [TestCase("fr", "fra")]
        [TestCase("de", "deu")]
        [TestCase("ja", "jpn")]
        public void to_iso3(string code, string expected)
        {
            EditionLanguages.ToIso3(code).Should().Be(expected);
        }

        // M6b pre-review fix (2026-09-24): the reverse of ToIso3 for a stored Edition.Language.
        [TestCase(null, "en")]
        [TestCase("", "en")]
        [TestCase("eng", "en")]
        [TestCase("jpn", "ja")]
        [TestCase("kor", "ko")]
        [TestCase("zho", "zh")]
        [TestCase("fra", "fr")]
        [TestCase("deu", "de")]
        [TestCase("xyz", "en")]
        [TestCase("English", "en")]
        public void from_iso3(string iso3, string expected)
        {
            EditionLanguages.FromIso3(iso3).Should().Be(expected);
        }

        [TestCase("ja")]
        [TestCase("ko")]
        [TestCase("zh-TW")]
        [TestCase("fr")]
        public void from_iso3_round_trips_to_iso3_by_base_language(string code)
        {
            EditionLanguages.FromIso3(EditionLanguages.ToIso3(code)).Should().Be(code.Split('-')[0]);
        }

        [TestCase(null, "5", "Vol. 5")]
        [TestCase("en", "12", "Vol. 12")]
        [TestCase("fr", "5", "Tome 5")]
        [TestCase("de", "3.5", "Band 3.5")]
        [TestCase("ja", "5", "第5巻")]
        [TestCase("it", "5", "Vol. 5")]
        [TestCase("ko", "5", "5권")]
        [TestCase("zh", "5", "第5卷")]
        [TestCase("zh-TW", "12", "第12卷")]
        public void volume_label(string code, string token, string expected)
        {
            EditionLanguages.VolumeLabel(code, token).Should().Be(expected);
        }

        // KR/CN piece 2 (2026-10-02, M5): the editions whose own script is not Latin.
        [TestCase("ja", true)]
        [TestCase("ko", true)]
        [TestCase("zh", true)]
        [TestCase("zh-TW", true)]
        [TestCase("zh-HK", true)]
        [TestCase("fr", false)]
        [TestCase("en", false)]
        [TestCase(null, false)]
        [TestCase("", false)]
        public void is_native_script(string code, bool expected)
        {
            EditionLanguages.IsNativeScript(code).Should().Be(expected);
        }

        [TestCase("zh-TW", "zh")]
        [TestCase("ko", "ko")]
        [TestCase(" fr ", "fr")]
        [TestCase(null, "en")]
        [TestCase("", "en")]
        public void base_code(string code, string expected)
        {
            EditionLanguages.BaseCode(code).Should().Be(expected);
        }

        [Test]
        public void of_reads_null_metadata_as_english()
        {
            EditionLanguages.Of(null).Should().Be("en");
            EditionLanguages.Of(new AuthorMetadata()).Should().Be("en");
            EditionLanguages.Of(new AuthorMetadata { EditionLanguage = "fr" }).Should().Be("fr");
        }

        [Test]
        public void name_uses_the_iso_table()
        {
            EditionLanguages.Name("fr").Should().Be("French");
            EditionLanguages.Name(null).Should().Be("English");
            EditionLanguages.Name("xx").Should().Be("xx");
        }

        // M15 polish: OpenTome's markets carry a "zh-TW" line count; it needs a display name of its own,
        // and the base "zh" entry must keep reading as plain Chinese.
        [Test]
        public void name_has_a_zh_tw_entry()
        {
            EditionLanguages.Name("zh-TW").Should().Be("Chinese (Taiwan)");
            EditionLanguages.Name("zh").Should().Be("Chinese");
        }

        // i18n leftovers (2026-09-28): OpenTome/Google also use "zh-HK"; it was falling back to the raw
        // code because IsoLanguages had no "hk" entry (neither "cn" nor "tw" is country-less, so the
        // "zh-HK" lookup's country-code filter emptied the candidate list instead of falling back).
        [Test]
        public void name_has_a_zh_hk_entry()
        {
            EditionLanguages.Name("zh-HK").Should().Be("Chinese (Hong Kong)");
            EditionLanguages.Name("zh-TW").Should().Be("Chinese (Taiwan)");
            EditionLanguages.Name("zh").Should().Be("Chinese");
        }

        // M15 polish: pins the ordering LocalizationService.GetSetLanguageFileName() depends on --
        // IsoLanguages.Get(Language.Chinese) must keep returning the "cn" entry (it builds "zh_CN"),
        // not the new "tw" one added for EditionLanguages.Name("zh-TW") (i18n leftovers 2026-09-28:
        // nor the "hk" one added after it for EditionLanguages.Name("zh-HK")).
        [Test]
        public void iso_languages_get_chinese_still_resolves_to_cn()
        {
            IsoLanguages.Get(Language.Chinese).CountryCode.Should().Be("cn");
            IsoLanguages.Find("zho").EnglishName.Should().Be("Chinese");
        }
    }
}
