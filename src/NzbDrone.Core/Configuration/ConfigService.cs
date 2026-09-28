using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Http.Proxy;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Security;

namespace NzbDrone.Core.Configuration
{
    public enum ConfigKey
    {
        DownloadedBooksFolder
    }

    public class ConfigService : IConfigService
    {
        private readonly IConfigRepository _repository;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private static Dictionary<string, string> _cache;

        public ConfigService(IConfigRepository repository, IEventAggregator eventAggregator, Logger logger)
        {
            _repository = repository;
            _eventAggregator = eventAggregator;
            _logger = logger;
            _cache = new Dictionary<string, string>();
        }

        private Dictionary<string, object> AllWithDefaults()
        {
            var dict = new Dictionary<string, object>(StringComparer.InvariantCultureIgnoreCase);

            var type = GetType();
            var properties = type.GetProperties();

            foreach (var propertyInfo in properties)
            {
                var value = propertyInfo.GetValue(this, null);
                dict.Add(propertyInfo.Name, value);
            }

            return dict;
        }

        public void SaveConfigDictionary(Dictionary<string, object> configValues)
        {
            var allWithDefaults = AllWithDefaults();

            foreach (var configValue in configValues)
            {
                allWithDefaults.TryGetValue(configValue.Key, out var currentValue);
                if (currentValue == null || configValue.Value == null)
                {
                    continue;
                }

                var equal = configValue.Value.ToString().Equals(currentValue.ToString());

                if (!equal)
                {
                    SetValue(configValue.Key, configValue.Value.ToString());
                }
            }

            _eventAggregator.PublishEvent(new ConfigSavedEvent());
        }

        public bool IsDefined(string key)
        {
            return _repository.Get(key.ToLower()) != null;
        }

        public bool AutoUnmonitorPreviouslyDownloadedBooks
        {
            get { return GetValueBoolean("AutoUnmonitorPreviouslyDownloadedBooks"); }
            set { SetValue("AutoUnmonitorPreviouslyDownloadedBooks", value); }
        }

        public int Retention
        {
            get { return GetValueInt("Retention", 0); }
            set { SetValue("Retention", value); }
        }

        public string RecycleBin
        {
            get { return GetValue("RecycleBin", string.Empty); }
            set { SetValue("RecycleBin", value); }
        }

        public int RecycleBinCleanupDays
        {
            get { return GetValueInt("RecycleBinCleanupDays", 7); }
            set { SetValue("RecycleBinCleanupDays", value); }
        }

        public int RssSyncInterval
        {
            get { return GetValueInt("RssSyncInterval", 15); }

            set { SetValue("RssSyncInterval", value); }
        }

        // Manga: recurring automatic search for monitored, missing volumes. RSS Sync only catches
        // releases that happen to be in an indexer's newest-items feed; ongoing series need an
        // active per-volume search to fill gaps (and pick up newly-released volumes). Minutes; 0
        // disables. Default 360 (every 6h).
        public int BookSearchInterval
        {
            get { return GetValueInt("BookSearchInterval", 360); }

            set { SetValue("BookSearchInterval", value); }
        }

        public int MaximumSize
        {
            get { return GetValueInt("MaximumSize", 0); }

            set { SetValue("MaximumSize", value); }
        }

        public int MinimumAge
        {
            get { return GetValueInt("MinimumAge", 0); }

            set { SetValue("MinimumAge", value); }
        }

        public ProperDownloadTypes DownloadPropersAndRepacks
        {
            get { return GetValueEnum("DownloadPropersAndRepacks", ProperDownloadTypes.PreferAndUpgrade); }

            set { SetValue("DownloadPropersAndRepacks", value); }
        }

        public bool EnableCompletedDownloadHandling
        {
            get { return GetValueBoolean("EnableCompletedDownloadHandling", true); }

            set { SetValue("EnableCompletedDownloadHandling", value); }
        }

        public bool AutoRedownloadFailed
        {
            get { return GetValueBoolean("AutoRedownloadFailed", true); }

            set { SetValue("AutoRedownloadFailed", value); }
        }

