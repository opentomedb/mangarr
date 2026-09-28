using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class AlreadyImportedSpecificationFixture : CoreTest<AlreadyImportedSpecification>
    {
        private const int FIRST_ALBUM_ID = 1;
        private const string TITLE = "Some.Author-Some.Book-2018-320kbps-CD-Readarr";

        private Author _author;
        private QualityModel _mp3;
        private QualityModel _flac;
        private RemoteBook _remoteBook;
        private List<EntityHistory> _history;
        private BookFile _firstFile;

        [SetUp]
        public void Setup()
        {
            var singleBookList = new List<Book>
                                    {
                                        new Book
                                        {
                                            Id = FIRST_ALBUM_ID,
                                            Title = "Some Book"
                                        }
                                    };

            singleBookList[0].WithEdition(MediaType.Archive, id: 11);

            _author = Builder<Author>.CreateNew()
                                     .Build();

            _firstFile = new BookFile { Quality = new QualityModel(Quality.FLAC, new Revision(version: 2)), DateAdded = DateTime.Now };

            _mp3 = new QualityModel(Quality.MP3, new Revision(version: 1));
            _flac = new QualityModel(Quality.FLAC, new Revision(version: 1));

            _remoteBook = new RemoteBook
            {
                Author = _author,
                ParsedBookInfo = new ParsedBookInfo { Quality = _mp3 },
                Books = singleBookList,
                Release = Builder<ReleaseInfo>.CreateNew()
                                              .Build()
            };

            _history = new List<EntityHistory>();

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnableCompletedDownloadHandling)
                  .Returns(true);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.GetByBook(It.IsAny<int>(), null))
                  .Returns(_history);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(c => c.GetFilesByEdition(It.IsAny<int>()))
                  .Returns(new List<BookFile> { _firstFile });
        }

        private void GivenCdhDisabled()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnableCompletedDownloadHandling)
                  .Returns(false);
        }

        private void GivenHistoryItem(string downloadId, string sourceTitle, QualityModel quality, EntityHistoryEventType eventType)
        {
            // The fixture's release is an Archive one (the default); its MP3/FLAC rows would read as
            // Audio through the quality fallback, so tag them with the release's class explicitly.
            _history.Add(new EntityHistory
            {
                DownloadId = downloadId,
                SourceTitle = sourceTitle,
                Quality = quality,
                Date = DateTime.UtcNow,
                EventType = eventType,
                Data = new Dictionary<string, string> { { EntityHistory.MEDIA_TYPE, "archive" } }
            });
        }

        [Test]
        public void should_be_accepted_if_CDH_is_disabled()
        {
            GivenCdhDisabled();

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_accepted_if_book_does_not_have_a_file()
        {
            Mocker.GetMock<IMediaFileService>()
                .Setup(c => c.GetFilesByEdition(It.IsAny<int>()))
                .Returns(new List<BookFile> { });

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_accepted_if_book_does_not_have_grabbed_event()
        {
            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_accepted_if_book_does_not_have_imported_event()
        {
            GivenHistoryItem(Guid.NewGuid().ToString().ToUpper(), TITLE, _mp3, EntityHistoryEventType.Grabbed);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_accepted_if_grabbed_and_imported_quality_is_the_same()
        {
            var downloadId = Guid.NewGuid().ToString().ToUpper();

            GivenHistoryItem(downloadId, TITLE, _mp3, EntityHistoryEventType.Grabbed);
            GivenHistoryItem(downloadId, TITLE, _mp3, EntityHistoryEventType.BookFileImported);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_rejected_if_grabbed_download_id_matches_release_torrent_hash()
        {
            var downloadId = Guid.NewGuid().ToString().ToUpper();

            GivenHistoryItem(downloadId, TITLE, _mp3, EntityHistoryEventType.Grabbed);
            GivenHistoryItem(downloadId, TITLE, _flac, EntityHistoryEventType.BookFileImported);

            _remoteBook.Release = Builder<TorrentInfo>.CreateNew()
                                                         .With(t => t.DownloadProtocol = DownloadProtocol.Torrent)
                                                         .With(t => t.InfoHash = downloadId)
                                                         .Build();

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_be_accepted_if_release_torrent_hash_is_null()
        {
            var downloadId = Guid.NewGuid().ToString().ToUpper();

            GivenHistoryItem(downloadId, TITLE, _mp3, EntityHistoryEventType.Grabbed);
            GivenHistoryItem(downloadId, TITLE, _flac, EntityHistoryEventType.BookFileImported);

            _remoteBook.Release = Builder<TorrentInfo>.CreateNew()
                                                         .With(t => t.DownloadProtocol = DownloadProtocol.Torrent)
                                                         .With(t => t.InfoHash = null)
                                                         .Build();

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_accepted_if_release_torrent_hash_is_null_and_downloadId_is_null()
        {
            GivenHistoryItem(null, TITLE, _mp3, EntityHistoryEventType.Grabbed);
            GivenHistoryItem(null, TITLE, _flac, EntityHistoryEventType.BookFileImported);

            _remoteBook.Release = Builder<TorrentInfo>.CreateNew()
                                                         .With(t => t.DownloadProtocol = DownloadProtocol.Torrent)
                                                         .With(t => t.InfoHash = null)
                                                         .Build();

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_rejected_if_release_title_matches_grabbed_event_source_title()
        {
            var downloadId = Guid.NewGuid().ToString().ToUpper();

            GivenHistoryItem(downloadId, TITLE, _mp3, EntityHistoryEventType.Grabbed);
            GivenHistoryItem(downloadId, TITLE, _flac, EntityHistoryEventType.BookFileImported);

            _remoteBook.Release = Builder<TorrentInfo>.CreateNew()
                                                         .With(t => t.DownloadProtocol = DownloadProtocol.Torrent)
                                                         .With(t => t.InfoHash = downloadId)
                                                         .Build();

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_only_consider_history_of_the_releases_media_type()
        {
            // The volume's audiobook was grabbed and imported under this exact release title;
            // an EPUB release with the same title is a different class and must not be blocked.
            GivenHistoryItem("abc", TITLE, new QualityModel(Quality.M4B), EntityHistoryEventType.Grabbed);
            GivenHistoryItem("abc", TITLE, new QualityModel(Quality.MP3), EntityHistoryEventType.BookFileImported);
            _history.ForEach(h => h.Data[EntityHistory.MEDIA_TYPE] = "audio");

            _remoteBook.Release.Title = TITLE;
            _remoteBook.MediaType = MediaType.Ebook;
            _remoteBook.Books[0].WithEdition(MediaType.Ebook, id: 13);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();

            _remoteBook.MediaType = MediaType.Audio;
            _remoteBook.Books[0].WithEdition(MediaType.Audio, id: 12);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }
    }
}
