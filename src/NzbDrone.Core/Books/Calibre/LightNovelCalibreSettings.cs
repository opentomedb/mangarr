using System;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books.Calibre
{
    // Light-novel storage (2026-09-22): the light-novel content server's settings carry their own path
    // pair ("Path as calibre sees it" -> "Path as Mangarr sees it"), which CalibreProxy applies to the
    // paths calibre reports. A subclass, not new CalibreSettings properties: CalibreSettings is stored
    // in RootFolders JSON and stays Readarr's.
    public class LightNovelCalibreServerSettings : CalibreSettings
    {
        public string RemotePath { get; set; }
        public string LocalPath { get; set; }
    }

    // One copy each (2026-09-20): a light-novel EPUB lives in calibre's own library, reached through
    // the content server named in Settings -> Media Management -> Light novel storage, not through a
    // calibre root folder. ForConfig() is those settings; For(file) picks them for a Calibre-homed
    // file and falls back to the file's root folder otherwise (null when that root is not a calibre root).
    public interface ILightNovelCalibreSettings
    {
        CalibreSettings ForConfig();
        CalibreSettings For(BookFile file);
    }

    public class LightNovelCalibreSettings : ILightNovelCalibreSettings
    {
        private static readonly TimeSpan DefaultLibraryLifetime = TimeSpan.FromHours(1);

        private readonly ILightNovelStorage _storage;
        private readonly IConfigService _configService;
        private readonly IRootFolderService _rootFolderService;
        private readonly ICalibreProxy _calibreProxy;
        private readonly ICached<string> _defaultLibrary;

        public LightNovelCalibreSettings(ILightNovelStorage storage,
                                         IConfigService configService,
                                         IRootFolderService rootFolderService,
                                         ICalibreProxy calibreProxy,
                                         ICacheManager cacheManager)
        {
            _storage = storage;
            _configService = configService;
            _rootFolderService = rootFolderService;
            _calibreProxy = calibreProxy;
            _defaultLibrary = cacheManager.GetCache<string>(GetType(), "defaultLibrary");
        }

        // http://calibre.test:8081 -> host, port, no url base; https://calibre.lan/cal/ -> ssl, 443,
        // url base "cal". HttpRequestBuilder.BuildBaseUrl puts the slash back, so this round-trips.
        // Pure: the storage probe builds settings from the form's unsaved values the same way. A blank
        // or non-http(s) address is a UriFormatException (new Uri("calibre:8081") would parse as a scheme).
        public static LightNovelCalibreServerSettings Build(CalibreConnection connection, string preferredFormat)
        {
            var url = new Uri(connection.Url?.Trim() ?? string.Empty);

            if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
            {
                throw new UriFormatException($"'{connection.Url}' is not an http(s) address");
            }

            var hasLogin = connection.Username.IsNotNullOrWhiteSpace();

            return new LightNovelCalibreServerSettings
            {
                Host = url.Host,
                Port = url.Port,
                UseSsl = url.Scheme == Uri.UriSchemeHttps,
                UrlBase = url.AbsolutePath.Trim('/'),
                Username = hasLogin ? connection.Username.Trim() : null,
                Password = hasLogin ? connection.Password : null,
                Library = connection.Library?.Trim() ?? string.Empty,
                RemotePath = connection.RemotePath,
                LocalPath = connection.LocalPath,

                // EPUB (default) = no conversion: Mangarr always grabs and keeps the EPUB; with
                // AZW3 or KEPUB chosen, calibre ADDS that format to the same book on import rather
                // than replacing it. KEPUB is not in the CalibreFormat enum CalibreSettingsValidator
                // checks -- these settings are never run through that validator, and calibre's own
                // content server lists KEPUB as a real output format, so it is fine.
                OutputFormat = PreferredFormats.LightNovelOutputFormat(preferredFormat),
                OutputProfile = (int)CalibreProfile.@default
            };
        }

        // A missing or bad address is a CalibreException naming the setting (ImportApprovedBooks turns
        // it into a clear import failure, not a generic UriFormatException). Conversion is calibre's:
        // with ebooks going to the entry folder no output format is ever asked for.
        public CalibreSettings ForConfig()
        {
            var connection = _storage.Calibre;

            if (connection.Url.IsNullOrWhiteSpace())
            {
                throw new CalibreException("The calibre content server URL is not set (Settings → Media Management → Light Novel Storage → Ebooks)");
            }

            LightNovelCalibreServerSettings settings;

            try
            {
                settings = Build(connection, _storage.EbookHome == LightNovelHome.Calibre ? _configService.PreferredLightNovelFormat : null);
            }
            catch (UriFormatException ex)
            {
                throw new CalibreException($"{ex.Message} (Calibre Content Server URL)");
            }

            if (settings.Library.IsNullOrWhiteSpace())
            {
                // Blank = the server's default library, asked once an hour per address.
                try
                {
                    settings.Library = _defaultLibrary.Get(connection.Url, () => _calibreProxy.GetLibraryInfo(settings).DefaultLibrary, DefaultLibraryLifetime);
                }
                catch (Exception ex)
                {
                    throw new CalibreException("Calibre did not name its default library: {0}", ex, ex.Message);
                }
            }

            return settings;
        }

        public CalibreSettings For(BookFile file)
        {
            return file.Home == FileHome.Calibre
                ? ForConfig()
                : _rootFolderService.GetBestRootFolder(file.Path)?.CalibreSettings;
        }
    }
}
