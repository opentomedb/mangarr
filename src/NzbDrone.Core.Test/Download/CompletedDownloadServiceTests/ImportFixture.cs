using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.CompletedDownloadServiceTests
{
    [TestFixture]
    public class ImportFixture : CoreTest<CompletedDownloadService>
    {
        private TrackedDownload _trackedDownload;
        private Author _author;

        [SetUp]
        public void Setup()
        {
            var completed = Builder<DownloadClientItem>.CreateNew()
                                                    .With(h => h.Status = DownloadItemStatus.Completed)
                                                    .With(h => h.OutputPath = new OsPath(@"C:\DropFolder\MyDownload".AsOsAgnostic()))
                                                    .With(h => h.Title = "Drone.S01E01.HDTV")
                                                    .Build();

            var remoteBook = BuildRemoteBook();

            _trackedDownload = Builder<TrackedDownload>.CreateNew()
                    .With(c => c.State = TrackedDownloadState.Downloading)
                    .With(c => c.DownloadItem = completed)
                    .With(c => c.RemoteBook = remoteBook)
                    .Build();

            _author = Builder<Author>.CreateNew()
                .Build();

            Mocker.GetMock<IDownloadClient>()
              .SetupGet(c => c.Definition)
              .Returns(new DownloadClientDefinition { Id = 1, Name = "testClient" });

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(c => c.Get(It.IsAny<int>()))
                  .Returns(Mocker.GetMock<IDownloadClient>().Object);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.MostRecentForDownloadId(_trackedDownload.DownloadItem.DownloadId))
                  .Returns(new EntityHistory());

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetAuthor("Drone.S01E01.HDTV"))
                  .Returns(remoteBook.Author);

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<EntityHistory>());

            Mocker.GetMock<IProvideImportItemService>()
                .Setup(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                .Returns<DownloadClientItem, DownloadClientItem>((i, p) => i);
        }

        private Book CreateBook(int id)
        {
            var book = new Book { Id = id };
            book.WithEdition(MediaType.Archive);

            return book;
        }

        private void GivenGrabbedHistory()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<EntityHistory> { new EntityHistory { EventType = EntityHistoryEventType.Grabbed } });
        }

        private void GivenPayloadFiles(params string[] files)
        {
            var outputPath = _trackedDownload.DownloadItem.OutputPath.FullPath;

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderExists(outputPath))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles(outputPath, true))
                .Returns(files.Select(f => Path.Combine(outputPath, f)).ToArray());
        }

        private void GivenNothingImportedFrom(params string[] files)
        {
            GivenPayloadFiles(files);

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(new List<ImportResult>());
        }

        // Since .epub and audio became importable (2026-09) every payload file gets a decision, so a
        // wrong-class payload comes back as one REJECTED result per file, never as an empty list.
        private void GivenEveryFileRejectedFrom(params string[] files)
        {
            GivenPayloadFiles(files);

            var outputPath = _trackedDownload.DownloadItem.OutputPath.FullPath;

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(files.Select(f => new ImportResult(
                    new ImportDecision<LocalBook>(new LocalBook { Path = Path.Combine(outputPath, f) }, new Rejection("Wrong media type for the edition")),
                    "Wrong media type for the edition")).ToList());
        }

        private void GivenLightNovelGrab(bool audioMonitored = true)
        {
            var volume = new Book { Id = 5 };
            volume.WithEdition(MediaType.Ebook);
            volume.WithEdition(MediaType.Audio, monitored: audioMonitored);

            _trackedDownload.RemoteBook.Author.Metadata.Value.ForeignAuthorId = "local-overlord~ln";
            _trackedDownload.RemoteBook.Books = new List<Book> { volume };
        }

        // Covered volumes (2026-09-17, D4): an audio release of a light-novel entry mapped to two volumes.
        private (Book Vol1, Book Vol2) GivenSpanningAudioGrab(MediaType mediaType = MediaType.Audio)
        {
            var vol1 = new Book { Id = 1, VolumeNumber = 1, AuthorMetadataId = 3, Title = "TBATE Vol. 1" };
            vol1.WithEdition(MediaType.Ebook);
            vol1.WithEdition(MediaType.Audio);

            var vol2 = new Book { Id = 2, VolumeNumber = 2, AuthorMetadataId = 3, Title = "TBATE Vol. 2" };
            vol2.WithEdition(MediaType.Ebook);
            vol2.WithEdition(MediaType.Audio);

            _trackedDownload.RemoteBook.Author.Metadata.Value.ForeignAuthorId = "local-the-beginning-after-the-end~ln";
            _trackedDownload.RemoteBook.Books = new List<Book> { vol1, vol2 };
            _trackedDownload.RemoteBook.MediaType = mediaType;
            _trackedDownload.DownloadItem.Title = "The Beginning After the End Vol. 01-02 [M4B]";

            // VerifyImport reads the marks back through the edition service: the books' own editions.
            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionsByBook(It.IsAny<int>()))
                .Returns<int>(id => _trackedDownload.RemoteBook.Books.Single(b => b.Id == id).Editions.Value);

            return (vol1, vol2);
        }

        // The real service marks the covered volumes' Audio editions; the mock does the same so the
        // verification that follows the marks sees them.
        private void GivenMarksAreWritten()
        {
            Mocker.GetMock<ICoveredVolumeService>()
                .Setup(s => s.MarkCoveredByImport(It.IsAny<Book>(), It.IsAny<IEnumerable<Book>>(), It.IsAny<string>()))
                .Returns<Book, IEnumerable<Book>, string>((carrier, covered, reason) =>
                {
                    var marked = covered.Select(b => b.EditionOf(MediaType.Audio)).ToList();
                    marked.ForEach(e => e.CoveredByVolume = carrier.VolumeNumber);
                    return marked;
                });
        }

        private List<ImportResult> GivenImported(string[] files, params Book[] books)
        {
            GivenPayloadFiles(files);

            var outputPath = _trackedDownload.DownloadItem.OutputPath.FullPath;
            var results = files.Zip(books, (file, book) => new ImportResult(
                    new ImportDecision<LocalBook>(new LocalBook { Path = Path.Combine(outputPath, file), Author = _author, Book = book })))
                .ToList();

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(results);

            return results;
        }

        private RemoteBook BuildRemoteBook()
        {
            return new RemoteBook
            {
                Author = new Author(),
                Books = new List<Book> { CreateBook(1) }
            };
        }

        private void GivenABadlyNamedDownload()
        {
            _trackedDownload.DownloadItem.DownloadId = "1234";
            _trackedDownload.DownloadItem.Title = "Droned Pilot"; // Set a badly named download
            Mocker.GetMock<IHistoryService>()
               .Setup(s => s.MostRecentForDownloadId(It.Is<string>(i => i == "1234")))
               .Returns(new EntityHistory() { SourceTitle = "Droned S01E01" });

            Mocker.GetMock<IParsingService>()
               .Setup(s => s.GetAuthor(It.IsAny<string>()))
               .Returns((Author)null);

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.GetAuthor("Droned S01E01"))
                .Returns(BuildRemoteBook().Author);
        }

        private void GivenAuthorMatch()
        {
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetAuthor(It.IsAny<string>()))
                  .Returns(_trackedDownload.RemoteBook.Author);
        }

        [Test]
        public void should_not_mark_as_imported_if_all_files_were_rejected()
        {
            Mocker.GetMock<IDownloadedBooksImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(
                                   new ImportDecision<LocalBook>(
                                       new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic() }, new Rejection("Rejected!")), "Test Failure"),

                               new ImportResult(
                                   new ImportDecision<LocalBook>(
                                       new LocalBook { Path = @"C:\TestPath\Droned.S01E02.mkv".AsOsAgnostic() }, new Rejection("Rejected!")), "Test Failure")
                           });

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent<DownloadCompletedEvent>(It.IsAny<DownloadCompletedEvent>()), Times.Never());

            AssertNotImported();
        }

        [Test]
        public void should_not_mark_as_imported_if_no_tracks_were_parsed()
        {
            Mocker.GetMock<IDownloadedBooksImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(
                                   new ImportDecision<LocalBook>(
                                       new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic() }, new Rejection("Rejected!")), "Test Failure"),

                               new ImportResult(
                                   new ImportDecision<LocalBook>(
                                       new LocalBook { Path = @"C:\TestPath\Droned.S01E02.mkv".AsOsAgnostic() }, new Rejection("Rejected!")), "Test Failure")
                           });

            _trackedDownload.RemoteBook.Books.Clear();

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [Test]
        public void should_not_mark_as_failed_if_nothing_found_to_import()
        {
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(new List<ImportResult>());

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        [Test]
        public void should_mark_as_failed_if_nothing_to_import_and_payload_is_ebook_only()
        {
            GivenGrabbedHistory();

            var outputPath = _trackedDownload.DownloadItem.OutputPath.FullPath;

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(new List<ImportResult>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderExists(outputPath))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles(outputPath, true))
                .Returns(new[]
                {
                    @"C:\DropFolder\MyDownload\Book v01.epub".AsOsAgnostic(),
                    @"C:\DropFolder\MyDownload\Book v02.epub".AsOsAgnostic(),
                    @"C:\DropFolder\MyDownload\cover.jpg".AsOsAgnostic()
                });

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>()
                .Verify(v => v.MarkAsFailed(_trackedDownload.DownloadItem.DownloadId, false, It.IsAny<string>()), Times.Once());

            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailed);

            // Not ExpectedWarns(1): the first test in a run loses NLog capture (config is wiped
            // once per process and only repaired by the next test's InitLogging), and this test
            // sorts first in the fixture. IgnoreWarns keeps teardown clean at any position.
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void should_not_mark_as_failed_if_nothing_to_import_and_payload_has_no_ebooks()
        {
            var outputPath = _trackedDownload.DownloadItem.OutputPath.FullPath;

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(new List<ImportResult>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderExists(outputPath))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles(outputPath, true))
                .Returns(new[]
                {
                    @"C:\DropFolder\MyDownload\release.nfo".AsOsAgnostic()
                });

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>()
                .Verify(v => v.MarkAsFailed(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        [Test]
        public void should_mark_as_failed_if_single_file_download_is_an_ebook()
        {
            GivenGrabbedHistory();

            _trackedDownload.DownloadItem.OutputPath = new OsPath(@"C:\DropFolder\Book v01.epub".AsOsAgnostic());
            var outputPath = _trackedDownload.DownloadItem.OutputPath.FullPath;

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(new List<ImportResult>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(outputPath))
                .Returns(true);

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>()
                .Verify(v => v.MarkAsFailed(_trackedDownload.DownloadItem.DownloadId, false, It.IsAny<string>()), Times.Once());

            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailed);

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void should_not_mark_as_imported_if_all_files_were_skipped()
        {
            Mocker.GetMock<IDownloadedBooksImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic() }), "Test Failure"),
                               new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic() }), "Test Failure")
                           });

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [Test]
        public void should_mark_as_imported_if_all_tracks_were_imported_but_extra_files_were_not()
        {
            GivenAuthorMatch();

            _trackedDownload.RemoteBook.Books = new List<Book>
            {
                CreateBook(1)
            };

            Mocker.GetMock<IDownloadedBooksImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic(), Author = _author })),
                               new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic(), Author = _author }), "Test Failure")
                           });

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<EntityHistory>());

            Subject.Import(_trackedDownload);

            AssertImported();
        }

        [Test]
        public void should_not_mark_as_imported_if_some_tracks_were_not_imported()
        {
            _trackedDownload.RemoteBook.Books = new List<Book>
            {
                CreateBook(1),
                CreateBook(1),
                CreateBook(1)
            };

            Mocker.GetMock<IDownloadedBooksImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic() })),
                               new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic() })),
                               new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic() }), "Test Failure"),
                               new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic() }), "Test Failure"),
                               new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic() }), "Test Failure")
                           });

            var history = Builder<EntityHistory>.CreateListOfSize(2)
                                                  .BuildList();

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(history);

            Mocker.GetMock<ITrackedDownloadAlreadyImported>()
                  .Setup(s => s.IsImported(_trackedDownload, history))
                  .Returns(true);

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [Test]
        public void should_not_mark_as_imported_if_some_of_episodes_were_not_imported_including_history()
        {
            var books = Builder<Book>.CreateListOfSize(3).BuildList();

            _trackedDownload.RemoteBook.Books = books;

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(new List<ImportResult>
                {
                    new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv" })),
                    new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv" }), "Test Failure"),
                    new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv" }), "Test Failure")
                });

            var history = Builder<EntityHistory>.CreateListOfSize(2)
                                                  .BuildList();

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(history);

            Mocker.GetMock<ITrackedDownloadAlreadyImported>()
                  .Setup(s => s.IsImported(It.IsAny<TrackedDownload>(), It.IsAny<List<EntityHistory>>()))
                  .Returns(false);

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [Test]
        public void should_mark_as_imported_if_all_tracks_were_imported()
        {
            _trackedDownload.RemoteBook.Books = new List<Book>
            {
                CreateBook(1)
            };

            Mocker.GetMock<IDownloadedBooksImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(
                                   new ImportDecision<LocalBook>(
                                       new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic(), Author = _author })),

                               new ImportResult(
                                   new ImportDecision<LocalBook>(
                                       new LocalBook { Path = @"C:\TestPath\Droned.S01E02.mkv".AsOsAgnostic(), Author = _author }))
                           });

            Subject.Import(_trackedDownload);

            AssertImported();
        }

        [Test]
        public void should_mark_as_imported_if_all_episodes_were_imported_including_history()
        {
            var books = Builder<Book>.CreateListOfSize(2).BuildList();

            _trackedDownload.RemoteBook.Books = books;

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(new List<ImportResult>
                {
                    new ImportResult(
                        new ImportDecision<LocalBook>(
                            new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv", Book = books[0], Author = _author })),

                    new ImportResult(
                        new ImportDecision<LocalBook>(
                            new LocalBook { Path = @"C:\TestPath\Droned.S01E02.mkv", Book = books[1], Author = _author }), "Test Failure")
                });

            var history = Builder<EntityHistory>.CreateListOfSize(2)
                .All()
                .With(x => x.EventType = EntityHistoryEventType.BookFileImported)
                .With(x => x.AuthorId = 1)
                .BuildList();

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(history);

            Mocker.GetMock<ITrackedDownloadAlreadyImported>()
                  .Setup(s => s.IsImported(It.IsAny<TrackedDownload>(), It.IsAny<List<EntityHistory>>()))
                  .Returns(true);

            Subject.Import(_trackedDownload);

            AssertImported();
        }

        [Test]
        public void should_mark_as_imported_if_the_download_can_be_tracked_using_the_source_seriesid()
        {
            GivenABadlyNamedDownload();

            Mocker.GetMock<IDownloadedBooksImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = @"C:\TestPath\Droned.S01E01.mkv".AsOsAgnostic(), Author = _author }))
                           });

            Subject.Import(_trackedDownload);

            AssertImported();
        }

        [Test]
        public void should_mark_as_imported_when_every_file_imported_but_mapping_expected_more_books()
        {
            // A tokenless pack maps to the whole series (3 books) but only holds 2 volumes.
            _trackedDownload.RemoteBook.Books = new List<Book>
            {
                CreateBook(1),
                CreateBook(2),
                CreateBook(3)
            };

            Mocker.GetMock<IDownloadedBooksImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(
                                   new ImportDecision<LocalBook>(
                                       new LocalBook { Path = @"C:\TestPath\Series v01.cbz".AsOsAgnostic(), Author = _author, Book = CreateBook(1) })),

                               new ImportResult(
                                   new ImportDecision<LocalBook>(
                                       new LocalBook { Path = @"C:\TestPath\Series v02.cbz".AsOsAgnostic(), Author = _author, Book = CreateBook(2) }))
                           });

            Mocker.GetMock<ITrackedDownloadAlreadyImported>()
                  .Setup(v => v.IsImported(It.IsAny<TrackedDownload>(), It.IsAny<List<EntityHistory>>()))
                  .Returns(false);

            Subject.Import(_trackedDownload);

            AssertImported();
        }

        [Test]
        public void should_remove_ebook_only_download_from_client_when_remove_failed_downloads_is_enabled()
        {
            GivenGrabbedHistory();

            var outputPath = _trackedDownload.DownloadItem.OutputPath.FullPath;

            Mocker.GetMock<IDownloadClient>()
              .SetupGet(c => c.Definition)
              .Returns(new DownloadClientDefinition { Id = 1, Name = "testClient", RemoveFailedDownloads = true });

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(new List<ImportResult>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderExists(outputPath))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles(outputPath, true))
                .Returns(new[]
                {
                    @"C:\DropFolder\MyDownload\Book v01.epub".AsOsAgnostic(),
                    @"C:\DropFolder\MyDownload\Book v02.epub".AsOsAgnostic()
                });

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IDownloadClient>()
                .Verify(v => v.RemoveItem(_trackedDownload.DownloadItem, true), Times.Once());

            _trackedDownload.DownloadItem.Removed.Should().BeTrue();
            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailed);

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void should_not_remove_ebook_only_download_from_client_when_remove_failed_downloads_is_disabled()
        {
            GivenGrabbedHistory();

            var outputPath = _trackedDownload.DownloadItem.OutputPath.FullPath;

            Mocker.GetMock<IDownloadClient>()
              .SetupGet(c => c.Definition)
              .Returns(new DownloadClientDefinition { Id = 1, Name = "testClient", RemoveFailedDownloads = false });

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(new List<ImportResult>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderExists(outputPath))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles(outputPath, true))
                .Returns(new[]
                {
                    @"C:\DropFolder\MyDownload\Book v01.epub".AsOsAgnostic()
                });

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IDownloadClient>()
                .Verify(v => v.RemoveItem(It.IsAny<DownloadClientItem>(), It.IsAny<bool>()), Times.Never());

            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailed);

            ExceptionVerification.IgnoreWarns();
        }

        // Light novels (2026-09): the payload's class against the grabbed volume's editions (D9).
        [Test]
        public void should_not_fail_an_epub_only_payload_for_a_light_novel_volume()
        {
            GivenLightNovelGrab();
            GivenGrabbedHistory();
            GivenNothingImportedFrom("Overlord v05.epub", "cover.jpg");

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        [Test]
        public void should_not_fail_an_audio_only_payload_for_a_light_novel_volume()
        {
            GivenLightNovelGrab();
            GivenGrabbedHistory();
            GivenNothingImportedFrom("Overlord v05 - 01.mp3", "Overlord v05 - 02.mp3");

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        [Test]
        public void should_fail_an_archive_only_payload_for_a_light_novel_volume()
        {
            GivenLightNovelGrab();
            GivenGrabbedHistory();
            GivenNothingImportedFrom("Overlord v05.cbz");

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(_trackedDownload.DownloadItem.DownloadId, false, It.IsAny<string>()), Times.Once());
            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailed);

            ExceptionVerification.IgnoreWarns();
        }

        // LN PDF fix round 1 (2026-09-22): the ebook profile's Ebook PDF item, so the payload check
        // only reads .pdf-is-ebook when id 8 is actually ticked -- the maintainer's default profiles ship it
        // unticked, so with nothing to opt in an LN PDF-only payload keeps failing.
        private void GivenEbookPdfAllowed(bool allowed)
        {
            _trackedDownload.RemoteBook.Author.QualityProfile = new QualityProfile
            {
                Items = new List<QualityProfileQualityItem> { new QualityProfileQualityItem { Quality = Quality.EbookPdf, Allowed = allowed } }
            };
        }

        // LN PDF (2026-09-22): a light novel's PDF is its ebook, so a .pdf-only payload for a
        // light-novel volume goes on to the import instead of being failed and blocklisted as
        // "only archive files"; a .cbz payload still fails (above). Fix round 1 (2026-09-22): only
        // when the ebook profile actually wants Ebook PDF (id 8) -- the maintainer's default profiles ship it
        // unticked, so this needs an explicit opt-in.
        [Test]
        public void should_not_fail_a_pdf_only_payload_for_a_light_novel_volume_when_ebook_pdf_is_allowed()
        {
            GivenLightNovelGrab();
            GivenEbookPdfAllowed(true);
            GivenGrabbedHistory();
            GivenNothingImportedFrom("Overlord v05.pdf");

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        // Fix round 1 (2026-09-22): the unticked twin -- the maintainer's default. Without the opt-in, a
        // .pdf-only light-novel payload fails + blocklists exactly as it did before the LN-PDF round.
        [Test]
        public void should_fail_a_pdf_only_payload_for_a_light_novel_volume_when_ebook_pdf_is_not_allowed()
        {
            GivenLightNovelGrab();
            GivenEbookPdfAllowed(false);
            GivenGrabbedHistory();
            GivenNothingImportedFrom("Overlord v05.pdf");

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(_trackedDownload.DownloadItem.DownloadId, false, It.IsAny<string>()), Times.Once());
            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailed);

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void should_fail_an_audio_only_payload_when_the_audio_edition_is_unmonitored()
        {
            GivenLightNovelGrab(audioMonitored: false);
            GivenGrabbedHistory();
            GivenNothingImportedFrom("Overlord v05.m4b");

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailed);

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void should_fail_an_audio_only_payload_for_a_manga_volume()
        {
            GivenGrabbedHistory();
            GivenNothingImportedFrom("Series v01.m4b");

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailed);

            ExceptionVerification.IgnoreWarns();
        }

        // Server messages (2026-09-26): each payload class has its own sentence; an audiobook payload warns (and
        // fails) with the audiobook one, word for word the English the old Describe(...) template gave.
        [Test]
        public void should_warn_with_the_audiobook_sentence_for_an_audio_only_payload()
        {
            const string english = "Download contains only audiobook files, which no monitored edition of the grabbed volume(s) can hold";

            GivenGrabbedHistory();
            GivenNothingImportedFrom("Series v01.m4b");

            Subject.Import(_trackedDownload);

            var warning = _trackedDownload.StatusMessages.Single();
            warning.Messages.Should().Equal(english);
            warning.MessageTexts.Single().Template.Should().Be(english);
            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(It.IsAny<string>(), false, english), Times.Once());

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void mixed_class_payload_keeps_the_manual_intervention_warning()
        {
            GivenGrabbedHistory();
            GivenNothingImportedFrom("Series v01.cbz", "Series v01.epub");

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        // C1 (final review): the class check must not hide behind an empty import result. With
        // .epub / audio importable, the pipeline REJECTS each wrong-class file instead of ignoring
        // it, and the grab must still fail (blocklist + re-search) rather than sit ImportPending.
        [Test]
        public void should_fail_an_epub_only_payload_for_a_manga_volume_even_when_the_import_rejected_it()
        {
            GivenGrabbedHistory();
            GivenEveryFileRejectedFrom("Series v01.epub");

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(_trackedDownload.DownloadItem.DownloadId, false, It.IsAny<string>()), Times.Once());
            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailed);

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void should_fail_an_archive_only_payload_for_a_light_novel_volume_even_when_the_import_rejected_it()
        {
            GivenLightNovelGrab();
            GivenGrabbedHistory();
            GivenEveryFileRejectedFrom("Series v01.cbz");

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(_trackedDownload.DownloadItem.DownloadId, false, It.IsAny<string>()), Times.Once());
            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailed);

            ExceptionVerification.IgnoreWarns();
        }

        // NB1 (re-review): a pack maps to several volumes; the class check must fail the grab only
        // when NO grabbed volume can hold the payload's class. One volume with that edition
        // unmonitored (the per-edition toggle) must not blocklist the whole pack.
        [Test]
        public void should_not_fail_a_light_novel_pack_when_one_mapped_volume_has_that_edition_unmonitored()
        {
            var vol5 = new Book { Id = 5 };
            vol5.WithEdition(MediaType.Ebook);
            vol5.WithEdition(MediaType.Audio);

            var vol6 = new Book { Id = 6 };
            vol6.WithEdition(MediaType.Ebook, monitored: false);
            vol6.WithEdition(MediaType.Audio);

            _trackedDownload.RemoteBook.Author.Metadata.Value.ForeignAuthorId = "local-overlord~ln";
            _trackedDownload.RemoteBook.Books = new List<Book> { vol5, vol6 };

            GivenGrabbedHistory();
            GivenNothingImportedFrom("Overlord v05.epub", "Overlord v06.epub");

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Verify(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()), Times.Once());

            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        // Line safety (2026-09-28): a pack the sibling-line guard rejects (SiblingLineReleaseSpecification) is
        // an import rejection like any other: the grab is parked as Import Failed with the reason for Manual
        // Import -- never failed, blocklisted or removed from the client the way an unusable payload is.
        [Test]
        public void a_sibling_line_rejection_is_parked_for_manual_import_not_failed()
        {
            const string reason = "Release covers volumes 1-13; this series' line has 6 — it may belong to Main";

            GivenLightNovelGrab();
            GivenGrabbedHistory();
            GivenPayloadFiles("Otome v01.epub", "Otome v02.epub");

            var outputPath = _trackedDownload.DownloadItem.OutputPath.FullPath;
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(new[] { "Otome v01.epub", "Otome v02.epub" }.Select(f => new ImportResult(
                    new ImportDecision<LocalBook>(new LocalBook { Path = Path.Combine(outputPath, f) }, new Rejection(reason)), reason)).ToList());

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDownloadClient>().Verify(v => v.RemoveItem(It.IsAny<DownloadClientItem>(), It.IsAny<bool>()), Times.Never());
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportFailed);
            _trackedDownload.StatusMessages.SelectMany(m => m.Messages).Should().Contain(reason);
        }

        // Beta polish (2026-09-28): a download Mangarr did not grab can't be marked as failed (no
        // Grabbed row), so it is parked as ImportFailed with the warning. It used to go back to
        // ImportPending and loop (the 2026-09-25 EPUB: 202 "marking as failed" warnings in two hours).
        [Test]
        public void wrong_class_payload_without_grabbed_history_is_parked_as_import_failed()
        {
            GivenNothingImportedFrom("Series v01.epub");

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDownloadClient>().Verify(v => v.RemoveItem(It.IsAny<DownloadClientItem>(), It.IsAny<bool>()), Times.Never());
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportFailed);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);

            ExceptionVerification.IgnoreWarns();
        }

        // Two consecutive queue refreshes through the real DownloadProcessingService, which runs Import
        // only for ImportPending: the unusable payload is judged, and failed, exactly once.
        private void ProcessMonitoredDownloadsTwice()
        {
            Mocker.GetMock<NzbDrone.Core.Configuration.IConfigService>()
                .SetupGet(c => c.EnableCompletedDownloadHandling)
                .Returns(true);

            Mocker.GetMock<ITrackedDownloadService>()
                .Setup(s => s.GetTrackedDownloads())
                .Returns(new List<TrackedDownload> { _trackedDownload });

            _trackedDownload.IsTrackable = true;
            _trackedDownload.State = TrackedDownloadState.ImportPending;

            var processor = new DownloadProcessingService(Mocker.GetMock<NzbDrone.Core.Configuration.IConfigService>().Object,
                                                          Subject,
                                                          Mocker.GetMock<IFailedDownloadService>().Object,
                                                          Mocker.GetMock<ITrackedDownloadService>().Object,
                                                          Mocker.GetMock<IEventAggregator>().Object,
                                                          TestLogger);

            processor.Execute(new ProcessMonitoredDownloadsCommand());
            processor.Execute(new ProcessMonitoredDownloadsCommand());
        }

        private static readonly object[] UnusablePayloads =
        {
            new object[] { new[] { "Series v01.epub" } },
            new object[] { new[] { "Series v01 - v03.m4b" } },
            new object[] { new[] { "Marriage Toxin Chapter 1.cbz", "Marriage Toxin Chapter 2.cbz", "Marriage Toxin Chapter 3.cbz" } }
        };

        [TestCaseSource(nameof(UnusablePayloads))]
        public void unusable_payload_without_grabbed_history_is_judged_once_over_two_refreshes(string[] files)
        {
            GivenNothingImportedFrom(files);

            ProcessMonitoredDownloadsTwice();

            var outputPath = _trackedDownload.DownloadItem.OutputPath.FullPath;
            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFiles(outputPath, true), Times.Once());
            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportFailed);

            ExceptionVerification.IgnoreWarns();
        }

        [TestCaseSource(nameof(UnusablePayloads))]
        public void unusable_grabbed_payload_is_failed_and_removed_once_over_two_refreshes(string[] files)
        {
            GivenGrabbedHistory();
            GivenNothingImportedFrom(files);

            Mocker.GetMock<IDownloadClient>()
                  .SetupGet(c => c.Definition)
                  .Returns(new DownloadClientDefinition { Id = 1, Name = "testClient", RemoveFailedDownloads = true });

            ProcessMonitoredDownloadsTwice();

            Mocker.GetMock<IFailedDownloadService>().Verify(v => v.MarkAsFailed(_trackedDownload.DownloadItem.DownloadId, false, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IDownloadClient>().Verify(v => v.RemoveItem(It.IsAny<DownloadClientItem>(), true), Times.Once());
            _trackedDownload.State.Should().Be(TrackedDownloadState.DownloadFailed);

            ExceptionVerification.IgnoreWarns();
        }

        // Covered volumes (2026-09-17, D4): a spanning audiobook -- one audio release mapped to several
        // volumes whose files all landed on one of them -- covers the others, and the covered volumes
        // count as imported. Manga and ebook releases never enter.
        [Test]
        public void a_spanning_audio_import_marks_the_other_volumes_covered_and_completes()
        {
            var (vol1, vol2) = GivenSpanningAudioGrab();
            GivenMarksAreWritten();
            GivenImported(new[] { "The Beginning After the End Vol. 01-02.m4b" }, vol1);

            Subject.Import(_trackedDownload);

            Mocker.GetMock<ICoveredVolumeService>()
                .Verify(v => v.MarkCoveredByImport(vol1, It.Is<IEnumerable<Book>>(c => c.Single() == vol2), "The Beginning After the End Vol. 01-02 [M4B]"), Times.Once());

            AssertImported();
        }

        // "Covered by Vol. c" means inside that volume's file, whichever way identification landed the pack.
        [Test]
        public void a_spanning_audio_import_landing_on_the_higher_volume_covers_the_lower_one()
        {
            var (vol1, vol2) = GivenSpanningAudioGrab();
            GivenMarksAreWritten();
            GivenImported(new[] { "The Beginning After the End Vol. 01-02.m4b" }, vol2);

            Subject.Import(_trackedDownload);

            Mocker.GetMock<ICoveredVolumeService>()
                .Verify(v => v.MarkCoveredByImport(vol2, It.Is<IEnumerable<Book>>(c => c.Single() == vol1), "The Beginning After the End Vol. 01-02 [M4B]"), Times.Once());

            AssertImported();
        }

        [Test]
        public void an_import_with_one_file_per_volume_marks_nothing()
        {
            var (vol1, vol2) = GivenSpanningAudioGrab();
            GivenImported(new[] { "The Beginning After the End Vol. 01.m4b", "The Beginning After the End Vol. 02.m4b" }, vol1, vol2);

            Subject.Import(_trackedDownload);

            Mocker.GetMock<ICoveredVolumeService>()
                .Verify(v => v.MarkCoveredByImport(It.IsAny<Book>(), It.IsAny<IEnumerable<Book>>(), It.IsAny<string>()), Times.Never());

            AssertImported();
        }

        [Test]
        public void an_ebook_spanning_import_marks_nothing()
        {
            var (vol1, _) = GivenSpanningAudioGrab(MediaType.Ebook);
            var results = GivenImported(new[] { "The Beginning After the End Vol. 01-02.epub" }, vol1);

            Subject.Import(_trackedDownload);

            Mocker.GetMock<ICoveredVolumeService>()
                .Verify(v => v.MarkCoveredByImport(It.IsAny<Book>(), It.IsAny<IEnumerable<Book>>(), It.IsAny<string>()), Times.Never());

            // one of two mapped books imported: not verified, as today
            Subject.VerifyImport(_trackedDownload, results).Should().BeFalse();
            Mocker.GetMock<IEditionService>().Verify(v => v.GetEditionsByBook(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void a_manga_pack_marks_nothing()
        {
            var vol1 = CreateBook(1);
            var vol2 = CreateBook(2);
            _trackedDownload.RemoteBook.Books = new List<Book> { vol1, vol2 };
            _trackedDownload.RemoteBook.MediaType = MediaType.Archive;
            _trackedDownload.DownloadItem.Title = "Series v01-02";

            var results = GivenImported(new[] { "Series v01-02.cbz" }, vol1);

            Subject.Import(_trackedDownload);

            Mocker.GetMock<ICoveredVolumeService>()
                .Verify(v => v.MarkCoveredByImport(It.IsAny<Book>(), It.IsAny<IEnumerable<Book>>(), It.IsAny<string>()), Times.Never());

            Subject.VerifyImport(_trackedDownload, results).Should().BeFalse();
            Mocker.GetMock<IEditionService>().Verify(v => v.GetEditionsByBook(It.IsAny<int>()), Times.Never());
        }

        // The release must SAY it spans (review I1): a tokenless title maps to every released volume,
        // and a single Book 1 audiobook must not cover the series. The download completes as today.
        [Test]
        public void a_tokenless_spanning_audio_grab_marks_nothing()
        {
            var (vol1, _) = GivenSpanningAudioGrab();
            _trackedDownload.DownloadItem.Title = "The Beginning After the End - TurtleMe [M4B]";
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<EntityHistory> { new EntityHistory { EventType = EntityHistoryEventType.Grabbed, SourceTitle = "The Beginning After the End - TurtleMe [M4B]" } });

            GivenImported(new[] { "The Beginning After the End - TurtleMe.m4b" }, vol1);

            Subject.Import(_trackedDownload);

            Mocker.GetMock<ICoveredVolumeService>()
                .Verify(v => v.MarkCoveredByImport(It.IsAny<Book>(), It.IsAny<IEnumerable<Book>>(), It.IsAny<string>()), Times.Never());

            // the existing all-files-imported branch, the other volume still wanted
            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }

        [Test]
        public void a_range_in_the_grab_source_title_counts_when_the_client_title_is_tokenless()
        {
            var (vol1, vol2) = GivenSpanningAudioGrab();
            GivenMarksAreWritten();
            _trackedDownload.DownloadItem.Title = "tbate-pack";
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<EntityHistory> { new EntityHistory { EventType = EntityHistoryEventType.Grabbed, SourceTitle = "The Beginning After the End Vol. 01-02 [M4B]" } });

            GivenImported(new[] { "tbate-pack.m4b" }, vol1);

            Subject.Import(_trackedDownload);

            Mocker.GetMock<ICoveredVolumeService>()
                .Verify(v => v.MarkCoveredByImport(vol1, It.Is<IEnumerable<Book>>(c => c.Single() == vol2), "tbate-pack"), Times.Once());

            AssertImported();
        }

        // The mark set is bounded by the parsed range, not the mapping (T5 re-review 1): a mapping
        // wider than what the title says — a re-grab under one download id whose older Grabbed row
        // was ranged, say — marks only the volumes inside the range, as the copy-in does.
        [Test]
        public void a_mapping_wider_than_the_parsed_range_marks_only_the_range()
        {
            var (vol1, vol2) = GivenSpanningAudioGrab();
            GivenMarksAreWritten();
            var vol3 = new Book { Id = 3, VolumeNumber = 3, AuthorMetadataId = 3, Title = "TBATE Vol. 3" };
            vol3.WithEdition(MediaType.Ebook);
            vol3.WithEdition(MediaType.Audio);
            _trackedDownload.RemoteBook.Books.Add(vol3);

            GivenImported(new[] { "The Beginning After the End Vol. 01-02.m4b" }, vol1);

            Subject.Import(_trackedDownload);

            Mocker.GetMock<ICoveredVolumeService>()
                .Verify(v => v.MarkCoveredByImport(vol1, It.Is<IEnumerable<Book>>(c => c.SequenceEqual(new[] { vol2 })), "The Beginning After the End Vol. 01-02 [M4B]"), Times.Once());
        }

        // Every file must have imported (review I2): a rejected second file's volume is inside nothing,
        // and a mark on it would hide the rejection. The download fails as it did before marks.
        [Test]
        public void a_partly_rejected_spanning_import_marks_nothing_and_fails_as_before()
        {
            var (vol1, _) = GivenSpanningAudioGrab();
            GivenPayloadFiles("The Beginning After the End Vol. 01.m4b", "The Beginning After the End Vol. 02.m4b");

            var outputPath = _trackedDownload.DownloadItem.OutputPath.FullPath;
            var results = new List<ImportResult>
            {
                new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = Path.Combine(outputPath, "The Beginning After the End Vol. 01.m4b"), Author = _author, Book = vol1 })),
                new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = Path.Combine(outputPath, "The Beginning After the End Vol. 02.m4b"), Author = _author }, new Rejection("Unable to identify")), "Unable to identify")
            };

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns(results);

            Subject.Import(_trackedDownload);

            Mocker.GetMock<ICoveredVolumeService>()
                .Verify(v => v.MarkCoveredByImport(It.IsAny<Book>(), It.IsAny<IEnumerable<Book>>(), It.IsAny<string>()), Times.Never());

            Subject.VerifyImport(_trackedDownload, results).Should().BeFalse();
            AssertNotImported();
        }

        [Test]
        public void verify_import_counts_a_covered_volume_as_imported()
        {
            var (vol1, vol2) = GivenSpanningAudioGrab();
            vol2.EditionOf(MediaType.Audio).CoveredByVolume = 1;

            var results = GivenImported(new[] { "The Beginning After the End Vol. 01-02.m4b" }, vol1);

            Subject.VerifyImport(_trackedDownload, results).Should().BeTrue();

            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Once());
        }

        [Test]
        public void verify_import_ignores_a_mark_by_a_volume_this_download_did_not_import()
        {
            var (vol1, vol2) = GivenSpanningAudioGrab();
            vol2.EditionOf(MediaType.Audio).CoveredByVolume = 3;

            var results = GivenImported(new[] { "The Beginning After the End Vol. 01-02.m4b" }, vol1);

            Subject.VerifyImport(_trackedDownload, results).Should().BeFalse();
        }

        // Release-trusted import (2026-09-18, B3b): the grab's book reaches the import as the known
        // book. One mapped book: that book (as before). Several mapped books of an Audio, light-novel
        // download: the one whose volume the download title names -- or, with one payload file, the
        // file name -- else null. A range is a pack, not a volume; manga packs stay null.
        private List<Book> GivenLightNovelAudioGrab(params int[] volumes)
        {
            var books = volumes.Select(n =>
            {
                var book = new Book { Id = n, VolumeNumber = n, AuthorMetadataId = 3, Title = $"TBATE Vol. {n}" };
                book.WithEdition(MediaType.Ebook);
                book.WithEdition(MediaType.Audio);
                return book;
            }).ToList();

            _trackedDownload.RemoteBook.Author.Metadata.Value.ForeignAuthorId = "local-the-beginning-after-the-end~ln";
            _trackedDownload.RemoteBook.Books = books;
            _trackedDownload.RemoteBook.MediaType = MediaType.Audio;

            return books;
        }

        private void VerifyKnownBook(Book expected)
        {
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Verify(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.Is<Book>(b => (expected == null && b == null) || (expected != null && b != null && b.Id == expected.Id))), Times.Once());
        }

        [Test]
        public void a_single_book_grab_still_passes_that_book_as_the_known_book()
        {
            GivenNothingImportedFrom("Series v01.cbz");

            Subject.Import(_trackedDownload);

            VerifyKnownBook(_trackedDownload.RemoteBook.Books.Single());
        }

        [Test]
        public void a_multi_volume_audio_grab_passes_the_volume_the_title_names_as_the_known_book()
        {
            var books = GivenLightNovelAudioGrab(6, 7, 8, 9);
            _trackedDownload.DownloadItem.Title = "The Beginning After the End Vol 6 [M4B]";
            GivenNothingImportedFrom("The Beginning After the End Vol 6.mp4", "cover.jpg");

            Subject.Import(_trackedDownload);

            VerifyKnownBook(books.Single(b => b.VolumeNumber == 6));
        }

        [Test]
        public void a_multi_volume_audio_grab_with_no_parsable_volume_passes_no_known_book()
        {
            GivenLightNovelAudioGrab(6, 7, 8, 9);
            _trackedDownload.DownloadItem.Title = "The Beginning After the End - TurtleMe [M4B]";
            GivenNothingImportedFrom("The Beginning After the End - TurtleMe.m4b", "The Beginning After the End - TurtleMe (2).m4b");

            Subject.Import(_trackedDownload);

            VerifyKnownBook(null);
        }

        [Test]
        public void a_single_audio_file_names_the_volume_when_the_title_does_not()
        {
            var books = GivenLightNovelAudioGrab(6, 7, 8, 9);
            _trackedDownload.DownloadItem.Title = "tbate-6";
            GivenNothingImportedFrom("The Beginning After the End Vol 6.m4b");

            Subject.Import(_trackedDownload);

            VerifyKnownBook(books.Single(b => b.VolumeNumber == 6));
        }

        [Test]
        public void a_single_audio_file_beside_cover_art_names_the_volume_when_the_title_does_not()
        {
            // The ordinary audiobook payload: one media file plus cover art.
            var books = GivenLightNovelAudioGrab(6, 7, 8, 9);
            _trackedDownload.DownloadItem.Title = "tbate-6";
            GivenNothingImportedFrom("The Beginning After the End Vol 6.m4b", "cover.jpg");

            Subject.Import(_trackedDownload);

            VerifyKnownBook(books.Single(b => b.VolumeNumber == 6));
        }

        [Test]
        public void two_audio_files_under_a_tokenless_title_pass_no_known_book()
        {
            GivenLightNovelAudioGrab(6, 7, 8, 9);
            _trackedDownload.DownloadItem.Title = "tbate-pack";
            GivenNothingImportedFrom("The Beginning After the End Vol 6.m4b", "The Beginning After the End Vol 7.m4b", "cover.jpg");

            Subject.Import(_trackedDownload);

            VerifyKnownBook(null);
        }

        [Test]
        public void a_ranged_title_passes_no_known_book_even_with_one_file()
        {
            // A title that says it spans is a pack; the file name is not consulted (the B2 marks own it).
            GivenLightNovelAudioGrab(1, 2);
            _trackedDownload.DownloadItem.Title = "The Beginning After the End Vol 1-2 [M4B]";
            GivenNothingImportedFrom("The Beginning After the End Vol 1.m4b");

            Subject.Import(_trackedDownload);

            VerifyKnownBook(null);
        }

        [Test]
        public void a_title_naming_a_volume_outside_the_mapping_passes_no_known_book()
        {
            GivenLightNovelAudioGrab(6, 7, 8, 9);
            _trackedDownload.DownloadItem.Title = "The Beginning After the End Vol 3 [M4B]";
            GivenNothingImportedFrom("The Beginning After the End Vol 3.m4b");

            Subject.Import(_trackedDownload);

            VerifyKnownBook(null);
        }

        [Test]
        public void a_title_naming_a_volume_two_mapped_books_carry_passes_no_known_book()
        {
            var books = GivenLightNovelAudioGrab(6, 7);
            books[1].VolumeNumber = 6;
            _trackedDownload.DownloadItem.Title = "The Beginning After the End Vol 6 [M4B]";
            GivenNothingImportedFrom("The Beginning After the End Vol 6.m4b");

            Subject.Import(_trackedDownload);

            VerifyKnownBook(null);
        }

        [Test]
        public void an_ebook_multi_volume_grab_passes_no_known_book()
        {
            GivenLightNovelAudioGrab(6, 7, 8, 9);
            _trackedDownload.RemoteBook.MediaType = MediaType.Ebook;
            _trackedDownload.DownloadItem.Title = "The Beginning After the End Vol 6 [EPUB]";
            GivenNothingImportedFrom("The Beginning After the End Vol 6.epub");

            Subject.Import(_trackedDownload);

            VerifyKnownBook(null);
        }

        [Test]
        public void a_manga_pack_passes_no_known_book_whatever_the_title_says()
        {
            _trackedDownload.RemoteBook.Books = new List<Book> { CreateBook(1), CreateBook(2) };
            _trackedDownload.RemoteBook.Books[0].VolumeNumber = 1;
            _trackedDownload.RemoteBook.Books[1].VolumeNumber = 2;
            _trackedDownload.RemoteBook.MediaType = MediaType.Archive;
            _trackedDownload.DownloadItem.Title = "Series v01";
            GivenNothingImportedFrom("Series v01.cbz");

            Subject.Import(_trackedDownload);

            VerifyKnownBook(null);
        }

        private void AssertNotImported()
        {
            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Never());

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportFailed);
        }

        private void AssertImported()
        {
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Verify(v => v.ProcessPath(_trackedDownload.DownloadItem.OutputPath.FullPath, ImportMode.Auto, _trackedDownload.RemoteBook.Author, _trackedDownload.DownloadItem, It.IsAny<Book>()), Times.Once());

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Once());

            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }
    }
}
