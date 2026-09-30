using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // Line safety review fixes (2026-09-28, I2): the refresh a line switch queues searches none of the volumes it
    // adds; the next refresh after it searches as always.
    [TestFixture]
    public class BookAddedServiceFixture : CoreTest<BookAddedService>
    {
        private Author _author;
        private List<Book> _added;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(new CacheManager());

            _author = new Author { Id = 107, Monitored = true };
            _added = new List<Book> { new Book { Id = 7, Monitored = true, ReleaseDate = DateTime.UtcNow.AddDays(-30) } };

            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(107)).Returns(_added);
        }

        private void Refresh()
        {
            Subject.Handle(new BookInfoRefreshedEvent(_author, _added, new List<Book>(), new List<Book>()));
            Subject.Handle(new AuthorRefreshCompleteEvent(_author));
            Subject.SearchForRecentlyAdded(107);
        }

        private void VerifySearches(Times times)
        {
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<BookSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), times);
        }

        [Test]
        public void a_refresh_searches_its_new_released_volumes()
        {
            Refresh();

            VerifySearches(Times.Once());
        }

        [Test]
        public void the_switchs_refresh_searches_nothing_and_the_next_one_does()
        {
            Subject.SkipNextRefreshSearch(107);

            Refresh();
            VerifySearches(Times.Never());

            Refresh();
            VerifySearches(Times.Once());
        }

        // A switch refresh that adds nothing publishes no BookInfoRefreshedEvent: the flag ends with it anyway.
        [Test]
        public void the_flag_ends_with_a_refresh_that_added_nothing()
        {
            Subject.SkipNextRefreshSearch(107);
            Subject.Handle(new AuthorRefreshCompleteEvent(_author));

            Refresh();

            VerifySearches(Times.Once());
        }
    }
}
