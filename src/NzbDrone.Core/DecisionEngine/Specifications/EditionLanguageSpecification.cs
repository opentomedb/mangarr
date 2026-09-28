using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // Preferred Edition (2026-09-24, D2): a series collected in a non-English edition takes only releases
    // that show that language -- a title tag (ReleaseLanguageParser) or the indexer's Newznab/Torznab
    // `language` attribute (ReleaseInfo.Languages). No marker at all is rejected (D2: unmarked = English by
    // convention), with the reason Interactive Search shows. A light novel's audiobook is the English one
    // (D8), so an Audio release passes a non-English edition. An English edition (2026-09-26, plan A2, the maintainer:
    // "go") rejects a release that shows another language and no English -- an untagged release is English
    // by convention and passes. The series' own names are cut from the title first, so a series called
    // "Raw Hero" is not Japanese evidence against itself.
    public class EditionLanguageSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public EditionLanguageSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            var edition = subject.Author?.Metadata?.Value?.EditionLanguage;

            if (EditionLanguages.IsEnglish(edition))
            {
                return English(subject);
            }

            if (subject.MediaType == MediaType.Audio || searchCriteria?.MediaType == MediaType.Audio)
            {
                return Decision.Accept();
            }

            var want = edition.Trim().Split('-')[0].ToLowerInvariant();
            var evidence = Evidence(subject);

            if (evidence.Contains(want))
            {
                return Decision.Accept();
            }

            var name = EditionLanguages.Name(edition.Trim());

            if (evidence.Count == 0)
            {
                _logger.Debug("No {0} marker on '{1}', rejecting", name, subject.Release?.Title);
                return Decision.Reject("No {0} language marker on this release (the series is the {0} edition)", Word(name));
            }

            var found = string.Join("/", evidence.Select(EditionLanguages.Name));
            _logger.Debug("'{0}' is {1}, the series is the {2} edition, rejecting", subject.Release?.Title, found, name);

            return Decision.Reject("Release is {0}, the series is the {1} edition", Word(found), Word(name));
        }

        private Decision English(RemoteBook subject)
        {
            var evidence = Evidence(subject, WithoutSeriesNames(subject.Release?.Title, subject.Author?.Metadata?.Value));

            if (evidence.Count == 0 || evidence.Contains("en"))
            {
                return Decision.Accept();
            }

            var found = string.Join("/", evidence.Select(EditionLanguages.Name));
            _logger.Debug("'{0}' is {1}, the series is the English edition, rejecting", subject.Release?.Title, found);

            return Decision.Reject("Release is {0}, the series is the {1} edition", Word(found), new ServerText("English"));
        }

        // Server messages (2026-09-26): a language's English name as a nested carrier, so the UI shows it in its
        // own language (the ServerRejectionLanguage* keys: the languages ReleaseLanguageParser tags, and since the
        // 2026-09-27 follow-ups the region names EditionLanguages.Name returns, "Portuguese (Brazil)" and
        // "Chinese (Taiwan)"). A name with no key (a joined "French/German") stays English.
        private static ServerText Word(string name)
        {
            return new ServerText(name);
        }

        private static string WithoutSeriesNames(string title, AuthorMetadata meta)
        {
            if (title == null || meta == null)
            {
                return title;
            }

            // '_' and non-decimal '.' are separators ("Raw.Hero.v01"), as EditionVolumeTokens.Rewrite reads
            // them; a name is cut only as whole words and only from 4 characters (SeriesMatcher's floor), so
            // a short name can never eat a language tag.
            title = Separators(title);

            var names = new[] { meta.Name, meta.AnchorName }
                .Concat(meta.Aliases ?? new List<string>())
                .Where(n => n.IsNotNullOrWhiteSpace())
                .Select(n => Separators(n).Trim())
                .Where(n => n.Length >= 4)
                .OrderByDescending(n => n.Length);

            foreach (var name in names)
            {
                var words = string.Join(@"\s+", name.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
                title = Regex.Replace(title, @"(?<![\p{L}\p{N}])" + words + @"(?![\p{L}\p{N}])", " ", RegexOptions.IgnoreCase);
            }

            return title;
        }

        private static string Separators(string text)
        {
            return Regex.Replace(text.Replace('_', ' '), @"(?<!\d)\.(?!\d)", " ");
        }

        private static List<string> Evidence(RemoteBook subject)
        {
            return Evidence(subject, subject.Release?.Title);
        }

        private static List<string> Evidence(RemoteBook subject, string title)
        {
            var fromIndexer = (subject.Release?.Languages ?? new List<Languages.Language>())
                .Select(l => IsoLanguages.Get(l)?.TwoLetterCode)
                .Where(c => !string.IsNullOrWhiteSpace(c));

            return fromIndexer.Concat(ReleaseLanguageParser.Parse(title)).Distinct().ToList();
        }
    }
}
