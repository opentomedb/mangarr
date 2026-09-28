using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(047)]
    public class add_parent_series : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Collections: the series this one is part of, from the metadata artifact's
            // parent_series_id (arc -> parent line, side story -> main line). Additive, nullable;
            // filled on the next refresh. Never re-keys or drops existing rows.
            Alter.Table("AuthorMetadata").AddColumn("ParentName").AsString().Nullable();
            Alter.Table("AuthorMetadata").AddColumn("ParentForeignAuthorId").AsString().Nullable();
        }
    }
}
