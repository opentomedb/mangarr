using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Update
{
    public interface IUpdatePackageProvider
    {
        UpdatePackage GetLatestUpdate(string branch, Version currentVersion);
        List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion = null);
    }

    // Mangarr does not use the upstream Readarr update feed: the fork ships as a Docker image
    // (rebuild + recreate is the update path), and offering readarr.servarr.com packages here
    // would present ANOTHER PRODUCT's tarballs on the Updates page. No update is ever available
    // through the in-app updater; System -> Updates simply shows none.
    public class UpdatePackageProvider : IUpdatePackageProvider
    {
        public UpdatePackage GetLatestUpdate(string branch, Version currentVersion)
        {
            return null;
        }

        public List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion = null)
        {
            return new List<UpdatePackage>();
        }
    }
}
