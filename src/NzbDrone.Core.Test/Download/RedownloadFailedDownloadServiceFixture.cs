using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class RedownloadFailedDownloadServiceFixture : CoreTest<RedownloadFailedDownloadService>
    {
        private Author _author;
        private List<Book> _books;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(x => x.AutoRedownloadFailed)
                .Returns(true);

            // a monitored manga series of three wanted volumes (one Archive edition each, no files)
            _author = new Author { Id = 1, Monitored = true, Metadata = new AuthorMetadata { ForeignAuthorId = "local-dandadan" } };
            _books = Builder<Book>.CreateListOfSize(3)
                .All()
                .With(b => b.Monitored = true)
                .With(b => b.Author = _author)
                .Build()
                .ToList();

            _books.ForEach(b => b.WithEdition(MediaType.Archive));

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthor(It.IsAny<int>()))
                .Returns(_books);

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooks(It.IsAny<IEnumerable<int>>()))
                .Returns<IEnumerable<int>>(ids => _books.Where(b => ids.Contains(b.Id)).ToList());

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByEdition(It.IsAny<int>()))
                .Returns(new List<BookFile>());
        }

        // A light-novel volume (Ebook + Audio editions, both monitored) in place of the manga one.
        private Book GivenLightNovelVolume(int id, bool hasEpub = false, bool hasAudio = false)
        {
            _author.Metadata.Value.ForeignAuthorId = "local-overlord~ln";

            var volume = new Book { Id = id, Monitored = true, Author = _author };
            var ebook = volume.WithEdition(MediaType.Ebook);
            var audio = volume.WithEdition(MediaType.Audio);

            _books = new List<Book> { volume };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByEdition(ebook.Id))
                .Returns(hasEpub ? new List<BookFile> { new BookFile { Id = 1, EditionId = ebook.Id } } : new List<BookFile>());

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByEdition(audio.Id))
                .Returns(hasAudio ? new List<BookFile> { new BookFile { Id = 2, EditionId = audio.Id } } : new List<BookFile>());

            return volume;
        }

        private static DownloadFailedEvent FailedGrab(int authorId, MediaType mediaType, params int[] bookIds)
        {
            // the way FailedDownloadService builds it: the grab row's Data (with its media type key)
            // and quality ride on the event
            return new DownloadFailedEvent
            {
                AuthorId = authorId,
                BookIds = bookIds.ToList(),
                Quality = new QualityModel(Quality.Unknown),
                Data = new Dictionary<string, string> { { EntityHistory.MEDIA_TYPE, mediaType.ToString().ToLowerInvariant() } }
            };
        }

        [Test]
        public void should_skip_redownload_if_event_has_skipredownload_set()
        {
            var failedEvent = new DownloadFailedEvent
            {
                AuthorId = 1,
                BookIds = new List<int> { 1 },
                SkipRedownload = true
            };

            Subject.Handle(failedEvent);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<Command>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()),
                        Times.Never());
        }

        [Test]
        public void should_skip_redownload_if_redownload_failed_disabled()
        {
            var failedEvent = new DownloadFailedEvent
            {
                AuthorId = 1,
                BookIds = new List<int> { 1 }
            };

            Mocker.GetMock<IConfigService>()
                .Setup(x => x.AutoRedownloadFailed)
                .Returns(false);

            Subject.Handle(failedEvent);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<Command>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()),
                        Times.Never());
        }

        [Test]
        public void should_redownload_book_on_failure()
        {
            var failedEvent = new DownloadFailedEvent
            {
                AuthorId = 1,
                BookIds = new List<int> { 2 }
            };

            Subject.Handle(failedEvent);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<BookSearchCommand>(c => c.BookIds.Count == 1 &&
                                                              c.BookIds[0] == 2),
                                    It.IsAny<CommandPriority>(),
                                    It.IsAny<CommandTrigger>()),
                        Times.Once());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<AuthorSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()),
                        Times.Never());
        }

        [Test]
        public void should_redownload_multiple_books_on_failure()
        {
            var failedEvent = new DownloadFailedEvent
            {
                AuthorId = 1,
                BookIds = new List<int> { 2, 3 }
            };

            Subject.Handle(failedEvent);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<BookSearchCommand>(c => c.BookIds.Count == 2 &&
                                                              c.BookIds[0] == 2 &&
                                                              c.BookIds[1] == 3),
                                    It.IsAny<CommandPriority>(),
                                    It.IsAny<CommandTrigger>()),
                        Times.Once());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<AuthorSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()),
                        Times.Never());
        }

        [Test]
        public void should_redownload_author_on_failure()
        {
            // note that author is set to have 3 books in setup
            var failedEvent = new DownloadFailedEvent
            {
                AuthorId = 2,
                BookIds = new List<int> { 1, 2, 3 }
            };

            Subject.Handle(failedEvent);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<AuthorSearchCommand>(c => c.AuthorId == failedEvent.AuthorId),
                                    It.IsAny<CommandPriority>(),
                                    It.IsAny<CommandTrigger>()),
                        Times.Once());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<BookSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()),
                        Times.Never());
        }

        // Light novels (2026-09, final review I1): the re-search is per (volume, media type). A
        // failed audiobook grab for a volume that already has its EPUB is still wanted -- on its
        // Audio edition -- and the search pushed is typed to that class only.

        [Test]
        public void should_search_the_failed_media_type_of_a_light_novel_volume_that_has_its_other_format()
        {
            var volume = GivenLightNovelVolume(5, hasEpub: true);

            Subject.Handle(FailedGrab(_author.Id, MediaType.Audio, volume.Id));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<BookSearchCommand>(c => c.BookIds.Count == 1 && c.BookIds[0] == 5 && c.MediaType == MediaType.Audio),
                                    It.IsAny<CommandPriority>(),
                                    It.IsAny<CommandTrigger>()),
                        Times.Once());
        }

        [Test]
        public void should_search_the_epub_of_a_light_novel_volume_that_has_its_audio()
        {
            var volume = GivenLightNovelVolume(5, hasAudio: true);

            Subject.Handle(FailedGrab(_author.Id, MediaType.Ebook, volume.Id));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<BookSearchCommand>(c => c.BookIds.Count == 1 && c.BookIds[0] == 5 && c.MediaType == MediaType.Ebook),
                                    It.IsAny<CommandPriority>(),
                                    It.IsAny<CommandTrigger>()),
                        Times.Once());
        }

        [Test]
        public void should_not_search_a_light_novel_edition_that_already_has_its_file()
        {
            var volume = GivenLightNovelVolume(5, hasEpub: true, hasAudio: true);

            Subject.Handle(FailedGrab(_author.Id, MediaType.Audio, volume.Id));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<Command>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()),
                        Times.Never());
        }

        [Test]
        public void should_not_search_an_unmonitored_light_novel_edition()
        {
            var volume = GivenLightNovelVolume(5);
            volume.EditionOf(MediaType.Audio).Monitored = false;

            Subject.Handle(FailedGrab(_author.Id, MediaType.Audio, volume.Id));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<Command>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()),
                        Times.Never());
        }

        [Test]
        public void should_type_a_whole_author_research_of_a_light_novel()
        {
            _author.Metadata.Value.ForeignAuthorId = "local-overlord~ln";
            _books.ForEach(b => b.Editions = new List<Edition>());
            _books.ForEach(b => b.WithEdition(MediaType.Ebook));
            _books.ForEach(b => b.WithEdition(MediaType.Audio));

            Subject.Handle(FailedGrab(_author.Id, MediaType.Audio, 1, 2, 3));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<AuthorSearchCommand>(c => c.AuthorId == _author.Id && c.MediaType == MediaType.Audio),
                                    It.IsAny<CommandPriority>(),
                                    It.IsAny<CommandTrigger>()),
                        Times.Once());
        }

        // Manga: one Archive edition per volume, the book-level file check as before, untyped search.

        [Test]
        public void should_not_search_a_manga_volume_that_has_a_file()
        {
            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(2))
                .Returns(new List<BookFile> { new BookFile { Id = 1 } });

            Subject.Handle(FailedGrab(_author.Id, MediaType.Archive, 2));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<Command>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()),
                        Times.Never());

            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.GetFilesByEdition(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void manga_research_is_untyped_and_reads_files_by_book()
        {
            Subject.Handle(FailedGrab(_author.Id, MediaType.Archive, 2));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<BookSearchCommand>(c => c.BookIds[0] == 2 && c.MediaType == null),
                                    It.IsAny<CommandPriority>(),
                                    It.IsAny<CommandTrigger>()),
                        Times.Once());

            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.GetFilesByEdition(It.IsAny<int>()), Times.Never());
        }
    
        // 2026-09-22: a failed grab of a light novel with several volumes of that class still wanted
        // queues ONE typed series search, and the rest of the burst does not repeat it.
        private List<Book> GivenLightNovelSeriesOfTwoWanted()
        {
            _author.Metadata.Value.ForeignAuthorId = "local-classroom-of-the-elite~ln";
            var volumes = new List<Book>
            {
                new Book { Id = 11, AuthorId = _author.Id, Monitored = true, Author = _author },
                new Book { Id = 12, AuthorId = _author.Id, Monitored = true, Author = _author }
            };

            foreach (var v in volumes)
            {
                v.WithEdition(MediaType.Ebook);
                var audio = v.WithEdition(MediaType.Audio);
                audio.Asin = "B0" + v.Id;                                   // released audio
                audio.AudioReleaseDate = System.DateTime.UtcNow.AddDays(-30);
            }

            _books = volumes;

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthor(It.IsAny<int>()))
                .Returns(volumes);
            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooks(It.IsAny<IEnumerable<int>>()))
                .Returns<IEnumerable<int>>(ids => volumes.Where(b => ids.Contains(b.Id)).ToList());

            return volumes;
        }

        [Test]
        public void a_light_novel_with_several_wanted_volumes_gets_one_typed_series_search()
        {
            GivenLightNovelSeriesOfTwoWanted();

            Subject.Handle(FailedGrab(_author.Id, MediaType.Ebook, 11));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<AuthorSearchCommand>(c => c.AuthorId == _author.Id && c.MediaType == MediaType.Ebook),
                                    It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<BookSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void the_rest_of_a_failure_burst_does_not_repeat_the_series_search()
        {
            GivenLightNovelSeriesOfTwoWanted();

            Subject.Handle(FailedGrab(_author.Id, MediaType.Ebook, 11));
            Subject.Handle(FailedGrab(_author.Id, MediaType.Ebook, 12));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<AuthorSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<BookSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void the_cooldown_is_per_media_type()
        {
            GivenLightNovelSeriesOfTwoWanted();

            Subject.Handle(FailedGrab(_author.Id, MediaType.Ebook, 11));
            Subject.Handle(FailedGrab(_author.Id, MediaType.Audio, 11));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<AuthorSearchCommand>(c => c.MediaType == MediaType.Audio),
                                    It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }
    }
}
