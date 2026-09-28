using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.BookImport.Existing
{
    // One display title (2026-09-23, the maintainer): see SyncLightNovelTitlesCommand. One entry (AuthorId) or
    // every light-novel entry; a manga entry is never touched. The calibre pass runs only when
    // EbookHome is calibre, the ABS pass only when AudioHome is audiobookshelf (entry-home installs
    // are untouched either way). The calibre half is CalibreTitleSyncService (shared with
    // ImportExistingLightNovelsService's L5 adoption-time step), the ABS half AdoptedAudioSyncService.
    public class SyncLightNovelTitlesService : IExecute<SyncLightNovelTitlesCommand>
    {
        private readonly IAuthorService _authorService;
        private readonly IMediaFileService _mediaFileService;
        private readonly ICalibreTitleSyncService _calibreTitleSync;
        private readonly ILightNovelCalibreSettings _calibreSettings;
        private readonly IAdoptedAudioSyncService _adoptedSync;
        private readonly ILightNovelStorage _storage;
        private readonly Logger _logger;

        public SyncLightNovelTitlesService(IAuthorService authorService,
                                            IMediaFileService mediaFileService,
                                            ICalibreTitleSyncService calibreTitleSync,
                                            ILightNovelCalibreSettings calibreSettings,
                                            IAdoptedAudioSyncService adoptedSync,
                                            ILightNovelStorage storage,
                                            Logger logger)
        {
            _authorService = authorService;
            _mediaFileService = mediaFileService;
            _calibreTitleSync = calibreTitleSync;
            _calibreSettings = calibreSettings;
            _adoptedSync = adoptedSync;
            _storage = storage;
            _logger = logger;
        }

        public void Execute(SyncLightNovelTitlesCommand message)
        {
            var authors = (message.AuthorId.HasValue
                    ? new List<Author> { _authorService.GetAuthor(message.AuthorId.Value) }
                    : _authorService.GetAllAuthors())
                .Where(a => a != null && a.Library == LibraryType.LightNovel)
                .ToList();

            foreach (var author in authors)
            {
                if (_storage.EbookHome == LightNovelHome.Calibre)
                {
                    SyncCalibreTitles(author);
                }

                if (_storage.AudioHome == LightNovelHome.Audiobookshelf)
                {
                    SyncAudiobookshelfTitles(author);
                }
            }
        }

        private void SyncCalibreTitles(Author author)
        {
            var rows = _mediaFileService.GetFilesByAuthor(author.Id)
                .Where(f => f.Home == FileHome.Calibre)
                .ToList();

            if (rows.Empty())
            {
                return;
            }

            _logger.ProgressInfo("Syncing calibre titles for {0}", author.Name);

            if (!_calibreTitleSync.Sync(author, rows, _calibreSettings.ForConfig(), out var failure))
            {
                _logger.Warn("{0}: calibre title sync incomplete: {1}", author.Name, failure);
            }
        }

        private void SyncAudiobookshelfTitles(Author author)
        {
            var audio = _mediaFileService.GetFilesByAuthor(author.Id)
                .Where(f => f.Home == FileHome.Audiobooks)
                .ToList();

            if (audio.Empty())
            {
                return;
            }

            _logger.ProgressInfo("Syncing Audiobookshelf titles for {0}", author.Name);

            if (!_adoptedSync.Sync(author, audio, out var failure))
            {
                _logger.Warn("{0}: Audiobookshelf sync incomplete: {1}", author.Name, failure);
            }
        }
    }
}
