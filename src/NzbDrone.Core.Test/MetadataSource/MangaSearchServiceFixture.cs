using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class MangaSearchServiceFixture : CoreTest<MangaSearchService>
    {
        [SetUp]
        public void Setup()
        {
            // Keep the deadline short so the "slow indexer" test doesn't wait 20 real seconds.
            MangaSearchService.DiscoveryTimeout = TimeSpan.FromMilliseconds(600);
        }

        [TearDown]
        public void TearDown()
        {
            MangaSearchService.DiscoveryTimeout = TimeSpan.FromSeconds(20);
        }

        private Mock<IIndexer> Indexer(Func<IList<ReleaseInfo>> fetch)
        {
            var mock = new Mock<IIndexer>();
            mock.SetupGet(i => i.SupportsSearch).Returns(true);
            mock.Setup(i => i.Definition).Returns(new IndexerDefinition { Name = "test" });
            mock.Setup(i => i.Fetch(It.IsAny<AuthorSearchCriteria>()))
                .Returns(() => Task.Run(fetch));
            return mock;
        }

        private static IList<ReleaseInfo> Releases(params string[] titles)
        {
            return titles.Select(t => (ReleaseInfo)new ReleaseInfo { Title = t }).ToList();
        }

        private void GivenIndexers(params Mock<IIndexer>[] indexers)
        {
            Mocker.GetMock<IIndexerFactory>()
                  .Setup(f => f.AutomaticSearchEnabled(It.IsAny<bool>()))
                  .Returns(indexers.Select(m => m.Object).ToList());
        }

        [Test]
        public void should_return_highest_volume_across_indexers()
        {
            GivenIndexers(
                Indexer(() => Releases("Chainsaw Man v10 (2023)")),
                Indexer(() => Releases("Chainsaw Man Vol. 16", "Chainsaw Man v13")));

            Subject.FindHighestVolume("Chainsaw Man").Should().Be(16);
        }

        [Test]
        public void should_not_block_on_a_slow_indexer_and_still_use_the_fast_ones()
        {
            GivenIndexers(
                Indexer(() => Releases("Chainsaw Man Vol. 16")),
                Indexer(() =>
                {
                    Thread.Sleep(TimeSpan.FromSeconds(30)); // a hung/slow indexer
                    return Releases("Chainsaw Man Vol. 99");
                }));

            var sw = Stopwatch.StartNew();
            var result = Subject.FindHighestVolume("Chainsaw Man");
            sw.Stop();

            // Must not wait for the 30s straggler — bounded by the (600ms test) deadline.
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

            // Uses the fast indexer's answer; the slow one that missed the deadline is dropped.
            result.Should().Be(16);
        }

        [Test]
        public void should_swallow_a_failing_indexer()
        {
            GivenIndexers(
                Indexer(() => throw new InvalidOperationException("indexer down")),
                Indexer(() => Releases("Chainsaw Man Vol. 12")));

            Subject.FindHighestVolume("Chainsaw Man").Should().Be(12);

            // The failing indexer is swallowed and logged as a Warn by design.
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_return_zero_with_no_indexers()
        {
            Mocker.GetMock<IIndexerFactory>()
                  .Setup(f => f.AutomaticSearchEnabled(It.IsAny<bool>()))
                  .Returns(new List<IIndexer>());

            Subject.FindHighestVolume("Chainsaw Man").Should().Be(0);
        }
    }
}
