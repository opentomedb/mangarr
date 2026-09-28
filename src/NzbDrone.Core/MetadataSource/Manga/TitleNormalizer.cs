using System.Text;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource.Manga
{
    // Query-side punctuation normalisation for the live sources (D2). AniList's search returns
    // ZERO candidates for a typographic apostrophe ("Let’s Do It Already!" in the 2026-09-15
    // audit), so the stored (curated) name is folded to ASCII punctuation before it is sent.
    // Only the QUERY changes: TitleMatcher still compares alphanumerics only, so a candidate
    // titled with the curly form still equals the folded query. Since 2026-09-24 the query is
    // also TitleFold'ed first (OpenTome's for_search()): "Fushigi Yûgi" is asked as "Fushigi
    // Yugi", "Ranma ½" as "Ranma 1/2" (AniList's own romaji); the key TitleMatcher takes after the
    // same fold, so query and key still agree.
    public static class TitleNormalizer
    {
        public static string ForSearch(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var sb = new StringBuilder(title.Length);
            var pendingSpace = false;

            foreach (var c in TitleFold.Fold(title).Trim())
            {
                var mapped = Fold(c);

                if (mapped == ' ')
                {
                    pendingSpace = true;
                    continue;
                }

                if (pendingSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                pendingSpace = false;
                sb.Append(mapped);
            }

            return sb.ToString();
        }

        private static char Fold(char c)
        {
            switch (c)
            {
                case '\u2018':
                case '\u2019':
                    return '\'';
                case '\u201C':
                case '\u201D':
                    return '"';
                case '\u2013':
                case '\u2014':
                    return '-';
                case '\u00A0':
                    return ' ';
                default:
                    return c;
            }
        }
    }
}
