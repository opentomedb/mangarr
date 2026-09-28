using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Manga;

namespace NzbDrone.Core.Parser
{
    // Light-novel audio (2026-09-18, D4): an audio release is named the way Audible names the
    // product -- "Sword Art Online 21 - Unital Ring I by Reki Kawahara [ENG / M4B]", "Early Years -
    // The Beginning After the End Book 1 (Unabridged)", "Unital Ring I (Sword Art Online 21)" --
    // not "<Series> v21", so the volume-token bridge finds nothing and the fuzzy author/book match
    // dies as Unknown Author. The bridge names the ONE searched volume whose audiobook title,
    // "<Series> <N>: <Subtitle>" or subtitle the release IS: exact normalised equality against a
    // finite key set per volume -- never containment, never a score. A subtitle that only names
    // the series (or an alias) is never a key; an Audible title that is the series name itself is
    // never a key (a bare "<Series>" release is a batch, not a volume); a release carrying a bare
    // numeric range ("1-2") is a pack and never bridges. Manga never enters: the parser gates on
    // the library and Matches checks it again.
    public static class AudiobookTitleMatcher
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(AudiobookTitleMatcher));

        private static readonly Regex BracketTag = new Regex(@"\[[^\]]*\]", RegexOptions.Compiled);
        private static readonly Regex Parenthetical = new Regex(@"\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex Parentheses = new Regex(@"[()]", RegexOptions.Compiled);
        private static readonly Regex ByAuthor = new Regex(@"\s+by\s+.+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TrailingLabel = new Regex(@"(?:[\s\-–—:,]*\b(?:audiobook|unabridged|light\s*novel))+[\s\-–—:,]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VolumeToken = new Regex(@"\b(?:vol\.?|volume|book)\s*\.?\s*(?=\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // "1-2", "3 – 4": a pack of volumes, never one volume (the vol/volume/book-prefixed ranges
        // are the volume-token bridge's; this is the bare shape it does not see). Judged per reading
        // of the release, on the text that reading keeps (Matches): a date, an ISBN or a technical
        // range inside a tag or a parenthetical is not a pack in the reading that drops it. Any
        // Unicode dash (\p{Pd}: hyphen, en/em dash, horizontal bar) joins the range -- Normalize
        // strips them all, so an unguarded one would fuse "1―2" into a "<Series> 12" key.
        private static readonly Regex BareRange = new Regex(@"\b\d{1,3}\s*\p{Pd}\s*\d{1,3}\b", RegexOptions.Compiled);

        // The release minus what is not the name: bracket tags, parentheticals, "by <author>" to
        // the end, a trailing audiobook / unabridged / light novel label, a vol/volume/book token
        // in front of a number ("Vol. 21" -> "21", the shape the keys carry); then lower-case
        // alphanumerics only, as everywhere else in the fork.
        public static string Normalise(string releaseTitle)
        {
            return Normalise(releaseTitle, false);
        }

        // The one searched volume the release names, or null when none or several do.
        public static Book Match(string releaseTitle, IEnumerable<Book> books, Author author)
        {
            var hits = (books ?? Enumerable.Empty<Book>()).Where(b => Matches(releaseTitle, b, author)).ToList();

            if (hits.Count > 1)
            {
                Logger.Debug("Audiobook title bridge: '{0}' names {1} searched volumes, not bridging", releaseTitle, hits.Count);
                return null;
            }

            return hits.SingleOrDefault();
        }

        public static bool Matches(string releaseTitle, Book book, Author author)
        {
            if (releaseTitle.IsNullOrWhiteSpace() || book == null || author?.Library != LibraryType.LightNovel)
            {
                return false;
            }

            // Both readings of a parenthetical: dropped ("Unital Ring I (Sword Art Online 21)" ->
            // the subtitle) and kept as text ("Sword Art Online 21 (Unital Ring I)" -> the title).
            // A reading is admitted only when the text it reads carries no bare range: "(1-2)" is
            // a pack in the kept reading and names nothing in the dropped one, while a bracketed
            // date or a parenthesised ISBN is gone from the dropped reading and matches through
            // it.
            var forms = new[] { false, true }
                .Select(keep => Stripped(releaseTitle, keep))
                .Where(text => !BareRange.IsMatch(text))
                .Select(NormaliseStripped)
                .Where(f => f.Length > 0)
                .Distinct()
                .ToList();

            if (forms.Count == 0)
            {
                return false;
            }

            var series = author.Name;
            var aliases = author.Metadata?.Value?.Aliases ?? new List<string>();
            var seriesKeys = new[] { series }.Concat(aliases).Select(TitleMatcher.Normalize).Where(k => k.Length > 0).ToList();
            var number = MangaVolumeParser.Format(book.VolumeNumber);
            var keys = new HashSet<string>(StringComparer.Ordinal);

            // The Audible product name, alone and beside the series in either order, with or without
            // the volume number ("Early Years - The Beginning After the End Book 1", "The Beginning
            // After the End: Early Years"). A product name that IS the series name (or an alias), or
            // a bare number, is no key; "<Series>, Vol. N" is one -- the number pins the volume.
            // "<Title> (<Series> <N>)" is the same key as "<Title> <Series> <N>" once normalised.
            var audiobookTitle = book.EditionOf(MediaType.Audio)?.AudiobookTitle;
            var audiobookKey = Normalise(audiobookTitle);

            if (audiobookKey.Any(char.IsLetter) && !seriesKeys.Contains(audiobookKey))
            {
                keys.Add(audiobookKey);
                keys.Add(Normalise($"{audiobookTitle} {series}"));
                keys.Add(Normalise($"{audiobookTitle} {series} {number}"));
                keys.Add(Normalise($"{series} {audiobookTitle}"));
                keys.Add(Normalise($"{series} {number} {audiobookTitle}"));
            }

            // The subtitle, alone and as "<Series> <N>: <Subtitle>" (the colon-less spelling is the
            // same key once normalised) -- never when it only names the series or an alias.
            if (book.Subtitle.IsNotNullOrWhiteSpace() && !Subtitles.NamesSeries(book.Subtitle, series, aliases))
            {
                keys.Add(Normalise($"{series} {number}: {book.Subtitle}"));
                keys.Add(Normalise(book.Subtitle));
            }

            keys.Remove(string.Empty);

            return forms.Any(keys.Contains);
        }

        private static string Normalise(string title, bool keepParentheticalText)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            return NormaliseStripped(Stripped(title, keepParentheticalText));
        }

        // The release with its bracket tags out and its parentheticals dropped, or unwrapped with
        // their text kept: the first step of Normalise, and the text each reading's bare-range
        // guard is judged on.
        private static string Stripped(string title, bool keepParentheticalText)
        {
            var text = BracketTag.Replace(title, " ");

            return keepParentheticalText ? Parentheses.Replace(text, " ") : Parenthetical.Replace(text, " ");
        }

        private static string NormaliseStripped(string text)
        {
            text = ByAuthor.Replace(text.Trim(), string.Empty);
            text = TrailingLabel.Replace(text, string.Empty);
            text = VolumeToken.Replace(text, string.Empty);

            return TitleMatcher.Normalize(text);
        }
    }
}
