using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    // Final review I3, through the refresh plumbing: the editions RefreshChildren inserts for a NEW
    // light-novel volume carry the series' per-class choice; a new manga volume's one Archive
    // edition is inserted monitored with no edition query at all.
    [TestFixture]
    public class RefreshBookServiceNewVolumeFixture : CoreTest<RefreshBookService>
    {
        private Author _author;

        [SetUp]
        public void Setup()
        {
            GivenAuthor("local-overlord~ln");

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionsForRefresh(It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns(new List<Edition>());

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionsByAuthor(It.IsAny<int>()))
                .Returns(new List<Edition>());
        }

        private void GivenAuthor(string foreignAuthorId)
        {
            _author = new Author
            {
                Id = 3,
                Name = "Overlord",
                Monitored = true,
                Metadata = new AuthorMetadata { Id = 7, Name = "Overlord", ForeignAuthorId = foreignAuthorId }
            };

            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById(foreignAuthorId)).Returns(_author);
        }

        private void GivenSeriesEditions(params Edition[] editions)
        {
            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionsByAuthor(_author.Id))
                .Returns(editions.ToList());
        }

        private static Edition SeriesEdition(int id, MediaType mediaType, bool monitored)
        {
            return new Edition { Id = id, BookId = id / 10, MediaType = mediaType, Monitored = monitored };
        }

        // The minted shape (BookInfoProxy.MintEdition): every edition arrives Monitored = true.
        private Book NewVolume(int number, params MediaType[] mediaTypes)
        {
            var foreignBookId = $"{_author.Metadata.Value.ForeignAuthorId}-v{number}";

            var book = new Book
            {
                ForeignBookId = foreignBookId,
                Title = $"Overlord Vol. {number}",
                TitleSlug = foreignBookId,
                VolumeNumber = number,
                Monitored = true,
                Author = _author,
                AuthorMetadata = _author.Metadata.Value,
                AuthorMetadataId = _author.Metadata.Value.Id
            };

            var editions = mediaTypes.Select(t => new Edition
            {
                ForeignEditionId = foreignBookId + MediaTypes.EditionIdSuffix(t),
                TitleSlug = foreignBookId + MediaTypes.EditionIdSuffix(t),
                Title = book.Title,
                Format = MediaTypes.EditionFormat(t),
                MediaType = t,
                Monitored = true,
                ManualAdd = true,
                Book = book
            }).ToList();

            book.Editions = editions;

            return book;
        }

        // The local row the author refresh just inserted for the new volume (same ids, no editions yet).
        private static Book LocalRowFor(Book remote, int id)
        {
            return new Book
            {
                Id = id,
                ForeignBookId = remote.ForeignBookId,
                Title = remote.Title,
                TitleSlug = remote.TitleSlug,
                VolumeNumber = remote.VolumeNumber,
                Monitored = true,
                Author = remote.Author.Value,
                AuthorMetadata = remote.AuthorMetadata.Value,
                AuthorMetadataId = remote.AuthorMetadataId
            };
        }

        private List<Edition> Refresh(Book remote)
        {
            List<Edition> inserted = null;

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.InsertMany(It.IsAny<List<Edition>>()))
                .Callback<List<Edition>>(e => inserted = e);

            Subject.RefreshBookInfo(LocalRowFor(remote, 55), new List<Book> { remote }, _author, false);

            return inserted;
        }

        [Test]
        public void a_new_light_novel_volume_of_a_series_with_no_monitored_audio_is_inserted_with_audio_unmonitored()
        {
            GivenSeriesEditions(
                SeriesEdition(11, MediaType.Ebook, true),
                SeriesEdition(12, MediaType.Audio, false),
                SeriesEdition(21, MediaType.Ebook, true),
                SeriesEdition(22, MediaType.Audio, false));

            var inserted = Refresh(NewVolume(3, MediaType.Ebook, MediaType.Audio));

            inserted.Should().HaveCount(2);
            inserted.Single(e => e.MediaType == MediaType.Ebook).Monitored.Should().BeTrue();
            inserted.Single(e => e.MediaType == MediaType.Audio).Monitored.Should().BeFalse();
        }

        [Test]
        public void a_new_light_novel_volume_of_a_series_with_a_monitored_audio_somewhere_is_inserted_with_audio_monitored()
        {
            GivenSeriesEditions(
                SeriesEdition(11, MediaType.Ebook, true),
                SeriesEdition(12, MediaType.Audio, false),
                SeriesEdition(21, MediaType.Ebook, true),
                SeriesEdition(22, MediaType.Audio, true));

            var inserted = Refresh(NewVolume(3, MediaType.Ebook, MediaType.Audio));

            inserted.Should().HaveCount(2);
            inserted.Should().OnlyContain(e => e.Monitored);
        }

        [Test]
        public void the_first_volume_of_a_new_light_novel_series_is_inserted_as_minted()
        {
            // nothing exists yet to seed from; AuthorScannedHandler applies the add-time choice after the scan
            var inserted = Refresh(NewVolume(1, MediaType.Ebook, MediaType.Audio));

            inserted.Should().HaveCount(2);
            inserted.Should().OnlyContain(e => e.Monitored);
        }

        [Test]
        public void a_new_manga_volume_is_inserted_monitored_without_reading_the_series_editions()
        {
            GivenAuthor("local-dandadan");

            var inserted = Refresh(NewVolume(3, MediaType.Archive));

            inserted.Should().ContainSingle(e => e.MediaType == MediaType.Archive && e.Monitored);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.GetEditionsByAuthor(It.IsAny<int>()), Times.Never());
        }
    }
}
