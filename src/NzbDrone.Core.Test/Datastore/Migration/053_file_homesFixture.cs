using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class file_homesFixture : MigrationTest<file_homes>
    {
        [Test]
        public void should_add_the_home_and_adopted_columns_defaulted_and_leave_every_existing_row_alone()
        {
            var db = WithMigrationTestDb(c =>
            {
                // A live manga file as it is on the dev server before this migration.
                c.Insert.IntoTable("BookFiles").Row(new
                {
                    Path = "/manga/Chainsaw Man/Chainsaw Man - Vol 001.cbz",
                    Size = 1,
                    Modified = DateTime.UtcNow,
                    DateAdded = DateTime.UtcNow,
                    Quality = "{\"quality\":{\"id\":1,\"name\":\"Unknown\"},\"revision\":{\"version\":1,\"real\":0,\"isRepack\":false}}",
                    EditionId = 1,
                    CalibreId = 0,
                    Part = 1
                });

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

                c.Insert.IntoTable("AuthorMetadata").Row(new
                {
                    ForeignAuthorId = "local-chainsaw-man",
                    TitleSlug = "local-chainsaw-man",
                    Name = "Chainsaw Man",
                    SortName = "chainsaw man",
                    NameLastFirst = "Chainsaw Man",
                    SortNameLastFirst = "chainsaw man",
                    Status = 1,
                    Images = "[]",
                    Aliases = "[]",
                    TotalVolumes = 17
                });
            });

            var file = db.Query<File53>("SELECT \"Id\", \"Path\", \"Size\", \"Quality\", \"EditionId\", \"CalibreId\", \"Part\", \"Home\", \"Adopted\" FROM \"BookFiles\"").Single();
            file.Home.Should().Be(0);
            file.Adopted.Should().BeFalse();
            file.Path.Should().Be("/manga/Chainsaw Man/Chainsaw Man - Vol 001.cbz");
            file.Size.Should().Be(1);
            file.Quality.Should().Be("{\"quality\":{\"id\":1,\"name\":\"Unknown\"},\"revision\":{\"version\":1,\"real\":0,\"isRepack\":false}}");
            file.EditionId.Should().Be(1);
            file.CalibreId.Should().Be(0);
            file.Part.Should().Be(1);

            var author = db.Query<Author53>("SELECT \"Id\", \"CleanName\", \"Path\", \"Monitored\", \"QualityProfileId\", \"CopyInPending\", \"AdoptedTagWrite\" FROM \"Authors\"").Single();
            author.AdoptedTagWrite.Should().BeFalse();
            author.CleanName.Should().Be("chainsawman");
            author.Path.Should().Be("/manga/Chainsaw Man");
            author.Monitored.Should().BeTrue();
            author.QualityProfileId.Should().Be(1);
            author.CopyInPending.Should().BeFalse();

            var meta = db.Query<Meta53>("SELECT \"Id\", \"ForeignAuthorId\", \"Name\", \"TotalVolumes\", \"Writer\" FROM \"AuthorMetadata\"").Single();
            meta.Writer.Should().BeNull();
            meta.ForeignAuthorId.Should().Be("local-chainsaw-man");
            meta.Name.Should().Be("Chainsaw Man");
            meta.TotalVolumes.Should().Be(17);
        }

        private class File53
        {
            public int Id { get; set; }
            public string Path { get; set; }
            public long Size { get; set; }
            public string Quality { get; set; }
            public int EditionId { get; set; }
            public int CalibreId { get; set; }
            public int Part { get; set; }
            public int Home { get; set; }
            public bool Adopted { get; set; }
        }

        private class Author53
        {
            public int Id { get; set; }
            public string CleanName { get; set; }
            public string Path { get; set; }
            public bool Monitored { get; set; }
            public int QualityProfileId { get; set; }
            public bool CopyInPending { get; set; }
            public bool AdoptedTagWrite { get; set; }
        }

        private class Meta53
        {
            public int Id { get; set; }
            public string ForeignAuthorId { get; set; }
            public string Name { get; set; }
            public int TotalVolumes { get; set; }
            public string Writer { get; set; }
        }
    }
}
