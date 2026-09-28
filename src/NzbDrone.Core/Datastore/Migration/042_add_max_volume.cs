using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(042)]
    public class add_max_volume : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Manual "Max volume" cap per series (0 = auto, derive from indexer search). Phase 3.
            Alter.Table("Authors").AddColumn("MaxVolume").AsInt32().WithDefaultValue(0);
        }
    }
}
