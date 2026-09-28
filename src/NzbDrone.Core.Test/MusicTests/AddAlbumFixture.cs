using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AddBookFixture : CoreTest<AddBookService>
    {
        private Author _fakeAuthor;
        private Book _fakeBook;

        [SetUp]
        public void Setup()
        {
            _fakeAuthor = Builder<Author>
                .CreateNew()
                .With(s => s.Path = null)
                .With(s => s.Metadata = Builder<AuthorMetadata>.CreateNew().Build())
                .Build();
        }

        private void GivenValidBook(string bookId, string editionId)
        {
            _fakeBook = Builder<Book>
                .CreateNew()
                .With(x => x.Editions = Builder<Edition>
                      .CreateListOfSize(1)
                      .TheFirst(1)
                      .With(e => e.ForeignEditionId = editionId)
                      .With(e => e.Monitored = true)
                      .BuildList())
                .Build();

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(s => s.GetBookInfo(bookId))
                .Returns(Tuple.Create(_fakeAuthor.Metadata.Value.ForeignAuthorId,
                                      _fakeBook,
                                      new List<AuthorMetadata> { _fakeAuthor.Metadata.Value }));

            Mocker.GetMock<IAddAuthorService>()
                .Setup(s => s.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()))
                .Returns(_fakeAuthor);
        }

        private void GivenValidPath()
        {
            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null))
                  .Returns<Author, NamingConfig>((c, n) => c.Name);
        }

        private Book BookToAdd(string editionId, string bookId, string authorId)
        {
            return new Book
            {
                ForeignBookId = bookId,
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = editionId,
                        Monitored = true
                    }
                },
                AuthorMetadata = new AuthorMetadata
                {
                    ForeignAuthorId = authorId
                }
            };
        }

        [Test]
        public void should_be_able_to_add_a_book_without_passing_in_name()
        {
            var newBook = BookToAdd("edition", "book", "author");

            GivenValidBook("book", "edition");
            GivenValidPath();

            var book = Subject.AddBook(newBook);

            book.Title.Should().Be(_fakeBook.Title);
        }

        [Test]
        public void should_throw_if_book_cannot_be_found()
        {
            var newBook = BookToAdd("edition", "book", "author");

            Mocker.GetMock<IProvideBookInfo>()
                  .Setup(s => s.GetBookInfo("book"))
                  .Throws(new BookNotFoundException("edition"));

            Assert.Throws<ValidationException>(() => Subject.AddBook(newBook));

            ExceptionVerification.ExpectedErrors(1);
        }

        // Preferred Edition (2026-09-24, M5 fix round 1): a series whose bound edition the catalogue lost
        // is a validation message on POST /book, never a server error.
        [Test]
        public void an_unavailable_edition_is_a_validation_message()
        {
            var newBook = BookToAdd("edition", "book", "author");

            Mocker.GetMock<IProvideBookInfo>()
                  .Setup(s => s.GetBookInfo("book"))
                  .Throws(new EditionUnavailableException("Attack on Titan", "fr", "rl_fr"));

            var ex = Assert.Throws<ValidationException>(() => Subject.AddBook(newBook));

            ex.Errors.Should().Contain(e => e.ErrorMessage == "No French edition of this series in the catalogue");
        }

        [Test]
        public void should_keep_every_monitored_edition_of_a_light_novel_volume()
        {
            var newBook = BookToAdd("local-x~ln-v1-ed", "local-x~ln-v1", "local-x~ln");
            newBook.Editions.Value[0].MediaType = MediaType.Ebook;
            newBook.Editions.Value.Add(new Edition { ForeignEditionId = "local-x~ln-v1-audio-ed", MediaType = MediaType.Audio, Monitored = true });

            GivenValidBook("local-x~ln-v1", "local-x~ln-v1-ed");
            _fakeBook.Editions.Value[0].MediaType = MediaType.Ebook;
            _fakeBook.Editions.Value.Add(new Edition { ForeignEditionId = "local-x~ln-v1-audio-ed", MediaType = MediaType.Audio });
            GivenValidPath();

            var book = Subject.AddBook(newBook);

            book.Editions.Value.Should().HaveCount(2);
            book.Editions.Value.Should().OnlyContain(e => e.Monitored && e.ManualAdd);
        }
    }
}
