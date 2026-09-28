using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public class BookSearchCriteria : SearchCriteriaBase
    {
        public string BookTitle { get; set; }
        public int BookYear { get; set; }
        public string BookIsbn { get; set; }
        public string Disambiguation { get; set; }
        public double VolumeNumber { get; set; }

        // Preferred Edition (2026-09-24, spec §3): the series' edition (null/"en" = English, every query
        // below byte-identical). A light-novel AUDIO search of a non-English series is for the English
        // audiobook (D8), so it searches like English, by the anchor name.
        private string EditionLanguage => Author?.Metadata?.Value?.EditionLanguage;

        private bool IsEditionSearch => VolumeNumber > 0 && !EditionLanguages.IsEnglish(EditionLanguage) && MediaType != MediaType.Audio;

        private string SearchName => VolumeNumber > 0 && MediaType == MediaType.Audio && !EditionLanguages.IsEnglish(EditionLanguage) &&
                                     Author.Metadata.Value.AnchorName.IsNotNullOrWhiteSpace()
            ? Author.Metadata.Value.AnchorName
            : Author.Name;

        private string VolumeToken(bool padded)
        {
            return IsEditionSearch && EditionSearchTokens.Has(EditionLanguage)
                ? EditionSearchTokens.Token(EditionLanguage, FormatVolume(padded), padded)
                : "v" + FormatVolume(padded);
        }

        // Light-novel audio (D4, 2026-09-18): the volume's subtitle and Audible title, set by
        // ReleaseSearchService for an Audio search only; null for manga and EPUB.
        public string Subtitle { get; set; }
        public string AudiobookTitle { get; set; }

        // The subtitle tier: "<Series> <N>: <Subtitle>" and, when it says something else, the
        // Audible title — how audiobook releases are actually named. Empty unless Audio.
        public List<string> SubtitleQueries
        {
            get
            {
                var queries = new List<string>();

                if (MediaType != MediaType.Audio || VolumeNumber <= 0)
                {
                    return queries;
                }

                var aliases = Author?.Metadata?.Value?.Aliases ?? new List<string>();

                // Full-title subtitles (2026-09-24): a subtitle that is the volume's whole title
                // ("Rascal Does Not Dream of Petite Devil Kohai") is the query itself -- never
                // "Rascal Does Not Dream 2: Rascal Does Not Dream of ...", which no release is named.
                if (Subtitle.IsNotNullOrWhiteSpace() && !Subtitles.NamesSeries(Subtitle, SearchName, aliases))
                {
                    queries.Add(Subtitles.IsFullTitle(Subtitle, SearchName)
                        ? GetQueryTitle(Subtitle)
                        : GetQueryTitle($"{SearchName} {FormatVolume(false)}: {Subtitle}"));
                }

                // The Audible title is a second query only when it says something else: compared
                // with its trailing "(light novel)" tag cut too (Audible's "Rascal Does Not Dream of a
                // Beach Queen + (light novel)" is the full-title query plus two words, so it can only
                // find a subset of what that query finds -- a wasted query on a 100/day indexer).
                if (AudiobookTitle.IsNotNullOrWhiteSpace())
                {
                    var q = GetQueryTitle(AudiobookTitle);
                    var untagged = GetQueryTitle(Subtitles.WithoutEditionTag(AudiobookTitle));

                    if (q != BookQueryBare && q != BookQuery && !queries.Contains(q) && !queries.Contains(untagged))
                    {
                        queries.Add(q);
                    }
                }

                return queries;
            }
        }

        // Manga (VolumeNumber > 0): the searchable unit is "<Series> v<NN>" (e.g. "Chainsaw Man v13"),
        // which is how Torznab names manga volumes. Author.Name IS the series for a manga fork.
        // Non-manga (VolumeNumber == 0) keeps the original Mangarr "<book title>" query unchanged.
        public string BookQuery => VolumeNumber > 0
            ? $"{GetQueryTitle(SearchName)}+{VolumeToken(true)}"
            : GetQueryTitle(BookTitle.SplitBookTitle(Author.Name).Item1);

        // The fielded Newznab "title=" param matches the indexer's stored book title, which for manga
        // does NOT carry a volume token — so search by series there and let the q= free-text tier carry
        // the volume. Non-manga keeps the original book-title behavior (identical to BookQuery).
        public string FieldedTitleQuery => VolumeNumber > 0
            ? GetQueryTitle(Author.Name)
            : GetQueryTitle(BookTitle.SplitBookTitle(Author.Name).Item1);

        // Recall variant: the unpadded volume form ("v5" rather than "v05"), since not every indexer or
        // release zero-pads. Null when it would duplicate BookQuery (non-manga, multi-digit, or
        // fractional volumes whose unpadded form equals the padded one).
        public string BookQueryAlt
        {
            get
            {
                if (VolumeNumber <= 0)
                {
                    return null;
                }

                var alt = $"{GetQueryTitle(SearchName)}+{VolumeToken(false)}";
                return alt == BookQuery ? null : alt;
            }
        }

        // Recall variant: bare padded number with no v token ("Fire Force 01") — some groups name
        // releases that way. Runs in the fallback tier only; the parser still validates every
        // result to the exact volume before it can be grabbed.
        public string BookQueryBare => VolumeNumber > 0
            ? $"{GetQueryTitle(SearchName)}+{FormatVolume(true)}"
            : null;

        // Recall variants from the series' stored alternate titles (romaji / licensed variants,
        // populated from AniList synonyms): releases named "Kaijuu 8-gou v05" never match the
        // English-title queries. Capped at two so a synonym-heavy entry can't balloon the search.
        public List<string> AliasQueries
        {
            get
            {
                if (VolumeNumber <= 0)
                {
                    return new List<string>();
                }

                var aliases = Author?.Metadata?.Value?.Aliases;

                if (aliases == null)
                {
                    return new List<string>();
                }

                if (IsEditionSearch)
                {
                    // Preferred Edition (2026-09-24, controller ruling S6): slot 1 is the first
                    // edition-language alias, in BuildEditionAltTitles order (own-language rows first,
                    // then AniList English/romaji, then the anchor) -- skipping any alias equal to the
                    // author's own name or to the anchor's query title, since the anchor already owns
                    // slot 2. Slot 2 is the English anchor + vNN, always on (it finds English-named
                    // [FR]-tagged releases). Two slots total: never more than the existing 2-slot cap
                    // per tier (the most an English series with aliases already sends).
                    var slots = new List<string>();
                    var nameQuery = GetQueryTitle(Author.Name);
                    var anchor = Author.Metadata.Value.AnchorName;
                    var anchorQuery = anchor.IsNotNullOrWhiteSpace() ? GetQueryTitle(anchor) : null;

                    var first = aliases.Where(a => a.IsNotNullOrWhiteSpace())
                                       .Where(a => IsSearchableAlias(a, EditionLanguage))
                                       .FirstOrDefault(a => GetQueryTitle(a) != nameQuery && GetQueryTitle(a) != anchorQuery);

                    if (first != null)
                    {
                        slots.Add($"{GetQueryTitle(first)}+{VolumeToken(true)}");
                    }

                    if (anchor.IsNotNullOrWhiteSpace() && IsSearchableAlias(anchor, EditionLanguages.English))
                    {
                        var anchorAlias = $"{anchorQuery}+v{FormatVolume(true)}";

                        if (!slots.Contains(anchorAlias))
                        {
                            slots.Add(anchorAlias);
                        }
                    }

                    return slots.Take(2).ToList();
                }

                return aliases
                    .Where(a => a.IsNotNullOrWhiteSpace())
                    .Where(IsSearchableAlias)
                    .Take(2)
                    .Select(a => $"{GetQueryTitle(a)}+v{FormatVolume(true)}")
                    .ToList();
            }
        }

        // AniList synonyms mix useful romaji/abbreviation aliases with European licensed titles
        // ("Guardianes de la Noche", "In dieser Welt mach ich alles anders") that never match
        // releases on English/romaji-named indexers — they only burn rate-limited API calls in
        // the recall tier. An alias qualifies only when it is pure ASCII and free of tell-tale
        // European function words; romaji is unaffected (Japanese particles "no"/"wa"/"na" are
        // deliberately NOT in the list). "die" also drops a rare English title word — acceptable,
        // a skipped alias costs one recall query at most.
        private static readonly string[] ForeignFunctionWords =
        {
            "de", "del", "der", "die", "das", "dem", "den", "des", "la", "le", "les",
            "los", "las", "el", "und", "ich", "ein", "eine", "dieser", "welt", "mach",
            "alles", "di", "della", "delle", "degli", "oltre", "dos", "uma"
        };

        private static readonly char[] AliasWordSeparators = { ' ', '-', ':', ',', '.' };

        private static bool IsSearchableAlias(string alias)
        {
            return IsSearchableAlias(alias, EditionLanguages.English);
        }

        // Preferred Edition (2026-09-24): an edition's own-language alias is exactly what the English rule
        // drops (accents, "de/la/les", "der/die/das"). For a non-English edition: accents folded (the
        // query folds them anyway -- GetQueryTitle's RemoveAccent), its own function words allowed,
        // everything else as before. English: unchanged.
        internal static bool IsSearchableAlias(string alias, string editionLanguage)
        {
            if (EditionLanguages.IsEnglish(editionLanguage))
            {
                if (alias.Any(c => c > 127))
                {
                    return false;
                }

                return !alias.Split(AliasWordSeparators, System.StringSplitOptions.RemoveEmptyEntries)
                             .Any(w => ForeignFunctionWords.Contains(w, System.StringComparer.OrdinalIgnoreCase));
            }

            var folded = alias.RemoveAccent();

            if (folded.Any(c => c > 127))
            {
                return false;
            }

            var own = EditionSearchTokens.FunctionWords(editionLanguage.Trim());

            return !folded.Split(AliasWordSeparators, System.StringSplitOptions.RemoveEmptyEntries)
                          .Select(w => w.Contains('\'') ? w.Substring(w.IndexOf('\'') + 1) : w)
                          .Any(w => ForeignFunctionWords.Contains(w, System.StringComparer.OrdinalIgnoreCase) &&
                                    !own.Contains(w, System.StringComparer.OrdinalIgnoreCase));
        }

        // Whole volumes use "05" (padded) or "5" (unpadded); fractional side-story volumes render "3.5".
        private string FormatVolume(bool padded)
        {
            var whole = (int)VolumeNumber;
            if (VolumeNumber != whole)
            {
                return VolumeNumber.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            }

            return padded ? whole.ToString("D2") : whole.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            return $"[{Author.Name} - {BookTitle}]";
        }
    }
}
