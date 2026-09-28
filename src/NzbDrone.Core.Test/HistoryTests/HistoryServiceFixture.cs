using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.Qualities;

namespace NzbDrone.Core.Test.HistoryTests
{
    public class HistoryServiceFixture : CoreTest<HistoryService>
    {
        private QualityProfile _profile;
        private QualityProfile _profileCustom;

        [SetUp]
        public void Setup()
        {
            _profile = new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = QualityFixture.GetDefaultQualities(),
            };

            _profileCustom = new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = QualityFixture.GetDefaultQualities(Quality.MP3),
            };
        }

        [Test]
        public void should_use_file_name_for_source_title_if_scene_name_is_null()
        {
            var author = Builder<Author>.CreateNew().Build();
            var trackFile = Builder<BookFile>.CreateNew()
                .With(f => f.SceneName = null)
                .With(f => f.Author = author)
                .Build();

            var localTrack = new LocalBook
            {
                Author = author,
                Book = new Book(),
                Path = @"C:\Test\Unsorted\Author.01.Hymn.mp3"
            };

            var downloadClientItem = new DownloadClientItem
            {
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = DownloadProtocol.Usenet,
                    Id = 1,
                    Name = "sab"
                },
                DownloadId = "abcd"
            };

            Subject.Handle(new TrackImportedEvent(localTrack, trackFile, new List<BookFile>(), true, downloadClientItem));

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EntityHistory>(h => h.SourceTitle == Path.GetFileNameWithoutExtension(localTrack.Path))));
        }

        [Test]
        public void grab_should_record_the_media_type_of_the_release()
        {
            var remoteBook = new RemoteBook
            {
                Author = new Author { Id = 1 },
                Books = new List<Book> { new Book { Id = 5, AuthorId = 1 } },
                MediaType = MediaType.Audio,
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.M4B) },
                Release = new ReleaseInfo { Title = "Overlord Vol. 5 [M4B]", PublishDate = DateTime.UtcNow }
            };

            Mocker.GetMock<IHistoryRepository>()
                .Setup(v => v.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<EntityHistory>());

            Subject.Handle(new BookGrabbedEvent(remoteBook) { DownloadId = "abcd" });

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EntityHistory>(h => h.Data[EntityHistory.MEDIA_TYPE] == "audio")), Times.Once());
        }

        [Test]
        public void import_should_record_the_media_type_of_the_edition()
        {
            var author = Builder<Author>.CreateNew().Build();
            var edition = new Edition { Id = 7, MediaType = MediaType.Ebook };
            var trackFile = Builder<BookFile>.CreateNew()
                .With(f => f.Author = author)
                .With(f => f.Edition = edition)
                .With(f => f.Path = @"C:\Test\Overlord\Overlord - Vol 005.epub")
                .Build();

            var localTrack = new LocalBook
            {
                Author = author,
                Book = new Book(),
                Edition = edition,
                Path = @"C:\Test\Unsorted\Overlord Vol. 5.epub"
            };

            Subject.Handle(new TrackImportedEvent(localTrack, trackFile, new List<BookFile>(), true, new DownloadClientItem { DownloadId = "abcd", DownloadClientInfo = new DownloadClientItemClientInfo { Protocol = DownloadProtocol.Usenet, Id = 1, Name = "sab" } }));

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EntityHistory>(h => h.Data[EntityHistory.MEDIA_TYPE] == "ebook")), Times.Once());
        }

        [Test]
        public void most_recent_for_book_should_filter_by_media_type_and_fall_back_to_the_quality_class()
        {
            var rows = new List<EntityHistory>
            {
                new EntityHistory { BookId = 5, Date = DateTime.UtcNow, Quality = new QualityModel(Quality.M4B), EventType = EntityHistoryEventType.Grabbed, Data = new Dictionary<string, string> { { EntityHistory.MEDIA_TYPE, "audio" } } },
                new EntityHistory { BookId = 5, Date = DateTime.UtcNow.AddHours(-1), Quality = new QualityModel(Quality.EPUB), EventType = EntityHistoryEventType.Grabbed, Data = new Dictionary<string, string> { { EntityHistory.MEDIA_TYPE, "ebook" } } },
                new EntityHistory { BookId = 5, Date = DateTime.UtcNow.AddHours(-2), Quality = new QualityModel(Quality.CBZ), EventType = EntityHistoryEventType.Grabbed }
            };

            Mocker.GetMock<IHistoryRepository>()
                .Setup(v => v.GetByBook(5, null))
                .Returns(rows);

            Subject.MostRecentForBook(5, MediaType.Audio).Quality.Quality.Should().Be(Quality.M4B);
            Subject.MostRecentForBook(5, MediaType.Ebook).Quality.Quality.Should().Be(Quality.EPUB);

            // A row from before 2026-09 has no mediaType key: its quality class stands in.
            Subject.MostRecentForBook(5, MediaType.Archive).Quality.Quality.Should().Be(Quality.CBZ);
        }

        private static TrackedDownload TrackedDownloadOf(MediaType mediaType)
        {
            return new TrackedDownload
            {
                DownloadItem = new DownloadClientItem { Title = "Overlord Vol. 5", DownloadId = "abcd", DownloadClientInfo = new DownloadClientItemClientInfo { Name = "sab" } },
                RemoteBook = new RemoteBook
                {
                    Author = new Author { Id = 1 },
                    Books = new List<Book> { new Book { Id = 5, AuthorId = 1 } },
                    MediaType = mediaType,
                    ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.CBZ) }
                }
            };
        }

        private static BookFile FileOfEdition(MediaType mediaType)
        {
            return new BookFile
            {
                Id = 3,
                Path = @"C:\Test\Overlord\Overlord - Vol 005.m4b",
                Quality = new QualityModel(Quality.M4B),
                Author = new Author { Id = 1 },
                Edition = new Edition { Id = 7, BookId = 5, MediaType = mediaType }
            };
        }

        [Test]
        public void import_incomplete_should_record_the_media_type_of_the_tracked_download()
        {
            // The tracked download's title-parsed quality is the manga default (CBZ); its media
            // type, inherited from the grab row, is the class that counts.
            Subject.Handle(new BookImportIncompleteEvent(TrackedDownloadOf(MediaType.Audio)));

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EntityHistory>(h => h.Data[EntityHistory.MEDIA_TYPE] == "audio")), Times.Once());
        }

        [Test]
        public void download_failed_should_record_the_media_type_of_the_grab_row()
        {
            // A manual mark-as-failed carries no tracked download; the grab row's own data (handed
            // over by FailedDownloadService) names the class, ahead of the quality fallback.
            Subject.Handle(new DownloadFailedEvent
            {
                AuthorId = 1,
                BookIds = new List<int> { 5 },
                Quality = new QualityModel(Quality.M4B),
                SourceTitle = "Overlord Vol. 5",
                DownloadId = "abcd",
                Data = new Dictionary<string, string> { { EntityHistory.MEDIA_TYPE, "ebook" } }
            });

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EntityHistory>(h => h.Data[EntityHistory.MEDIA_TYPE] == "ebook")), Times.Once());
        }

        [Test]
        public void download_ignored_should_record_the_media_type_of_the_tracked_download()
        {
            Subject.Handle(new DownloadIgnoredEvent
            {
                AuthorId = 1,
                BookIds = new List<int> { 5 },
                Quality = new QualityModel(Quality.CBZ),
                SourceTitle = "Overlord Vol. 5",
                DownloadId = "abcd",
                DownloadClientInfo = new DownloadClientItemClientInfo { Name = "sab" },
                TrackedDownload = TrackedDownloadOf(MediaType.Ebook)
            });

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.InsertMany(It.Is<IList<EntityHistory>>(l => l.Count == 1 && l[0].Data[EntityHistory.MEDIA_TYPE] == "ebook")), Times.Once());
        }

        [Test]
        public void file_deleted_should_record_the_media_type_of_the_edition()
        {
            Subject.Handle(new BookFileDeletedEvent(FileOfEdition(MediaType.Audio), DeleteMediaFileReason.Manual));

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EntityHistory>(h => h.Data[EntityHistory.MEDIA_TYPE] == "audio")), Times.Once());
        }

        [Test]
        public void file_renamed_should_record_the_media_type_of_the_edition()
        {
            var file = FileOfEdition(MediaType.Audio);

            Subject.Handle(new BookFileRenamedEvent(file.Author.Value, file, @"C:\Test\Unsorted\Overlord Vol. 5.m4b"));

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EntityHistory>(h => h.Data[EntityHistory.MEDIA_TYPE] == "audio")), Times.Once());
        }

        [Test]
        public void file_retagged_should_record_the_media_type_of_the_edition()
        {
            var file = FileOfEdition(MediaType.Ebook);

            Subject.Handle(new BookFileRetaggedEvent(file.Author.Value, file, new Dictionary<string, Tuple<string, string>>(), false));

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EntityHistory>(h => h.Data[EntityHistory.MEDIA_TYPE] == "ebook")), Times.Once());
        }
    }
}
