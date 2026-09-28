using System.Data;
using System.Linq;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Light-novel storage (2026-09-22): the calibre and Audiobookshelf addresses, the calibre library
    // and the container paths used to be code defaults -- one install's values (the dev server's). They are
    // settings now, blank by default, and this migration is the record of what the old defaults
    // were. It carries them over ONLY on an install that used them: one with a light-novel file
    // homed in calibre (BookFiles.Home 1) or in Audiobookshelf's tree (Home 2). This migration never
    // overwrites a stored non-empty value (the stored ABS API key stays); a stored row whose Value is
    // null or "" is filled in, matching ConfigService.GetValue's own emptiness rule (~line 608:
    // !string.IsNullOrEmpty), since an install with that row saved as "" was still effectively
    // running on the old default. The OpenTome link is not carried over: its old default answered
    // nothing (spec §6.4). Only Config rows are added -- an older image ignores them, so an image
    // rollback stays safe.
    [Migration(055)]
    public class light_novel_storage : NzbDroneMigrationBase
    {
        // calibre: same server, library "books", no login (calibre's trusted_ips lists Mangarr), and
        // "/books/" on both sides -- no translation, but it keeps the mount existence check the old
        // constant made (the host path is mounted at /books in both containers).
        public const string CalibreUrl = "";
        public const string CalibreLibrary = "books";
        public const string CalibrePath = "/books/";

        // Audiobookshelf: its library folder is /audiobooks in its container, /abs-audiobooks in Mangarr's.
        public const string AudiobookshelfUrl = "";
        public const string AudiobookshelfLibraryId = "";
        public const string AudiobookshelfRemotePath = "/audiobooks/";
        public const string AudiobookshelfLocalPath = "/abs-audiobooks/";

        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(CarryOver);
        }

        private void CarryOver(IDbConnection conn, IDbTransaction tran)
        {
            if (HasFileHomedIn(conn, tran, 1))
            {
                SetIfUnset(conn, tran, "lightnovelebookhome", "calibre");
                SetIfUnset(conn, tran, "calibrecontentserverurl", CalibreUrl);
                SetIfUnset(conn, tran, "calibrelibrary", CalibreLibrary);
                SetIfUnset(conn, tran, "calibreremotepath", CalibrePath);
                SetIfUnset(conn, tran, "calibrelocalpath", CalibrePath);
            }

            if (HasFileHomedIn(conn, tran, 2))
            {
                SetIfUnset(conn, tran, "lightnovelaudiohome", "audiobookshelf");
                SetIfUnset(conn, tran, "audiobookshelfurl", AudiobookshelfUrl);
                SetIfUnset(conn, tran, "audiobookshelflibraryid", AudiobookshelfLibraryId);
                SetIfUnset(conn, tran, "audiobookshelfremotepath", AudiobookshelfRemotePath);
                SetIfUnset(conn, tran, "audiobookshelflocalpath", AudiobookshelfLocalPath);
            }
        }

        private static bool HasFileHomedIn(IDbConnection conn, IDbTransaction tran, int home)
        {
            return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM \"BookFiles\" WHERE \"Home\" = @home", new { home }, tran) > 0;
        }

        // No row -> INSERT. A row whose Value is null or "" -> UPDATE (ConfigService.GetValue treats
        // that row as unset too, so this is filling in the value the install was effectively already
        // using). A row with a non-empty value -> left alone.
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
