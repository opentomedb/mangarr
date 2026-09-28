using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests.BookMonitoredServiceTests
{
    [TestFixture]
    public class SetBookMontitoredFixture : CoreTest<BookMonitoredService>
    {
        private Author _author;
        private List<Book> _books;

        [SetUp]
        public void Setup()
        {
            const int books = 4;

            _author = Builder<Author>.CreateNew()
                                     .Build();

            _books = Builder<Book>.CreateListOfSize(books)
                                        .All()
                                        .With(e => e.Monitored = true)
                                        .With(e => e.ReleaseDate = DateTime.UtcNow.AddDays(-7))

                                        //Future
                                        .TheFirst(1)
                                        .With(e => e.ReleaseDate = DateTime.UtcNow.AddDays(7))

                                        //Future/TBA
                                        .TheNext(1)
                                        .With(e => e.ReleaseDate = null)
                                        .Build()
                                        .ToList();

            Mocker.GetMock<IBookService>()
                  .Setup(s => s.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(_books);

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetAuthorBooksWithFiles(It.IsAny<Author>()))
                .Returns(new List<Book>());
        }

        [Test]
        public void should_be_able_to_monitor_author_without_changing_books()
        {
            Subject.SetBookMonitoredStatus(_author, null);

            Mocker.GetMock<IAuthorService>()
                  .Verify(v => v.UpdateAuthor(It.IsAny<Author>()), Times.Once());

            Mocker.GetMock<IBookService>()
                  .Verify(v => v.UpdateMany(It.IsAny<List<Book>>()), Times.Never());
        }

        [Test]
        public void should_be_able_to_monitor_books_when_passed_in_author()
        {
            var booksToMonitor = new List<string> { _books.First().ForeignBookId };

            Subject.SetBookMonitoredStatus(_author, new MonitoringOptions { Monitored = true, BooksToMonitor = booksToMonitor });

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateAuthor(It.IsAny<Author>()), Times.Once());

            VerifyMonitored(e => e.ForeignBookId == _books.First().ForeignBookId);
            VerifyNotMonitored(e => e.ForeignBookId != _books.First().ForeignBookId);
        }

        [Test]
        public void should_be_able_to_monitor_all_books()
        {
            Subject.SetBookMonitoredStatus(_author, new MonitoringOptions { Monitor = MonitorTypes.All });

            Mocker.GetMock<IBookService>()
                  .Verify(v => v.UpdateBook(It.Is<Book>(l => l.Monitored)), Times.Exactly(_books.Count));
        }

        [Test]
        public void should_be_able_to_monitor_new_books_only()
        {
            var monitoringOptions = new MonitoringOptions
            {
                Monitor = MonitorTypes.Future
            };

            Subject.SetBookMonitoredStatus(_author, monitoringOptions);

            VerifyMonitored(e => e.ReleaseDate.HasValue && e.ReleaseDate.Value.After(DateTime.UtcNow));
            VerifyMonitored(e => !e.ReleaseDate.HasValue);
            VerifyNotMonitored(e => e.ReleaseDate.HasValue && e.ReleaseDate.Value.Before(DateTime.UtcNow));
        }

        private void VerifyMonitored(Func<Book, bool> predicate)
        {
            Mocker.GetMock<IBookService>()
                .Verify(v => v.UpdateBook(It.Is<Book>(b => b.Monitored)), Times.AtLeast(_books.Where(predicate).Count()));
        }

        private void VerifyNotMonitored(Func<Book, bool> predicate)
        {
            Mocker.GetMock<IBookService>()
                .Verify(v => v.UpdateBook(It.Is<Book>(b => !b.Monitored)), Times.AtLeast(_books.Where(predicate).Count()));
        }

        private void GivenLightNovelEditions()
        {
            // A light-novel volume: an Ebook and an Audio edition, both minted monitored.
            foreach (var book in _books)
            {
                book.Editions = new List<Edition>
                {
                    new Edition { Id = (book.Id * 10) + 1, BookId = book.Id, MediaType = MediaType.Ebook, Monitored = true },
                    new Edition { Id = (book.Id * 10) + 2, BookId = book.Id, MediaType = MediaType.Audio, Monitored = true }
                };
            }
        }

        [Test]
        public void should_unmonitor_the_editions_of_formats_not_in_monitor_media_types()
        {
            GivenLightNovelEditions();

            Subject.SetBookMonitoredStatus(_author, new AddAuthorOptions { Monitor = MonitorTypes.All, MonitorMediaTypes = new List<string> { "ebook" } });

            Mocker.GetMock<IEditionService>()
                  .Verify(v => v.UpdateMany(It.Is<List<Edition>>(l => l.Count == _books.Count && l.All(e => e.MediaType == MediaType.Audio && !e.Monitored))), Times.Once());

            _books.SelectMany(b => b.Editions.Value).Where(e => e.MediaType == MediaType.Ebook).Should().OnlyContain(e => e.Monitored);
            _books.SelectMany(b => b.Editions.Value).Where(e => e.MediaType == MediaType.Audio).Should().OnlyContain(e => !e.Monitored);

            // the book flag is the Monitor option's business, not the media types'
            VerifyMonitored(e => true);
        }

        [Test]
        public void should_leave_every_edition_alone_when_monitor_media_types_is_not_sent()
        {
            GivenLightNovelEditions();

            Subject.SetBookMonitoredStatus(_author, new AddAuthorOptions { Monitor = MonitorTypes.All });

            Mocker.GetMock<IEditionService>()
                  .Verify(v => v.UpdateMany(It.IsAny<List<Edition>>()), Times.Never());
            _books.SelectMany(b => b.Editions.Value).Should().OnlyContain(e => e.Monitored);
        }

        [Test]
        public void should_leave_every_edition_alone_when_no_media_type_name_is_recognised()
        {
            GivenLightNovelEditions();

            // A typo (or a numeric string) must not read as "monitor nothing".
            Subject.SetBookMonitoredStatus(_author, new AddAuthorOptions { Monitor = MonitorTypes.All, MonitorMediaTypes = new List<string> { "ebooks", "99" } });

            Mocker.GetMock<IEditionService>()
                  .Verify(v => v.UpdateMany(It.IsAny<List<Edition>>()), Times.Never());
            _books.SelectMany(b => b.Editions.Value).Should().OnlyContain(e => e.Monitored);
            ExceptionVerification.ExpectedWarns(3);
        }

        [Test]
        public void should_leave_a_manga_volume_alone_when_monitor_media_types_is_empty()
        {
            _books.ForEach(b => b.Editions = new List<Edition> { new Edition { Id = b.Id, BookId = b.Id, MediaType = MediaType.Archive, Monitored = true } });

            Subject.SetBookMonitoredStatus(_author, new AddAuthorOptions { Monitor = MonitorTypes.All, MonitorMediaTypes = new List<string>() });

            Mocker.GetMock<IEditionService>()
                  .Verify(v => v.UpdateMany(It.IsAny<List<Edition>>()), Times.Never());
        }
    }
}
