using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Parser
{
    // Phase 3: search-driven manga volume parsing.
    // Ported from the proven Python Mangarr blueprint (match.py), validated against
    // real private-tracker release titles. Detects single volumes and multi-volume packs so the
    // discovery + planner layers can map one release to one-or-many volumes.
    // Single volumes may be fractional ("Vol 3.5" side-story volumes); pack ranges are
    // whole numbers. Both "Vol/Volume/v" and the comic-style "Book N" naming are recognised.
    public static class MangaVolumeParser
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(MangaVolumeParser));

        // "Volumes 1 to 13", "Books 1 to 13"
        private static readonly Regex VolRangeToRegex = new Regex(
            @"\b(?:volumes?|books?)\s+(?<start>\d{1,3})\s+to\s+(?<end>\d{1,3})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "Vol. 1-13", "v01-v13", "Volumes 1-13", "Books 1-13" (any Unicode dash: en/em dash, horizontal bar)
        private static readonly Regex VolRangeDashRegex = new Regex(
            @"\b(?:v|vol\.?|volume|book)s?\.?\s*(?<start>\d{1,3})\s*\p{Pd}\s*(?:(?:v|vol\.?)\s*)?(?<end>\d{1,3})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "v01", "Vol. 5", "Volume 12", "Vol. 3.5" (fractional side-story volumes), "Book 4".
        // "Book N" is the common comic/manhwa file naming (e.g. Yen Press "Solo Leveling Book 4").
        // The \b before the token keeps it from matching mid-word ("Cookbook 2", "Booklet 5").
        // Fraction capped at 2 digits: scene eBook names are fully dotted ("Vol.05.2017.Hybrid"),
        // and an unbounded fraction swallowed the year as "volume 5.2017".
        private static readonly Regex VolSingleRegex = new Regex(
            @"\b(?:v|vol\.?|volume|book)\s*\.?\s*(?<num>\d{1,3}(?:\.\d{1,2})?)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Japanese: 第N巻 (volume N). Run before punctuation normalization strips 巻.
        private static readonly Regex VolCjkRegex = new Regex(
            @"第\s*(?<num>\d{1,3}(?:\.\d+)?)\s*巻",
            RegexOptions.Compiled);

        // Publisher/format parentheticals — "(Light Novel)", "(Digital)", "(Omnibus)", "(comic)".
        // Stripped only when comparing a release's parsed series to a library author, never from
        // the stored series identity.
        private static readonly Regex ParentheticalRegex = new Regex(@"\s*\([^)]*\)", RegexOptions.Compiled);

        // Leading square-bracket groups: "[Manga] Tokyo Ghoul v01", "[Group] Series v05". Usually
        // scene/scanlation tags to strip — but some series are literally named in brackets
        // ("[Oshi No Ko] v13", "【OSHI NO KO】 v13"), so the contents are also kept as series
        // candidates (still subject to the exact normalized match against the searched series).
        private static readonly Regex LeadingBracketsRegex = new Regex(@"^\s*(?:\[(?<tag>[^\]]*)\]\s*)+", RegexOptions.Compiled);

        // Dual-title separators: Nyaa-style "Romaji / English" and "Romaji | English" headers
        // ("Tensei Shitara Slime Datta Ken / That Time I Got Reincarnated as a Slime v01-28").
        private static readonly Regex DualTitleSeparatorRegex = new Regex(@"\s+[/|]\s+", RegexOptions.Compiled);

        // Explicit chapter tokens in a FILE name: "Chapter 12", "Ch.12", "ch 012", "c001",
        // "#12", 第N話. Used only by IsChapterOnlyFile — never by volume parsing.
        private static readonly Regex ChapterTokenRegex = new Regex(
            @"(?:\b(?:c(?:h(?:ap(?:ter)?)?)?)[ ._-]*(?<num>\d{1,4}(?:\.\d{1,2})?)\b)|(?:#(?<num>\d{1,4})\b)|(?:第\s*\d{1,4}\s*話)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Volume numbers are culture-invariant: "3.5" is always three-and-a-half, never "thirty-five".
        private static double ParseNum(string value) => double.Parse(value, CultureInfo.InvariantCulture);

        // Render a volume number back to its canonical string: whole numbers drop the
        // fractional part ("3"), half/side-story volumes keep it ("3.5"). Used for stable
        // ForeignBookIds and display so 3.0 and 3 are never two different identities.
        public static string Format(double volume) => volume.ToString("0.####", CultureInfo.InvariantCulture);

        // Populates VolumeNumber OR VolumeStart/VolumeEnd on the parsed info.
        // Range patterns take precedence over single (a pack title also contains a bare "v01").
        public static void ParseVolume(string releaseTitle, ParsedBookInfo info)
        {
            if (releaseTitle == null || info == null)
            {
                return;
            }

            var rangeTo = VolRangeToRegex.Match(releaseTitle);
            if (rangeTo.Success)
            {
                info.VolumeStart = ParseNum(rangeTo.Groups["start"].Value);
                info.VolumeEnd = ParseNum(rangeTo.Groups["end"].Value);
                Logger.Trace("Manga volume range (to) {0}-{1} from '{2}'", info.VolumeStart, info.VolumeEnd, releaseTitle);
                return;
            }

            var rangeDash = VolRangeDashRegex.Match(releaseTitle);
            if (rangeDash.Success)
            {
                var start = ParseNum(rangeDash.Groups["start"].Value);
                var end = ParseNum(rangeDash.Groups["end"].Value);

                // Guard: a dash match where start>=end isn't a real range (e.g. "v03-04" scanlation chapter noise).
                if (end > start)
                {
                    info.VolumeStart = start;
                    info.VolumeEnd = end;
                    Logger.Trace("Manga volume range (dash) {0}-{1} from '{2}'", start, end, releaseTitle);
                    return;
                }
            }

            var cjk = VolCjkRegex.Match(releaseTitle);
            if (cjk.Success)
            {
                info.VolumeNumber = ParseNum(cjk.Groups["num"].Value);
                Logger.Trace("Manga volume (cjk) {0} from '{1}'", info.VolumeNumber, releaseTitle);
                return;
            }

            var single = VolSingleRegex.Match(releaseTitle);
            if (single.Success)
            {
                info.VolumeNumber = ParseNum(single.Groups["num"].Value);
                Logger.Trace("Manga volume (single) {0} from '{1}'", info.VolumeNumber, releaseTitle);
            }
        }

        // Parse a single volume number from arbitrary text (e.g. a book title like
        // "Chainsaw Man Vol. 3"). Returns 0 if none. Ranges are intentionally ignored
        // here — a library file or a book tag represents a single volume.
        public static double ParseSingleVolume(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var cjk = VolCjkRegex.Match(text);
            if (cjk.Success)
            {
                return ParseNum(cjk.Groups["num"].Value);
            }

            var single = VolSingleRegex.Match(text);
            if (single.Success)
            {
                return ParseNum(single.Groups["num"].Value);
            }

            return 0;
        }

        // Preferred Edition (2026-09-24): the same parse for a series of another edition -- its tokens are
        // read as "Vol." first (EditionVolumeTokens). Null / English = exactly the one-argument parse.
        public static double ParseSingleVolume(string text, string editionLanguage)
        {
            return ParseSingleVolume(EditionVolumeTokens.Rewrite(text, editionLanguage));
        }

        // Parse a library file name like "Chainsaw Man - Vol 001" (or "Chainsaw_Man_v01")
        // into its series name and single volume number. Returns false when no volume
        // token is present, so callers can leave non-manga files untouched.
        public static bool TryParseSeriesVolume(string fileName, out string series, out double volume)
        {
            series = null;
            volume = 0;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            // Normalize scanlation separators so "Series_v01"/"Series.v01" parse like "Series v01".
            // Underscores are always separators; dots are too EXCEPT a decimal point inside a volume
            // number (3.5) — that must survive so a side-story volume isn't truncated to the whole one.
            var normalized = fileName.Replace('_', ' ');
            normalized = Regex.Replace(normalized, @"(?<!\d)\.(?!\d)", " ");

            var cjk = VolCjkRegex.Match(normalized);
            var single = VolSingleRegex.Match(normalized);
            var match = cjk.Success ? cjk : (single.Success ? single : null);

            if (match == null)
            {
                return false;
            }

            volume = ParseNum(match.Groups["num"].Value);
            series = CleanSeries(normalized.Substring(0, match.Index));

            return !string.IsNullOrWhiteSpace(series);
        }

        // Preferred Edition (2026-09-24): a library file of a series of another edition ("… Tome 5", or
        // "… T05" right after a name isAcceptedSeries takes). Null / English = exactly the three-argument parse.
        public static bool TryParseSeriesVolume(string fileName, out string series, out double volume, string editionLanguage, System.Func<string, bool> isAcceptedSeries = null)
        {
            return TryParseSeriesVolume(EditionVolumeTokens.Rewrite(fileName, editionLanguage, isAcceptedSeries), out series, out volume);
        }

        // Parse just the series name from a release title that may be a single volume OR a
        // multi-volume pack ("Series Volumes 1 to 13", "Series v01-v13"). Unlike
        // TryParseSeriesVolume (single-volume only, used by the importer), this also recognises
        // the range tokens so a whole-series pack release gets attributed to its series; the range
        // itself is parsed separately by ParseVolume into VolumeStart/VolumeEnd and fanned out to
        // every volume by ParsingService.GetBooks.
        public static bool TryParseSeries(string fileName, out string series)
        {
            series = null;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            var normalized = fileName.Replace('_', ' ');
            normalized = Regex.Replace(normalized, @"(?<!\d)\.(?!\d)", " ");

            // Strip leading scene/format tags like "[Manga]" or a scanlation "[Group]" so they
            // don't poison the series name — "[Manga] Tokyo Ghoul v01" must read as "Tokyo Ghoul",
            // not "Manga Tokyo Ghoul" (which then fails to match the searched author).
            normalized = LeadingBracketsRegex.Replace(normalized, "");

            // The series name is everything before the FIRST volume token, whichever kind
            // (range or single) appears earliest in the title.
            Match earliest = null;
            foreach (var candidate in new[]
                     {
                         VolRangeToRegex.Match(normalized),
                         VolRangeDashRegex.Match(normalized),
                         VolCjkRegex.Match(normalized),
                         VolSingleRegex.Match(normalized)
                     })
            {
                if (candidate.Success && (earliest == null || candidate.Index < earliest.Index))
                {
                    earliest = candidate;
                }
            }

            if (earliest == null)
            {
                return false;
            }

            series = CleanSeries(normalized.Substring(0, earliest.Index));

            return !string.IsNullOrWhiteSpace(series);
        }

        // Candidate series names for a volume-tokened release, for matching against a searched
        // series. Standard scene naming is "Series vN" so the series is BEFORE the token, but some
        // releases lead with the token ("Vol 7 Dan da Dan", "Book 5 Tensura by Fuse") — there the
        // series is AFTER it. Return both so the matcher (which still requires an EXACT normalized
        // match against the series' name or a curated alias) can try each. The trailing "by Author"
        // attribution and any trailing (year)/[group] tags are stripped from the after-token form.
        // Each reading is expanded through ExpandDualTitles, and the contents of any leading
        // bracket groups are tried last ("[Oshi No Ko] v13": the bracketed prefix IS the series).
        public static List<string> GetSeriesCandidates(string fileName)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return result;
            }

            var normalized = fileName.Replace('_', ' ');
            normalized = Regex.Replace(normalized, @"(?<!\d)\.(?!\d)", " ");
            var leadingTags = LeadingBracketContents(normalized);
            normalized = LeadingBracketsRegex.Replace(normalized, "");

            var matches = new[] { VolRangeToRegex, VolRangeDashRegex, VolCjkRegex, VolSingleRegex }
                .SelectMany(r => r.Matches(normalized).Cast<Match>())
                .Where(m => m.Success)
                .OrderBy(m => m.Index)
                .ToList();

            if (matches.Count == 0)
            {
                return result;
            }

            void Add(string candidate)
            {
                foreach (var variant in ExpandDualTitles(candidate))
                {
                    if (!result.Contains(variant))
                    {
                        result.Add(variant);
                    }
                }
            }

            Add(CleanSeries(normalized.Substring(0, matches[0].Index)));

            var last = matches[matches.Count - 1];
            var afterStart = last.Index + last.Length;
            if (afterStart < normalized.Length)
            {
                Add(CleanSeries(StripTrailingAttribution(normalized.Substring(afterStart))));
            }

            foreach (var tag in leadingTags)
            {
                Add(tag);
            }

            return result;
        }

        // A release's series text may carry two names — the Nyaa "Romaji / English" or
        // "Romaji | English" header ("Kekkon suru tte, Hontou desu ka | 365 Days to the Wedding").
        // Return the text itself plus each half so the matcher can try every reading; each is
        // still an EXACT normalized match against the searched series, so a half can only match
        // the series it names.
        public static List<string> ExpandDualTitles(string series)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(series))
            {
                return result;
            }

            result.Add(series);

            if (!DualTitleSeparatorRegex.IsMatch(series))
            {
                return result;
            }

            foreach (var part in DualTitleSeparatorRegex.Split(series))
            {
                var cleaned = CleanSeries(part);
                if (!string.IsNullOrWhiteSpace(cleaned) && !result.Contains(cleaned))
                {
                    result.Add(cleaned);
                }
            }

            return result;
        }

        // Contents of the leading bracket groups of a normalized title, in order.
        private static List<string> LeadingBracketContents(string normalized)
        {
            var result = new List<string>();
            var match = LeadingBracketsRegex.Match(normalized);

            if (!match.Success)
            {
                return result;
            }

            foreach (Capture capture in match.Groups["tag"].Captures)
            {
                var cleaned = CleanSeries(capture.Value);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    result.Add(cleaned);
                }
            }

            return result;
        }

        // Remove a trailing "by <author>" credit and any trailing (year)/[group] tag groups, so the
        // after-token text "Tensura by Fuse [ENG]" reduces to the bare series "Tensura".
        private static string StripTrailingAttribution(string text)
        {
            text = Regex.Replace(text, @"\s+by\s+.+$", string.Empty, RegexOptions.IgnoreCase);

            string previous = null;
            while (previous != text)
            {
                previous = text;
                text = Regex.Replace(text, @"[\[(][^\[\]()]*[\])]\s*$", string.Empty).Trim();
            }

            return text;
        }

        // True when ANY volume token is present — single, fractional, CJK, or a pack range.
        // The quality parser uses this to default manga releases (which rarely carry a CBZ
        // keyword) to CBZ; covers whole-series packs that ParseSingleVolume alone would miss.
        public static bool HasVolumeToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return VolRangeToRegex.IsMatch(text)
                || VolRangeDashRegex.IsMatch(text)
                || VolCjkRegex.IsMatch(text)
                || VolSingleRegex.IsMatch(text);
        }

        // True when the file name carries an explicit chapter token and NO volume token.
        // Volume wins: "ReZERO...Chapter.4...v07.cbz" is a real volume whose series title
        // contains "Chapter" and must return false. Bare-number files ("001.cbz") are
        // deliberately not chapter-patterned — ambiguous, they could be volumes.
        public static bool IsChapterOnlyFile(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var normalized = name.Replace('_', ' ');
            normalized = Regex.Replace(normalized, @"(?<!\d)\.(?!\d)", " ");

            if (VolCjkRegex.IsMatch(normalized) || VolSingleRegex.IsMatch(normalized))
            {
                return false;
            }

            return ChapterTokenRegex.IsMatch(normalized);
        }

        private static string CleanSeries(string prefix)
        {
            if (prefix == null)
            {
                return null;
            }

            var cleaned = Regex.Replace(prefix, @"\s+", " ").Trim();
            return cleaned.Trim(' ', '-', '–', ',', ':');
        }

        // Strip publisher/format parentheticals from a series name so a release whose series
        // reads "Mushoku Tensei: Jobless Reincarnation (Light Novel)" matches the library author
        // "Mushoku Tensei: Jobless Reincarnation". Used for author attribution only.
        public static string StripParentheticals(string text)
        {
            return text == null ? null : CleanSeries(ParentheticalRegex.Replace(text, " "));
        }

        // Scene eBook names may prefix the series with a DOT-joined publisher rather than a
        // hyphen-joined one ("VIZ.Media.Jujutsu.Kaisen.Vol.30..."), so after dot->space
        // normalization the parsed series reads "VIZ Media Jujutsu Kaisen" and the exact match
        // fails. Only a KNOWN publisher may be shaved from the front: blindly dropping leading
        // words would let a prefix-extended sibling series ("Shin <Series>") match its base.
        // Longer names sort before their prefixes so "Seven Seas Entertainment" wins over
        // "Seven Seas".
        private static readonly string[] LeadingPublishers =
        {
            "VIZ Media LLC",
            "VIZ Media",
            "Seven Seas Entertainment",
            "Seven Seas",
            "Kodansha Comics",
            "Kodansha USA",
            "Kodansha",
            "Yen Press",
            "Yen On",
            "Dark Horse Comics",
            "Dark Horse Manga",
            "Dark Horse",
            "Square Enix Manga",
            "Square Enix",
            "J-Novel Club",
            "Vertical Comics",
            "One Peace Books",
            "Ghost Ship",
            "Airship",
            "Ize Press",
            "Udon Entertainment",
            "Tokyopop",
            "Shueisha",
            "Shogakukan",
            "Kadokawa",
        };

        // The cleaned series-name readings of a TOKENLESS release title ("That Time I Got
        // Reincarnated as a Slime [Yen Press] [Stick]" -> "That Time I Got Reincarnated as a
        // Slime"). Whole-series batches are named this way — the series IS the whole title, with
        // group/format tags around it. Dual titles yield both halves; the contents of leading
        // bracket groups come last. Empty for titles that carry a volume token (those go
        // through GetSeriesCandidates instead).
        public static List<string> GetBatchSeriesCandidates(string fileName)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(fileName) || HasVolumeToken(fileName))
            {
                return result;
            }

            var normalized = fileName.Replace('_', ' ');
            normalized = Regex.Replace(normalized, @"(?<!\d)\.(?!\d)", " ");
            var leadingTags = LeadingBracketContents(normalized);
            normalized = LeadingBracketsRegex.Replace(normalized, "");

            var readings = ExpandDualTitles(CleanSeries(StripTrailingAttribution(normalized)));
            foreach (var tag in leadingTags)
            {
                readings.AddRange(ExpandDualTitles(tag));
            }

            foreach (var reading in readings)
            {
                if (!result.Contains(reading))
                {
                    result.Add(reading);
                }
            }

            return result;
        }

        public static string GetBatchSeriesCandidate(string fileName)
        {
            return GetBatchSeriesCandidates(fileName).FirstOrDefault();
        }

        // Returns the candidate with a known leading publisher removed, or null when no known
        // publisher prefix is present.
        public static string StripLeadingPublisher(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            foreach (var publisher in LeadingPublishers)
            {
                if (candidate.Length > publisher.Length + 1 &&
                    candidate.StartsWith(publisher, System.StringComparison.OrdinalIgnoreCase) &&
                    candidate[publisher.Length] == ' ')
                {
                    return candidate.Substring(publisher.Length + 1).Trim();
                }
            }

            return null;
        }
    }
}
