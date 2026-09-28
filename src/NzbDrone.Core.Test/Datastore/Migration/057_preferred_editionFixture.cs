using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class preferred_editionFixture : MigrationTest<preferred_edition>
    {
        [Test]
        public void should_add_null_edition_columns_and_leave_every_existing_row_alone()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("AuthorMetadata").Row(new
                {
                    ForeignAuthorId = "local-attack-on-titan",
                    TitleSlug = "local-attack-on-titan",
                    Name = "Attack on Titan",
                    SortName = "attack on titan",
                    NameLastFirst = "Attack on Titan",
                    SortNameLastFirst = "attack on titan",
                    Status = 1,
                    Images = "[]",
                    Aliases = "[\"Shingeki no Kyojin\"]",
                    TotalVolumes = 34,
                    AniListId = 53390
                });
            });

            var rows = db.Query<Meta57>("SELECT \"Name\", \"Aliases\", \"TotalVolumes\", \"AniListId\", \"EditionLanguage\", \"TomeLineId\", \"AnchorName\" FROM \"AuthorMetadata\"");

            rows.Should().HaveCount(1);
            rows[0].Name.Should().Be("Attack on Titan");
            rows[0].Aliases.Should().Be("[\"Shingeki no Kyojin\"]");
            rows[0].TotalVolumes.Should().Be(34);
            rows[0].AniListId.Should().Be(53390);
            rows[0].EditionLanguage.Should().BeNull();
            rows[0].TomeLineId.Should().BeNull();
            rows[0].AnchorName.Should().BeNull();

            db.Query<Book57>("SELECT \"ReleaseDatePrecision\" FROM \"Books\"").Should().BeEmpty();
        }

        private class Meta57
        {
            public string Name { get; set; }
            public string Aliases { get; set; }
            public int TotalVolumes { get; set; }
            public int? AniListId { get; set; }
            public string EditionLanguage { get; set; }
            public string TomeLineId { get; set; }
            public string AnchorName { get; set; }
        }

        private class Book57
        {
            public string ReleaseDatePrecision { get; set; }
        }
    }
}
