using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MediaFiles.BookImport.Existing
{
    public interface IAdoptedAudioSyncService
    {
        // True only when the library was read and every needed patch went through; false carries
        // the reason ("library read failed: …", "N patch(es) failed") for a report line.
        bool Sync(Author author, List<BookFile> audioFiles, out string failure);
    }

    // One copy each (2026-09-20); one display title (2026-09-23, the maintainer): every Audiobooks-homed LN
    // audio row of the entry -- adopted AND grabbed, Mangarr never writes into either kind -- has its
    // series, sequence and title pushed to ABS over its API so the shelf shows the same title as
    // calibre: LightNovelTitles.Display(entry name, Book.VolumeNumber, Book.Subtitle) -- the same
    // builder CalibreProxy.SetFields calls for calibre's Title. ABS's own subtitle is always cleared
    // (the subtitle is folded into the title text, never a separate ABS field). The overrides.json
    // audiobookTitle pin no longer feeds this -- it keeps its other jobs (Audible matching/tagging).
    // One library read per call; each ABS item is one decision (the parts of a multi-file audiobook
    // share it); a patch only when ABS disagrees on series, sequence, title or subtitle. ABS's answer
    // is logged, never a failed refresh or retag; the result says whether the shelf is aligned, for
    // the adoption report.
    public class AdoptedAudioSyncService : IAdoptedAudioSyncService
    {
        private readonly IAudiobookshelfClient _audiobookshelf;
        private readonly ILightNovelStorage _storage;
        private readonly Logger _logger;

        public AdoptedAudioSyncService(IAudiobookshelfClient audiobookshelf,
                                       ILightNovelStorage storage,
                                       Logger logger)
        {
            _audiobookshelf = audiobookshelf;
            _storage = storage;
            _logger = logger;
        }

        public bool Sync(Author author, List<BookFile> audioFiles, out string failure)
        {
            failure = null;

            // Light-novel storage (2026-09-22): writing series/sequence to Audiobookshelf is ABS-only
            // behaviour; while audio goes to the entry folder nothing is sent.
            if (_storage.AudioHome != LightNovelHome.Audiobookshelf)
            {
                _logger.Debug("Audiobooks go to the entry folder; {0}'s Audiobookshelf shelf left as it is", author.Name);
                return true;
            }

            if (audioFiles.Empty())
            {
                return true;
            }

            Dictionary<string, AudiobookshelfItem> items;

            try
            {
                items = _audiobookshelf.GetLibraryItems()
                    .Where(i => i.Path.IsNotNullOrWhiteSpace())
                    .GroupBy(i => i.Path, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Audiobookshelf did not answer; {0}'s audiobooks left as they are", author.Name);
                failure = $"library read failed: {ex.Message}";
                return false;
            }

            var done = new HashSet<string>(StringComparer.Ordinal);
            var failed = 0;

            foreach (var row in audioFiles)
            {
                var item = FindItem(items, row.Path);

                if (item == null)
                {
                    _logger.Debug("{0}: Audiobookshelf has no item at this path; nothing to sync", row.Path);
                    continue;
                }

                if (!done.Add(item.Id))
                {
                    continue;
                }

                var book = row.Edition?.Value?.Book?.Value;
                var sequence = book != null && book.VolumeNumber > 0 ? MangaVolumeParser.Format(book.VolumeNumber) : null;

                if (sequence == null)
                {
                    _logger.Debug("{0}: unnumbered volume; nothing to sync", row.Path);
                    continue;
                }

                // One display title (2026-09-23): the subtitle is folded into the title text, so
                // ABS's own subtitle field is always cleared (null) -- never Book.Subtitle.
                // Preferred Edition (2026-09-24, D4): the edition label calibre's title carries too.
                var title = LightNovelTitles.Display(author.Name, book.VolumeNumber, book.Subtitle, EditionLanguages.ReleaseLanguage(author.Metadata.Value));
                var seriesMatches = HasSeries(item.SeriesName, author.Name, sequence);
                var titleMatches = string.Equals(title, item.Title, StringComparison.Ordinal);
                var subtitleMatches = item.Subtitle.IsNullOrWhiteSpace();

                if (seriesMatches && titleMatches && subtitleMatches)
                {
                    _logger.Debug("Audiobookshelf item {0} '{1}' already has {2} #{3}, title and subtitle; nothing to sync", item.Id, item.Title, author.Name, sequence);
                    continue;
                }

                try
                {
                    var patched = _audiobookshelf.PatchMetadata(item.Id, author.Name, sequence, title, null);
                    _logger.Debug("Audiobookshelf item {0} '{1}': {2} #{3}, title '{4}' {5}", item.Id, item.Title, author.Name, sequence, title, patched ? "patched" : "already so");
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Audiobookshelf item {0} '{1}' could not be patched to {2} #{3}", item.Id, item.Title, author.Name, sequence);
                    failed++;
                }
            }

            if (failed > 0)
            {
                failure = $"{failed} patch(es) failed";
                return false;
            }

            return true;
        }

        // The row's item: a single-file item's ABS path is the file itself, a folder item's is the
        // folder -- the file first, then its folder. MapPath in reverse (Mangarr's path -> Audiobookshelf's);
        // a row outside the mapped folder is nobody's.
        private AudiobookshelfItem FindItem(Dictionary<string, AudiobookshelfItem> items, string hostPath)
        {
            var absFile = ToAbsPath(hostPath);

            if (absFile == null)
            {
                return null;
            }

            if (items.TryGetValue(absFile, out var item))
            {
                return item;
            }

            var absFolder = ToAbsPath(Path.GetDirectoryName(hostPath));

            return absFolder != null && items.TryGetValue(absFolder, out item) ? item : null;
        }

        // Same identity hole as ImportExistingLightNovelsService.MapPath: with a blank pair the
        // mapper returns hostPath unchanged before its own "." / ".." check runs. hostPath is
        // Mangarr's own stored row, never attacker-controlled, so this is defense in depth -- a
        // dictionary-key mismatch either way -- not a live escape (nothing is read from disk here).
        private string ToAbsPath(string hostPath)
        {
            if (hostPath != null && hostPath.Split('/').Any(s => s == "." || s == ".."))
            {
                return null;
            }

            return _storage.MapToAudiobookshelf(hostPath);
        }

        // ABS's seriesName is "Name #seq", several joined with ", ". Ours anywhere in it counts; the
        // name without case, the sequence as a number ("#01" is "#1") when it parses, else as text.
        // A range ("#3-4", a pack adopted onto its first volume) whose FIRST number is ours is not a
        // difference: ABS's record is right and richer than ours, and a patch would cut it to "#3".
        private static bool HasSeries(string seriesName, string name, string sequence)
        {
            if (seriesName.IsNullOrWhiteSpace())
            {
                return false;
            }

            var match = Regex.Match(seriesName, $@"(?:^|,\s*){Regex.Escape(name)}\s*#\s*(?<seq>[^,]+?)\s*(?:,|$)", RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                return false;
            }

            var actual = match.Groups["seq"].Value;
            var range = AudibleSequence.Parse(actual);

            return range.HasValue
                ? MangaVolumeParser.Format(range.Value.From) == sequence
                : string.Equals(actual, sequence, StringComparison.OrdinalIgnoreCase);
        }
    }
}
