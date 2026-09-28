using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using Readarr.Api.V1.Author;
using Readarr.Http.REST;
using Swashbuckle.AspNetCore.Annotations;

namespace Readarr.Api.V1.Books
{
    public class BookResource : RestResource
    {
        public string Title { get; set; }

        // Audiobook identity (2026-09-17, D1): the book's own subtitle; read-only (ToModel(resource,
        // book) applies only ApplyChanges' fields to the stored book, so an Edit save keeps it).
        public string Subtitle { get; set; }

        // UI pass (2026-09-24, V1): a light-novel volume's display title (LightNovelTitles.DisplayOf,
        // the string calibre and Audiobookshelf carry); null for manga. Read-only, additive.
        public string DisplayTitle { get; set; }

        // Full-title subtitles (2026-09-24): the subtitle as the display title shows it
        // (LightNovelTitles.DisplaySubtitleOf) -- the series page row reads "Vol. N: <this>"; "" for a
        // light-novel volume with none (so the frontend's merge clears a stale value), null for
        // manga. Read-only, additive.
        public string DisplaySubtitle { get; set; }
        public string AuthorTitle { get; set; }
        public string SeriesTitle { get; set; }
        public string Disambiguation { get; set; }
        public string Overview { get; set; }
        public int AuthorId { get; set; }
        public string ForeignBookId { get; set; }
        public string ForeignEditionId { get; set; }
        public string TitleSlug { get; set; }
        public bool Monitored { get; set; }
        public bool AnyEditionOk { get; set; }
        public Ratings Ratings { get; set; }
        public double VolumeNumber { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string ReleaseDatePrecision { get; set; }
        public int PageCount { get; set; }
        public List<string> Genres { get; set; }
        public AuthorResource Author { get; set; }
        public List<MediaCover> Images { get; set; }
        public List<Links> Links { get; set; }
        public BookStatisticsResource Statistics { get; set; }
        public DateTime? Added { get; set; }
        public AddBookOptions AddOptions { get; set; }
        public string RemoteCover { get; set; }
        public DateTime? LastSearchTime { get; set; }
        public List<EditionResource> Editions { get; set; }
        public List<BookMediaTypeResource> MediaTypes { get; set; }
        public List<MediaType> MissingMediaTypes { get; set; }

        //Hiding this so people don't think its usable (only used to set the initial state)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        [SwaggerIgnore]
        public bool Grabbed { get; set; }
    }

