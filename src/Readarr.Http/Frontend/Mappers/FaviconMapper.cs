using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace Readarr.Http.Frontend.Mappers
{
    public class FaviconMapper : StaticResourceMapperBase
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IConfigFileProvider _configFileProvider;

        public FaviconMapper(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, IConfigFileProvider configFileProvider, Logger logger)
            : base(diskProvider, logger)
        {
            _appFolderInfo = appFolderInfo;
            _configFileProvider = configFileProvider;
        }

        public override string Map(string resourceUrl)
        {
            var fileName = "favicon.ico";

            // iOS probes these at the site root regardless of what index.html links;
            // serve the icon that already ships in the UI instead of warn-spamming.
            if (resourceUrl.StartsWith("/apple-touch-icon"))
            {
                fileName = "apple-touch-icon.png";
            }
            else if (BuildInfo.IsDebug)
            {
                fileName = "favicon-debug.ico";
            }

            var path = Path.Combine("Content", "Images", "Icons", fileName);

            return Path.Combine(_appFolderInfo.StartUpFolder, _configFileProvider.UiFolder, path);
        }

        public override bool CanHandle(string resourceUrl)
        {
            return resourceUrl.Equals("/favicon.ico") ||
                   resourceUrl.Equals("/apple-touch-icon.png") ||
                   resourceUrl.Equals("/apple-touch-icon-precomposed.png");
        }
    }
}
