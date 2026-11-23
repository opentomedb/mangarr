using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // The size floor (2026-11-14): the five volume comic grades (PDF 1, CBZ 2, CBR 3, ZIP 4, RAR 5)
    // get a 10 MB minimum on an install that never set one (NULL or 0). A minimum the user set,
    // and every other grade (chapters, ebooks, audio -- a chapter or an EPUB is legitimately a
    // few MB), are left alone. No schema change. Idempotent.
    [Migration(065)]
    public class size_floor : NzbDroneMigrationBase
    {
        public const double VolumeFloorMb = 10;

        protected override void MainDbUpgrade()
        {
            Execute.Sql("UPDATE \"QualityDefinitions\" SET \"MinSize\" = 10 WHERE \"Quality\" IN (1, 2, 3, 4, 5) AND (\"MinSize\" IS NULL OR \"MinSize\" = 0)");
        }

        public class Definition65
        {
            public int Quality { get; set; }
            public double? MinSize { get; set; }
            public double? MaxSize { get; set; }
        }
    }
}
