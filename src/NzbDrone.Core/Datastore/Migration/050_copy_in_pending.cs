using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(050)]
    public class copy_in_pending : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Copy-in hold (2026-09-16, D1): a light-novel entry is held from add until the copy-in
            // of the material the maintainer already owns has run and rescanned. Additive, defaulted; the
            // previous image ignores the column (rollback is image-only).
            Alter.Table("Authors").AddColumn("CopyInPending").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
