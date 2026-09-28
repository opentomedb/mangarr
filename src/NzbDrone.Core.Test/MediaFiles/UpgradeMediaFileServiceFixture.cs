using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    public class UpgradeMediaFileServiceFixture : CoreTest<UpgradeMediaFileService>
    {
        private BookFile _trackFile;
        private LocalBook _localTrack;
        private string _rootPath = @"C:\Test\Music\Author".AsOsAgnostic();

        [SetUp]
        public void Setup()
        {
            LightNovelStorageMock.Setup(Mocker.GetMock<ILightNovelStorage>());

            _localTrack = new LocalBook();
            _localTrack.Author = new Author
            {
                Path = _rootPath
            };

            _trackFile = Builder<BookFile>
                .CreateNew()
                .Build();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FolderExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                 .Setup(c => c.GetParentFolder(It.IsAny<string>()))
                 .Returns<string>(c => Path.GetDirectoryName(c));

            Mocker.GetMock<IRootFolderService>()
                .Setup(c => c.GetBestRootFolder(It.IsAny<string>()))
                .Returns(new RootFolder());
        }

        private void GivenSingleTrackWithSingleTrackFile()
        {
            var existing = new List<BookFile>
            {
                new BookFile
                {
                    Id = 1,
                    EditionId = 11,
                    Path = Path.Combine(_rootPath, @"Season 01\30.rock.s01e01.avi"),
                }
            };

            _localTrack.Book = Builder<Book>.CreateNew()
                .With(e => e.BookFiles = new LazyLoaded<List<BookFile>>(existing))
                .Build();

            // Light novels (2026-09): the upgrade replaces the files of the edition being imported
            // to; a manga volume's one edition holds every file of the volume, as here.
            _localTrack.Edition = Builder<Edition>.CreateNew()
                .With(e => e.Id = 11)
                .With(e => e.BookFiles = new LazyLoaded<List<BookFile>>(existing))
                .Build();
        }

        private (BookFile Epub, BookFile Audio) GivenVolumeWithEpubAndAudio()
        {
            var epub = new BookFile { Id = 1, EditionId = 11, Path = Path.Combine(_rootPath, "Overlord - Vol. 5", "Overlord - Vol 005.epub") };
            var audio = new BookFile { Id = 2, EditionId = 12, Path = Path.Combine(_rootPath, "Overlord - Vol. 5", "Overlord - Vol 005.mp3") };

            _localTrack.Book = Builder<Book>.CreateNew()
                .With(e => e.BookFiles = new LazyLoaded<List<BookFile>>(new List<BookFile> { epub, audio }))
                .Build();

            _localTrack.Edition = Builder<Edition>.CreateNew()
                .With(e => e.Id = 12)
                .With(e => e.MediaType = MediaType.Audio)
                .With(e => e.BookFiles = new LazyLoaded<List<BookFile>>(new List<BookFile> { audio }))
                .Build();

            return (epub, audio);
        }

        [Test]
        public void should_only_replace_the_files_of_the_edition_being_imported()
        {
            // Importing an M4B for the Audio edition must never touch the EPUB of the same volume.
            var (epub, audio) = GivenVolumeWithEpubAndAudio();

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(audio.Path, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(epub.Path, It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(audio, DeleteMediaFileReason.Upgrade), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(epub, It.IsAny<DeleteMediaFileReason>()), Times.Never());
            result.OldFiles.Should().BeEquivalentTo(new[] { audio });
        }

        [Test]
        public void should_delete_single_track_file_once()
        {
            GivenSingleTrackWithSingleTrackFile();

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_delete_track_file_from_database()
        {
            GivenSingleTrackWithSingleTrackFile();

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_delete_existing_file_fromdb_if_file_doesnt_exist()
        {
            GivenSingleTrackWithSingleTrackFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            // Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localTrack.Book.BookFiles.Value, DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_not_try_to_recyclebin_existing_file_if_file_doesnt_exist()
        {
            GivenSingleTrackWithSingleTrackFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_return_old_track_file_in_oldFiles()
        {
            GivenSingleTrackWithSingleTrackFile();

            Subject.UpgradeBookFile(_trackFile, _localTrack).OldFiles.Count.Should().Be(1);
        }

        [Test]
        [Ignore("Pending readarr fix")]
        public void should_import_if_existing_file_doesnt_exist_in_db()
        {
            _localTrack.Book = Builder<Book>.CreateNew()
                .With(e => e.BookFiles = new LazyLoaded<List<BookFile>>())
                .Build();

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            // Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localTrack.Book.BookFiles.Value, It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        // One copy each (2026-09-20): a light-novel EPUB is handed to calibre's content server, its
        // audio is moved into Audiobookshelf's tree, and an adopted original is never replaced.
        // Placement keys on the destination; removal of each old file keys on that file's home.

        private string _lnPath = @"C:\Test\LightNovels\KonoSuba".AsOsAgnostic();
        private CalibreSettings _lnCalibre;

        private void GivenLightNovel(MediaType editionType, params BookFile[] existing)
        {
            // A fresh download has no calibre id yet (ImportApprovedBooks copies LocalBook.CalibreId, 0).
            _trackFile.CalibreId = 0;

            _localTrack.Author = new Author
            {
                Path = _lnPath,
                Metadata = new AuthorMetadata { Name = "KonoSuba", ForeignAuthorId = "local-konosuba~ln" }
            };

            _localTrack.Book = Builder<Book>.CreateNew()
                .With(e => e.BookFiles = new LazyLoaded<List<BookFile>>(new List<BookFile>(existing)))
                .Build();

            _localTrack.Edition = Builder<Edition>.CreateNew()
                .With(e => e.Id = 12)
                .With(e => e.MediaType = editionType)
                .With(e => e.BookFiles = new LazyLoaded<List<BookFile>>(new List<BookFile>(existing)))
                .Build();

            _lnCalibre = new CalibreSettings { Host = "calibre.test", Port = 8081, Library = "books" };

            Mocker.GetMock<ILightNovelCalibreSettings>()
                .Setup(s => s.ForConfig())
                .Returns(_lnCalibre);

            // Rename Books on (the live setting): the audio layout needs it (I3); calibre accepts writes
            Mocker.GetMock<INamingConfigService>()
                .Setup(s => s.GetConfig())
                .Returns(new NamingConfig { RenameBooks = true });

            Mocker.GetMock<ICalibreProxy>()
                .Setup(c => c.HasWriteAccess(_lnCalibre))
                .Returns(true);

            Mocker.GetMock<ICalibreProxy>()
                .Setup(c => c.AddAndConvert(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>()))
                .Returns<BookFile, CalibreSettings>((f, s) => f);

            Mocker.GetMock<IMoveBookFiles>()
                .Setup(c => c.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()))
                .Returns<BookFile, LocalBook>((f, l) => f);

            Mocker.GetMock<IMoveBookFiles>()
                .Setup(c => c.CopyBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()))
                .Returns<BookFile, LocalBook>((f, l) => f);
        }

        [Test]
        public void light_novel_epub_goes_to_calibre_with_the_config_settings()
        {
            GivenLightNovel(MediaType.Ebook);
            var source = _trackFile.Path;

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.AddAndConvert(_trackFile, _lnCalibre), Times.Once());
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()), Times.Never());
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.CopyBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(source), Times.Once());
            result.BookFile.Home.Should().Be(FileHome.Calibre);
        }

        [Test]
        public void light_novel_epub_copy_only_keeps_the_source()
        {
            GivenLightNovel(MediaType.Ebook);

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack, copyOnly: true);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.AddAndConvert(_trackFile, _lnCalibre), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(It.IsAny<string>()), Times.Never());
            result.BookFile.Home.Should().Be(FileHome.Calibre);
        }

        [Test]
        public void light_novel_audio_is_moved_and_tagged()
        {
            GivenLightNovel(MediaType.Audio);

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(_trackFile, _localTrack), Times.Once());
            Mocker.GetMock<IMetadataTagService>().Verify(v => v.WriteTags(_trackFile, true, false), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.AddAndConvert(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>()), Times.Never());
            result.BookFile.Home.Should().Be(FileHome.Audiobooks);
        }

        [Test]
        public void light_novel_audio_import_fails_when_the_audiobooks_mount_is_missing()
        {
            // Without the mount, CreateFolder would happily build /srv/audiobooks inside the container
            // and the "moved" file would die with it: the import must fail and keep the download --
            // and the old file, since a refused import must not touch it either.
            var old = new BookFile { Id = 3, EditionId = 12, Path = "/srv/audiobooks/KonoSuba - Vol. 1/KonoSuba - Vol 001.m4b", Home = FileHome.Audiobooks };
            GivenLightNovel(MediaType.Audio, old);

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FolderExists(LightNovelStorageMock.AbsRoot))
                .Returns(false);

            Assert.Throws<RootFolderNotFoundException>(() => Subject.UpgradeBookFile(_trackFile, _localTrack));

            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()), Times.Never());
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.CopyBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        // Light-novel storage (2026-09-22): an unknown Audiobookshelf folder (unmapped, ABS silent) fails
        // the import the way a missing one does -- the download stays put, and so does the old file.
        [Test]
        public void light_novel_audio_import_fails_when_audiobookshelfs_folder_is_unknown()
        {
            var old = new BookFile { Id = 3, EditionId = 12, Path = "/srv/audiobooks/KonoSuba - Vol. 1/KonoSuba - Vol 001.m4b", Home = FileHome.Audiobooks };
            GivenLightNovel(MediaType.Audio, old);
            Mocker.GetMock<ILightNovelStorage>().Setup(s => s.AudiobookshelfRoot()).Returns((string)null);

            Assert.Throws<RootFolderNotFoundException>(() => Subject.UpgradeBookFile(_trackFile, _localTrack));

            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        // Rename Books off: the namer keeps the release's file name, so there is no
        // "<Series> - Vol. N" folder and the audio would land loose in Audiobookshelf's root.
        // Refused before anything is touched (final review I3).
        [Test]
        public void light_novel_audio_import_is_refused_when_rename_books_is_off()
        {
            var old = new BookFile { Id = 3, EditionId = 12, Path = "/srv/audiobooks/KonoSuba - Vol. 1/KonoSuba - Vol 001.m4b", Home = FileHome.Audiobooks };
            GivenLightNovel(MediaType.Audio, old);

            Mocker.GetMock<INamingConfigService>()
                .Setup(s => s.GetConfig())
                .Returns(new NamingConfig { RenameBooks = false });

            var ex = Assert.Throws<InvalidOperationException>(() => Subject.UpgradeBookFile(_trackFile, _localTrack));

            ex.Message.Should().Contain("Rename Books is off");
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()), Times.Never());
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.CopyBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void rename_books_off_does_not_stop_a_light_novel_epub_or_a_manga_import()
        {
            GivenLightNovel(MediaType.Ebook);

            Mocker.GetMock<INamingConfigService>()
                .Setup(s => s.GetConfig())
                .Returns(new NamingConfig { RenameBooks = false });

            Subject.UpgradeBookFile(_trackFile, _localTrack).BookFile.Home.Should().Be(FileHome.Calibre);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.AddAndConvert(_trackFile, _lnCalibre), Times.Once());
        }

        // A refused calibre write (trusted_ips) is found BEFORE the old EPUB's formats are removed:
        // otherwise AddAndConvert's refusal would leave a format-less calibre book behind and the
        // volume back on Wanted (final review, T4 tied item).
        [Test]
        public void a_calibre_that_refuses_writes_fails_the_epub_import_before_the_old_file_is_touched()
        {
            var old = new BookFile
            {
                Id = 3,
                EditionId = 12,
                Path = "/books/Rifujin na Magonote/KonoSuba Vol 1 (42)/KonoSuba Vol 1 - Rifujin na Magonote.epub",
                Home = FileHome.Calibre,
                CalibreId = 42
            };
            GivenLightNovel(MediaType.Ebook, old);

            Mocker.GetMock<ICalibreProxy>()
                .Setup(c => c.HasWriteAccess(_lnCalibre))
                .Returns(false);

            var ex = Assert.Throws<CalibreException>(() => Subject.UpgradeBookFile(_trackFile, _localTrack));

            ex.Message.Should().Contain("trusted_ips");
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBook(It.IsAny<int>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.RemoveFormats(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.AddAndConvert(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void the_calibre_write_probe_is_not_sent_for_a_light_novel_audio_or_a_manga_import()
        {
            GivenLightNovel(MediaType.Audio);

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.HasWriteAccess(It.IsAny<CalibreSettings>()), Times.Never());
        }

        [Test]
        public void manga_in_a_plain_root_is_moved_and_stays_entry_homed()
        {
            GivenSingleTrackWithSingleTrackFile();

            Mocker.GetMock<IMoveBookFiles>()
                .Setup(c => c.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()))
                .Returns<BookFile, LocalBook>((f, l) => f);

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(_trackFile, _localTrack), Times.Once());
            Mocker.GetMock<IMetadataTagService>().Verify(v => v.WriteTags(_trackFile, true, false), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.AddAndConvert(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<ILightNovelCalibreSettings>().Verify(v => v.ForConfig(), Times.Never());
            Mocker.GetMock<INamingConfigService>().Verify(v => v.GetConfig(), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.HasWriteAccess(It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            result.BookFile.Home.Should().Be(FileHome.Entry);
        }

        [Test]
        public void light_novel_audio_upgrade_recycles_the_old_audiobooks_file()
        {
            var old = new BookFile
            {
                Id = 3,
                EditionId = 12,
                Path = "/srv/audiobooks/KonoSuba - Vol. 1/KonoSuba - Vol 001.m4b",
                Home = FileHome.Audiobooks
            };
            GivenLightNovel(MediaType.Audio, old);

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(old.Path, "audiobooks"), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(old, DeleteMediaFileReason.Upgrade), Times.Once());
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(_trackFile, _localTrack), Times.Once());
            result.OldFiles.Should().BeEquivalentTo(new[] { old });
            result.BookFile.Home.Should().Be(FileHome.Audiobooks);
        }

        [Test]
        public void an_adopted_original_is_never_upgraded()
        {
            var old = new BookFile
            {
                Id = 3,
                EditionId = 12,
                Path = "/srv/audiobooks/KonoSuba - Vol. 1/KonoSuba - Vol 001.m4b",
                Home = FileHome.Audiobooks,
                Adopted = true
            };
            GivenLightNovel(MediaType.Audio, old);

            Assert.Throws<InvalidOperationException>(() => Subject.UpgradeBookFile(_trackFile, _localTrack));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.RemoveFormats(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.AddAndConvert(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>()), Times.Never());
        }

        [Test]
        public void light_novel_epub_upgrade_recycles_an_entry_copy_without_asking_calibre()
        {
            var old = new BookFile
            {
                Id = 3,
                EditionId = 12,
                Path = Path.Combine(_lnPath, "KonoSuba - Vol. 1", "KonoSuba - Vol 001.epub"),
                Home = FileHome.Entry,
                CalibreId = 0
            };
            GivenLightNovel(MediaType.Ebook, old);

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(old.Path, Path.Combine("KonoSuba", "KonoSuba - Vol. 1")), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBook(It.IsAny<int>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.RemoveFormats(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(old, DeleteMediaFileReason.Upgrade), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.AddAndConvert(_trackFile, _lnCalibre), Times.Once());
            _trackFile.CalibreId.Should().Be(0);
            result.BookFile.Home.Should().Be(FileHome.Calibre);
        }

        [Test]
        public void light_novel_epub_upgrade_removes_the_old_format_from_calibre()
        {
            // The old EPUB lives in calibre's library, off the entry's root: its formats are removed
            // through the content server and the new file takes over its calibre id (add-format path).
            var old = new BookFile
            {
                Id = 3,
                EditionId = 12,
                Path = "/books/Rifujin na Magonote/KonoSuba Vol 1 (42)/KonoSuba Vol 1 - Rifujin na Magonote.epub",
                Home = FileHome.Calibre,
                CalibreId = 42
            };
            GivenLightNovel(MediaType.Ebook, old);

            Mocker.GetMock<ICalibreProxy>()
                .Setup(c => c.GetBook(42, _lnCalibre))
                .Returns(new CalibreBook { Formats = new Dictionary<string, CalibreBookFormat> { { "EPUB", new CalibreBookFormat() } } });

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.RemoveFormats(42, It.Is<IEnumerable<string>>(f => f.Single() == "EPUB"), _lnCalibre), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(old, DeleteMediaFileReason.Upgrade), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.AddAndConvert(_trackFile, _lnCalibre), Times.Once());
            _trackFile.CalibreId.Should().Be(42);
            result.BookFile.Home.Should().Be(FileHome.Calibre);
        }

        // Light-novel storage (2026-09-22): a kind whose home is the entry folder imports like manga.
        [Test]
        public void light_novel_epub_with_the_entry_home_is_moved_into_the_entry_folder()
        {
            GivenLightNovel(MediaType.Ebook);
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Entry);

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(_trackFile, _localTrack), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.AddAndConvert(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.HasWriteAccess(It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<ILightNovelCalibreSettings>().Verify(v => v.ForConfig(), Times.Never());
            result.BookFile.Home.Should().Be(FileHome.Entry);
        }

        [Test]
        public void light_novel_audio_with_the_entry_home_is_moved_into_the_entry_folder_even_with_rename_books_off()
        {
            GivenLightNovel(MediaType.Audio);
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.AudioHome).Returns(LightNovelHome.Entry);
            Mocker.GetMock<INamingConfigService>().Setup(s => s.GetConfig()).Returns(new NamingConfig { RenameBooks = false });

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(_trackFile, _localTrack), Times.Once());
            Mocker.GetMock<ILightNovelStorage>().Verify(v => v.AudiobookshelfRoot(), Times.Never());
            result.BookFile.Home.Should().Be(FileHome.Entry);
        }

        // An existing file keeps its home: a calibre-homed EPUB replaced by an entry-folder import is
        // removed through the configured calibre (controller ruling C6: DeleteBook, not RemoveFormats,
        // so no empty calibre book is left behind), and the new entry file does not inherit its calibre id.
        [Test]
        public void an_entry_home_epub_over_a_calibre_homed_one_removes_the_old_through_calibre()
        {
            var old = new BookFile { Id = 3, EditionId = 12, Path = "/books/KonoSuba/KonoSuba 1 (7)/KonoSuba 1.epub", Home = FileHome.Calibre, CalibreId = 7 };
            GivenLightNovel(MediaType.Ebook, old);
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Entry);

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBook(old, _lnCalibre), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.RemoveFormats(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(_trackFile, _localTrack), Times.Once());
            result.BookFile.Home.Should().Be(FileHome.Entry);
            result.BookFile.CalibreId.Should().Be(0);
        }

        // Fix round 1 (2026-09-22): the old calibre book on the C6 branch is removed only once the
        // new entry file is safely placed -- a failed move must leave the old EPUB exactly where it
        // was, in calibre, not lost in between.
        private BookFile GivenCalibreHomedOldOverEntryHome()
        {
            var old = new BookFile { Id = 3, EditionId = 12, Path = "/books/KonoSuba/KonoSuba 1 (7)/KonoSuba 1.epub", Home = FileHome.Calibre, CalibreId = 7 };
            GivenLightNovel(MediaType.Ebook, old);
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Entry);

            return old;
        }

        [Test]
        public void a_failed_move_to_the_entry_folder_leaves_the_old_calibre_book_untouched()
        {
            var old = GivenCalibreHomedOldOverEntryHome();

            Mocker.GetMock<IMoveBookFiles>()
                .Setup(c => c.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()))
                .Throws(new IOException("disk full"));

            Assert.Throws<IOException>(() => Subject.UpgradeBookFile(_trackFile, _localTrack));

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBook(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(old, It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void a_successful_move_then_removes_the_old_calibre_book()
        {
            var old = GivenCalibreHomedOldOverEntryHome();
            var moved = false;

            Mocker.GetMock<IMoveBookFiles>()
                .Setup(c => c.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()))
                .Callback(() => moved = true)
                .Returns<BookFile, LocalBook>((f, l) => f);

            Mocker.GetMock<ICalibreProxy>()
                .Setup(c => c.DeleteBook(old, _lnCalibre))
                .Callback(() => moved.Should().BeTrue("the old calibre book is removed only after the new file is placed"));

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(_trackFile, _localTrack), Times.Once());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBook(old, _lnCalibre), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(old, DeleteMediaFileReason.Upgrade), Times.Once());
            result.BookFile.Home.Should().Be(FileHome.Entry);
        }

        [Test]
        public void a_calibre_delete_failure_after_a_successful_move_is_a_warning_not_a_failed_import()
        {
            var old = GivenCalibreHomedOldOverEntryHome();

            Mocker.GetMock<ICalibreProxy>()
                .Setup(c => c.DeleteBook(old, _lnCalibre))
                .Throws(new CalibreException("calibre is down"));

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(_trackFile, _localTrack), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(old, DeleteMediaFileReason.Upgrade), Times.Once());
            result.BookFile.Home.Should().Be(FileHome.Entry);

            ExceptionVerification.ExpectedWarns(1);
        }

        // The old calibre-homed file is calibre's to remove whether or not its path is visible on
        // disk right now -- unlike an Entry/Audiobooks file, it was never the recycle bin's job.
        [Test]
        public void the_old_calibre_book_is_removed_even_when_its_path_is_not_visible_on_disk()
        {
            var old = GivenCalibreHomedOldOverEntryHome();
            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileExists(old.Path)).Returns(false);

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.DeleteBook(old, _lnCalibre), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(old, DeleteMediaFileReason.Upgrade), Times.Once());
            result.BookFile.Home.Should().Be(FileHome.Entry);
        }
    }
}
