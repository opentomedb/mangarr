using System.Globalization;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Extras.Metadata.ComicInfo
{
    public interface IComicInfoWriter
    {
        ComicInfoEmbedStatus WriteForFile(Author author, Book book, BookFile file);
    }

    // Orchestrates the read -> merge -> embed flow for one volume: maps Mangarr's owned metadata to
    // ComicInfo fields, preserves whatever another tool already wrote, and keeps the tracked file
    // size in sync after the archive is rewritten so a later scan doesn't see a phantom change.
    public class ComicInfoWriter : IComicInfoWriter
    {
        private readonly IComicInfoEmbedder _embedder;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public ComicInfoWriter(IComicInfoEmbedder embedder,
                               IEditionService editionService,
                               IMediaFileService mediaFileService,
                               IDiskProvider diskProvider,
                               Logger logger)
        {
            _embedder = embedder;
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public ComicInfoEmbedStatus WriteForFile(Author author, Book book, BookFile file)
        {
            if (file == null || book == null || author == null || file.Path.IsNullOrWhiteSpace())
            {
                return ComicInfoEmbedStatus.Skipped;
            }

            if (!_embedder.CanEmbed(file.Path))
            {
                return ComicInfoEmbedStatus.Skipped;
            }

            var edition = file.EditionId > 0 ? _editionService.GetEdition(file.EditionId) : null;
            var fields = BuildFields(author, book, edition);

            var existing = _embedder.ReadExisting(file.Path);
            if (!ComicInfoBuilder.WouldChange(existing, fields))
            {
                return ComicInfoEmbedStatus.Unchanged;
            }

            var merged = ComicInfoBuilder.Merge(existing, fields);
            var result = _embedder.Embed(file.Path, merged);

            if (result.Status == ComicInfoEmbedStatus.Written)
            {
                // The archive was rebuilt, so its size moved; keep the DB record honest so the
                // "same filesize" import guard and upgrade checks stay accurate.
                var newSize = _diskProvider.GetFileSize(file.Path);
                if (newSize > 0 && newSize != file.Size)
                {
                    file.Size = newSize;
                    _mediaFileService.Update(file);
                }
            }

            return result.Status;
        }

        private static ComicInfoFields BuildFields(Author author, Book book, Edition edition)
        {
            var meta = author.Metadata?.Value;
            var release = edition?.ReleaseDate ?? book.ReleaseDate;

            return new ComicInfoFields
            {
                Series = meta?.Name,
                Title = edition?.Title.IsNotNullOrWhiteSpace() == true ? edition.Title : book.Title,
                Number = MangaVolumeParser.Format(book.VolumeNumber),
                Summary = edition?.Overview,
                Year = release?.Year,
                Month = release?.Month,
                Day = release?.Day,
                PageCount = edition?.PageCount ?? 0,
                Publisher = edition?.Publisher,
                LanguageIso = "en",

                // Manga read right-to-left; set as the default so readers navigate correctly. Only
                // applied when the file has no reading-direction yet (see ComicInfoBuilder).
                Manga = "YesAndRightToLeft",

                // A total volume count is only meaningful once the series has finished; on an
                // ongoing series it would falsely cap the series in the reader.
                Count = meta != null && meta.Status == AuthorStatusType.Ended && meta.TotalVolumes > 0
                    ? meta.TotalVolumes
                    : (int?)null
            };
        }
    }
}
