using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class anilist_idFixture : MigrationTest<anilist_id>
    {
        [Test]
        public void should_add_a_null_anilist_id_and_leave_every_existing_row_alone()
        {
            var db = WithMigrationTestDb(c =>
            {
                // A live manga row as it is on the dev server before this migration (the wrong Fairy Tail).
                c.Insert.IntoTable("AuthorMetadata").Row(new
                {
                    ForeignAuthorId = "local-fairy-tail",
                    TitleSlug = "local-fairy-tail",
                    Name = "Fairy Tail",
                    SortName = "fairy tail",
                    NameLastFirst = "Fairy Tail",
                    SortNameLastFirst = "fairy tail",
                    Status = 1,
                    Images = "[{\"url\":\"https://s4.anilist.co/file/anilistcdn/media/manga/cover/large/bx128087-x.jpg\",\"coverType\":\"poster\"}]",
                    Overview = "Pantsu Agerune chapter list",
                    Aliases = "[\"Pantsu Agerune\",\"Kenkyuu tte Muzukashii\"]",
                    TotalVolumes = 63
                });

                // A light-novel row (048 shape: same table, suffixed id).
                c.Insert.IntoTable("AuthorMetadata").Row(new
                {
                    ForeignAuthorId = "local-sword-art-online~ln",
                    TitleSlug = "local-sword-art-online~ln",
                    Name = "Sword Art Online",
                    SortName = "sword art online",
                    NameLastFirst = "Sword Art Online",
                    SortNameLastFirst = "sword art online",
                    Status = 0,
                    Images = "[]",
                    Aliases = "[]",
                    TotalVolumes = 28
                });
            });

            var rows = db.Query<Meta49>("SELECT \"Id\", \"ForeignAuthorId\", \"Name\", \"Overview\", \"Aliases\", \"TotalVolumes\", \"Images\", \"AniListId\" FROM \"AuthorMetadata\" ORDER BY \"Id\"");

            rows.Should().HaveCount(2);
            rows.Should().OnlyContain(r => r.AniListId == null);

            rows[0].ForeignAuthorId.Should().Be("local-fairy-tail");
            rows[0].Name.Should().Be("Fairy Tail");
            rows[0].Overview.Should().Be("Pantsu Agerune chapter list");
            rows[0].Aliases.Should().Be("[\"Pantsu Agerune\",\"Kenkyuu tte Muzukashii\"]");
            rows[0].TotalVolumes.Should().Be(63);
            rows[0].Images.Should().Contain("bx128087");

            rows[1].ForeignAuthorId.Should().Be("local-sword-art-online~ln");
            rows[1].Name.Should().Be("Sword Art Online");
            rows[1].TotalVolumes.Should().Be(28);
            rows[1].Images.Should().Be("[]");
            rows[1].Aliases.Should().Be("[]");
        }

        private class Meta49
        {
            public int Id { get; set; }
            public string ForeignAuthorId { get; set; }
            public string Name { get; set; }
            public string Overview { get; set; }
            public string Aliases { get; set; }
            public int TotalVolumes { get; set; }
            public string Images { get; set; }
            public int? AniListId { get; set; }
        }
    }
}
