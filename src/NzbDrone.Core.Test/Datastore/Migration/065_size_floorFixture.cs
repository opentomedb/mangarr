using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // The size floor (2026-11-14): volume comic grades with no minimum get 10 MB; a user's own
    // minimum and every other grade stay as they were.
    [TestFixture]
    public class size_floorFixture : MigrationTest<size_floor>
    {
        private const string Select = "SELECT \"Quality\", \"MinSize\", \"MaxSize\" FROM \"QualityDefinitions\"";

        private static void GivenDefinition(size_floor c, int quality, string title, double? minSize)
        {
            c.Insert.IntoTable("QualityDefinitions").Row(new
            {
                Quality = quality,
                Title = title,
                MinSize = minSize,
                MaxSize = (double?)null
            });
        }

        [Test]
        public void should_floor_a_volume_grade_whose_minimum_is_zero_or_unset()
        {
            var db = WithMigrationTestDb(c =>
            {
                GivenDefinition(c, 2, "CBZ", 0);
                GivenDefinition(c, 3, "CBR", null);
                GivenDefinition(c, 1, "PDF", 0);
                GivenDefinition(c, 4, "ZIP", 0);
                GivenDefinition(c, 5, "RAR", 0);
            });

            var rows = db.Query<size_floor.Definition65>(Select).ToDictionary(r => r.Quality, r => r.MinSize);

            rows[2].Should().Be(10);
            rows[3].Should().Be(10);
            rows[1].Should().Be(10);
            rows[4].Should().Be(10);
            rows[5].Should().Be(10);
        }

        [Test]
        public void should_keep_a_minimum_the_user_set()
        {
            var db = WithMigrationTestDb(c => GivenDefinition(c, 2, "CBZ", 25));

            db.Query<size_floor.Definition65>(Select).Single().MinSize.Should().Be(25);
        }

        [Test]
        public void should_leave_chapter_ebook_and_audio_grades_alone()
        {
            var db = WithMigrationTestDb(c =>
            {
                GivenDefinition(c, 9, "CBZ (chapters)", 0);
                GivenDefinition(c, 14, "CBZ (fan translation)", 0);
                GivenDefinition(c, 6, "EPUB", 0);
                GivenDefinition(c, 8, "Ebook PDF", null);
                GivenDefinition(c, 12, "M4B", 0);
            });

            var rows = db.Query<size_floor.Definition65>(Select).ToList();

            rows.Should().HaveCount(5);
            rows.Select(r => r.MinSize ?? 0).Should().OnlyContain(m => m == 0);
        }

        [Test]
        public void should_not_touch_the_maximum()
        {
            var db = WithMigrationTestDb(c => GivenDefinition(c, 2, "CBZ", 0));

            db.Query<size_floor.Definition65>(Select).Single().MaxSize.Should().BeNull();
        }
    }
}
