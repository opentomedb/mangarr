using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // Light novels (2026-09, incident fix B): "Sword Art Online - Progressive - Volume 02" was
    // grabbed and imported as main-line SAO Vol 2. The generic title parser splits it into author
    // "Sword Art Online" + book "Progressive - Volume 02", the name lookup lands on the parent
    // line, and the volume number maps it to Vol 2. Manga has a guard for exactly this shape
    // (ParsingService.GetAuthor: a release whose parsed SERIES extends the searched author is a
    // different series), but it compares against Authors.CleanName, which carries the "~ln"
    // suffix for a light novel, so it never fires for one. This is the same rule at the decision
    // level, for light-novel authors only: the release's series -- everything before its first
    // volume token -- must BE the author's name (or an alias); a series that is the name PLUS
    // more names a different line of the franchise ("Progressive", "Alternative Gun Gale
    // Online"), and the extra segment is the reason. A subtitle AFTER the volume token
    // ("- Volume 17 - Alicization Awakening") is not part of the series and passes; a title with
    // no volume token is not this spec's business. A manga author never enters.
    public class FranchiseLineSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public FranchiseLineSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        private static readonly Regex GenericEditionWords = new Regex(@"\b(?:series|light\s*novels?|novels?|collection|complete|batch|english)\b",
                                                                     RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            if (subject.Author?.Library != LibraryType.LightNovel)
            {
                return Decision.Accept();
            }

            var title = subject.Release?.Title;

            if (title.IsNullOrWhiteSpace() || !MangaVolumeParser.TryParseSeries(title, out var series))
            {
                return Decision.Accept();
            }

            // The series name plus its aliases, cleaned like the search bridge's accepted keys
            // (Parser.ParseBookTitleWithSearchCriteria): exact normalized match, length floor.
            var keys = new List<string> { subject.Author.Name, MangaVolumeParser.StripParentheticals(subject.Author.Name) }
                .Concat(subject.Author.Metadata?.Value?.Aliases ?? Enumerable.Empty<string>())
                .Select(k => k.CleanAuthorName())
                .Where(k => k.Length >= 4)
                .Distinct()
                .OrderByDescending(k => k.Length)
                .ToList();

            // Each reading of the series: the text itself and each half of a "Romaji / English"
            // dual title, with publisher/format parentheticals ("(Light Novel)") stripped first;
            // then, as the search bridge does for publisher-led scene names ("Yen.On.Sword.Art.
            // Online...", "Seven.Seas.Entertainment-Sword.Art.Online..."), the text with a known
            // leading publisher shaved and the text after each hyphen.
            // Generic edition words are not a line of the franchise (2026-09-21): "Rascal Does Not
            // Dream Series v01-16 [Audiobook]" was rejected as the line 'Series'. Each reading is
            // also tried with them removed.
            var readings = MangaVolumeParser.ExpandDualTitles(series)
                .SelectMany(r => new[] { MangaVolumeParser.StripParentheticals(r), r })
                .SelectMany(r => new[] { r, MangaVolumeParser.StripLeadingPublisher(r) }.Concat(AfterEachHyphen(r)))
                .Where(r => r.IsNotNullOrWhiteSpace())
                .SelectMany(r => new[] { r, GenericEditionWords.Replace(r, " ").Trim() })
                .Where(r => r.IsNotNullOrWhiteSpace())
                .Distinct()
                .ToList();

            if (readings.Any(r => keys.Contains(r.CleanAuthorName())))
            {
                return Decision.Accept();
            }

            foreach (var reading in readings)
            {
                foreach (var key in keys)
                {
                    var segment = SegmentAfter(reading, key);

                    if (segment != null)
                    {
                        _logger.Debug("Series '{0}' of '{1}' extends light novel '{2}' with '{3}', rejecting (different line)", series, title, subject.Author.Name, segment);
                        return Decision.Reject("Release names a different line of the franchise: '{0}'", segment);
                    }
                }
            }

            return Decision.Accept();
        }

        // The text after each hyphen of a series reading (Parser.ParseBookTitleWithSearchCriteria
        // tries the same for "Seven Seas Entertainment-Mushoku Tensei...").
        private static IEnumerable<string> AfterEachHyphen(string text)
        {
            var index = text.IndexOf('-');

            while (index >= 0 && index < text.Length - 1)
            {
                yield return text.Substring(index + 1).Trim();
                index = text.IndexOf('-', index + 1);
            }
        }

        // The text of the series after its shortest prefix that cleans to the key, trimmed of the
        // separators between them; null when the series does not start with the key.
        private static string SegmentAfter(string series, string key)
        {
            for (var length = 1; length < series.Length; length++)
            {
                if (series.Substring(0, length).CleanAuthorName() == key)
                {
                    var segment = series.Substring(length).Trim(' ', '-', '–', ',', ':', '/', '|');

                    return segment.IsNullOrWhiteSpace() ? null : segment;
                }
            }

            return null;
        }
    }
}
