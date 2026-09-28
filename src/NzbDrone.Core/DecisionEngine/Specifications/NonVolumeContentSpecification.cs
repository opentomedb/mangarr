using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    // Manga: reject releases whose TITLE says the content is not volume archives —
    // chapter rips and audiobooks. Both incident classes downloaded fully, then jammed
    // the queue forever (importFailed / "No files found are eligible"). Refusing the
    // grab is the only fully seeding-safe layer: marking a failed download removes it
    // from the client WITH DATA when RemoveFailedDownloads is on and the torrent has
    // reached its seed limits (DownloadEventHub.Handle -> RemoveItem(item, true)).
    public class NonVolumeContentSpecification : IDecisionEngineSpecification
    {
        // Chapter markers safe to reject ONLY when no volume token is present:
        //  - plural/abbreviated chapter words: "chapters", "chaps", "chpt(s)"
        //  - explicit ranges: "c001-c118", "ch. 1-118", "chapter 1 to 118"
        //  - additive "+ chapters"
        // Singular "Chapter N" is deliberately NOT here: Re:ZERO names its arcs
        // "Chapter 4 - The Sanctuary and the Witch of Greed" and those are legit volume
        // releases. The volume-token gate below also protects mixed packs like
        // "Slime - Volumes 1 to 25 and Chapters 114 to 123", which legitimately fills
        // wanted volumes.
        private static readonly Regex ChapterMarkerRegex = new Regex(
            @"\b(?:chapters|chaps?|chpts?)\b" +
            @"|\bc(?:h(?:ap(?:ter)?)?)?\.?\s*\d{1,4}\s*(?:[-–~]|\s+to\s+)\s*c?(?:h(?:ap(?:ter)?)?)?\.?\s*\d{1,4}\b" +
            @"|\+\s*\d*\s*chapters?\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Audio signals, NOT gated on volume tokens (an audiobook of vols 1-7 is still audio):
        //  - wording: audiobook(s), narrated, (un)abridged
        //  - codec tokens: m4b/m4a/mp3/aac/flac (partly redundant with the quality profile,
        //    which already rejects audio qualities — kept for an explicit visible reason)
        //  - square-bracketed audiobook release-group tags, following the EbookGroupTagRegex
        //    precedent: [Troglodyte] batches carry NO format token at all, which is how the
        //    m4b Slime grab happened (58 grab events before it was stopped by hand).
        private static readonly Regex AudioMarkerRegex = new Regex(
            @"\b(?:audio\s?books?|m4[ab]|mp3|aac|flac|unabridged|abridged|narrated)\b" +
            @"|\[(?:troglodyte)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Bare [v2]/(v3) scan-revision tags read as volume tokens and would defeat the
        // chapter gate ("Series c001-c118 (Digital) (v2)"); strip them before gating.
        private static readonly Regex RevisionTagRegex = new Regex(
            @"[\[(]v\d{1,2}[\])]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly Logger _logger;

        public NonVolumeContentSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            var title = subject.Release?.Title;

            if (title.IsNullOrWhiteSpace())
            {
                return Decision.Accept();
            }

            // Light novels (2026-09, incident fix C): the Audio leg of a light novel wants exactly
            // these releases, so the audio gate only guards the Archive and Ebook legs.
            if (subject.MediaType != MediaType.Audio && AudioMarkerRegex.IsMatch(title))
            {
                _logger.Debug("Audiobook markers in '{0}', rejecting.", title);
                return Decision.Reject("Audiobook release");
            }

            if (ChapterMarkerRegex.IsMatch(title) && !MangaVolumeParser.HasVolumeToken(RevisionTagRegex.Replace(title, string.Empty)))
            {
                _logger.Debug("Chapter markers and no volume token in '{0}', rejecting.", title);
                return Decision.Reject("Chapter release with no volume archives");
            }

            return Decision.Accept();
        }
    }
}
