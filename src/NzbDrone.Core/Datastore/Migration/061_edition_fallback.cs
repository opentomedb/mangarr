using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(061)]
    public class edition_fallback : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // KR/CN consumer (2026-09-29): the series is bound to a line outside the user's Preferred Edition
            // languages because the work has none in them (a new add's fallback). Keeps its English name on
            // refresh. Additive with a default, so an image rollback stays safe.
            Alter.Table("AuthorMetadata").AddColumn("EditionFallback").AsBoolean().WithDefaultValue(false);
        }
    }
}
