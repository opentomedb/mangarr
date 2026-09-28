using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(052)]
    public class covered_source : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Covered volumes (2026-09-17, D4a): who wrote CoveredByVolume — "audible" (re-derived
            // every refresh) or "import" (a spanning file; only its deletion clears it). Additive.
            Alter.Table("Editions").AddColumn("CoveredSource").AsString().Nullable();
        }
    }
}
