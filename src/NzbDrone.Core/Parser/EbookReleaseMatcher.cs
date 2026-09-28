using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Manga;

namespace NzbDrone.Core.Parser
{
    // Light-novel ebook (2026-09-20, T2): usenet names an ebook after its WRITER -- "Reki.Kawahara.
    // -.Sword.Art.Online-.Unital.Ring.I.21.(epub)", "TurtleMe - [The Beginning After the End 02] -
    // New Heights (epub)", "Natsume.Akatsuki.-.[Konosuba-...World.07].-.110-Million.Bride.(epub)".
    // The series-name attribution refuses those (the release's "author" is the writer, not the
    // series) and neither volume bridge fires (no v/Vol token), so they died as Unknown Author.
    //
    // This names the ONE searched volume the release IS: the writer is stripped from the front, a
    // bracket group that names the series is unwrapped (other tags are dropped), and what is left
    // must EQUAL one of that volume's finite keys -- "<Series> <N>" with the subtitle, the Audible
    // product name or the Audible subtitle in each order, or one of those names alone. Exact
    // normalised equality, never containment, never a score; a subtitle that only names the series
    // or is the numbered label "<Series>, No. 2" is no key. The one widening is rule 5's
    // writer-anchored prefix: when the writer WAS stripped and what follows starts with
    // "<Series> <N>" for exactly one searched volume and carries no second number, that volume is
    // named even though the rest is a subtitle we do not have (SAO 25-28 have none stored).
    // A release carrying a range is a pack and never bridges; manga never enters.
    public static class EbookReleaseMatcher
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(EbookReleaseMatcher));

        // Same floor as the parser's accepted series keys: a 3-character name is too short to
        // identify a series on its own.
        private const int SeriesKeyFloor = 4;

        private static readonly Regex BracketGroup = new Regex(@"\[[^\]]*\]", RegexOptions.Compiled);
        private static readonly Regex Parenthetical = new Regex(@"\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex Parentheses = new Regex(@"[()]", RegexOptions.Compiled);
        private static readonly Regex ByAuthor = new Regex(@"\s+by\s+.+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TrailingLabel = new Regex(@"(?:[\s\-–—:,]*\b(?:audiobook|unabridged|light\s*novel))+[\s\-–—:,]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VolumeToken = new Regex(@"\b(?:vol\.?|volume|book)\s*\.?\s*(?=\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Scene spacing: "_" and "." are separators, except the decimal point of a fractional
        // side-story volume ("Vol.3.5"), which must survive as one number.
        private static readonly Regex DottedSpacing = new Regex(@"(?<!\d)\.|\.(?!\d)", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        // "02" is volume 2: leading zeros are dropped from every number, on the release and on the
        // keys, before the text is joined (once joined, "02 110" and "2 110" would differ).
        private static readonly Regex LeadingZeros = new Regex(@"(?<!\d)0+(\d)", RegexOptions.Compiled);

        // "v01-06", "21-22", "1 - 2": a pack of volumes, the batch bridge's business. Judged on the
        // release BEFORE bracket groups are unwrapped, so an unwrapped "[... World 07] - 110-Million
        // Bride" does not read as the range "07-110" it never was.
        private static readonly Regex BareRange = new Regex(@"\b(?:v|vol\.?)?\s*\d{1,3}\s*\p{Pd}\s*(?:v|vol\.?)?\s*\d{1,3}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // What is left of a normalised subtitle once the series name is off it: "no2" of "<Series>,
        // No. 2", "book2", or the bare "2". Such a subtitle is a numbered label, not a title.
        private static readonly Regex NumberedLabel = new Regex(@"^(?:no|nos|vol|volume|book|part|pt)?\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // A fractional number ("Vol. 3.5", "Series 2.5"): normalising drops the dot, so "2.5" and
        // "25" are one string and a side story would claim volume 25. Fractional volumes are not
        // this bridge's -- they keep the volume-token path, which reads the number properly -- so
        // neither a fractional release nor a fractional searched volume ever bridges here.
        // The right-hand run is 1-2 digits (final review M2): a 4-digit run is a year
        // ("...21.2024.Retail"), never a fraction. The check runs on each reading of the release
        // once its bracket tags are off it, so "[8.5 MB]" is no longer a refusal.
        private static readonly Regex FractionalNumber = new Regex(@"\d\.\d{1,2}(?!\d)", RegexOptions.Compiled);

        // The separator a writer credit is followed by: "Reki Kawahara - ...", "Kawahara, Reki: ...".
        private static readonly char[] WriterSeparators = { '-', '–', '—', ':', ',' };

        // The one searched volume the release names, or null when none or several do.
        public static Book Match(string releaseTitle, IEnumerable<Book> books, Author author)
        {
            if (releaseTitle.IsNullOrWhiteSpace() || author?.Library != LibraryType.LightNovel)
            {
                return null;
            }

            var searched = (books ?? Enumerable.Empty<Book>()).Where(b => b != null).ToList();
            var series = author.Name;
            var aliases = author.Metadata?.Value?.Aliases ?? new List<string>();

            // The series' own name and its curated aliases, each a name a release may use: the
            // keys are built from all of them, and the prefix rule anchors on any of them.
            var seriesNames = new[] { series }.Concat(aliases)
                .Where(n => Normalise(n).Length >= SeriesKeyFloor)
                .ToList();
            var seriesKeys = seriesNames.Select(Normalise).Distinct().ToList();

            if (searched.Count == 0 || seriesKeys.Count == 0)
            {
                return null;
            }

            var spaced = Spaced(releaseTitle);

            if (BareRange.IsMatch(spaced))
            {
                Logger.Debug("Ebook release bridge: '{0}' carries a range, not one volume", releaseTitle);
                return null;
            }

            var stripped = StripWriter(spaced, author);
            var writerRemoved = !string.Equals(stripped, spaced, StringComparison.Ordinal);
            var unwrapped = UnwrapSeriesBrackets(stripped, seriesKeys);

            // Both readings of a parenthetical: dropped ("Sword Art Online 25 (retail) (epub)" ->
            // the volume) and kept as text ("Unital Ring I (Sword Art Online 21)" -> the whole name).
            var readings = new[] { false, true }
                .Select(keep => keep ? Parentheses.Replace(unwrapped, " ") : Parenthetical.Replace(unwrapped, " "))
                .ToList();

            // A fractional number refuses the reading that KEEPS it (final review M2): "2.5" and
            // "25" normalise alike, so a side story must never key its integer twin. Judged here
            // rather than on the raw release, where a size tag "[8.5 MB]" or a "(v1.1)" repack
            // marker -- neither of them the name -- refused an ordinary integer volume outright.
            var fractional = readings.RemoveAll(reading => FractionalNumber.IsMatch(reading));

            if (fractional > 0)
            {
                Logger.Debug("Ebook release bridge: '{0}' carries a fractional number, not bridging on {1} of its readings", releaseTitle, fractional);
            }

            var forms = readings
                .Select(Normalise)
                .Where(f => f.Length > 0)
                .Distinct()
                .ToList();

            if (forms.Count == 0)
            {
                return null;
            }

            var hits = searched.Where(b => Keys(b, seriesNames, series, aliases, seriesKeys).Overlaps(forms)).ToList();

            if (hits.Count == 0 && writerRemoved)
            {
                hits = searched.Where(b => NamedByPrefix(forms, b, seriesKeys)).ToList();
            }

            if (hits.Count > 1)
            {
                Logger.Debug("Ebook release bridge: '{0}' names {1} searched volumes, not bridging", releaseTitle, hits.Count);
                return null;
            }

            return hits.SingleOrDefault();
        }

        // The release without its leading writer credit: with the dots-as-spaces title, when it
        // starts with the writer ("Reki Kawahara", or the "Kawahara, Reki" spelling calibre uses)
        // followed by a separator, what follows it; otherwise the title unchanged. The separator is
        // what makes the boundary safe -- a writer name that merely opens the title ("Reki Kawahara
        // Sword Art Online 21") is not a credit and is left alone.
        public static string StripWriter(string title, Author author)
        {
            var writer = author?.Metadata?.Value?.Writer;

            if (title.IsNullOrWhiteSpace() || writer.IsNullOrWhiteSpace())
            {
                return title;
            }

            var forms = new[] { writer, writer.ToLastFirst() }
                .Select(TitleMatcher.Normalize)
                .Where(f => f.Length > 0)
                .ToHashSet();

            if (forms.Count == 0)
            {
                return title;
            }

            var longest = forms.Max(f => f.Length);

            // Folded like the writer forms (TitleFold, 2026-09-24), so "Shōgo Kinugasa" in the
            // release meets the stored "Shogo Kinugasa" and vice versa. An unstripped title is still
            // returned as given below, so the caller's writer-removed test compares like with like.
            var text = TitleFold.Fold(Spaced(title));
            var seen = new StringBuilder();

            for (var i = 0; i < text.Length; i++)
            {
                if (!char.IsLetterOrDigit(text[i]))
                {
                    continue;
                }

                seen.Append(char.ToLowerInvariant(text[i]));

                if (seen.Length > longest)
                {
                    break;
                }

                // Only at the end of a word, so a writer form can never be half of a longer one.
                if (i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1]))
                {
                    continue;
                }

                if (!forms.Contains(seen.ToString()))
                {
                    continue;
                }

                var remainder = text.Substring(i + 1).TrimStart();

                if (remainder.Length > 0 && WriterSeparators.Contains(remainder[0]))
                {
                    return remainder.TrimStart(WriterSeparators).TrimStart();
                }
            }

            return title;
        }

        // A "[...]" group whose text names the series ("[The Beginning After the End 02]") IS the
        // title and is unwrapped to its text; every other group is a scene/publisher tag and is
        // dropped, as the audiobook matcher drops them.
        public static string UnwrapSeriesBrackets(string title, IEnumerable<string> seriesKeys)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return title;
            }

            var keys = (seriesKeys ?? Enumerable.Empty<string>()).Where(k => k.IsNotNullOrWhiteSpace()).ToList();

            var unwrapped = BracketGroup.Replace(title, match =>
            {
                var text = match.Value.Substring(1, match.Value.Length - 2);
                var normalised = Normalise(text);

                return keys.Any(k => normalised.StartsWith(k, StringComparison.Ordinal)) ? " " + text + " " : " ";
            });

            return Whitespace.Replace(unwrapped, " ").Trim();
        }

        // Every spelling of this volume the keys cover. The number pins the volume, so "<Series>
        // <N>" alone is a key; a name (the stored subtitle, the Audible title, the Audible subtitle)
        // is a key beside the series in either order, and alone. Each of the series' names -- its
        // own and its aliases -- spells the same key set, so a release naming the alias matches
        // exactly rather than only through the prefix rule.
        private static HashSet<string> Keys(Book book, List<string> seriesNames, string series, List<string> aliases, List<string> seriesKeys)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var number = MangaVolumeParser.Format(book.VolumeNumber);

            if (number.Contains('.'))
            {
                return keys;
            }

            var audio = book.EditionOf(MediaType.Audio);
            var names = new[] { book.Subtitle, audio?.AudiobookTitle, audio?.AudiobookSubtitle }
                .Where(n => IsName(n, series, aliases, seriesKeys))
                .ToList();

            foreach (var seriesName in seriesNames)
            {
                keys.Add(Key(seriesName, number));

                foreach (var name in names)
                {
                    keys.Add(Key(seriesName, number, name));
                    keys.Add(Key(name, seriesName, number));
                    keys.Add(Key(seriesName, name, number));
                }
            }

            foreach (var name in names)
            {
                keys.Add(Key(name));
            }

            keys.Remove(string.Empty);

            return keys;
        }

        // Rule 5: the writer was stripped and what follows starts with "<Series> <N>" -- the volume
        // is pinned by the number, so the rest may be a subtitle we do not have. The character after
        // the number must not be a digit (else volume 2 would claim "<Series> 25") and the remainder
        // must carry no second number (else "<Series> 2 - Side Story 3" would claim volume 2).
        private static bool NamedByPrefix(List<string> forms, Book book, List<string> seriesKeys)
        {
            var number = MangaVolumeParser.Format(book.VolumeNumber);

            if (number.Contains('.'))
            {
                return false;
            }

            number = LeadingZeros.Replace(number, "$1");

            foreach (var form in forms)
            {
                foreach (var prefix in seriesKeys.Select(k => k + TitleMatcher.Normalize(number)))
                {
                    if (!form.StartsWith(prefix, StringComparison.Ordinal) ||
                        (form.Length > prefix.Length && char.IsDigit(form[prefix.Length])))
                    {
                        continue;
                    }

                    if (!form.Substring(prefix.Length).Any(char.IsDigit))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // A stored name is a key only when it is a title: not the series name itself (or an alias,
        // or either with a volume number after it), not the numbered label "<Series>, No. 2" /
        // "Book 2" that the LN metadata leaves behind, and not a bare number.
        private static bool IsName(string name, string series, List<string> aliases, List<string> seriesKeys)
        {
            if (name.IsNullOrWhiteSpace() || Subtitles.NamesSeries(name, series, aliases))
            {
                return false;
            }

            var normalised = Normalise(name);

            if (!normalised.Any(char.IsLetter) || NumberedLabel.IsMatch(normalised))
            {
                return false;
            }

            return !seriesKeys.Any(k => normalised.StartsWith(k, StringComparison.Ordinal) &&
                                        NumberedLabel.IsMatch(normalised.Substring(k.Length)));
        }

        private static string Key(params string[] parts)
        {
            return Normalise(string.Join(" ", parts));
        }

        // Lower-case alphanumerics, as everywhere else in the fork, once the text that is not the
        // name is off it: a "by <author>" credit, a trailing audiobook / light-novel label,
        // a vol/volume/book token in front of a number ("Vol. 21" -> "21", the shape the keys
        // carry), and the leading zeros of every number. Release readings and keys go through the
        // same pipeline, so both sides are shaped identically.
        private static string Normalise(string text)
        {
            if (text.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            text = ByAuthor.Replace(text.Trim(), string.Empty);
            text = TrailingLabel.Replace(text, string.Empty);
            text = VolumeToken.Replace(text, string.Empty);
            text = LeadingZeros.Replace(text, "$1");

            return TitleMatcher.Normalize(text);
        }

        private static string Spaced(string title)
        {
            var text = DottedSpacing.Replace(title.Replace('_', ' '), " ");

            return Whitespace.Replace(text, " ").Trim();
        }
    }
}
