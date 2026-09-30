using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    // Line safety (2026-09-28): the Trapped in a Dating Sim incident. The main series' 13-volume pack was
    // grabbed for the 6-volume spin-off entry (its catalogue line is named "<main> (<spin-off>)") and
    // volumes 1-6 imported onto it. A download whose title names a volume RANGE that runs past the highest
    // volume of the entry's bound line, when that line is not its work's main line and another line of the
    // same work, market and medium reaches the range's end, is rejected here -- it lands in the queue as
    // Import Failed with this reason, and Manual Import (which never re-runs these specs) can still put it
    // anywhere. Left alone: main lines and entries with no catalogue line (an ongoing series legitimately
    // gets new volumes), single-volume releases (an ongoing spin-off's next volume), anything that is not a
    // download (a disk scan has no title), and any artifact without is_main / tome_work_id (no siblings).
    // Review fixes (2026-09-28, I5): a line's catalogue count can be stale (a spin-off that has grown since the
    // artifact was built), so a release whose title names the bound line's own distinguishing part -- its name
    // past the part it shares with the covering line, or its parenthetical -- is the spin-off's and is let in.
    public class SiblingLineReleaseSpecification : IImportDecisionEngineSpecification<LocalEdition>
    {
        private readonly IGcdMetadataService _gcdMetadataService;
        private readonly Logger _logger;

        public SiblingLineReleaseSpecification(IGcdMetadataService gcdMetadataService, Logger logger)
        {
            _gcdMetadataService = gcdMetadataService;
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalEdition item, DownloadClientItem downloadClientItem)
        {
            if (downloadClientItem == null || !item.NewDownload || downloadClientItem.Title.IsNullOrWhiteSpace())
            {
                return Decision.Accept();
            }

            var author = item.Edition?.Book?.Value?.Author?.Value ?? item.LocalBooks.FirstOrDefault()?.Author;
            var tomeLineId = author?.Metadata?.Value?.TomeLineId;

            if (tomeLineId.IsNullOrWhiteSpace())
            {
                return Decision.Accept();
            }

            var range = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(downloadClientItem.Title, range);

            if (!(range.VolumeStart > 0 && range.VolumeEnd > range.VolumeStart))
            {
                return Decision.Accept();
            }

            var start = range.VolumeStart.Value;
            var end = range.VolumeEnd.Value;

            if (!_gcdMetadataService.Available)
            {
                return Decision.Accept();
            }

            var line = _gcdMetadataService.FindSeriesByTomeId(tomeLineId);

            if (line == null || line.IsMain || end <= line.VolumeCount)
            {
                return Decision.Accept();
            }

            var covering = WorkLines.Siblings(line, WorkLines.Of(_gcdMetadataService, line))
                .Where(c => c.VolumeCount >= end)
                .OrderByDescending(c => c.IsMain)
                .ThenByDescending(c => c.DatedCount ?? 0)
                .ThenBy(c => c.GcdSeriesId)
                .FirstOrDefault();

            if (covering == null)
            {
                return Decision.Accept();
            }

            if (NamesOwnLine(downloadClientItem.Title, line, covering, author.Library))
            {
                return Decision.Accept();
            }

            // Judged by the line's catalogue count only, never the entry's own volume rows: rows past the
            // count are exactly what a mis-attached sibling pack or disk file would leave behind.
            var known = line.VolumeCount;

            var belongsTo = WorkLines.DisplayName(covering, author.Library);

            _logger.Debug("'{0}' covers volumes {1}-{2}; {3} is bound to a line with {4}, rejecting (may belong to {5})",
                downloadClientItem.Title,
                MangaVolumeParser.Format(start),
                MangaVolumeParser.Format(end),
                author,
                MangaVolumeParser.Format(known),
                belongsTo);

            return Decision.Reject("Release covers volumes {0}-{1}; this series' line has {2} — it may belong to {3}",
                MangaVolumeParser.Format(start),
                MangaVolumeParser.Format(end),
                MangaVolumeParser.Format(known),
                belongsTo);
        }

        private static readonly Regex Parenthetical = new Regex(@"\(([^()]+)\)", RegexOptions.Compiled);
        private static readonly Regex NonWord = new Regex(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

        // The title names the bound line's own part: the words of its name past those it shares with the
        // covering line's name, or its parenthetical (whole, or past that shared prefix). A part the covering
        // line's name also contains (or a part under four characters) tells the two lines apart by nothing and
        // is not counted.
        private static bool NamesOwnLine(string title, GcdSeries line, GcdSeries covering, LibraryType library)
        {
            var own = WorkLines.DisplayName(line, library) ?? string.Empty;
            var other = Words(WorkLines.DisplayName(covering, library));
            var ownWords = Words(own);

            var parts = new List<string> { string.Join(" ", PastShared(ownWords, other)) };

            // The parenthetical, whole and past the franchise prefix it may repeat ("(Trapped in a Dating Sim:
            // Otome Games Are Tough For Us, Too!)" -> "otome games are tough for us too").
            foreach (var paren in Parenthetical.Matches(own).Select(m => Words(m.Groups[1].Value)))
            {
                parts.Add(string.Join(" ", paren));
                parts.Add(string.Join(" ", PastShared(paren, other)));
            }

            var paddedTitle = $" {string.Join(" ", Words(title))} ";
            var paddedOther = $" {string.Join(" ", other)} ";

            return parts.Any(p => p.Length >= 4 && !paddedOther.Contains($" {p} ") && paddedTitle.Contains($" {p} "));
        }

        private static IEnumerable<string> PastShared(List<string> words, List<string> other)
        {
            var shared = 0;
            while (shared < words.Count && shared < other.Count && words[shared] == other[shared])
            {
                shared++;
            }

            return words.Skip(shared);
        }

        private static List<string> Words(string text)
        {
            return NonWord.Split((text ?? string.Empty).ToLowerInvariant()).Where(w => w.Length > 0).ToList();
        }
    }
}
