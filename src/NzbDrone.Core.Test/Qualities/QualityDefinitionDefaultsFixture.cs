using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Qualities
{
    // The size floor (2026-11-14): a fresh install starts with the 10 MB minimum on the five
    // volume comic grades and no minimum anywhere else.
    [TestFixture]
    public class QualityDefinitionDefaultsFixture : CoreTest
    {
        private static readonly Quality[] VolumeGrades = { Quality.PDF, Quality.CBZ, Quality.CBR, Quality.ZIP, Quality.RAR };

        [Test]
        public void volume_comic_grades_default_to_a_10_mb_minimum()
        {
            Quality.DefaultQualityDefinitions
                   .Where(d => VolumeGrades.Contains(d.Quality))
                   .Select(d => d.MinSize)
                   .Should().HaveCount(5).And.OnlyContain(m => m == 10);
        }

        [Test]
        public void every_other_grade_defaults_to_no_minimum()
        {
            Quality.DefaultQualityDefinitions
                   .Where(d => !VolumeGrades.Contains(d.Quality))
                   .Select(d => d.MinSize ?? 0)
                   .Should().OnlyContain(m => m == 0);
        }

        [Test]
        public void no_grade_defaults_to_a_maximum()
        {
            Quality.DefaultQualityDefinitions.Select(d => d.MaxSize ?? 0).Should().OnlyContain(m => m == 0);
        }
    }
}
