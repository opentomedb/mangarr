using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.PreferredEdition
{
    // Preferred Edition (2026-09-24): the exact request list an English manga volume search sends --
    // tier count, queries per tier and their text (the private-tracker query budget: an edition search must match the
    // counts, and an English one must match this byte for byte).
    [TestFixture]
    public class EnEditionPinningNewznabFixture : CoreTest<NewznabRequestGenerator>
    {
        [SetUp]
        public void SetUp()
        {
            Subject.Settings = new NewznabSettings
            {
                BaseUrl = "http://127.0.0.1:1234/",
                Categories = new[] { 7030 },
                ApiKey = "abcd"
            };

            Mocker.GetMock<INewznabCapabilitiesProvider>()
                .Setup(v => v.GetCapabilities(It.IsAny<NewznabSettings>()))
                .Returns(new NewznabCapabilities());
        }

        private static List<List<string>> Queries(IndexerPageableRequestChain chain)
        {
            return Enumerable.Range(0, chain.Tiers)
                .Select(t => chain.GetTier(t).Select(r => r.First().Url.Query).ToList())
                .ToList();
        }

        [Test]
        public void manga_volume_search_requests_are_pinned()
        {
            var author = new Author { Name = "Kaiju No. 8" };
            author.Metadata.Value.Aliases = new List<string> { "Kaijuu 8-gou", "Monster #8" };

            var requests = Subject.GetSearchRequests(new BookSearchCriteria { Author = author, BookTitle = "Kaiju No. 8 Vol. 5", VolumeNumber = 5 });

            EnGolden.Pin("newznab", Queries(requests));
        }
    }
}
