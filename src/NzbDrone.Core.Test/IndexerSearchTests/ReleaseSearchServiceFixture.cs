using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
        public class ReleaseSearchServiceFixture : CoreTest<ReleaseSearchService>
    {
        private Mock<IIndexer> _mockIndexer;
        private Author _author;
        private Book _firstBook;
        private MemoryTarget _log;

        [SetUp]
        public void SetUp()
        {
            _mockIndexer = Mocker.GetMock<IIndexer>();
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition { Id = 1 });
            _mockIndexer.SetupGet(s => s.SupportsSearch).Returns(true);

            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.AutomaticSearchEnabled(true))
                  .Returns(new List<IIndexer> { _mockIndexer.Object });

            Mocker.GetMock<IMakeDownloadDecision>()
                .Setup(s => s.GetSearchDecision(It.IsAny<List<Parser.Model.ReleaseInfo>>(), It.IsAny<SearchCriteriaBase>()))
                .Returns(new List<DownloadDecision>());

            _author = Builder<Author>.CreateNew()
                .With(v => v.Monitored = true)
                .Build();

            _firstBook = Builder<Book>.CreateNew()
                .With(e => e.Author = _author)
                .Build();

            var edition = Builder<Edition>.CreateNew()
                .With(e => e.Book = _firstBook)
                .With(e => e.Monitored = true)
                .Build();

            _firstBook.Editions = new List<Edition> { edition };

            Mocker.GetMock<IAuthorService>()
                .Setup(v => v.GetAuthor(_author.Id))
                .Returns(_author);

            _log = new MemoryTarget("search") { Layout = "${level}|${message}" };
            LogManager.Configuration.AddTarget(_log.Name, _log);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Info, _log));
            LogManager.ReconfigExistingLoggers();
        }

        [TearDown]
        public void ReleaseLog()
        {
            foreach (var rule in LogManager.Configuration.LoggingRules.Where(r => r.Targets.Contains(_log)).ToList())
            {
                LogManager.Configuration.LoggingRules.Remove(rule);
            }

            LogManager.Configuration.RemoveTarget(_log.Name);
            LogManager.ReconfigExistingLoggers();
        }

        private List<SearchCriteriaBase> WatchForSearchCriteria()
        {
            var result = new List<SearchCriteriaBase>();

            _mockIndexer.Setup(v => v.Fetch(It.IsAny<BookSearchCriteria>()))
                .Callback<BookSearchCriteria>(s => result.Add(s))
                .Returns(Task.FromResult<IList<Parser.Model.ReleaseInfo>>(new List<Parser.Model.ReleaseInfo>()));

            return result;
        }

        [Test]
        public async Task Tags_IndexerTags_AuthorNoTags_IndexerNotIncluded()
        {
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition
            {
                Id = 1,
                Tags = new HashSet<int> { 3 }
            });

            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().ToList();

            criteria.Count.Should().Be(0);
        }

        [Test]
        public async Task Tags_IndexerNoTags_AuthorTags_IndexerIncluded()
        {
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition
            {
                Id = 1
            });

            _author = Builder<Author>.CreateNew()
                .With(v => v.Monitored = true)
                .With(v => v.Tags = new HashSet<int> { 3 })
                .Build();

            Mocker.GetMock<IAuthorService>()
                .Setup(v => v.GetAuthor(_author.Id))
                .Returns(_author);

            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().ToList();

            criteria.Count.Should().Be(1);
        }

        [Test]
        public async Task Tags_IndexerAndAuthorTagsMatch_IndexerIncluded()
        {
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition
            {
                Id = 1,
                Tags = new HashSet<int> { 1, 2, 3 }
            });

            _author = Builder<Author>.CreateNew()
                .With(v => v.Monitored = true)
                .With(v => v.Tags = new HashSet<int> { 3, 4, 5 })
                .Build();

            Mocker.GetMock<IAuthorService>()
                .Setup(v => v.GetAuthor(_author.Id))
                .Returns(_author);

            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().ToList();

            criteria.Count.Should().Be(1);
        }

        [Test]
        public async Task Tags_IndexerAndAuthorTagsMismatch_IndexerNotIncluded()
        {
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition
            {
                Id = 1,
                Tags = new HashSet<int> { 1, 2, 3 }
            });

            _author = Builder<Author>.CreateNew()
                .With(v => v.Monitored = true)
                .With(v => v.Tags = new HashSet<int> { 4, 5, 6 })
                .Build();

            Mocker.GetMock<IAuthorService>()
                .Setup(v => v.GetAuthor(_author.Id))
                .Returns(_author);

            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().ToList();

            criteria.Count.Should().Be(0);
        }

        // Light novels (2026-09): one search per monitored edition class.

        private (Edition Ebook, Edition Audio) GivenLightNovelVolume(bool audioMonitored = true)
        {
            _author.ForeignAuthorId = "local-overlord~ln";

            var ebook = Builder<Edition>.CreateNew()
                .With(e => e.Id = 51)
                .With(e => e.Book = _firstBook)
                .With(e => e.Title = "Overlord Vol. 5")
                .With(e => e.MediaType = MediaType.Ebook)
                .With(e => e.Monitored = true)
                .With(e => e.CoveredByVolume = null)
                .Build();

            var audio = Builder<Edition>.CreateNew()
                .With(e => e.Id = 52)
                .With(e => e.Book = _firstBook)
                .With(e => e.Title = "Overlord Vol. 5")
                .With(e => e.MediaType = MediaType.Audio)
                .With(e => e.Monitored = audioMonitored)
                .With(e => e.CoveredByVolume = null)
                .Build();

            _firstBook.Editions = new List<Edition> { ebook, audio };

            return (ebook, audio);
        }

        private List<SearchCriteriaBase> WatchForAuthorSearchCriteria()
        {
            var result = new List<SearchCriteriaBase>();

            _mockIndexer.Setup(v => v.Fetch(It.IsAny<AuthorSearchCriteria>()))
                .Callback<AuthorSearchCriteria>(s => result.Add(s))
                .Returns(Task.FromResult<IList<Parser.Model.ReleaseInfo>>(new List<Parser.Model.ReleaseInfo>()));

            return result;
        }

        [Test]
        public async Task manga_volume_still_runs_one_archive_search()
        {
            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().ToList();

            criteria.Should().HaveCount(1);
            criteria[0].MediaType.Should().Be(MediaType.Archive);
            criteria[0].Edition.Should().BeSameAs(_firstBook.Editions.Value[0]);
        }

        [Test]
        public async Task book_search_runs_one_search_per_monitored_media_type()
        {
            var (ebook, audio) = GivenLightNovelVolume();
            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().ToList();

            criteria.Should().HaveCount(2);
            criteria.Select(c => c.MediaType).Should().BeEquivalentTo(new[] { MediaType.Ebook, MediaType.Audio });
            criteria.Single(c => c.MediaType == MediaType.Ebook).Edition.Should().BeSameAs(ebook);
            criteria.Single(c => c.MediaType == MediaType.Audio).Edition.Should().BeSameAs(audio);
            criteria.Should().OnlyContain(c => c.BookTitle == "Overlord Vol. 5");
        }

        [Test]
        public async Task book_search_for_one_media_type_searches_that_edition_only()
        {
            var (_, audio) = GivenLightNovelVolume();
            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, MediaType.Audio, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().ToList();

            criteria.Should().HaveCount(1);
            criteria[0].MediaType.Should().Be(MediaType.Audio);
            criteria[0].Edition.Should().BeSameAs(audio);
        }

        [Test]
        public async Task unmonitored_edition_is_not_searched()
        {
            GivenLightNovelVolume(audioMonitored: false);
            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            allCriteria.OfType<BookSearchCriteria>().Select(c => c.MediaType).Should().BeEquivalentTo(new[] { MediaType.Ebook });
        }

        [Test]
        public async Task author_search_runs_one_search_per_monitored_media_type_across_the_series()
        {
            GivenLightNovelVolume();
            _firstBook.Monitored = true;

            Mocker.GetMock<IBookService>()
                .Setup(v => v.GetBooksByAuthor(_author.Id))
                .Returns(new List<Book> { _firstBook });

            var allCriteria = WatchForAuthorSearchCriteria();

            await Subject.AuthorSearch(_author, false, true, false);

            var criteria = allCriteria.OfType<AuthorSearchCriteria>().ToList();

            criteria.Should().HaveCount(2);
            criteria.Select(c => c.MediaType).Should().BeEquivalentTo(new[] { MediaType.Ebook, MediaType.Audio });
            criteria.Should().OnlyContain(c => c.Books.Count == 1 && c.Edition == null);
        }

        // Copy-in hold (2026-09-16, D4a): a held entry is not searched automatically; a person at
        // the interactive table still gets a search.

        [Test]
        public async Task pending_author_non_interactive_search_dispatches_nothing()
        {
            _author.CopyInPending = true;
            var criteria = WatchForSearchCriteria();

            var decisions = await Subject.BookSearch(_firstBook, false, true, false);

            decisions.Should().BeEmpty();
            criteria.Should().BeEmpty();
        }

        [Test]
        public async Task pending_author_interactive_search_still_dispatches()
        {
            _author.CopyInPending = true;
            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.InteractiveSearchEnabled(true))
                  .Returns(new List<IIndexer> { _mockIndexer.Object });
            var criteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, true);

            criteria.Should().HaveCount(1);
        }

        [Test]
        public async Task pending_author_series_search_dispatches_nothing()
        {
            GivenLightNovelVolume();
            _firstBook.Monitored = true;
            _author.CopyInPending = true;

            Mocker.GetMock<IBookService>()
                .Setup(v => v.GetBooksByAuthor(_author.Id))
                .Returns(new List<Book> { _firstBook });

            var criteria = WatchForAuthorSearchCriteria();

            var decisions = await Subject.AuthorSearch(_author, false, true, false);

            decisions.Should().BeEmpty();
            criteria.Should().BeEmpty();
        }

        // Covered volumes (2026-09-17, D4): the audio search of a covered volume is skipped
        // automatically; a person at the interactive table still gets one; the EPUB is unaffected.

        [Test]
        public async Task covered_audio_non_interactive_search_dispatches_nothing()
        {
            var (_, audio) = GivenLightNovelVolume();
            audio.CoveredByVolume = 1;
            _firstBook.Title = "Overlord Vol. 5";
            var criteria = WatchForSearchCriteria();

            var decisions = await Subject.BookSearch(_firstBook, MediaType.Audio, false, true, false);

            decisions.Should().BeEmpty();
            criteria.Should().BeEmpty();
            _log.Logs.Should().Contain("Info|Search for \"Overlord Vol. 5\" audio skipped: covered by Vol. 1");
        }

        [Test]
        public async Task covered_audio_interactive_search_still_dispatches()
        {
            var (_, audio) = GivenLightNovelVolume();
            audio.CoveredByVolume = 1;
            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.InteractiveSearchEnabled(true))
                  .Returns(new List<IIndexer> { _mockIndexer.Object });
            var criteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, MediaType.Audio, false, true, true);

            criteria.Should().HaveCount(1);
            criteria.OfType<BookSearchCriteria>().Single().Edition.Should().BeSameAs(audio);
        }

        [Test]
        public async Task covered_volume_still_searches_its_ebook_edition()
        {
            var (ebook, audio) = GivenLightNovelVolume();
            audio.CoveredByVolume = 1;
            var criteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, false, true, false);

            criteria.OfType<BookSearchCriteria>().Select(c => c.MediaType).Should().BeEquivalentTo(new[] { MediaType.Ebook });
            criteria.OfType<BookSearchCriteria>().Single().Edition.Should().BeSameAs(ebook);
        }

        // Light-novel audio (2026-09-18, D4): the volume's subtitle and Audible title ride on an
        // Audio search's criteria only -- the EPUB search never carries them.

        [Test]
        public async Task audio_search_carries_the_subtitle_and_audiobook_title()
        {
            var (_, audio) = GivenLightNovelVolume();
            _firstBook.Subtitle = "Aincrad";
            audio.AudiobookTitle = "Sword Art Online 1: Aincrad";
            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, MediaType.Audio, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().Single();
            criteria.Subtitle.Should().Be("Aincrad");
            criteria.AudiobookTitle.Should().Be("Sword Art Online 1: Aincrad");
        }

        [Test]
        public async Task ebook_search_leaves_the_subtitle_and_audiobook_title_null()
        {
            var (_, audio) = GivenLightNovelVolume();
            _firstBook.Subtitle = "Aincrad";
            audio.AudiobookTitle = "Sword Art Online 1: Aincrad";
            var allCriteria = WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, MediaType.Ebook, false, true, false);

            var criteria = allCriteria.OfType<BookSearchCriteria>().Single();
            criteria.Subtitle.Should().BeNull();
            criteria.AudiobookTitle.Should().BeNull();
        }

        // Final review I1 (2026-09-20): Book.LastSearchTime is one stamp per volume, shared by the
        // missing search (fileless editions) and the cutoff search (below-cutoff ones). A search
        // asked not to stamp leaves it alone, so the scheduled cutoff run cannot burn the missing
        // search's 24h back-off for an edition it never searched.

        [Test]
        public async Task a_search_that_does_not_stamp_leaves_the_last_search_time_alone()
        {
            GivenLightNovelVolume();
            var stamped = new DateTime(2026, 1, 1);
            _firstBook.LastSearchTime = stamped;
            WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, MediaType.Audio, false, true, false, false);

            _firstBook.LastSearchTime.Should().Be(stamped);

            Mocker.GetMock<IBookService>()
                .Verify(v => v.UpdateLastSearchTime(It.IsAny<List<Book>>()), Times.Never());
        }

        [Test]
        public async Task a_search_that_stamps_updates_the_last_search_time_as_before()
        {
            GivenLightNovelVolume();
            var stamped = new DateTime(2026, 1, 1);
            _firstBook.LastSearchTime = stamped;
            WatchForSearchCriteria();

            await Subject.BookSearch(_firstBook, MediaType.Audio, false, true, false);

            _firstBook.LastSearchTime.Should().BeAfter(stamped);

            Mocker.GetMock<IBookService>()
                .Verify(v => v.UpdateLastSearchTime(It.Is<List<Book>>(b => b.Single() == _firstBook)), Times.Once());
        }
    }
}
