using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IUpgradeMediaFiles
    {
        BookFileMoveResult UpgradeBookFile(BookFile bookFile, LocalBook localBook, bool copyOnly = false);
    }

    public class UpgradeMediaFileService : IUpgradeMediaFiles
    {
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IMoveBookFiles _bookFileMover;
        private readonly IDiskProvider _diskProvider;
        private readonly IRootFolderService _rootFolderService;
        private readonly ICalibreProxy _calibre;
        private readonly ILightNovelCalibreSettings _lightNovelCalibre;
        private readonly ILightNovelStorage _storage;
        private readonly INamingConfigService _namingConfigService;
        private readonly Logger _logger;

        public UpgradeMediaFileService(IRecycleBinProvider recycleBinProvider,
                                       IMediaFileService mediaFileService,
                                       IMetadataTagService metadataTagService,
                                       IMoveBookFiles bookFileMover,
                                       IDiskProvider diskProvider,
                                       IRootFolderService rootFolderService,
                                       ICalibreProxy calibre,
                                       ILightNovelCalibreSettings lightNovelCalibre,
                                       ILightNovelStorage storage,
                                       INamingConfigService namingConfigService,
                                       Logger logger)
        {
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _metadataTagService = metadataTagService;
            _bookFileMover = bookFileMover;
            _diskProvider = diskProvider;
            _rootFolderService = rootFolderService;
            _calibre = calibre;
            _lightNovelCalibre = lightNovelCalibre;
            _storage = storage;
            _namingConfigService = namingConfigService;
            _logger = logger;
        }

        public BookFileMoveResult UpgradeBookFile(BookFile bookFile, LocalBook localBook, bool copyOnly = false)
        {
            var moveFileResult = new BookFileMoveResult();

            // Light novels (2026-09): an upgrade replaces the files of the edition this file is
            // imported to -- the EPUB and the audiobook of a volume live side by side. A manga
            // volume's one edition holds every file of the volume, so nothing changes for manga.
            // (Edition.BookFiles is edition-scoped, Book.BookFiles spans every edition: §5 of the map.)
            var existingFiles = localBook.Edition.BookFiles.Value;

            var rootFolderPath = _diskProvider.GetParentFolder(localBook.Author.Path);
            var rootFolder = _rootFolderService.GetBestRootFolder(rootFolderPath);

            // One copy each (2026-09-20): a light novel's files go where their readers are -- the
            // EPUB to calibre's content server (settings from config, not a calibre root folder),
            // the audio into Audiobookshelf's tree. Placement keys on the DESTINATION; the removal
            // of each old file, below, keys on that file's own home. A stock calibre root and every
            // manga entry take the branches they always did.
            var author = localBook.Author;
            var isLightNovel = author.Library == LibraryType.LightNovel;

            // Light-novel storage (2026-09-22): each kind goes to its home from Settings -- calibre /
            // Audiobookshelf, or the entry folder like manga (the default; works with neither installed).
            var toCalibre = isLightNovel && localBook.Edition.MediaType == MediaType.Ebook && _storage.EbookHome == LightNovelHome.Calibre;
            var toAudiobooks = isLightNovel && localBook.Edition.MediaType == MediaType.Audio && _storage.AudioHome == LightNovelHome.Audiobookshelf;
            var isCalibre = toCalibre || (rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null);

            var settings = toCalibre ? _lightNovelCalibre.ForConfig() : rootFolder.CalibreSettings;

            // An old file keeps its own home (a changed setting applies to new imports only): a
            // calibre-homed EPUB is removed through the configured calibre even when the new file goes
            // to the entry folder -- resolved before anything is touched, so a missing calibre URL
            // fails the import cleanly (CalibreException) instead of half-way through.
            var oldCalibreSettings = toCalibre ? settings : (existingFiles.Any(f => f.Home == FileHome.Calibre) ? _lightNovelCalibre.ForConfig() : null);

            // If there are existing book files and the root folder is missing, throw, so the old file isn't left behind during the import process.
            if (existingFiles.Any() && !_diskProvider.FolderExists(rootFolderPath))
            {
                throw new RootFolderNotFoundException($"Root folder '{rootFolderPath}' was not found.");
            }

            // Audiobookshelf's library folder is the destination, so a missing (or, unmapped and ABS
            // silent, unknown) folder fails the import the way a missing root folder does -- the
            // download stays put. Without this, CreateFolder would build the folder inside the
            // container, the move would "succeed", and the file would die with the container without
            // Audiobookshelf ever seeing it.
            if (toAudiobooks)
            {
                var audioRoot = _storage.AudiobookshelfRoot();

                if (audioRoot == null || !_diskProvider.FolderExists(audioRoot))
                {
                    throw new RootFolderNotFoundException($"Audiobookshelf's library folder '{audioRoot ?? "(unknown: Audiobookshelf did not answer)"}' was not found (Settings → Media Management → Light Novel Storage → Audiobooks).");
                }
            }

            // The Audiobookshelf layout (one "<Series> - Vol. N" folder per audiobook) is built by
            // the file namer; with Rename Books off the namer keeps the release's own file name and
            // the audio would land loose in the root of Audiobookshelf's tree. Refused up front: the
            // download stays put and the queue says why (final review I3).
            if (toAudiobooks && !_namingConfigService.GetConfig().RenameBooks)
            {
                throw new InvalidOperationException("Rename Books is off; light-novel audio needs it for the Audiobookshelf layout (Settings → Media Management)");
            }

            // A refused calibre write is found BEFORE the old EPUB's formats are removed below,
            // otherwise the refusal at AddAndConvert would leave a format-less calibre book and a
            // volume back on Wanted (final review, T4 tied item).
            if (toCalibre && !_calibre.HasWriteAccess(settings))
            {
                throw new CalibreException("Calibre refuses writes from Mangarr; set Calibre Username / Calibre Password (Settings → Media Management → Light Novel Storage → Ebooks) to a user with write access, or add this address to calibre's trusted_ips");
            }

            // An adopted original is managed outside Mangarr and is never replaced; the decision
            // specs (AdoptedFileSpecification, AdoptedEditionSpecification) refuse this earlier, so
            // this is the backstop -- checked before anything is touched.
            var adopted = existingFiles.FirstOrDefault(f => f.Adopted);

            if (adopted != null)
            {
                throw new InvalidOperationException($"{adopted} is an adopted original; the decision should have refused this upgrade");
            }

            // Fix round 1 (C6, 2026-09-22): the old calibre book on this branch is removed only once
            // the new entry file is safely in place below -- collected here instead of removed
            // in-loop, so a failed move/copy leaves the old EPUB exactly where it was, in calibre.
            var deferredCalibreDeletes = new List<BookFile>();

            foreach (var file in existingFiles)
            {
                var bookFilePath = file.Path;

                // A file calibre holds is removed through calibre (its formats), anything else goes
                // to the recycle bin -- by the OLD file's home, so an LN EPUB upgrade over an
                // Entry copy with no calibre id never asks calibre for book 0.
                var oldIsCalibre = file.Home == FileHome.Calibre || (file.CalibreId > 0 && rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null);

                // the new file joins the same calibre book only when it goes to calibre too
                if (oldIsCalibre && isCalibre)
                {
                    bookFile.CalibreId = file.CalibreId;
                }

                // Fix round 1 (C6): the new file goes to the entry folder while the old one is
                // calibre's -- deferred below, whether or not its path is visible on disk right now
                // (it's calibre's to remove either way, not the recycle bin's).
                if (oldIsCalibre && !isCalibre)
                {
                    deferredCalibreDeletes.Add(file);
                    continue;
                }

                if (_diskProvider.FileExists(bookFilePath))
                {
                    _logger.Debug("Removing existing book file: {0} CalibreId: {1}", file, file.CalibreId);

                    if (!oldIsCalibre)
                    {
                        // An off-root file (Audiobookshelf's tree) is not a child of the root folder,
                        // so it gets a fixed subfolder instead of its relative path.
                        var subfolder = file.Home == FileHome.Audiobooks
                            ? "audiobooks"
                            : rootFolderPath.GetRelativePath(_diskProvider.GetParentFolder(bookFilePath));

                        _recycleBinProvider.DeleteFile(bookFilePath, subfolder);
                    }
                    else
                    {
                        var removal = file.Home == FileHome.Calibre ? oldCalibreSettings : rootFolder.CalibreSettings;

                        var existing = _calibre.GetBook(file.CalibreId, removal);
                        var existingFormats = existing.Formats.Keys;
                        _logger.Debug($"Removing existing formats {existingFormats.ConcatToString()} from calibre");
                        _calibre.RemoveFormats(file.CalibreId, existingFormats, removal);
                    }
                }

                moveFileResult.OldFiles.Add(file);
                _mediaFileService.Delete(file, DeleteMediaFileReason.Upgrade);
            }

            if (!isCalibre)
            {
                if (copyOnly)
                {
                    moveFileResult.BookFile = _bookFileMover.CopyBookFile(bookFile, localBook);
                }
                else
                {
                    moveFileResult.BookFile = _bookFileMover.MoveBookFile(bookFile, localBook);
                }

                // Fix round 1 (C6): the move above already threw if it failed, so the new file is
                // safely placed by the time this runs. A calibre removal failure here is never the
                // import's failure -- the old book is logged for the user to remove by hand, never thrown.
                foreach (var old in deferredCalibreDeletes)
                {
                    try
                    {
                        _logger.Debug("Removing old calibre book {0} entirely; the new file goes to the entry folder", old.CalibreId);
                        _calibre.DeleteBook(old, oldCalibreSettings);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Old calibre book {0} ({1}) was left in calibre; remove it by hand", old.CalibreId, old.Path);
                    }

                    moveFileResult.OldFiles.Add(old);
                    _mediaFileService.Delete(old, DeleteMediaFileReason.Upgrade);
                }

                bookFile.Home = toAudiobooks ? FileHome.Audiobooks : FileHome.Entry;

                _metadataTagService.WriteTags(bookFile, true);
            }
            else
            {
                var source = bookFile.Path;

                moveFileResult.BookFile = _calibre.AddAndConvert(bookFile, settings);

                // A stock calibre root keeps Entry: its files sit under the root folder.
                bookFile.Home = toCalibre ? FileHome.Calibre : FileHome.Entry;

                if (!copyOnly)
                {
                    _diskProvider.DeleteFile(source);
                }
            }

            return moveFileResult;
        }
    }
}
