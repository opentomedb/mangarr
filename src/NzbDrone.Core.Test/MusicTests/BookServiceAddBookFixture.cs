using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class BookServiceAddBookFixture : CoreTest<BookService>
    {
        [Test]
        public void add_book_monitors_one_edition_per_media_type_of_a_light_novel_volume()
        {
            var ebook = new Edition { ForeignEditionId = "local-x~ln-v1-ed", MediaType = MediaType.Ebook, Monitored = true };
            var audio = new Edition { ForeignEditionId = "local-x~ln-v1-audio-ed", MediaType = MediaType.Audio, Monitored = true };
            var book = new Book { AuthorMetadataId = 1, ForeignBookId = "local-x~ln-v1", Editions = new List<Edition> { ebook, audio } };

            Subject.AddBook(book, false);

            Mocker.GetMock<IEditionService>().Verify(v => v.SetMonitored(ebook), Times.Once());
            Mocker.GetMock<IEditionService>().Verify(v => v.SetMonitored(audio), Times.Once());
        }

        [Test]
        public void add_book_of_a_manga_volume_monitors_its_one_edition_as_before()
        {
            var only = new Edition { ForeignEditionId = "local-m-v1-ed", MediaType = MediaType.Archive, Monitored = true };
            var book = new Book { AuthorMetadataId = 1, ForeignBookId = "local-m-v1", Editions = new List<Edition> { only } };

            Subject.AddBook(book, false);

            Mocker.GetMock<IEditionService>().Verify(v => v.SetMonitored(only), Times.Once());
            Mocker.GetMock<IEditionService>().Verify(v => v.SetMonitored(It.IsAny<Edition>()), Times.Once());
        }
    }
}
