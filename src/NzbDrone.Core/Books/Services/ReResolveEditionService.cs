using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Books
{
    // Preferred Edition (2026-09-24, spec §4): change a series' edition. Re-previews each series (the
    // catalogue may have moved since the user looked), skips blocked ones (D9), writes the binding from a
    // copy (as Fix Match / the rebind pass do), and queues a refresh -- which fetches the new line by id:
    // ISBNs, dates, covers and blurbs change, files stay attached, no file is re-searched (a "replace with
    // the French file" search would drain a private tracker) -- volumes the new line adds are handled as any refresh's new
    // volumes: BookAddedService searches the monitored, released ones (line safety review, 2026-09-28). A rename (manga only, plan A7) moves the pins keyed by
    // the display name and the folder.
    // Fix round 1 (2026-09-24): nothing is written for a series a refresh is already refreshing (I1), or
    // whose rename destination is taken (I2, checked again here -- the preview may be stale); the writes
    // run pins-copy -> binding -> pins-remove -> path + move (I3), so a failure part-way leaves duplicate
    // pins, never orphaned ones, and never a folder move for a name that was not saved.
    public class ReResolveEditionService : IExecute<ReResolveEditionCommand>
    {
        private readonly IAuthorService _authorService;
        private readonly IAuthorMetadataService _authorMetadataService;
        private readonly IEditionPreviewService _previewService;
        private readonly IMangaSeriesMetadataProvider _mangaMetadataProvider;
        private readonly IMetadataOverridesService _metadataOverrides;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IDiskProvider _diskProvider;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public ReResolveEditionService(IAuthorService authorService,
                                       IAuthorMetadataService authorMetadataService,
                                       IEditionPreviewService previewService,
                                       IMangaSeriesMetadataProvider mangaMetadataProvider,
                                       IMetadataOverridesService metadataOverrides,
                                       IBuildFileNames fileNameBuilder,
                                       IDiskProvider diskProvider,
                                       IManageCommandQueue commandQueueManager,
                                       Logger logger)
        {
            _authorService = authorService;
            _authorMetadataService = authorMetadataService;
            _previewService = previewService;
            _mangaMetadataProvider = mangaMetadataProvider;
            _metadataOverrides = metadataOverrides;
            _fileNameBuilder = fileNameBuilder;
            _diskProvider = diskProvider;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Execute(ReResolveEditionCommand message)
        {
            var ids = message.AuthorIds ?? new List<int>();
            var changed = 0;
            var blocked = 0;
            var failed = 0;

            foreach (var id in ids)
            {
                Author author = null;

                try
                {
                    author = _authorService.GetAuthor(id);

                    var reason = BlockedReason(author, message, out var preview, out var renaming, out var newPath);

                    if (reason != null)
                    {
                        blocked++;
                        _logger.Warn("Change edition {0}: {1}", author.Name, reason);
                        continue;
                    }

                    reason = Apply(author, preview, renaming, newPath);

                    if (reason != null)
                    {
                        blocked++;
                        _logger.Warn("Change edition {0}: {1}", author.Name, reason);
                        continue;
                    }

                    changed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.Warn(ex, "Change edition {0}: failed — {1}", author?.Name ?? id.ToString(), ex.Message);
                }
            }

            message.SetResultMessage(new ServerText("{0} changed, {1} blocked, {2} failed", changed, blocked, failed));
            _logger.Info("Change edition to {0}: {1} changed, {2} blocked, {3} failed of {4}", EditionLanguages.Name(message.Language), changed, blocked, failed, ids.Count);
        }

        // Every check runs before the first write (I3 "validate").
        private string BlockedReason(Author author, ReResolveEditionCommand message, out EditionPreview preview, out bool renaming, out string newPath)
        {
            preview = null;
            renaming = false;
            newPath = null;

            if (EditionChangeChecks.RefreshRunningFor(_commandQueueManager, author.Id))
            {
                return EditionChangeChecks.RefreshRunningReason;
            }

            preview = _previewService.Preview(author, message.Language);

            if (preview.BlockedReason != null)
            {
                return preview.BlockedReason;
            }

            renaming = message.Rename && author.Library == LibraryType.Manga && preview.NewName.IsNotNullOrWhiteSpace();

            if (preview.RenameOnly && !renaming)
            {
                // i18n leftovers (2026-09-28): same ServerText as EditionPreviewService's "Already the {0}
                // edition" sites, reusing its key; this one only reaches the Warn log below (English, always).
                return new ServerText("Already the {0} edition", new ServerText(EditionLanguages.Name(preview.ToLanguage))).English;
            }

            if (!renaming)
            {
                return null;
            }

            newPath = EditionChangeChecks.NewPath(author, preview.NewName, _fileNameBuilder);

            return EditionChangeChecks.RenameBlockedReason(author, preview.NewName, newPath, _diskProvider, _authorService, _metadataOverrides);
        }

        // Null when written; else why nothing was.
        private string Apply(Author author, EditionPreview preview, bool renaming, string newPath)
        {
            var meta = author.Metadata.Value;
            var oldName = author.Name;

            // Follow-up round (KR/CN consumer): a fallback or anchorless series moving to English takes the English
            // line's name as its anchor (EditionPreview.ToAnchorName); everything else keeps today's anchor.
            var anchorName = preview.ToAnchorName.IsNotNullOrWhiteSpace() ? preview.ToAnchorName
                : meta.AnchorName.IsNotNullOrWhiteSpace() ? meta.AnchorName : oldName;
            var newName = renaming ? preview.NewName : oldName;

            // Written from a copy: meta is the live instance in AuthorService's cache (see the rebind pass).
            var write = meta.JsonClone();
            write.EditionLanguage = EditionLanguages.IsEnglish(preview.ToLanguage) ? null : preview.ToLanguage;
            write.TomeLineId = preview.ToTomeLineId;

            // 2026-09-26 (migration 058): the old line's collected flag must not ride along with the new
            // line; false is the safe side (a numbered collected release is rejected) until the queued
            // refresh recomputes it from the new line.
            write.EditionCollected = false;

            // KR/CN consumer (2026-09-29): a change of edition is the user's choice, never a fallback.
            write.EditionFallback = false;

            // The anchor is kept whenever the name is not it (a localized name, renamed or kept). A refresh
            // never writes it (create-only, M5); this is the one place an existing entry's anchor changes.
            write.AnchorName = newName == anchorName ? null : anchorName;

            if (renaming)
            {
                write.Name = newName;
                write.SortName = newName.ToLowerInvariant();
                write.NameLastFirst = newName;
                write.SortNameLastFirst = newName.ToLowerInvariant();
            }

            // Pins are keyed by the display name (LibraryTypes.PinKey): every row -- the series row ("*":
            // writer, cover) and each volume's -- is copied to the new key BEFORE the name is saved and
            // removed from the old one only AFTER. The store is case-insensitive, so a case-only rename is
            // the same key and moves nothing (Set-then-Remove would delete them).
            var oldKey = LibraryTypes.PinKey(oldName, author.Library);
            var newKey = LibraryTypes.PinKey(newName, author.Library);
            var pins = renaming && !oldKey.Equals(newKey, StringComparison.OrdinalIgnoreCase)
                ? _metadataOverrides.Get(oldKey) ?? new Dictionary<string, VolumeOverride>()
                : new Dictionary<string, VolumeOverride>();

            // Polish: the refresh check again, right before the first write -- the preview and the checks
            // above took time, and a refresh that started meanwhile would save its stale copy over this.
            if (EditionChangeChecks.RefreshRunningFor(_commandQueueManager, author.Id))
            {
                return EditionChangeChecks.RefreshRunningReason;
            }

            foreach (var pin in pins)
            {
                _metadataOverrides.Set(newKey, pin.Key, pin.Value);
            }

            try
            {
                _authorMetadataService.Upsert(write);
            }
            catch
            {
                // Polish: undo the copies, so a retry is not blocked by its own "pins already exist" check.
                foreach (var pin in pins)
                {
                    _metadataOverrides.Remove(newKey, pin.Key);
                }

                throw;
            }

            meta.EditionLanguage = write.EditionLanguage;
            meta.TomeLineId = write.TomeLineId;
            meta.AnchorName = write.AnchorName;
            meta.EditionCollected = write.EditionCollected;

            // KR/CN consumer (2026-09-29): a change of edition is the user's choice, never a fallback.
            meta.EditionFallback = false;

            if (renaming)
            {
                meta.Name = write.Name;
                meta.SortName = write.SortName;
                meta.NameLastFirst = write.NameLastFirst;
                meta.SortNameLastFirst = write.SortNameLastFirst;
            }

            foreach (var pin in pins)
            {
                _metadataOverrides.Remove(oldKey, pin.Key);
            }

            // CleanName is left to the queued refresh: RefreshAuthorService derives it from the new name
            // and keeps the old one when another series already holds it (IX_Authors_CleanName is UNIQUE).
            // The folder follows the name the way Edit Series' path change moves it (MoveAuthorCommand).
            var oldPath = author.Path;

            if (renaming && oldPath.IsNotNullOrWhiteSpace() && newPath.IsNotNullOrWhiteSpace() && oldPath != newPath)
            {
                author.Path = newPath;
                _authorService.UpdateAuthor(author);
                _commandQueueManager.Push(new MoveAuthorCommand { AuthorId = author.Id, SourcePath = oldPath, DestinationPath = newPath });
            }

            // The refresh re-mints the volumes from the new line. It is also what corrects a refresh that
            // was already running when this wrote (BookInfoProxy's editionMoved keeps this binding, but that
            // pass's volumes are still the old line's).
            _mangaMetadataProvider.ForgetLookup(oldName);
            _commandQueueManager.Push(new RefreshAuthorCommand(author.Id));

            _logger.Info("Change edition {0}: {1} -> {2} ({3} -> {4} volumes){5}",
                oldName,
                preview.FromLanguage,
                preview.ToLanguage,
                preview.FromVolumes,
                preview.ToVolumes,
                renaming ? $", renamed to \"{newName}\"" : string.Empty);

            return null;
        }
    }
}
