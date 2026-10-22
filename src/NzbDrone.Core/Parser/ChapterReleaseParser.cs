using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Parser
{
    // Volumes built from chapter packs (2026-10-21): reads a chapter release ("Series c001-073
    // (Digital)", "Series Ch. 74", the market's own "Chapitre 12" / "第12話" ...) into its chapter
    // range. Any volume token means it is not one (a volume, a pack, or a "c001-042 as v01-v05"
    // compilation) -- volume parsing stays exactly as it is. The series' own name and aliases are
    // removed first, so a name's own "Chapter 4" (Re:ZERO's arcs) never counts as a chapter.
    public static class ChapterReleaseParser
    {
        // "c001", "c001-073", "Ch. 12", "Chapter 12", "Chapters 1-73", "c073.5"; an end may repeat the token ("c001-c073").
        // A bare "c" right after "(" is a Comiket tag ("(C102)"), never a chapter.
        private static readonly Regex EnglishChapterRegex = new Regex(
            @"(?<![\p{L}\p{N}])(?:chapters|chapter|chap|chs|ch|(?<!\()c)\.?\s*(?<s>\d{1,4}(?:\.\d{1,2})?)(?:\s*[\p{Pd}~]\s*(?:(?:chapters|chapter|chap|chs|ch|c)\.?\s*)?(?<e>\d{1,4}(?:\.\d{1,2})?))?(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Official = "(Digital)"; a scan / scans / scanlation marker is fan work even beside it.
        private static readonly Regex DigitalRegex = new Regex(@"\(digital\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ScanlationRegex = new Regex(@"\bscan(?:s|lation)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BracketGroupRegex = new Regex(@"\([^()]*\)|\[[^\[\]]*\]|\{[^{}]*\}", RegexOptions.Compiled);
        private static readonly Regex WordCharRegex = new Regex(@"[\p{L}\p{N}]", RegexOptions.Compiled);
        private static readonly Regex WordRegex = new Regex(@"[\p{L}\p{N}]+", RegexOptions.Compiled);
        private static readonly Regex FileExtensionRegex = new Regex(@"\.[a-z][a-z0-9]{1,3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Stands in for a stripped series name: not a word character, so the token regexes read it as a separator.
        private const char NameMark = '\u0001';

        // A chapter release of THIS series: one of its names, then the chapter token, then only tags
        // ("Marriagetoxin c001-073 (Digital) (empyrealarrow)"). Names are replaced first, so a name's own
        // "Chapter 4" is never the token; the series is decided on the same match as the range, and any
        // other words around the token ("Series Chapter 3 Other Arc") mean it is not this series' chapters.
        public static ChapterRange ParseTitle(string title, string seriesName, IEnumerable<string> aliases, string language)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var names = new[] { seriesName }.Concat(aliases ?? Enumerable.Empty<string>());
            var text = MarkNames(Normalize(title), names);
            var match = ChapterTokens(text, language).FirstOrDefault();

            if (match == null)
            {
                return null;
            }

            var before = BracketGroupRegex.Replace(text.Substring(0, match.Index), " ");
            var after = BracketGroupRegex.Replace(text.Substring(match.Index + match.Length), " ");

            if (!before.Contains(NameMark) || WordCharRegex.IsMatch(before) || WordCharRegex.IsMatch(after))
            {
                return null;
            }

            var start = ParseNum(match.Groups["s"].Value);
            var end = match.Groups["e"].Success ? ParseNum(match.Groups["e"].Value) : start;

            if (end < start)
            {
                return null;
            }

            return new ChapterRange
            {
                Start = start,
                End = end,
                IsDigital = DigitalRegex.IsMatch(title),
                IsScanlation = ScanlationRegex.IsMatch(title)
            };
        }

        // The chapter number of one file: its LAST chapter token, since the series name (which may carry
        // its own "Chapter 4") comes before the file's number. A ranged file ("c001-006") holds several
        // chapters and is not one chapter's file.
        public static double? ParseFile(string fileName, string language)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var match = ChapterTokens(Normalize(FileExtensionRegex.Replace(fileName, string.Empty)), language).LastOrDefault();

            if (match == null)
            {
                return null;
            }

            var start = ParseNum(match.Groups["s"].Value);

            if (match.Groups["e"].Success && ParseNum(match.Groups["e"].Value) != start)
            {
                return null;
            }

            return start;
        }

        // A cheap pre-check before trying series names against a title: does it carry a chapter token in
        // English or any market's own form?
        public static bool HasChapterToken(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var text = Normalize(title);

            return EnglishChapterRegex.IsMatch(text) ||
                   MarketLanguages.Any(l => EditionVolumeTokens.ChapterToken(l).IsMatch(text));
        }

        private static readonly string[] MarketLanguages = { "fr", "de", "ja", "ko", "zh" };

        // Every chapter token in order, or none when ANY volume token is present (the market's own
        // volume tokens included).
        private static List<Match> ChapterTokens(string text, string language)
        {
            if (MangaVolumeParser.HasVolumeToken(text) || MangaVolumeParser.HasVolumeToken(EditionVolumeTokens.Rewrite(text, language)))
            {
                return new List<Match>();
            }

            return ChapterRegexes(language)
                .SelectMany(r => r.Matches(text).Cast<Match>())
                .OrderBy(m => m.Index)
                .ToList();
        }

        private static IEnumerable<Regex> ChapterRegexes(string language)
        {
            yield return EnglishChapterRegex;

            var edition = EditionVolumeTokens.ChapterToken(language);

            if (edition != null)
            {
                yield return edition;
            }
        }

        // '_' and a non-decimal '.' are separators, as the volume parse reads them.
        private static string Normalize(string text)
        {
            text = text.Replace('_', ' ');
            return Regex.Replace(text, @"(?<!\d)\.(?!\d)", " ");
        }

        // Replace every occurrence of each name with NameMark, word by word, whatever punctuation sits
        // between the words ("Re:ZERO -Starting Life ... Chapter 4: The Sanctuary" matches "Re ZERO Starting
        // Life ... Chapter 4 The Sanctuary"). Longest names first, so an alias inside the name is not cut out of it.
        private static string MarkNames(string text, IEnumerable<string> names)
        {
            foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderByDescending(n => n.Length))
            {
                var words = WordRegex.Matches(name).Select(m => Regex.Escape(m.Value)).ToList();

                if (words.Count == 0)
                {
                    continue;
                }

                var pattern = string.Join(@"[^\p{L}\p{N}]*", words);

                // Whole words only for a Latin-script edge; CJK names run straight into their tokens ("シリーズ第12話").
                if (IsWordChar(name.Trim().First()))
                {
                    pattern = @"(?<![\p{L}\p{N}])" + pattern;
                }

                if (IsWordChar(name.Trim().Last()))
                {
                    pattern += @"(?![\p{L}\p{N}])";
                }

                text = Regex.Replace(text, pattern, " " + NameMark + " ", RegexOptions.IgnoreCase);
            }

            return text;
        }

        private static bool IsWordChar(char c)
        {
            return c < 0x3000 && char.IsLetterOrDigit(c);
        }

        private static double ParseNum(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    }
}
