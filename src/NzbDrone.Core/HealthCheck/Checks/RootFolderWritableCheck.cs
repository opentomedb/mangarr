using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // A root folder that exists but that Mangarr cannot write to fails every import into a NEW series
    // folder there ("permissions error" at EnsureBookFolder). 2026-09-21: /lightnovels was left
    // root:root and every light-novel audiobook import failed until it was chowned.
    [CheckOn(typeof(AuthorAddedEvent))]
    [CheckOn(typeof(TrackImportFailedEvent))]
    public class RootFolderWritableCheck : HealthCheckBase
    {
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskProvider _diskProvider;

        public RootFolderWritableCheck(IRootFolderService rootFolderService, IDiskProvider diskProvider, ILocalizationService localizationService)
            : base(localizationService)
        {
            _rootFolderService = rootFolderService;
            _diskProvider = diskProvider;
        }

        public override HealthCheck Check()
        {
            var readOnly = _rootFolderService.All()
                .Select(r => r.Path)
                .Where(p => _diskProvider.FolderExists(p) && !_diskProvider.FolderWritable(p))
                .ToList();

            if (!readOnly.Any())
            {
                return new HealthCheck(GetType());
            }

            return new HealthCheck(GetType(),
                                   HealthCheckResult.Error,
                                   string.Format(_localizationService.GetLocalizedString("RootFolderNotWritableCheckMessage"), string.Join(" | ", readOnly)),
                                   "#root-folder-not-writable");
        }
    }
}
