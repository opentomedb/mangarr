using System;
using System.Collections.Generic;
using System.Linq;
using Equ;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Books
{
    public class AuthorMetadata : Entity<AuthorMetadata>
    {
        public AuthorMetadata()
        {
            Images = new List<MediaCover.MediaCover>();
            Genres = new List<string>();
            Links = new List<Links>();
            Aliases = new List<string>();
            Ratings = new Ratings();
        }

        public string ForeignAuthorId { get; set; }
        public string TitleSlug { get; set; }
        public string Name { get; set; }
        public string SortName { get; set; }
        public string NameLastFirst { get; set; }
        public string SortNameLastFirst { get; set; }
        public List<string> Aliases { get; set; }
        public string Overview { get; set; }
        public string Disambiguation { get; set; }
        public string Gender { get; set; }
        public string Hometown { get; set; }
        public DateTime? Born { get; set; }
        public DateTime? Died { get; set; }
        public AuthorStatusType Status { get; set; }
        public int TotalVolumes { get; set; }

        // The AniList entry this series is bound to (migration 049, D4). Null = resolve by title
        // on refresh (today's behaviour); set by a strict match, the catalogue's anilist_id, the
        // rebind pass or Fix Match — then refreshes fetch by id and never re-run the search.
        public int? AniListId { get; set; }

        // Preferred Edition (2026-09-24, D1, migration 057): the market edition this series is bound to
        // (null = English), the OpenTome release line it is bound to, and -- when Name is a localized
        // title (D3) -- the English anchor name the metadata sources are asked by (plan A1).
        public string EditionLanguage { get; set; }
        public string TomeLineId { get; set; }
        public string AnchorName { get; set; }

        // Preferred Edition follow-up (2026-09-26, migration 058): the bound line is a collected edition
        // (MangaSeriesMetadataProvider.IsCollectedEdition), recomputed on every refresh. Lets a series bound
        // to an omnibus-only line take marker-named releases (EditionVolumeTokens.IsCollectedSeries).
        public bool EditionCollected { get; set; }

        // Transient (TableMapping ignores it): the languages this work has a line in, filled on a search
        // candidate for the Add form's Edition picker. Never stored.
        // Preferred Edition (2026-09-24, M5 pre-review fix): outside memberwise equality too -- a
        // transient list of reference-compared options must not make two otherwise equal rows differ
        // (the refresh's change detection and EntityFixture compare AuthorMetadata memberwise).
        [MemberwiseEqualityIgnore]
        public List<EditionOption> EditionOptions { get; set; }

        // Line safety (2026-09-28): transient like EditionOptions -- the bound line's count, publisher and
        // "Spin-off of" name, filled on a search candidate only (the Add result's one line). Never stored.
        [MemberwiseEqualityIgnore]
        public CatalogueLineFacts CatalogueLine { get; set; }

        // One copy each (2026-09-20): the real author of a light novel -- Name stays the series;
        // null for manga.
        public string Writer { get; set; }

        // Collection: the series this one is part of (an arc under its parent line, a side story
        // under the main line), from the metadata artifact. Null at the top of a collection.
        public string ParentName { get; set; }
        public string ParentForeignAuthorId { get; set; }
        public List<MediaCover.MediaCover> Images { get; set; }
        public List<Links> Links { get; set; }
        public List<string> Genres { get; set; }
        public Ratings Ratings { get; set; }

        public override string ToString()
        {
            return string.Format("[{0}][{1}]", ForeignAuthorId, Name.NullSafe());
        }

        public override void UseMetadataFrom(AuthorMetadata other)
        {
            ForeignAuthorId = other.ForeignAuthorId;
            TitleSlug = other.TitleSlug;
            Name = other.Name;
            NameLastFirst = other.NameLastFirst;
            SortName = other.SortName;
            SortNameLastFirst = other.SortNameLastFirst;
            // Keep-local when remote is empty (metadata ratchet): a refresh where AniList missed
            // must not wipe the stored alt titles that search recall depends on.
            Aliases = other.Aliases != null && other.Aliases.Any() ? other.Aliases : Aliases;
            Overview = other.Overview.IsNullOrWhiteSpace() ? Overview : other.Overview;
            Disambiguation = other.Disambiguation;
            Gender = other.Gender;
            Hometown = other.Hometown;
            Born = other.Born;
            Died = other.Died;
            Status = other.Status;
            TotalVolumes = other.TotalVolumes;
            // Never un-bind from an empty refresh: a null remote id means "nothing resolved",
            // not "unbind" (unbinding is Fix Match's explicit null, written directly).
            // The refresh path does not call this method (RefreshAuthorService uses
            // Author.UseMetadataFrom and AuthorMetadataRepository.UpsertMany writes the row
            // whole): BookInfoProxy.BuildFakeAuthor's carry-forward is what keeps a stored id
            // across a miss — this ratchet is not redundant with it, and neither replaces the other.
            AniListId = other.AniListId ?? AniListId;
            // Preferred Edition: the binding ratchets like AniListId -- a change of edition is the
            // re-resolve command's explicit write, never a refresh's empty value.
            EditionLanguage = other.EditionLanguage ?? EditionLanguage;
            TomeLineId = other.TomeLineId ?? TomeLineId;
            AnchorName = other.AnchorName ?? AnchorName;
            EditionCollected = other.TomeLineId != null ? other.EditionCollected : EditionCollected;
            // Plain copy: the writer ladder (IWriterResolver) is handed the stored author and owns
            // any keep-existing decision before this is reached.
            Writer = other.Writer;
            ParentName = other.ParentName;
            ParentForeignAuthorId = other.ParentForeignAuthorId;
            Images = other.Images.Any() ? other.Images : Images;
            Links = other.Links;
            Genres = other.Genres;
            Ratings = other.Ratings.Votes > 0 ? other.Ratings : Ratings;
        }
    }
}