        public bool AutoRedownloadFailedFromInteractiveSearch
        {
            get { return GetValueBoolean("AutoRedownloadFailedFromInteractiveSearch", true); }

            set { SetValue("AutoRedownloadFailedFromInteractiveSearch", value); }
        }

        public bool CreateEmptyAuthorFolders
        {
            get { return GetValueBoolean("CreateEmptyAuthorFolders", false); }

            set { SetValue("CreateEmptyAuthorFolders", value); }
        }

        public bool DeleteEmptyFolders
        {
            get { return GetValueBoolean("DeleteEmptyFolders", false); }

            set { SetValue("DeleteEmptyFolders", value); }
        }

        public FileDateType FileDate
        {
            get { return GetValueEnum("FileDate", FileDateType.None); }

            set { SetValue("FileDate", value); }
        }

        public string DownloadClientWorkingFolders
        {
            get { return GetValue("DownloadClientWorkingFolders", "_UNPACK_|_FAILED_"); }
            set { SetValue("DownloadClientWorkingFolders", value); }
        }

        public int DownloadClientHistoryLimit
        {
            get { return GetValueInt("DownloadClientHistoryLimit", 60); }

            set { SetValue("DownloadClientHistoryLimit", value); }
        }

        public bool SkipFreeSpaceCheckWhenImporting
        {
            get { return GetValueBoolean("SkipFreeSpaceCheckWhenImporting", false); }

            set { SetValue("SkipFreeSpaceCheckWhenImporting", value); }
        }

        public int MinimumFreeSpaceWhenImporting
        {
            get { return GetValueInt("MinimumFreeSpaceWhenImporting", 100); }

            set { SetValue("MinimumFreeSpaceWhenImporting", value); }
        }

        public bool CopyUsingHardlinks
        {
            get { return GetValueBoolean("CopyUsingHardlinks", true); }

            set { SetValue("CopyUsingHardlinks", value); }
        }

        public bool ImportExtraFiles
        {
            get { return GetValueBoolean("ImportExtraFiles", false); }

            set { SetValue("ImportExtraFiles", value); }
        }

        public string ExtraFileExtensions
        {
            get { return GetValue("ExtraFileExtensions", "srt"); }

            set { SetValue("ExtraFileExtensions", value); }
        }

        public bool WatchLibraryForChanges
        {
            get { return GetValueBoolean("WatchLibraryForChanges", true); }

            set { SetValue("WatchLibraryForChanges", value); }
        }

        public RescanAfterRefreshType RescanAfterRefresh
        {
            get { return GetValueEnum("RescanAfterRefresh", RescanAfterRefreshType.Always); }

            set { SetValue("RescanAfterRefresh", value); }
        }

        public AllowFingerprinting AllowFingerprinting
        {
            get { return GetValueEnum("AllowFingerprinting", AllowFingerprinting.NewFiles); }

            set { SetValue("AllowFingerprinting", value); }
        }

        public bool SetPermissionsLinux
        {
            get { return GetValueBoolean("SetPermissionsLinux", false); }

            set { SetValue("SetPermissionsLinux", value); }
        }

        public string ChmodFolder
        {
            get { return GetValue("ChmodFolder", "755"); }

            set { SetValue("ChmodFolder", value); }
        }

        public string ChownGroup
        {
            get { return GetValue("ChownGroup", ""); }

            set { SetValue("ChownGroup", value); }
        }

        public string MetadataSource
        {
            get { return GetValue("MetadataSource", ""); }

            set { SetValue("MetadataSource", value); }
        }

        public bool MetadataAutoUpdate
        {
            get { return GetValueBoolean("MetadataAutoUpdate", true); }

            set { SetValue("MetadataAutoUpdate", value); }
        }

        public string MetadataManifestUrl
        {
            get { return GetValue("MetadataManifestUrl", ""); }

            set { SetValue("MetadataManifestUrl", value); }
        }

