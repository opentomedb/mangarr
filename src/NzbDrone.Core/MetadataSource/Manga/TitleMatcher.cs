using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource.Manga
{
    // Validates that a metadata-source search hit is actually the series that was searched for.
    // The live sources (AniList / MangaUpdates / MangaDex) rank fuzzily, and blindly taking the
    // first hit for a subtitle-heavy query ("Re:ZERO ..., Chapter 3: Truth of Zero") attaches a
    // WRONG series' volume count and status — worse than no data. Matching is normalized exact
    // equality: punctuation, spacing and case differences are ignored, but "Black Clover" never
    // matches "Black Clover Gaiden: Quartet Knights". Since 2026-09-24 the key is taken after
    // TitleFold (OpenTome's fold()): an accent on a Latin letter and a numeric symbol are ignored
    // too ("Fushigi Yûgi" = "Fushigi Yugi", "Ranma ½" = "Ranma 1/2" = ranma12); kana voicing
    // marks, Hangul, CJK and Cyrillic still count.
    public static class TitleMatcher
    {
        public static string Normalize(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var sb = new StringBuilder(title.Length);

            foreach (var c in TitleFold.Fold(title).ToLowerInvariant())
            {
                if (IsKeyChar(c))
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        // Python's isalnum (OpenTome's key()): letters, decimal digits, and the numeric letters /
        // other numbers the fold left as they were -- a No / Nl character with no NFKC form, e.g.
        // the ideographic zero 〇 (U+3007).
        private static bool IsKeyChar(char c)
        {
            if (char.IsLetterOrDigit(c))
            {
                return true;
            }

            if (c < 0x80)
            {
                return false;
            }

            var category = char.GetUnicodeCategory(c);

            return category == UnicodeCategory.LetterNumber || category == UnicodeCategory.OtherNumber;
        }

        public static bool Matches(string query, IEnumerable<string> candidateTitles)
        {
            var normalizedQuery = Normalize(query);

            if (normalizedQuery.Length == 0 || candidateTitles == null)
            {
                return false;
            }

            return candidateTitles.Any(c => Normalize(c) == normalizedQuery);
        }

        public static bool Matches(string query, params string[] candidateTitles)
        {
            return Matches(query, (IEnumerable<string>)candidateTitles);
        }

        // Floor for SEARCH-path relaxed matching (never used on add/refresh): below this
        // token similarity a candidate is treated as a different series entirely.
        public const decimal SearchFloor = 0.5m;

        // Relaxed similarity for user-typed search queries: 1.0 on normalized exact equality,
        // else the best Dice coefficient over normalized word tokens across the candidate's
        // titles. "apothecary diaries" scores 0.8 against "The Apothecary Diaries"; a candidate
        // sharing no tokens scores 0. Callers must pick the HIGHEST-scoring candidate at or
        // above SearchFloor, not the first above it — provider relevance ranking routinely puts
        // a franchise spinoff above the main series ("...Slime: Trinity in Tempest" outranks
        // "...Slime" on AniList), and the main series wins only by score.
        public static decimal SearchScore(string query, IEnumerable<string> candidateTitles)
        {
            var normalizedQuery = Normalize(query);

            if (normalizedQuery.Length == 0 || candidateTitles == null)
            {
                return 0m;
            }

            var queryTokens = Tokenize(query);
            var best = 0m;

            foreach (var candidate in candidateTitles)
            {
                if (Normalize(candidate) == normalizedQuery)
                {
                    return 1m;
                }

                var candidateTokens = Tokenize(candidate);

                if (queryTokens.Count == 0 || candidateTokens.Count == 0)
                {
                    continue;
                }

                var overlap = queryTokens.Count(candidateTokens.Contains);
                var dice = 2m * overlap / (queryTokens.Count + candidateTokens.Count);

                if (dice > best)
                {
                    best = dice;
                }
            }

            return best;
        }

        private static HashSet<string> Tokenize(string title)
        {
            var tokens = new HashSet<string>();

            if (title.IsNullOrWhiteSpace())
            {
                return tokens;
            }

            var sb = new StringBuilder();

            foreach (var c in TitleFold.Fold(title).ToLowerInvariant())
            {
                if (IsKeyChar(c))
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
            }

            if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
            }

            return tokens;
        }
    }
}
