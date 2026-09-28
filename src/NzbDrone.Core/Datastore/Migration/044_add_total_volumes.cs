using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(044)]
    public class add_total_volumes : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Full series (Japanese tankoubon) volume total, shown alongside the English-available
            // count as "X of Y". Additive + default 0; never re-keys or drops existing rows.
            Alter.Table("AuthorMetadata").AddColumn("TotalVolumes").AsInt32().WithDefaultValue(0);
        }
    }
}
