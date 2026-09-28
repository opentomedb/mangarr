using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.MediaFiles
{
    // Light-novel storage (2026-09-22): where each kind of light-novel file goes, and how Mangarr
    // reaches calibre and Audiobookshelf -- every value from Settings -> Media Management -> Light
    // novel storage, nothing install-specific in code (the old built-in values are migration 055's record).
    // Replaces the LightNovelHomes path constants.
    public static class LightNovelHome
    {
        public const string Entry = "entry";
        public const string Calibre = "calibre";
        public const string Audiobookshelf = "audiobookshelf";
    }

    public class CalibreConnection
    {
        public string Url { get; set; }
        public string Library { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string RemotePath { get; set; }
        public string LocalPath { get; set; }
    }

    public class AudiobookshelfConnection
    {
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public string LibraryId { get; set; }
        public string RemotePath { get; set; }
        public string LocalPath { get; set; }
    }

    public interface ILightNovelStorage
    {
        string EbookHome { get; }
        string AudioHome { get; }
        CalibreConnection Calibre { get; }
        AudiobookshelfConnection Audiobookshelf { get; }
        string MapFromCalibre(string calibrePath);
        string MapToCalibre(string localPath);
        string MapFromAudiobookshelf(string absPath);
        string MapToAudiobookshelf(string localPath);

        // The folder new light-novel audio goes into, as Mangarr sees it; null when unknown.
        string AudiobookshelfRoot();
    }

    public class LightNovelStorage : ILightNovelStorage
    {
        private static readonly TimeSpan FolderLifetime = TimeSpan.FromHours(1);

        private readonly IConfigService _configService;
        private readonly IAudiobookshelfClient _audiobookshelf;
        private readonly ICached<string> _folderCache;
        private readonly Logger _logger;

        public LightNovelStorage(IConfigService configService, IAudiobookshelfClient audiobookshelf, ICacheManager cacheManager, Logger logger)
        {
            _configService = configService;
            _audiobookshelf = audiobookshelf;
            _folderCache = cacheManager.GetCache<string>(GetType(), "audiobookshelfFolder");
            _logger = logger;
        }

        public string EbookHome => HomeOf(_configService.LightNovelEbookHome, LightNovelHome.Calibre);

        public string AudioHome => HomeOf(_configService.LightNovelAudioHome, LightNovelHome.Audiobookshelf);

        public CalibreConnection Calibre => new CalibreConnection
        {
            Url = _configService.CalibreContentServerUrl,
            Library = _configService.CalibreLibrary,
            Username = _configService.CalibreUsername,
            Password = _configService.CalibrePassword,
            RemotePath = _configService.CalibreRemotePath,
            LocalPath = _configService.CalibreLocalPath
        };

        public AudiobookshelfConnection Audiobookshelf => new AudiobookshelfConnection
        {
            Url = _configService.AudiobookshelfUrl,
            ApiKey = _configService.AudiobookshelfApiKey,
            LibraryId = _configService.AudiobookshelfLibraryId,
            RemotePath = _configService.AudiobookshelfRemotePath,
            LocalPath = _configService.AudiobookshelfLocalPath
        };

        public string MapFromCalibre(string calibrePath)
        {
            return LightNovelPathMap.Map(calibrePath, _configService.CalibreRemotePath, _configService.CalibreLocalPath);
        }

        public string MapToCalibre(string localPath)
        {
            return LightNovelPathMap.Map(localPath, _configService.CalibreLocalPath, _configService.CalibreRemotePath);
        }

        public string MapFromAudiobookshelf(string absPath)
        {
            return LightNovelPathMap.Map(absPath, _configService.AudiobookshelfRemotePath, _configService.AudiobookshelfLocalPath);
        }

        public string MapToAudiobookshelf(string localPath)
        {
            return LightNovelPathMap.Map(localPath, _configService.AudiobookshelfLocalPath, _configService.AudiobookshelfRemotePath);
        }

        // Mapped: the Mangarr side of the pair -- no network call at import time. Blank: the
        // library's first folder as Audiobookshelf reports it (the same path in both containers),
        // asked once an hour. No answer = null: the reconciler then judges nothing and an import is
        // refused (the download stays put), never a guess.
        public string AudiobookshelfRoot()
        {
            var connection = Audiobookshelf;

            if (LightNovelPathMap.IsSet(connection.RemotePath, connection.LocalPath))
            {
                return AudiobookshelfRootOf(connection, null);
            }

            if (connection.Url.IsNullOrWhiteSpace() || connection.LibraryId.IsNullOrWhiteSpace())
            {
                return null;
            }

            try
            {
                return _folderCache.Get($"{connection.Url}|{connection.LibraryId}", () => FirstFolder(connection), FolderLifetime);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Audiobookshelf did not name the folder of library {0}", connection.LibraryId);
                return null;
            }
        }

        // The Settings form's Test and library buttons send what the form holds: an untouched password
        // or API key is still the mask, which means the stored value (the same rule as the save).
        public static string Unmask(string incoming, string stored)
        {
            return GoogleBooksService.SanitizeIncomingApiKey(incoming) ?? stored;
        }

        // Fix round 1 (2026-09-22): the Test/library buttons ask about whatever host is in the box
        // right now, not necessarily the saved one. A stored secret is only substituted -- for the
        // mask or for an omitted field alike -- when that host, trimmed and trailing-slash-
        // insensitive, still matches the one the secret belongs to; otherwise it goes out empty
        // rather than to a newly typed host. A typed secret always passes through untouched.
        public static string UnmaskForHost(string incoming, string stored, string incomingUrl, string storedUrl)
        {
            if (SameHost(incomingUrl, storedUrl))
            {
                return Unmask(incoming, stored);
            }

            return incoming == GoogleBooksService.ApiKeyMask || incoming == null ? string.Empty : incoming;
        }

        private static bool SameHost(string a, string b)
        {
            return string.Equals(NormalizedHost(a), NormalizedHost(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizedHost(string url)
        {
            return url?.Trim().TrimEnd('/');
        }

        // Also the storage probe's answer for values not saved yet.
        public static string AudiobookshelfRootOf(AudiobookshelfConnection connection, AudiobookshelfLibrary library)
        {
            var root = LightNovelPathMap.IsSet(connection.RemotePath, connection.LocalPath)
                ? connection.LocalPath
                : library?.Folders?.FirstOrDefault();

            return LightNovelPathMap.WithoutSlash(root);
        }

        private string FirstFolder(AudiobookshelfConnection connection)
        {
            var library = _audiobookshelf.GetLibraries(connection.Url, connection.ApiKey).FirstOrDefault(l => l.Id == connection.LibraryId);

            return AudiobookshelfRootOf(connection, library)
                ?? throw new InvalidOperationException($"Audiobookshelf has no library {connection.LibraryId} with a folder");
        }

        private static string HomeOf(string value, string other)
        {
            return string.Equals(value?.Trim(), other, StringComparison.OrdinalIgnoreCase) ? other : LightNovelHome.Entry;
        }
    }
}