    public static class BookResourceMapper
    {
        public static BookResource ToResource(this Book model)
        {
            if (model == null)
            {
                return null;
            }

            // Book-level display fields (title, cover, overview, links, ratings, foreignEditionId)
            // come from the PRIMARY edition: the monitored Ebook/Archive edition, else the first
            // monitored, else the first. With one edition per book (manga) that is the edition
            // Single() found before; a light-novel volume shows its EPUB edition here and lists
            // both editions in mediaTypes[] (A7).
            var selectedEdition = model.PrimaryEdition();

            var title = selectedEdition?.Title ?? model.Title;
            var authorTitle = $"{model.Author?.Value?.Metadata?.Value?.SortNameLastFirst} {title}";

            var seriesLinks = model.SeriesLinks?.Value?.OrderBy(x => x.SeriesPosition);
            var seriesTitle = seriesLinks?.Select(x => x?.Series?.Value?.Title + (x?.Position.IsNotNullOrWhiteSpace() ?? false ? $" #{x.Position}" : string.Empty)).ConcatToString("; ");

            // Per-edition state (contract "Statistics and wanted"): what each media type has.
            // Book.BookFiles spans every edition; a book built in memory (search results) has none.
            var editions = model.Editions?.Value ?? new List<Edition>();
            var files = model.BookFiles?.Value ?? new List<BookFile>();
            var audioAvailable = model.Author?.Value?.AudioAvailable ?? false;

            var mediaTypes = editions.Select(edition =>
            {
                var editionFiles = files.Where(f => f.EditionId == edition.Id).ToList();

                return new BookMediaTypeResource
                {
                    MediaType = edition.MediaType,
                    Monitored = edition.Monitored,
                    HasFile = editionFiles.Any(),
                    FileCount = editionFiles.Count,
                    Pending = edition.MediaType == MediaType.Audio && !audioAvailable && !editionFiles.Any(),
                    // Unreleased audio (2026-09-21): the badge shows Audible's date (or "not yet"); not missing.
                    Unreleased = edition.MediaType == MediaType.Audio && audioAvailable && !editionFiles.Any() && edition.IsUnreleasedAudio(DateTime.UtcNow),
                    AudioReleaseDate = edition.AudioReleaseDate,
                    // Covered volumes (2026-09-17, D8): the badge and the missing list read the mark.
                    CoveredByVolume = edition.CoveredByVolume,
                    CoveredSource = edition.CoveredSource
                };
            }).ToList();

            return new BookResource
            {
                Id = model.Id,
                AuthorId = model.AuthorId,
                ForeignBookId = model.ForeignBookId,
                ForeignEditionId = selectedEdition?.ForeignEditionId,
                TitleSlug = model.TitleSlug,
                Monitored = model.Monitored,
                AnyEditionOk = model.AnyEditionOk,
                VolumeNumber = model.VolumeNumber,
                ReleaseDate = model.ReleaseDate,
                ReleaseDatePrecision = model.ReleaseDatePrecision,
                PageCount = selectedEdition?.PageCount ?? 0,
                Genres = model.Genres,
                Title = title,
                Subtitle = model.Subtitle,
                DisplayTitle = LightNovelTitles.DisplayOf(model.Author?.Value, model),
                DisplaySubtitle = LightNovelTitles.DisplaySubtitleOf(model.Author?.Value, model),
                AuthorTitle = authorTitle,
                SeriesTitle = seriesTitle,
                Disambiguation = selectedEdition?.Disambiguation,
                Images = selectedEdition?.Images ?? new List<MediaCover>(),
                Links = model.Links.Concat(selectedEdition?.Links ?? new List<Links>()).ToList(),
                Ratings = selectedEdition?.Ratings ?? new Ratings(),
                Added = model.Added,
                LastSearchTime = model.LastSearchTime,
                Editions = editions.ToResource(),
                MediaTypes = mediaTypes,
                MissingMediaTypes = mediaTypes.Where(m => m.Monitored && !m.Pending && !m.Unreleased && !m.HasFile && !m.CoveredByVolume.HasValue).Select(m => m.MediaType).ToList()
            };
        }

        public static Book ToModel(this BookResource resource)
        {
            if (resource == null)
            {
                return null;
            }

            var author = resource.Author?.ToModel() ?? new NzbDrone.Core.Books.Author();

            return new Book
            {
                Id = resource.Id,
                ForeignBookId = resource.ForeignBookId,
                ForeignEditionId = resource.ForeignEditionId,
                TitleSlug = resource.TitleSlug,
                Title = resource.Title,
                VolumeNumber = resource.VolumeNumber,
                Monitored = resource.Monitored,
                AnyEditionOk = resource.AnyEditionOk,
                Editions = resource.Editions.ToModel(),
                AddOptions = resource.AddOptions,
                Author = author,
                AuthorMetadata = author.Metadata.Value
            };
        }

        public static Book ToModel(this BookResource resource, Book book)
        {
            var updatedBook = resource.ToModel();

            book.ApplyChanges(updatedBook);
            book.Editions = updatedBook.Editions;

            return book;
        }

        public static List<BookResource> ToResource(this IEnumerable<Book> models)
        {
            return models?.Select(ToResource).ToList();
        }

        public static List<Book> ToModel(this IEnumerable<BookResource> resources)
        {
            return resources.Select(ToModel).ToList();
        }
    }
}
