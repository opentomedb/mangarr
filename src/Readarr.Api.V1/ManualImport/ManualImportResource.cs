using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using Readarr.Api.V1.Author;
using Readarr.Api.V1.Books;
using Readarr.Http.REST;

namespace Readarr.Api.V1.ManualImport
{
    public class ManualImportResource : RestResource
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public long Size { get; set; }
        public AuthorResource Author { get; set; }
        public BookResource Book { get; set; }
        public string ForeignEditionId { get; set; }
        public QualityModel Quality { get; set; }
        public string ReleaseGroup { get; set; }
        public int QualityWeight { get; set; }
        public string DownloadId { get; set; }
        public int IndexerFlags { get; set; }
        public IEnumerable<Rejection> Rejections { get; set; }
        public ParsedTrackInfo AudioTags { get; set; }
        public bool AdditionalFile { get; set; }
        public bool ReplaceExistingFiles { get; set; }
        public bool DisableReleaseSwitching { get; set; }
    }

    public static class ManualImportResourceMapper
    {
        public static ManualImportResource ToResource(this ManualImportItem model, IServerMessageLocalizer messages = null)
        {
            if (model == null)
            {
                return null;
            }

            return new ManualImportResource
            {
                Id = model.Id,
                Path = model.Path,
                Name = model.Name,
                Size = model.Size,
                Author = model.Author.ToResource(),
                Book = model.Book.ToResource(),
                // Light novels (2026-09): a two-edition volume has two monitored editions, so the
                // fallback is the edition of the file's class, then the display edition. Manga: the
                // volume's one edition either way.
                ForeignEditionId = model.Edition?.ForeignEditionId
                                   ?? model.Book?.EditionOf(MediaTypes.OfExtension(Path.GetExtension(model.Path)))?.ForeignEditionId
                                   ?? model.Book?.PrimaryEdition()?.ForeignEditionId,
                Quality = model.Quality,
                ReleaseGroup = model.ReleaseGroup,

                //QualityWeight
                DownloadId = model.DownloadId,
                IndexerFlags = model.IndexerFlags,

                // Server messages (2026-09-26): copies in the UI language; the item's own rejections stay English.
                Rejections = messages == null ? model.Rejections : model.Rejections?.Select(r => new Rejection(messages.Localize(r.Reason, r.Text), r.Type)).ToList(),

                AudioTags = model.Tags,
                AdditionalFile = model.AdditionalFile,
                ReplaceExistingFiles = model.ReplaceExistingFiles,
                DisableReleaseSwitching = model.DisableReleaseSwitching
            };
        }

        public static List<ManualImportResource> ToResource(this IEnumerable<ManualImportItem> models, IServerMessageLocalizer messages = null)
        {
            return models.Select(m => m.ToResource(messages)).ToList();
        }
    }
}
