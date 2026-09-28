using System;
using System.Collections.Generic;
using Equ;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Books
{
    public class Author : Entity<Author>
    {
        public Author()
        {
            Tags = new HashSet<int>();
            Metadata = new AuthorMetadata();
        }

        // These correspond to columns in the Authors table
        public int AuthorMetadataId { get; set; }
        public string CleanName { get; set; }
        public bool Monitored { get; set; }
        public NewItemMonitorTypes MonitorNewItems { get; set; }
        public DateTime? LastInfoSync { get; set; }
        public string Path { get; set; }
        public string RootFolderPath { get; set; }
        public DateTime Added { get; set; }
        public int QualityProfileId { get; set; }
        public int MetadataProfileId { get; set; }

        // Light novels (2026-09): the audiobook profile (null = QualityProfileId judges audio too),
        // whether the series has any audio yet (false = its Audio editions are wanted but PENDING:
        // shown "not yet", searched only by the weekly best-effort probe) and when that probe last
        // ran. A manga entry never sets any of these.
        public int? AudioQualityProfileId { get; set; }
        public bool AudioAvailable { get; set; }
        public DateTime? LastAudioSearch { get; set; }

        // Copy-in hold (2026-09-16, D1): true from add until ImportExistingLightNovels has run clean
        // for this entry. Only ever true for a light novel. While true no automatic search or RSS
        // grab may touch the entry (ReleaseSearchService, BookSearchService, CopyInPendingSpecification).
        public bool CopyInPending { get; set; }

        // One copy each (2026-09-20): opt-in: write tags into adopted audio files; default off --
        // adopted originals are otherwise never written. Only ever meaningful for a light novel.
        public bool AdoptedTagWrite { get; set; }

        // Manga: manual cap on how many volumes this series has (0 = auto from indexer search). Phase 3.
        public int MaxVolume { get; set; }

        public HashSet<int> Tags { get; set; }
        [MemberwiseEqualityIgnore]
        public AddAuthorOptions AddOptions { get; set; }

        // Dynamically loaded from DB
        [MemberwiseEqualityIgnore]
        public LazyLoaded<AuthorMetadata> Metadata { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<QualityProfile> QualityProfile { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<QualityProfile> AudioQualityProfile { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<MetadataProfile> MetadataProfile { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<List<Book>> Books { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<List<Series>> Series { get; set; }

        //compatibility properties
        [MemberwiseEqualityIgnore]
        public string Name
        {
            get { return Metadata.Value.Name; } set { Metadata.Value.Name = value; }
        }

        [MemberwiseEqualityIgnore]
        public string ForeignAuthorId
        {
            get { return Metadata.Value.ForeignAuthorId; } set { Metadata.Value.ForeignAuthorId = value; }
        }

        // Derived from the foreign id (see LibraryType.cs); never a column.
        [MemberwiseEqualityIgnore]
        public LibraryType Library => LibraryTypes.Parse(Metadata?.Value?.ForeignAuthorId);

        // The profile that judges a release or file of this media type: the audio profile for
        // Audio (falling back to the main profile when none is set), the main profile otherwise.
        // A manga entry is never asked about Audio, so it always gets its one profile.
        public QualityProfile QualityProfileFor(MediaType mediaType)
        {
            return mediaType == MediaType.Audio
                ? (AudioQualityProfile?.Value ?? QualityProfile?.Value)
                : QualityProfile?.Value;
        }

        // 0 counts as unset, matching the mapping (HasOne loads only > 0) and the validator.
        public int QualityProfileIdFor(MediaType mediaType)
        {
            return mediaType == MediaType.Audio && AudioQualityProfileId > 0
                ? AudioQualityProfileId.Value
                : QualityProfileId;
        }

        public override string ToString()
        {
            return string.Format("[{0}][{1}]", Metadata.Value.ForeignAuthorId.NullSafe(), Metadata.Value.Name.NullSafe());
        }

        public override void UseMetadataFrom(Author other)
        {
            CleanName = other.CleanName;
        }

        public override void UseDbFieldsFrom(Author other)
        {
            Id = other.Id;
            AuthorMetadataId = other.AuthorMetadataId;
            Monitored = other.Monitored;
            MonitorNewItems = other.MonitorNewItems;
            LastInfoSync = other.LastInfoSync;
            Path = other.Path;
            RootFolderPath = other.RootFolderPath;
            Added = other.Added;
            QualityProfileId = other.QualityProfileId;
            QualityProfile = other.QualityProfile;
            AudioQualityProfileId = other.AudioQualityProfileId;
            AudioQualityProfile = other.AudioQualityProfile;
            AudioAvailable = other.AudioAvailable;
            LastAudioSearch = other.LastAudioSearch;
            CopyInPending = other.CopyInPending;
            AdoptedTagWrite = other.AdoptedTagWrite;
            MetadataProfileId = other.MetadataProfileId;
            MetadataProfile = other.MetadataProfile;
            MaxVolume = other.MaxVolume;
            Tags = other.Tags;
            AddOptions = other.AddOptions;
        }

        public override void ApplyChanges(Author other)
        {
            Path = other.Path;
            QualityProfileId = other.QualityProfileId;
            QualityProfile = other.QualityProfile;
            AudioQualityProfileId = other.AudioQualityProfileId;
            AudioQualityProfile = other.AudioQualityProfile;
            AudioAvailable = other.AudioAvailable;
            CopyInPending = other.CopyInPending;
            AdoptedTagWrite = other.AdoptedTagWrite;
            MetadataProfileId = other.MetadataProfileId;
            MetadataProfile = other.MetadataProfile;
            MaxVolume = other.MaxVolume;

            Books = other.Books;
            Tags = other.Tags;
            AddOptions = other.AddOptions;
            RootFolderPath = other.RootFolderPath;
            Monitored = other.Monitored;
            MonitorNewItems = other.MonitorNewItems;
        }
    }
}