        public string MetadataLastCheckResult
        {
            get { return GetValue("MetadataLastCheckResult", ""); }

            set { SetValue("MetadataLastCheckResult", value); }
        }

        public WriteAudioTagsType WriteAudioTags
        {
            get { return GetValueEnum("WriteAudioTags", WriteAudioTagsType.No); }

            set { SetValue("WriteAudioTags", value); }
        }

        public bool ScrubAudioTags
        {
            get { return GetValueBoolean("ScrubAudioTags", false); }

            set { SetValue("ScrubAudioTags", value); }
        }

        public WriteBookTagsType WriteBookTags
        {
            get { return GetValueEnum("WriteBookTags", WriteBookTagsType.NewFiles); }

            set { SetValue("WriteBookTags", value); }
        }

        public bool UpdateCovers
        {
            get { return GetValueBoolean("UpdateCovers", true); }

            set { SetValue("UpdateCovers", value); }
        }

        public bool EmbedMetadata
        {
            get { return GetValueBoolean("EmbedMetadata", false); }

            set { SetValue("EmbedMetadata", value); }
        }

        // Beta readiness (2026-09-28, F2): ComicInfo.xml on import. Default NewDownloads (a download-client
        // item only), so a first Library Import never rewrites the CBZs a user already curated; an
        // existing library keeps AllImports through migration 059's explicit row.
        public WriteComicInfoType WriteComicInfo
        {
            get { return GetValueEnum("WriteComicInfo", WriteComicInfoType.NewDownloads); }

            set { SetValue("WriteComicInfo", value); }
        }

        // Beta readiness (2026-09-28, F3): the daily whole-library PDF->CBZ sweep. Off by default (it
        // converts a user's existing manga PDFs in place); an existing library keeps it on through
        // migration 059. Off = the task is manual only (System -> Tasks). Import-time conversion of
        // grabs (PdfImportConversionService) does not read this.
        public bool PdfToCbzSweep
        {
            get { return GetValueBoolean("PdfToCbzSweep", false); }

            set { SetValue("PdfToCbzSweep", value); }
        }

        // Beta readiness (2026-09-28, F7): the two light-novel quality profiles are seeded once; a user who
        // deletes or renames one keeps it that way (QualityProfileService). Not a user setting.
        public bool LightNovelProfilesSeeded
        {
            get { return GetValueBoolean("LightNovelProfilesSeeded", false); }

            set { SetValue("LightNovelProfilesSeeded", value); }
        }

        // Optional personal Google Books API key (raises the metadata-lookup quota). The
        // GOOGLE_BOOKS_API_KEY container variable remains the fallback when this is blank.
        public string GoogleBooksApiKey
        {
            get { return GetValue("GoogleBooksApiKey", string.Empty); }

            set { SetValue("GoogleBooksApiKey", value); }
        }

        // The public page for adding or correcting a series (spec §6.4); the catalogue-miss link opens it as-is.
        public string OpenTomeUrl
        {
            get { return GetValue("OpenTomeUrl", "https://opentomedb.com/contribute/"); }

            set { SetValue("OpenTomeUrl", value); }
        }

        public string PreferredLightNovelFormat
        {
            get { return GetValue("PreferredLightNovelFormat", "epub"); }

            set { SetValue("PreferredLightNovelFormat", value); }
        }

        public string PreferredEditionLanguages
        {
            get { return GetValue("PreferredEditionLanguages", "en"); }

            set { SetValue("PreferredEditionLanguages", value); }
        }

        public string CalibreContentServerUrl
        {
            get { return GetValue("CalibreContentServerUrl", string.Empty); }

            set { SetValue("CalibreContentServerUrl", value); }
        }

        public string AudiobookshelfUrl
        {
            get { return GetValue("AudiobookshelfUrl", string.Empty); }

            set { SetValue("AudiobookshelfUrl", value); }
        }

        public string AudiobookshelfApiKey
        {
            get { return GetValue("AudiobookshelfApiKey", string.Empty); }

            set { SetValue("AudiobookshelfApiKey", value); }
        }

