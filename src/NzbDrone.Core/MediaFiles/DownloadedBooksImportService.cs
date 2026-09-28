using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDownloadedBooksImportService
    {
        List<ImportResult> ProcessRootFolder(IDirectoryInfo directoryInfo);
        List<ImportResult> ProcessPath(string path, ImportMode importMode = ImportMode.Auto, Author author = null, DownloadClientItem downloadClientItem = null, Book book = null);
        bool ShouldDeleteFolder(IDirectoryInfo directoryInfo);
    }

    public class DownloadedBooksImportService : IDownloadedBooksImportService
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskScanService _diskScanService;
        private readonly IAuthorService _authorService;
        private readonly IParsingService _parsingService;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IImportApprovedBooks _importApprovedTracks;
        private readonly IEventAggregator _eventAggregator;
        private readonly IRuntimeInfo _runtimeInfo;
        private readonly Logger _logger;

        public DownloadedBooksImportService(IDiskProvider diskProvider,
                                             IDiskScanService diskScanService,
                                             IAuthorService authorService,
                                             IParsingService parsingService,
                                             IMakeImportDecision importDecisionMaker,
                                             IImportApprovedBooks importApprovedTracks,
                                             IEventAggregator eventAggregator,
                                             IRuntimeInfo runtimeInfo,
                                             Logger logger)
        {
            _diskProvider = diskProvider;
            _diskScanService = diskScanService;
            _authorService = authorService;
            _parsingService = parsingService;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedTracks = importApprovedTracks;
            _eventAggregator = eventAggregator;
            _runtimeInfo = runtimeInfo;
            _logger = logger;
        }

        public List<ImportResult> ProcessRootFolder(IDirectoryInfo directoryInfo)
        {
            var results = new List<ImportResult>();

            foreach (var subFolder in _diskProvider.GetDirectoryInfos(directoryInfo.FullName))
            {
                var folderResults = ProcessFolder(subFolder, ImportMode.Auto, null);
                results.AddRange(folderResults);
            }

            foreach (var audioFile in _diskScanService.GetBookFiles(directoryInfo.FullName, false))
            {
                var fileResults = ProcessFile(audioFile, ImportMode.Auto, null);
                results.AddRange(fileResults);
            }

            return results;
        }

        public List<ImportResult> ProcessPath(string path, ImportMode importMode = ImportMode.Auto, Author author = null, DownloadClientItem downloadClientItem = null, Book book = null)
        {
            _logger.Debug("Processing path: {0}", path);

            if (_diskProvider.FolderExists(path))
            {
                var directoryInfo = _diskProvider.GetDirectoryInfo(path);

                if (author == null)
                {
                    return ProcessFolder(directoryInfo, importMode, downloadClientItem);
                }

                return ProcessFolder(directoryInfo, importMode, author, downloadClientItem, book);
            }

            if (_diskProvider.FileExists(path))
            {
                var fileInfo = _diskProvider.GetFileInfo(path);

                if (author == null)
                {
                    return ProcessFile(fileInfo, importMode, downloadClientItem);
                }

                return ProcessFile(fileInfo, importMode, author, downloadClientItem, book);
            }

            LogInaccessiblePathError(path);
            _eventAggregator.PublishEvent(new TrackImportFailedEvent(null, null, true, downloadClientItem));

            return new List<ImportResult>();
        }

        public bool ShouldDeleteFolder(IDirectoryInfo directoryInfo)
        {
            try
            {
                var bookFiles = _diskScanService.GetBookFiles(directoryInfo.FullName);
                var rarFiles = _diskProvider.GetFiles(directoryInfo.FullName, true).Where(f =>
                    Path.GetExtension(f).Equals(".rar",
                        StringComparison.OrdinalIgnoreCase));

                foreach (var bookFile in bookFiles)
                {
                    var bookParseResult = Parser.Parser.ParseTitle(bookFile.Name);

                    if (bookParseResult == null)
                    {
                        _logger.Warn("Unable to parse file on import: [{0}]", bookFile);
                        return false;
                    }

                    _logger.Warn("Book file detected: [{0}]", bookFile);
                    return false;
                }

                if (rarFiles.Any(f => _diskProvider.GetFileSize(f) > 10.Megabytes()))
                {
                    _logger.Warn("RAR file detected, will require manual cleanup");
                    return false;
                }

                return true;
            }
            catch (DirectoryNotFoundException e)
            {
                _logger.Debug(e, "Folder {0} has already been removed", directoryInfo.FullName);
                return false;
            }
            catch (Exception e)
            {
                _logger.Debug(e, "Unable to determine whether folder {0} should be removed", directoryInfo.FullName);
                return false;
            }
        }

        private List<ImportResult> ProcessFolder(IDirectoryInfo directoryInfo, ImportMode importMode, DownloadClientItem downloadClientItem)
        {
            var cleanedUpName = GetCleanedUpFolderName(directoryInfo.Name);
            var author = _parsingService.GetAuthor(cleanedUpName);

            return ProcessFolder(directoryInfo, importMode, author, downloadClientItem);
        }

        private List<ImportResult> ProcessFolder(IDirectoryInfo directoryInfo, ImportMode importMode, Author author, DownloadClientItem downloadClientItem, Book book = null)
        {
            if (_authorService.AuthorPathExists(directoryInfo.FullName))
            {
                _logger.Warn("Unable to process folder that is mapped to an existing author");
                return new List<ImportResult>();
            }

            var cleanedUpName = GetCleanedUpFolderName(directoryInfo.Name);
            var folderInfo = Parser.Parser.ParseBookTitle(directoryInfo.Name);
            var trackInfo = new ParsedTrackInfo { };

            if (folderInfo != null)
            {
                _logger.Debug("{0} folder quality: {1}", cleanedUpName, folderInfo.Quality);

                trackInfo = new ParsedTrackInfo
                {
                    BookTitle = folderInfo.BookTitle,
                    Authors = new List<string> { folderInfo.AuthorName },
                    Quality = folderInfo.Quality,
                    ReleaseGroup = folderInfo.ReleaseGroup,
                    ReleaseHash = folderInfo.ReleaseHash,
                };
            }
            else
            {
                trackInfo = null;
            }

            var audioFiles = _diskScanService.FilterFiles(directoryInfo.FullName, _diskScanService.GetBookFiles(directoryInfo.FullName));

            if (downloadClientItem == null)
            {
                foreach (var audioFile in audioFiles)
                {
                    if (_diskProvider.IsFileLocked(audioFile.FullName))
                    {
                        return new List<ImportResult>
                               {
                                   FileIsLockedResult(audioFile.FullName)
                               };
                    }
                }
            }

            var idOverrides = new IdentificationOverrides
            {
                Author = author
            };

            // Release-trusted import (2026-09-18): the grab's book is authoritative for a folder download too — the TBATE .mp4 releases failed CloseAlbumMatchSpecification at 49.5 % because the folder path never reached the override.
            // The edition is the one of the folder's files' class (as ProcessFile picks it for a
            // single file). Light novels only for now (D7: manga stays byte-identical -- a manga
            // folder keeps per-file identification, and the manga CBZ case is the single-file
            // shortcut in ProcessFile). A null author is not a light novel.
            if (book != null && author?.Library == LibraryType.LightNovel && audioFiles.Any())
            {
                ApplyKnownBook(idOverrides, book, DominantExtension(audioFiles, author.Library), author, "Folder grab");
            }

            var idInfo = new ImportDecisionMakerInfo
            {
                DownloadClientItem = downloadClientItem,
                ParsedBookInfo = folderInfo
            };
            var idConfig = new ImportDecisionMakerConfig
            {
                Filter = FilterFilesType.None,
                NewDownload = true,
                SingleRelease = false,
                IncludeExisting = false,
                AddNewAuthors = false
            };

            var decisions = _importDecisionMaker.GetImportDecisions(audioFiles, idOverrides, idInfo, idConfig);
            var importResults = _importApprovedTracks.Import(decisions, true, downloadClientItem, importMode);

            if (importMode == ImportMode.Auto)
            {
                importMode = (downloadClientItem == null || downloadClientItem.CanMoveFiles) ? ImportMode.Move : ImportMode.Copy;
            }

            if (importMode == ImportMode.Move &&
                importResults.Any(i => i.Result == ImportResultType.Imported) &&
                ShouldDeleteFolder(directoryInfo))
            {
                _logger.Debug("Deleting folder after importing valid files");

                try
                {
                    _diskProvider.DeleteFolder(directoryInfo.FullName, true);
                }
                catch (IOException e)
                {
                    _logger.Debug(e, "Unable to delete folder after importing: {0}", e.Message);
                }
            }

            return importResults;
        }

        private List<ImportResult> ProcessFile(IFileInfo fileInfo, ImportMode importMode, DownloadClientItem downloadClientItem)
        {
            var author = _parsingService.GetAuthor(Path.GetFileNameWithoutExtension(fileInfo.Name));

            if (author == null)
            {
                _logger.Debug("Unknown Author for file: {0}", fileInfo.Name);

                return new List<ImportResult>
                       {
                           UnknownAuthorResult(fileInfo.Name, fileInfo.FullName)
                       };
            }

            return ProcessFile(fileInfo, importMode, author, downloadClientItem);
        }

        private List<ImportResult> ProcessFile(IFileInfo fileInfo, ImportMode importMode, Author author, DownloadClientItem downloadClientItem, Book book = null)
        {
            if (Path.GetFileNameWithoutExtension(fileInfo.Name).StartsWith("._"))
            {
                _logger.Debug("[{0}] starts with '._', skipping", fileInfo.FullName);

                return new List<ImportResult>
                       {
                           new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = fileInfo.FullName }, new Rejection("Invalid music file, filename starts with '._'")), "Invalid music file, filename starts with '._'")
                       };
            }

            if (downloadClientItem == null)
            {
                if (_diskProvider.IsFileLocked(fileInfo.FullName))
                {
                    return new List<ImportResult>
                           {
                               FileIsLockedResult(fileInfo.FullName)
                           };
                }
            }

            var idOverrides = new IdentificationOverrides
            {
                Author = author
            };

            // Manga / single-book grab: a single-file download whose grab resolved to exactly one
            // known Book (passed via `book`) can be matched directly to that Book's Edition,
            // bypassing tag-based identification. Manga CBZs have no embedded tags and the music/book
            // filename parser can't read the volume, so identification returns 0 candidates and the
            // file never imports. Packs/albums keep the existing multi-track identification; a
            // light-novel folder download takes the same shortcut in ProcessFolder (2026-09-18).
            if (book != null)
            {
                ApplyKnownBook(idOverrides, book, Path.GetExtension(fileInfo.Name), author, "Single-file grab");
            }

            var idInfo = new ImportDecisionMakerInfo
            {
                DownloadClientItem = downloadClientItem
            };
            var idConfig = new ImportDecisionMakerConfig
            {
                Filter = FilterFilesType.None,
                NewDownload = true,
                SingleRelease = false,
                IncludeExisting = false,
                AddNewAuthors = false
            };

            var decisions = _importDecisionMaker.GetImportDecisions(new List<IFileInfo>() { fileInfo }, idOverrides, idInfo, idConfig);

            return _importApprovedTracks.Import(decisions, true, downloadClientItem, importMode);
        }

        // The grab's book is authoritative: the volume's edition of the FILE's class carries the
        // override -- an EPUB goes to the Ebook edition, an M4B to the Audio edition; a manga
        // volume's one edition is Archive, and every manga grab is an archive, so it picks the
        // edition it always did. A file of a class the volume lacks gets no shortcut and falls
        // through to identification (which rejects it: MediaTypeMatchesEditionSpecification). The
        // label names the caller in the log line ("Single-file grab", the manga CBZ line as it always read).
        // LN PDF (2026-09-22): the class is read against the author's library, so a light novel's
        // PDF goes to its Ebook edition (MediaTypes.OfFile; a bare extension is a valid path here).
        private void ApplyKnownBook(IdentificationOverrides idOverrides, Book book, string extension, Author author, string label)
        {
            var edition = book.EditionOf(MediaTypes.OfFile(extension, author?.Library));

            if (edition != null)
            {
                idOverrides.Book = book;
                idOverrides.Edition = edition;
                _logger.Debug("{0} matched to known book [{1}] edition [{2}]; bypassing tag identification", label, book, edition);
            }
        }

        // The class the folder is really of: the one holding the most bytes, not the first file's.
        // 2026-09-22: PZG's "Classroom of the Elite, Vol. 04 (Audiobook)" folder bundles the EPUB
        // next to the 489 MB M4B; the EPUB sorts first, so the grab was forced onto the EPUB edition,
        // the M4B then matched nothing ("Couldn't find similar book") and the audiobook was lost.
        // LN PDF (2026-09-22): classes are read against the entry's library (a light novel's PDF is
        // an ebook).
        private static string DominantExtension(IEnumerable<IFileInfo> files, LibraryType? library)
        {
            return files
                .GroupBy(f => MediaTypes.OfFile(f.FullName, library))
                .OrderByDescending(g => g.Sum(f => f.Length))
                .First()
                .OrderByDescending(f => f.Length)
                .First()
                .Extension;
        }

        private string GetCleanedUpFolderName(string folder)
        {
            folder = folder.Replace("_UNPACK_", "")
                           .Replace("_FAILED_", "");

            return folder;
        }

        private ImportResult FileIsLockedResult(string audioFile)
        {
            _logger.Debug("[{0}] is currently locked by another process, skipping", audioFile);
            return new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = audioFile }, new Rejection("Locked file, try again later")), "Locked file, try again later");
        }

        // Server messages (2026-09-26): the message's template lives here, with its one caller's file name as the
        // argument, so the import result keeps it for the queue in the UI language.
        private ImportResult UnknownAuthorResult(string fileName, string bookFile = null)
        {
            var localTrack = bookFile == null ? null : new LocalBook { Path = bookFile };

            return new ImportResult(new ImportDecision<LocalBook>(localTrack, new Rejection("Unknown Series")), new ServerText("Unknown Series for file: {0}", fileName));
        }

        private void LogInaccessiblePathError(string path)
        {
            if (_runtimeInfo.IsWindowsService)
            {
                var mounts = _diskProvider.GetMounts();
                var mount = mounts.FirstOrDefault(m => m.RootDirectory == Path.GetPathRoot(path));

                if (mount == null)
                {
                    _logger.Error("Import failed, path does not exist or is not accessible by Readarr: {0}. Unable to find a volume mounted for the path. If you're using a mapped network drive see the FAQ for more info", path);
                    return;
                }

                if (mount.DriveType == DriveType.Network)
                {
                    _logger.Error("Import failed, path does not exist or is not accessible by Readarr: {0}. It's recommended to avoid mapped network drives when running as a Windows service. See the FAQ for more info", path);
                    return;
                }
            }

            if (OsInfo.IsWindows)
            {
                if (path.StartsWith(@"\\"))
                {
                    _logger.Error("Import failed, path does not exist or is not accessible by Readarr: {0}. Ensure the user running Mangarr has access to the network share", path);
                    return;
                }
            }

            _logger.Error("Import failed, path does not exist or is not accessible by Readarr: {0}. Ensure the path exists and the user running Mangarr has the correct permissions to access this file/folder", path);
        }
    }
}
