using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Parser
{
    // Preferred Edition (2026-09-24, D2): the language tags scene/indexer names carry. Words are
    // case-insensitive; short codes only in UPPER case and standing alone -- a lower-case "de" is a
    // French word ("Journal de la guerre"), not a German tag. Two-letter codes as OpenTome/Mangarr
    // store edition languages.
    public static class ReleaseLanguageParser
    {
        private static readonly (string Language, Regex Pattern)[] Tags =
        {
            ("fr", new Regex(@"\b(?:french|fran[cç]ais|truefrench)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("fr", new Regex(@"(?<![A-Za-z])(?:VF|FR|FRA)(?![A-Za-z])", RegexOptions.Compiled)),
            ("de", new Regex(@"\b(?:german|deutsch)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("de", new Regex(@"(?<![A-Za-z])(?:DE|GER)(?![A-Za-z])", RegexOptions.Compiled)),
            ("ja", new Regex(@"\b(?:japanese|raws?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("ja", new Regex(@"(?<![A-Za-z])(?:JP|JPN|JA)(?![A-Za-z])", RegexOptions.Compiled)),

            ("en", new Regex(@"\benglish\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("en", new Regex(@"(?<![A-Za-z])(?:EN|ENG)(?![A-Za-z])", RegexOptions.Compiled)),

            // Preferred Edition (2026-09-25, M10 fix round 1): ko/zh/es/it/pt editions had no tags at
            // all, so they were rejected whenever the indexer sent no language attribute. "CN" is left
            // out of the zh code list -- unlike JP/DE/FR, it is common as an unrelated group/region tag
            // in scene names, and zh already has four other codes plus two words. Bare "IT"/"PT" are
            // left out for the same reason "EN"-as-a-word risk is avoided elsewhere: "IT" and "PT" are
            // both common English words/abbreviations standing alone in an all-caps title.
            ("ko", new Regex(@"\bkorean\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("ko", new Regex(@"(?<![A-Za-z])(?:KOR|KR)(?![A-Za-z])", RegexOptions.Compiled)),
            ("zh", new Regex(@"\b(?:chinese|mandarin)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("zh", new Regex(@"(?<![A-Za-z])(?:CHS|CHT|CHI|ZH)(?![A-Za-z])", RegexOptions.Compiled)),
            ("es", new Regex(@"\b(?:spanish|espa[nñ]ol|castellano)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("es", new Regex(@"(?<![A-Za-z])(?:ESP|SPA)(?![A-Za-z])", RegexOptions.Compiled)),
            ("it", new Regex(@"\b(?:italian|italiano)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("it", new Regex(@"(?<![A-Za-z])ITA(?![A-Za-z])", RegexOptions.Compiled)),
            ("pt", new Regex(@"\bportugu(?:ese|[eê]s)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("pt", new Regex(@"(?<![A-Za-z])(?:POR|PT-BR|PTBR)(?![A-Za-z])", RegexOptions.Compiled))
        };

        // Preferred Edition (2026-09-24, final fix round I1): a native-titled release ("進撃の巨人 第05巻")
        // usually carries no tag at all. Kana are written only in Japanese, so any hiragana or katakana
        // is ja evidence. Kanji alone is not: Chinese titles are written in them too. The katakana-block
        // middle dot (・, U+30FB) is left out -- Chinese titles copied from Japanese keep it.
        private static readonly Regex Kana = new Regex(@"[\p{IsHiragana}\p{IsKatakana}-[\u30FB]]", RegexOptions.Compiled);

        public static List<string> Parse(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return new List<string>();
            }

            var found = Tags.Where(t => t.Pattern.IsMatch(title)).Select(t => t.Language).Distinct().ToList();

            // Kana count only when no other language is tagged: "進撃の巨人 第05巻 [ENG]" is an English
            // release under its native title, not Japanese evidence.
            if (found.Count == 0 && Kana.IsMatch(title))
            {
                found.Add("ja");
            }

            return found;
        }
    }
}