        public string AudiobookshelfLibraryId
        {
            get { return GetValue("AudiobookshelfLibraryId", string.Empty); }

            set { SetValue("AudiobookshelfLibraryId", value); }
        }

        // Light-novel storage (2026-09-22): "entry" (the series folder, like manga -- works with nothing
        // else installed) or the reader's own library: "calibre" for ebooks, "audiobookshelf" for audio.
        // ILightNovelStorage reads anything else as "entry".
        public string LightNovelEbookHome
        {
            get { return GetValue("LightNovelEbookHome", "entry"); }

            set { SetValue("LightNovelEbookHome", value); }
        }

        public string LightNovelAudioHome
        {
            get { return GetValue("LightNovelAudioHome", "entry"); }

            set { SetValue("LightNovelAudioHome", value); }
        }

        // Blank = the server's default library (ajax/library-info, LightNovelCalibreSettings.ForConfig).
        public string CalibreLibrary
        {
            get { return GetValue("CalibreLibrary", string.Empty); }

            set { SetValue("CalibreLibrary", value); }
        }

        // Optional login for a calibre that only accepts writes from a user (not trusted_ips).
        public string CalibreUsername
        {
            get { return GetValue("CalibreUsername", string.Empty); }

            set { SetValue("CalibreUsername", value); }
        }

        public string CalibrePassword
        {
            get { return GetValue("CalibrePassword", string.Empty); }

            set { SetValue("CalibrePassword", value); }
        }

        // "Path as calibre sees it" -> "Path as Mangarr sees it"; both blank = the same path, no check.
        public string CalibreRemotePath
        {
            get { return GetValue("CalibreRemotePath", string.Empty); }

            set { SetValue("CalibreRemotePath", value); }
        }

        public string CalibreLocalPath
        {
            get { return GetValue("CalibreLocalPath", string.Empty); }

            set { SetValue("CalibreLocalPath", value); }
        }

        // The Audiobookshelf library's folder as ABS sees it -> as Mangarr sees it; both blank = the same path.
        public string AudiobookshelfRemotePath
        {
            get { return GetValue("AudiobookshelfRemotePath", string.Empty); }

            set { SetValue("AudiobookshelfRemotePath", value); }
        }

        public string AudiobookshelfLocalPath
        {
            get { return GetValue("AudiobookshelfLocalPath", string.Empty); }

            set { SetValue("AudiobookshelfLocalPath", value); }
        }

