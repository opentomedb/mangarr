using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(049)]
    public class anilist_id : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Manga metadata binding (D4): the AniList entry a series is bound to. Additive,
            // nullable; null = "resolve by title" (today's behaviour). Filled by the next strict
            // match, the rebind pass or Fix Match. Never re-keys or drops existing rows.
            Alter.Table("AuthorMetadata").AddColumn("AniListId").AsInt32().Nullable();
        }
    }
}
