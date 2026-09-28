using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class CopyInPendingSpecificationFixture : CoreTest<CopyInPendingSpecification>
    {
        private RemoteBook _remoteBook;

        [SetUp]
        public void Setup()
        {
            _remoteBook = new RemoteBook
            {
                Author = new Author { Id = 3, Name = "Overlord" },
                Release = new ReleaseInfo { Title = "Overlord Vol. 5 [EPUB]" },
                Books = new List<Book> { new Book { Id = 5 } }
            };
        }

        [Test]
        public void accepts_when_not_pending()
        {
            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void rejects_when_pending()
        {
            _remoteBook.Author.CopyInPending = true;

            var decision = Subject.IsSatisfiedBy(_remoteBook, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Copy-in pending for this series");
        }

        [Test]
        public void accepts_when_author_is_null()
        {
            _remoteBook.Author = null;

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }
    }
}
