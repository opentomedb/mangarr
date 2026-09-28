using System;
using System.Linq;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using Readarr.Http.REST;

namespace Readarr.Api.V1.BookFiles
{
    public class BookFileResource : RestResource
    {
        public int AuthorId { get; set; }
        public int BookId { get; set; }
        public int EditionId { get; set; }      // the edition the file belongs to (a light novel's EPUB or audio)
        public string Path { get; set; }
        public long Size { get; set; }
        public DateTime DateAdded { get; set; }
        public QualityModel Quality { get; set; }
        public int QualityWeight { get; set; }
        public int? IndexerFlags { get; set; }
        public MediaInfoResource MediaInfo { get; set; }

        public bool QualityCutoffNotMet { get; set; }
        public ParsedTrackInfo AudioTags { get; set; }

        // One copy each (2026-09-20): FileHome as its int (0 entry, 1 calibre, 2 audiobooks).
        public int Home { get; set; }
        public bool Adopted { get; set; }
    }

    public static class BookFileResourceMapper
    {
        private static int QualityWeight(QualityModel quality)
        {
            if (quality == null)
            {
                return 0;
            }

            // Manga fork: legacy audio qualities (MP3/FLAC/M4B/UnknownAudio, ids 10-13) were
            // removed from DefaultQualityDefinitions but may still exist on old BookFile rows.
            // A plain Single() throws InvalidOperationException for those, which would 500 any
            // GET /api/v1/bookfiles that returns such a row. Fall back to weight 0.
            var qualityDefinition = Quality.DefaultQualityDefinitions.SingleOrDefault(q => q.Quality == quality.Quality);
            var qualityWeight = qualityDefinition?.Weight ?? 0;
            qualityWeight += quality.Revision.Real * 10;
            qualityWeight += quality.Revision.Version;
            return qualityWeight;
        }

        public static BookFileResource ToResource(this BookFile model)
        {
            if (model == null)
            {
                return null;
            }

            return new BookFileResource
            {
                Id = model.Id,
                BookId = model.Edition.Value?.BookId ?? 0,
                EditionId = model.EditionId,
                Path = model.Path,
                Size = model.Size,
                DateAdded = model.DateAdded,
                Quality = model.Quality,
                QualityWeight = QualityWeight(model.Quality),
                MediaInfo = model.MediaInfo.ToResource(),
                Home = (int)model.Home,
                Adopted = model.Adopted
            };
        }

        public static BookFileResource ToResource(this BookFile model, NzbDrone.Core.Books.Author author, IUpgradableSpecification upgradableSpecification)
        {
            if (model == null)
            {
                return null;
            }

            return new BookFileResource
            {
                Id = model.Id,

                AuthorId = author.Id,
                BookId = model.Edition.Value?.BookId ?? 0,
                EditionId = model.EditionId,
                Path = model.Path,
                Size = model.Size,
                DateAdded = model.DateAdded,
                Quality = model.Quality,
                QualityWeight = QualityWeight(model.Quality),
                MediaInfo = model.MediaInfo.ToResource(),
                // judged by the profile of the file's media type (the audio profile for an audio file)
                QualityCutoffNotMet = upgradableSpecification.QualityCutoffNotMet(author.QualityProfileFor(model.Edition?.Value?.MediaType ?? MediaType.Archive), model.Quality),
                IndexerFlags = (int)model.IndexerFlags,
                Home = (int)model.Home,
                Adopted = model.Adopted
            };
        }
    }
}
