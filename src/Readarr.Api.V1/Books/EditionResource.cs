using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using Readarr.Http.REST;
using Swashbuckle.AspNetCore.Annotations;

namespace Readarr.Api.V1.Books
{
    public class EditionResource : RestResource
    {
        public int BookId { get; set; }
        public string ForeignEditionId { get; set; }
        public string TitleSlug { get; set; }
        public string Isbn13 { get; set; }
        public string Asin { get; set; }

        // Audiobook identity (2026-09-17, D1): read-only for clients, but carried through ToModel
        // too — the Edit-book save (BookController.UpdateBook) rebuilds every edition from the
        // resource and UpdateMany rewrites the whole row, so a field left out of ToModel is nulled
        // on every save (same reason Isbn13/Asin round-trip). CoveredSource (D4a: who wrote
        // CoveredByVolume — "audible" | "import" | null) is owned by the services and read-only in
        // intent, but round-trips for the same reason: left out, a save would null an "import"
        // mark's source and the next Audible-asserted refresh would overwrite the mark.
        public string AudiobookTitle { get; set; }
        public string AudiobookSubtitle { get; set; }
        public int? RuntimeMinutes { get; set; }
        public DateTime? AudioReleaseDate { get; set; }
        public double? CoveredByVolume { get; set; }
        public string CoveredSource { get; set; }
        public string Title { get; set; }
        public string Language { get; set; }
        public string Overview { get; set; }
        public string Format { get; set; }
        public bool IsEbook { get; set; }
        public MediaType MediaType { get; set; }
        public string Disambiguation { get; set; }
        public string Publisher { get; set; }
        public int PageCount { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public List<MediaCover> Images { get; set; }
        public List<Links> Links { get; set; }
        public Ratings Ratings { get; set; }
        public bool Monitored { get; set; }
        public bool ManualAdd { get; set; }
        public string RemoteCover { get; set; }

        //Hiding this so people don't think its usable (only used to set the initial state)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        [SwaggerIgnore]
        public bool Grabbed { get; set; }
    }

    public static class EditionResourceMapper
    {
        public static EditionResource ToResource(this Edition model)
        {
            if (model == null)
            {
                return null;
            }

            return new EditionResource
            {
                Id = model.Id,
                BookId = model.BookId,
                ForeignEditionId = model.ForeignEditionId,
                TitleSlug = model.TitleSlug,
                Isbn13 = model.Isbn13,
                Asin = model.Asin,
                AudiobookTitle = model.AudiobookTitle,
                AudiobookSubtitle = model.AudiobookSubtitle,
                RuntimeMinutes = model.RuntimeMinutes,
                AudioReleaseDate = model.AudioReleaseDate,
                CoveredByVolume = model.CoveredByVolume,
                CoveredSource = model.CoveredSource,
                Title = model.Title,
                Language = model.Language,
                Overview = model.Overview,
                Format = model.Format,
                IsEbook = model.IsEbook,
                MediaType = model.MediaType,
                Disambiguation = model.Disambiguation,
                Publisher = model.Publisher,
                PageCount = model.PageCount,
                ReleaseDate = model.ReleaseDate,
                Images = model.Images,
                Links = model.Links,
                Ratings = model.Ratings,
                Monitored = model.Monitored,
                ManualAdd = model.ManualAdd
            };
        }

        public static Edition ToModel(this EditionResource resource)
        {
            if (resource == null)
            {
                return null;
            }

            return new Edition
            {
                Id = resource.Id,
                BookId = resource.BookId,
                ForeignEditionId = resource.ForeignEditionId,
                TitleSlug = resource.TitleSlug,
                Isbn13 = resource.Isbn13,
                Asin = resource.Asin,
                AudiobookTitle = resource.AudiobookTitle,
                AudiobookSubtitle = resource.AudiobookSubtitle,
                RuntimeMinutes = resource.RuntimeMinutes,
                AudioReleaseDate = resource.AudioReleaseDate,
                CoveredByVolume = resource.CoveredByVolume,
                CoveredSource = resource.CoveredSource,
                Title = resource.Title,
                Language = resource.Language,
                Overview = resource.Overview,
                Format = resource.Format,
                IsEbook = resource.IsEbook,
                MediaType = resource.MediaType,
                Disambiguation = resource.Disambiguation,
                Publisher = resource.Publisher,
                PageCount = resource.PageCount,
                ReleaseDate = resource.ReleaseDate,
                Images = resource.Images,
                Links = resource.Links,
                Ratings = resource.Ratings,
                Monitored = resource.Monitored,
                ManualAdd = resource.ManualAdd
            };
        }

        public static List<EditionResource> ToResource(this IEnumerable<Edition> models)
        {
            return models?.Select(ToResource).ToList();
        }

        public static List<Edition> ToModel(this IEnumerable<EditionResource> resources)
        {
            return resources.Select(ToModel).ToList();
        }
    }
}
