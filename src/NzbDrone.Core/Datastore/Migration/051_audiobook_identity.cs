using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(051)]
    public class audiobook_identity : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Audiobook identity (2026-09-17, D1): additive, nullable; null = no identity (manga always).
            Alter.Table("Books").AddColumn("Subtitle").AsString().Nullable();
            Alter.Table("Editions").AddColumn("AudiobookTitle").AsString().Nullable();
            Alter.Table("Editions").AddColumn("AudiobookSubtitle").AsString().Nullable();
            Alter.Table("Editions").AddColumn("RuntimeMinutes").AsInt32().Nullable();
            Alter.Table("Editions").AddColumn("AudioReleaseDate").AsDateTime().Nullable();
            Alter.Table("Editions").AddColumn("CoveredByVolume").AsDouble().Nullable();
        }
    }
}
