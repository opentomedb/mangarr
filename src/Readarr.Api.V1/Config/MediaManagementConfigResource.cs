using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Qualities;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Config
{
    public class MediaManagementConfigResource : RestResource
    {
        // Preferred light-novel delivery format (2026-09): epub|azw3|kepub, the calibre conversion
        // to add on import (Settings -> Media Management -> Light novel storage).
        public string PreferredLightNovelFormat { get; set; }

        // Light-novel storage (2026-09-22): where light-novel files go and how calibre / Audiobookshelf
        // are reached. The calibre password and the ABS API key go out as the fixed mask
        // (GoogleBooksService.MaskApiKey); the mask echoing back keeps the stored value.
        public string LightNovelEbookHome { get; set; }
        public string LightNovelAudioHome { get; set; }
        public string CalibreContentServerUrl { get; set; }
        public string CalibreLibrary { get; set; }
        public string CalibreUsername { get; set; }
        public string CalibrePassword { get; set; }
        public string CalibreRemotePath { get; set; }
        public string CalibreLocalPath { get; set; }
        public string AudiobookshelfUrl { get; set; }
        public string AudiobookshelfApiKey { get; set; }
        public string AudiobookshelfLibraryId { get; set; }
        public string AudiobookshelfRemotePath { get; set; }
        public string AudiobookshelfLocalPath { get; set; }

        public bool AutoUnmonitorPreviouslyDownloadedBooks { get; set; }

        // Beta readiness fix round (2026-09-28, F3): the daily library-wide PDF->CBZ sweep.
        public bool PdfToCbzSweep { get; set; }
        public string RecycleBin { get; set; }
        public int RecycleBinCleanupDays { get; set; }
        public ProperDownloadTypes DownloadPropersAndRepacks { get; set; }
        public bool CreateEmptyAuthorFolders { get; set; }
        public bool DeleteEmptyFolders { get; set; }
        public FileDateType FileDate { get; set; }
        public bool WatchLibraryForChanges { get; set; }
        public RescanAfterRefreshType RescanAfterRefresh { get; set; }
        public AllowFingerprinting AllowFingerprinting { get; set; }

        public bool SetPermissionsLinux { get; set; }
        public string ChmodFolder { get; set; }
        public string ChownGroup { get; set; }

        public bool SkipFreeSpaceCheckWhenImporting { get; set; }
        public int MinimumFreeSpaceWhenImporting { get; set; }
        public bool CopyUsingHardlinks { get; set; }
        public bool ImportExtraFiles { get; set; }
        public string ExtraFileExtensions { get; set; }
    }

    public static class MediaManagementConfigResourceMapper
    {
        public static MediaManagementConfigResource ToResource(IConfigService model)
        {
            return new MediaManagementConfigResource
            {
                PreferredLightNovelFormat = model.PreferredLightNovelFormat,

                LightNovelEbookHome = model.LightNovelEbookHome,
                LightNovelAudioHome = model.LightNovelAudioHome,
                CalibreContentServerUrl = model.CalibreContentServerUrl,
                CalibreLibrary = model.CalibreLibrary,
                CalibreUsername = model.CalibreUsername,
                CalibrePassword = GoogleBooksService.MaskApiKey(model.CalibrePassword),
                CalibreRemotePath = model.CalibreRemotePath,
                CalibreLocalPath = model.CalibreLocalPath,
                AudiobookshelfUrl = model.AudiobookshelfUrl,
                AudiobookshelfApiKey = GoogleBooksService.MaskApiKey(model.AudiobookshelfApiKey),
                AudiobookshelfLibraryId = model.AudiobookshelfLibraryId,
                AudiobookshelfRemotePath = model.AudiobookshelfRemotePath,
                AudiobookshelfLocalPath = model.AudiobookshelfLocalPath,

                AutoUnmonitorPreviouslyDownloadedBooks = model.AutoUnmonitorPreviouslyDownloadedBooks,
                PdfToCbzSweep = model.PdfToCbzSweep,
                RecycleBin = model.RecycleBin,
                RecycleBinCleanupDays = model.RecycleBinCleanupDays,
                DownloadPropersAndRepacks = model.DownloadPropersAndRepacks,
                CreateEmptyAuthorFolders = model.CreateEmptyAuthorFolders,
                DeleteEmptyFolders = model.DeleteEmptyFolders,
                FileDate = model.FileDate,
                WatchLibraryForChanges = model.WatchLibraryForChanges,
                RescanAfterRefresh = model.RescanAfterRefresh,
                AllowFingerprinting = model.AllowFingerprinting,

                SetPermissionsLinux = model.SetPermissionsLinux,
                ChmodFolder = model.ChmodFolder,
                ChownGroup = model.ChownGroup,

                SkipFreeSpaceCheckWhenImporting = model.SkipFreeSpaceCheckWhenImporting,
                MinimumFreeSpaceWhenImporting = model.MinimumFreeSpaceWhenImporting,
                CopyUsingHardlinks = model.CopyUsingHardlinks,
                ImportExtraFiles = model.ImportExtraFiles,
                ExtraFileExtensions = model.ExtraFileExtensions,
            };
        }
    }
}
