using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.NewznabTests
{
    public class NewznabRequestGeneratorFixture : CoreTest<NewznabRequestGenerator>
    {
        private BookSearchCriteria _singleBookSearchCriteria;
        private NewznabCapabilities _capabilities;

        [SetUp]
        public void SetUp()
        {
            Subject.Settings = new NewznabSettings()
            {
                BaseUrl = "http://127.0.0.1:1234/",
                Categories = new[] { 1, 2 },
                ApiKey = "abcd",
            };

            _singleBookSearchCriteria = new BookSearchCriteria
            {
                Author = new Books.Author { Name = "Alien Ant Farm" },
                BookTitle = "TruANT"
            };

            _capabilities = new NewznabCapabilities();

            Mocker.GetMock<INewznabCapabilitiesProvider>()
                .Setup(v => v.GetCapabilities(It.IsAny<NewznabSettings>()))
                .Returns(_capabilities);
        }

        [Test]
        public void should_use_all_categories_for_feed()
        {
            var results = Subject.GetRecentRequests();

            results.GetAllTiers().Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.Query.Should().Contain("&cat=1,2&");
        }

        [Test]
        [Ignore("Disabled since no usenet indexers seem to support it")]
        public void should_search_by_author_and_book_if_supported()
        {
            _capabilities.SupportedBookSearchParameters = new[] { "q", "author", "title" };

            var results = Subject.GetSearchRequests(_singleBookSearchCriteria);
            results.GetTier(0).Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.Query.Should().Contain("author=Alien%20Ant%20Farm");
            page.Url.Query.Should().Contain("title=TruANT");
        }

        [Test]
        [Ignore("TODO: add raw search support")]
        public void should_encode_raw_title()
        {
            _capabilities.SupportedBookSearchParameters = new[] { "q", "author", "title" };

            // _capabilities.BookTextSearchEngine = "raw";
            _singleBookSearchCriteria.BookTitle = "Daisy Jones & The Six";

            var results = Subject.GetSearchRequests(_singleBookSearchCriteria);
            results.Tiers.Should().Be(1);

            var pageTier = results.GetTier(0).First().First();

            pageTier.Url.Query.Should().Contain("q=Daisy%20Jones%20%26%20The%20Six");
            pageTier.Url.Query.Should().NotContain(" & ");
            pageTier.Url.Query.Should().Contain("%26");
        }

        [Test]
        public void manga_search_should_add_recall_tier_with_bare_number_and_alias_variants()
        {
            var author = new Books.Author { Name = "Kaiju No. 8" };
            author.Metadata.Value.Aliases = new System.Collections.Generic.List<string> { "Kaijuu 8-gou" };

            var mangaCriteria = new BookSearchCriteria
            {
                Author = author,
                BookTitle = "Kaiju No. 8 Vol. 5",
                VolumeNumber = 5
            };

            var results = Subject.GetSearchRequests(mangaCriteria);

            // Beta readiness (2026-09-28, S1): the fork's tiers -- the primary q tier + the recall tier. A manga
            // search (VolumeNumber > 0) skips the upstream author-augmented tier (NewznabRequestGenerator).
            results.Tiers.Should().Be(2);

            var recallTier = results.GetTier(1).ToList();
            recallTier.Should().HaveCount(2);

            // bare padded number, no v token
            var bareQuery = recallTier[0].First().Url.Query;
            bareQuery.Should().Contain("05");
            bareQuery.Should().NotContain("v05");

            // alias variant with the v token
            var aliasQuery = recallTier[1].First().Url.Query;
            aliasQuery.Should().Contain("Kaijuu");
            aliasQuery.Should().Contain("v05");
        }

        // Light-novel audio (2026-09-18, D4/D7): the subtitle tier is gated on an Audio search, so a
        // manga search's tiers are byte-identical -- BookQuery + BookQueryAlt first, recall second.
        [Test]
        public void manga_search_tiers_are_unchanged_by_the_subtitle_tier()
        {
            var author = new Books.Author { Name = "Kaiju No. 8" };
            author.Metadata.Value.Aliases = new System.Collections.Generic.List<string> { "Kaijuu 8-gou" };

            var mangaCriteria = new BookSearchCriteria
            {
                Author = author,
                BookTitle = "Kaiju No. 8 Vol. 5",
                VolumeNumber = 5,
                MediaType = MediaType.Archive
            };

            var results = Subject.GetSearchRequests(mangaCriteria);

            results.Tiers.Should().Be(2);

            var primaryTier = results.GetTier(0).ToList();
            primaryTier.Should().HaveCount(2);
            primaryTier[0].First().Url.Query.Should().EndWith("&q=Kaiju%20No%208%20v05");
            primaryTier[1].First().Url.Query.Should().EndWith("&q=Kaiju%20No%208%20v5");

            var recallTier = results.GetTier(1).ToList();
            recallTier.Should().HaveCount(2);
            recallTier[0].First().Url.Query.Should().EndWith("&q=Kaiju%20No%208%2005");
            recallTier[1].First().Url.Query.Should().EndWith("&q=Kaijuu%208%20gou%20v05");
        }

        // Light-novel audio (2026-09-18, D4): the subtitle tier comes first and demotes the v<NN>
        // tier to second; recall stays last. Tiers are fallbacks, so a subtitle hit ends the search.
        // 2026-09-21: Nyaa files audiobooks under Books; an indexer with no audio category used to get
        // no request at all for a light-novel audio search.
        [Test]
        public void light_novel_audio_search_on_an_indexer_without_an_audio_category_asks_its_book_categories()
        {
            Subject.Settings.Categories = new[] { 7000, 117084 };

            var criteria = new BookSearchCriteria
            {
                Author = new Books.Author { Metadata = new Books.AuthorMetadata { Name = "Rascal Does Not Dream", ForeignAuthorId = "local-rascal-does-not-dream~ln" } },
                BookTitle = "Rascal Does Not Dream Vol. 16",
                VolumeNumber = 16,
                MediaType = MediaType.Audio
            };

            var results = Subject.GetSearchRequests(criteria);

            results.GetAllTiers().SelectMany(t => t).Should().NotBeEmpty();
            results.GetAllTiers().SelectMany(t => t).First().Url.Query.Should().Contain("&cat=7000,117084&");
        }

        [Test]
        public void a_manga_search_on_an_indexer_with_only_audio_categories_still_asks_nothing()
        {
            Subject.Settings.Categories = new[] { 3030 };

            var criteria = new BookSearchCriteria
            {
                Author = new Books.Author { Metadata = new Books.AuthorMetadata { Name = "Dandadan", ForeignAuthorId = "local-dandadan" } },
                BookTitle = "Dandadan Vol. 5",
                VolumeNumber = 5,
                MediaType = MediaType.Archive
            };

            Subject.GetSearchRequests(criteria).GetAllTiers().SelectMany(t => t).Should().BeEmpty();
        }

        [Test]
        public void light_novel_audio_search_puts_the_subtitle_tier_before_the_volume_tier()
        {
            Subject.Settings.Categories = new[] { 7030, 3030 };

            var criteria = new BookSearchCriteria
            {
                Author = new Books.Author { Name = "Sword Art Online" },
                BookTitle = "Sword Art Online Vol. 21",
                VolumeNumber = 21,
                MediaType = MediaType.Audio,
                Subtitle = "Unital Ring I",
                AudiobookTitle = "Sword Art Online 21"
            };

            var results = Subject.GetSearchRequests(criteria);

            results.Tiers.Should().Be(3);

            var subtitleTier = results.GetTier(0).ToList();
            subtitleTier.Should().HaveCount(1);
            subtitleTier[0].First().Url.Query.Should().Contain("&cat=3030&");
            subtitleTier[0].First().Url.Query.Should().EndWith("&q=Sword%20Art%20Online%2021%20Unital%20Ring%20I");

            var volumeTier = results.GetTier(1).ToList();
            volumeTier.Should().HaveCount(1);
            volumeTier[0].First().Url.Query.Should().EndWith("&q=Sword%20Art%20Online%20v21");

            var recallTier = results.GetTier(2).ToList();
            recallTier.Should().HaveCount(1);
            recallTier[0].First().Url.Query.Should().EndWith("&q=Sword%20Art%20Online%2021");
        }

        [Test]
        public void light_novel_audio_search_without_a_subtitle_keeps_the_volume_tier_first()
        {
            Subject.Settings.Categories = new[] { 7030, 3030 };

            var criteria = new BookSearchCriteria
            {
                Author = new Books.Author { Name = "Overlord" },
                BookTitle = "Overlord Vol. 5",
                VolumeNumber = 5,
                MediaType = MediaType.Audio
            };

            var results = Subject.GetSearchRequests(criteria);

            results.Tiers.Should().Be(2);
            results.GetTier(0).First().First().Url.Query.Should().EndWith("&q=Overlord%20v05");
            results.GetTier(1).First().First().Url.Query.Should().EndWith("&q=Overlord%2005");
        }

        [Test]
        public void non_manga_search_should_not_add_recall_tier()
        {
            var results = Subject.GetSearchRequests(_singleBookSearchCriteria);

            results.Tiers.Should().Be(2);
        }

        [Test]
        public void should_use_clean_title_and_encode()
        {
            _capabilities.SupportedBookSearchParameters = new[] { "q", "author", "title" };

            // _capabilities.BookTextSearchEngine = "sphinx";
            _singleBookSearchCriteria.BookTitle = "Daisy Jones & The Six";

            var results = Subject.GetSearchRequests(_singleBookSearchCriteria);
            results.Tiers.Should().Be(2);

            var pageTier = results.GetTier(0).First().First();

            pageTier.Url.Query.Should().Contain("q=Daisy%20Jones%20The%20Six");
            pageTier.Url.Query.Should().NotContain("and");
            pageTier.Url.Query.Should().NotContain(" & ");
            pageTier.Url.Query.Should().NotContain("%26");
        }

        // Light novels (2026-09): categories follow the searched edition class (D10).

        [Test]
        public void should_search_only_book_categories_for_an_archive_or_ebook_search()
        {
            Subject.Settings.Categories = new[] { 7000, 7020, 7030, 3030 };

            _singleBookSearchCriteria.MediaType = MediaType.Archive;
            Subject.GetSearchRequests(_singleBookSearchCriteria).GetAllTiers().First().First().Url.Query.Should().Contain("&cat=7000,7020,7030&");

            _singleBookSearchCriteria.MediaType = MediaType.Ebook;
            Subject.GetSearchRequests(_singleBookSearchCriteria).GetAllTiers().First().First().Url.Query.Should().Contain("&cat=7000,7020,7030&");
        }

        // The deploy expectation (D10, C7's pre-deploy category check): once 3030 is synced next to
        // 7030 on a live indexer, a manga (Archive) search sends the book category only -- the audio
        // category is deliberately excluded, never appended.
        [Test]
        public void manga_search_on_an_indexer_with_7030_and_3030_sends_7030_only()
        {
            Subject.Settings.Categories = new[] { 7030, 3030 };
            _singleBookSearchCriteria.MediaType = MediaType.Archive;

            var page = Subject.GetSearchRequests(_singleBookSearchCriteria).GetAllTiers().First().First();

            page.Url.Query.Should().Contain("&cat=7030&");
            page.Url.Query.Should().NotContain("3030");
        }

        [Test]
        public void should_search_only_audio_categories_for_an_audio_search()
        {
            Subject.Settings.Categories = new[] { 7000, 7020, 7030, 3030 };
            _singleBookSearchCriteria.MediaType = MediaType.Audio;

            var page = Subject.GetSearchRequests(_singleBookSearchCriteria).GetAllTiers().First().First();

            page.Url.Query.Should().Contain("&cat=3030&");
        }

        [Test]
        public void author_search_splits_the_same_way()
        {
            Subject.Settings.Categories = new[] { 7000, 7020, 7030, 3030 };

            var criteria = new AuthorSearchCriteria { Author = new Books.Author { Name = "Overlord" }, MediaType = MediaType.Audio };

            Subject.GetSearchRequests(criteria).GetAllTiers().First().First().Url.Query.Should().Contain("&cat=3030&");
        }

        // An indexer with no category of the searched class gets no request for that leg -- never
        // the other class's categories (Add wraps an empty page set, so assert on the requests).

        [Test]
        public void audio_search_sends_nothing_to_an_indexer_without_an_audio_category()
        {
            Subject.Settings.Categories = new[] { 7020, 7030 };
            _singleBookSearchCriteria.MediaType = MediaType.Audio;

            Subject.GetSearchRequests(_singleBookSearchCriteria).GetAllTiers().SelectMany(r => r).Should().BeEmpty();
        }

        [Test]
        public void ebook_search_sends_nothing_to_an_audio_only_indexer()
        {
            Subject.Settings.Categories = new[] { 3030 };
            _singleBookSearchCriteria.MediaType = MediaType.Ebook;

            Subject.GetSearchRequests(_singleBookSearchCriteria).GetAllTiers().SelectMany(r => r).Should().BeEmpty();
        }

        [Test]
        public void author_search_sends_nothing_to_an_indexer_without_an_audio_category()
        {
            Subject.Settings.Categories = new[] { 7020, 7030 };

            var criteria = new AuthorSearchCriteria { Author = new Books.Author { Name = "Overlord" }, MediaType = MediaType.Audio };

            Subject.GetSearchRequests(criteria).GetAllTiers().SelectMany(r => r).Should().BeEmpty();
        }
    }
}
