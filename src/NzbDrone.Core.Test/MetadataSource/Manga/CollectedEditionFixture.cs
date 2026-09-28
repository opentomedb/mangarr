using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    [TestFixture]
    public class CollectedEditionFixture : CoreTest
    {
        private static List<GcdVolume> Volumes(params int?[] pageCounts)
        {
            return pageCounts.Select((p, i) => new GcdVolume { VolumeNumber = i + 1, PageCount = p }).ToList();
        }

        [Test]
        public void should_trust_the_omnibus_flag()
        {
            MangaSeriesMetadataProvider.IsCollectedEdition(true, Volumes(200, 200, 200)).Should().BeTrue();
        }

        [Test]
        public void should_detect_composition_data()
        {
            var vols = Volumes(200, 200);
            vols[0].Composition = new List<int> { 1, 2 };

            MangaSeriesMetadataProvider.IsCollectedEdition(false, vols).Should().BeTrue();
        }

        [Test]
        public void should_detect_deluxe_page_counts()
        {
            // Vinland Saga Book Edition shape: 2-in-1 books, no flag, no composition.
            MangaSeriesMetadataProvider.IsCollectedEdition(false, Volumes(484, 464, 444, 460)).Should().BeTrue();
        }

        [Test]
        public void should_not_flag_single_volume_page_counts()
        {
            // Dandadan shape: ~200pp singles.
            MangaSeriesMetadataProvider.IsCollectedEdition(false, Volumes(212, 214, 220, 206, 188)).Should().BeFalse();
        }

        [Test]
        public void should_not_flag_on_sparse_page_data()
        {
            // Two known page counts is too few to call it — even if both look big.
            MangaSeriesMetadataProvider.IsCollectedEdition(false, Volumes(450, 460, null, null)).Should().BeFalse();
        }

        [Test]
        public void should_ignore_null_and_zero_page_counts()
        {
            MangaSeriesMetadataProvider.IsCollectedEdition(false, Volumes(null, 0, 480, 460, 440)).Should().BeTrue();
        }
    }
}
