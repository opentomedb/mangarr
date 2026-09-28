using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Mangarr: fresh installs get manga-friendly metadata-profile defaults from the seed
    // (MinPopularity=0, SkipMissingDate=false, SkipPartsAndSets=false), but databases that
    // were created before that change keep the stock book/audiobook defaults, which silently
    // FILTER OUT legitimate manga volumes (a manga volume has low popularity, may lack a
    // per-volume date, and literally IS a "part of a set"). This one-time migration rewrites
    // ONLY profiles that still hold the exact stock signature, so user-customized profiles and
    // the "None" sentinel profile (MinPopularity=1e10) are left untouched, and it is a no-op on
    // installs already patched to the manga values (idempotent).
    [Migration(043)]
    public class manga_metadata_profile_defaults : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.Sql(
                "UPDATE \"MetadataProfiles\" " +
                "SET \"MinPopularity\" = 0, \"SkipMissingDate\" = 0, \"SkipPartsAndSets\" = 0 " +
                "WHERE \"MinPopularity\" = 350 AND \"SkipMissingDate\" = 1 AND \"SkipPartsAndSets\" = 1");
        }
    }
}
