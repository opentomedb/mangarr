using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class light_novel_editionsFixture : MigrationTest<light_novel_editions>
    {
        // The live Manga profile as it is on the dev server before this migration (ids 0,1,5,4,3,2).
        private const string MangaItems = "[{\"quality\":0,\"items\":[],\"allowed\":false},{\"quality\":1,\"items\":[],\"allowed\":true},{\"quality\":5,\"items\":[],\"allowed\":true},{\"quality\":4,\"items\":[],\"allowed\":true},{\"quality\":3,\"items\":[],\"allowed\":true},{\"quality\":2,\"items\":[],\"allowed\":true}]";

        [Test]
        public void should_add_the_edition_and_author_columns_with_their_defaults()
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

            var edition = db.Query<Edition48>("SELECT \"Id\", \"MediaType\", \"Monitored\" FROM \"Editions\"").Single();
            edition.MediaType.Should().Be(0);
            edition.Monitored.Should().BeTrue();

            var author = db.Query<Author48>("SELECT \"Id\", \"QualityProfileId\", \"AudioQualityProfileId\", \"AudioAvailable\", \"LastAudioSearch\" FROM \"Authors\"").Single();
            author.QualityProfileId.Should().Be(1);
            author.AudioQualityProfileId.Should().BeNull();
            author.AudioAvailable.Should().BeFalse();
            author.LastAudioSearch.Should().BeNull();
        }

        [Test]
        public void should_prepend_the_light_novel_qualities_disallowed()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("QualityProfiles").Row(new
                {
                    Name = "Manga",
                    Cutoff = 2,
                    Items = MangaItems,
                    UpgradeAllowed = true,
                    FormatItems = "[]",
                    MinFormatScore = 0,
                    CutoffFormatScore = 0
                });
            });

            var profile = db.Query<light_novel_editions.Profile48>("SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"").Single();

            profile.Cutoff.Should().Be(2);
            profile.Items.Select(i => i.Quality).Should().StartWith(new int?[] { 6, 13, 10, 11, 12 });
            profile.Items.Where(i => new int?[] { 6, 13, 10, 11, 12 }.Contains(i.Quality)).Should().OnlyContain(i => !i.Allowed);
            profile.Items.Where(i => new int?[] { 1, 5, 4, 3, 2 }.Contains(i.Quality)).Should().OnlyContain(i => i.Allowed);
            profile.Items.Should().HaveCount(11);
        }

        [Test]
        public void should_not_duplicate_a_quality_the_profile_already_has()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("QualityProfiles").Row(new
                {
                    Name = "Already patched",
                    Cutoff = 6,
                    Items = "[{\"quality\":6,\"items\":[],\"allowed\":true},{\"quality\":0,\"items\":[],\"allowed\":false}]",
                    UpgradeAllowed = true,
                    FormatItems = "[]",
                    MinFormatScore = 0,
                    CutoffFormatScore = 0
                });
            });

            var profile = db.Query<light_novel_editions.Profile48>("SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"").Single();

            profile.Items.Count(i => i.Quality == 6).Should().Be(1);
            profile.Items.Single(i => i.Quality == 6).Allowed.Should().BeTrue();
            profile.Items.Should().HaveCount(6);
        }

        [Test]
        public void should_keep_a_grouped_item_intact_and_not_duplicate_a_quality_inside_it()
        {
            // A group item has no "quality" key; its qualities live in "items". The rewrite must
            // hand it back unchanged and must not prepend a quality the group already holds.
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("QualityProfiles").Row(new
                {
                    Name = "Grouped",
                    Cutoff = 1000,
                    Items = "[{\"id\":1000,\"name\":\"Audio\",\"items\":[{\"quality\":10,\"items\":[],\"allowed\":true},{\"quality\":11,\"items\":[],\"allowed\":true}],\"allowed\":true},{\"quality\":0,\"items\":[],\"allowed\":false}]",
                    UpgradeAllowed = true,
                    FormatItems = "[]",
                    MinFormatScore = 0,
                    CutoffFormatScore = 0
                });
            });

            var profile = db.Query<light_novel_editions.Profile48>("SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"").Single();

            profile.Cutoff.Should().Be(1000);
            profile.Items.Select(i => i.Quality).Should().Equal(6, 13, 12, null, 0);

            var group = profile.Items.Single(i => i.Quality == null);
            group.Id.Should().Be(1000);
            group.Name.Should().Be("Audio");
            group.Allowed.Should().BeTrue();
            group.Items.Select(i => i.Quality).Should().Equal(10, 11);
            group.Items.Should().OnlyContain(i => i.Allowed);
        }

        private class Edition48
        {
            public int Id { get; set; }
            public int MediaType { get; set; }
            public bool Monitored { get; set; }
        }

        private class Author48
        {
            public int Id { get; set; }
            public int QualityProfileId { get; set; }
            public int? AudioQualityProfileId { get; set; }
            public bool AudioAvailable { get; set; }
            public DateTime? LastAudioSearch { get; set; }
        }
    }
}
