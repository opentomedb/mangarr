using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // Copy-in hold (2026-09-16, D1): a light-novel entry is held from the moment it is added; a
    // manga entry never is.
    [TestFixture]
    public class CopyInHoldFixture : CoreTest<AuthorService>
    {
        private Author NewAuthor(string foreignAuthorId)
        {
            var author = new Author
            {
                Id = 7,
                Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = foreignAuthorId }
            };

            Mocker.GetMock<IAuthorRepository>().Setup(s => s.Get(7)).Returns(author);

            return author;
        }

        [Test]
        public void adding_a_light_novel_sets_the_hold()
        {
            var author = NewAuthor("local-overlord~ln");

            Subject.AddAuthor(author, false);

            author.CopyInPending.Should().BeTrue();
            Mocker.GetMock<IAuthorRepository>().Verify(v => v.Insert(It.Is<Author>(a => a.CopyInPending)), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.IsAny<AuthorAddedEvent>()), Times.Once());
        }

        [Test]
        public void adding_a_manga_never_sets_the_hold()
        {
            var author = NewAuthor("local-overlord");

            Subject.AddAuthor(author, false);

            author.CopyInPending.Should().BeFalse();
        }

        [Test]
        public void bulk_add_sets_the_hold_per_library()
        {
            var ln = NewAuthor("local-overlord~ln");
            var manga = new Author { Id = 8, Metadata = new AuthorMetadata { Name = "Dandadan", ForeignAuthorId = "local-dandadan" } };

            Subject.AddAuthors(new List<Author> { ln, manga }, false);

            ln.CopyInPending.Should().BeTrue();
            manga.CopyInPending.Should().BeFalse();
        }

        [Test]
        public void clear_writes_only_that_column()
        {
            var author = NewAuthor("local-overlord~ln");
            author.CopyInPending = true;

            Subject.ClearCopyInPending(author);

            author.CopyInPending.Should().BeFalse();
            // params Expression<Func<Author, object>>[] -- match the array, not the lambda (Moq compares expression trees
            // by reference for params arrays); the property name is asserted through the compiled selector.
            Mocker.GetMock<IAuthorRepository>().Verify(v => v.SetFields(author, It.Is<System.Linq.Expressions.Expression<Func<Author, object>>[]>(p => p.Length == 1 && p[0].ToString().Contains("CopyInPending"))), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.IsAny<AuthorEditedEvent>()), Times.Never());

            // The page's SignalR copy flips on the same broadcast a refresh uses.
            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.Is<AuthorUpdatedEvent>(e => e.Author == author && !e.Author.CopyInPending)), Times.Once());
        }

        // Copy-in hold (final review #1): the hold is server-owned. A full-row update from a stale
        // object (the background refresh, a client PUT that never saw the clear) keeps the stored value.
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void update_keeps_the_stored_hold(bool stored, bool incoming)
        {
            var storedAuthor = NewAuthor("local-overlord~ln");
            storedAuthor.CopyInPending = stored;

            var incomingAuthor = new Author
            {
                Id = 7,
                Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord~ln" },
                CopyInPending = incoming
            };

            Mocker.GetMock<IAuthorRepository>().Setup(s => s.Update(It.IsAny<Author>())).Returns<Author>(a => a);

            Subject.UpdateAuthor(incomingAuthor);

            incomingAuthor.CopyInPending.Should().Be(stored);
            Mocker.GetMock<IAuthorRepository>().Verify(v => v.Update(It.Is<Author>(a => a.CopyInPending == stored)), Times.Once());
        }
    }
}
