using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Books
{
    public class EditionPreview
    {
        public int AuthorId { get; set; }
        public string Name { get; set; }
        public string FromLanguage { get; set; }
        public string ToLanguage { get; set; }
        public string FromLineName { get; set; }
        public string ToLineName { get; set; }
        public int FromVolumes { get; set; }
        public int ToVolumes { get; set; }
        public bool Compatible { get; set; }
        public string BlockedReason { get; set; }

        // i18n leftovers (2026-09-28): the template and arguments BlockedReason was built from (a nested
        // language-name ServerText, exactly as AddAuthorService/AddBookService carry theirs), for the UI
        // language at the API boundary (PreferredEditionController). Null for a reason built elsewhere
        // (EditionChangeChecks, or the bulk-preview "(rename from Edit Series)" suffix), which stays English.
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ServerText BlockedReasonText { get; set; }

        public int FilesAffected { get; set; }
        public string NewName { get; set; }
        public string ToTomeLineId { get; set; }

        // Fix round 1 (2026-09-24): why the rename to NewName cannot happen (destination taken), or null.
        // The edition change itself is still allowed; a run with Rename ticked skips the series.
        public string RenameBlockedReason { get; set; }

        // Fix round 1: the edition does not change -- only a rename back to the edition's name (e.g. after
        // "back to English without Rename"). Applies only when Rename is ticked.
        public bool RenameOnly { get; set; }
    }

    // Preferred Edition (2026-09-24, M13 fix round 1): the checks the preview shows and the run repeats
    // before writing anything. A refresh already running for the series would save its stale name and
    // path over the change (and dedupe the refresh the change queues); a rename onto a taken folder,
    // path, name or pin key would merge two series' files or pins.
    public static class EditionChangeChecks
    {
        public const string RefreshRunningReason = "A refresh is running for this series; try again when it finishes";

        // Polish (2026-09-24): the rebind pass (ReResolveMetadata with Rebind) loads its authors when it starts
        // -- one (AuthorId) or all (none) -- and upserts copies of their metadata, so it can write a stale
        // name or binding back just like a refresh. Its non-rebind path only clears book dates and queues a
        // RefreshAuthorCommand (counted when that starts), so it does not count here.
        public static bool RefreshRunningFor(IManageCommandQueue commandQueue, int authorId)
        {
            return (commandQueue.GetStarted() ?? new List<CommandModel>()).Any(c =>
                (c.Body is RefreshAuthorCommand refresh && (!refresh.AuthorId.HasValue || refresh.AuthorId.Value == authorId)) ||
                (c.Body is BulkRefreshAuthorCommand bulk && (bulk.AuthorIds == null || bulk.AuthorIds.Contains(authorId))) ||
                (c.Body is ReResolveMetadataCommand rebind && rebind.Rebind && (!rebind.AuthorId.HasValue || rebind.AuthorId.Value == authorId)));
        }

        // The folder the series would move to under its new name (the naming format's author folder, next
        // to the current one); null when the series has no path.
        public static string NewPath(Author author, string newName, IBuildFileNames fileNameBuilder)
        {
            if (author.Path.IsNullOrWhiteSpace())
            {
                return null;
            }

            var meta = author.Metadata.Value;
            var renamed = new Author
            {
                Id = author.Id,
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = meta.ForeignAuthorId,
                    Name = newName,
                    SortName = newName.ToLowerInvariant(),
                    NameLastFirst = newName,
                    SortNameLastFirst = newName.ToLowerInvariant(),
                    Disambiguation = meta.Disambiguation
                }
            };

            return Path.Combine(Path.GetDirectoryName(author.Path.TrimEnd('/', '\\')), fileNameBuilder.GetAuthorFolder(renamed));
        }

        public static string RenameBlockedReason(Author author, string newName, string newPath, IDiskProvider diskProvider, IAuthorService authorService, IMetadataOverridesService metadataOverrides)
        {
            if (newPath.IsNotNullOrWhiteSpace() && !newPath.PathEquals(author.Path))
            {
                if (diskProvider.FolderExists(newPath))
                {
                    return $"A folder already exists at {newPath}";
                }

                if (authorService.AuthorPathExists(newPath))
                {
                    return $"Another series already uses {newPath}";
                }
            }

            var namesake = authorService.FindByName(newName, author.Library);

            if (namesake != null && namesake.Id != author.Id)
            {
                return $"Another series is already named \"{namesake.Name}\"";
            }

            var oldKey = LibraryTypes.PinKey(author.Name, author.Library);
            var newKey = LibraryTypes.PinKey(newName, author.Library);

            if (!oldKey.Equals(newKey, StringComparison.OrdinalIgnoreCase) && metadataOverrides.Get(newKey)?.Any() == true)
            {
                return $"Metadata pins already exist under \"{newKey}\"";
            }

            return null;
        }
    }

    public interface IEditionPreviewService
    {
        EditionPreview Preview(Author author, string language);
    }

    // Preferred Edition (2026-09-24, spec §4 / D9): what changing a series' edition would do -- the line
    // it moves to, volume counts, whether the numbering is the same unit (neither line a collected
    // edition: the exporter nulls composition on non-omnibus lines, so volume N is volume N), the files
    // attached, and the name it could take. Different numbering with files attached is blocked in v1:
    // JP v1-v2 files would sit on DE Band 1-2, and DiscoverExtraVolumes would re-mint orphans from disk.
    public class EditionPreviewService : IEditionPreviewService
    {
        private readonly IGcdMetadataService _gcdMetadataService;
        private readonly IEditionResolver _editionResolver;
        private readonly IMediaFileService _mediaFileService;
        private readonly IAniListService _aniListService;
        private readonly IManageCommandQueue _commandQueue;
        private readonly IDiskProvider _diskProvider;
        private readonly IAuthorService _authorService;
        private readonly IMetadataOverridesService _metadataOverrides;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly ILocalizationService _localizationService;

        public EditionPreviewService(IGcdMetadataService gcdMetadataService,
                                     IEditionResolver editionResolver,
                                     IMediaFileService mediaFileService,
                                     IAniListService aniListService,
                                     IManageCommandQueue commandQueue,
                                     IDiskProvider diskProvider,
                                     IAuthorService authorService,
                                     IMetadataOverridesService metadataOverrides,
                                     IBuildFileNames fileNameBuilder,
                                     ILocalizationService localizationService)
        {
            _gcdMetadataService = gcdMetadataService;
            _editionResolver = editionResolver;
            _mediaFileService = mediaFileService;
            _aniListService = aniListService;
            _commandQueue = commandQueue;
            _diskProvider = diskProvider;
            _authorService = authorService;
            _metadataOverrides = metadataOverrides;
            _fileNameBuilder = fileNameBuilder;
            _localizationService = localizationService;
        }

        public EditionPreview Preview(Author author, string language)
        {
            var meta = author.Metadata.Value;
            var to = EditionLanguages.IsEnglish(language) ? EditionLanguages.English : language.Trim();
            var from = EditionLanguages.Of(meta);
            var anchorName = meta.AnchorName.IsNotNullOrWhiteSpace() ? meta.AnchorName : author.Name;

            // Final fix round I2 (2026-09-24): an English series' bound line IS its anchor. The provider binds
            // through more than the name (the AniList title, the catalogue hint, an arc subtitle), so a name
            // lookup can miss the line ("No French edition") or find another one (a spurious en -> en change).
            var bound = meta.TomeLineId.IsNotNullOrWhiteSpace() ? _gcdMetadataService.FindSeriesByTomeId(meta.TomeLineId) : null;
            var anchor = from == EditionLanguages.English && bound != null ? bound : _gcdMetadataService.FindSeriesByTitle(anchorName, author.Library);
            var current = bound ?? anchor;
            var target = to == EditionLanguages.English
                ? anchor
                : _editionResolver.Resolve(anchor, new EditionRequest { Language = to }, author.Library, anchorName)?.Line;

            var preview = new EditionPreview
            {
                AuthorId = author.Id,
                Name = author.Name,
                FromLanguage = from,
                ToLanguage = to,
                FromLineName = LineName(current),
                ToLineName = LineName(target),
                FromVolumes = current?.VolumeCount ?? 0,
                ToVolumes = target?.VolumeCount ?? 0,
                FilesAffected = _mediaFileService.GetFilesByAuthor(author.Id).Count,
                ToTomeLineId = target?.TomeId
            };

            // Fix round 1 (I1): a refresh already running would write its stale name and path back.
            if (EditionChangeChecks.RefreshRunningFor(_commandQueue, author.Id))
            {
                var reason = _localizationService.GetLocalizedString("EditionRefreshRunning");
                preview.BlockedReason = reason.IsNotNullOrWhiteSpace() && reason != "EditionRefreshRunning" ? reason : EditionChangeChecks.RefreshRunningReason;
                return preview;
            }

            if (target == null)
            {
                // Final fix round I2: an English series the catalogue has no line of is still the English edition.
                // i18n leftovers (2026-09-28): both are a ServerText with the language name nested, exactly as
                // AddBookService/AddAuthorService build the reused "No {0} edition..." one.
                var text = from == EditionLanguages.English && to == EditionLanguages.English
                    ? new ServerText("Already the {0} edition", new ServerText(EditionLanguages.Name(to)))
                    : new ServerText("No {0} edition of this series in the catalogue", new ServerText(EditionLanguages.Name(to)));
                preview.BlockedReason = text.English;
                preview.BlockedReasonText = text;
                return preview;
            }

            var newName = NewName(author, target, to, anchorName);
            preview.NewName = newName.IsNullOrWhiteSpace() || newName == author.Name ? null : newName;

            // Fix round 1: the same edition is a no-op -- unless the series is still under another name that
            // a rename restores (after "back to English without Rename"): then it is a rename-only change.
            // Final fix round I2: English to English is the same edition whatever the line ids say.
            if (from == to && (to == EditionLanguages.English || current?.GcdSeriesId == target.GcdSeriesId))
            {
                if (preview.NewName == null || author.Library != LibraryType.Manga)
                {
                    // i18n leftovers (2026-09-28): same ServerText as the target == null branch above.
                    var text = new ServerText("Already the {0} edition", new ServerText(EditionLanguages.Name(to)));
                    preview.BlockedReason = text.English;
                    preview.BlockedReasonText = text;
                    return preview;
                }

                preview.RenameOnly = true;
            }

            // Polish: a rename-only change stays on its line -- no numbering changes, even on a collected edition.
            preview.Compatible = preview.RenameOnly ||
                                 (current != null && !_editionResolver.IsCollected(current) && !_editionResolver.IsCollected(target));

            if (!preview.Compatible && preview.FilesAffected > 0)
            {
                preview.BlockedReason = $"Volume numbering differs between the two editions and {preview.FilesAffected} file(s) are attached";
            }

            // Fix round 1 (I2): only manga is ever renamed (plan A7); a taken destination blocks the rename.
            if (preview.NewName != null && author.Library == LibraryType.Manga)
            {
                preview.RenameBlockedReason = EditionChangeChecks.RenameBlockedReason(author, preview.NewName,
                    EditionChangeChecks.NewPath(author, preview.NewName, _fileNameBuilder), _diskProvider, _authorService, _metadataOverrides);

                if (preview.RenameOnly && preview.RenameBlockedReason != null)
                {
                    preview.BlockedReason ??= preview.RenameBlockedReason;
                }
            }

            return preview;
        }

        // D3/D7, ruling S3: the name the edition would give a new series, by the add path's own rule
        // (EditionDisplayName: the line's local title, AniList's romaji for Japanese). Its fallback is the
        // anchor, which for a non-English edition means "no local title" -- no new name. Back to English
        // the anchor IS the name to return to (a rename works in both directions).
        private string NewName(Author author, GcdSeries target, string to, string anchorName)
        {
            if (to == EditionLanguages.English)
            {
                return anchorName;
            }

            // Only Japanese needs AniList (the romaji name): a bulk preview to any other language costs no call.
            var aniListId = author.Metadata.Value.AniListId;
            var ani = to == "ja" && aniListId.HasValue ? _aniListService.GetById(aniListId.Value) : null;
            var name = MangaSeriesMetadataProvider.EditionDisplayName(target, to, ani, anchorName, author.Library);

            return name == anchorName ? null : name;
        }

        private static string LineName(GcdSeries line)
        {
            return line == null ? null : line.LocalName.IsNotNullOrWhiteSpace() ? line.LocalName : line.Name;
        }
    }
}
