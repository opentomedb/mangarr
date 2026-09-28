using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    [TestFixture]
    public class DateOutlierFixture : CoreTest
    {
        private static List<MangaVolumeMetadata> Volumes(params string[] dates)
        {
            return dates.Select((d, i) => new MangaVolumeMetadata
            {
                VolumeNumber = i + 1,
                ReleaseDate = d == null ? (DateTime?)null : DateTime.Parse(d)
            }).ToList();
        }

        [Test]
        public void should_null_a_regression_flanked_by_agreeing_neighbors()
        {
            // Fire Force shape: 2018 stamped between consistent 2022 neighbors.
            var vols = Volumes("2022-07-05", "2022-08-16", "2018-10-18", "2022-12-20");

            MangaSeriesMetadataProvider.NullDateOutliers(vols);

            vols[2].ReleaseDate.Should().BeNull();
            vols[1].ReleaseDate.Should().NotBeNull();
            vols[3].ReleaseDate.Should().NotBeNull();
        }

        [Test]
        public void should_null_a_reprint_spike_between_agreeing_neighbors()
        {
            // Vinland shape: a 2025 deluxe-reprint date on a volume between 2015-era neighbors.
            var vols = Volumes("2015-06-01", "2025-05-06", "2016-12-01");

            MangaSeriesMetadataProvider.NullDateOutliers(vols);

            vols[1].ReleaseDate.Should().BeNull();
        }

        [Test]
        public void should_leave_monotonic_series_untouched()
        {
            var vols = Volumes("2020-01-07", "2020-06-02", "2021-01-05");

            MangaSeriesMetadataProvider.NullDateOutliers(vols);

            vols.Should().OnlyContain(v => v.ReleaseDate.HasValue);
        }

        [Test]
        public void should_leave_disagreeing_neighbors_alone()
        {
            // When the flanks contradict each other (sharply descending here), which date is
            // wrong is ambiguous — the middle is left alone even though it regresses vs prev.
            var vols = Volumes("2016-06-01", "2013-01-01", "2011-06-01");

            MangaSeriesMetadataProvider.NullDateOutliers(vols);

            vols[1].ReleaseDate.Should().NotBeNull();
        }

        [Test]
        public void should_never_touch_edge_volumes_and_skip_dateless()
        {
            var vols = Volumes("2010-01-01", null, "2020-01-01", "2020-06-01");

            MangaSeriesMetadataProvider.NullDateOutliers(vols);

            vols[0].ReleaseDate.Should().NotBeNull();
            vols[3].ReleaseDate.Should().NotBeNull();
        }

        [Test]
        public void should_not_crash_on_empty_or_tiny_lists()
        {
            var empty = new List<MangaVolumeMetadata>();
            var one = Volumes("2020-01-01");
            var two = Volumes("2020-01-01", "1990-01-01");

            MangaSeriesMetadataProvider.NullDateOutliers(empty);
            MangaSeriesMetadataProvider.NullDateOutliers(one);
            MangaSeriesMetadataProvider.NullDateOutliers(two);

            one[0].ReleaseDate.Should().NotBeNull();
            two[1].ReleaseDate.Should().NotBeNull();
        }
    }
}
