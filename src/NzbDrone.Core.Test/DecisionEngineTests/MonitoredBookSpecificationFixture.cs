using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications.RssSync;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]

    public class MonitoredBookSpecificationFixture : CoreTest<MonitoredBookSpecification>
    {
        private MonitoredBookSpecification _monitoredBookSpecification;

        private RemoteBook _parseResultMulti;
        private RemoteBook _parseResultSingle;
        private Author _fakeAuthor;
        private Book _firstBook;
        private Book _secondBook;

        [SetUp]
        public void Setup()
        {
            _monitoredBookSpecification = Mocker.Resolve<MonitoredBookSpecification>();

            _fakeAuthor = Builder<Author>.CreateNew()
                .With(c => c.Monitored = true)
                .Build();

            _firstBook = new Book { Id = 1, Monitored = true };
            _firstBook.WithEdition(MediaType.Archive);
            _secondBook = new Book { Id = 2, Monitored = true };
            _secondBook.WithEdition(MediaType.Archive);

            var singleBookList = new List<Book> { _firstBook };
            var doubleBookList = new List<Book> { _firstBook, _secondBook };

            _parseResultMulti = new RemoteBook
            {
                Author = _fakeAuthor,
                Books = doubleBookList
            };

            _parseResultSingle = new RemoteBook
            {
                Author = _fakeAuthor,
                Books = singleBookList
            };
        }

        private void WithFirstBookUnmonitored()
        {
            _firstBook.Monitored = false;
        }

        private void WithSecondBookUnmonitored()
        {
            _secondBook.Monitored = false;
        }

        [Test]
        public void setup_should_return_monitored_book_should_return_true()
        {
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, null).Accepted.Should().BeTrue();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultMulti, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void not_monitored_author_should_be_skipped()
        {
            _fakeAuthor.Monitored = false;
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultMulti, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void only_book_not_monitored_should_return_false()
        {
            WithFirstBookUnmonitored();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void both_books_not_monitored_should_return_false()
        {
            WithFirstBookUnmonitored();
            WithSecondBookUnmonitored();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultMulti, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void only_first_book_not_monitored_should_return_false()
        {
            WithFirstBookUnmonitored();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultMulti, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void only_second_book_not_monitored_should_return_false()
        {
            WithSecondBookUnmonitored();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultMulti, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_true_for_single_book_search()
        {
            _fakeAuthor.Monitored = false;
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, new BookSearchCriteria()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_book_is_not_monitored_and_monitoredEpisodesOnly_flag_is_false()
        {
            WithFirstBookUnmonitored();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, new BookSearchCriteria { MonitoredBooksOnly = false }).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_book_is_not_monitored_and_monitoredEpisodesOnly_flag_is_true()
        {
            WithFirstBookUnmonitored();
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, new BookSearchCriteria { MonitoredBooksOnly = true }).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_all_books_are_not_monitored_for_discography_pack_release()
        {
            WithSecondBookUnmonitored();
            _parseResultMulti.ParsedBookInfo = new ParsedBookInfo()
            {
                Discography = true
            };

            _monitoredBookSpecification.IsSatisfiedBy(_parseResultMulti, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void unmonitored_audio_edition_should_reject_an_audio_release_but_not_an_ebook_one()
        {
            _fakeAuthor.AudioAvailable = true;
            var volume = new Book { Id = 3, Monitored = true };
            volume.WithEdition(MediaType.Ebook);
            volume.WithEdition(MediaType.Audio, monitored: false);

            _parseResultSingle.Books = new List<Book> { volume };

            _parseResultSingle.MediaType = MediaType.Audio;
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, null).Accepted.Should().BeFalse();

            _parseResultSingle.MediaType = MediaType.Ebook;
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void pending_audio_should_be_rejected_on_rss_and_accepted_once_audio_is_available()
        {
            var volume = new Book { Id = 3, Monitored = true };
            volume.WithEdition(MediaType.Ebook);
            volume.WithEdition(MediaType.Audio);

            _parseResultSingle.Books = new List<Book> { volume };
            _parseResultSingle.MediaType = MediaType.Audio;

            _fakeAuthor.AudioAvailable = false;
            var decision = _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, null);
            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Audiobook not available yet for this series");

            _fakeAuthor.AudioAvailable = true;
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void pending_audio_does_not_block_the_ebook_edition_or_a_search()
        {
            var volume = new Book { Id = 3, Monitored = true };
            volume.WithEdition(MediaType.Ebook);
            volume.WithEdition(MediaType.Audio);

            _parseResultSingle.Books = new List<Book> { volume };
            _fakeAuthor.AudioAvailable = false;

            _parseResultSingle.MediaType = MediaType.Ebook;
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, null).Accepted.Should().BeTrue();

            // Searches skip the monitored check entirely (MonitoredBooksOnly is never set), so the
            // weekly probe and interactive searches reach pending audio.
            _parseResultSingle.MediaType = MediaType.Audio;
            _monitoredBookSpecification.IsSatisfiedBy(_parseResultSingle, new AuthorSearchCriteria()).Accepted.Should().BeTrue();
        }
    }
}
