using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Download
{
    public interface ICompletedDownloadService
    {
        void Check(TrackedDownload trackedDownload);
        void Import(TrackedDownload trackedDownload);
        bool VerifyImport(TrackedDownload trackedDownload, List<ImportResult> importResults);
    }

    public class CompletedDownloadService : ICompletedDownloadService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IHistoryService _historyService;
        private readonly IProvideImportItemService _provideImportItemService;
        private readonly IDownloadedBooksImportService _downloadedTracksImportService;
        private readonly ITrackedDownloadAlreadyImported _trackedDownloadAlreadyImported;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IDiskProvider _diskProvider;
        private readonly ICoveredVolumeService _coveredVolumeService;
        private readonly IEditionService _editionService;
        private readonly Logger _logger;

        public CompletedDownloadService(IEventAggregator eventAggregator,
                                        IHistoryService historyService,
                                        IProvideImportItemService provideImportItemService,
                                        IDownloadedBooksImportService downloadedTracksImportService,
                                        ITrackedDownloadAlreadyImported trackedDownloadAlreadyImported,
                                        IFailedDownloadService failedDownloadService,
                                        IProvideDownloadClient downloadClientProvider,
                                        IDiskProvider diskProvider,
                                        ICoveredVolumeService coveredVolumeService,
                                        IEditionService editionService,
                                        Logger logger)
        {
            _eventAggregator = eventAggregator;
            _historyService = historyService;
            _provideImportItemService = provideImportItemService;
            _downloadedTracksImportService = downloadedTracksImportService;
            _trackedDownloadAlreadyImported = trackedDownloadAlreadyImported;
            _failedDownloadService = failedDownloadService;
            _downloadClientProvider = downloadClientProvider;
            _diskProvider = diskProvider;
            _coveredVolumeService = coveredVolumeService;
            _editionService = editionService;
            _logger = logger;
        }

        public void Check(TrackedDownload trackedDownload)
        {
            if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
            {
                return;
            }

            SetImportItem(trackedDownload);

            // Only process tracked downloads that are still downloading
            if (trackedDownload.State != TrackedDownloadState.Downloading)
            {
                return;
            }

            var historyItem = _historyService.MostRecentForDownloadId(trackedDownload.DownloadItem.DownloadId);

            if (historyItem == null && trackedDownload.DownloadItem.Category.IsNullOrWhiteSpace())
            {
                trackedDownload.Warn("Download wasn't grabbed by Mangarr and not in a category, Skipping.");
                return;
            }

            if (!ValidatePath(trackedDownload))
            {
                return;
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;
        }

        public void Import(TrackedDownload trackedDownload)
        {
            SetImportItem(trackedDownload);

            if (!ValidatePath(trackedDownload))
            {
                return;
            }

            trackedDownload.State = TrackedDownloadState.Importing;

            var outputPath = trackedDownload.ImportItem.OutputPath.FullPath;
            var payloadFiles = GetPayloadFiles(outputPath);

            // Chapter rip: per-chapter archives can never map onto volume books; importing
            // would fail into the queue forever (MARRIAGE TOXIN: 118 "Chapter N.cbz" files at
            // 48% match). Fail + blocklist + re-search instead. Filename-only detection,
            // deliberately conservative: fires only when NO file volume-parses, at least 3
            // files are chapter-patterned, and they are >=80% of the comic files (tolerates
            // an Omake/credits extra).
            var allExtensions = MediaFileExtensions.AllExtensions;
            var mediaFiles = payloadFiles.Where(f => allExtensions.Contains(Path.GetExtension(f))).ToList();
            var chapterFiles = mediaFiles.Where(f => MangaVolumeParser.IsChapterOnlyFile(f)).ToList();

            if (chapterFiles.Count >= 3 &&
                chapterFiles.Count >= mediaFiles.Count * 0.8 &&
                mediaFiles.None(f => MangaVolumeParser.ParseSingleVolume(Path.GetFileNameWithoutExtension(f)) > 0))
            {
                var reason = new ServerText("Chapter-numbered rip: {0} of {1} comic files are per-chapter, no volume files", chapterFiles.Count, mediaFiles.Count);
                trackedDownload.Warn(reason);
                FailUnusablePayload(trackedDownload, reason.English);

                return;
            }

            // The one media class every payload file belongs to (null: none, or mixed). Read by the
            // known-book rule below and by the wrong-class check after it. LN PDF fix round 1
            // (2026-09-22): the PDF-is-ebook read only applies when Ebook PDF (id 8) is actually
            // wanted in the author's ebook profile -- the default profiles ship it unticked, so
            // with nothing to opt in a light-novel PDF-only payload still fails + blocklists exactly
            // as before the LN-PDF round, not silently import.
            var remoteAuthor = trackedDownload.RemoteBook?.Author;
            var ebookPdfWanted = remoteAuthor?.QualityProfileFor(MediaType.Ebook)?.Allows(Quality.EbookPdf) ?? false;
            var payloadType = PayloadMediaType(payloadFiles, ebookPdfWanted ? remoteAuthor?.Library : null);

            // Single-volume grab: if the grab resolved to exactly one Book, pass it so a loose
            // single-file import (manga CBZ) matches that known Book directly instead of relying on
            // tag identification (which returns 0 candidates for tagless CBZs). Packs/multi-book
            // grabs (Books.Count != 1) pass null and keep the existing per-file identification --
            // except an Audio, light-novel grab whose title names ONE of its mapped volumes
            // (KnownBook, 2026-09-18).
            var knownBook = KnownBook(trackedDownload, mediaFiles, payloadType);

            // A lone chapter-named file must never be force-matched as the grabbed volume;
            // let per-file identification judge (and reject) it instead.
            if (knownBook != null && payloadFiles.Count == 1 && MangaVolumeParser.IsChapterOnlyFile(payloadFiles[0]))
            {
                knownBook = null;
            }

            // Wrong-class payload: when every media file in the payload is of ONE class that no
            // monitored edition of the grabbed volume(s) can hold -- an EPUB or an audiobook for a
            // manga volume, an archive for a light-novel volume -- the grab can never import: fail
            // it (blocklist + re-search) instead of leaving it jammed in the queue. Judged BEFORE
            // the import runs: .epub and audio files are importable now, so the pipeline hands back
            // a rejected decision per file rather than an empty list, and a check behind
            // importResults.Empty() would never fire (final review C1). A mixed-class payload is
            // null here and falls through to the import; a pure-archive payload for a manga volume
            // is accepted and takes exactly the path it always did. (Light novels 2026-09; replaces
            // the ebook-only / audiobook-only guards of the manga-only days.)
            if (payloadType.HasValue && !AcceptsPayloadClass(trackedDownload.RemoteBook, payloadType.Value))
            {
                // Server messages (2026-09-26): one whole sentence per class, never the class word as an argument,
                // so each sentence translates with its own grammar.
                var reason = new ServerText("Download contains only archive files, which no monitored edition of the grabbed volume(s) can hold");

                if (payloadType.Value == MediaType.Ebook)
                {
                    reason = new ServerText("Download contains only ebook files, which no monitored edition of the grabbed volume(s) can hold");
                }
                else if (payloadType.Value == MediaType.Audio)
                {
                    reason = new ServerText("Download contains only audiobook files, which no monitored edition of the grabbed volume(s) can hold");
                }

                trackedDownload.Warn(reason);
                FailUnusablePayload(trackedDownload, reason.English);

                return;
            }

            var importResults = _downloadedTracksImportService.ProcessPath(outputPath, ImportMode.Auto, trackedDownload.RemoteBook?.Author, trackedDownload.DownloadItem, knownBook);

            if (importResults.Empty())
            {
                // Nothing imported and nothing to blame on the payload's class (a payload of a
                // class the volume does want is an identification problem, not a wrong grab).
                trackedDownload.Warn("No files found are eligible for import in {0}", outputPath);
                trackedDownload.State = TrackedDownloadState.ImportPending;
                return;
            }

            // Covered volumes (2026-09-17, D4): a spanning audiobook — one release mapped to several
            // volumes whose files all landed on one of them — covers the others.
            MarkCoveredVolumes(trackedDownload, importResults);

            if (VerifyImport(trackedDownload, importResults))
            {
                return;
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;

            if (importResults.All(c => c.Result == ImportResultType.Imported))
            {
                // Every eligible file imported, but the mapping expected more books: a tokenless
                // pack maps to the whole series while holding only part of it. The release can
                // never deliver more, so it is done. Left ImportPending it re-imports on every
                // pass, and with a second pack of the same volumes the two replace each other's
                // library files forever (SAO Progressive, 2026-09-03).
                _logger.Debug("All {0} files imported from {1} although the release mapped to {2} books, marking download as imported", importResults.Count, trackedDownload.DownloadItem.Title, trackedDownload.RemoteBook?.Books?.Count);

                var fullyImportedAuthorId = importResults
                    .Select(c => c.ImportDecision.Item.Author.Id)
                    .MostCommon();

                trackedDownload.State = TrackedDownloadState.Imported;
                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, trackedDownload.RemoteBook?.Author?.Id ?? fullyImportedAuthorId));
                return;
            }

            if (importResults.Any(c => c.Result != ImportResultType.Imported))
            {
                // If every file that did not import was rejected only because its book was
                // already imported by this same download, the download's content is fully in
                // the library. Mark it Imported so it leaves the queue and stops re-attempting,
                // leaving the download client item untouched (torrent keeps seeding).
                var allFailuresAlreadyImported = importResults
                    .Where(c => c.Result != ImportResultType.Imported)
                    .All(c => c.Errors.Any() && c.Errors.All(AlreadyImportedSpecification.IsAlreadyImportedRejection));

                if (allFailuresAlreadyImported)
                {
                    _logger.Debug("All files not imported from {0} were previously imported, marking download as imported", trackedDownload.DownloadItem.Title);

                    var historyItems = _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId);
                    var importedAuthorId = historyItems.Where(x => x.EventType == EntityHistoryEventType.BookFileImported)
                        .Select(x => x.AuthorId)
                        .MostCommon();

                    trackedDownload.State = TrackedDownloadState.Imported;
                    _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, trackedDownload.RemoteBook?.Author?.Id ?? importedAuthorId));
                    return;
                }

                trackedDownload.State = TrackedDownloadState.ImportFailed;
                var statusMessages = importResults
                    .Where(v => v.Result != ImportResultType.Imported && v.ImportDecision.Item != null)
                    .Select(v => new TrackedDownloadStatusMessage(Path.GetFileName(v.ImportDecision.Item.Path), v.Errors, v.ErrorTexts))
                    .ToArray();

                trackedDownload.Warn(statusMessages);
                _eventAggregator.PublishEvent(new BookImportIncompleteEvent(trackedDownload));
                return;
            }
        }

        public bool VerifyImport(TrackedDownload trackedDownload, List<ImportResult> importResults)
        {
            var imported = importResults.Where(c => c.Result == ImportResultType.Imported).ToList();
            var allItemsImported = imported.Count + CoveredCount(trackedDownload, imported) >= Math.Max(1, trackedDownload.RemoteBook?.Books.Count ?? 1);

            if (allItemsImported)
            {
                _logger.Debug("All books were imported for {0}", trackedDownload.DownloadItem.Title);
                trackedDownload.State = TrackedDownloadState.Imported;

                var importedAuthorId = importResults.Where(x => x.Result == ImportResultType.Imported)
                    .Select(c => c.ImportDecision.Item.Author.Id)
                    .MostCommon();
                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, trackedDownload.RemoteBook?.Author.Id ?? importedAuthorId));
                return true;
            }

            // Double check if all episodes were imported by checking the history if at least one
            // file was imported. This will allow the decision engine to reject already imported
            // episode files and still mark the download complete when all files are imported.

            // EDGE CASE: This process relies on EpisodeIds being consistent between executions, if a series is updated
            // and an episode is removed, but later comes back with a different ID then Sonarr will treat it as incomplete.
            // Since imports should be relatively fast and these types of data changes are infrequent this should be quite
            // safe, but commenting for future benefit.
            var atLeastOneEpisodeImported = importResults.Any(c => c.Result == ImportResultType.Imported);

            var historyItems = _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId)
                                              .OrderByDescending(h => h.Date)
                                              .ToList();

            var allEpisodesImportedInHistory = _trackedDownloadAlreadyImported.IsImported(trackedDownload, historyItems);

            if (allEpisodesImportedInHistory)
            {
                // Log different error messages depending on the circumstances, but treat both as fully imported, because that's the reality.
                // The second message shouldn't be logged in most cases, but continued reporting would indicate an ongoing issue.
                if (atLeastOneEpisodeImported)
                {
                    _logger.Debug("All books were imported in history for {0}", trackedDownload.DownloadItem.Title);
                }
                else
                {
                    _logger.ForDebugEvent()
                           .Message("No books were just imported, but all books were previously imported, possible issue with download history.")
                           .Property("AuthorId", trackedDownload.RemoteBook.Author.Id)
                           .Property("DownloadId", trackedDownload.DownloadItem.DownloadId)
                           .Property("Title", trackedDownload.DownloadItem.Title)
                           .Property("Path", trackedDownload.DownloadItem.OutputPath.ToString())
                           .WriteSentryWarn("DownloadHistoryIncomplete")
                           .Log();
                }

                trackedDownload.State = TrackedDownloadState.Imported;

                var importedAuthorId = historyItems.Where(x => x.EventType == EntityHistoryEventType.BookFileImported)
                    .Select(x => x.AuthorId)
                    .MostCommon();
                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, trackedDownload.RemoteBook?.Author.Id ?? importedAuthorId));

                return true;
            }

            _logger.Debug("Not all books have been imported for {0}", trackedDownload.DownloadItem.Title);
            return false;
        }

        // The shape covered marks apply to: an audio release of a light-novel entry mapped to several
        // volumes (a manga release never enters, whatever it maps to).
        private static bool IsSpanningAudio(RemoteBook remote)
        {
            return remote != null && remote.MediaType == MediaType.Audio && remote.Author?.Library == LibraryType.LightNovel && remote.Books?.Count >= 2;
        }

        // Every imported file landed on ONE of the mapped volumes: that volume carries the span, every
        // other mapped volume is covered by it — "covered by Vol. c" means inside that volume's file,
        // whichever way identification landed the pack. The carrier's own file state is not a
        // condition — an upgrade replaces the carrier's file and comes through here the same way.
        // One file per volume is an ordinary pack and marks nothing.
        //
        // Two things the release itself must say first (review I1/I2). Every file must have imported:
        // a rejected second file's volume is inside nothing, and a mark on it would hide the rejection
        // from the queue (the free check, so a rejected pack costs no parse). And it must carry an
        // explicit bounded range: a tokenless title maps to every released volume, and one Book 1
        // audiobook would otherwise cover the whole series. The range also bounds the marks — only the
        // mapped volumes inside it, as the copy-in does — so a mapping wider than the title (a re-grab
        // under one download id) never marks past what the title says. Either way the download then
        // completes or fails exactly as it did before marks.
        private void MarkCoveredVolumes(TrackedDownload trackedDownload, List<ImportResult> importResults)
        {
            var remote = trackedDownload.RemoteBook;

            if (!IsSpanningAudio(remote) || importResults.Any(r => r.Result != ImportResultType.Imported))
            {
                return;
            }

            var range = BoundedRange(trackedDownload);

            if (range == null)
            {
                return;
            }

            var imported = importResults.Select(r => r.ImportDecision.Item.Book)
                                        .DistinctBy(b => b.Id)
                                        .ToList();

            if (imported.Count != 1)
            {
                return;
            }

            var carrier = imported[0];
            var covered = remote.Books.Where(b => b.Id != carrier.Id && b.VolumeNumber >= range.Value.From && b.VolumeNumber <= range.Value.To).ToList();

            _coveredVolumeService.MarkCoveredByImport(carrier, covered, trackedDownload.DownloadItem.Title);
        }

        // An explicit range in the client's title ("Vol. 01-02", "Books 3-4"), else in the grab's
        // source title — the same parse TrackedDownloadService bounds a history-inherited mapping
        // with. The tokenless bridge's 1-9999 is not a range anyone wrote down.
        private (double From, double To)? BoundedRange(TrackedDownload trackedDownload)
        {
            return BoundedRange(trackedDownload.DownloadItem.Title)
                   ?? _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId)
                                     .Where(h => h.EventType == EntityHistoryEventType.Grabbed)
                                     .Select(h => BoundedRange(h.SourceTitle))
                                     .FirstOrDefault(r => r != null);
        }

        private static (double From, double To)? BoundedRange(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return null;
            }

            var range = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(title, range);

            if (!range.VolumeStart.HasValue || !range.VolumeEnd.HasValue || range.VolumeEnd.Value >= 9999)
            {
                return null;
            }

            return (range.VolumeStart.Value, range.VolumeEnd.Value);
        }

        // The mapped volumes this download did not import but whose Audio edition is covered by one it
        // did: they count as imported, the release delivered everything it maps to. Read back from
        // the store, not the tracked download's copies, which may be a history rehydration.
        private int CoveredCount(TrackedDownload trackedDownload, List<ImportResult> imported)
        {
            var remote = trackedDownload.RemoteBook;

            if (!IsSpanningAudio(remote))
            {
                return 0;
            }

            var importedBooks = imported.Select(r => r.ImportDecision.Item.Book).DistinctBy(b => b.Id).ToList();
            var importedIds = importedBooks.Select(b => b.Id).ToHashSet();
            var importedNumbers = importedBooks.Select(b => b.VolumeNumber).ToHashSet();

            return remote.Books.Count(b => !importedIds.Contains(b.Id) &&
                                           _editionService.GetEditionsByBook(b.Id).Any(e => e.MediaType == MediaType.Audio && e.CoveredByVolume.HasValue && importedNumbers.Contains(e.CoveredByVolume.Value)));
        }

        private List<string> GetPayloadFiles(string outputPath)
        {
            if (_diskProvider.FolderExists(outputPath))
            {
                return _diskProvider.GetFiles(outputPath, true).ToList();
            }

            if (_diskProvider.FileExists(outputPath))
            {
                return new List<string> { outputPath };
            }

            return new List<string>();
        }

        // The book the import may trust the grab for: the single mapped book, as always. Several
        // mapped books -- the Audible-named release maps wide, a history-inherited mapping too --
        // are still ONE volume for an Audio download of a light-novel entry when the download title
        // (or, with one media file, that file's name -- cover art and the like do not count) parses
        // to a single volume number that exactly one mapped book carries (release-trusted import,
        // 2026-09-18: the TBATE .mp4 releases failed CloseAlbumMatchSpecification at 49.5 % because
        // no override reached the folder path). A range is a pack, not a volume, and the file name
        // is not consulted for it -- the B2 covered-volume marks handle a spanning pack. No parse,
        // or zero or several matching books: null, today's per-file identification. Manga:
        // Books.Count != 1 is null, unchanged.
        private static Book KnownBook(TrackedDownload trackedDownload, List<string> mediaFiles, MediaType? payloadType)
        {
            var books = trackedDownload.RemoteBook?.Books;

            if (books == null || books.Count == 0)
            {
                return null;
            }

            if (books.Count == 1)
            {
                return books[0];
            }

            if (trackedDownload.RemoteBook.Author?.Library != LibraryType.LightNovel || payloadType != MediaType.Audio)
            {
                return null;
            }

            var volume = SingleVolume(trackedDownload.DownloadItem.Title, out var range);

            // A title that says it spans is a pack, whatever its one file is called.
            if (range)
            {
                return null;
            }

            if (!volume.HasValue && mediaFiles.Count == 1)
            {
                volume = SingleVolume(Path.GetFileNameWithoutExtension(mediaFiles[0]), out _);
            }

            if (!volume.HasValue)
            {
                return null;
            }

            var named = books.Where(b => b.VolumeNumber == volume.Value).ToList();

            return named.Count == 1 ? named[0] : null;
        }

        // The single volume a title names, per MangaVolumeParser.ParseVolume; null for no volume
        // token. A range fills VolumeStart/VolumeEnd, never VolumeNumber: null, with range set.
        private static double? SingleVolume(string title, out bool range)
        {
            var info = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(title, info);

            range = info.VolumeStart.HasValue || info.VolumeEnd.HasValue;

            return range ? null : info.VolumeNumber;
        }

        // The one media class every media file in the payload belongs to; null when the payload
        // holds no media file or mixes classes. Media = the importable extensions (archives, .epub,
        // audio) plus the never-importable ebook variants (.mobi/.azw/.kepub; .azw3 is imported since 2026-09-22), which class as Ebook
        // so a mobi-only grab for a manga volume still fails instead of looping. LN PDF (2026-09-22):
        // classes are read against the grab's library, so a light novel's PDF payload is the ebook
        // class, not an archive the volume cannot hold.
        private static MediaType? PayloadMediaType(List<string> files, LibraryType? library)
        {
            var classes = files
                .Where(f => MediaFileExtensions.AllExtensions.Contains(Path.GetExtension(f)) ||
                            MediaFileExtensions.EbookExtensions.Contains(Path.GetExtension(f)) ||
                            MediaFileExtensions.AudiobookExtensions.Contains(Path.GetExtension(f)))
                .Select(f => MediaTypes.OfFile(f, library))
                .Distinct()
                .ToList();

            return classes.Count == 1 ? classes[0] : (MediaType?)null;
        }

        // Some grabbed volume must have a monitored edition of the payload's class: a pack maps to
        // several volumes, and one whose edition of that class is unmonitored (the per-edition
        // toggle) must not fail the whole pack -- the import rejects that one file on its own. The
        // grab is failed only when NO grabbed volume can hold the class. With no mapped volumes
        // whose editions are loaded (a history-rehydrated grab) fall back to the entry's library,
        // and to manga when nothing is known -- the behaviour before 2026-09.
        private static bool AcceptsPayloadClass(RemoteBook remoteBook, MediaType payloadType)
        {
            var books = remoteBook?.Books?.Where(b => b.Editions?.Value?.Any() == true).ToList();

            if (books != null && books.Any())
            {
                return books.Any(b => b.EditionOf(payloadType)?.Monitored == true);
            }

            var library = remoteBook?.Author?.Library ?? LibraryType.Manga;

            return library == LibraryType.LightNovel ? payloadType != MediaType.Archive : payloadType == MediaType.Archive;
        }

        // A payload that can never import (chapter rip, wrong class). A grab is marked as failed:
        // blocklist, re-search, removal per the client's Remove Failed Downloads. A download Mangarr
        // did not grab (added to its category by hand) has no Grabbed row, so MarkAsFailed would do
        // nothing; it is parked as ImportFailed with the warning, where the queue offers Manual
        // Import and DownloadProcessingService never runs Import again. It used to go back to
        // ImportPending, which re-ran Import and re-logged "marking as failed" on every queue
        // refresh for hours without failing anything (2026-09-25; beta polish 2026-09-28). A restart
        // re-evaluates it once and parks it again. It is never removed from the client: it isn't
        // Mangarr's download.
        private void FailUnusablePayload(TrackedDownload trackedDownload, string reason)
        {
            if (HasGrabbedHistory(trackedDownload))
            {
                _logger.Warn("Download {0}: {1}, marking as failed", trackedDownload.DownloadItem.Title, reason);
                trackedDownload.State = TrackedDownloadState.DownloadFailed;
                _failedDownloadService.MarkAsFailed(trackedDownload.DownloadItem.DownloadId, false, reason);
                RemoveFailedFromClient(trackedDownload);
                return;
            }

            _logger.Warn("Download {0}: {1}. Mangarr did not grab it, so it is not marked as failed or removed; use Manual Import or remove it from the download client", trackedDownload.DownloadItem.Title, reason);
            trackedDownload.State = TrackedDownloadState.ImportFailed;
        }

        // The fork fails unusable payloads (chapter rips, ebook-only, audiobook-only) itself.
        // DownloadEventHub only removes a failed item once the client would let it go (seed
        // goals met), so a still-seeding torrent lingered in the download category forever.
        // A payload judged unusable has no reason to seed: remove it the way the queue's
        // Remove + Blocklist does, honouring the client's Remove Failed Downloads setting.
        private void RemoveFailedFromClient(TrackedDownload trackedDownload)
        {
            if (trackedDownload.DownloadItem.Removed)
            {
                return;
            }

            try
            {
                var downloadClient = _downloadClientProvider.Get(trackedDownload.DownloadClient);

                if (downloadClient?.Definition is DownloadClientDefinition definition && definition.RemoveFailedDownloads)
                {
                    downloadClient.RemoveItem(trackedDownload.DownloadItem, true);
                    trackedDownload.DownloadItem.Removed = true;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to remove failed download {0} from the download client", trackedDownload.DownloadItem.Title);
            }
        }

        private bool HasGrabbedHistory(TrackedDownload trackedDownload)
        {
            return _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId)
                .Any(h => h.EventType == EntityHistoryEventType.Grabbed);
        }

        private void SetImportItem(TrackedDownload trackedDownload)
        {
            trackedDownload.ImportItem = _provideImportItemService.ProvideImportItem(trackedDownload.DownloadItem, trackedDownload.ImportItem);
        }

        private bool ValidatePath(TrackedDownload trackedDownload)
        {
            var downloadItemOutputPath = trackedDownload.ImportItem.OutputPath;

            if (downloadItemOutputPath.IsEmpty)
            {
                trackedDownload.Warn("Download doesn't contain intermediate path, Skipping.");
                return false;
            }

            if ((OsInfo.IsWindows && !downloadItemOutputPath.IsWindowsPath) ||
                (OsInfo.IsNotWindows && !downloadItemOutputPath.IsUnixPath))
            {
                trackedDownload.Warn("[{0}] is not a valid local path. You may need a Remote Path Mapping.", downloadItemOutputPath);
                return false;
            }

            return true;
        }
    }
}
