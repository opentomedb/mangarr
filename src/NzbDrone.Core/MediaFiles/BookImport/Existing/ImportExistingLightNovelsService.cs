using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.BookImport.Existing
{
    public class ImportExistingReport
    {
        public List<string> Adopted { get; } = new List<string>();
        public List<string> Held { get; } = new List<string>();
        public List<string> Covered { get; } = new List<string>();
        public List<string> Skipped { get; } = new List<string>();
        public List<string> Unmatched { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
    }

    // Adopt what the maintainer already owns (one copy each, 2026-09-20; the copy-in of 2026-09, D12, before
    // it): per light-novel entry, (1) find its Calibre series by name / alias, map series_index ->
    // volume, and register the EPUB of every volume whose Ebook edition has none; (2) find its
    // Audiobookshelf items by ASIN / series name / title + sequence and register their audio files
    // as the parts of the volume's Audio edition; then rescan the entry. Every match registers the
    // original IN PLACE through the normal import pipeline (a BookFile row with Home / Adopted /
    // CalibreId), never copies: a copy left over from the copy-in era is verified against its
    // original, moved to the holding folder inside the root and forgotten -- never deleted (the maintainer's
    // Plex-content rule). Unmatched items are listed in the report for the maintainer, never guessed.
    public class ImportExistingLightNovelsService : IExecute<ImportExistingLightNovelsCommand>
    {
        // The sequence may be a range ("#1-2"): a pack (2026-09-17, D4).
        private static readonly Regex SeriesSequenceRegex = new Regex(@"^(?<name>.+?)\s*#(?<seq>\d+(?:\.\d+)?(?:\s*[-–—]\s*\d+(?:\.\d+)?)?)\s*$", RegexOptions.Compiled);
        private static readonly Regex TitleVolumeRegex = new Regex(@"^(?<name>.+?)[,:\-]?\s+(?:Vol\.?|Volume|Book)\s*(?<seq>\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // "The Beginning After the End, Books 3-4", "Overlord, Vol. 1-2" -> (name, "3-4"): a pack (2026-09-17, D4).
        // The name is the segment before the token: ABS titles a pack "<pack title>: <series>, Books 3-4".
        // Pack numbers are whole and at most three digits (MangaVolumeParser's rule): "Vol. 5 - 2019" is no pack.
        private static readonly Regex TitleRangeRegex = new Regex(@"(?<name>[^:]+?)[,:\-]?\s+(?:Vol(?:ume)?s?\.?|Books?)\s*(?<seq>\d{1,3}\s*[-–—]\s*\d{1,3})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TitleBareNumberRegex = new Regex(@"^(?<name>.+?)\s+(?<seq>\d+(?:\.\d+)?)\s*(?:[:\-–—].*)?$", RegexOptions.Compiled);
        private static readonly Regex LightNovelTagRegex = new Regex(@"\s*\((?:Light Novel|LN|Novel)\)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TrailingSeriesRegex = new Regex(@"\s+Series$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly RegexReplace PadNumbers = new RegexReplace(@"\d+", n => n.Value.PadLeft(9, '0'), RegexOptions.Compiled);

        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly ICalibreContentServerClient _calibre;
        private readonly ICalibreProxy _calibreProxy;
        private readonly ILightNovelCalibreSettings _calibreSettings;
        private readonly IAudiobookshelfClient _audiobookshelf;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IImportApprovedBooks _importApprovedBooks;
        private readonly IMediaFileService _mediaFileService;
        private readonly IAdoptedAudioSyncService _adoptedSync;
        private readonly ICalibreTitleSyncService _calibreTitleSync;
        private readonly IDiskProvider _diskProvider;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IDiskScanService _diskScanService;
        private readonly IAudibleCatalogService _audibleCatalogService;
        private readonly ICoveredVolumeService _coveredVolumeService;
        private readonly ILightNovelStorage _storage;
        private readonly Logger _logger;

        public ImportExistingLightNovelsService(IAuthorService authorService,
                                                IBookService bookService,
                                                ICalibreContentServerClient calibre,
                                                ICalibreProxy calibreProxy,
                                                ILightNovelCalibreSettings calibreSettings,
                                                IAudiobookshelfClient audiobookshelf,
                                                IMakeImportDecision importDecisionMaker,
                                                IImportApprovedBooks importApprovedBooks,
                                                IMediaFileService mediaFileService,
                                                IAdoptedAudioSyncService adoptedSync,
                                                ICalibreTitleSyncService calibreTitleSync,
                                                IDiskProvider diskProvider,
                                                IAppFolderInfo appFolderInfo,
                                                IManageCommandQueue commandQueueManager,
                                                IDiskScanService diskScanService,
                                                IAudibleCatalogService audibleCatalogService,
                                                ICoveredVolumeService coveredVolumeService,
                                                ILightNovelStorage storage,
                                                Logger logger)
        {
            _authorService = authorService;
            _bookService = bookService;
            _calibre = calibre;
            _calibreProxy = calibreProxy;
            _calibreSettings = calibreSettings;
            _audiobookshelf = audiobookshelf;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedBooks = importApprovedBooks;
            _mediaFileService = mediaFileService;
            _adoptedSync = adoptedSync;
            _calibreTitleSync = calibreTitleSync;
            _diskProvider = diskProvider;
            _appFolderInfo = appFolderInfo;
            _commandQueueManager = commandQueueManager;
            _diskScanService = diskScanService;
            _audibleCatalogService = audibleCatalogService;
            _coveredVolumeService = coveredVolumeService;
            _storage = storage;
            _logger = logger;
        }

        public void Execute(ImportExistingLightNovelsCommand message)
        {
            var authors = (message.AuthorId.HasValue
                    ? new List<Author> { _authorService.GetAuthor(message.AuthorId.Value) }
                    : _authorService.GetAllAuthors())
                .Where(a => a != null && a.Library == LibraryType.LightNovel)
                .ToList();

            // A manga entry is never touched: the command says so and stops.
            if (message.AuthorId.HasValue && authors.Empty())
            {
                _logger.ProgressInfo("Series {0} is not a light-novel entry; nothing to import", message.AuthorId.Value);
            }

            foreach (var author in authors)
            {
                _logger.ProgressInfo("Adopting existing files for {0}", author.Name);

                var report = ImportAuthor(author);

                // The rescan runs here, inline (the command holds disk access), so the hold below
                // is released only once the entry folder is current (the copies held out of it).
                // A scan that throws is an Errors line (the hold stays), never a lost report: the
                // Held section is the per-item deletion-approval list and must reach the file.
                if (author.Path.IsNotNullOrWhiteSpace())
                {
                    try
                    {
                        _diskScanService.Scan(new List<string> { author.Path }, FilterFilesType.Matched, false, new List<int> { author.Id });
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Rescan of {0} failed after adoption", author.Name);
                        report.Errors.Add($"Rescan: {ex.Message}");
                    }
                }

                // Copy-in hold (2026-09-16, D3): a report with errors (a source pass failed, a copy
                // that failed verification, an import the pipeline refused) keeps the hold -- and the
                // search -- for a clean re-run. Decided before the shelf sync below: its failure is
                // reported, never a reason to hold.
                var hold = report.Errors.Any();

                SyncCalibreTitles(author, report);
                SyncShelf(author, report);
                WriteReport(author, report);

                if (hold)
                {
                    _logger.Warn("{0}: adoption reported {1} error(s); the hold stays until a clean run", author.Name, report.Errors.Count);
                    continue;
                }

                if (author.CopyInPending)
                {
                    _authorService.ClearCopyInPending(author);
                    _logger.Info("{0}: adoption complete, searches released", author.Name);
                }

                if (message.SearchAfter)
                {
                    _commandQueueManager.Push(new MissingBookSearchCommand(author.Id));
                }
            }
        }

        // One display title, fix round 1 (2026-09-23, L5): a newly adopted calibre book keeps its
        // old title until someone runs the whole-library command otherwise. The adoption itself sets
        // the calibre title/sort for every Calibre-homed row of the entry (adopted AND grabbed) --
        // same CalibreTitleSyncService as the command, title/sort only (C2). A row that fails is an
        // Errors line, never a held search.
        private void SyncCalibreTitles(Author author, ImportExistingReport report)
        {
            if (_storage.EbookHome != LightNovelHome.Calibre)
            {
                return;
            }

            var rows = _mediaFileService.GetFilesByAuthor(author.Id)
                .Where(f => f.Home == FileHome.Calibre)
                .ToList();

            if (rows.Empty())
            {
                return;
            }

            try
            {
                if (!_calibreTitleSync.Sync(author, rows, _calibreSettings.ForConfig(), out var failure))
                {
                    report.Errors.Add($"Calibre title: {failure}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Calibre title sync failed for {0}", author.Name);
                report.Errors.Add($"Calibre title: {ex.Message}");
            }
        }

        // One copy each (2026-09-20, ruling); ABS titles match calibre (2026-09-23): the adoption
        // itself aligns the Audiobookshelf shelf once -- every Audiobooks-homed audio row of the
        // entry, adopted AND grabbed, as the rescan left them, go to the API sync (series, sequence,
        // title, subtitle). Unconditional of WriteAudioTags: that setting governs file writes, this
        // is the shelf. A shelf the sync could not align (ABS not answering, a patch refused -- the
        // sync warns and says why) is an Errors line for the maintainer, never a hold.
        private void SyncShelf(Author author, ImportExistingReport report)
        {
            if (_storage.AudioHome != LightNovelHome.Audiobookshelf)
            {
                return;
            }

            var audio = _mediaFileService.GetFilesByAuthor(author.Id)
                .Where(f => f.Home == FileHome.Audiobooks)
                .ToList();

            if (audio.Empty())
            {
                return;
            }

            try
            {
                if (!_adoptedSync.Sync(author, audio, out var failure))
                {
                    report.Errors.Add($"Audiobookshelf shelf: {failure}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Audiobookshelf shelf sync failed for {0}", author.Name);
                report.Errors.Add($"Audiobookshelf shelf: {ex.Message}");
            }
        }

        public ImportExistingReport ImportAuthor(Author author)
        {
            var report = new ImportExistingReport();
            var names = SeriesNames(author);
            var volumes = _bookService.GetBooksByAuthor(author.Id);

            // Light-novel storage (2026-09-22): a kind whose home is the entry folder has nothing to
            // adopt from calibre / Audiobookshelf. A Skipped line, never an Errors line: an error would
            // keep the copy-in hold (and every search) for a user who simply has neither installed.
            if (_storage.EbookHome == LightNovelHome.Calibre)
            {
                try
                {
                    ImportFromCalibre(author, volumes, names, report);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Calibre content server pass failed for {0}", author.Name);
                    report.Errors.Add($"Calibre: {ex.Message}");
                }
            }
            else
            {
                report.Skipped.Add("calibre: ebooks go to the entry folder (Settings → Media Management → Light Novel Storage → Ebooks)");
            }

            if (_storage.AudioHome == LightNovelHome.Audiobookshelf)
            {
                try
                {
                    ImportFromAudiobookshelf(author, volumes, names, report);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Audiobookshelf pass failed for {0}", author.Name);
                    report.Errors.Add($"Audiobookshelf: {ex.Message}");
                }
            }
            else
            {
                report.Skipped.Add("audiobookshelf: audiobooks go to the entry folder (Settings → Media Management → Light Novel Storage → Audiobooks)");
            }

            _logger.Info("{0}: {1} adopted, {2} held, {3} covered, {4} skipped, {5} unmatched, {6} errors", author.Name, report.Adopted.Count, report.Held.Count, report.Covered.Count, report.Skipped.Count, report.Unmatched.Count, report.Errors.Count);

            return report;
        }

        // The entry's name and every alias, normalized the way the catalogue matches (case,
        // punctuation and a "(Light Novel)" tag do not count).
        private static HashSet<string> SeriesNames(Author author)
        {
            var aliases = author.Metadata?.Value?.Aliases ?? new List<string>();

            // "(Second edition)", "(novel series)": the catalogue's disambiguation is not part of Calibre's series name.
            // normalized FIRST: a native-script alias normalizes to "" and must not enter the set
            return aliases.Prepend(MangaVolumeParser.StripParentheticals(author.Name)).Prepend(author.Name)
                .Select(NormalizeName)
                .Where(n => n.IsNotNullOrWhiteSpace())
                .ToHashSet();
        }

        private static string NormalizeName(string name)
        {
            return GcdMetadataService.Normalize(LightNovelTagRegex.Replace(name ?? string.Empty, " "));
        }

        // An empty normalized candidate (a native-script title) never matches anything.
        private static bool IsOurs(HashSet<string> names, string candidate)
        {
            var key = NormalizeName(candidate);

            return key.IsNotNullOrWhiteSpace() && names.Contains(key);
        }

        private void ImportFromCalibre(Author author, List<Book> volumes, HashSet<string> names, ImportExistingReport report)
        {
            // "(Second edition)": the catalogue's disambiguation is not part of Calibre's series name, so the bare name is searched too.
            var aliases = (author.Metadata?.Value?.Aliases ?? new List<string>())
                .Prepend(MangaVolumeParser.StripParentheticals(author.Name))
                .Prepend(author.Name)
                .Where(n => n.IsNotNullOrWhiteSpace())
                .Distinct()
                .ToList();

            var calibreIds = new SortedSet<int>();

            foreach (var name in aliases)
            {
                foreach (var id in _calibre.SearchSeries(name))
                {
                    calibreIds.Add(id);
                }
            }

            // volume id -> the Calibre book that claimed it in this run (a second book at the same
            // series_index is a duplicate, never a second copy)
            var claimed = new Dictionary<int, int>();

            // One bad book (a 404 EPUB, a null title, a full disk) must not abort the rest of the
            // series: each is its own unit of work with its own Errors line.
            foreach (var calibreId in calibreIds)
            {
                CalibreContentBook calibreBook = null;

                try
                {
                    calibreBook = _calibre.GetBook(calibreId);

                    if (calibreBook != null)
                    {
                        ImportCalibreBook(author, volumes, names, report, claimed, calibreBook);
                    }
                }
                catch (Exception ex)
                {
                    var title = calibreBook?.Title ?? $"calibre-{calibreId}";

                    _logger.Warn(ex, "Calibre book {0} '{1}' failed for {2}", calibreId, title, author.Name);
                    report.Errors.Add($"calibre #{calibreId} '{title}': {ex.Message}");
                }
            }
        }

        private void ImportCalibreBook(Author author, List<Book> volumes, HashSet<string> names, ImportExistingReport report, Dictionary<int, int> claimed, CalibreContentBook calibreBook)
        {
            var calibreId = calibreBook.Id;
            var title = calibreBook.Title ?? $"calibre-{calibreId}";

            // The content server's series: search is a contains-search; only an exact
            // (normalized) name or alias counts.
            if (!IsOurs(names, calibreBook.Series))
            {
                report.Unmatched.Add($"calibre #{calibreId} '{title}': series '{calibreBook.Series}' is not {author.Name}");
                return;
            }

            // EPUB, else the AZW3 fallback (final review C1), else a PDF (LN PDF, 2026-09-22)
            if (CalibreFormats.TrackedLightNovelFormat(calibreBook.Formats) == null)
            {
                report.Skipped.Add($"calibre #{calibreId} '{title}': no EPUB, AZW3 or PDF format");
                return;
            }

            if (!calibreBook.SeriesIndex.HasValue)
            {
                report.Unmatched.Add($"calibre #{calibreId} '{title}': no series index");
                return;
            }

            var volume = FindVolume(volumes, calibreBook.SeriesIndex.Value);

            if (volume == null)
            {
                report.Unmatched.Add($"calibre #{calibreId} '{title}': no volume {calibreBook.SeriesIndex.Value} in {author.Name}");
                return;
            }

            var edition = volume.EditionOf(MediaType.Ebook);

            if (edition == null)
            {
                report.Unmatched.Add($"calibre #{calibreId} '{title}': {volume} has no Ebook edition");
                return;
            }

            // An unmonitored edition is the add-time "EPUB" checkbox left unticked (or the per-edition
            // toggle); adopting into it would have the import re-monitor it (final review I2).
            if (!edition.Monitored)
            {
                report.Skipped.Add($"calibre #{calibreId} '{title}': {volume} Ebook edition not monitored");
                return;
            }

            if (claimed.TryGetValue(volume.Id, out var first))
            {
                report.Skipped.Add($"calibre #{calibreId} '{title}': duplicate in Calibre (book id {first}) — first copy kept");
                return;
            }

            claimed[volume.Id] = calibreId;

            // The tracked format's path as Mangarr sees it (/books/...): calibre's own answer through
            // Readarr's proxy (the content-server client above reads no paths). The format list
            // already named an EPUB, AZW3 or PDF; a book that then has no path for it is calibre's
            // inconsistency, not a skip. format_metadata is keyed "epub" by the live server (checked
            // 2026-09-20), so keys are matched without case (CalibreFormats.TrackedLightNovelFormat).
            var formats = _calibreProxy.GetBook(calibreId, _calibreSettings.ForConfig()).Formats;
            var key = CalibreFormats.TrackedLightNovelFormat(formats?.Keys);
            var ebook = key != null ? formats[key] : null;

            if (ebook == null || ebook.Path.IsNullOrWhiteSpace())
            {
                report.Errors.Add($"calibre #{calibreId} '{title}': calibre gives no path for its EPUB/AZW3/PDF");
                return;
            }

            // LN PDF final review (2026-09-22): the profile opt-in has to hold at adoption time too --
            // a calibre book tracked by its PDF (no EPUB or AZW3) only adopts once the entry's ebook
            // profile actually wants Ebook PDF (the default ships id 8 unticked). Skipped, not
            // Errors: the book is fine as it is, there is just nothing to do until the profile says so.
            var ebookProfile = author.QualityProfileFor(MediaType.Ebook);
            if (string.Equals(key, "PDF", StringComparison.OrdinalIgnoreCase) && ebookProfile != null && !ebookProfile.Allows(Quality.EbookPdf))
            {
                report.Skipped.Add($"calibre #{calibreId} '{title}': PDF only — tick Ebook PDF in the light-novel profile to adopt it");
                return;
            }

            Adopt(author, volume, edition, new List<string> { ebook.Path }, FileHome.Calibre, calibreId, $"calibre #{calibreId} '{title}'", report);
        }

        private void ImportFromAudiobookshelf(Author author, List<Book> volumes, HashSet<string> names, ImportExistingReport report)
        {
            // volume id -> the Audiobookshelf item that claimed it in this run (a second item for the
            // same volume never restarts the part numbering into the first item's targets)
            var claimed = new Dictionary<int, string>();

            // Audible's products for the entry (2026-09-17, D4), asked at most once per run, the first time
            // an item whose title parses to nothing (or to ours without a number) reaches the range check.
            // A cache hit after the refresh this copy-in follows; null = Audible did not answer.
            // Preferred Edition (2026-09-24, D8; M14 fix round 1): a non-English entry asks by its English
            // anchor name (AnchorName; null for every English entry, which keeps asking by its Name), and an
            // edition with no English audio (every Audio edition minted unmonitored) does not ask at all --
            // no products, as when Audible does not answer. English: unchanged.
            var audible = new Lazy<List<AudibleProduct>>(() => AudioSkipped(author, volumes)
                ? null
                : _audibleCatalogService.GetSeries(author.Metadata.Value.AnchorName ?? author.Name, names));

            // One bad item (a folder missing from the mount, a full disk) must not abort the rest.
            foreach (var item in _audiobookshelf.GetLibraryItems())
            {
                try
                {
                    ImportAudiobookshelfItem(author, volumes, names, report, claimed, audible, item);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Audiobookshelf item {0} '{1}' failed for {2}", item.Id, item.Title, author.Name);
                    report.Errors.Add($"abs '{item.Title}': {ex.Message}");
                }
            }
        }

        // Preferred Edition (2026-09-24, D8): a non-English entry whose Audio editions are all unmonitored
        // -- how BookInfoProxy mints an edition not 1:1 with the English line. English entries never
        // qualify (a user who unticked every audiobook still gets today's behaviour) and load no editions.
        private static bool AudioSkipped(Author author, List<Book> volumes)
        {
            if (EditionLanguages.IsEnglish(author.Metadata.Value.EditionLanguage))
            {
                return false;
            }

            var audio = volumes.Select(v => v.EditionOf(MediaType.Audio)).Where(e => e != null).ToList();

            return audio.Any() && audio.All(e => !e.Monitored);
        }

        private void ImportAudiobookshelfItem(Author author, List<Book> volumes, HashSet<string> names, ImportExistingReport report, Dictionary<int, string> claimed, Lazy<List<AudibleProduct>> audible, AudiobookshelfItem item)
        {
            var candidates = ParseSeriesAndSequenceCandidates(item);
            var candidate = candidates.FirstOrDefault(c => IsOurs(names, c.Name));
            var range = candidate.Range;

            // Publisher's packs (2026-09-17, D4): "The Beginning After the End: Publisher's Pack" carries no
            // number anywhere -- Audible's cached product of that title does ("1-2"), and the products
            // answer the entry's own name, so a title match is ours. Asked only when the title parses to
            // nothing at all, or to ours without a number: a stranger that parses is left alone.
            if (!range.HasValue && (candidate.Name != null || candidates.Count == 0))
            {
                range = AudiblePackRange(audible.Value, item.Title);
            }

            // ASIN first (B3b, 2026-09-18): the Audible product ABS matched the item to is the one on
            // exactly one Audio edition of the entry (B1's identity) -- that volume, whatever the title
            // says. A pack the title (or Audible's cached product) spans FROM that volume keeps its range,
            // so the covered marks are written as for a name-matched pack; anything else is the volume
            // alone. Everything from the edition on is the one copy/claim path.
            var volume = MatchByAsin(volumes, item.Asin);

            if (volume != null)
            {
                var pack = range.HasValue && Math.Abs(range.Value.From - volume.VolumeNumber) < 0.001 && range.Value.To > range.Value.From;

                _logger.Debug("abs '{0}': matched by ASIN {1} → Vol. {2}{3}", item.Title, item.Asin, volume.VolumeNumber, pack ? $" (pack {range.Value.From}-{range.Value.To})" : string.Empty);
                range = pack ? range : (volume.VolumeNumber, volume.VolumeNumber);
            }
            else
            {
                if (!range.HasValue)
                {
                    // Another series entirely is not "unmatched" -- it is simply not ours.
                    if (candidate.Name != null)
                    {
                        report.Unmatched.Add($"abs '{item.Title}': matches {author.Name} but carries no volume number");
                    }

                    return;
                }

                // A pack (from < to) copies onto its first volume and covers the rest, as a spanning import does.
                var sequence = range.Value.From;
                volume = FindVolume(volumes, sequence);

                if (volume == null)
                {
                    report.Unmatched.Add($"abs '{item.Title}': no volume {sequence} in {author.Name}");
                    return;
                }
            }

            var edition = volume.EditionOf(MediaType.Audio);

            if (edition == null)
            {
                report.Unmatched.Add($"abs '{item.Title}': {volume} has no Audiobook edition");
                return;
            }

            // An unmonitored edition is the add-time "Audiobook" checkbox left unticked (or the
            // per-edition toggle); adopting into it would have the import re-monitor it and flip
            // AudioAvailable (final review I2).
            if (!edition.Monitored)
            {
                report.Skipped.Add($"abs '{item.Title}': {volume} Audiobook edition not monitored");
                return;
            }

            if (claimed.TryGetValue(volume.Id, out var first))
            {
                report.Skipped.Add($"abs '{item.Title}': duplicate Audiobookshelf item {item.Id} for {volume} — first item {first} kept");
                return;
            }

            var hostPath = MapPath(item.Path);

            if (hostPath == null)
            {
                report.Errors.Add($"abs '{item.Title}': path '{item.Path}' is not under Audiobookshelf's mapped folder or escapes it, cannot reach it");
                return;
            }

            var sources = (item.IsFile ? new[] { hostPath } : _diskProvider.GetFiles(hostPath, true))
                .Where(f => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => PadNumbers.Replace(f))
                .ToList();

            if (!sources.Any())
            {
                report.Errors.Add($"abs '{item.Title}': no audio files under {hostPath}");
                return;
            }

            claimed[volume.Id] = item.Id;

            // A pack whose first volume already holds audio that is not a copy of this item marks
            // nothing (review I3): Adopt skips it as already adopted, or keeps a copy that fails
            // verification, and either way the marks wait for this item's own adoption.
            if (Adopt(author, volume, edition, sources, FileHome.Audiobooks, 0, $"abs '{item.Title}'", report))
            {
                MarkCovered(volumes, volume, range.Value, item, report);
            }
        }

        // One copy each (2026-09-20): the ending of both passes. files: the original's file paths as
        // Mangarr sees them (calibre: the EPUB path under /books; ABS: MapPath'd audio files in
        // PadNumbers order). The original is registered in place through the normal pipeline --
        // GetImportDecisions identified as this very edition, then Import -- so it gets the same row,
        // events and statistics as a grab, and the specs (UpgradeSpecification,
        // AdoptedEditionSpecification) still have their say. True only when the pipeline took it.
        private bool Adopt(Author author, Book volume, Edition edition, List<string> files, FileHome home, int calibreId, string what, ImportExistingReport report)
        {
            var existing = edition.BookFiles.Value;

            if (existing.Any(f => f.Home != FileHome.Entry))
            {
                report.Skipped.Add($"{what}: {volume} already adopted");
                return false;
            }

            // Copies from the copy-in era: verified against the original, then held, then forgotten.
            if (existing.Any())
            {
                var copies = existing.OrderBy(f => f.Part).ToList();
                var problem = Verify(copies, files);
                if (problem != null)
                {
                    report.Errors.Add($"{what}: copy kept, original failed verification: {problem}");
                    return false;
                }

                // every holding path resolved before the first move: a row that is not under the entry
                // folder (HoldingPath throws) leaves the whole set where it is, never half of it
                var held = copies.Select(c => HoldingPath(author, c.Path)).ToList();

                for (var i = 0; i < copies.Count; i++)
                {
                    var copy = copies[i];
                    _diskProvider.CreateFolder(Path.GetDirectoryName(held[i]));
                    _diskProvider.MoveFile(copy.Path, held[i]);
                    // all three paths: the maintainer approves deletions from this line
                    report.Held.Add($"{copy.Path} -> {held[i]} (replaced by {files[i]}; {copy.Size} / {SizeOf(files, i)})");
                    _mediaFileService.Delete(copy, DeleteMediaFileReason.Manual);
                }
            }

            // the edition's lazy file list still holds the copy rows just deleted; the import specs
            // (UpgradeSpecification, AdoptedEditionSpecification) read it -- hand them the truth
            edition.BookFiles = new LazyLoaded<List<BookFile>>(new List<BookFile>());

            // ImportDecisionMaker.EnsureData resolves GetBestRootFolder(path) -- null for a /books or
            // Audiobookshelf-tree path -- only when the author's QualityProfileId is 0, and an existing
            // light-novel entry always has a profile, so the off-root path never reaches it.
            var infos = files.Select(f => _diskProvider.GetFileInfo(f)).ToList();
            var decisions = _importDecisionMaker.GetImportDecisions(infos,
                new IdentificationOverrides { Author = author, Book = volume, Edition = edition },
                null,
                new ImportDecisionMakerConfig { Filter = FilterFilesType.None, NewDownload = false, IncludeExisting = true, AddNewAuthors = false });

            var part = 1;
            foreach (var d in decisions.OrderBy(d => PadNumbers.Replace(d.Item.Path)))
            {
                d.Item.Home = home;
                d.Item.Adopted = true;
                d.Item.CalibreId = calibreId;
                d.Item.Part = part++;
                d.Item.PartCount = decisions.Count;
            }

            var results = _importApprovedBooks.Import(decisions, false);
            var rejected = results.Where(r => r.Errors.Any()).ToList();
            if (rejected.Any())
            {
                report.Errors.Add($"{what}: {string.Join("; ", rejected.SelectMany(r => r.Errors).Distinct())}");
                return false;
            }

            report.Adopted.Add($"{what} -> {string.Join(", ", files)}");
            return true;
        }

        // A copy set stands in for its original only when the original is all there: every original
        // exists and opens, the counts agree, and each copy (by part) has its original's extension
        // (by file order). The one-line reason, or null when the copies can go.
        private string Verify(List<BookFile> copies, List<string> originals)
        {
            foreach (var original in originals)
            {
                if (!_diskProvider.FileExists(original))
                {
                    return $"{original} does not exist";
                }

                try
                {
                    using (_diskProvider.OpenReadStream(original))
                    {
                    }
                }
                catch (Exception ex)
                {
                    return $"{original} could not be read: {ex.Message}";
                }
            }

            if (copies.Count != originals.Count)
            {
                return $"{copies.Count} copies for {originals.Count} original files";
            }

            for (var i = 0; i < copies.Count; i++)
            {
                if (!Path.GetExtension(copies[i].Path).Equals(Path.GetExtension(originals[i]), StringComparison.OrdinalIgnoreCase))
                {
                    return $"{copies[i].Path} is not a {Path.GetExtension(originals[i])} like {originals[i]}";
                }
            }

            return null;
        }

        private long SizeOf(List<string> files, int index)
        {
            return _diskProvider.GetFileSize(files[index]);
        }

        // The holding folder for a copy-in era copy: <root>/.adopted-holding/<entry folder>/<copy path
        // relative to the entry>, e.g. /lightnovels/.adopted-holding/Mushoku Tensei/Mushoku Tensei -
        // Vol. 1/Mushoku Tensei - Vol 001.m4b. Inside the root on purpose (the same filesystem: a
        // rename, never a copy), and safe there: DiskScanService.ExcludedSubFoldersRegex
        // ((?:\\|\/|^)(?:extras|@eadir|extrafanart|plex versions|\.[^\\/]+)(?:\\|\/)) drops every
        // dot-folder from the walk, so nothing under it ever reaches the decision maker or the
        // unmatched-row insert. Reclaiming the space is the maintainer's call, never automated.
        private string HoldingPath(Author author, string copyPath)
        {
            var root = _diskProvider.GetParentFolder(author.Path);

            return Path.Combine(root, LightNovelHomes.HoldingFolder, root.GetRelativePath(author.Path), author.Path.GetRelativePath(copyPath));
        }

        // A pack covers every other volume in its range (2026-09-17, D4): the marks a tracked import
        // writes, here for an adopted item. A single (from == to) marks nothing; the service leaves a
        // volume that already carries a mark or owns audio alone and hands back what it marked.
        private void MarkCovered(List<Book> volumes, Book carrier, (double From, double To) range, AudiobookshelfItem item, ImportExistingReport report)
        {
            if (range.To <= range.From)
            {
                return;
            }

            var covered = volumes.Where(v => v.Id != carrier.Id && v.VolumeNumber >= range.From && v.VolumeNumber <= range.To).ToList();

            foreach (var edition in _coveredVolumeService.MarkCoveredByImport(carrier, covered, $"abs '{item.Title}'"))
            {
                var number = covered.First(v => v.Id == edition.BookId).VolumeNumber;
                report.Covered.Add($"{item.Title} → Vol. {number} by Vol. {carrier.VolumeNumber}");
            }
        }

        // The cached Audible pack whose title the item's title is, or begins with ("The Beginning After
        // the End: Publisher's Pack 2: <subtitle>"), and its range ("3-4"). The longest title wins:
        // "Publisher's Pack 2" begins with "Publisher's Pack" too. No products (Audible did not answer,
        // or has no packs) = no match.
        private static (double From, double To)? AudiblePackRange(List<AudibleProduct> products, string title)
        {
            var key = NormalizeName(title);

            if (products == null || key.IsNullOrWhiteSpace())
            {
                return null;
            }

            return products
                .Select(p => (Key: NormalizeName(p.Title), Range: AudibleSequence.Parse(p.Sequence)))
                .Where(p => p.Key.IsNotNullOrWhiteSpace() && p.Range.HasValue && p.Range.Value.To > p.Range.Value.From)
                .Where(p => key == p.Key || key.StartsWith(p.Key + " ", StringComparison.Ordinal))
                .OrderByDescending(p => p.Key.Length)
                .Select(p => p.Range)
                .FirstOrDefault();
        }

        // Copy-in (2026-09-16): ABS series fields are free text ("Solo Leveling Series #2") -- when the series name is not ours, the item's own title ("Solo Leveling, Vol. 2") still can be. Candidates in order: series field, then title with a Vol. token, then title with a bare number ("Sword Art Online 1: Aincrad").
        // A sequence is a range (From < To) for a pack, From == To for a single (2026-09-17, D4).
        private static List<(string Name, (double From, double To)? Range)> ParseSeriesAndSequenceCandidates(AudiobookshelfItem item)
        {
            var candidates = new List<(string Name, (double From, double To)? Range)>();

            // "Overlord, Vol. 5 (Light Novel)" -> (Overlord, 5); "Overlord, Vol. 1-2" -> (Overlord, 1-2): the range
            // first, since a pack title also matches the single-volume token as "Vol. 1"
            var strippedTitle = LightNovelTagRegex.Replace(item.Title ?? string.Empty, " ");
            var titleRange = TitleRangeRegex.Match(strippedTitle);
            var title = TitleVolumeRegex.Match(strippedTitle);
            var titleSequence = titleRange.Success ? ParseSequence(titleRange.Groups["seq"].Value)
                              : title.Success ? ParseSequence(title.Groups["seq"].Value)
                              : null;

            // "Overlord #5" -> (Overlord, 5); "Overlord Series #5" -> (Overlord, 5); a bare series name takes the title's number
            if (item.SeriesName.IsNotNullOrWhiteSpace())
            {
                var series = SeriesSequenceRegex.Match(item.SeriesName);

                candidates.Add(series.Success
                    ? (TrailingSeriesRegex.Replace(series.Groups["name"].Value, string.Empty), ParseSequence(series.Groups["seq"].Value))
                    : (TrailingSeriesRegex.Replace(item.SeriesName, string.Empty), titleSequence));
            }

            if (titleRange.Success)
            {
                candidates.Add((titleRange.Groups["name"].Value.Trim(), titleSequence));
            }

            if (title.Success)
            {
                candidates.Add((title.Groups["name"].Value, titleSequence));
            }

            // "Sword Art Online 1: Aincrad" -> (Sword Art Online, 1): last, so a Vol. token or the series field always wins
            var bare = TitleBareNumberRegex.Match(strippedTitle);

            if (bare.Success)
            {
                candidates.Add((bare.Groups["name"].Value, ParseSequence(bare.Groups["seq"].Value)));
            }

            return candidates.DistinctBy(c => (NormalizeName(c.Name), c.Range)).ToList();
        }

        // "5" -> (5, 5); "3.5" -> (3.5, 3.5); "1-2" -> (1, 2): Audible's own sequence shapes, which the
        // regexes above capture.
        private static (double From, double To)? ParseSequence(string value)
        {
            return AudibleSequence.Parse(value);
        }

        private static Book FindVolume(List<Book> volumes, double number)
        {
            return volumes.FirstOrDefault(b => Math.Abs(b.VolumeNumber - number) < 0.001);
        }

        // The volume whose Audio edition carries this ASIN (case and surrounding space do not count), when
        // exactly one does: none, or two (a data fault), decide nothing and the name parse takes over.
        private static Book MatchByAsin(List<Book> volumes, string asin)
        {
            if (asin.IsNullOrWhiteSpace())
            {
                return null;
            }

            var matches = volumes
                .Where(v => string.Equals(v.EditionOf(MediaType.Audio)?.Asin?.Trim(), asin.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        // Audiobookshelf's container path -> Mangarr's (the pair in Settings; blank = the same path).
        // ABS never emits "..", but with a blank pair LightNovelPathMap.Map returns the path
        // unchanged before its own "." / ".." check runs, so a "." or ".." segment is refused here
        // first, regardless of mapping -- the same way a result outside a SET pair's mapped folder
        // is refused before anything is read.
        private string MapPath(string absPath)
        {
            if (absPath != null && absPath.Split('/').Any(s => s == "." || s == ".."))
            {
                return null;
            }

            return _storage.MapFromAudiobookshelf(absPath);
        }

        private void WriteReport(Author author, ImportExistingReport report)
        {
            var text = new StringBuilder();

            text.AppendLine($"Adopt existing light-novel files for {author.Name} (author {author.Id}) -- {DateTime.UtcNow:u}");
            text.AppendLine($"Originals were registered in place, never copied, moved or deleted. A leftover copy of an adopted file was moved to {LightNovelHomes.HoldingFolder} under the root folder (never deleted) once its original checked out; deleting it to reclaim the space is up to you -- Mangarr never does.");
            text.AppendLine();
            AppendSection(text, "Adopted", report.Adopted);
            AppendSection(text, "Held", report.Held);
            AppendSection(text, "Covered", report.Covered);
            AppendSection(text, "Skipped", report.Skipped);
            AppendSection(text, "Unmatched", report.Unmatched);
            AppendSection(text, "Errors", report.Errors);

            var path = Path.Combine(_appFolderInfo.GetLogFolder(), $"import-existing-{author.Id}.txt");

            _diskProvider.WriteAllText(path, text.ToString());
            _logger.Info("Report written to {0}", path);
        }

        private static void AppendSection(StringBuilder text, string title, List<string> lines)
        {
            text.AppendLine($"{title} ({lines.Count})");

            foreach (var line in lines)
            {
                text.AppendLine("  " + line);
            }

            text.AppendLine();
        }
    }
}
