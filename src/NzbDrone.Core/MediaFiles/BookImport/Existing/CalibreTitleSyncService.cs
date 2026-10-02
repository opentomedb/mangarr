using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;

namespace NzbDrone.Core.MediaFiles.BookImport.Existing
{
    public interface ICalibreTitleSyncService
    {
        // True only when calibre answered and every needed title/sort write went through; false
        // carries the reason ("library read failed: …", "N title(s) failed") for a report line.
        bool Sync(Author author, List<BookFile> calibreFiles, CalibreSettings settings, out string failure);
    }

    // One display title (2026-09-23, the maintainer); fix round 1: the calibre half of the one-title feature,
    // shared by SyncLightNovelTitlesService (whole-library/one-entry command) and
    // ImportExistingLightNovelsService (L5: a newly adopted book gets the format immediately,
    // instead of waiting for someone to run the command). One GetBooks read per call; a set-fields
    // (CalibreProxy.SetTitle -- title and sort ONLY, C2) only when calibre's title or sort disagrees
    // (I1); a row calibre no longer has (L3), an unnumbered volume (L1, both stores must agree with
    // ABS skipping it) or a row with no loaded Book is skipped, never a failed sync. A per-item
    // failure is a Warn and the loop continues (L4), matching AdoptedAudioSyncService's shape.
    public class CalibreTitleSyncService : ICalibreTitleSyncService
    {
        private readonly ICalibreProxy _calibreProxy;
        private readonly Logger _logger;

        public CalibreTitleSyncService(ICalibreProxy calibreProxy, Logger logger)
        {
            _calibreProxy = calibreProxy;
            _logger = logger;
        }

        public bool Sync(Author author, List<BookFile> calibreFiles, CalibreSettings settings, out string failure)
        {
            failure = null;

            if (calibreFiles.Empty())
            {
                return true;
            }

            Dictionary<int, CalibreBook> calibreBooks;

            try
            {
                calibreBooks = _calibreProxy.GetBooks(calibreFiles.Select(r => r.CalibreId).Distinct().ToList(), settings)
                    .ToDictionary(b => b.Id);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Calibre did not answer; {0}'s calibre titles left as they are", author.Name);
                failure = $"library read failed: {ex.Message}";
                return false;
            }

            var seriesTitle = LightNovelTitles.SeriesOf(author);
            var failed = 0;

            foreach (var row in calibreFiles)
            {
                var book = row.Edition?.Value?.Book?.Value;

                // L1: an unnumbered volume keeps edition.Title in calibre (CalibreProxy.SetFields) --
                // never touched here either, so the two stores still agree.
                if (book == null || book.VolumeNumber <= 0)
                {
                    continue;
                }

                // L3: the id is gone from calibre (deleted there); OffEntryFileReconciler owns
                // forgetting the row, never this pass.
                if (!calibreBooks.TryGetValue(row.CalibreId, out var calibreBook))
                {
                    continue;
                }

                // Preferred Edition (2026-09-24, D4): the same edition label CalibreProxy.SetFields writes.
                var title = LightNovelTitles.Display(seriesTitle, book.VolumeNumber, book.Subtitle, EditionLanguages.ReleaseLanguage(author.Metadata.Value));
                var sort = LightNovelTitles.SortTitle(seriesTitle, book.VolumeNumber);

                // I1: compare BOTH title and sort -- a book whose title landed without its sort (a
                // pre-fix-round-1 build, or a sort a user changed by hand) must still be repaired.
                if (string.Equals(calibreBook.Title, title, StringComparison.Ordinal) &&
                    string.Equals(calibreBook.Sort, sort, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    _calibreProxy.SetTitle(row, title, sort, settings);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Calibre title for {0} ({1}) could not be set", row.Path, author.Name);
                    failed++;
                }
            }

            if (failed > 0)
            {
                failure = $"{failed} title(s) failed";
                return false;
            }

            return true;
        }
    }
}
