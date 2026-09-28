using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(053)]
    public class file_homes : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // One copy each (2026-09-20): where a light-novel file lives (0 entry folder, 1 calibre,
            // 2 Audiobookshelf's tree) and whether Mangarr adopted it in place (immutable to it).
            // Additive, defaulted; a manga row is 0/false forever.
            Alter.Table("BookFiles").AddColumn("Home").AsInt32().NotNullable().WithDefaultValue(0);
            Alter.Table("BookFiles").AddColumn("Adopted").AsBoolean().NotNullable().WithDefaultValue(false);
            Alter.Table("AuthorMetadata").AddColumn("Writer").AsString().Nullable();
            Alter.Table("Authors").AddColumn("AdoptedTagWrite").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
