using System.Data;
using System.Linq;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Beta readiness (2026-09-28): two defaults change for NEW installs -- ComicInfo.xml is embedded only
    // into new downloads (F2, WriteComicInfo) and the daily library-wide PDF->CBZ sweep is off (F3,
    // PdfToCbzSweep). An install that already holds a library keeps today's behaviour through explicit
    // Config rows: WriteComicInfo = allimports, PdfToCbzSweep = True. "Holds a library" = any Authors or
    // BookFiles row, the same data test as migration 055. A fresh DB has neither, so this writes nothing
    // there and the new defaults apply. Same SetIfUnset rule as 055: no row -> insert; a null/"" row ->
    // update (ConfigService.GetValue reads that as unset too); a stored value -> left alone. Only Config
    // rows: an older image ignores them, so an image rollback stays safe.
    [Migration(059)]
    public class beta_defaults : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(KeepExistingBehaviour);
        }

        private void KeepExistingBehaviour(IDbConnection conn, IDbTransaction tran)
        {
            if (!HasLibrary(conn, tran))
            {
                return;
            }

            SetIfUnset(conn, tran, "writecomicinfo", "allimports");
            SetIfUnset(conn, tran, "pdftocbzsweep", "True");
        }

        private static bool HasLibrary(IDbConnection conn, IDbTransaction tran)
        {
            return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM \"Authors\"", null, tran) > 0 ||
                   conn.ExecuteScalar<int>("SELECT COUNT(*) FROM \"BookFiles\"", null, tran) > 0;
        }

        private static void SetIfUnset(IDbConnection conn, IDbTransaction tran, string key, string value)
        {
            var rows = conn.Query<string>("SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = @key", new { key }, tran).ToList();

            if (!rows.Any())
            {
                conn.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES (@key, @value)", new { key, value }, tran);
                return;
            }

            if (string.IsNullOrEmpty(rows[0]))
            {
                conn.Execute("UPDATE \"Config\" SET \"Value\" = @value WHERE \"Key\" = @key", new { key, value }, tran);
            }
        }
    }
}
