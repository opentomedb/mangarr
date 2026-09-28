using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class DownloadedTracksImportServiceFixture : FileSystemTest<DownloadedBooksImportService>
    {
        private string _droneFactory = "c:\\drop\\".AsOsAgnostic();
        private string[] _subFolders = new[] { "c:\\drop\\foldername".AsOsAgnostic() };
        private string[] _audioFiles = new[] { "c:\\drop\\foldername\\01 the first track.ext".AsOsAgnostic() };

        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            GivenAudioFiles(_audioFiles, 10);

            Mocker.GetMock<IDiskScanService>().Setup(c => c.GetBookFiles(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(_audioFiles.Select(x => DiskProvider.GetFileInfo(x)).ToArray());

            Mocker.GetMock<IDiskScanService>().Setup(c => c.FilterFiles(It.IsAny<string>(), It.IsAny<IEnumerable<IFileInfo>>()))
                  .Returns<string, IEnumerable<IFileInfo>>((b, s) => s.ToList());

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), true, null, ImportMode.Auto))
                  .Returns(new List<ImportResult>());

            var downloadItem = Builder<DownloadClientItem>.CreateNew()
                .With(v => v.DownloadId = "sab1")
                .With(v => v.Status = DownloadItemStatus.Downloading)
                .Build();

            var remoteBook = Builder<RemoteBook>.CreateNew()
                .With(v => v.Author = new Author())
                .Build();

            _trackedDownload = new TrackedDownload
            {
                DownloadItem = downloadItem,
                RemoteBook = remoteBook,
                State = TrackedDownloadState.Downloading
            };
        }

        private void GivenAudioFiles(string[] files, long filesize)
        {
            foreach (var file in files)
            {
                FileSystem.AddFile(file, new MockFileData("".PadRight((int)filesize)));
            }
        }

        private void GivenValidAuthor()
        {
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetAuthor(It.IsAny<string>()))
                  .Returns(Builder<Author>.CreateNew().Build());
        }

        private void GivenSuccessfulImport()
        {
            var localTrack = new LocalBook();

            var imported = new List<ImportDecision<LocalBook>>();
            imported.Add(new ImportDecision<LocalBook>(localTrack));

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Returns(imported);

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                  .Returns(imported.Select(i => new ImportResult(i)).ToList())
                  .Callback(() => WasImportedResponse());
        }

        private void WasImportedResponse()
        {
            Mocker.GetMock<IDiskScanService>().Setup(c => c.GetBookFiles(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(new IFileInfo[0]);
        }

        [Test]
        public void should_search_for_author_using_folder_name()
        {
            Subject.ProcessRootFolder(DiskProvider.GetDirectoryInfo(_droneFactory));

            Mocker.GetMock<IParsingService>().Verify(c => c.GetAuthor("foldername"), Times.Once());
        }

        [Test]
        public void should_skip_if_file_is_in_use_by_another_process()
        {
            GivenValidAuthor();

            foreach (var file in _audioFiles)
            {
                FileSystem.AddFile(file, new MockFileData("".PadRight(10)) { AllowedFileShare = FileShare.None });
            }

            Subject.ProcessRootFolder(DiskProvider.GetDirectoryInfo(_droneFactory));

            VerifyNoImport();
        }

        [Test]
        public void should_not_skip_if_no_author_found()
        {
            Mocker.GetMock<IParsingService>().Setup(c => c.GetAuthor("foldername")).Returns((Author)null);

            Subject.ProcessRootFolder(DiskProvider.GetDirectoryInfo(_droneFactory));

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(c => c.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()),
                          Times.Once());

            VerifyImport();
        }

        [Test]
        public void should_not_import_if_folder_is_a_author_path()
        {
            GivenValidAuthor();

            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.AuthorPathExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskScanService>()
                  .Setup(c => c.GetBookFiles(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(new IFileInfo[0]);

            Subject.ProcessRootFolder(DiskProvider.GetDirectoryInfo(_droneFactory));

            Mocker.GetMock<IDiskScanService>()
                  .Verify(v => v.GetBookFiles(It.IsAny<string>(), true), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_delete_folder_if_no_files_were_imported()
        {
            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), false, null, ImportMode.Auto))
                  .Returns(new List<ImportResult>());

            Subject.ProcessRootFolder(DiskProvider.GetDirectoryInfo(_droneFactory));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.GetFolderSize(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_delete_folder_if_files_were_imported_and_audio_files_remain()
        {
            GivenValidAuthor();

            var localTrack = new LocalBook();

            var imported = new List<ImportDecision<LocalBook>>();
            imported.Add(new ImportDecision<LocalBook>(localTrack));

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Returns(imported);

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), true, null, ImportMode.Auto))
                  .Returns(imported.Select(i => new ImportResult(i)).ToList());

            Subject.ProcessRootFolder(DiskProvider.GetDirectoryInfo(_droneFactory));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>(), true), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase("_UNPACK_")]
        [TestCase("_FAILED_")]
        public void should_remove_unpack_from_folder_name(string prefix)
        {
            var folderName = "Alien Ant Farm - Truant (2003)";
            FileSystem.AddDirectory(string.Format(@"C:\drop\{0}{1}", prefix, folderName).AsOsAgnostic());

            Subject.ProcessRootFolder(DiskProvider.GetDirectoryInfo(_droneFactory));

            Mocker.GetMock<IParsingService>()
                .Verify(v => v.GetAuthor(folderName), Times.Once());

            Mocker.GetMock<IParsingService>()
                .Verify(v => v.GetAuthor(It.Is<string>(s => s.StartsWith(prefix))), Times.Never());
        }

        [Test]
        public void should_return_importresult_on_unknown_author()
        {
            var fileName = @"C:\folder\file.mkv".AsOsAgnostic();
            FileSystem.AddFile(fileName, new MockFileData(string.Empty));

            var result = Subject.ProcessPath(fileName);

            result.Should().HaveCount(1);
            result.First().ImportDecision.Should().NotBeNull();
            result.First().ImportDecision.Item.Should().NotBeNull();
            result.First().ImportDecision.Item.Path.Should().Be(fileName);
            result.First().Result.Should().Be(ImportResultType.Rejected);
        }

        [Test]
        public void should_not_delete_if_there_is_large_rar_file()
        {
            GivenValidAuthor();

            var localTrack = new LocalBook();

            var imported = new List<ImportDecision<LocalBook>>();
            imported.Add(new ImportDecision<LocalBook>(localTrack));

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Returns(imported);

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), true, null, ImportMode.Auto))
                  .Returns(imported.Select(i => new ImportResult(i)).ToList());

            GivenAudioFiles(new[] { _audioFiles.First().Replace(".ext", ".rar") }, 15.Megabytes());

            Subject.ProcessRootFolder(DiskProvider.GetDirectoryInfo(_droneFactory));

            DiskProvider.FolderExists(_subFolders[0]).Should().BeTrue();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_process_if_file_and_folder_do_not_exist()
        {
            var folderName = @"C:\media\ba09030e-1234-1234-1234-123456789abc\[HorribleSubs] Maria the Virgin Witch - 09 [720p]".AsOsAgnostic();

            Subject.ProcessPath(folderName).Should().BeEmpty();

            Mocker.GetMock<IParsingService>()
                .Verify(v => v.GetAuthor(It.IsAny<string>()), Times.Never());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_delete_if_no_files_were_imported()
        {
            GivenValidAuthor();

            var localTrack = new LocalBook();

            var imported = new List<ImportDecision<LocalBook>>();
            imported.Add(new ImportDecision<LocalBook>(localTrack));

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Returns(imported);

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), true, null, ImportMode.Auto))
                  .Returns(new List<ImportResult>());

            Subject.ProcessRootFolder(DiskProvider.GetDirectoryInfo(_droneFactory));

            DiskProvider.FolderExists(_subFolders[0]).Should().BeTrue();

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>(), true), Times.Never());
        }

        [Test]
        public void should_not_delete_folder_after_import()
        {
            GivenValidAuthor();

            GivenSuccessfulImport();

            _trackedDownload.DownloadItem.CanMoveFiles = false;

            Subject.ProcessPath(_droneFactory, ImportMode.Auto, _trackedDownload.RemoteBook.Author, _trackedDownload.DownloadItem);

            DiskProvider.FolderExists(_subFolders[0]).Should().BeTrue();
        }

        [Test]
        public void should_delete_folder_if_importmode_move()
        {
            GivenValidAuthor();

            GivenSuccessfulImport();

            _trackedDownload.DownloadItem.CanMoveFiles = false;

            Subject.ProcessPath(_droneFactory, ImportMode.Move, _trackedDownload.RemoteBook.Author, _trackedDownload.DownloadItem);

            DiskProvider.FolderExists(_subFolders[0]).Should().BeFalse();
        }

        [Test]
        public void should_not_delete_folder_if_importmode_copy()
        {
            GivenValidAuthor();

            GivenSuccessfulImport();

            _trackedDownload.DownloadItem.CanMoveFiles = true;

            Subject.ProcessPath(_droneFactory, ImportMode.Copy, _trackedDownload.RemoteBook.Author, _trackedDownload.DownloadItem);

            DiskProvider.FolderExists(_subFolders[0]).Should().BeTrue();
        }

        [Test]
        public void single_file_grab_matches_the_edition_of_the_files_class()
        {
            // Light novels (2026-09): the grab knows the volume; the file's class picks the edition.
            var author = Builder<Author>.CreateNew().Build();
            var volume = new Book { Id = 5 };
            var ebook = volume.WithEdition(MediaType.Ebook, id: 51);
            var audio = volume.WithEdition(MediaType.Audio, id: 52);

            IdentificationOverrides seen = null;

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((f, o, i, c) => seen = o)
                  .Returns(new List<ImportDecision<LocalBook>>());

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                  .Returns(new List<ImportResult>());

            var epubPath = @"c:\drop\Overlord - Vol 005.epub".AsOsAgnostic();
            var m4bPath = @"c:\drop\Overlord - Vol 005.m4b".AsOsAgnostic();
            GivenAudioFiles(new[] { epubPath, m4bPath }, 10);

            Subject.ProcessPath(epubPath, ImportMode.Auto, author, _trackedDownload.DownloadItem, volume);
            seen.Book.Should().BeSameAs(volume);
            seen.Edition.Should().BeSameAs(ebook);

            Subject.ProcessPath(m4bPath, ImportMode.Auto, author, _trackedDownload.DownloadItem, volume);
            seen.Edition.Should().BeSameAs(audio);
        }

        [Test]
        public void single_file_grab_of_a_class_the_volume_lacks_falls_back_to_identification()
        {
            var author = Builder<Author>.CreateNew().Build();
            var manga = new Book { Id = 6 };
            manga.WithEdition(MediaType.Archive, id: 61);

            IdentificationOverrides seen = null;

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((f, o, i, c) => seen = o)
                  .Returns(new List<ImportDecision<LocalBook>>());

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                  .Returns(new List<ImportResult>());

            var epubPath = @"c:\drop\Dandadan - Vol 001.epub".AsOsAgnostic();
            GivenAudioFiles(new[] { epubPath }, 10);

            Subject.ProcessPath(epubPath, ImportMode.Auto, author, _trackedDownload.DownloadItem, manga);

            seen.Book.Should().BeNull();
            seen.Edition.Should().BeNull();
            seen.Author.Should().BeSameAs(author);
        }

        // Release-trusted import (2026-09-18): a light-novel FOLDER download carries the grab's book
        // to the identification override too, the edition picked by the class of the folder's
        // files -- the TBATE .mp4 releases came as folders and never reached the override. A manga
        // folder keeps per-file identification (D7: manga byte-identical).
        private static Author LightNovelAuthor()
        {
            return new Author { Id = 3, Name = "The Beginning After the End", Metadata = new AuthorMetadata { Name = "The Beginning After the End", ForeignAuthorId = "local-the-beginning-after-the-end~ln" } };
        }

        private static Author MangaAuthor()
        {
            return new Author { Id = 4, Name = "Dandadan", Metadata = new AuthorMetadata { Name = "Dandadan", ForeignAuthorId = "local-dandadan" } };
        }

        private IdentificationOverrides GivenFolderImportCapturingOverrides(string folder, params string[] files)
        {
            var seen = new IdentificationOverrides();

            // a test that sized its files first keeps those sizes
            GivenAudioFiles(files.Where(f => !FileSystem.FileExists(f)).ToArray(), 10);

            Mocker.GetMock<IDiskScanService>().Setup(c => c.GetBookFiles(folder, It.IsAny<bool>()))
                .Returns(files.Select(x => DiskProvider.GetFileInfo(x)).ToArray());

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((f, o, i, c) =>
                  {
                      seen.Author = o.Author;
                      seen.Book = o.Book;
                      seen.Edition = o.Edition;
                  })
                  .Returns(new List<ImportDecision<LocalBook>>());

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                  .Returns(new List<ImportResult>());

            return seen;
        }

        [Test]
        public void folder_grab_matches_the_edition_of_the_folders_files_class()
        {
            var author = LightNovelAuthor();
            var volume = new Book { Id = 6 };
            var ebook = volume.WithEdition(MediaType.Ebook, id: 61);
            var audio = volume.WithEdition(MediaType.Audio, id: 62);

            var audioFolder = @"c:\drop\The Beginning After the End Vol 6 [M4B]".AsOsAgnostic();
            var seen = GivenFolderImportCapturingOverrides(audioFolder, Path.Combine(audioFolder, "The Beginning After the End Vol 6.m4b"));

            Subject.ProcessPath(audioFolder, ImportMode.Auto, author, _trackedDownload.DownloadItem, volume);

            seen.Author.Should().BeSameAs(author);
            seen.Book.Should().BeSameAs(volume);
            seen.Edition.Should().BeSameAs(audio);

            var epubFolder = @"c:\drop\The Beginning After the End Vol 6 [EPUB]".AsOsAgnostic();
            seen = GivenFolderImportCapturingOverrides(epubFolder, Path.Combine(epubFolder, "The Beginning After the End Vol 6.epub"));

            Subject.ProcessPath(epubFolder, ImportMode.Auto, author, _trackedDownload.DownloadItem, volume);

            seen.Book.Should().BeSameAs(volume);
            seen.Edition.Should().BeSameAs(ebook);
        }

        [Test]
        public void folder_grab_bundling_an_epub_with_the_audiobook_matches_the_audio_edition()
        {
            var author = LightNovelAuthor();
            var volume = new Book { Id = 4 };
            volume.WithEdition(MediaType.Ebook, id: 41);
            var audio = volume.WithEdition(MediaType.Audio, id: 42);

            var folder = @"c:\drop\[PZG].Classroom.of.the.Elite,.Vol..04. Audiobook .[Seven.Seas.Siren]-xpost".AsOsAgnostic();
            var epub = Path.Combine(folder, "Classroom of the Elite - Volume 04 [Seven Seas] [LuCaZ].epub");
            var m4b = Path.Combine(folder, "Classroom of the Elite, Vol. 04 [PZG].m4b");
            // the audiobook is far bigger than the bundled EPUB that sorts first
            FileSystem.AddFile(epub, new MockFileData("".PadRight(100)));
            FileSystem.AddFile(m4b, new MockFileData("".PadRight(5000)));

            var seen = GivenFolderImportCapturingOverrides(folder, epub, m4b);

            Subject.ProcessPath(folder, ImportMode.Auto, author, _trackedDownload.DownloadItem, volume);

            seen.Edition.Should().BeSameAs(audio);
        }

        [Test]
        public void folder_grab_without_a_known_book_keeps_the_author_only()
        {
            var author = LightNovelAuthor();

            var folder = @"c:\drop\The Beginning After the End Vol 6 [M4B]".AsOsAgnostic();
            var seen = GivenFolderImportCapturingOverrides(folder, Path.Combine(folder, "The Beginning After the End Vol 6.m4b"));

            Subject.ProcessPath(folder, ImportMode.Auto, author, _trackedDownload.DownloadItem);

            seen.Author.Should().BeSameAs(author);
            seen.Book.Should().BeNull();
            seen.Edition.Should().BeNull();
        }

        [Test]
        public void folder_grab_of_a_class_the_volume_lacks_falls_back_to_identification()
        {
            var author = LightNovelAuthor();
            var audioOnly = new Book { Id = 7 };
            audioOnly.WithEdition(MediaType.Audio, id: 72);

            var folder = @"c:\drop\The Beginning After the End Vol 7 [EPUB]".AsOsAgnostic();
            var seen = GivenFolderImportCapturingOverrides(folder, Path.Combine(folder, "The Beginning After the End Vol 7.epub"));

            Subject.ProcessPath(folder, ImportMode.Auto, author, _trackedDownload.DownloadItem, audioOnly);

            seen.Author.Should().BeSameAs(author);
            seen.Book.Should().BeNull();
            seen.Edition.Should().BeNull();
        }

        [Test]
        public void manga_folder_grab_keeps_per_file_identification()
        {
            // D7: a manga folder with the grab's book still gets the Author override only.
            var author = MangaAuthor();
            var manga = new Book { Id = 8 };
            manga.WithEdition(MediaType.Archive, id: 81);

            var folder = @"c:\drop\Dandadan Vol 1".AsOsAgnostic();
            var seen = GivenFolderImportCapturingOverrides(folder, Path.Combine(folder, "Dandadan Vol 1.cbz"));

            Subject.ProcessPath(folder, ImportMode.Auto, author, _trackedDownload.DownloadItem, manga);

            seen.Author.Should().BeSameAs(author);
            seen.Book.Should().BeNull();
            seen.Edition.Should().BeNull();
        }

        private List<IdentificationOverrides> GivenImportCapturingEveryOverride()
        {
            var seen = new List<IdentificationOverrides>();

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(v => v.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((f, o, i, c) => seen.Add(o))
                  .Returns(new List<ImportDecision<LocalBook>>());

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                  .Returns(new List<ImportResult>());

            return seen;
        }

        // LN PDF (2026-09-22): a light novel's PDF is its ebook, so the grab's volume is matched on
        // its Ebook edition -- folder and single file alike. A manga PDF keeps its Archive edition.
        [Test]
        public void folder_grab_of_a_light_novel_pdf_matches_the_ebook_edition()
        {
            var author = LightNovelAuthor();
            var volume = new Book { Id = 9 };
            var ebook = volume.WithEdition(MediaType.Ebook, id: 91);
            volume.WithEdition(MediaType.Audio, id: 92);

            var folder = @"c:\drop\The Beginning After the End Vol 9 [PDF]".AsOsAgnostic();
            var seen = GivenFolderImportCapturingOverrides(folder, Path.Combine(folder, "The Beginning After the End Vol 9.pdf"));

            Subject.ProcessPath(folder, ImportMode.Auto, author, _trackedDownload.DownloadItem, volume);

            seen.Book.Should().BeSameAs(volume);
            seen.Edition.Should().BeSameAs(ebook);
        }

        [Test]
        public void single_file_grab_of_a_light_novel_pdf_matches_the_ebook_edition()
        {
            var author = LightNovelAuthor();
            var volume = new Book { Id = 9 };
            var ebook = volume.WithEdition(MediaType.Ebook, id: 91);
            volume.WithEdition(MediaType.Audio, id: 92);
            var seen = GivenImportCapturingEveryOverride();

            var pdfPath = @"c:\drop\The Beginning After the End - Vol 009.pdf".AsOsAgnostic();
            GivenAudioFiles(new[] { pdfPath }, 10);

            Subject.ProcessPath(pdfPath, ImportMode.Auto, author, _trackedDownload.DownloadItem, volume);

            seen.Single().Edition.Should().BeSameAs(ebook);
        }

        [Test]
        public void single_file_grab_of_a_manga_pdf_matches_the_archive_edition()
        {
            var author = MangaAuthor();
            var manga = new Book { Id = 8 };
            var archive = manga.WithEdition(MediaType.Archive, id: 81);
            var seen = GivenImportCapturingEveryOverride();

            var pdfPath = @"c:\drop\Dandadan - Vol 001.pdf".AsOsAgnostic();
            GivenAudioFiles(new[] { pdfPath }, 10);

            Subject.ProcessPath(pdfPath, ImportMode.Auto, author, _trackedDownload.DownloadItem, manga);

            seen.Single().Edition.Should().BeSameAs(archive);
        }

        private void VerifyNoImport()
        {
            Mocker.GetMock<IImportApprovedBooks>().Verify(c => c.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), true, null, ImportMode.Auto),
                Times.Never());
        }

        private void VerifyImport()
        {
            Mocker.GetMock<IImportApprovedBooks>().Verify(c => c.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), true, null, ImportMode.Auto),
                Times.Once());
        }
    }
}
