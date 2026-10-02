using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Books
{
    // Line safety (2026-09-28): one line of the series' work the series could switch to, and what the switch
    // would do -- the name the series takes, how many of its files sit on a volume the line also has (they
    // stay on that volume), which volumes have no counterpart (their files stay where they are, never
    // deleted), and why the switch is blocked, if it is.
    public class LineSwitchOption
    {
        public string TomeLineId { get; set; }
        public string Name { get; set; }
        public string Language { get; set; }
        public int VolumeCount { get; set; }
        public string Publisher { get; set; }
        public string SpinOffOf { get; set; }
        public bool IsMain { get; set; }
        public int FilesMoving { get; set; }
        public int FilesKept { get; set; }
        public List<string> KeptVolumes { get; set; } = new List<string>();

        // Review fixes (2026-09-28): the volumes the line has that the series does not (I2, the confirm
        // dialog's count), and a manga line with no AniList id (M1: the refresh re-resolves by title).
        public int VolumesAdded { get; set; }
        public bool NoAniListMatch { get; set; }
        public string BlockedReason { get; set; }

        // The template BlockedReason was built from, for the UI language at the API boundary
        // (PreferredEditionController). Null for the refresh-running reason, which is already localized.
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ServerText BlockedReasonText { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public List<int> MovingFileIds { get; set; } = new List<int>();

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public HashSet<double> Volumes { get; set; } = new HashSet<double>();

        // A series of another edition: the English line it is anchored to (null for an English series).
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string AnchorName { get; set; }
    }

    public class LineSwitchChoices
    {
        public string CurrentTomeLineId { get; set; }
        public string CurrentLineName { get; set; }
        public List<LineSwitchOption> Options { get; set; } = new List<LineSwitchOption>();
    }

    public interface ILineSwitchService
    {
        // The work's other lines in the series' market and library class, each previewed. Empty when the
        // series is not bound to a catalogue line (or the line has no siblings, or there is no catalogue).
        LineSwitchChoices Choices(Author author);

        // The prompt's yes after a switch: RetagFiles for the given files (only this series' own) and, for a
        // light novel, SyncLightNovelTitles. Null when queued; the reason nothing was queued otherwise.
        ServerText SyncExternal(Author author, List<int> bookFileIds);
    }

    // Line safety (2026-09-28): Switch Line (Fix Match). A series bound to the wrong line of its work (the
    // Trapped in a Dating Sim spin-off entry that took the main series' files) is rebound to another line of
    // the same work, market and library class. A volume's identity is "<series id>-v<N>", which a refresh by
    // id never changes: every file stays on the volume with its number, and the queued refresh re-mints that
    // volume from the new line; a volume the new line lacks keeps its files (RefreshBookService never deletes a
    // book with files). What the switch writes, from a copy of the metadata as Change Edition does:
    //   - the line id, and the line's name as the stored name (the one explicit action that overrides "a stored
    //     name never changes"): an English series is resolved by its name, so the name is what makes the next
    //     refresh bind the new line (the preview blocks a line whose name finds another line); a series of
    //     another edition is named by the add path's rule (EditionDisplayName: AniList's romaji for Japanese,
    //     else the line's local title; the English anchor when there is neither);
    //   - the line's AniList id (null = the refresh searches), no aliases (the old line's would keep matching
    //     its releases), the collected flag cleared (as Change Edition);
    //   - on the volumes the new line also has: the old line's per-volume identity cleared (subtitle, ISBN,
    //     Audible product, covers, blurb, page count, an Audible covered mark) -- the refresh's ratchets keep a
    //     stored value when the new line has none, and a stale subtitle would become a wrong calibre title.
    // Not touched: the folder and file names on disk, metadata pins (left under the old name, not moved: they
    // are the old line's), app-owned covered marks, monitoring. No retag or calibre/ABS write happens unless the
    // user answers the prompt (SyncExternal): the queued refresh skips the tag sync (SkipTagSync) even with
    // Write Audio/Book Tags = Sync, and queues no search for the volumes it adds (the scheduled missing search
    // picks up the monitored ones). Review fixes (2026-09-28, I4): the writes run volume data -> binding ->
    // refresh, so a failure part-way leaves the old binding with cleared per-volume data (the next refresh
    // re-fills it from the old line), never a new binding over the old line's volume identity.
    public class LineSwitchService : ILineSwitchService, IExecute<SwitchLineCommand>
    {
        private readonly IAuthorService _authorService;
        private readonly IAuthorMetadataService _authorMetadataService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IGcdMetadataService _gcdMetadataService;
        private readonly IEditionResolver _editionResolver;
        private readonly IMangaSeriesMetadataProvider _mangaMetadataProvider;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly ILocalizationService _localizationService;
        private readonly IAniListService _aniListService;
        private readonly IBookAddedService _bookAddedService;
        private readonly Logger _logger;

        public LineSwitchService(IAuthorService authorService,
                                 IAuthorMetadataService authorMetadataService,
                                 IBookService bookService,
                                 IEditionService editionService,
                                 IMediaFileService mediaFileService,
                                 IGcdMetadataService gcdMetadataService,
                                 IEditionResolver editionResolver,
                                 IMangaSeriesMetadataProvider mangaMetadataProvider,
                                 IManageCommandQueue commandQueueManager,
                                 ILocalizationService localizationService,
                                 IAniListService aniListService,
                                 IBookAddedService bookAddedService,
                                 Logger logger)
        {
            _authorService = authorService;
            _authorMetadataService = authorMetadataService;
            _bookService = bookService;
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _gcdMetadataService = gcdMetadataService;
            _editionResolver = editionResolver;
            _mangaMetadataProvider = mangaMetadataProvider;
            _commandQueueManager = commandQueueManager;
            _localizationService = localizationService;
            _aniListService = aniListService;
            _bookAddedService = bookAddedService;
            _logger = logger;
        }

        public LineSwitchChoices Choices(Author author)
        {
            var meta = author.Metadata.Value;
            var choices = new LineSwitchChoices { CurrentTomeLineId = meta.TomeLineId };

            if (meta.TomeLineId.IsNullOrWhiteSpace() || !_gcdMetadataService.Available)
            {
                return choices;
            }

            var current = _gcdMetadataService.FindSeriesByTomeId(meta.TomeLineId);

            if (current == null)
            {
                return choices;
            }

            choices.CurrentLineName = WorkLines.DisplayName(current, author.Library);

            var work = WorkLines.Of(_gcdMetadataService, current);
            var siblings = WorkLines.Siblings(current, work);

            if (!siblings.Any())
            {
                return choices;
            }

            var editions = _editionService.GetEditionsByAuthor(author.Id).ToDictionary(e => e.Id);
            var books = _bookService.GetBooksByAuthor(author.Id).ToDictionary(b => b.Id);
            var files = _mediaFileService.GetFilesByAuthor(author.Id)
                .Select(f => (File: f, Volume: editions.TryGetValue(f.EditionId, out var e) && books.TryGetValue(e.BookId, out var b) ? b.VolumeNumber : (double?)null))
                .ToList();

            var existingVolumes = books.Values.Select(b => b.VolumeNumber).ToHashSet();
            var english = EditionLanguages.IsEnglish(meta.EditionLanguage);
            var refreshRunning = RefreshRunning(author.Id);
            var currentCollected = files.Any() && EditionChangeChecks.IsCollected(current, author.Library, _editionResolver, _gcdMetadataService);
            var others = _authorService.GetAllAuthors().Where(a => a.Id != author.Id).ToList();

            foreach (var line in siblings.OrderByDescending(l => l.IsMain).ThenBy(l => l.Name))
            {
                var anchor = english ? null : EnglishAnchor(line, author.Library);
                var name = english ? WorkLines.DisplayName(line, author.Library) : EditionName(line, meta.EditionLanguage, anchor, author.Library, meta.EditionFallback);

                var option = new LineSwitchOption
                {
                    TomeLineId = line.TomeId,
                    Name = name,
                    AnchorName = anchor.IsNullOrWhiteSpace() || anchor == name ? null : anchor,
                    NoAniListMatch = author.Library == LibraryType.Manga && !line.AnilistId.HasValue,
                    Language = line.Language,
                    VolumeCount = line.VolumeCount,
                    Publisher = line.Publisher.IsNotNullOrWhiteSpace() ? line.Publisher : null,
                    SpinOffOf = WorkLines.SpinOffOf(line, work, author.Library),
                    IsMain = line.IsMain,
                    Volumes = VolumesOf(line, out var lineVolumes)
                };

                foreach (var (file, volume) in files)
                {
                    if (volume.HasValue && option.Volumes.Contains(volume.Value))
                    {
                        option.MovingFileIds.Add(file.Id);
                    }
                    else
                    {
                        option.FilesKept++;

                        if (volume.HasValue)
                        {
                            option.KeptVolumes.Add(MangaVolumeParser.Format(volume.Value));
                        }
                    }
                }

                option.FilesMoving = option.MovingFileIds.Count;
                option.VolumesAdded = option.Volumes.Count(v => !existingVolumes.Contains(v));
                option.KeptVolumes = option.KeptVolumes.Distinct().OrderBy(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToList();

                if (refreshRunning)
                {
                    var reason = _localizationService.GetLocalizedString("EditionRefreshRunning");
                    option.BlockedReason = reason.IsNotNullOrWhiteSpace() && reason != "EditionRefreshRunning" ? reason : EditionChangeChecks.RefreshRunningReason;
                }
                else
                {
                    Block(option, BlockedReason(author, line, lineVolumes, option, others, files.Count, currentCollected));
                }

                choices.Options.Add(option);
            }

            return choices;
        }

        private static void Block(LineSwitchOption option, ServerText reason)
        {
            if (reason != null)
            {
                option.BlockedReason = reason.English;
                option.BlockedReasonText = reason;
            }
        }

        private ServerText BlockedReason(Author author, GcdSeries line, List<GcdVolume> lineVolumes, LineSwitchOption option, List<Author> others, int fileCount, bool currentCollected)
        {
            // Two entries on one line would share its releases.
            var bound = others.FirstOrDefault(a => a.Metadata?.Value?.TomeLineId == line.TomeId);

            if (bound != null)
            {
                return new ServerText("{0} is already bound to this line", bound.Name);
            }

            // The name is the switch (a refresh keeps it): another series holding it would clash on the clean
            // name, and two series of one name confuse every release match.
            var namesake = EditionChangeChecks.Namesake(author, option.Name, _authorService);

            if (namesake != null)
            {
                return new ServerText("Another series is already named \"{0}\"", namesake.Name);
            }

            // An English series is resolved by its name on every refresh (the six-argument path never reads the
            // stored line): a name that finds another line would undo the switch on the next refresh. A series
            // of another edition is resolved by its bound line and needs no such check.
            if (EditionLanguages.IsEnglish(author.Metadata.Value.EditionLanguage) &&
                _gcdMetadataService.FindSeriesByTitle(option.Name, author.Library)?.TomeId != line.TomeId)
            {
                return new ServerText("The catalogue finds another line by this line's name, so a refresh would not keep the switch");
            }

            // D9 (Change Edition's rule): volume N of a collected edition is not volume N of a single-volume
            // line, so files cannot keep their numbers across it.
            if (fileCount > 0 && (currentCollected || EditionChangeChecks.IsCollected(line, author.Library, _editionResolver, _gcdMetadataService, lineVolumes)))
            {
                return new ServerText("Volume numbering differs between the two lines and {0} file(s) are attached", fileCount);
            }

            return null;
        }

        private HashSet<double> VolumesOf(GcdSeries line, out List<GcdVolume> volumes)
        {
            volumes = _gcdMetadataService.GetVolumes(line.GcdSeriesId) ?? new List<GcdVolume>();

            var numbers = volumes
                .Select(v => (double)v.VolumeNumber)
                .Where(n => n > 0)
                .ToHashSet();

            // A line the catalogue lists no volume rows for still has its count.
            return numbers.Any() ? numbers : Enumerable.Range(1, line.VolumeCount).Select(n => (double)n).ToHashSet();
        }

        // A refresh (or a rebind pass, or a Change Edition) already running would write its stale name and
        // binding back over the switch.
        private bool RefreshRunning(int authorId)
        {
            return EditionChangeChecks.RefreshRunningFor(_commandQueueManager, authorId) ||
                   (_commandQueueManager.GetStarted() ?? new List<CommandModel>()).Any(c => c.Body is ReResolveEditionCommand change && change.AuthorIds?.Contains(authorId) == true);
        }

        public void Execute(SwitchLineCommand message)
        {
            Author author;

            try
            {
                author = _authorService.GetAuthor(message.AuthorId);
            }
            catch (ModelNotFoundException)
            {
                author = null;
            }

            if (author == null)
            {
                _logger.Warn("Switch line: series {0} no longer exists", message.AuthorId);
                message.SetResultMessage(new ServerText("Not switched: {0}", new ServerText("The series no longer exists")));
                return;
            }

            var option = Choices(author).Options.FirstOrDefault(o => o.TomeLineId == message.TomeLineId);

            var reason = option == null
                ? new ServerText("Not a line of this series' work in its market")
                : option.BlockedReasonText ?? (option.BlockedReason != null ? new ServerText("A refresh is running for this series; try again when it finishes") : null);

            if (reason == null && RefreshRunning(author.Id))
            {
                reason = new ServerText("A refresh is running for this series; try again when it finishes");
            }

            if (reason != null)
            {
                _logger.Warn("Switch line {0} -> {1}: {2}", author.Name, message.TomeLineId, reason.English);
                message.SetResultMessage(new ServerText("Not switched: {0}", reason));
                return;
            }

            var line = _gcdMetadataService.FindSeriesByTomeId(option.TomeLineId);
            var meta = author.Metadata.Value;
            var oldName = author.Name;

            // I4: the old line's volume identity goes first, then the binding, then the refresh.
            ClearOldLineVolumeData(author, option.Volumes);

            // Written from a copy: meta is the live instance in AuthorService's cache (as Change Edition).
            var write = meta.JsonClone();
            write.TomeLineId = line.TomeId;
            write.Name = option.Name;
            write.SortName = option.Name.ToLowerInvariant();
            write.NameLastFirst = option.Name;
            write.SortNameLastFirst = option.Name.ToLowerInvariant();
            write.AniListId = line.AnilistId;
            write.Aliases = new List<string>();
            write.EditionCollected = false;
            write.AnchorName = option.AnchorName;

            _authorMetadataService.Upsert(write);

            meta.TomeLineId = write.TomeLineId;
            meta.Name = write.Name;
            meta.SortName = write.SortName;
            meta.NameLastFirst = write.NameLastFirst;
            meta.SortNameLastFirst = write.SortNameLastFirst;
            meta.AniListId = write.AniListId;
            meta.Aliases = write.Aliases;
            meta.EditionCollected = write.EditionCollected;
            meta.AnchorName = write.AnchorName;

            _mangaMetadataProvider.ForgetLookup(oldName);
            _mangaMetadataProvider.ForgetLookup(option.Name);

            // I1/I2: the refresh writes no tags and searches nothing; set before the push (it runs elsewhere).
            _bookAddedService.SkipNextRefreshSearch(author.Id);
            var refresh = _commandQueueManager.Push(new RefreshAuthorCommand(author.Id) { SkipTagSync = true });

            var movingFiles = _mediaFileService.GetFilesByAuthor(author.Id).Where(f => option.MovingFileIds.Contains(f.Id)).ToList();

            message.Switched = true;
            message.MovedBookFileIds = option.MovingFileIds;
            message.KeptVolumes = option.KeptVolumes;
            message.RefreshCommandId = refresh?.Id;
            message.OfferExternalSync = author.Library == LibraryType.LightNovel && movingFiles.Any(f => f.Home == FileHome.Calibre || f.Home == FileHome.Audiobooks);
            message.SetResultMessage(new ServerText("Switched to {0}: {1} file(s) on matching volumes, {2} kept", option.Name, option.FilesMoving, option.FilesKept));

            _logger.Info("Switch line {0} -> \"{1}\" ({2}): {3} file(s) on matching volumes, {4} kept{5}",
                oldName,
                option.Name,
                line.TomeId,
                option.FilesMoving,
                option.FilesKept,
                option.KeptVolumes.Any() ? $" (volumes {string.Join(", ", option.KeptVolumes)} have no counterpart and keep their files)" : string.Empty);
        }

        // A series of another edition is resolved by its English anchor (AniList presentation, aliases): the
        // English line of the same original as the new line, else the English line of its role (main or not);
        // none -> no anchor.
        private string EnglishAnchor(GcdSeries line, LibraryType library)
        {
            var english = WorkLines.Of(_gcdMetadataService, line)
                .Where(c => EditionLanguages.IsEnglish(c.Language) && c.VolumeCount >= 1)
                .Where(c => GcdMetadataService.IsLightNovel(c.Medium) == GcdMetadataService.IsLightNovel(line.Medium))
                .OrderByDescending(c => EditionResolver.IsCounterpart(c, line) || EditionResolver.IsCounterpart(line, c))
                .ThenByDescending(c => c.IsMain == line.IsMain)
                .ThenBy(c => c.GcdSeriesId)
                .FirstOrDefault();

            return WorkLines.DisplayName(english, library);
        }

        // I3: the add path's name for a series of that edition (EditionDisplayName): a native-script edition
        // (ja / ko / zh) -> AniList's romaji for the line's AniList id, other languages -> the line's local
        // title; either missing -> the English anchor; no anchor either -> the line's own display name.
        // Final fix wave I2: a fallback series (AuthorMetadata.EditionFallback; Switch Line keeps the flag) is named
        // by the add path's fallback rule instead -- AniList English, romaji, the line's Latin name; never local_name.
        private string EditionName(GcdSeries line, string editionLanguage, string anchor, LibraryType library, bool fallback)
        {
            var ani = (fallback || EditionLanguages.IsNativeScript(editionLanguage)) && line.AnilistId.HasValue ? _aniListService.GetById(line.AnilistId.Value) : null;
            var name = fallback
                ? MangaSeriesMetadataProvider.FallbackDisplayName(line, ani, anchor, library)
                : MangaSeriesMetadataProvider.EditionDisplayName(line, editionLanguage, ani, anchor, library);

            return name.IsNotNullOrWhiteSpace() ? name : WorkLines.DisplayName(line, library);
        }

        // The refresh's ratchets keep a stored value when the remote has none: on the volumes the new line also
        // has, the old line's per-volume identity must go first or it rides onto the new line's volume.
        private void ClearOldLineVolumeData(Author author, HashSet<double> volumes)
        {
            var books = _bookService.GetBooksByAuthor(author.Id).Where(b => volumes.Contains(b.VolumeNumber)).ToList();

            if (!books.Any())
            {
                return;
            }

            var bookIds = books.Select(b => b.Id).ToHashSet();
            var editions = _editionService.GetEditionsByAuthor(author.Id).Where(e => bookIds.Contains(e.BookId)).ToList();

            foreach (var book in books)
            {
                book.Subtitle = null;
            }

            foreach (var edition in editions)
            {
                edition.Isbn13 = null;
                edition.Asin = null;
                edition.AudiobookTitle = null;
                edition.AudiobookSubtitle = null;
                edition.RuntimeMinutes = null;
                edition.AudioReleaseDate = null;
                edition.Overview = string.Empty;
                edition.PageCount = 0;
                edition.Images = new List<MediaCover.MediaCover>();

                if (!CoveredSources.IsAppOwned(edition.CoveredSource))
                {
                    edition.CoveredByVolume = null;
                    edition.CoveredSource = null;
                }
            }

            _bookService.UpdateMany(books);
            _editionService.UpdateMany(editions);
        }

        public ServerText SyncExternal(Author author, List<int> bookFileIds)
        {
            if (author.Library != LibraryType.LightNovel)
            {
                return new ServerText("Only a light novel's files live in calibre or Audiobookshelf");
            }

            var queuedOrRunning = (_commandQueueManager.All() ?? new List<CommandModel>())
                .Where(c => c.Status == CommandStatus.Queued || c.Status == CommandStatus.Started)
                .Any(c => (c.Body is RefreshAuthorCommand refresh && (!refresh.AuthorId.HasValue || refresh.AuthorId.Value == author.Id)) ||
                          (c.Body is BulkRefreshAuthorCommand bulk && (bulk.AuthorIds == null || bulk.AuthorIds.Contains(author.Id))) ||
                          (c.Body is SwitchLineCommand other && other.AuthorId == author.Id));

            // The titles and tags are the refreshed volumes' -- written before the refresh they would be the old ones.
            if (queuedOrRunning)
            {
                return new ServerText("A refresh is running for this series; try again when it finishes");
            }

            var own = _mediaFileService.GetFilesByAuthor(author.Id).Select(f => f.Id).ToHashSet();
            var files = (bookFileIds ?? new List<int>()).Where(own.Contains).Distinct().ToList();

            if (files.Any())
            {
                _commandQueueManager.Push(new RetagFilesCommand(author.Id, files));
            }

            _commandQueueManager.Push(new SyncLightNovelTitlesCommand(author.Id));

            return null;
        }
    }
}
