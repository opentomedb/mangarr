using System;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDeleteMediaFiles
    {
        void DeleteTrackFile(Author author, BookFile bookFile);
        void DeleteTrackFile(BookFile bookFile, string subfolder = "");
    }

    public class MediaFileDeletionService : IDeleteMediaFiles,
                                            IHandle<AuthorDeletedEvent>,
                                            IHandleAsync<AuthorDeletedEvent>,
                                            IHandleAsync<BookDeletedEvent>,
                                            IHandle<BookFileDeletedEvent>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IAuthorService _authorService;
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IRootFolderService _rootFolderService;
        private readonly ICalibreProxy _calibre;
        private readonly ILightNovelCalibreSettings _lightNovelCalibre;
        private readonly Logger _logger;

        public MediaFileDeletionService(IDiskProvider diskProvider,
                                        IRecycleBinProvider recycleBinProvider,
                                        IMediaFileService mediaFileService,
                                        IAuthorService authorService,
                                        IConfigService configService,
                                        IEventAggregator eventAggregator,
                                        IRootFolderService rootFolderService,
                                        ICalibreProxy calibre,
                                        ILightNovelCalibreSettings lightNovelCalibre,
                                        Logger logger)
        {
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _authorService = authorService;
            _configService = configService;
            _eventAggregator = eventAggregator;
            _rootFolderService = rootFolderService;
            _calibre = calibre;
            _lightNovelCalibre = lightNovelCalibre;
            _logger = logger;
        }

        public void DeleteTrackFile(Author author, BookFile bookFile)
        {
            // One copy each (2026-09-20): an off-entry file (calibre's library, Audiobookshelf's
            // tree) is not under the entry's root folder, so the root-folder checks and the
            // relative subfolder below do not apply to it; DeleteFile dispatches on its home.
            if (bookFile.Home != FileHome.Entry)
            {
                DeleteTrackFile(bookFile);
                return;
            }

            var fullPath = bookFile.Path;
            var rootFolder = _diskProvider.GetParentFolder(author.Path);

            if (!_diskProvider.FolderExists(rootFolder))
            {
                _logger.Warn("Author's root folder ({0}) doesn't exist.", rootFolder);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Series' root folder ({0}) doesn't exist.", rootFolder);
            }

            if (_diskProvider.GetDirectories(rootFolder).Empty())
            {
                _logger.Warn("Author's root folder ({0}) is empty.", rootFolder);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Series' root folder ({0}) is empty.", rootFolder);
            }

            if (_diskProvider.FolderExists(author.Path))
            {
                var subfolder = _diskProvider.GetParentFolder(author.Path).GetRelativePath(_diskProvider.GetParentFolder(fullPath));
                DeleteTrackFile(bookFile, subfolder);
            }
            else
            {
                // delete from db even if the author folder is missing
                _mediaFileService.Delete(bookFile, DeleteMediaFileReason.Manual);
            }
        }

        public void DeleteTrackFile(BookFile bookFile, string subfolder = "")
        {
            var fullPath = bookFile.Path;

            // One copy each (2026-09-20): a calibre row's file is calibre's to remove, whether or
            // not its path is visible here (the ro mount may be absent, or calibre may have renamed
            // the file since the last scan); a refused DeleteBook throws and keeps the row.
            var throughCalibre = bookFile.Home == FileHome.Calibre && !bookFile.Adopted;

            if (throughCalibre || _diskProvider.FileExists(fullPath))
            {
                _logger.Info("Deleting book file: {0}", fullPath);
                DeleteFile(bookFile, subfolder);
            }

            // Delete the track file from the database to clean it up even if the file was already deleted
            _mediaFileService.Delete(bookFile, DeleteMediaFileReason.Manual);

            _eventAggregator.PublishEvent(new DeleteCompletedEvent());
        }

        private void DeleteFile(BookFile bookFile, string subfolder = "")
        {
            // One copy each (2026-09-20): an adopted original is managed outside Mangarr -- the
            // caller forgets the row, the file stays where it is.
            if (bookFile.Adopted)
            {
                _logger.Info("adopted file left in place: {0}", bookFile.Path);
                return;
            }

            try
            {
                // By the file's home: a light-novel EPUB in calibre's library goes through the
                // content server named in config, an audiobook to the recycle bin (its folder too
                // once nothing is left in it); an Entry file as always, by its root folder. The
                // root folder is resolved only for Entry -- there is none under /books or
                // Audiobookshelf's tree.
                switch (bookFile.Home)
                {
                    case FileHome.Calibre:
                        _calibre.DeleteBook(bookFile, _lightNovelCalibre.ForConfig());
                        break;

                    case FileHome.Audiobooks:
                        _recycleBinProvider.DeleteFile(bookFile.Path, "audiobooks");

                        var folder = bookFile.Path.GetParentPath();
                        if (_diskProvider.GetFiles(folder, true).Empty())
                        {
                            _diskProvider.DeleteFolder(folder, false);
                        }

                        break;

                    default:
                        var rootFolder = _rootFolderService.GetBestRootFolder(bookFile.Path);
                        var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;

                        if (!isCalibre)
                        {
                            _recycleBinProvider.DeleteFile(bookFile.Path, subfolder);
                        }
                        else
                        {
                            _calibre.DeleteBook(bookFile, rootFolder.CalibreSettings);
                        }

                        break;
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Unable to delete book file");
                throw new NzbDroneClientException(HttpStatusCode.InternalServerError, "Unable to delete volume file");
            }
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(AuthorDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                var author = message.Author;

                DeleteOffEntryFiles(author);

                var rootFolder = _rootFolderService.GetBestRootFolder(message.Author.Path);
                var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;

                if (isCalibre)
                {
                    // use metadataId instead of authorId so that query works even after author deleted
                    // (Entry rows only: the off-entry rows went through DeleteOffEntryFiles above)
                    var books = _mediaFileService.GetFilesByAuthorMetadataId(author.AuthorMetadataId)
                        .Where(f => f.Home == FileHome.Entry)
                        .ToList();
                    _calibre.DeleteBooks(books, rootFolder.CalibreSettings);
                }
            }
        }

        // One copy each (2026-09-20): deleting an entry with its files applies the same per-file
        // rule to its off-entry rows -- calibre books through the content server, each
        // Mangarr-grabbed audiobook FILE to the recycle bin (its folder only once nothing is left
        // in it: a grab may have landed in a pre-existing folder of the same name, and whatever
        // else is in there is not Mangarr's to take), adopted originals left where they are.
        // The entry folder goes as today.
        private void DeleteOffEntryFiles(Author author)
        {
            // use metadataId instead of authorId so that query works even after author deleted
            var files = _mediaFileService.GetFilesByAuthorMetadataId(author.AuthorMetadataId)
                .Where(f => f.Home != FileHome.Entry)
                .ToList();

            foreach (var file in files.Where(f => f.Adopted))
            {
                _logger.Info("adopted file left in place: {0}", file.Path);
            }

            var calibreRows = files.Where(f => !f.Adopted && f.Home == FileHome.Calibre).ToList();

            if (calibreRows.Any())
            {
                try
                {
                    _calibre.DeleteBooks(calibreRows, _lightNovelCalibre.ForConfig());
                }
                catch (Exception e)
                {
                    // the author row is already gone; a calibre that is down must not keep the
                    // audiobooks below from being recycled
                    _logger.Error(e, "Unable to delete {0}'s calibre books; they stay in calibre", author.Name);
                }
            }

            var audiobooks = files.Where(f => !f.Adopted && f.Home == FileHome.Audiobooks).ToList();

            foreach (var file in audiobooks)
            {
                if (_diskProvider.FileExists(file.Path))
                {
                    _recycleBinProvider.DeleteFile(file.Path, "audiobooks");
                }
            }

            var folders = audiobooks.Select(f => f.Path.GetParentPath()).Distinct(PathEqualityComparer.Instance);

            foreach (var folder in folders)
            {
                if (_diskProvider.FolderExists(folder) && _diskProvider.GetFiles(folder, true).Empty())
                {
                    _diskProvider.DeleteFolder(folder, false);
                }
            }
        }

        public void HandleAsync(AuthorDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                var author = message.Author;

                var rootFolder = _rootFolderService.GetBestRootFolder(message.Author.Path);
                var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;

                if (!isCalibre)
                {
                    var allAuthors = _authorService.AllAuthorPaths();

                    foreach (var s in allAuthors)
                    {
                        if (s.Key == author.Id)
                        {
                            continue;
                        }

                        if (author.Path.IsParentPath(s.Value))
                        {
                            _logger.Error("Author path: '{0}' is a parent of another author, not deleting files.", author.Path);
                            return;
                        }

                        if (author.Path.PathEquals(s.Value))
                        {
                            _logger.Error("Author path: '{0}' is the same as another author, not deleting files.", author.Path);
                            return;
                        }
                    }

                    if (_diskProvider.FolderExists(message.Author.Path))
                    {
                        _recycleBinProvider.DeleteFolder(message.Author.Path);
                    }

                    _eventAggregator.PublishEvent(new DeleteCompletedEvent());
                }
            }
        }

        public void HandleAsync(BookDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                var files = _mediaFileService.GetFilesByBook(message.Book.Id);
                foreach (var file in files)
                {
                    DeleteFile(file);
                }
            }
        }

        [EventHandleOrder(EventHandleOrder.Last)]
        public void Handle(BookFileDeletedEvent message)
        {
            if (message.Reason == DeleteMediaFileReason.Upgrade)
            {
                return;
            }

            // One copy each (2026-09-20): an off-entry row's folder is calibre's or
            // Audiobookshelf's, and its entry folder is legitimately empty (an audio-only
            // entry); the empty-folder sweep is for Entry rows only.
            if (message.BookFile.Home != FileHome.Entry)
            {
                return;
            }

            if (_configService.DeleteEmptyFolders)
            {
                var author = message.BookFile.Author.Value;
                var bookFolder = message.BookFile.Path.GetParentPath();

                if (_diskProvider.GetFiles(author.Path, true).Empty())
                {
                    _diskProvider.DeleteFolder(author.Path, true);
                }
                else if (_diskProvider.GetFiles(bookFolder, true).Empty())
                {
                    _diskProvider.RemoveEmptySubfolders(bookFolder);
                }
            }
        }
    }
}
