using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    // Preferred Edition (2026-09-24, D2): a non-English edition takes only releases with evidence of its
    // language (a tag in the title, or the indexer's language attribute); no marker -> rejected with the
    // reason shown. English editions (2026-09-26, plan A2): a release showing another language and no English
    // is rejected; untagged passes. A light novel's audiobook is English (D8).
    [TestFixture]
    public class EditionLanguageSpecificationFixture : CoreTest<EditionLanguageSpecification>
    {
        private static RemoteBook Release(string edition, string title, MediaType mediaType = MediaType.Archive, params Language[] languages)
        {
            return new RemoteBook
            {
                Author = new Author { Metadata = new AuthorMetadata { Name = "x", EditionLanguage = edition } },
                Release = new ReleaseInfo { Title = title, Languages = new List<Language>(languages) },
                MediaType = mediaType
            };
        }

        // Final fix wave I1 (ruling): a fallback series takes releases as an English series does.
        [Test]
        public void a_fallback_series_accepts_an_unmarked_release()
        {
            var release = Release("ja", "Stand Up Start v05");
            release.Author.Metadata.Value.EditionFallback = true;

            Subject.IsSatisfiedBy(release, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void the_same_binding_as_an_edition_rejects_an_unmarked_release()
        {
            Subject.IsSatisfiedBy(Release("ja", "Stand Up Start v05"), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void a_french_tag_passes_a_french_edition()
        {
            Subject.IsSatisfiedBy(Release("fr", "L'Attaque des Titans T05 [FR]"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void no_marker_is_rejected_with_the_reason()
        {
            var decision = Subject.IsSatisfiedBy(Release("fr", "L'Attaque des Titans T05"), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("No French language marker on this release (the series is the French edition)");
        }

        [Test]
        public void another_language_is_rejected_with_the_reason()
        {
            var decision = Subject.IsSatisfiedBy(Release("fr", "Attack on Titan v05 [ENG]"), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Release is English, the series is the French edition");
        }

        [Test]
        public void the_indexer_language_attribute_counts()
        {
            Subject.IsSatisfiedBy(Release("fr", "L'Attaque des Titans T05", MediaType.Archive, Language.French), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_light_novel_audiobook_is_english_and_passes()
        {
            Subject.IsSatisfiedBy(Release("fr", "Sword Art Online 2 Aincrad", MediaType.Audio), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_japanese_raw_passes_a_japanese_edition()
        {
            Subject.IsSatisfiedBy(Release("ja", "進撃の巨人 第5巻 RAW"), null).Accepted.Should().BeTrue();
        }

        // Final fix round I1 (2026-09-24): an untagged native-titled release carries kana (の), which only
        // Japanese writes -- it passes a Japanese edition. Kanji alone are not Japanese evidence (Chinese
        // writes them too), and a Chinese edition is unaffected.
        [Test]
        public void an_untagged_native_title_with_kana_passes_a_japanese_edition()
        {
            Subject.IsSatisfiedBy(Release("ja", "進撃の巨人 第05巻"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_kanji_only_title_is_not_japanese_evidence()
        {
            var decision = Subject.IsSatisfiedBy(Release("ja", "進撃巨人 第05巻"), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("No Japanese language marker on this release (the series is the Japanese edition)");
        }

        [Test]
        public void a_chinese_edition_is_unaffected_by_kana()
        {
            Subject.IsSatisfiedBy(Release("zh", "海贼王 第05卷 [CHS]"), null).Accepted.Should().BeTrue();

            // KR/CN piece 2 (2026-10-02, M5): a Chinese volume marker (a digit before 卷) is zh evidence on its own.
            Subject.IsSatisfiedBy(Release("zh", "海贼王 第05卷"), null).Accepted.Should().BeTrue();

            // Han characters alone, with no marker, are still no evidence.
            var untagged = Subject.IsSatisfiedBy(Release("zh", "海贼王 05"), null);
            untagged.Accepted.Should().BeFalse();
            untagged.Reason.Should().Be("No Chinese language marker on this release (the series is the Chinese edition)");

            Subject.IsSatisfiedBy(Release("zh", "進撃の巨人 第05巻"), null).Accepted.Should().BeFalse();
        }

        // 2026-09-26 (plan A2, the maintainer: "go"): an English series rejects a release that shows another language
        // and no English. Untagged is English by convention; a release that also shows English passes.
        [TestCase(null, "Attack on Titan v05")]
        [TestCase("en", "Attack on Titan v05 (Digital) (1r0n)")]
        [TestCase(null, "Attack on Titan v05 [JP-ENG]")]
        [TestCase("en", "Attack on Titan v05 [FR] [ENG]")]
        public void an_english_edition_accepts_untagged_and_english_releases(string edition, string title)
        {
            Subject.IsSatisfiedBy(Release(edition, title), null).Accepted.Should().BeTrue();
        }

        [TestCase(null, "Attack on Titan v05 [FR]", "French")]
        [TestCase("en", "Shingeki no Kyojin v05 RAW", "Japanese")]
        [TestCase("en", "進撃の巨人 第05巻", "Japanese")]
        [TestCase(null, "Attack on Titan Band 05 GERMAN", "German")]
        public void an_english_edition_rejects_another_language_with_the_reason(string edition, string title, string language)
        {
            var decision = Subject.IsSatisfiedBy(Release(edition, title), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be($"Release is {language}, the series is the English edition");
        }

        [Test]
        public void an_english_edition_reads_the_indexer_language_attribute()
        {
            Subject.IsSatisfiedBy(Release(null, "Attack on Titan v05", MediaType.Archive, Language.Japanese), null).Accepted.Should().BeFalse();
            Subject.IsSatisfiedBy(Release(null, "Attack on Titan v05", MediaType.Archive, Language.English), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void an_english_audiobook_is_checked_too()
        {
            Subject.IsSatisfiedBy(Release(null, "Sword Art Online 2 Aincrad by Reki Kawahara [ENG / M4B]", MediaType.Audio), null).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(Release(null, "Sword Art Online 2 Aincrad by Reki Kawahara [GER / MP3]", MediaType.Audio), null).Accepted.Should().BeFalse();
        }

        // A series whose own name holds a language word is not evidence against itself; a real tag still is.
        [Test]
        public void an_english_series_name_is_not_language_evidence()
        {
            var raw = new RemoteBook
            {
                Author = new Author { Metadata = new AuthorMetadata { Name = "Raw Hero", Aliases = new List<string> { "RAW HERO" } } },
                Release = new ReleaseInfo { Title = "Raw Hero v01 (Digital)", Languages = new List<Language>() },
                MediaType = MediaType.Archive
            };

            Subject.IsSatisfiedBy(raw, null).Accepted.Should().BeTrue();

            raw.Release.Title = "Raw.Hero.v01.cbz";
            Subject.IsSatisfiedBy(raw, null).Accepted.Should().BeTrue();

            raw.Release.Title = "Raw_Hero_v01";
            Subject.IsSatisfiedBy(raw, null).Accepted.Should().BeTrue();

            raw.Release.Title = "Raw Hero v01 RAW";
            Subject.IsSatisfiedBy(raw, null).Accepted.Should().BeFalse();
        }

        // A name is cut only as whole words and from 4 characters: a series called "K" cannot eat "[KOR]".
        [Test]
        public void a_short_series_name_never_eats_a_language_tag()
        {
            var k = new RemoteBook
            {
                Author = new Author { Metadata = new AuthorMetadata { Name = "K" } },
                Release = new ReleaseInfo { Title = "K v01 [KOR]", Languages = new List<Language>() },
                MediaType = MediaType.Archive
            };

            Subject.IsSatisfiedBy(k, null).Accepted.Should().BeFalse();
        }

        // Review Focus 4: a French series named in romaji ("Shingeki no Kyojin") resolves through its
        // stored aliases and still needs the language tag to pass; without it, it is rejected with a reason.
        [Test]
        public void a_romaji_titled_release_with_a_french_tag_passes_a_french_edition()
        {
            Subject.IsSatisfiedBy(Release("fr", "Shingeki no Kyojin T05 [FR]"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void the_same_romaji_title_without_a_tag_is_rejected_with_the_reason()
        {
            var decision = Subject.IsSatisfiedBy(Release("fr", "Shingeki no Kyojin T05"), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("No French language marker on this release (the series is the French edition)");
        }

        // M10 fix round 1: ko/zh/es/it/pt editions were rejecting every release because the parser had
        // no tags for them at all. "zh-TW" is a region-qualified chain entry -- want = "zh" (the code
        // before the "-"), so a Traditional-Chinese [CHT] tag still counts as evidence.
        [Test]
        public void a_zh_tw_edition_accepts_a_traditional_chinese_tag()
        {
            Subject.IsSatisfiedBy(Release("zh-TW", "One Piece Vol. 05 [CHT]"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_korean_tag_passes_a_korean_edition()
        {
            Subject.IsSatisfiedBy(Release("ko", "Solo Leveling Vol. 05 [KOR]"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void an_untagged_release_is_rejected_for_a_korean_edition()
        {
            var decision = Subject.IsSatisfiedBy(Release("ko", "Solo Leveling Vol. 05"), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("No Korean language marker on this release (the series is the Korean edition)");
        }

        // Bare "IT" is not a tag (it's an English word/abbreviation), so a standalone " IT " token in an
        // all-caps title is not evidence -- the release is still rejected as unmarked.
        [Test]
        public void an_italian_edition_with_a_standalone_it_word_and_no_tag_is_rejected()
        {
            var decision = Subject.IsSatisfiedBy(Release("it", "ATTACK ON TITAN VOL 05 IT SCAN"), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("No Italian language marker on this release (the series is the Italian edition)");
        }
    }
}
