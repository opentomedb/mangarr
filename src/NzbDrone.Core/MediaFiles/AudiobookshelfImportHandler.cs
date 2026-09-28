using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles
{
    // One copy each (2026-09-20); ABS titles match calibre (2026-09-23): a light novel's audio now
    // lands in Audiobookshelf's own tree, so once an import has placed one there Mangarr asks ABS to
    // scan its library, then attempts the same series/title/subtitle sync every other path runs. One
    // scan and one sync attempt per event (an event carries every part of the audiobook); an adopted
    // file was already ABS's, so it triggers neither. ABS's scan endpoint can return before the scan
    // actually finishes, so the item is often not there yet -- Sync's own "no item at this path"
    // debug log covers that, and the next scheduled pass (SyncTags with WriteAudioTags=Sync, a
    // retag, or the whole-library SyncLightNovelTitles command) catches it. Neither a failed
    // scan request nor a failed sync ever fails the import.
    public class AudiobookshelfImportHandler : IHandleAsync<BookImportedEvent>
    {
        private readonly IAudiobookshelfClient _audiobookshelf;
        private readonly IAdoptedAudioSyncService _adoptedSync;
        private readonly Logger _logger;

        public AudiobookshelfImportHandler(IAudiobookshelfClient audiobookshelf, IAdoptedAudioSyncService adoptedSync, Logger logger)
        {
            _audiobookshelf = audiobookshelf;
            _adoptedSync = adoptedSync;
            _logger = logger;
        }

        public void HandleAsync(BookImportedEvent message)
        {
            if (message.Author?.Library != LibraryType.LightNovel)
            {
                return;
            }

            var grabbed = message.ImportedBooks.Where(f => f.Home == FileHome.Audiobooks && !f.Adopted).ToList();

            if (grabbed.Empty())
            {
                return;
            }

            try
            {
                _audiobookshelf.ScanLibrary();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Audiobookshelf scan request failed");
            }

            // Fix round 1 (M1): copies, never the shared instances in message.ImportedBooks --
            // those are also in ImportApprovedBooks' allImportedTrackFiles, read by
            // MarkAudioAvailable on the import thread concurrently with this async handler.
            // Mutating file.Edition here raced that read.
            var syncCopies = grabbed.Select(f => new BookFile
            {
                Id = f.Id,
                Path = f.Path,
                Home = f.Home,
                Adopted = f.Adopted,
                Edition = new Edition { Book = message.Book }
            }).ToList();

            try
            {
                if (!_adoptedSync.Sync(message.Author, syncCopies, out var failure))
                {
                    _logger.Debug("Audiobookshelf sync after import: {0}", failure);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Audiobookshelf sync after import failed");
            }
        }
    }
}
