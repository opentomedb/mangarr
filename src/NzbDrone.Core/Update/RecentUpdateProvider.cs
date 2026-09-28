using System.Collections.Generic;
using System.IO;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Update
{
    public interface IRecentUpdateProvider
    {
        List<UpdatePackage> GetRecentUpdatePackages();
    }

    // The Updates page shows the changelog deploy.sh baked into this build (see BuildChangelog)
    // rather than an upstream feed — the fork has no update server; updates ship as Docker
    // images. No baked changelog (ad-hoc build) = an empty page.
    public class RecentUpdateProvider : IRecentUpdateProvider
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;

        public RecentUpdateProvider(IConfigFileProvider configFileProvider,
                                    IAppFolderInfo appFolderInfo,
                                    IDiskProvider diskProvider)
        {
            _configFileProvider = configFileProvider;
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
        }

        public List<UpdatePackage> GetRecentUpdatePackages()
        {
            var changelogPath = Path.Combine(_appFolderInfo.StartUpFolder, "changelog.json");

            if (!_diskProvider.FileExists(changelogPath))
            {
                return new List<UpdatePackage>();
            }

            var package = BuildChangelog.Parse(_diskProvider.ReadAllText(changelogPath),
                                               BuildInfo.Version,
                                               _configFileProvider.Branch);

            return package == null ? new List<UpdatePackage>() : new List<UpdatePackage> { package };
        }
    }
}
