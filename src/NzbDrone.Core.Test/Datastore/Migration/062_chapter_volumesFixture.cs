using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Volumes built from chapter packs (2026-10-21): CBZ (chapters) is quality 9, added as the lowest
    // allowed item to every profile that already lists CBZ; Authors gain BuildFromChapters (on) and
    // Editions gain a nullable Chapters column.
    [TestFixture]
    public class chapter_volumesFixture : MigrationTest<chapter_volumes>
    {
        private const string Select = "SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"";

        private static void GivenProfile(chapter_volumes c, string name, int cutoff, string items)
        {
            c.Insert.IntoTable("QualityProfiles").Row(new
            {
                Name = name,
                Cutoff = cutoff,
                Items = items,
                UpgradeAllowed = true,
                FormatItems = "[]",
                MinFormatScore = 0,
                CutoffFormatScore = 0
            });
        }

        [Test]
        public void should_add_cbz_chapters_allowed_as_the_lowest_item_of_a_profile_with_cbz()
        {
            var db = WithMigrationTestDb(c => GivenProfile(c, "Manga", 2,
                "[{\"quality\":1,\"items\":[],\"allowed\":true},{\"quality\":5,\"items\":[],\"allowed\":true},{\"quality\":4,\"items\":[],\"allowed\":true},{\"quality\":3,\"items\":[],\"allowed\":true},{\"quality\":2,\"items\":[],\"allowed\":true}]"));

            var profile = db.Query<chapter_volumes.Profile62>(Select).Single();

            profile.Cutoff.Should().Be(2);
            profile.Items.Select(i => i.Quality).Should().Equal(9, 1, 5, 4, 3, 2);
            profile.Items.First().Allowed.Should().BeTrue();
        }

        [Test]
        public void should_add_cbz_chapters_disallowed_to_a_profile_without_cbz()
        {
            var items = "[{\"quality\":6,\"items\":[],\"allowed\":true},{\"quality\":0,\"items\":[],\"allowed\":false}]";
            var db = WithMigrationTestDb(c => GivenProfile(c, "Light Novel EPUB", 6, items));

            var profile = db.Query<chapter_volumes.Profile62>(Select).Single();

            profile.Cutoff.Should().Be(6);
            profile.Items.Select(i => i.Quality).Should().Equal(9, 6, 0);
            profile.Items.Single(i => i.Quality == 9).Allowed.Should().BeFalse();
            profile.Items.Single(i => i.Quality == 6).Allowed.Should().BeTrue();
            profile.Items.Single(i => i.Quality == 0).Allowed.Should().BeFalse();
        }

        [Test]
        public void should_not_duplicate_cbz_chapters_on_a_profile_that_already_has_it()
        {
            var db = WithMigrationTestDb(c => GivenProfile(c, "Patched", 2,
                "[{\"quality\":9,\"items\":[],\"allowed\":true},{\"quality\":2,\"items\":[],\"allowed\":true}]"));

            var profile = db.Query<chapter_volumes.Profile62>(Select).Single();

            profile.Items.Count(i => i.Quality == 9).Should().Be(1);
        }

        [Test]
        public void should_default_build_from_chapters_on_and_add_a_null_chapters_column()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("Authors").Row(new
                {
                    CleanName = "chainsawman",
                    Path = "/manga/Chainsaw Man",
                    Monitored = true,
                    MonitorNewItems = 0,
                    QualityProfileId = 1,
                    MetadataProfileId = 1,
                    AuthorMetadataId = 1
                });

                c.Insert.IntoTable("Editions").Row(new
                {
                    BookId = 1,
                    ForeignEditionId = "local-chainsaw-man-v1-ed",
                    TitleSlug = "local-chainsaw-man-v1-ed",
                    Title = "Chainsaw Man Vol. 1",
                    Images = "[]",
                    Monitored = true,
                    ManualAdd = true
                });
            });

            db.Query<AuthorRow62>("SELECT \"BuildFromChapters\" FROM \"Authors\"").Single().BuildFromChapters.Should().Be(1);
            db.Query<EditionRow62>("SELECT \"Chapters\" FROM \"Editions\"").Single().Chapters.Should().BeNull();
        }

        private class AuthorRow62
        {
            public long BuildFromChapters { get; set; }
        }

        private class EditionRow62
        {
            public string Chapters { get; set; }
        }
    }
}
