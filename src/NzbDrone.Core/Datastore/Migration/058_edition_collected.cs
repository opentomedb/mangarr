using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(058)]
    public class edition_collected : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Preferred Edition follow-up (2026-09-26): whether the bound OpenTome line is a collected edition
            // (omnibus, Perfect Edition, ...), so a series bound to an omnibus-only line takes its own
            // marker-named releases even when none of its names carry the marker. Set on every refresh;
            // additive with a default, so an image rollback stays safe.
            Alter.Table("AuthorMetadata").AddColumn("EditionCollected").AsBoolean().WithDefaultValue(false);
        }
    }
}
