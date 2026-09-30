using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using Readarr.Http.REST;
using STJ = System.Text.Json.Serialization;

namespace Readarr.Api.V1.Author
{
    public class AuthorResource : RestResource
    {
        //Todo: Sorters should be done completely on the client
        //Todo: Is there an easy way to keep IgnoreArticlesWhenSorting in sync between, Series, History, Missing?
        //Todo: We should get the entire Profile instead of ID and Name separately
        [JsonIgnore]
        public int AuthorMetadataId { get; set; }
        public AuthorStatusType Status { get; set; }

        public bool Ended => Status == AuthorStatusType.Ended;

        public string AuthorName { get; set; }
        public string AuthorNameLastFirst { get; set; }
        public string ForeignAuthorId { get; set; }
        public string TitleSlug { get; set; }
        public LibraryType Library { get; set; }
        public string Overview { get; set; }
        public int TotalVolumes { get; set; }
        public string ParentName { get; set; }
        public string ParentForeignAuthorId { get; set; }

        // Written even when null: the app's STJson default is WhenWritingNull, which would drop
        // "aniListId" from the Fix Match PUT's 202 body and from SignalR's updated resource, so
        // the store's merge (updateItem) could never clear the old id after an Unbind. Aliased
        // because this file's bare [JsonIgnore] above is Newtonsoft's (and Readarr.Api.V1.System
        // shadows System.* inside this namespace).
        [STJ.JsonIgnore(Condition = STJ.JsonIgnoreCondition.Never)]
        public int? AniListId { get; set; }

        // Preferred Edition (2026-09-24): read-only on the wire (the re-resolve command writes them).
        public string EditionLanguage { get; set; }
        public string TomeLineId { get; set; }

        // Search candidates only: the languages the work has a line in (Add form picker). Fully
        // qualified like the file's other Core types (this namespace is Readarr.Api.V1.Author).
        public List<NzbDrone.Core.Books.EditionOption> EditionOptions { get; set; }

        // Line safety (2026-09-28): search candidates only (null elsewhere, so a library series' JSON is
        // unchanged): the bound line's volume count, publisher and "Spin-off of" name.
        public NzbDrone.Core.Books.CatalogueLineFacts CatalogueLine { get; set; }

        // One copy each (2026-09-20): the real author of a light novel (null for manga). Read-only on
        // the wire: set by the writer ladder / a pin, never by a PUT.
        public string Writer { get; set; }
        public string Disambiguation { get; set; }
        public List<Links> Links { get; set; }

        public Book NextBook { get; set; }
        public Book LastBook { get; set; }

        public List<MediaCover> Images { get; set; }

        public string RemotePoster { get; set; }

        //View & Edit
        public string Path { get; set; }
        public int QualityProfileId { get; set; }
        public int? AudioQualityProfileId { get; set; }
        public int MetadataProfileId { get; set; }
        public int MaxVolume { get; set; }

        //Editing Only
        public bool Monitored { get; set; }
        public NewItemMonitorTypes MonitorNewItems { get; set; }
        public bool AudioAvailable { get; set; }
        public bool CopyInPending { get; set; }
        public bool AdoptedTagWrite { get; set; }

        public string RootFolderPath { get; set; }
        public string Folder { get; set; }
        public List<string> Genres { get; set; }
        public string CleanName { get; set; }
        public string SortName { get; set; }
        public string SortNameLastFirst { get; set; }

        public HashSet<int> Tags { get; set; }
        public DateTime Added { get; set; }
        public AddAuthorOptions AddOptions { get; set; }
        public Ratings Ratings { get; set; }

        public AuthorStatisticsResource Statistics { get; set; }
    }

