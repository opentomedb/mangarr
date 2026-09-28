using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Localization;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(ModelEvent<RootFolder>))]
    [CheckOn(typeof(ModelEvent<RemotePathMapping>))]
    public class CalibreRootFolderCheck : HealthCheckBase
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IRootFolderService _rootFolderService;
        private readonly ICalibreProxy _calibreProxy;
        private readonly Logger _logger;
        private readonly IOsInfo _osInfo;

        public CalibreRootFolderCheck(IDiskProvider diskProvider,
                                      IRootFolderService rootFolderService,
                                      ICalibreProxy calibreProxy,
                                      IOsInfo osInfo,
                                      Logger logger,
                                      ILocalizationService localizationService)
            : base(localizationService)
        {
            _diskProvider = diskProvider;
            _rootFolderService = rootFolderService;
            _calibreProxy = calibreProxy;
            _logger = logger;
            _osInfo = osInfo;
        }

        // UI translations v1 (2026-09-25): the messages come from en.json; the tokens are the values the
        // old interpolated strings used, and {appName} renders "Mangarr".
        private string Message(string key, RootFolder folder, string libraryFolder, string file = null)
        {
            return _localizationService.GetLocalizedString(key, new Dictionary<string, object>
            {
                { "rootFolderName", folder.Name },
                { "libraryFolder", libraryFolder },
                { "osName", _osInfo.Name },
                { "file", file },
                { "rootFolderPath", folder.Path }
            });
        }

        public override HealthCheck Check()
        {
            var rootFolders = _rootFolderService.All().Where(x => x.IsCalibreLibrary);

            foreach (var folder in rootFolders)
            {
                try
                {
                    var calibreIsLocal = folder.CalibreSettings.Host == "127.0.0.1" || folder.CalibreSettings.Host == "localhost";

                    var files = _calibreProxy.GetAllBookFilePaths(folder.CalibreSettings);
                    if (files.Any())
                    {
                        var file = files.First();

                        // This directory structure is forced by calibre
                        var bookFolder = Path.GetDirectoryName(file);
                        var authorFolder = Path.GetDirectoryName(bookFolder);
                        var libraryFolder = Path.GetDirectoryName(authorFolder);

                        var osPath = new OsPath(libraryFolder);

                        if (!osPath.IsValid)
                        {
                            if (!calibreIsLocal)
                            {
                                return new HealthCheck(GetType(), HealthCheckResult.Error, Message("CalibreRootFolderCheckRemoteBadPath", folder, libraryFolder), "#bad-remote-path-mapping");
                            }
                            else if (_osInfo.IsDocker)
                            {
                                return new HealthCheck(GetType(), HealthCheckResult.Error, Message("CalibreRootFolderCheckDockerBadPath", folder, libraryFolder), "#docker-bad-remote-path-mapping");
                            }
                            else
                            {
                                return new HealthCheck(GetType(), HealthCheckResult.Error, Message("CalibreRootFolderCheckLocalBadPath", folder, libraryFolder), "#bad-download-client-settings");
                            }
                        }

                        if (!_diskProvider.FolderExists(libraryFolder))
                        {
                            if (_osInfo.IsDocker)
                            {
                                return new HealthCheck(GetType(), HealthCheckResult.Error, Message("CalibreRootFolderCheckDockerFolderMissing", folder, libraryFolder), "#docker-bad-remote-path-mapping");
                            }
                            else if (!calibreIsLocal)
                            {
                                return new HealthCheck(GetType(), HealthCheckResult.Error, Message("CalibreRootFolderCheckRemoteFolderMissing", folder, libraryFolder), "#bad-remote-path-mapping");
                            }
                            else
                            {
                                return new HealthCheck(GetType(), HealthCheckResult.Error, Message("CalibreRootFolderCheckFolderPermissions", folder, libraryFolder), "#permissions-error");
                            }
                        }

                        if (!_diskProvider.FileExists(file))
                        {
                            if (_osInfo.IsDocker)
                            {
                                return new HealthCheck(GetType(), HealthCheckResult.Error, Message("CalibreRootFolderCheckDockerFileMissing", folder, libraryFolder, file), "#docker-bad-remote-path-mapping");
                            }
                            else if (!calibreIsLocal)
                            {
                                return new HealthCheck(GetType(), HealthCheckResult.Error, Message("CalibreRootFolderCheckRemoteFileMissing", folder, libraryFolder, file), "#permissions-error");
                            }
                            else
                            {
                                return new HealthCheck(GetType(), HealthCheckResult.Error, Message("CalibreRootFolderCheckFilePermissions", folder, libraryFolder, file), "#permissions-error");
                            }
                        }

                        if (!libraryFolder.PathEquals(folder.Path))
                        {
                            return new HealthCheck(GetType(), HealthCheckResult.Error, Message("CalibreRootFolderCheckPathMismatch", folder, libraryFolder), "#calibre-root-does-not-match");
                        }
                    }
                }
                catch (DownloadClientException ex)
                {
                    _logger.Debug(ex, "Unable to communicate with calibre server for root folder {0}", folder.Name);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unknown error occured in CalibreRootFolderCheck HealthCheck");
                }
            }

            return new HealthCheck(GetType());
        }
    }
}