        public int FirstDayOfWeek
        {
            get { return GetValueInt("FirstDayOfWeek", (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek); }

            set { SetValue("FirstDayOfWeek", value); }
        }

        public string CalendarWeekColumnHeader
        {
            get { return GetValue("CalendarWeekColumnHeader", "ddd M/D"); }

            set { SetValue("CalendarWeekColumnHeader", value); }
        }

        public string ShortDateFormat
        {
            get { return GetValue("ShortDateFormat", "MMM D YYYY"); }

            set { SetValue("ShortDateFormat", value); }
        }

        public string LongDateFormat
        {
            get { return GetValue("LongDateFormat", "dddd, MMMM D YYYY"); }

            set { SetValue("LongDateFormat", value); }
        }

        public string TimeFormat
        {
            get { return GetValue("TimeFormat", "h(:mm)a"); }

            set { SetValue("TimeFormat", value); }
        }

        public bool ShowRelativeDates
        {
            get { return GetValueBoolean("ShowRelativeDates", true); }

            set { SetValue("ShowRelativeDates", value); }
        }

        public bool EnableColorImpairedMode
        {
            get { return GetValueBoolean("EnableColorImpairedMode", false); }

            set { SetValue("EnableColorImpairedMode", value); }
        }

        public int UILanguage
        {
            get { return GetValueInt("UILanguage", (int)Language.English); }

            set { SetValue("UILanguage", value); }
        }

        public bool CleanupMetadataImages
        {
            get { return GetValueBoolean("CleanupMetadataImages", true); }

            set { SetValue("CleanupMetadataImages", value); }
        }

        public string PlexClientIdentifier => GetValue("PlexClientIdentifier", Guid.NewGuid().ToString(), true);

        public string RijndaelPassphrase => GetValue("RijndaelPassphrase", Guid.NewGuid().ToString(), true);

        public string HmacPassphrase => GetValue("HmacPassphrase", Guid.NewGuid().ToString(), true);

        public string RijndaelSalt => GetValue("RijndaelSalt", Guid.NewGuid().ToString(), true);

        public string HmacSalt => GetValue("HmacSalt", Guid.NewGuid().ToString(), true);

        public bool ProxyEnabled => GetValueBoolean("ProxyEnabled", false);

        public ProxyType ProxyType => GetValueEnum<ProxyType>("ProxyType", ProxyType.Http);

        public string ProxyHostname => GetValue("ProxyHostname", string.Empty);

        public int ProxyPort => GetValueInt("ProxyPort", 8080);

        public string ProxyUsername => GetValue("ProxyUsername", string.Empty);

        public string ProxyPassword => GetValue("ProxyPassword", string.Empty);

        public string ProxyBypassFilter => GetValue("ProxyBypassFilter", string.Empty);

        public bool ProxyBypassLocalAddresses => GetValueBoolean("ProxyBypassLocalAddresses", true);

        public string BackupFolder => GetValue("BackupFolder", "Backups");

        public int BackupInterval => GetValueInt("BackupInterval", 7);

        public int BackupRetention => GetValueInt("BackupRetention", 28);

        public CertificateValidationType CertificateValidation =>
            GetValueEnum("CertificateValidation", CertificateValidationType.Enabled);

        public string ApplicationUrl => GetValue("ApplicationUrl", string.Empty);

        public bool TrustCgnatIpAddresses
        {
            get { return GetValueBoolean("TrustCgnatIpAddresses", false); }
            set { SetValue("TrustCgnatIpAddresses", value); }
        }

        private string GetValue(string key)
        {
            return GetValue(key, string.Empty);
        }

        private bool GetValueBoolean(string key, bool defaultValue = false)
        {
            return Convert.ToBoolean(GetValue(key, defaultValue));
        }

        private int GetValueInt(string key, int defaultValue = 0)
        {
            return Convert.ToInt32(GetValue(key, defaultValue));
        }

        private T GetValueEnum<T>(string key, T defaultValue)
        {
            return (T)Enum.Parse(typeof(T), GetValue(key, defaultValue), true);
        }

        public string GetValue(string key, object defaultValue, bool persist = false)
        {
            key = key.ToLowerInvariant();
            Ensure.That(key, () => key).IsNotNullOrWhiteSpace();

            EnsureCache();

            if (_cache.TryGetValue(key, out var dbValue) && dbValue != null && !string.IsNullOrEmpty(dbValue))
            {
                return dbValue;
            }

            _logger.Trace("Using default config value for '{0}' defaultValue:'{1}'", key, defaultValue);

            if (persist)
            {
                SetValue(key, defaultValue.ToString());
            }

            return defaultValue.ToString();
        }

        private void SetValue(string key, bool value)
        {
            SetValue(key, value.ToString());
        }

        private void SetValue(string key, int value)
        {
            SetValue(key, value.ToString());
        }

        private void SetValue(string key, Enum value)
        {
            SetValue(key, value.ToString().ToLower());
        }

        private void SetValue(string key, string value)
        {
            key = key.ToLowerInvariant();

            _logger.Trace("Writing Setting to database. Key:'{0}' Value:'{1}'", key, value);
            _repository.Upsert(key, value);

            ClearCache();
        }

        private void EnsureCache()
        {
            lock (_cache)
            {
                if (!_cache.Any())
                {
                    var all = _repository.All();
                    _cache = all.ToDictionary(c => c.Key.ToLower(), c => c.Value);
                }
            }
        }

        private static void ClearCache()
        {
            lock (_cache)
            {
                _cache = new Dictionary<string, string>();
            }
        }
    }
}
