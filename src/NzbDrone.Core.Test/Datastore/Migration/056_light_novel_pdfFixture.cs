using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // LN PDF (2026-09-22): Ebook PDF (8) goes into EVERY profile, unticked, directly below the
    // item holding AZW3 (7) -- or below EPUB's (6) when a profile has no AZW3, else at index 0.
    // Cutoffs, names and every existing item are untouched.
    [TestFixture]
    public class light_novel_pdfFixture : MigrationTest<light_novel_pdf>
    {
        private light_novel_pdf.Profile56 MigrateProfile(string name, int cutoff, string items)
        {
            var db = WithMigrationTestDb(c =>
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
            });

            return db.Query<light_novel_pdf.Profile56>("SELECT \"Id\", \"Name\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"").Single();
        }

        [Test]
        public void should_insert_ebook_pdf_unticked_below_azw3_on_the_manga_profile()
        {
            // the dev server's Manga profile after 054.
            var profile = MigrateProfile("Manga", 2,
                "[{\"quality\":7,\"items\":[],\"allowed\":false},{\"quality\":6,\"items\":[],\"allowed\":false},{\"quality\":13,\"items\":[],\"allowed\":false},{\"quality\":10,\"items\":[],\"allowed\":false},{\"quality\":11,\"items\":[],\"allowed\":false},{\"quality\":12,\"items\":[],\"allowed\":false},{\"quality\":0,\"items\":[],\"allowed\":false},{\"quality\":1,\"items\":[],\"allowed\":true},{\"quality\":5,\"items\":[],\"allowed\":true},{\"quality\":4,\"items\":[],\"allowed\":true},{\"quality\":3,\"items\":[],\"allowed\":true},{\"quality\":2,\"items\":[],\"allowed\":true}]");

            profile.Cutoff.Should().Be(2);
            profile.Items.Select(i => i.Quality).Should().Equal(8, 7, 6, 13, 10, 11, 12, 0, 1, 5, 4, 3, 2);
            profile.Items.Single(i => i.Quality == 8).Allowed.Should().BeFalse();
            profile.Items.Single(i => i.Quality == 1).Allowed.Should().BeTrue();
        }

        [Test]
        public void should_insert_ebook_pdf_unticked_below_an_allowed_azw3_on_the_light_novel_epub_profile()
        {
            // The Light Novel EPUB profile as GetDefaultProfile built it, after 054 (AZW3 allowed).
            var profile = MigrateProfile("Light Novel EPUB", 6,
                "[{\"quality\":0,\"items\":[],\"allowed\":false},{\"quality\":1,\"items\":[],\"allowed\":false},{\"quality\":5,\"items\":[],\"allowed\":false},{\"quality\":4,\"items\":[],\"allowed\":false},{\"quality\":3,\"items\":[],\"allowed\":false},{\"quality\":2,\"items\":[],\"allowed\":false},{\"quality\":7,\"items\":[],\"allowed\":true},{\"quality\":6,\"items\":[],\"allowed\":true},{\"quality\":13,\"items\":[],\"allowed\":false}]");

            profile.Cutoff.Should().Be(6);
            profile.Items.Select(i => i.Quality).Should().Equal(0, 1, 5, 4, 3, 2, 8, 7, 6, 13);
            profile.Items.Single(i => i.Quality == 8).Allowed.Should().BeFalse();
            profile.Items.Single(i => i.Quality == 7).Allowed.Should().BeTrue();
            profile.Items.Single(i => i.Quality == 6).Allowed.Should().BeTrue();
        }

        [Test]
        public void should_insert_ebook_pdf_unticked_before_a_group_holding_azw3_even_when_the_group_is_allowed()
        {
            var profile = MigrateProfile("Grouped", 1000,
                "[{\"id\":1000,\"name\":\"Ebook\",\"items\":[{\"quality\":7,\"items\":[],\"allowed\":true},{\"quality\":6,\"items\":[],\"allowed\":true}],\"allowed\":true},{\"quality\":0,\"items\":[],\"allowed\":false}]");

            profile.Cutoff.Should().Be(1000);
            profile.Items.Select(i => i.Quality).Should().Equal(8, null, 0);
            profile.Items.Single(i => i.Quality == 8).Allowed.Should().BeFalse();

            var group = profile.Items.Single(i => i.Quality == null);
            group.Id.Should().Be(1000);
            group.Items.Select(i => i.Quality).Should().Equal(7, 6);
        }

        [Test]
        public void should_insert_ebook_pdf_below_epub_when_a_profile_has_no_azw3()
        {
            var profile = MigrateProfile("Custom EPUB", 6,
                "[{\"quality\":0,\"items\":[],\"allowed\":false},{\"quality\":6,\"items\":[],\"allowed\":true}]");

            profile.Items.Select(i => i.Quality).Should().Equal(0, 8, 6);
            profile.Items.Single(i => i.Quality == 8).Allowed.Should().BeFalse();
        }

        [Test]
        public void should_insert_ebook_pdf_at_index_zero_when_a_profile_has_neither()
        {
            var profile = MigrateProfile("Audio only", 10,
                "[{\"quality\":13,\"items\":[],\"allowed\":true},{\"quality\":10,\"items\":[],\"allowed\":true}]");

            profile.Items.Select(i => i.Quality).Should().Equal(8, 13, 10);
            profile.Items.Single(i => i.Quality == 8).Allowed.Should().BeFalse();
        }

        [Test]
        public void should_not_duplicate_ebook_pdf_on_a_profile_that_already_has_it()
        {
            var profile = MigrateProfile("Already patched", 6,
                "[{\"quality\":8,\"items\":[],\"allowed\":true},{\"quality\":7,\"items\":[],\"allowed\":true},{\"quality\":6,\"items\":[],\"allowed\":true}]");

            profile.Items.Select(i => i.Quality).Should().Equal(8, 7, 6);
            profile.Items.Single(i => i.Quality == 8).Allowed.Should().BeTrue();
        }
    }
}
