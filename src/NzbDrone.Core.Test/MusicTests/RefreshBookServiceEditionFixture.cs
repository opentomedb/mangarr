using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    // Preferred Edition (2026-09-24, ruling S10, M5 pre-review fix): a single-volume refresh of a series
    // whose bound edition the catalogue lost is a Warn, and the volume is left exactly as stored.
    [TestFixture]
    public class RefreshBookServiceEditionFixture : CoreTest<RefreshBookService>
    {
        private Book _book;

        [SetUp]
        public void Setup()
        {
            _book = new Book
            {
                Id = 11,
                ForeignBookId = "local-attack-on-titan-v5",
                Title = "L'Attaque des Titans Vol. 5",
                AuthorMetadataId = 7
            };

            Mocker.GetMock<IBookService>().Setup(s => s.GetBook(_book.Id)).Returns(_book);
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooks(It.IsAny<IEnumerable<int>>())).Returns(new List<Book> { _book });
        }

        private void GivenTheBoundLineVanished(bool fromBookInfo)
        {
            var unavailable = new EditionUnavailableException("Attack on Titan", "fr", "rl_fr");

            if (fromBookInfo)
            {
                Mocker.GetMock<IProvideBookInfo>().Setup(s => s.GetBookInfo(It.IsAny<string>())).Throws(unavailable);
            }
            else
            {
                Mocker.GetMock<IProvideBookInfo>()
                      .Setup(s => s.GetBookInfo(It.IsAny<string>()))
                      .Returns(System.Tuple.Create("local-attack-on-titan", new Book { ForeignBookId = _book.ForeignBookId }, new List<AuthorMetadata>()));
                Mocker.GetMock<IProvideAuthorInfo>().Setup(s => s.GetAuthorInfo(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>())).Throws(unavailable);
            }
        }

        private void VerifyLeftAlone()
        {
            Mocker.GetMock<IBookService>().Verify(s => s.UpdateMany(It.IsAny<List<Book>>()), Times.Never());
            Mocker.GetMock<IBookService>().Verify(s => s.DeleteMany(It.IsAny<List<Book>>()), Times.Never());
            Mocker.GetMock<IBookService>().Verify(s => s.UpdateBook(It.IsAny<Book>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void an_unavailable_edition_is_a_warn_and_leaves_the_volume_alone(bool fromBookInfo)
        {
            GivenTheBoundLineVanished(fromBookInfo);

            Subject.Execute(new RefreshBookCommand(_book.Id));

            VerifyLeftAlone();
        }

        [Test]
        public void a_bulk_volume_refresh_warns_too()
        {
            GivenTheBoundLineVanished(true);

            Subject.Execute(new BulkRefreshBookCommand(new List<int> { _book.Id }));

            VerifyLeftAlone();
        }
    }
}