    public static class AuthorResourceMapper
    {
        public static AuthorResource ToResource(this NzbDrone.Core.Books.Author model)
        {
            if (model == null)
            {
                return null;
            }

            return new AuthorResource
            {
                Id = model.Id,
                AuthorMetadataId = model.AuthorMetadataId,

                AuthorName = model.Name,
                AuthorNameLastFirst = model.Metadata.Value.NameLastFirst,

                //AlternateTitles
                SortName = model.Metadata.Value.SortName,
                SortNameLastFirst = model.Metadata.Value.SortNameLastFirst,

                Status = model.Metadata.Value.Status,
                Overview = model.Metadata.Value.Overview,
                TotalVolumes = model.Metadata.Value.TotalVolumes,
                ParentName = model.Metadata.Value.ParentName,
                ParentForeignAuthorId = model.Metadata.Value.ParentForeignAuthorId,
                AniListId = model.Metadata.Value.AniListId,
                EditionLanguage = model.Metadata.Value.EditionLanguage,
                TomeLineId = model.Metadata.Value.TomeLineId,
                EditionOptions = model.Metadata.Value.EditionOptions,
                CatalogueLine = model.Metadata.Value.CatalogueLine,
                Writer = model.Metadata.Value.Writer,
                Disambiguation = model.Metadata.Value.Disambiguation,

                Images = model.Metadata.Value.Images.JsonClone(),

                Path = model.Path,
                QualityProfileId = model.QualityProfileId,
                AudioQualityProfileId = model.AudioQualityProfileId,
                MetadataProfileId = model.MetadataProfileId,
                MaxVolume = model.MaxVolume,
                Links = model.Metadata.Value.Links,

                Monitored = model.Monitored,
                MonitorNewItems = model.MonitorNewItems,
                AudioAvailable = model.AudioAvailable,
                CopyInPending = model.CopyInPending,
                AdoptedTagWrite = model.AdoptedTagWrite,

                CleanName = model.CleanName,
                ForeignAuthorId = model.Metadata.Value.ForeignAuthorId,
                TitleSlug = model.Metadata.Value.TitleSlug,
                Library = model.Library,

                // Root folder path is now calculated from the author path
                // RootFolderPath = model.RootFolderPath,
                Genres = model.Metadata.Value.Genres,
                Tags = model.Tags,
                Added = model.Added,
                AddOptions = model.AddOptions,
                Ratings = model.Metadata.Value.Ratings,

                Statistics = new AuthorStatisticsResource()
            };
        }

        public static NzbDrone.Core.Books.Author ToModel(this AuthorResource resource)
        {
            if (resource == null)
            {
                return null;
            }

            return new NzbDrone.Core.Books.Author
            {
                Id = resource.Id,

                Metadata = new NzbDrone.Core.Books.AuthorMetadata
                {
                    ForeignAuthorId = resource.ForeignAuthorId,
                    TitleSlug = resource.TitleSlug,
                    Name = resource.AuthorName,
                    NameLastFirst = resource.AuthorNameLastFirst,
                    SortName = resource.SortName,
                    SortNameLastFirst = resource.SortNameLastFirst,
                    Status = resource.Status,
                    Overview = resource.Overview,
                    TotalVolumes = resource.TotalVolumes,
                    ParentName = resource.ParentName,
                    ParentForeignAuthorId = resource.ParentForeignAuthorId,
                    AniListId = resource.AniListId,
                    Links = resource.Links,
                    Images = resource.Images,
                    Genres = resource.Genres,
                    Ratings = resource.Ratings,
                },

                //AlternateTitles
                Path = resource.Path,
                QualityProfileId = resource.QualityProfileId,
                AudioQualityProfileId = resource.AudioQualityProfileId,
                MetadataProfileId = resource.MetadataProfileId,
                MaxVolume = resource.MaxVolume,

                Monitored = resource.Monitored,
                MonitorNewItems = resource.MonitorNewItems,
                AudioAvailable = resource.AudioAvailable,
                CopyInPending = resource.CopyInPending,
                AdoptedTagWrite = resource.AdoptedTagWrite,

                CleanName = resource.CleanName,
                RootFolderPath = resource.RootFolderPath,

                Tags = resource.Tags,
                Added = resource.Added,
                AddOptions = resource.AddOptions
            };
        }

        public static NzbDrone.Core.Books.Author ToModel(this AuthorResource resource, NzbDrone.Core.Books.Author author)
        {
            var updatedAuthor = resource.ToModel();

            author.ApplyChanges(updatedAuthor);

            return author;
        }

        public static List<AuthorResource> ToResource(this IEnumerable<NzbDrone.Core.Books.Author> author)
        {
            return author.Select(ToResource).ToList();
        }

        public static List<NzbDrone.Core.Books.Author> ToModel(this IEnumerable<AuthorResource> resources)
        {
            return resources.Select(ToModel).ToList();
        }
    }
}
