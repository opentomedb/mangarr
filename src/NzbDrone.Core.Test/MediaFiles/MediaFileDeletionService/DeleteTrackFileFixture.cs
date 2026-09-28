using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileDeletionService
{
    [TestFixture]
    public class DeleteTrackFileFixture : CoreTest<Core.MediaFiles.MediaFileDeletionService>
    {
        private static readonly string RootFolder = @"C:\Test\Music";
        private Author _author;
        private BookFile _trackFile;
        private CalibreSettings _config;
        private MemoryTarget _log;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateNew()
                                     .With(s => s.Path = Path.Combine(RootFolder, "Author Name"))
                                     .Build();

            _trackFile = Builder<BookFile>.CreateNew()
                                               .With(f => f.Path = "/Author Name - Track01")
                                               .With(f => f.Home = FileHome.Entry)
                                               .With(f => f.Adopted = false)
                                               .Build();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetParentFolder(_author.Path))
                  .Returns(RootFolder);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetParentFolder(_trackFile.Path))
                  .Returns(_author.Path);

            // One copy each (2026-09-20): calibre settings for a Calibre-homed row come from config
            _config = new CalibreSettings { Host = "calibre.test", Port = 8081, Library = "books" };

            Mocker.GetMock<ILightNovelCalibreSettings>()
                  .Setup(s => s.ForConfig())
                  .Returns(_config);

            _log = new MemoryTarget("media-file-deletion") { Layout = "${level}|${message}" };
            LogManager.Configuration.AddTarget(_log.Name, _log);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Info, _log));
            LogManager.ReconfigExistingLoggers();
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var rule in LogManager.Configuration.LoggingRules.Where(r => r.Targets.Contains(_log)).ToList())
            {
                LogManager.Configuration.LoggingRules.Remove(rule);
            }

            LogManager.Configuration.RemoveTarget(_log.Name);
            LogManager.ReconfigExistingLoggers();
        }

        private BookFile GivenOffEntryFile(FileHome home, bool adopted, string path = null, int calibreId = 12)
        {
            var file = new BookFile
            {
                Id = 42,
                Path = path ?? (home == FileHome.Calibre
                    ? "/books/Reki Kawahara/Sword Art Online 2 (12)/Sword Art Online 2 - Reki Kawahara.epub"
                    : "/srv/audiobooks/Sword Art Online/Sword Art Online - Vol. 3/Sword Art Online - Vol 003.m4b"),
                Home = home,
                Adopted = adopted,
                CalibreId = home == FileHome.Calibre ? calibreId : 0
            };

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(file.Path))
                  .Returns(true);

            return file;
        }

        private void GivenRootFolderExists()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(RootFolder))
                  .Returns(true);
        }

        private void GivenRootFolderHasFolders()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetDirectories(RootFolder))
                  .Returns(new[] { _author.Path });
        }

        private void GivenAuthorFolderExists()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(_author.Path))
                  .Returns(true);
        }

        private void GivenNonCalibreRootFolder()
        {
            Mocker.GetMock<IRootFolderService>()
                .Setup(x => x.GetBestRootFolder(It.IsAny<string>()))
                .Returns(new RootFolder());
        }

        [Test]
        public void should_throw_if_root_folder_does_not_exist()
        {
            Assert.Throws<NzbDroneClientException>(() => Subject.DeleteTrackFile(_author, _trackFile));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_throw_if_root_folder_is_empty()
        {
            GivenRootFolderExists();
            Assert.Throws<NzbDroneClientException>(() => Subject.DeleteTrackFile(_author, _trackFile));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_delete_from_db_if_author_folder_does_not_exist()
        {
            GivenRootFolderExists();
            GivenRootFolderHasFolders();

            Subject.DeleteTrackFile(_author, _trackFile);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_trackFile, DeleteMediaFileReason.Manual), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(_trackFile.Path, It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_delete_from_db_if_track_file_does_not_exist()
        {
            GivenRootFolderExists();
            GivenRootFolderHasFolders();
            GivenAuthorFolderExists();

            Subject.DeleteTrackFile(_author, _trackFile);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_trackFile, DeleteMediaFileReason.Manual), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(_trackFile.Path, It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_delete_from_disk_and_db_if_track_file_exists()
        {
            GivenNonCalibreRootFolder();
            GivenRootFolderExists();
            GivenRootFolderHasFolders();
            GivenAuthorFolderExists();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(_trackFile.Path))
                  .Returns(true);

            Subject.DeleteTrackFile(_author, _trackFile);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(_trackFile.Path, "Author Name"), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_trackFile, DeleteMediaFileReason.Manual), Times.Once());
        }

        [Test]
        public void should_handle_error_deleting_track_file()
        {
            GivenNonCalibreRootFolder();
            GivenRootFolderExists();
            GivenRootFolderHasFolders();
            GivenAuthorFolderExists();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(_trackFile.Path))
                  .Returns(true);

            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(s => s.DeleteFile(_trackFile.Path, "Author Name"))
                  .Throws(new IOException());

            Assert.Throws<NzbDroneClientException>(() => Subject.DeleteTrackFile(_author, _trackFile));

            ExceptionVerification.ExpectedErrors(1);
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(_trackFile.Path, "Author Name"), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_trackFile, DeleteMediaFileReason.Manual), Times.Never());
        }
        // One copy each (2026-09-20): an off-entry row is deleted by its home, never through the
        // entry's root folder (its path is not under it); an adopted original is never touched.
        [Test]
        public void calibre_homed_file_is_deleted_through_calibre()
        {
            var file = GivenOffEntryFile(FileHome.Calibre, adopted: false);

            Subject.DeleteTrackFile(_author, file);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBook(file, _config), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(file, DeleteMediaFileReason.Manual), Times.Once());

            // never judged by the entry's root folder
            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetParentFolder(_author.Path), Times.Never());
            Mocker.GetMock<IRootFolderService>().Verify(v => v.GetBestRootFolder(It.IsAny<string>()), Times.Never());
        }

        // A calibre row's file is calibre's to remove: the ro mount may be absent, or calibre may
        // have renamed the file since the last scan, so its path is no reason to skip DeleteBook
        // (which would drop the row, send the volume back to Wanted, and land a second copy in
        // calibre). A refused DeleteBook throws and keeps the row.
        [Test]
        public void calibre_row_is_deleted_from_calibre_even_when_its_path_is_not_visible()
        {
            var file = GivenOffEntryFile(FileHome.Calibre, adopted: false);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(file.Path))
                  .Returns(false);

            Subject.DeleteTrackFile(_author, file);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBook(file, _config), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(file, DeleteMediaFileReason.Manual), Times.Once());
        }

        [Test]
        public void a_refused_calibre_delete_keeps_the_row()
        {
            var file = GivenOffEntryFile(FileHome.Calibre, adopted: false);

            Mocker.GetMock<ICalibreProxy>()
                  .Setup(s => s.DeleteBook(file, _config))
                  .Throws(new CalibreException("calibre refused"));

            Assert.Throws<NzbDroneClientException>(() => Subject.DeleteTrackFile(_author, file));

            ExceptionVerification.ExpectedErrors(1);
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBook(file, _config), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void adopted_calibre_row_whose_path_is_not_visible_is_forgotten_without_asking_calibre()
        {
            var file = GivenOffEntryFile(FileHome.Calibre, adopted: true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(file.Path))
                  .Returns(false);

            Subject.DeleteTrackFile(_author, file);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBook(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(file, DeleteMediaFileReason.Manual), Times.Once());
        }

        [Test]
        public void audiobook_homed_file_is_recycled_and_its_emptied_folder_removed()
        {
            var file = GivenOffEntryFile(FileHome.Audiobooks, adopted: false);
            var folder = "/srv/audiobooks/Sword Art Online/Sword Art Online - Vol. 3";

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(folder, true))
                  .Returns(new List<string>());

            Subject.DeleteTrackFile(_author, file);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(file.Path, "audiobooks"), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(folder, false), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBook(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(file, DeleteMediaFileReason.Manual), Times.Once());
            Mocker.GetMock<IRootFolderService>().Verify(v => v.GetBestRootFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void audiobook_folder_is_kept_while_other_files_remain_in_it()
        {
            var file = GivenOffEntryFile(FileHome.Audiobooks, adopted: false);
            var folder = "/srv/audiobooks/Sword Art Online/Sword Art Online - Vol. 3";

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(folder, true))
                  .Returns(new List<string> { Path.Combine(folder, "Sword Art Online - Vol 003 (part 2).m4b") });

            Subject.DeleteTrackFile(_author, file);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(file.Path, "audiobooks"), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(file, DeleteMediaFileReason.Manual), Times.Once());
        }

        [TestCase(FileHome.Calibre)]
        [TestCase(FileHome.Audiobooks)]
        public void adopted_file_is_forgotten_and_left_in_place(FileHome home)
        {
            var file = GivenOffEntryFile(home, adopted: true);

            Subject.DeleteTrackFile(_author, file);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(file, DeleteMediaFileReason.Manual), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBook(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(It.IsAny<string>()), Times.Never());
            _log.Logs.Should().Contain($"Info|adopted file left in place: {file.Path}");
        }

        [Test]
        public void entry_file_still_takes_the_root_folder_path()
        {
            GivenNonCalibreRootFolder();
            GivenRootFolderExists();
            GivenRootFolderHasFolders();
            GivenAuthorFolderExists();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(_trackFile.Path))
                  .Returns(true);

            Subject.DeleteTrackFile(_author, _trackFile);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(_trackFile.Path, "Author Name"), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBook(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<ILightNovelCalibreSettings>().Verify(v => v.ForConfig(), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            _log.Logs.Should().NotContain(l => l.Contains("adopted file left in place"));
        }

        // Deleting an entry with its files: the same per-row rule, all rows read by metadata id
        // (the author row is already gone); the entry folder itself goes as today (HandleAsync).
        private List<BookFile> GivenEntryFiles(params BookFile[] offEntry)
        {
            var files = new List<BookFile> { _trackFile };
            files.AddRange(offEntry);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByAuthorMetadataId(_author.AuthorMetadataId))
                  .Returns(files);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.Is<string>(p => p.StartsWith("/srv/audiobooks/"))))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.Is<string>(p => p.StartsWith("/srv/audiobooks/"))))
                  .Returns(true);

            return files;
        }

        private void GivenFolderHolds(string folder, params string[] files)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(folder, true))
                  .Returns(new List<string>(files));
        }

        [Test]
        public void deleting_an_entry_with_files_removes_its_calibre_rows_through_calibre()
        {
            GivenNonCalibreRootFolder();
            var grabbed = new BookFile { Id = 10, Home = FileHome.Calibre, CalibreId = 12, Path = "/books/Reki Kawahara/Sword Art Online 2 (12)/Sword Art Online 2 - Reki Kawahara.epub" };
            var adopted = new BookFile { Id = 11, Home = FileHome.Calibre, CalibreId = 13, Adopted = true, Path = "/books/Reki Kawahara/Sword Art Online 1 (13)/Sword Art Online 1 - Reki Kawahara.epub" };
            GivenEntryFiles(grabbed, adopted);

            Subject.Handle(new AuthorDeletedEvent(_author, true, false));

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBooks(It.Is<List<BookFile>>(l => l.Count == 1 && l[0] == grabbed), _config), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBooks(It.IsAny<List<BookFile>>(), It.IsAny<CalibreSettings>()), Times.Once());
            _log.Logs.Should().Contain($"Info|adopted file left in place: {adopted.Path}");
        }

        // Final review I4: each Mangarr-grabbed audiobook FILE is recycled (the single-file rule),
        // and the folder goes only once nothing is left in it -- a grab may have landed in a
        // pre-existing folder of the same name, and a folder is never recycled wholesale.
        [Test]
        public void deleting_an_entry_with_files_recycles_each_grabbed_audiobook_file_and_removes_the_emptied_folder()
        {
            GivenNonCalibreRootFolder();
            var folder = "/srv/audiobooks/Sword Art Online/Sword Art Online - Vol. 3";
            var part1 = new BookFile { Id = 10, Home = FileHome.Audiobooks, Path = folder + "/Sword Art Online - Vol 003 (part 1).m4b" };
            var part2 = new BookFile { Id = 11, Home = FileHome.Audiobooks, Path = folder + "/Sword Art Online - Vol 003 (part 2).m4b" };
            var adoptedFolder = "/srv/audiobooks/Sword Art Online/Sword Art Online - Vol. 1";
            var adopted = new BookFile { Id = 12, Home = FileHome.Audiobooks, Adopted = true, Path = adoptedFolder + "/Sword Art Online - Vol 001.m4b" };
            GivenEntryFiles(part1, part2, adopted);
            GivenFolderHolds(folder);

            Subject.Handle(new AuthorDeletedEvent(_author, true, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(part1.Path, "audiobooks"), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(part2.Path, "audiobooks"), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(folder, false), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBooks(It.IsAny<List<BookFile>>(), It.IsAny<CalibreSettings>()), Times.Never());
            _log.Logs.Should().Contain($"Info|adopted file left in place: {adopted.Path}");
        }

        [Test]
        public void deleting_an_entry_with_files_keeps_a_folder_that_still_holds_a_foreign_file()
        {
            GivenNonCalibreRootFolder();
            var folder = "/srv/audiobooks/Sword Art Online/Sword Art Online - Vol. 3";
            var grabbed = new BookFile { Id = 10, Home = FileHome.Audiobooks, Path = folder + "/Sword Art Online - Vol 003.m4b" };
            GivenEntryFiles(grabbed);
            GivenFolderHolds(folder, folder + "/cover.jpg");

            Subject.Handle(new AuthorDeletedEvent(_author, true, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(grabbed.Path, "audiobooks"), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void deleting_an_entry_with_files_never_touches_an_adopted_original_sharing_a_grabbed_files_folder()
        {
            GivenNonCalibreRootFolder();
            var folder = "/srv/audiobooks/Sword Art Online/Sword Art Online - Vol. 3";
            var grabbed = new BookFile { Id = 10, Home = FileHome.Audiobooks, Path = folder + "/Sword Art Online - Vol 003 (part 1).m4b" };
            var adopted = new BookFile { Id = 11, Home = FileHome.Audiobooks, Adopted = true, Path = folder + "/Sword Art Online - Vol 003 (part 2).m4b" };
            GivenEntryFiles(grabbed, adopted);
            GivenFolderHolds(folder, adopted.Path);

            Subject.Handle(new AuthorDeletedEvent(_author, true, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(grabbed.Path, "audiobooks"), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(adopted.Path, It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
            _log.Logs.Should().Contain($"Info|adopted file left in place: {adopted.Path}");
        }

        [Test]
        public void deleting_an_entry_with_files_skips_a_grabbed_audiobook_file_that_is_already_gone()
        {
            GivenNonCalibreRootFolder();
            var folder = "/srv/audiobooks/Sword Art Online/Sword Art Online - Vol. 3";
            var grabbed = new BookFile { Id = 10, Home = FileHome.Audiobooks, Path = folder + "/Sword Art Online - Vol 003.m4b" };
            GivenEntryFiles(grabbed);
            GivenFolderHolds(folder);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(grabbed.Path))
                  .Returns(false);

            Subject.Handle(new AuthorDeletedEvent(_author, true, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(folder, false), Times.Once());
        }

        // A calibre that is down (T5 deferred, folded into I4): the author row is already gone, so
        // the audiobooks are still recycled and the calibre failure is one Error line.
        [Test]
        public void deleting_an_entry_with_files_still_recycles_its_audiobooks_when_calibre_throws()
        {
            GivenNonCalibreRootFolder();
            var folder = "/srv/audiobooks/Sword Art Online/Sword Art Online - Vol. 3";
            var epub = new BookFile { Id = 10, Home = FileHome.Calibre, CalibreId = 12, Path = "/books/Reki Kawahara/Sword Art Online 2 (12)/Sword Art Online 2 - Reki Kawahara.epub" };
            var audio = new BookFile { Id = 11, Home = FileHome.Audiobooks, Path = folder + "/Sword Art Online - Vol 003.m4b" };
            GivenEntryFiles(epub, audio);
            GivenFolderHolds(folder);

            Mocker.GetMock<ICalibreProxy>()
                  .Setup(s => s.DeleteBooks(It.IsAny<List<BookFile>>(), _config))
                  .Throws(new CalibreException("calibre did not answer"));

            Subject.Handle(new AuthorDeletedEvent(_author, true, false));

            ExceptionVerification.ExpectedErrors(1);
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(audio.Path, "audiobooks"), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(folder, false), Times.Once());
        }

        [Test]
        public void deleting_an_entry_without_its_files_touches_nothing()
        {
            var grabbed = new BookFile { Id = 10, Home = FileHome.Calibre, CalibreId = 12, Path = "/books/x.epub" };
            GivenEntryFiles(grabbed);

            Subject.Handle(new AuthorDeletedEvent(_author, false, false));

            Mocker.GetMock<IMediaFileService>().Verify(v => v.GetFilesByAuthorMetadataId(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBooks(It.IsAny<List<BookFile>>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void deleting_a_manga_entry_with_files_takes_todays_path()
        {
            GivenNonCalibreRootFolder();
            GivenEntryFiles();

            Subject.Handle(new AuthorDeletedEvent(_author, true, false));

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBooks(It.IsAny<List<BookFile>>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ILightNovelCalibreSettings>().Verify(v => v.ForConfig(), Times.Never());
        }

        [Test]
        public void deleting_an_entry_in_a_stock_calibre_root_still_deletes_its_entry_rows_through_that_root()
        {
            var rootSettings = new CalibreSettings { Host = "stock.test", Port = 8080, Library = "library" };
            Mocker.GetMock<IRootFolderService>()
                  .Setup(x => x.GetBestRootFolder(_author.Path))
                  .Returns(new RootFolder { Path = RootFolder, IsCalibreLibrary = true, CalibreSettings = rootSettings });
            GivenEntryFiles();

            Subject.Handle(new AuthorDeletedEvent(_author, true, false));

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBooks(It.Is<List<BookFile>>(l => l.Count == 1 && l[0] == _trackFile), rootSettings), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBooks(It.IsAny<List<BookFile>>(), It.IsAny<CalibreSettings>()), Times.Once());
            Mocker.GetMock<ILightNovelCalibreSettings>().Verify(v => v.ForConfig(), Times.Never());
        }

        [Test]
        public void deleting_an_entry_in_a_stock_calibre_root_sends_an_off_entry_row_through_config_only()
        {
            var rootSettings = new CalibreSettings { Host = "stock.test", Port = 8080, Library = "library" };
            Mocker.GetMock<IRootFolderService>()
                  .Setup(x => x.GetBestRootFolder(_author.Path))
                  .Returns(new RootFolder { Path = RootFolder, IsCalibreLibrary = true, CalibreSettings = rootSettings });
            var grabbed = new BookFile { Id = 10, Home = FileHome.Calibre, CalibreId = 12, Path = "/books/x.epub" };
            GivenEntryFiles(grabbed);

            Subject.Handle(new AuthorDeletedEvent(_author, true, false));

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBooks(It.Is<List<BookFile>>(l => l.Count == 1 && l[0] == grabbed), _config), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBooks(It.Is<List<BookFile>>(l => l.Count == 1 && l[0] == _trackFile), rootSettings), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBooks(It.IsAny<List<BookFile>>(), It.IsAny<CalibreSettings>()), Times.Exactly(2));
        }

        // The empty-folder sweep after a row is deleted (DeleteEmptyFolders) looks at the entry
        // folder and the file's own folder. An off-entry row's folder is calibre's or
        // Audiobookshelf's, and an audio-only entry's folder is legitimately empty -- so the sweep
        // is for Entry rows only, whichever way the row went (a delete, or the scan reconcile's
        // MissingFromDisk).
        [TestCase(FileHome.Calibre, DeleteMediaFileReason.Manual)]
        [TestCase(FileHome.Audiobooks, DeleteMediaFileReason.Manual)]
        [TestCase(FileHome.Audiobooks, DeleteMediaFileReason.MissingFromDisk)]
        public void deleting_an_off_entry_row_never_sweeps_folders(FileHome home, DeleteMediaFileReason reason)
        {
            Mocker.GetMock<IConfigService>().Setup(x => x.DeleteEmptyFolders).Returns(true);

            var file = GivenOffEntryFile(home, adopted: false);
            file.Author = _author;

            Subject.Handle(new BookFileDeletedEvent(file, reason));

            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.RemoveEmptySubfolders(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void deleting_an_entry_row_still_sweeps_the_emptied_entry_folder()
        {
            Mocker.GetMock<IConfigService>().Setup(x => x.DeleteEmptyFolders).Returns(true);

            _trackFile.Author = _author;

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(_author.Path, true))
                  .Returns(new List<string>());

            Subject.Handle(new BookFileDeletedEvent(_trackFile, DeleteMediaFileReason.Manual));

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(_author.Path, true), Times.Once());
        }
    }
}
