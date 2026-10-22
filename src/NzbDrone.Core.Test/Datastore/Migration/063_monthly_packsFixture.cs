using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class monthly_packsFixture : MigrationTest<monthly_packs>
    {
        [Test]
        public void should_create_pack_file_lists_table_with_unique_info_hash()
        {
            var db = WithMigrationTestDb();

            var columns = db.Query("PRAGMA table_info(\"PackFileLists\")").Select(r => (string)r["name"]);
            columns.Should().Contain(new[] { "InfoHash", "Title", "Name", "Files", "Torrent", "Fetched" });

            var unique = db.Query("PRAGMA index_list(\"PackFileLists\")").Where(r => Convert.ToInt64(r["unique"]) == 1);
            unique.Should().NotBeEmpty();
            db.Query("PRAGMA index_info(\"" + unique.First()["name"] + "\")").Select(r => (string)r["name"]).Should().Equal("InfoHash");
        }
    }
}
