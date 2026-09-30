using System.Collections.Generic;
using NzbDrone.Common.Http.Proxy;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Security;

namespace NzbDrone.Core.Configuration
{
    public interface IConfigService
    {
        void SaveConfigDictionary(Dictionary<string, object> configValues);

        bool IsDefined(string key);

        //Download Client
        string DownloadClientWorkingFolders { get; set; }
        int DownloadClientHistoryLimit { get; set; }

        //Completed/Failed Download Handling (Download client)
        bool EnableCompletedDownloadHandling { get; set; }
        bool AutoRedownloadFailed { get; set; }
        bool AutoRedownloadFailedFromInteractiveSearch { get; set; }

        //Media Management
        bool AutoUnmonitorPreviouslyDownloadedBooks { get; set; }
        string RecycleBin { get; set; }
        int RecycleBinCleanupDays { get; set; }
        ProperDownloadTypes DownloadPropersAndRepacks { get; set; }
        bool CreateEmptyAuthorFolders { get; set; }
        bool DeleteEmptyFolders { get; set; }
        FileDateType FileDate { get; set; }
        bool SkipFreeSpaceCheckWhenImporting { get; set; }
        int MinimumFreeSpaceWhenImporting { get; set; }
        bool CopyUsingHardlinks { get; set; }
        bool ImportExtraFiles { get; set; }
        string ExtraFileExtensions { get; set; }
        bool WatchLibraryForChanges { get; set; }
        RescanAfterRefreshType RescanAfterRefresh { get; set; }
        AllowFingerprinting AllowFingerprinting { get; set; }

        //Permissions (Media Management)
        bool SetPermissionsLinux { get; set; }
        string ChmodFolder { get; set; }
        string ChownGroup { get; set; }

        //Indexers
        int Retention { get; set; }
        int RssSyncInterval { get; set; }
        int BookSearchInterval { get; set; }
        int MaximumSize { get; set; }
        int MinimumAge { get; set; }

        //UI
        int FirstDayOfWeek { get; set; }
        string CalendarWeekColumnHeader { get; set; }

        string ShortDateFormat { get; set; }
        string LongDateFormat { get; set; }
        string TimeFormat { get; set; }
        bool ShowRelativeDates { get; set; }
        bool EnableColorImpairedMode { get; set; }
        int UILanguage { get; set; }

        //Internal
        bool CleanupMetadataImages { get; set; }

        string PlexClientIdentifier { get; }

        //Metadata
        string MetadataSource { get; set; }
        bool MetadataAutoUpdate { get; set; }

        // Where the metadata artifact updater looks for its version manifest. Empty = the
        // built-in default (MetadataUpdateService.DefaultManifestUrl).
        string MetadataManifestUrl { get; set; }

        // Outcome of the last update check ("<utc>: up to date (...)"), for the settings page.
        string MetadataLastCheckResult { get; set; }
        WriteAudioTagsType WriteAudioTags { get; set; }
        bool ScrubAudioTags { get; set; }
        WriteBookTagsType WriteBookTags { get; set; }
        bool UpdateCovers { get; set; }
        bool EmbedMetadata { get; set; }
        WriteComicInfoType WriteComicInfo { get; set; }
        bool PdfToCbzSweep { get; set; }
        bool CheckForNewReleases { get; set; }
        bool LightNovelProfilesSeeded { get; set; }
        string GoogleBooksApiKey { get; set; }

        // Light novels (2026-09). OpenTome: the page the catalogue-miss notice links to.
        // Light-novel storage (2026-09-22): where light-novel files go and how calibre and
        // Audiobookshelf are reached -- all from Settings -> Media Management -> Light novel storage
        // (read through ILightNovelStorage). The password and the API key are masked on the way out.
        string OpenTomeUrl { get; set; }
        string LightNovelEbookHome { get; set; }
        string LightNovelAudioHome { get; set; }
        string CalibreContentServerUrl { get; set; }
        string CalibreLibrary { get; set; }
        string CalibreUsername { get; set; }
        string CalibrePassword { get; set; }
        string CalibreRemotePath { get; set; }
        string CalibreLocalPath { get; set; }
        string AudiobookshelfUrl { get; set; }
        string AudiobookshelfApiKey { get; set; }
        string AudiobookshelfLibraryId { get; set; }
        string AudiobookshelfRemotePath { get; set; }
        string AudiobookshelfLocalPath { get; set; }

        // Preferred light-novel delivery format (2026-09): epub|azw3|kepub, the calibre conversion
        // to add on import -- not a grab order: see LightNovelCalibreSettings.ForConfig.
        string PreferredLightNovelFormat { get; set; }

        // Preferred Edition (2026-09-24): ordered CSV of OpenTome language codes for NEW series
        // ("fr,en"); default "en". Settings -> UI -> Language.
        string PreferredEditionLanguages { get; set; }

        //Forms Auth
        string RijndaelPassphrase { get; }
        string HmacPassphrase { get; }
        string RijndaelSalt { get; }
        string HmacSalt { get; }

        //Proxy
        bool ProxyEnabled { get; }
        ProxyType ProxyType { get; }
        string ProxyHostname { get; }
        int ProxyPort { get; }
        string ProxyUsername { get; }
        string ProxyPassword { get; }
        string ProxyBypassFilter { get; }
        bool ProxyBypassLocalAddresses { get; }

        // Backups
        string BackupFolder { get; }
        int BackupInterval { get; }
        int BackupRetention { get; }

        CertificateValidationType CertificateValidation { get; }
        string ApplicationUrl { get; }
    }
}
