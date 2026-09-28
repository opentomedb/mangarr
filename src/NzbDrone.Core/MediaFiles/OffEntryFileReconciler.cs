using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;

namespace NzbDrone.Core.MediaFiles
{
    public interface IOffEntryFileReconciler
    {
        void Reconcile(Author author);
    }

    // One copy each (2026-09-20): a light-novel entry's off-entry rows -- EPUBs in calibre's own
    // library, audio in Audiobookshelf's tree -- are never under the entry folder, so the scan's
    // folder walk and its sweep of rows under the entry path never see them. This is their only
    // judge: a calibre row is re-read by id (a rename inside calibre updates Path instead of
    // looking new-plus-missing; a book, or the EPUB/AZW3/PDF format it is tracked by, that is gone
    // drops the row -- CalibreFormats.TrackedLightNovelFormat), an audiobook
    // row is kept while its path exists (judged only while the Audiobookshelf mount is present
    // and non-empty). A dropped row returns its volume to Wanted, exactly as a deleted file does
    // today.
    public class OffEntryFileReconciler : IOffEntryFileReconciler
    {
        private readonly IMediaFileService _mediaFileService;
        private readonly ICalibreProxy _calibre;
        private readonly ILightNovelCalibreSettings _settings;
        private readonly ILightNovelStorage _storage;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public OffEntryFileReconciler(IMediaFileService mediaFileService,
                                      ICalibreProxy calibre,
                                      ILightNovelCalibreSettings settings,
                                      ILightNovelStorage storage,
                                      IDiskProvider diskProvider,
                                      Logger logger)
        {
            _mediaFileService = mediaFileService;
            _calibre = calibre;
            _settings = settings;
            _storage = storage;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Reconcile(Author author)
        {
            var rows = _mediaFileService.GetFilesByAuthor(author.Id).Where(f => f.Home != FileHome.Entry).ToList();
            if (rows.Empty())
            {
                return;
            }

            var calibreRows = rows.Where(f => f.Home == FileHome.Calibre).ToList();

            // Light-novel storage (2026-09-22): while calibre is not the ebook home its rows keep their
            // home and nobody judges them (a changed setting moves nothing, and calibre may be gone).
            if (calibreRows.Any() && _storage.EbookHome != LightNovelHome.Calibre)
            {
                _logger.Debug("Ebooks go to the entry folder; {0}'s calibre rows left as they are", author.Name);
                calibreRows.Clear();
            }

            if (calibreRows.Any())
            {
                Dictionary<int, CalibreBook> books = null;
                try
                {
                    books = _calibre.GetBooks(calibreRows.Select(f => f.CalibreId).Distinct().ToList(), _settings.ForConfig()).ToDictionary(b => b.Id);
                }
                catch (Exception ex)
                {
                    // an unreachable content server judges nothing this pass: dropping rows here would re-grab the library
                    _logger.Warn(ex, "Calibre did not answer; {0}'s calibre files left as they are this scan", author.Name);
                }

                if (books != null)
                {
                    foreach (var row in calibreRows)
                    {
                        // EPUB, else the AZW3 fallback (final review C1), else a PDF (LN PDF, 2026-09-22):
                        // an AZW3-only book judged by EPUB alone was forgotten every scan and re-grabbed
                        // as a duplicate book
                        var formats = books.TryGetValue(row.CalibreId, out var book) ? book.Formats : null;
                        var key = CalibreFormats.TrackedLightNovelFormat(formats?.Keys);
                        var tracked = key != null ? formats[key] : null;

                        if (tracked == null || tracked.Path.IsNullOrWhiteSpace())
                        {
                            _logger.Info("calibre book {0} (or its EPUB/AZW3/PDF) is gone; forgetting {1}", row.CalibreId, row.Path);
                            _mediaFileService.Delete(row, DeleteMediaFileReason.MissingFromDisk);
                            continue;
                        }

                        if (!row.Path.PathEquals(tracked.Path) || row.Size != tracked.Size)
                        {
                            _logger.Debug("calibre moved {0} -> {1}", row.Path, tracked.Path);
                            row.Path = tracked.Path;
                            row.Size = tracked.Size;
                            row.Modified = tracked.LastModified;
                            _mediaFileService.Update(row);
                        }
                    }
                }
            }

            var audioRows = rows.Where(f => f.Home == FileHome.Audiobooks).ToList();

            // Light-novel storage (2026-09-22): while Audiobookshelf is not the audio home its rows
            // keep their home and nobody judges them (a changed setting moves nothing, and ABS may be gone).
            if (audioRows.Any() && _storage.AudioHome != LightNovelHome.Audiobookshelf)
            {
                _logger.Debug("Audiobooks go to the entry folder; {0}'s audiobook rows left as they are", author.Name);
                audioRows.Clear();
            }

            if (audioRows.Empty())
            {
                return;
            }

            // An absent bind mount answers "no file" for every path and a bind mount whose share
            // is offline is an empty directory: either would forget every audiobook row (the
            // adopted ones included) in one scan, and the next search would re-grab audio already
            // owned. The folder is judged first -- unknown counts as absent -- the same way the
            // calibre outage is above (and the way DiskScanService skips an absent or empty root).
            var audioRoot = _storage.AudiobookshelfRoot();

            if (audioRoot == null || !_diskProvider.FolderExists(audioRoot) || _diskProvider.FolderEmpty(audioRoot))
            {
                _logger.Warn("Audiobookshelf's library folder {0} is missing, empty or unknown; {1}'s audiobook rows left as they are this scan", audioRoot ?? "(unknown)", author.Name);
                return;
            }

            foreach (var row in audioRows)
            {
                if (!_diskProvider.FileExists(row.Path))
                {
                    _logger.Info("audiobook file is gone; forgetting {0}", row.Path);
                    _mediaFileService.Delete(row, DeleteMediaFileReason.MissingFromDisk);
                }
            }
        }
    }
}
