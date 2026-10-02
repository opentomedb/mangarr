using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Manga;

namespace NzbDrone.Core.MetadataSource
{
    // Audiobook identity B1 (2026-09-17, D3): the one subtitle rule for an Audible product and a Google
    // ISBN record. The record's own subtitle wins, then Google's shortSeriesBookTitle, then the title
    // itself when it is "<series> <N>: <subtitle>" for THIS series and THIS volume — the same regex on
    // "Sword Art Online 1: Aincrad" (Audible) and "Sword Art Online 2: Aincrad (light novel)" (Google).
    // Every candidate goes through the same gate: a bare volume label ("Vol. 1"), the series name
    // itself (or an alias, or either followed by a volume number: "Overlord, Book 1"), an edition
    // label ("A Light Novel"), a stub shorter than 3 characters, or (fix round 3, 2026-09-24) a
    // re-stated volume/book number or "(Light Novel)" tag anywhere in the text, or the series' own
    // name anywhere in the text (IsJunk) is no subtitle, and a rejected candidate is null — there is
    // no second guess from a weaker source. Exception (2026-09-24): a full volume title that OPENS
    // with the whole series name ("Rascal Does Not Dream of Petite Devil Kohai") is kept (IsFullTitle),
    // and "<series>: X" is cut to X (WithoutSeriesPrefix) -- both for the trusted catalogue title only.
    public static class Subtitles
    {
        private static readonly Regex TrailingEditionTag = new Regex(@"\s*\((light novel|manga|novel)\)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TitleShape = new Regex(@"^(?<series>.+?)\s*[,:\-–—]?\s*(?:Vol\.?|Volume|Book)?\s*(?<num>\d+(?:\.\d+)?)\s*[:\-–—]\s*(?<sub>.+)$", RegexOptions.Compiled);
        private static readonly Regex VolumeLabel = new Regex(@"^(Vol\.?|Volume|Book)\s*\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Fix wave (2026-09-17, I1): on the normalised candidate (lower-case alphanumerics only) —
        // what is left after the series name or an alias ("book1", "2", "3lightnovel"), and an
        // edition label on its own ("alightnovel", "manga").
        private static readonly Regex SeriesRemainder = new Regex(@"^(vol|volume|book)?\d+(lightnovel|novel|manga)?$", RegexOptions.Compiled);
        private static readonly Regex EditionLabel = new Regex(@"^a?(lightnovel|novel|manga)$", RegexOptions.Compiled);

        // Junk subtitle filter (fix round 3, 2026-09-24; live finding after 10.0.0.557): Audible and
        // Google sometimes put the WHOLE volume label into their "subtitle" field instead of a real
        // arc name -- "Jobless Reincarnation (Light Novel), Vol. 1", "Light Novel, Vol. 5", "Light
        // Novel (Classroom of the Elite, Book 26)" all reached storage this way before this filter
        // existed. None of a re-stated volume/book number, the "(Light Novel)" edition tag anywhere
        // in the text (TrailingEditionTag only strips it at the very end), or the series' own name is
        // ever a real subtitle.
        private static readonly Regex VolumeNumberToken = new Regex(@"\b(?:vol|volume|book)\.?\s*\d+|\bv\.?\s*\d+|#\s*\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LightNovelToken = new Regex(@"light\s*novel", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Full-title subtitles (2026-09-24, the maintainer: "Rascal Does Not Dream have subtitles that should be
        // present in Mangarr for LN's too"): words after the series name that say nothing about THIS
        // volume -- a bare number ("Sword Art Online Progressive 2") or an edition/generic word -- do
        // not count towards the two a full volume title needs.
        private static readonly Regex BareNumber = new Regex(@"^\d+(?:\.\d+)?$", RegexOptions.Compiled);
        private static readonly HashSet<string> EditionWords = new HashSet<string> { "light", "novel", "novels", "manga", "series", "edition", "collection", "omnibus", "audiobook", "unabridged", "audio" };

        // Preferred Edition (2026-09-24, spec §5): an edition's own volume labels and edition words -- a
        // French "Tome 3", a German "Band 2" / "Bd. 2", a Japanese "第3巻", "Roman" -- are no subtitle
        // either. Only for that edition: an English subtitle "Brass Band 2" stays what it was.
        // M14 fix round 1 (2026-09-24): "Tome" in any case, but the abbreviation only as a capital "T"
        // ("T. 3", "T05") -- a lower-case "t 3" is ordinary text, not a French volume label. German also
        // numbers parts ("Teil 2"). Japanese: Arabic or kanji numerals ("第3巻", "第三巻") and the
        // two-/three-part labels ("上巻", "下巻") as a token of their own.
        private static readonly Regex FrenchVolumeToken = new Regex(@"\b(?:(?i:tome)|T)\.?\s*\d+", RegexOptions.Compiled);
        private static readonly Regex GermanVolumeToken = new Regex(@"\b(?:band|bd|teil)\.?\s*\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JapaneseVolumeToken = new Regex(@"第?\s*\d+\s*巻|第\s*[一二三四五六七八九十百千〇零]+\s*巻|(?:^|\s)[上中下]巻(?:\s|$)", RegexOptions.Compiled);

        // KR/CN piece 2 (2026-10-02, M5): "5권" / "제5권"; "第5卷" / "5册" / "第五卷" / "第5集".
        private static readonly Regex KoreanVolumeToken = new Regex(@"(?:제\s*)?\d+\s*권", RegexOptions.Compiled);
        private static readonly Regex ChineseVolumeToken = new Regex(@"第?\s*\d+\s*[卷册冊集]|第\s*[一二三四五六七八九十百千〇零]+\s*[卷册冊集]", RegexOptions.Compiled);

        // Built once (not per word): IsFullTitle asks for every word of every English candidate too, and
        // an English (or unknown) edition answers the one shared empty set.
        private static readonly HashSet<string> NoEditionWords = new HashSet<string>();
        private static readonly HashSet<string> FrenchEditionWords = new HashSet<string> { "roman", "romans", "tome", "edition", "integrale", "collector" };
        private static readonly HashSet<string> GermanEditionWords = new HashSet<string> { "roman", "band", "ausgabe", "edition", "sammelband" };
        private static readonly HashSet<string> JapaneseEditionWords = new HashSet<string> { "小説", "巻", "文庫", "ライトノベル" };
        private static readonly HashSet<string> KoreanEditionWords = new HashSet<string> { "소설", "라이트노벨", "권", "완전판", "합본" };
        private static readonly HashSet<string> ChineseEditionWords = new HashSet<string> { "小说", "小說", "轻小说", "輕小說", "卷", "完全版", "合订本", "合訂本" };

        // M14 fix round 1 (2026-09-24): an article with an edition word is still only the edition word
        // ("Le roman", "L'intégrale", "Der Roman") -- EditionJunk alone reads these, and only next to an
        // edition word, so an article never makes a real subtitle junk and IsFullTitle still counts it.
        private static readonly HashSet<string> FrenchArticles = new HashSet<string> { "le", "la", "les", "l", "un", "une", "des" };
        private static readonly HashSet<string> GermanArticles = new HashSet<string> { "der", "die", "das", "ein", "eine" };
        // M14 polish (2026-09-24): every Unicode space (\s is \p{Z} plus the controls -- a French
        // non-breaking U+00A0, a Japanese ideographic U+3000) and both apostrophes separate words.
        private static readonly Regex WordAndElisionSeparators = new Regex(@"[\s'\u2019]+", RegexOptions.Compiled);

        // Aincrad subtitle (2026-09-23, controller ruling): a trusted candidate is the catalogue
        // artifact's own volume title (OpenTome's volumes.title, already filtered of markup,
        // number-only and redundant titles) -- it bypasses the alias gate, since a catalogue title
        // that happens to equal one of the entry's OWN aliases (SAO's "Aincrad") is still a real
        // subtitle, not the series naming itself. An Audible/Google candidate keeps the gate.
        //
        // editionLanguage (Preferred Edition, 2026-09-24): the series' edition; null/"en" is today's filter.
        public static string Derive(string title, string subtitle, string seriesBookTitle, string seriesName, IEnumerable<string> aliases, double volumeNumber, bool trustedArtifactCandidate = false, string editionLanguage = null)
        {
            var candidate = subtitle;

            if (candidate.IsNullOrWhiteSpace())
            {
                candidate = seriesBookTitle;
            }

            if (candidate.IsNullOrWhiteSpace())
            {
                candidate = FromTitle(title, seriesName, aliases, volumeNumber);
            }

            if (candidate == null)
            {
                return null;
            }

            // Review fix (2026-09-24): the "<series>: X" cut and the full-title exception are for the
            // catalogue's own volume title only -- an Audible/Google field that opens with the series
            // name is far more often a product/edition label ("Sword Art Online Progressive Barcarolle
            // of Froth", "Overlord Unabridged Audiobook") than a volume title, so it keeps the strict gate.
            var stripped = TrailingEditionTag.Replace(candidate, string.Empty).Trim();
            candidate = trustedArtifactCandidate ? WithoutSeriesPrefix(stripped, seriesName) : stripped;

            if (candidate.Length < 3 || VolumeLabel.IsMatch(candidate) || NamesSeries(candidate, seriesName, aliases, trustedArtifactCandidate) || IsJunk(candidate, seriesName, allowFullTitle: trustedArtifactCandidate, editionLanguage: editionLanguage))
            {
                return null;
            }

            return candidate;
        }

        // Fix round 3 (2026-09-24): a re-stated volume/book number ("Vol. 1", "Book 26", "#3"), the
        // "(Light Novel)" edition tag anywhere in the text, or the series' own name -- the whole name,
        // or (a colon-separated series like "Mushoku Tensei: Jobless Reincarnation") one of its main
        // segments -- appearing anywhere in the candidate, unless the whole name opens a full volume
        // title (IsFullTitle, 2026-09-24). Public so LightNovelTitles.Display can run
        // the same test as defence in depth against a Book.Subtitle stored before this filter existed
        // (a refresh clears it -- see MangaSeriesMetadataProvider.HadSubtitleCandidate -- but Display
        // never trusts stored data blindly either).
        //
        // allowFullTitle (review fix, 2026-09-24): Derive passes it only for a trusted catalogue
        // candidate; the default (true) keeps Display/ShownSubtitle permissive for a stored value.
        //
        // editionLanguage (Preferred Edition, 2026-09-24): a non-English edition's own volume labels and
        // edition words are junk too (EditionJunk); null/"en" is today's filter, byte for byte.
        public static bool IsJunk(string candidate, string seriesName, bool allowFullTitle = true, string editionLanguage = null)
        {
            if (candidate.IsNullOrWhiteSpace())
            {
                return true;
            }

            if (VolumeNumberToken.IsMatch(candidate) || LightNovelToken.IsMatch(candidate))
            {
                return true;
            }

            if (!EditionLanguages.IsEnglish(editionLanguage) && EditionJunk(candidate, editionLanguage.Trim()))
            {
                return true;
            }

            if (seriesName.IsNullOrWhiteSpace())
            {
                return false;
            }

            // Full-title subtitles (2026-09-24): Yen Press names Rascal's volumes "Rascal Does Not
            // Dream of Petite Devil Kohai" -- the series name opening a real volume title is that
            // title, not a restatement of the series. Only the whole name as a word prefix counts;
            // the series name anywhere else still rejects below.
            if (allowFullTitle && IsFullTitle(candidate, seriesName, editionLanguage))
            {
                return false;
            }

            var normalisedCandidate = TitleMatcher.Normalize(candidate);

            // Split only on ": " (colon-space) -- the "Series: Subtitle-restating-text" shape from
            // the brief's own example ("Mushoku Tensei: Jobless Reincarnation"). A bare colon with no
            // following space is part of a stylised single name ("Re:Zero"), and splitting on it too
            // would cut that down to "Re" / "Zero" -- "Zero" alone then false-positives against a
            // genuine arc name like "Truth of Zero" (an_alias_series_prefix_is_accepted).
            return seriesName.Split(new[] { ": " }, StringSplitOptions.None)
                .Select(s => s.Trim())
                .Where(s => s.Length >= 4)
                .Select(TitleMatcher.Normalize)
                .Any(segment => segment.Length > 0 && normalisedCandidate.Contains(segment));
        }

        // The title without a trailing "(light novel)" / "(manga)" / "(novel)" edition tag.
        public static string WithoutEditionTag(string title)
        {
            return title.IsNullOrWhiteSpace() ? title : TrailingEditionTag.Replace(title, string.Empty).Trim();
        }

        // "<series>: X" subtitles (2026-09-24): a candidate that opens with the exact series name
        // (case aside) followed by ": " is the series naming its own volume -- the remainder is the
        // subtitle ("Tokyo Ghoul: Days" -> "Days", shown "Tokyo Ghoul: Days (Vol. N)"). The
        // remainder still goes through every gate (volume label, series name/alias, IsJunk), so
        // "Mushoku Tensei: Jobless Reincarnation: Jobless Reincarnation (Light Novel), Vol. 1"
        // stays junk; a name that is not at the start ("The Melancholy of Haruhi Suzumiya") is
        // untouched here and IsJunk rejects it as before. Public so LightNovelTitles.ShownSubtitle
        // applies the same cut to a stored value.
        public static string WithoutSeriesPrefix(string candidate, string seriesName)
        {
            if (candidate.IsNullOrWhiteSpace() || seriesName.IsNullOrWhiteSpace())
            {
                return candidate;
            }

            var prefix = seriesName.Trim() + ": ";

            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            var remainder = candidate.Substring(prefix.Length).Trim();

            return remainder.Length > 0 ? remainder : candidate;
        }

        // Full-title subtitles (2026-09-24): the candidate opens with the WHOLE series name as a word
        // prefix (TitleMatcher-normalised, so punctuation and case are ignored: "Fullmetal Alchemist:
        // The Land of Sand") and goes on with at least two words that are neither a bare number nor an
        // edition word ("Rascal Does Not Dream of Petite Devil Kohai" -- yes; "Sword Art Online
        // Progressive 2", "Rascal Does Not Dream Novel Series", "Rascal Does Not Dreamer of X" -- no).
        // A token with no letter or digit ("+" in "...of a Beach Queen +") is not a word. The volume
        // label / "(Light Novel)" tokens are IsJunk's to reject, before this is asked.
        // Preferred Edition (2026-09-24): a non-English edition's own edition words ("Roman", "Tome",
        // "Band") do not count either; for English (null) that set is empty and nothing changes.
        public static bool IsFullTitle(string candidate, string seriesName, string editionLanguage = null)
        {
            var seriesKey = TitleMatcher.Normalize(seriesName);

            if (candidate.IsNullOrWhiteSpace() || seriesKey.Length == 0)
            {
                return false;
            }

            var words = candidate.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var prefix = string.Empty;
            var editionWords = EditionWordsFor(editionLanguage);

            for (var i = 0; i < words.Length; i++)
            {
                prefix += TitleMatcher.Normalize(words[i]);

                if (prefix == seriesKey)
                {
                    return words.Skip(i + 1)
                        .Select(TitleMatcher.Normalize)
                        .Count(w => w.Length > 0 && !BareNumber.IsMatch(w) && !EditionWords.Contains(w) && !editionWords.Contains(w)) >= 2;
                }

                if (prefix.Length >= seriesKey.Length)
                {
                    return false;
                }
            }

            return false;
        }

        private static HashSet<string> EditionWordsFor(string editionLanguage)
        {
            switch (EditionLanguages.BaseCode(editionLanguage))
            {
                case "fr":
                    return FrenchEditionWords;
                case "de":
                    return GermanEditionWords;
                case "ja":
                    return JapaneseEditionWords;
                case "ko":
                    return KoreanEditionWords;
                case "zh":
                    return ChineseEditionWords;
                default:
                    return NoEditionWords;
            }
        }

        private static bool EditionJunk(string candidate, string editionLanguage)
        {
            var language = EditionLanguages.BaseCode(editionLanguage);

            switch (language)
            {
                case "fr":
                    if (FrenchVolumeToken.IsMatch(candidate))
                    {
                        return true;
                    }

                    break;
                case "de":
                    if (GermanVolumeToken.IsMatch(candidate))
                    {
                        return true;
                    }

                    break;
                case "ja":
                    if (JapaneseVolumeToken.IsMatch(candidate))
                    {
                        return true;
                    }

                    break;
                case "ko":
                    if (KoreanVolumeToken.IsMatch(candidate))
                    {
                        return true;
                    }

                    break;
                case "zh":
                    if (ChineseVolumeToken.IsMatch(candidate))
                    {
                        return true;
                    }

                    break;
                default:
                    return false;
            }

            var editionWords = EditionWordsFor(editionLanguage);
            var articles = language == "fr" ? FrenchArticles : language == "de" ? GermanArticles : NoEditionWords;
            var words = WordAndElisionSeparators.Split(candidate).Select(TitleMatcher.Normalize).Where(w => w.Length > 0).ToList();

            return words.Any(w => editionWords.Contains(w)) && words.All(w => editionWords.Contains(w) || articles.Contains(w));
        }

        // The candidate is the series name or an alias, or one of those followed by nothing but a
        // volume number ("Sword Art Online 2", "Overlord, Book 1"), or an edition label. Public
        // since the search tier (B3b, D4) asks the same question of a stored subtitle.
        //
        // Aincrad subtitle (2026-09-23): a trusted artifact candidate skips only the ALIAS exact-
        // match branch (an alias the catalogue's own volume title happens to equal, like SAO's
        // "Aincrad", is still a real subtitle) -- the series-name match and the "<name/alias> + bare
        // number" pattern still reject for every candidate, trusted or not, since neither is ever a
        // real subtitle.
        public static bool NamesSeries(string candidate, string seriesName, IEnumerable<string> aliases, bool trustedArtifactCandidate = false)
        {
            var normalised = TitleMatcher.Normalize(candidate);

            if (EditionLabel.IsMatch(normalised))
            {
                return true;
            }

            var seriesKey = TitleMatcher.Normalize(seriesName);

            if (seriesKey.Length > 0 && NamesKey(normalised, seriesKey, exactCounts: true))
            {
                return true;
            }

            foreach (var key in (aliases ?? Enumerable.Empty<string>()).Select(TitleMatcher.Normalize).Where(k => k.Length > 0))
            {
                if (NamesKey(normalised, key, exactCounts: !trustedArtifactCandidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NamesKey(string normalised, string key, bool exactCounts)
        {
            return (exactCounts && normalised == key) ||
                   (normalised.StartsWith(key, StringComparison.Ordinal) && SeriesRemainder.IsMatch(normalised.Substring(key.Length)));
        }

        private static string FromTitle(string title, string seriesName, IEnumerable<string> aliases, double volumeNumber)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return null;
            }

            var match = TitleShape.Match(TrailingEditionTag.Replace(title, string.Empty));

            if (!match.Success ||
                !TitleMatcher.Matches(match.Groups["series"].Value, new[] { seriesName }.Concat(aliases ?? Enumerable.Empty<string>())) ||
                double.Parse(match.Groups["num"].Value, CultureInfo.InvariantCulture) != volumeNumber)
            {
                return null;
            }

            return match.Groups["sub"].Value.Trim();
        }
    }
}
