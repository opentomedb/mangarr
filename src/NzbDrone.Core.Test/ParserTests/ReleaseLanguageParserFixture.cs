using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    // Preferred Edition (2026-09-24, D2): the tags ReleaseLanguageParser looks for -- language words,
    // and short codes standing alone in upper case only, so a lower-case French word ("de", "en") is
    // never mistaken for a tag.
    [TestFixture]
    public class ReleaseLanguageParserFixture : CoreTest
    {
        [TestCase("L'Attaque des Titans T05 [FR]", new[] { "fr" })]
        [TestCase("L.Attaque.des.Titans.T05.FRENCH.CBZ-GRP", new[] { "fr" })]
        [TestCase("L'Attaque des Titans T05 VF", new[] { "fr" })]
        [TestCase("Angriff auf Titan Band 05 GERMAN", new[] { "de" })]
        [TestCase("Angriff auf Titan Band 05 (Deutsch)", new[] { "de" })]
        [TestCase("進撃の巨人 第5巻 RAW", new[] { "ja" })]
        [TestCase("Shingeki no Kyojin v05 [JP]", new[] { "ja" })]
        [TestCase("Attack on Titan v05 [ENG]", new[] { "en" })]
        [TestCase("L'Attaque des Titans T05", new string[0])]
        [TestCase("Journal de la guerre des Titans T05", new string[0])]
        [TestCase("Attack on Titan v05 (Digital) (danke-Empire)", new string[0])]

        // Review Focus 4: a French edition's release named in romaji and tagged [FR] still carries evidence.
        [TestCase("Shingeki no Kyojin T05 [FR]", new[] { "fr" })]

        // M10 fix round 1: ko/zh/es/it/pt tags, in the same word + short-code style as fr/de/ja.
        [TestCase("Solo Leveling Vol. 05 KOREAN", new[] { "ko" })]
        [TestCase("Solo Leveling Vol. 05 [KOR]", new[] { "ko" })]
        [TestCase("Solo Leveling Vol. 05 KR", new[] { "ko" })]
        [TestCase("One Piece Vol. 05 CHINESE", new[] { "zh" })]
        [TestCase("One Piece Vol. 05 MANDARIN", new[] { "zh" })]
        [TestCase("One Piece Vol. 05 [CHT]", new[] { "zh" })]
        [TestCase("One Piece Vol. 05 CHS", new[] { "zh" })]
        [TestCase("One Piece Vol. 05 ZH", new[] { "zh" })]
        [TestCase("Naruto Vol. 05 SPANISH", new[] { "es" })]
        [TestCase("Naruto Vol. 05 (Español)", new[] { "es" })]
        [TestCase("Naruto Vol. 05 (Espanol)", new[] { "es" })]
        [TestCase("Naruto Vol. 05 (Castellano)", new[] { "es" })]
        [TestCase("Naruto Vol. 05 ESP", new[] { "es" })]
        [TestCase("Naruto Vol. 05 SPA", new[] { "es" })]
        [TestCase("Bleach Vol. 05 ITALIAN", new[] { "it" })]
        [TestCase("Bleach Vol. 05 (Italiano)", new[] { "it" })]
        [TestCase("Bleach Vol. 05 ITA", new[] { "it" })]
        [TestCase("Bleach Vol. 05 PORTUGUESE", new[] { "pt" })]
        [TestCase("Bleach Vol. 05 (Português)", new[] { "pt" })]
        [TestCase("Bleach Vol. 05 (Portugues)", new[] { "pt" })]
        [TestCase("Bleach Vol. 05 POR", new[] { "pt" })]
        [TestCase("Bleach Vol. 05 PT-BR", new[] { "pt" })]
        [TestCase("Bleach Vol. 05 PTBR", new[] { "pt" })]

        // Bare "IT"/"PT" and "CN" are deliberately not tags: common English words/abbreviations and
        // an overloaded group/region code respectively, standing alone in an all-caps title.
        [TestCase("ATTACK ON TITAN VOL 05 IT SCAN", new string[0])]
        [TestCase("ATTACK ON TITAN VOL 05 PT SCAN", new string[0])]
        [TestCase("ATTACK ON TITAN VOL 05 CN SCAN", new string[0])]

        // Final fix round I1 (2026-09-24): kana are ja evidence. KR/CN piece 2 (2026-10-02, M5): Hangul is ko evidence, a Chinese volume marker (卷/册/冊 after a digit) zh evidence; kanji alone still are not.
        [TestCase("進撃の巨人 第05巻", new[] { "ja" })]
        [TestCase("ソードアート・オンライン 1", new[] { "ja" })]
        [TestCase("進撃巨人 第05巻", new string[0])]
        [TestCase("海贼王 第05卷", new[] { "zh" })]
        [TestCase("刀剑神域・进击篇 第05卷", new[] { "zh" })]
        [TestCase("海贼王 第05卷 [CHS]", new[] { "zh" })]
        // Final review follow-up (2026-09-25): an explicit other-language tag wins over kana.
        [TestCase("進撃の巨人 第05巻 [ENG]", new[] { "en" })]
        [TestCase("進撃の巨人 第05巻 [JP][ENG]", new[] { "ja", "en" })]

        // KR/CN piece 2 (2026-10-02, M5): script evidence for Korean and Chinese; a tag still wins (Review Focus 1).
        [TestCase("나 혼자만 레벨업 5권", new[] { "ko" })]
        [TestCase("나 혼자만 레벨업 제05권", new[] { "ko" })]
        [TestCase("나 혼자만 레벨업 5권 [ENG]", new[] { "en" })]
        [TestCase("進撃の巨人 5巻 [KOR]", new[] { "ko" })]
        [TestCase("進撃の巨人 5권", new[] { "ja" })]
        [TestCase("斗破苍穹 第05册", new[] { "zh" })]
        [TestCase("霹靂神州 第05集", new string[0])]
        public void finds_language_tags(string title, string[] expected)
        {
            ReleaseLanguageParser.Parse(title).Should().BeEquivalentTo(expected);
        }
    }
}
