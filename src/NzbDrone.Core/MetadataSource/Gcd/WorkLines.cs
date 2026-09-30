using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Manga;

namespace NzbDrone.Core.MetadataSource.Gcd
{
    // Line safety (2026-09-28): the other lines of a line's work in the same market (language) and the
    // same library class (novel or not, as GcdMetadataService.IsLightNovel splits them). The Add results'
    // "Spin-off of" label, Switch Line's list and the sibling-release import guard all read this one
    // helper. A line without tome_work_id or language (an older artifact) has no siblings, so nothing is
    // labelled, listed or rejected for it.
    public static class WorkLines
    {
        // Every line of the work (the catalogue read); empty when the line carries no work id.
        public static List<GcdSeries> Of(IGcdMetadataService gcd, GcdSeries line)
        {
            if (gcd == null || line == null || line.TomeWorkId.IsNullOrWhiteSpace())
            {
                return new List<GcdSeries>();
            }

            return gcd.GetWorkLines(line.TomeWorkId) ?? new List<GcdSeries>();
        }

        // The work's other lines in the line's market and library class that have at least one volume.
        public static List<GcdSeries> Siblings(GcdSeries line, IEnumerable<GcdSeries> workLines)
        {
            if (line == null || line.TomeWorkId.IsNullOrWhiteSpace() || line.Language.IsNullOrWhiteSpace())
            {
                return new List<GcdSeries>();
            }

            var novel = GcdMetadataService.IsLightNovel(line.Medium);

            return (workLines ?? Enumerable.Empty<GcdSeries>())
                .Where(c => c.GcdSeriesId != line.GcdSeriesId)
                .Where(c => c.TomeWorkId == line.TomeWorkId && c.Language == line.Language)
                .Where(c => GcdMetadataService.IsLightNovel(c.Medium) == novel)
                .Where(c => c.VolumeCount >= 1)
                .ToList();
        }

        // The work's main line in that market and class, when this line is not it; null when this line is
        // the main line or the market has none. Several flagged main lines: the best dated, then the longest.
        public static GcdSeries MainLine(GcdSeries line, IEnumerable<GcdSeries> workLines)
        {
            if (line == null || line.IsMain)
            {
                return null;
            }

            return Siblings(line, workLines)
                .Where(c => c.IsMain)
                .OrderByDescending(c => c.DatedCount ?? 0)
                .ThenByDescending(c => c.VolumeCount)
                .ThenBy(c => c.GcdSeriesId)
                .FirstOrDefault();
        }

        // The name a line is shown by: its local title when it has one, else its name; a light novel's
        // catalogue name sheds the "(light novel)"-style qualifier, as the add path names an entry.
        public static string DisplayName(GcdSeries line, LibraryType library)
        {
            if (line == null)
            {
                return null;
            }

            var name = line.LocalName.IsNotNullOrWhiteSpace() ? line.LocalName : line.Name;

            return library == LibraryType.LightNovel ? MangaSeriesMetadataProvider.StripLightNovelQualifier(name) : name;
        }

        // Review fixes (2026-09-28, I6); ported from mangarrbot/spinoff.py (2026-09-28) so the bot's
        // "Spin-off of" label reads the same as Mangarr's. KEEP THESE WORD LISTS IN STEP WITH THAT FILE
        // (EDITION_WORDS / MEDIUM_WORDS) -- a line is another edition/format of the same run, never a
        // spin-off, when (case- and accent-insensitive, whole words/phrases; a space or hyphen inside a
        // phrase matches either):
        //   1. its name carries an EDITION word anywhere ("Inuyasha (VizBig edition)", "X 3-in-1",
        //      "X (Première édition)"), or
        //   2. its last parenthetical names nothing but EDITION/MEDIUM words ("(Roman web)", "(Médias)",
        //      "(Web novel)", "(Print)", "(Roman illustré)", "(Printed media)"). MEDIUM words only count
        //      there: anywhere in the name they'd hide real spin-offs such as "86 (novel series) (Alter)",
        //      "One Piece - Novel HEROINES", "Rail Wars! (Light novel - side story)" and
        //      "Tearmoon Empire (Light novel dérivé)".
        private static readonly string[] EditionWords =
        {
            "edition", "omnibus", "deluxe", "kanzenban", "shinsoban", "shinsouban", "bunko", "perfect",
            "collector", "collectors", "collector's", "vizbig", "release", "volumes"
        };

