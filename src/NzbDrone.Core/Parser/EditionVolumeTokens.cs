using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Parser
{
    // Preferred Edition (2026-09-24, spec §3 Parser): a market's own volume tokens, rewritten to "Vol."
    // so the existing parse (MangaVolumeParser: singles, ranges, the pack fan-out) reads them unchanged.
    // Applied ONLY for a series whose edition is that language: an English title that happens to say
    // "Band 2" or "T05" parses exactly as before (EnEditionPinning). "T05" is so short it counts only
    // right after a series name the caller accepts. Tokens are data: a new market is one case here and
    // one in IndexerSearch/Definitions/EditionSearchTokens.
    public static class EditionVolumeTokens
    {
        // Preferred Edition (2026-09-24, M9 fix round 1 I1): the end may repeat the token ("Tome 1 à Tome 5").
        private static readonly Regex FrRange = new Regex(@"\bTomes?\s*\.?\s*(?<s>\d{1,3})\s*(?:à|a|-|\p{Pd})\s*(?:Tomes?\s*\.?\s*)?(?<e>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FrSingle = new Regex(@"\bTome\s*\.?\s*(?<n>\d{1,3}(?:\.\d{1,2})?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Preferred Edition (2026-09-24): "T05", or the range "T01 à T03" / "T01-T03" / "T01-03" -- the range read in the same match, so a
        // pack never becomes "Vol. 01 à T03" (volume 1). Same acceptance gate either way.
        private static readonly Regex FrShort = new Regex(@"(?<![\w'])T(?<s>\d{1,3})(?:\s*(?:à|a|-|\p{Pd})\s*T?(?<e>\d{1,3}))?\b", RegexOptions.Compiled);
        // Fix round 1: "Band.05" (a dotted scene name keeps the dot before a digit) reads like "Band 05".
        private static readonly Regex DeRange = new Regex(@"\b(?:Bände|Baende|Bd|Band)\s*\.?\s*(?<s>\d{1,3})\s*(?:bis|-|\p{Pd})\s*(?:(?:Bände|Baende|Bd|Band)\s*\.?\s*)?(?<e>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DeSingle = new Regex(@"\b(?:Band|Bd)\s*\.?\s*(?<n>\d{1,3}(?:\.\d{1,2})?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // Preferred Edition (2026-09-24, M9 fix round 1 I2): "1-5巻" / "第01-34巻" is a pack. It is written as
        // "Volumes 1 to 5", which ParseVolume reads as a range and the single-volume parses do not read at all,
        // so a pack is never its last volume. A single N巻 never follows a digit, an adjacent dash or 全 ("全34巻"
        // is a whole set, not volume 34).
        private static readonly Regex JaRange = new Regex(@"(?:第\s*)?(?<![\d.])(?<s>\d{1,3})\s*[\p{Pd}～〜]\s*(?<e>\d{1,3})\s*巻", RegexOptions.Compiled);
        // Polish: a dash blocks only when it touches the number ("-5巻" is a pack's tail); a spaced separator
        // ("進撃の巨人 - 5巻") is volume 5 -- JaRange has already taken every "N - M巻" pack.
        private static readonly Regex JaSingle = new Regex(@"(?<!第\s*)(?<![\d.～〜全]\s*)(?<!\p{Pd})(?<n>\d{1,3})\s*巻", RegexOptions.Compiled);

        // Preferred Edition (2026-09-24, ruling A9): box sets ("Coffret", "Box Set") are collected releases too;
        // fix round 1: so is Carlsen's "Massiv".
        private static readonly Regex CollectedMarker = new Regex(@"\b(?:Int[ée]grale|Doppelband|Massiv|Perfect\s+Edition|Coffret|Box\s*-?\s*Set)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Rewrite(string title, string editionLanguage, Func<string, bool> isAcceptedSeries = null)
        {
            if (title == null || EditionLanguages.IsEnglish(editionLanguage))
            {
                return title;
            }

            // Preferred Edition (2026-09-24, M9 fix round 1): '_' and non-decimal '.' are separators, as
            // TryParseSeriesVolume reads them -- "L_Attaque_des_Titans_Tome_05" has no word boundary before
            // "Tome" otherwise. The language is matched case-insensitively.
            title = title.Replace('_', ' ');
            title = Regex.Replace(title, @"(?<!\d)\.(?!\d)", " ");

            switch (editionLanguage.Trim().ToLowerInvariant())
            {
                case "fr":
                    title = FrRange.Replace(title, " Vol. ${s}-${e} ");
                    title = FrSingle.Replace(title, " Vol. ${n} ");

                    if (isAcceptedSeries != null)
                    {
                        var m = FrShort.Match(title);

                        if (m.Success && isAcceptedSeries(SeriesText(title.Substring(0, m.Index))))
                        {
                            var volume = m.Groups["e"].Success ? m.Groups["s"].Value + "-" + m.Groups["e"].Value : m.Groups["s"].Value;
                            title = title.Substring(0, m.Index) + " Vol. " + volume + " " + title.Substring(m.Index + m.Length);
                        }
                    }

                    break;
                case "de":
                    title = DeRange.Replace(title, " Vol. ${s}-${e} ");
                    title = DeSingle.Replace(title, " Vol. ${n} ");
                    break;
                case "ja":
                    title = JaRange.Replace(title, " Volumes ${s} to ${e} ");
                    title = JaSingle.Replace(title, "第${n}巻");
                    break;
            }

            return title;
        }

        // Preferred Edition (2026-09-24): "Intégrale" / "Doppelband" / "Perfect Edition" / "Coffret" / "Box Set": a collected book. A numbered
        // one is no volume -- nor pack -- of the edition (Parser rejects it, ruling A9); a tokenless one stays a
        // whole-series batch.
        public static bool IsCollectedEdition(string title)
        {
            return title != null && CollectedMarker.IsMatch(title);
        }

        // Preferred Edition (2026-09-24, M9 fix round 1 ⚠2): a series whose own name, anchor or alias carries
        // a collected marker is bound to that collected line ("One Piece Massiv", "… Perfect Edition"), so its
        // releases carry the marker too and are its own volumes -- exempt from the A9 reject.
        // 2026-09-26: or the bound line itself is collected (EditionCollected, set on refresh) -- an
        // omnibus-only line whose names carry no marker.
        public static bool IsCollectedSeries(AuthorMetadata meta)
        {
            return meta != null && (meta.EditionCollected || SeriesNames(meta).Any(IsCollectedEdition));
        }

        // Preferred Edition (2026-09-24): the names a file or release of this edition's series may carry before
        // "T05" -- its name, English anchor and aliases (alias language is a hint, never a filter), each an exact
        // normalized match with the parser's length floor. Null for an English series, so "T05" stays unread.
        public static Func<string, bool> SeriesMatcher(AuthorMetadata meta)
        {
            if (EditionLanguages.IsEnglish(meta?.EditionLanguage))
            {
                return null;
            }

            var keys = SeriesNames(meta)
                .Select(k => k.CleanAuthorName())
                .Where(k => k.Length >= 4)
                .ToHashSet();

            return s => keys.Contains(s.CleanAuthorName());
        }

        private static IEnumerable<string> SeriesNames(AuthorMetadata meta)
        {
            return new[] { meta.Name, meta.AnchorName }
                .Concat(meta.Aliases ?? new List<string>())
                .Where(k => k.IsNotNullOrWhiteSpace());
        }

        // The series text before a token as the parser reads it: '_' and non-decimal '.' are spaces,
        // leading [group] tags go, trailing separators are trimmed.
        private static string SeriesText(string prefix)
        {
            var text = prefix.Replace('_', ' ');
            text = Regex.Replace(text, @"(?<!\d)\.(?!\d)", " ");
            text = Regex.Replace(text, @"^\s*(?:\[[^\]]*\]\s*)+", string.Empty);

            return Regex.Replace(text, @"\s+", " ").Trim(' ', '-', '–', ',', ':');
        }
    }
}
