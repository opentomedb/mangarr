using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    // One copy each (2026-09-20): a light-novel entry's off-entry rows (EPUBs in calibre, audio in
    // Audiobookshelf's tree) are never seen by the folder walk; the reconciler is their only judge.
    [TestFixture]
    public class OffEntryFileReconcilerFixture : CoreTest<OffEntryFileReconciler>
    {
        private Author _author;
        private CalibreSettings _config;
        private BookFile _entry;
        private BookFile _calibre;
        private BookFile _audio;

        [SetUp]
        public void Setup()
        {
            LightNovelStorageMock.Setup(Mocker.GetMock<ILightNovelStorage>());

            _author = new Author { Id = 7, Name = "Sword Art Online", Path = "/lightnovels/Sword Art Online" };
            _config = new CalibreSettings { Host = "calibre.test", Port = 8081, Library = "books" };

            // an entry row with a calibre id (a stock calibre root's shape) proves the id filter
            _entry = new BookFile
            {
                Id = 1,
                Path = "/lightnovels/Sword Art Online/Sword Art Online - Vol. 1/Sword Art Online - Vol 001.epub",
                Home = FileHome.Entry,
                CalibreId = 5,
                Size = 100
            };

            _calibre = new BookFile
            {
                Id = 2,
                Path = "/books/Reki Kawahara/Sword Art Online 2 (12)/Sword Art Online 2 - Reki Kawahara.epub",
                Home = FileHome.Calibre,
                CalibreId = 12,
                Size = 2000,
                Modified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            _audio = new BookFile
            {
                Id = 3,
                Path = "/srv/audiobooks/Sword Art Online/Sword Art Online - Vol. 3/Sword Art Online - Vol 003.m4b",
                Home = FileHome.Audiobooks,
                Size = 3000
            };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(_author.Id))
                .Returns(new List<BookFile> { _entry, _calibre, _audio });

            Mocker.GetMock<ILightNovelCalibreSettings>()
                .Setup(x => x.ForConfig())
                .Returns(_config);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists(_audio.Path))
                .Returns(true);

            GivenAudiobookshelfFolder(present: true, empty: false);

            GivenCalibreBook(_calibre.Path, _calibre.Size, _calibre.Modified);
        }

        private void GivenAudiobookshelfFolder(bool present, bool empty)
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderExists(LightNovelStorageMock.AbsRoot))
                .Returns(present);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderEmpty(LightNovelStorageMock.AbsRoot))
                .Returns(empty);
        }

        private void GivenCalibreBooks(params CalibreBook[] books)
        {
            Mocker.GetMock<ICalibreProxy>()
                .Setup(x => x.GetBooks(It.IsAny<List<int>>(), _config))
                .Returns(books.ToList());
        }

        // formatKey: the live content server keys format_metadata "epub", lowercase (checked 2026-09-20);
        // the cases here keep the upper-case key the proxy fixture uses, one case below is live-shaped.
        private void GivenCalibreBook(string epubPath, long size, DateTime modified, string formatKey = "EPUB")
        {
            GivenCalibreBooks(new CalibreBook
            {
                Id = _calibre.CalibreId,
                Formats = new Dictionary<string, CalibreBookFormat>
                {
                    { formatKey, new CalibreBookFormat { Path = epubPath, Size = size, LastModified = modified } }
                }
            });
        }

        [Test]
        public void a_calibre_rename_updates_the_path_and_size()
        {
            var moved = "/books/Reki Kawahara/Sword Art Online 2 - Aincrad (12)/Sword Art Online 2 - Aincrad - Reki Kawahara.epub";
            var modified = new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
            GivenCalibreBook(moved, 2048, modified);

            Subject.Reconcile(_author);

            _calibre.Path.Should().Be(moved);
            _calibre.Size.Should().Be(2048);
            _calibre.Modified.Should().Be(modified);
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(_calibre), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void a_calibre_row_that_still_matches_is_left_alone()
        {
            Subject.Reconcile(_author);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<BookFile>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void a_live_shaped_answer_with_a_lowercase_format_key_keeps_the_row()
        {
            GivenCalibreBook(_calibre.Path, _calibre.Size, _calibre.Modified, formatKey: "epub");

            Subject.Reconcile(_author);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<BookFile>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void a_calibre_book_that_is_gone_forgets_the_row()
        {
            GivenCalibreBooks();

            Subject.Reconcile(_author);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_calibre, DeleteMediaFileReason.MissingFromDisk), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<BookFile>()), Times.Never());
        }

        [Test]
        public void a_batch_answer_that_omits_a_row_forgets_that_row_and_keeps_the_rest()
        {
            var gone = new BookFile
            {
                Id = 4,
                Path = "/books/Reki Kawahara/Sword Art Online 3 (13)/Sword Art Online 3 - Reki Kawahara.epub",
                Home = FileHome.Calibre,
                CalibreId = 13,
                Size = 1300
            };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(_author.Id))
                .Returns(new List<BookFile> { _entry, _calibre, gone, _audio });

            // calibre answers for 12 only (13 came back as null and GetBooks dropped it)
            GivenCalibreBook(_calibre.Path, _calibre.Size, _calibre.Modified);

            Subject.Reconcile(_author);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBooks(It.Is<List<int>>(ids => ids.SequenceEqual(new[] { 12, 13 })), _config), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(gone, DeleteMediaFileReason.MissingFromDisk), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_calibre, It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<BookFile>()), Times.Never());
        }

        [Test]
        public void a_calibre_book_without_an_epub_forgets_the_row()
        {
            GivenCalibreBooks(new CalibreBook
            {
                Id = _calibre.CalibreId,
                Formats = new Dictionary<string, CalibreBookFormat>
                {
                    { "MOBI", new CalibreBookFormat { Path = "/books/x.mobi", Size = 1 } }
                }
            });

            Subject.Reconcile(_author);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_calibre, DeleteMediaFileReason.MissingFromDisk), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<BookFile>()), Times.Never());
        }

        // Final review C1 (2026-09-22): an AZW3 fallback import is a calibre book with no EPUB at
        // all. The row is tracked by calibre's AZW3 then -- judging it by EPUB alone forgot it on
        // every scan, sent the volume back to Wanted and the re-grab added a duplicate book.
        [Test]
        public void an_azw3_only_calibre_book_keeps_its_row()
        {
            _calibre.Path = "/books/Reki Kawahara/Sword Art Online 2 (12)/Sword Art Online 2 - Reki Kawahara.azw3";
            GivenCalibreBook(_calibre.Path, _calibre.Size, _calibre.Modified, formatKey: "azw3");

            Subject.Reconcile(_author);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<BookFile>()), Times.Never());
        }

        [Test]
        public void an_azw3_only_calibre_rename_updates_the_path()
        {
            _calibre.Path = "/books/Reki Kawahara/Sword Art Online 2 (12)/Sword Art Online 2 - Reki Kawahara.azw3";
            var moved = "/books/Reki Kawahara/Sword Art Online 2 - Aincrad (12)/Sword Art Online 2 - Aincrad - Reki Kawahara.azw3";
            GivenCalibreBook(moved, 2048, _calibre.Modified, formatKey: "azw3");

            Subject.Reconcile(_author);

            _calibre.Path.Should().Be(moved);
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(_calibre), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        // LN PDF (2026-09-22): a light novel imported as Ebook PDF is a calibre book with only a PDF.
        // The row is tracked by that PDF -- judging it by EPUB/AZW3 alone would forget it on every
        // scan, send the volume back to Wanted and add a duplicate book on the re-grab (the C1 loop).
        [Test]
        public void a_pdf_only_calibre_book_keeps_its_row()
        {
            _calibre.Path = "/books/Reki Kawahara/Sword Art Online 2 (12)/Sword Art Online 2 - Reki Kawahara.pdf";
            GivenCalibreBook(_calibre.Path, _calibre.Size, _calibre.Modified, formatKey: "pdf");

            Subject.Reconcile(_author);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<BookFile>()), Times.Never());
        }

        [Test]
        public void a_pdf_only_calibre_rename_updates_the_path()
        {
            _calibre.Path = "/books/Reki Kawahara/Sword Art Online 2 (12)/Sword Art Online 2 - Reki Kawahara.pdf";
            var moved = "/books/Reki Kawahara/Sword Art Online 2 - Aincrad (12)/Sword Art Online 2 - Aincrad - Reki Kawahara.pdf";
            GivenCalibreBook(moved, 2048, _calibre.Modified, formatKey: "pdf");

            Subject.Reconcile(_author);

            _calibre.Path.Should().Be(moved);
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(_calibre), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        // A book holding EPUB + the converted AZW3 is tracked by its EPUB, whatever the mtimes say.
        [Test]
        public void an_epub_and_azw3_book_is_tracked_by_its_epub()
        {
            GivenCalibreBooks(new CalibreBook
            {
                Id = _calibre.CalibreId,
                Formats = new Dictionary<string, CalibreBookFormat>
                {
                    { "azw3", new CalibreBookFormat { Path = "/books/Reki Kawahara/Sword Art Online 2 (12)/Sword Art Online 2 - Reki Kawahara.azw3", Size = 5, LastModified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) } },
                    { "epub", new CalibreBookFormat { Path = _calibre.Path, Size = _calibre.Size, LastModified = _calibre.Modified } }
                }
            });

            Subject.Reconcile(_author);

            _calibre.Path.Should().EndWith(".epub");
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<BookFile>()), Times.Never());
        }

        [Test]
        public void an_unanswering_calibre_judges_nothing()
        {
            Mocker.GetMock<ICalibreProxy>()
                .Setup(x => x.GetBooks(It.IsAny<List<int>>(), _config))
                .Throws(new CalibreException("calibre did not answer"));

            Subject.Reconcile(_author);

            ExceptionVerification.IgnoreWarns();
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<BookFile>()), Times.Never());
        }

        [Test]
        public void a_missing_audiobook_file_forgets_the_row()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists(_audio.Path))
                .Returns(false);

            Subject.Reconcile(_author);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_audio, DeleteMediaFileReason.MissingFromDisk), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_calibre, It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        // An absent or empty /srv/audiobooks (bind mount gone, share offline) would otherwise
        // forget every audiobook row -- adopted ones included -- in one scan; the mount is judged
        // first and nothing audio is touched, the calibre half runs as usual.
        [Test]
        public void a_missing_audiobooks_mount_judges_no_audiobook_row()
        {
            GivenAudiobookshelfFolder(present: false, empty: false);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists(_audio.Path))
                .Returns(false);

            Subject.Reconcile(_author);

            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.FileExists(_audio.Path), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.FolderEmpty(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBooks(It.IsAny<List<int>>(), _config), Times.Once());
        }

        [Test]
        public void an_unknown_audiobookshelf_folder_judges_no_audiobook_row()
        {
            // The folder and empty checks are stubbed true/false so the null-root guard is the only
            // thing stopping the scan (a loose FolderExists mock defaults to false, which would pass
            // this test even without the guard).
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FolderExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FolderEmpty(It.IsAny<string>())).Returns(false);
            Mocker.GetMock<ILightNovelStorage>().Setup(s => s.AudiobookshelfRoot()).Returns((string)null);

            Subject.Reconcile(_author);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_audio, It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.FileExists(_audio.Path), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void an_empty_audiobooks_mount_judges_no_audiobook_row()
        {
            GivenAudiobookshelfFolder(present: true, empty: true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists(_audio.Path))
                .Returns(false);

            Subject.Reconcile(_author);

            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.FileExists(_audio.Path), Times.Never());
        }

        [Test]
        public void an_empty_audiobooks_mount_still_forgets_a_gone_calibre_book()
        {
            GivenAudiobookshelfFolder(present: true, empty: true);
            GivenCalibreBooks();

            Subject.Reconcile(_author);

            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_calibre, DeleteMediaFileReason.MissingFromDisk), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_audio, It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void a_present_audiobook_file_is_left_alone()
        {
            Subject.Reconcile(_author);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_audio, It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(_audio), Times.Never());
        }

        [Test]
        public void an_entry_row_is_never_read()
        {
            GivenCalibreBooks();

            Subject.Reconcile(_author);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBooks(It.Is<List<int>>(ids => ids.SequenceEqual(new[] { _calibre.CalibreId })), _config), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.FileExists(_entry.Path), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_entry, It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(_entry), Times.Never());
        }

        [Test]
        public void an_entry_with_no_off_entry_rows_asks_nothing()
        {
            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(_author.Id))
                .Returns(new List<BookFile> { _entry });

            Subject.Reconcile(_author);

            Mocker.GetMock<ILightNovelCalibreSettings>().Verify(v => v.ForConfig(), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBooks(It.IsAny<List<int>>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.FileExists(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.FolderExists(It.IsAny<string>()), Times.Never());
        }

        // Light-novel storage (2026-09-22): calibre / Audiobookshelf is not this install's home for that
        // kind any more; its rows keep their home and nobody judges them (a changed setting moves nothing).
        [Test]
        public void calibre_rows_are_left_alone_while_ebooks_go_to_the_entry_folder()
        {
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Entry);

            Subject.Reconcile(_author);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBooks(It.IsAny<List<int>>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_calibre, It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void audiobook_rows_are_left_alone_while_audio_goes_to_the_entry_folder()
        {
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.AudioHome).Returns(LightNovelHome.Entry);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FileExists(_audio.Path)).Returns(false);

            Subject.Reconcile(_author);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_audio, It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }
    }
}
