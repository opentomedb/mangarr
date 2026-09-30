using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Beta polish (2026-09-28): a library without a NamingConfig row gets Readarr's old default, so the
    // new fresh-install default (rename on, flat) never reaches it; a fresh DB and a stored row are left alone.
    [TestFixture]
    public class naming_defaultsFixture : MigrationTest<naming_defaults>
    {
        private const string Quality = "{\"quality\":{\"id\":1,\"name\":\"CBZ\"},\"revision\":{\"version\":1,\"real\":0,\"isRepack\":false}}";

        private static void GivenAuthor(naming_defaults c)
        {
            c.Insert.IntoTable("Authors").Row(new
            {
                CleanName = "chainsawman",
                Path = "/manga/Chainsaw Man",
                Monitored = true,
                MonitorNewItems = 0,
                QualityProfileId = 1,
                MetadataProfileId = 1,
                AuthorMetadataId = 1,
                CopyInPending = false
            });
        }

        private static void GivenBookFile(naming_defaults c)
        {
            c.Insert.IntoTable("BookFiles").Row(new
            {
                Path = "/manga/Chainsaw Man/Chainsaw Man v01.cbz",
                Size = 1,
                Modified = DateTime.UtcNow,
                DateAdded = DateTime.UtcNow,
                Quality,
                EditionId = 1,
                CalibreId = 0,
                Part = 1,
                Home = 0,
                Adopted = false
            });
        }

        private static NamingRow60[] RowsOf(IDirectDataMapper db)
        {
            return db.Query<NamingRow60>("SELECT * FROM \"NamingConfig\"").ToArray();
        }

        [Test]
        public void a_fresh_install_gets_no_row()
        {
            RowsOf(WithMigrationTestDb()).Should().BeEmpty();
        }

        [Test]
        public void a_library_without_a_row_gets_the_old_default()
        {
            var rows = RowsOf(WithMigrationTestDb(GivenAuthor));

            rows.Should().HaveCount(1);
            rows[0].RenameBooks.Should().BeFalse();
            rows[0].StandardBookFormat.Should().Be("{Book Title}/{Author Name} - {Book Title}{ (PartNumber)}");
            rows[0].AuthorFolderFormat.Should().Be("{Author Name}");
            rows[0].ReplaceIllegalCharacters.Should().BeTrue();
            rows[0].ColonReplacementFormat.Should().Be(4);
        }

        [Test]
        public void files_alone_count_as_a_library()
        {
            RowsOf(WithMigrationTestDb(GivenBookFile)).Should().HaveCount(1);
        }

        [Test]
        public void a_stored_row_is_never_touched()
        {
            var db = WithMigrationTestDb(c =>
            {
                GivenAuthor(c);
                c.Insert.IntoTable("NamingConfig").Row(new
                {
                    ReplaceIllegalCharacters = true,
                    AuthorFolderFormat = "{Author Name}",
                    RenameBooks = true,
                    StandardBookFormat = "{Author Name} - Vol {Volume:000}",
                    ColonReplacementFormat = 4
                });
            });

            var rows = RowsOf(db);

            rows.Should().HaveCount(1);
            rows[0].RenameBooks.Should().BeTrue();
            rows[0].StandardBookFormat.Should().Be("{Author Name} - Vol {Volume:000}");
        }

        private class NamingRow60
        {
            public int Id { get; set; }
            public bool ReplaceIllegalCharacters { get; set; }
            public string AuthorFolderFormat { get; set; }
            public bool RenameBooks { get; set; }
            public string StandardBookFormat { get; set; }
            public int ColonReplacementFormat { get; set; }
        }
    }
}
