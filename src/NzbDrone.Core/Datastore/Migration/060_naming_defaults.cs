using System.Data;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Beta polish (2026-09-28): NamingConfig.Default changes for NEW installs (Rename Volumes on, flat
    // "{Author Name} - Vol. {Volume:00}"). NamingConfigService inserts the Default the first time the
    // naming config is read with no row. An install that holds a library (any Authors or BookFiles row,
    // the data test of 055/059) always has that row already -- adding a series reads it -- but if one
    // ever lacks it, this writes Readarr's old default so its files keep today's names. A fresh DB has
    // no library, so nothing is written and the new default applies. An existing row is never touched.
    // The values are spelled out, not read from NamingConfig.Default, so they stay what they were.
    [Migration(060)]
    public class naming_defaults : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(KeepExistingNaming);
        }

        private void KeepExistingNaming(IDbConnection conn, IDbTransaction tran)
        {
            if (conn.ExecuteScalar<int>("SELECT COUNT(*) FROM \"NamingConfig\"", null, tran) > 0)
            {
                return;
            }

            if (conn.ExecuteScalar<int>("SELECT COUNT(*) FROM \"Authors\"", null, tran) == 0 &&
                conn.ExecuteScalar<int>("SELECT COUNT(*) FROM \"BookFiles\"", null, tran) == 0)
            {
                return;
            }

            conn.Execute("INSERT INTO \"NamingConfig\" (\"ReplaceIllegalCharacters\", \"AuthorFolderFormat\", \"RenameBooks\", \"StandardBookFormat\", \"ColonReplacementFormat\") " +
                         "VALUES (@ReplaceIllegalCharacters, @AuthorFolderFormat, @RenameBooks, @StandardBookFormat, @ColonReplacementFormat)",
                         new
                         {
                             ReplaceIllegalCharacters = true,
                             AuthorFolderFormat = "{Author Name}",
                             RenameBooks = false,
                             StandardBookFormat = "{Book Title}/{Author Name} - {Book Title}{ (PartNumber)}",
                             ColonReplacementFormat = 4
                         },
                         tran);
        }
    }
}