        private static readonly string[] MediumWords =
        {
            "web novel", "webnovel", "roman web", "light novel", "novel", "roman", "webtoon", "manhwa", "manhua",
            "médias", "medias", "media", "print", "digital", "e-book", "ebook",
            "roman illustré", "roman illustre", "illustrated novel", "printed media", "print media"
        };

        private const string NIn1 = @"\d+\s*-?\s*in\s*-?\s*1";

        private static readonly Regex EditionQualifier = BuildWordsRegex(EditionWords);
        private static readonly Regex AnyQualifier = BuildWordsRegex(EditionWords.Concat(MediumWords));
        private static readonly Regex Parenthetical = new Regex(@"\(([^()]*)\)", RegexOptions.Compiled);
        private static readonly Regex NonWord = new Regex(@"[\W_]+", RegexOptions.Compiled);

        private static Regex BuildWordsRegex(IEnumerable<string> words)
        {
            var phrases = new HashSet<string>(words.Select(w =>
                string.Join(@"[\s-]+", Regex.Split(FoldAccents(w), @"[\s-]+").Select(Regex.Escape))));

            var alternatives = phrases.OrderByDescending(p => p.Length).ToList();
            alternatives.Add(NIn1);

            return new Regex(@"\b(?:" + string.Join("|", alternatives) + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        // Accent-insensitive form: drop Latin combining marks (ō -> o, é -> e). Mirrors spinoff.py's _fold
        // (NFKD, not NFD -- compatibility decomposition so full-width/compatibility forms fold the same way).
        private static string FoldAccents(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var decomposed = text.Normalize(NormalizationForm.FormKD);
            var sb = new StringBuilder(decomposed.Length);

            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // True when the name says the line is an edition/format of the run (rules 1 and 2 above).
        public static bool HasEditionQualifier(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var folded = FoldAccents(name);

            if (EditionQualifier.IsMatch(folded))
            {
                return true;
            }

            var matches = Parenthetical.Matches(folded);

            if (matches.Count == 0)
            {
                return false;
            }

            var lastGroup = matches[matches.Count - 1].Groups[1].Value;

            if (!AnyQualifier.IsMatch(lastGroup))
            {
                return false;
            }

            return NonWord.Replace(AnyQualifier.Replace(lastGroup, string.Empty), string.Empty).Length == 0;
        }

        // "Spin-off of <main line>" (the Add results, Switch Line's list, Collections): the main line's name
        // when this line is another story of the work. Null when the line is main or the market has none, and
        // for a line that is the same run in another edition: a counterpart of the main line (IsCounterpart
        // either way -- the same original), an omnibus, or a name carrying an edition qualifier.
        public static string SpinOffOf(GcdSeries line, IEnumerable<GcdSeries> workLines, LibraryType library)
        {
            var main = MainLine(line, workLines);

            if (main == null ||
                EditionResolver.IsCounterpart(line, main) ||
                EditionResolver.IsCounterpart(main, line) ||
                line.IsOmnibus ||
                HasEditionQualifier(line.Name) ||
                HasEditionQualifier(line.LocalName))
            {
                return null;
            }

            return DisplayName(main, library);
        }

        // Search candidates and Collections (item A): the bound line's count and publisher, and the
        // "Spin-off of" name when the line is not its market's main line and one exists.
        public static CatalogueLineFacts Facts(GcdSeries line, IEnumerable<GcdSeries> workLines, LibraryType library)
        {
            if (line == null)
            {
                return null;
            }

            return new CatalogueLineFacts
            {
                VolumeCount = line.VolumeCount,
                Publisher = line.Publisher.IsNotNullOrWhiteSpace() ? line.Publisher : null,
                SpinOffOf = SpinOffOf(line, workLines, library)
            };
        }
    }
}
