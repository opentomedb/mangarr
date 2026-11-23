using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Fan translations (2026-11-05): CBZ (fan translation) is quality 14, added as the lowest,
    // disallowed item to every profile that does not list it yet.
    [TestFixture]
    public class fan_translation_qualityFixture : MigrationTest<fan_translation_quality>
    {
        private const string Select = "SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"";

        private static void GivenProfile(fan_translation_quality c, string name, int cutoff, string items)
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
        public void should_add_fan_translation_disallowed_as_the_lowest_item_of_a_manga_profile()
        {
            var db = WithMigrationTestDb(c => GivenProfile(c,
                "Manga",
                2,
                "[{\"quality\":9,\"items\":[],\"allowed\":true},{\"quality\":1,\"items\":[],\"allowed\":false},{\"quality\":5,\"items\":[],\"allowed\":false},{\"quality\":4,\"items\":[],\"allowed\":false},{\"quality\":3,\"items\":[],\"allowed\":false},{\"quality\":2,\"items\":[],\"allowed\":true}]"));

            var profile = db.Query<fan_translation_quality.Profile64>(Select).Single();

            profile.Cutoff.Should().Be(2);
            profile.Items.Select(i => i.Quality).Should().Equal(14, 9, 1, 5, 4, 3, 2);
            profile.Items.First().Allowed.Should().BeFalse();
            profile.Items.Where(i => i.Quality == 9 || i.Quality == 2).Select(i => i.Allowed).Should().OnlyContain(a => a);
            profile.Items.Where(i => i.Quality != 14 && i.Quality != 9 && i.Quality != 2).Select(i => i.Allowed).Should().OnlyContain(a => !a);
        }

        [Test]
        public void should_add_fan_translation_disallowed_to_a_light_novel_profile()
        {
            var items = "[{\"quality\":6,\"items\":[],\"allowed\":true},{\"quality\":0,\"items\":[],\"allowed\":false}]";
            var db = WithMigrationTestDb(c => GivenProfile(c, "Light Novel EPUB", 6, items));

            var profile = db.Query<fan_translation_quality.Profile64>(Select).Single();

            profile.Cutoff.Should().Be(6);
            profile.Items.Select(i => i.Quality).Should().Equal(14, 6, 0);
            profile.Items.Single(i => i.Quality == 14).Allowed.Should().BeFalse();
            profile.Items.Single(i => i.Quality == 6).Allowed.Should().BeTrue();
            profile.Items.Single(i => i.Quality == 0).Allowed.Should().BeFalse();
        }

        [Test]
        public void should_not_duplicate_fan_translation_on_a_profile_that_already_has_it()
        {
            var db = WithMigrationTestDb(c => GivenProfile(c,
                "Patched",
                2,
                "[{\"quality\":14,\"items\":[],\"allowed\":false},{\"quality\":2,\"items\":[],\"allowed\":true}]"));

            var profile = db.Query<fan_translation_quality.Profile64>(Select).Single();

            profile.Items.Select(i => i.Quality).Should().Equal(14, 2);
        }

        [Test]
        public void should_see_fan_translation_inside_a_group_and_leave_the_group_alone()
        {
            var db = WithMigrationTestDb(c => GivenProfile(c,
                "Grouped",
                2,
                "[{\"id\":1000,\"name\":\"Archives\",\"items\":[{\"quality\":14,\"allowed\":false},{\"quality\":2,\"allowed\":true}],\"allowed\":true}]"));

            var profile = db.Query<fan_translation_quality.Profile64>(Select).Single();

            profile.Items.Should().HaveCount(1);
            profile.Items.Single().Id.Should().Be(1000);
            profile.Items.Single().Items.Select(i => i.Quality).Should().Equal(14, 2);
            profile.Items.Single().Items.Select(i => i.Allowed).Should().Equal(false, true);
        }
    }
}
