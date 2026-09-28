using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(057)]
    public class preferred_edition : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Preferred Edition (2026-09-24, D1/D6): the market edition a series is bound to (null =
            // English -- no data rewrite), the OpenTome release line it is bound to (rl_..., a public
            // contract), and the English anchor name a localized series keeps resolving by (plan A1;
            // null whenever Name is the anchor). Plus a volume's date precision (null = day). Additive,
            // nullable, nothing re-keyed: an image rollback stays safe.
            Alter.Table("AuthorMetadata").AddColumn("EditionLanguage").AsString().Nullable();
            Alter.Table("AuthorMetadata").AddColumn("TomeLineId").AsString().Nullable();
            Alter.Table("AuthorMetadata").AddColumn("AnchorName").AsString().Nullable();
            Alter.Table("Books").AddColumn("ReleaseDatePrecision").AsString().Nullable();
        }
    }
}
