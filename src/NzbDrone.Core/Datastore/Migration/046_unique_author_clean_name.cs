using System.Data;
using System.Linq;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Mangarr: the duplicate-series guard hangs off AuthorRepository.FindByName, which uses
    // ExclusiveOrDefault — the moment two rows share a CleanName it returns null and BOTH the
    // add-time guard and the import-list pre-filters go blind (2026-08-06: a check-then-insert
    // race double-added 'Marriagetoxin'). The service-layer probes narrow the window but cannot
    // close it; this unique index is the backstop that makes a second row impossible.
    //
    // Dedupe policy (must run BEFORE the index is created): keep the lowest Id in each group
    // untouched — it is the original add, the row files/history/UI hang off — and rename the
    // later rows by suffixing " " + lower(ForeignAuthorId). The suffix is deterministic and
    // provably unique (Authors.AuthorMetadataId and AuthorMetadata.ForeignAuthorId are both
    // UNIQUE), and since every organically generated CleanName is space-free (both Clean() in
    // BookInfoProxy and Parser.CleanAuthorName strip spaces), a suffixed value can never collide
    // with a real one. No rows are deleted — renamed dupes stay visible for manual cleanup.
    [Migration(046)]
    public class unique_author_clean_name : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(DedupeCleanNames);

            // 001 created plain IX_Authors_CleanName; recreate under the same name as UNIQUE.
            Delete.Index().OnTable("Authors").OnColumn("CleanName");
            Create.Index().OnTable("Authors").OnColumn("CleanName").Ascending().WithOptions().Unique();
        }

        private void DedupeCleanNames(IDbConnection conn, IDbTransaction tran)
        {
            var dupes = conn.Query<DupeRow>(
                "SELECT \"Authors\".\"Id\" AS \"Id\", \"Authors\".\"CleanName\" AS \"CleanName\", \"AuthorMetadata\".\"ForeignAuthorId\" AS \"ForeignAuthorId\" " +
                "FROM \"Authors\" " +
                "JOIN \"AuthorMetadata\" ON \"AuthorMetadata\".\"Id\" = \"Authors\".\"AuthorMetadataId\" " +
                "WHERE \"Authors\".\"Id\" NOT IN (SELECT MIN(\"Id\") FROM \"Authors\" GROUP BY \"CleanName\")",
                transaction: tran).ToList();

            foreach (var dupe in dupes)
            {
                conn.Execute(
                    "UPDATE \"Authors\" SET \"CleanName\" = @CleanName WHERE \"Id\" = @Id",
                    new { CleanName = $"{dupe.CleanName} {dupe.ForeignAuthorId.ToLowerInvariant()}", Id = dupe.Id },
                    transaction: tran);
            }
        }

        private class DupeRow
        {
            public int Id { get; set; }
            public string CleanName { get; set; }
            public string ForeignAuthorId { get; set; }
        }
    }
}
