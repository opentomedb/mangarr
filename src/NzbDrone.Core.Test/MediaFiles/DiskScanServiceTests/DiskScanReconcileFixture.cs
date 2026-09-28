using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.DiskScanServiceTests
{
    // One copy each (2026-09-20): a scan reconciles a light-novel entry's off-entry rows before
    // the folder walk (the walk never sees them). ScanFixture is known-red; this stands apart.
    [TestFixture]
    public class DiskScanReconcileFixture : CoreTest<DiskScanService>
    {
        private string _rootFolder;
        private Author _lightNovel;
        private Author _otherLightNovel;
        private Author _manga;
        private List<string> _calls;

        [SetUp]
        public void Setup()
        {
            _rootFolder = @"C:\Test\LightNovels".AsOsAgnostic();
            _lightNovel = GivenAuthor(1, "Sword Art Online", "local-sword-art-online~ln");
            _otherLightNovel = GivenAuthor(2, "Overlord", "local-overlord~ln");
            _manga = GivenAuthor(3, "Dandadan", "local-dandadan");
            _calls = new List<string>();

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.All())
                .Returns(new List<RootFolder> { new RootFolder { Path = _rootFolder } });

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder(It.IsAny<string>()))
                .Returns(new RootFolder { Path = _rootFolder });

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.GetAuthors(It.IsAny<List<int>>()))
                .Returns<List<int>>(ids => new List<Author> { _lightNovel, _otherLightNovel, _manga }.FindAll(a => ids.Contains(a.Id)));

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.GetAllAuthors())
                .Returns(new List<Author> { _lightNovel, _manga, _otherLightNovel });

            // the walk: folder present, nothing in it
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderExists(It.IsAny<string>()))
                .Callback<string>(p => _calls.Add("walk"))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFileInfos(It.IsAny<string>(), true))
                .Returns(new List<IFileInfo>());

            Mocker.GetMock<IMediaFileService>()
                .Setup(v => v.GetFilesWithBasePath(It.IsAny<string>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IOffEntryFileReconciler>()
                .Setup(s => s.Reconcile(It.IsAny<Author>()))
                .Callback<Author>(a => _calls.Add("reconcile " + a.Name));

            // Fix round 1 (I2): every LN reconcile attempts the entry's ABS title sync too; an
            // author with no rows (the default here) makes it a no-op with nothing configured.
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByAuthor(It.IsAny<int>()))
                .Returns(new List<BookFile>());
        }

        private Author GivenAuthor(int id, string name, string foreignAuthorId)
        {
            return new Author
            {
                Id = id,
                Name = name,
                Path = Path.Combine(_rootFolder, name),
                Metadata = new AuthorMetadata { Name = name, ForeignAuthorId = foreignAuthorId }
            };
        }

        [Test]
        public void scan_of_a_light_novel_entry_reconciles_it_before_the_folder_walk()
        {
            Subject.Scan(new List<string> { _lightNovel.Path }, authorIds: new List<int> { _lightNovel.Id });

            ExceptionVerification.IgnoreWarns();
            Mocker.GetMock<IOffEntryFileReconciler>().Verify(v => v.Reconcile(_lightNovel), Times.Once());
            Mocker.GetMock<IOffEntryFileReconciler>().Verify(v => v.Reconcile(It.IsAny<Author>()), Times.Once());
            _calls.Should().HaveCountGreaterThan(1);
            _calls[0].Should().Be("reconcile Sword Art Online");
            _calls.Should().Contain("walk");
        }

        [Test]
        public void scan_reconciles_even_when_the_root_folder_is_missing()
        {
            // the walk bails before touching anything; the off-entry rows are still judged
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderExists(It.IsAny<string>()))
                .Returns(false);

            Subject.Scan(new List<string> { _lightNovel.Path }, authorIds: new List<int> { _lightNovel.Id });

            ExceptionVerification.IgnoreWarns();
            Mocker.GetMock<IOffEntryFileReconciler>().Verify(v => v.Reconcile(_lightNovel), Times.Once());
        }

        [Test]
        public void scan_of_a_manga_entry_never_reconciles()
        {
            Subject.Scan(new List<string> { _manga.Path }, authorIds: new List<int> { _manga.Id });

            ExceptionVerification.IgnoreWarns();
            Mocker.GetMock<IOffEntryFileReconciler>().Verify(v => v.Reconcile(It.IsAny<Author>()), Times.Never());
        }

        [Test]
        public void unscoped_scan_reconciles_every_light_novel_entry()
        {
            Subject.Scan();

            ExceptionVerification.IgnoreWarns();
            Mocker.GetMock<IAuthorService>().Verify(v => v.GetAllAuthors(), Times.Once());
            Mocker.GetMock<IOffEntryFileReconciler>().Verify(v => v.Reconcile(_lightNovel), Times.Once());
            Mocker.GetMock<IOffEntryFileReconciler>().Verify(v => v.Reconcile(_otherLightNovel), Times.Once());
            Mocker.GetMock<IOffEntryFileReconciler>().Verify(v => v.Reconcile(_manga), Times.Never());
            _calls[0].Should().StartWith("reconcile ");
            _calls[1].Should().StartWith("reconcile ");
            _calls[2].Should().Be("walk");
        }

        // Fix round 1 (I2, 2026-09-23): ABS's own scan endpoint answers before it actually scans, so
        // a grabbed audiobook's import-time sync attempt usually finds no item yet. Every LN scan
        // attempts the entry's ABS title sync right after the reconciler, so the next scan corrects it.
        [Test]
        public void scan_of_a_light_novel_entry_syncs_its_audiobookshelf_title_after_reconciling()
        {
            var audioRow = new BookFile { Id = 1, Home = FileHome.Audiobooks, Author = _lightNovel };
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(_lightNovel.Id)).Returns(new List<BookFile> { audioRow });
            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Setup(s => s.Sync(_lightNovel, It.Is<List<BookFile>>(l => l.Count == 1 && l[0] == audioRow), out It.Ref<string>.IsAny))
                .Returns(true);

            Subject.Scan(new List<string> { _lightNovel.Path }, authorIds: new List<int> { _lightNovel.Id });

            ExceptionVerification.IgnoreWarns();
            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(_lightNovel, It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny), Times.Once());
        }

        [Test]
        public void the_audiobookshelf_sync_runs_after_the_reconciler()
        {
            var audioRow = new BookFile { Id = 1, Home = FileHome.Audiobooks, Author = _lightNovel };
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(_lightNovel.Id)).Returns(new List<BookFile> { audioRow });
            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Setup(s => s.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny))
                .Callback(() => _calls.Add("abs-sync"))
                .Returns(true);

            Subject.Scan(new List<string> { _lightNovel.Path }, authorIds: new List<int> { _lightNovel.Id });

            ExceptionVerification.IgnoreWarns();
            _calls[0].Should().Be("reconcile Sword Art Online");
            _calls[1].Should().Be("abs-sync");
        }

        [Test]
        public void an_entry_with_no_audiobooks_homed_rows_is_never_synced()
        {
            Subject.Scan(new List<string> { _lightNovel.Path }, authorIds: new List<int> { _lightNovel.Id });

            ExceptionVerification.IgnoreWarns();
            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny), Times.Never());
        }

        // Fix round 2 (Low, 2026-09-23): an unexpected exception from the ABS sync (or from
        // GetFilesByAuthor) must never abort the rest of the scan -- new-file imports included.
        [Test]
        public void a_throwing_audiobookshelf_sync_is_a_warning_and_the_scan_still_completes()
        {
            var audioRow = new BookFile { Id = 1, Home = FileHome.Audiobooks, Author = _lightNovel };
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(_lightNovel.Id)).Returns(new List<BookFile> { audioRow });
            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Setup(s => s.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny))
                .Throws(new InvalidOperationException("ABS is down"));

            Assert.DoesNotThrow(() => Subject.Scan(new List<string> { _lightNovel.Path }, authorIds: new List<int> { _lightNovel.Id }));

            ExceptionVerification.IgnoreWarns();
            _calls.Should().Contain("walk");
        }

        [Test]
        public void a_throwing_audiobookshelf_sync_for_one_entry_does_not_skip_the_next_entrys_reconcile()
        {
            var audioRow = new BookFile { Id = 1, Home = FileHome.Audiobooks, Author = _lightNovel };
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(_lightNovel.Id)).Returns(new List<BookFile> { audioRow });
            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Setup(s => s.Sync(_lightNovel, It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny))
                .Throws(new InvalidOperationException("ABS is down"));

            Subject.Scan();

            ExceptionVerification.IgnoreWarns();
            Mocker.GetMock<IOffEntryFileReconciler>().Verify(v => v.Reconcile(_otherLightNovel), Times.Once());
        }
    }
}
