using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Manga;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    [TestFixture]
    public class BookInfoProxyVolumeCountFixture
    {
        private static int Count(int cap, string aniStatus, int? aniVols, int muVols, int mdx, int indexer)
        {
            return MangaSeriesMetadataProvider.DetermineVolumeCount(cap, aniStatus, aniVols, muVols, mdx, () => indexer);
        }

        [Test]
        public void max_volume_cap_always_wins()
        {
            Count(5, "FINISHED", 24, 99, 99, 99).Should().Be(5);
        }

        [Test]
        public void finished_uses_authoritative_count_over_overcounting_live_sources()
        {
            // Gyo: AniList + MangaUpdates both FINISHED = 2; MangaDex aggregate / indexer over-count.
            Count(0, "FINISHED", 2, 2, 7, 3).Should().Be(2);

            // Chainsaw Man.
            Count(0, "FINISHED", 24, 24, 0, 19).Should().Be(24);
        }

        [Test]
        public void finished_disagreement_prefers_lower_count()
        {
            // AniList counts JP tankoubon (24); MangaUpdates the fewer-volume English omnibus (8) -> 8.
            Count(0, "FINISHED", 24, 8, 0, 0).Should().Be(8);
            Count(0, "FINISHED", 8, 24, 0, 0).Should().Be(8);
        }

        [Test]
        public void finished_disagreement_distrusts_implausibly_low_count()
        {
            // A 4x+ gap is bad AniList data (a "FINISHED, 1 volume" series that MangaUpdates knows
            // has 11), not an omnibus -> trust MangaUpdates rather than silently dropping 10 volumes.
            Count(0, "FINISHED", 1, 11, 0, 0).Should().Be(11);
            Count(0, "FINISHED", 11, 1, 0, 0).Should().Be(11);

            // Boundary: exactly 4x is still omnibus-plausible (a 4-in-1) -> keep the lower count;
            // just past 4x, the low value is distrusted.
            Count(0, "FINISHED", 10, 40, 0, 0).Should().Be(10);
            Count(0, "FINISHED", 10, 41, 0, 0).Should().Be(41);
        }

        [Test]
        public void mangaupdates_supplies_count_for_ongoing_series()
        {
            // Dandadan: AniList volumes null (RELEASING); MangaUpdates "24 Volumes (Ongoing)".
            Count(0, "RELEASING", null, 24, 5, 8).Should().Be(24);
        }

        [Test]
        public void mangaupdates_covers_finished_series_anilist_missed()
        {
            Count(0, null, null, 2, 0, 0).Should().Be(2);
        }

        [Test]
        public void falls_back_to_live_when_no_authority()
        {
            Count(0, "RELEASING", null, 0, 5, 8).Should().Be(8);
            Count(0, "RELEASING", null, 0, 9, 6).Should().Be(9);
        }

        [Test]
        public void nothing_known_returns_zero_no_fabrication()
        {
            Count(0, "RELEASING", null, 0, 0, 0).Should().Be(0);
            Count(0, null, null, 0, 0, 0).Should().Be(0);
        }

        [Test]
        public void indexer_search_not_run_when_a_higher_source_resolves()
        {
            var called = 0;
            Func<int> indexer = () =>
            {
                called++;
                return 99;
            };

            MangaSeriesMetadataProvider.DetermineVolumeCount(0, "FINISHED", 2, 2, 7, indexer).Should().Be(2);
            MangaSeriesMetadataProvider.DetermineVolumeCount(0, "RELEASING", null, 24, 0, indexer).Should().Be(24);
            MangaSeriesMetadataProvider.DetermineVolumeCount(5, "RELEASING", null, 0, 0, indexer).Should().Be(5);

            called.Should().Be(0, "the expensive indexer search must be skipped when cap/AniList/MangaUpdates resolve the count");
        }

        [TestCase("24 Volumes (Complete)", 24)]
        [TestCase("2 Volumes (Ongoing)", 2)]
        [TestCase("1 Volume (Complete)", 1)]
        [TestCase("24 Volumes (Complete)\n\nPart 1: 11 Volumes\nPart 2: 13 Volumes", 24)]
        [TestCase("Complete", 0)]
        [TestCase("Ongoing", 0)]
        [TestCase("Oneshot", 0)]
        [TestCase("", 0)]
        [TestCase(null, 0)]
        public void parses_volume_count_from_mangaupdates_status(string status, int expected)
        {
            MangaUpdatesService.ParseVolumeCount(status).Should().Be(expected);
        }

        [TestCase("FINISHED", false, AuthorStatusType.Ended)]
        [TestCase("FINISHED", true, AuthorStatusType.Ended)]
        [TestCase("RELEASING", true, AuthorStatusType.Continuing)]   // AniList "still releasing" wins over MU "completed"
        [TestCase("RELEASING", false, AuthorStatusType.Continuing)]
        [TestCase("HIATUS", false, AuthorStatusType.Continuing)]
        [TestCase(null, true, AuthorStatusType.Ended)]               // AniList absent -> MangaUpdates completed
        [TestCase(null, false, AuthorStatusType.Continuing)]
        [TestCase("", true, AuthorStatusType.Ended)]
        public void determines_status_anilist_first_then_mangaupdates_biased_to_continuing(string aniStatus, bool muCompleted, AuthorStatusType expected)
        {
            MangaSeriesMetadataProvider.DetermineStatus(aniStatus, muCompleted).Should().Be(expected);
        }
    }
}
