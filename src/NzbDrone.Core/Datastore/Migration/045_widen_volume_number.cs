using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(045)]
    public class widen_volume_number : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Widen Books.VolumeNumber from INTEGER to REAL so fractional side-story volumes
            // (e.g. 3.5) are representable. Existing whole-number values are preserved.
            Alter.Column("VolumeNumber").OnTable("Books").AsDouble().WithDefaultValue(0);
        }
    }
}
