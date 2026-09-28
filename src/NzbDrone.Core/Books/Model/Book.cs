using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Equ;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Books
{
    [DebuggerDisplay("{GetType().FullName} ID = {Id} [{ForeignBookId}][{Title}]")]
    public class Book : Entity<Book>
    {
        public Book()
        {
            Links = new List<Links>();
            Genres = new List<string>();
            RelatedBooks = new List<int>();
            Ratings = new Ratings();
            Author = new Author();
            AddOptions = new AddBookOptions();
        }

        // These correspond to columns in the Books table
        // These are metadata entries
        public int AuthorMetadataId { get; set; }
        public string ForeignBookId { get; set; }
        public string ForeignEditionId { get; set; }
        public string TitleSlug { get; set; }
        public string Title { get; set; }

        // Audiobook identity (2026-09-17, D1): the volume's subtitle (Subtitles.Derive), shown next
        // to the title. Null = none (manga always). Column lands in 051.
        public string Subtitle { get; set; }

        // Fix round 3 (2026-09-24): set by BookInfoProxy on a remote book whose volume had subtitle
        // candidate text this pass (MangaSeriesMetadataProvider.HadSubtitleCandidate) that
        // Subtitles.Derive's junk filter rejected -- UseMetadataFrom clears the stored Subtitle
        // instead of keeping it. Never a column (ignored in TableMapping) and never stored, so it
        // stays out of the up-to-date comparison too -- same shape as Edition.OverviewRejected.
        [MemberwiseEqualityIgnore]
        public bool SubtitleRejected { get; set; }

        public double VolumeNumber { get; set; }
        public DateTime? ReleaseDate { get; set; }

        // Preferred Edition (2026-09-24, D6, migration 057): how precise ReleaseDate is -- null = a day,
        // "month" = a planned month (stored as its last day), "year" = a year (stored as Dec 31, or the
        // catalogue's build date for a current-year deposit copy). Shown as "2019" / "Nov 2026".
        public string ReleaseDatePrecision { get; set; }
        public List<Links> Links { get; set; }
        public List<string> Genres { get; set; }
        public List<int> RelatedBooks { get; set; }
        public Ratings Ratings { get; set; }
        public DateTime? LastSearchTime { get; set; }

        // These are Mangarr generated/config
        public string CleanTitle { get; set; }
        public bool Monitored { get; set; }
        public bool AnyEditionOk { get; set; }
        public DateTime? LastInfoSync { get; set; }
        public DateTime Added { get; set; }
        [MemberwiseEqualityIgnore]
        public AddBookOptions AddOptions { get; set; }

        // These are dynamically queried from other tables
        [MemberwiseEqualityIgnore]
        public LazyLoaded<AuthorMetadata> AuthorMetadata { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<Author> Author { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<List<Edition>> Editions { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<List<BookFile>> BookFiles { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<List<SeriesBookLink>> SeriesLinks { get; set; }

        //compatibility properties with old version of Book
        [MemberwiseEqualityIgnore]
        [JsonIgnore]
        public int AuthorId
        {
            get { return Author?.Value?.Id ?? 0; } set { Author.Value.Id = value; }
        }

        public override string ToString()
        {
            return string.Format("[{0}][{1}]", ForeignBookId, Title.NullSafe());
        }

        public override void UseMetadataFrom(Book other)
        {
            ForeignBookId = other.ForeignBookId;
            ForeignEditionId = other.ForeignEditionId;
            TitleSlug = other.TitleSlug;
            Title = other.Title;

            // Audiobook identity (2026-09-17, D1): a pass that derived no subtitle keeps the one we
            // hold -- UNLESS this pass had candidate text and rejected it (SubtitleRejected, fix
            // round 3, 2026-09-24), which clears it instead. A junk subtitle already stored before
            // the filter existed is data that must not survive a refresh.
            Subtitle = other.SubtitleRejected ? null : (other.Subtitle.IsNullOrWhiteSpace() ? Subtitle : other.Subtitle);
            VolumeNumber = other.VolumeNumber;

            // Metadata ratchet: never erase a known release date with an empty resolution
            // (mirrors Edition.UseMetadataFrom).
            ReleaseDate = other.ReleaseDate ?? ReleaseDate;
            ReleaseDatePrecision = other.ReleaseDate.HasValue ? other.ReleaseDatePrecision : ReleaseDatePrecision;
            Links = other.Links;
            Genres = other.Genres;
            RelatedBooks = other.RelatedBooks;
            Ratings = other.Ratings;
            CleanTitle = other.CleanTitle;
        }

        public override void UseDbFieldsFrom(Book other)
        {
            Id = other.Id;
            AuthorMetadataId = other.AuthorMetadataId;
            Monitored = other.Monitored;
            AnyEditionOk = other.AnyEditionOk;
            LastInfoSync = other.LastInfoSync;
            LastSearchTime = other.LastSearchTime;
            Added = other.Added;
            AddOptions = other.AddOptions;
        }

        public override void ApplyChanges(Book other)
        {
            ForeignBookId = other.ForeignBookId;
            ForeignEditionId = other.ForeignEditionId;
            AddOptions = other.AddOptions;
            Monitored = other.Monitored;
            AnyEditionOk = other.AnyEditionOk;
        }
    }
}
