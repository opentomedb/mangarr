using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Volumes from monthly multi-series packs (2026-10-21): PackFileLists caches the .torrent of a
    // pack (by info hash) with its parsed file list, so a pack is fetched and read once.
    [Migration(063)]
    public class monthly_packs : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("PackFileLists")
                .WithColumn("InfoHash").AsString().NotNullable().Unique()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("Name").AsString().Nullable()
                .WithColumn("Files").AsString().NotNullable()
                .WithColumn("Torrent").AsBinary().Nullable()
                .WithColumn("Fetched").AsDateTime().NotNullable();
        }
    }
}
