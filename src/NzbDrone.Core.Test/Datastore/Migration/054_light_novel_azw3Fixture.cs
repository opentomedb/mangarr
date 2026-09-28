using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class light_novel_azw3Fixture : MigrationTest<light_novel_azw3>
    {
        [Test]
        public void should_insert_azw3_disallowed_directly_before_epub_on_a_manga_profile()
        {
            // The live Manga profile after 048 (EPUB + the audiobook qualities prepended,
            // disallowed) as it is on the dev server before this migration.
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("QualityProfiles").Row(new
                {
                    Name = "Manga",
                    Cutoff = 2,
                    Items = "[{\"quality\":6,\"items\":[],\"allowed\":false},{\"quality\":13,\"items\":[],\"allowed\":false},{\"quality\":10,\"items\":[],\"allowed\":false},{\"quality\":11,\"items\":[],\"allowed\":false},{\"quality\":12,\"items\":[],\"allowed\":false},{\"quality\":0,\"items\":[],\"allowed\":false},{\"quality\":1,\"items\":[],\"allowed\":true},{\"quality\":5,\"items\":[],\"allowed\":true},{\"quality\":4,\"items\":[],\"allowed\":true},{\"quality\":3,\"items\":[],\"allowed\":true},{\"quality\":2,\"items\":[],\"allowed\":true}]",
                    UpgradeAllowed = true,
                    FormatItems = "[]",
                    MinFormatScore = 0,
                    CutoffFormatScore = 0
                });
            });

            var profile = db.Query<light_novel_azw3.Profile54>("SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"").Single();

            profile.Cutoff.Should().Be(2);
            profile.Items.Select(i => i.Quality).Should().Equal(7, 6, 13, 10, 11, 12, 0, 1, 5, 4, 3, 2);
            profile.Items.Single(i => i.Quality == 7).Allowed.Should().BeFalse();
            profile.Items.Single(i => i.Quality == 6).Allowed.Should().BeFalse();
        }

        [Test]
        public void should_insert_azw3_allowed_directly_before_epub_on_the_light_novel_epub_profile()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("QualityProfiles").Row(new
                {
                    Name = "Light Novel EPUB",
                    Cutoff = 6,
                    Items = "[{\"quality\":6,\"items\":[],\"allowed\":true},{\"quality\":0,\"items\":[],\"allowed\":false}]",
                    UpgradeAllowed = true,
                    FormatItems = "[]",
                    MinFormatScore = 0,
                    CutoffFormatScore = 0
                });
            });

            var profile = db.Query<light_novel_azw3.Profile54>("SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"").Single();

            profile.Cutoff.Should().Be(6);
            profile.Items.Select(i => i.Quality).Should().Equal(7, 6, 0);
            profile.Items.Single(i => i.Quality == 7).Allowed.Should().BeTrue();
            profile.Items.Single(i => i.Quality == 6).Allowed.Should().BeTrue();
        }

        [Test]
        public void should_not_duplicate_azw3_on_a_profile_that_already_has_it()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("QualityProfiles").Row(new
                {
                    Name = "Already patched",
                    Cutoff = 6,
                    Items = "[{\"quality\":7,\"items\":[],\"allowed\":true},{\"quality\":6,\"items\":[],\"allowed\":true},{\"quality\":0,\"items\":[],\"allowed\":false}]",
                    UpgradeAllowed = true,
                    FormatItems = "[]",
                    MinFormatScore = 0,
                    CutoffFormatScore = 0
                });
            });

            var profile = db.Query<light_novel_azw3.Profile54>("SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"").Single();

            profile.Items.Count(i => i.Quality == 7).Should().Be(1);
            profile.Items.Select(i => i.Quality).Should().Equal(7, 6, 0);
        }

        [Test]
        public void should_insert_azw3_before_a_group_holding_epub_using_the_groups_own_allowed_flag()
        {
            // A group item has no "quality" key; its qualities live in "items". EPUB (6) can be
            // grouped, in which case AZW3 goes before the group and copies the GROUP's own
            // allowed flag — the runtime only ever consults the group's own flag for a quality
            // nested inside it (QualityProfile.GetIndex resolves a grouped quality to the group's
            // index; QualityAllowedByProfileSpecification reads allowed at that index), so the
            // nested EPUB item's own flag is irrelevant here. Both the group and the nested item
            // are allowed in this case; the two tests below isolate the group flag from the
            // nested one.
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("QualityProfiles").Row(new
                {
                    Name = "Grouped",
                    Cutoff = 1000,
                    Items = "[{\"id\":1000,\"name\":\"Ebook\",\"items\":[{\"quality\":6,\"items\":[],\"allowed\":true}],\"allowed\":true},{\"quality\":0,\"items\":[],\"allowed\":false}]",
                    UpgradeAllowed = true,
                    FormatItems = "[]",
                    MinFormatScore = 0,
                    CutoffFormatScore = 0
                });
            });

            var profile = db.Query<light_novel_azw3.Profile54>("SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"").Single();

            profile.Items.Select(i => i.Quality).Should().Equal(7, null, 0);
            profile.Items.Single(i => i.Quality == 7).Allowed.Should().BeTrue();

            var group = profile.Items.Single(i => i.Quality == null);
            group.Id.Should().Be(1000);
            group.Items.Select(i => i.Quality).Should().Equal(6);
        }

        [Test]
        public void should_insert_azw3_disallowed_when_a_group_holding_epub_is_disallowed_even_though_nested_epub_is_allowed()
        {
            // The group itself is disallowed; the nested EPUB item's own "allowed": true must be
            // ignored, because the runtime never reads it directly.
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("QualityProfiles").Row(new
                {
                    Name = "Grouped disallowed",
                    Cutoff = 0,
                    Items = "[{\"id\":1000,\"name\":\"Ebook\",\"items\":[{\"quality\":6,\"items\":[],\"allowed\":true}],\"allowed\":false},{\"quality\":0,\"items\":[],\"allowed\":true}]",
                    UpgradeAllowed = true,
                    FormatItems = "[]",
                    MinFormatScore = 0,
                    CutoffFormatScore = 0
                });
            });

            var profile = db.Query<light_novel_azw3.Profile54>("SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"").Single();

            profile.Items.Select(i => i.Quality).Should().Equal(7, null, 0);
            profile.Items.Single(i => i.Quality == 7).Allowed.Should().BeFalse();
        }

        [Test]
        public void should_insert_azw3_allowed_when_a_group_holding_epub_is_allowed_even_though_nested_epub_is_disallowed()
        {
            // The inverse: the group itself is allowed; the nested EPUB item's own
            // "allowed": false must be ignored the same way.
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("QualityProfiles").Row(new
                {
                    Name = "Grouped allowed",
                    Cutoff = 1000,
                    Items = "[{\"id\":1000,\"name\":\"Ebook\",\"items\":[{\"quality\":6,\"items\":[],\"allowed\":false}],\"allowed\":true},{\"quality\":0,\"items\":[],\"allowed\":false}]",
                    UpgradeAllowed = true,
                    FormatItems = "[]",
                    MinFormatScore = 0,
                    CutoffFormatScore = 0
                });
            });

            var profile = db.Query<light_novel_azw3.Profile54>("SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"").Single();

            profile.Items.Select(i => i.Quality).Should().Equal(7, null, 0);
            profile.Items.Single(i => i.Quality == 7).Allowed.Should().BeTrue();
        }

        [Test]
        public void should_insert_azw3_disallowed_at_index_zero_when_the_profile_has_no_epub()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("QualityProfiles").Row(new
                {
                    Name = "Light Novel Audio",
                    Cutoff = 10,
                    Items = "[{\"quality\":13,\"items\":[],\"allowed\":true},{\"quality\":10,\"items\":[],\"allowed\":true}]",
                    UpgradeAllowed = true,
                    FormatItems = "[]",
                    MinFormatScore = 0,
                    CutoffFormatScore = 0
                });
            });

            var profile = db.Query<light_novel_azw3.Profile54>("SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"").Single();

            profile.Items.Select(i => i.Quality).Should().Equal(7, 13, 10);
            profile.Items.Single(i => i.Quality == 7).Allowed.Should().BeFalse();
        }
    }
}
