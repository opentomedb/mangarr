using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Existing
{
    // One display title (2026-09-23, the maintainer); fix round 1: the calibre half of the one-title feature,
    // shared by SyncLightNovelTitlesService and ImportExistingLightNovelsService (L5). One GetBooks
    // read per call; CalibreProxy.SetTitle only where calibre's title OR sort disagrees (I1); an
    // unnumbered volume (L1) or a row calibre no longer has (L3) is skipped, never written; a
    // per-item failure is a Warn and the loop continues (L4).
    [TestFixture]
    public class CalibreTitleSyncServiceFixture : CoreTest<CalibreTitleSyncService>
    {
        private Author _author;
        private CalibreSettings _settings;

        [SetUp]
        public void Setup()
        {
            _author = new Author { Id = 3, Metadata = new AuthorMetadata { Name = "Mushoku Tensei", ForeignAuthorId = "local-mushoku-tensei~ln" } };
            _settings = new CalibreSettings { Host = "calibre.test", Port = 8081, Library = "books" };

            Mocker.GetMock<ICalibreProxy>()
                .Setup(s => s.SetTitle(It.IsAny<BookFile>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CalibreSettings>()));
        }

        private BookFile GivenRow(int calibreId, double volumeNumber, string subtitle = null)
        {
            var book = new Book { Id = calibreId * 10, VolumeNumber = volumeNumber, Subtitle = subtitle, Author = _author };
            var edition = book.WithEdition(MediaType.Ebook, id: (calibreId * 100) + 1);

            return new BookFile { Id = calibreId, CalibreId = calibreId, Path = $"/lightnovels/Mushoku Tensei/Vol {volumeNumber}.epub", Edition = edition, Author = _author };
        }

        private void GivenCalibreBooks(params CalibreBook[] books)
        {
            Mocker.GetMock<ICalibreProxy>()
                .Setup(s => s.GetBooks(It.IsAny<List<int>>(), _settings))
                .Returns(books.ToList());
        }

        private void VerifyNoSetTitle()
        {
            Mocker.GetMock<ICalibreProxy>()
                .Verify(v => v.SetTitle(It.IsAny<BookFile>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CalibreSettings>()), Times.Never());
        }

        [Test]
        public void a_row_whose_calibre_title_differs_is_set_title()
        {
            var row = GivenRow(20, 1);
            GivenCalibreBooks(new CalibreBook { Id = 20, Title = "Mushoku Tensei Vol 1", Sort = "Mushoku Tensei 0001" });

            var aligned = Subject.Sync(_author, new List<BookFile> { row }, _settings, out var failure);

            aligned.Should().BeTrue();
            failure.Should().BeNull();
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.SetTitle(row, "Mushoku Tensei (Vol. 1)", "Mushoku Tensei 0001", _settings), Times.Once());
        }

        // I1: the title already matches, but the sort does not (e.g. a pre-fix-round-1 build wrote
        // the title without ever reaching the sort, or a user changed it by hand) -- still repaired.
        [Test]
        public void a_row_whose_calibre_sort_differs_from_a_matching_title_is_still_set_title()
        {
            var row = GivenRow(20, 1);
            GivenCalibreBooks(new CalibreBook { Id = 20, Title = "Mushoku Tensei (Vol. 1)", Sort = "wrong sort" });

            Subject.Sync(_author, new List<BookFile> { row }, _settings, out _);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.SetTitle(row, "Mushoku Tensei (Vol. 1)", "Mushoku Tensei 0001", _settings), Times.Once());
        }

        [Test]
        public void a_row_whose_title_and_sort_already_match_is_left_alone()
        {
            var row = GivenRow(20, 1);
            GivenCalibreBooks(new CalibreBook { Id = 20, Title = "Mushoku Tensei (Vol. 1)", Sort = "Mushoku Tensei 0001" });

            var aligned = Subject.Sync(_author, new List<BookFile> { row }, _settings, out var failure);

            aligned.Should().BeTrue();
            failure.Should().BeNull();
            VerifyNoSetTitle();
        }

        // L1: an unnumbered volume keeps edition.Title in calibre (CalibreProxy.SetFields) -- never
        // touched here, so the two stores still agree.
        [Test]
        public void an_unnumbered_volume_is_skipped()
        {
            var row = GivenRow(20, 0);
            GivenCalibreBooks(new CalibreBook { Id = 20, Title = "whatever calibre has" });

            Subject.Sync(_author, new List<BookFile> { row }, _settings, out _);

            VerifyNoSetTitle();
        }

        // L3: the id is gone from calibre (deleted there); OffEntryFileReconciler owns forgetting
        // the row, never this pass.
        [Test]
        public void a_row_calibre_no_longer_has_is_skipped()
        {
            var row = GivenRow(20, 1);
            GivenCalibreBooks(); // GetBooks answers with nothing for id 20

            var aligned = Subject.Sync(_author, new List<BookFile> { row }, _settings, out var failure);

            aligned.Should().BeTrue();
            failure.Should().BeNull();
            VerifyNoSetTitle();
        }

        [Test]
        public void a_row_with_no_loaded_book_is_skipped()
        {
            var row = new BookFile { Id = 20, CalibreId = 20, Path = "/lightnovels/x.epub", Author = _author };
            GivenCalibreBooks(new CalibreBook { Id = 20, Title = "whatever" });

            Subject.Sync(_author, new List<BookFile> { row }, _settings, out _);

            VerifyNoSetTitle();
        }

        [Test]
        public void no_rows_reads_nothing()
        {
            var aligned = Subject.Sync(_author, new List<BookFile>(), _settings, out var failure);

            aligned.Should().BeTrue();
            failure.Should().BeNull();
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBooks(It.IsAny<List<int>>(), It.IsAny<CalibreSettings>()), Times.Never());
        }

        [Test]
        public void a_library_read_failure_is_a_warning_and_a_false_result()
        {
            var row = GivenRow(20, 1);
            Mocker.GetMock<ICalibreProxy>()
                .Setup(s => s.GetBooks(It.IsAny<List<int>>(), _settings))
                .Throws(new InvalidOperationException("calibre is down"));

            var aligned = Subject.Sync(_author, new List<BookFile> { row }, _settings, out var failure);

            aligned.Should().BeFalse();
            failure.Should().Be("library read failed: calibre is down");
            VerifyNoSetTitle();
            ExceptionVerification.ExpectedWarns(1);
        }

        // L4: a per-row SetTitle failure is a Warn, the next row still runs, and the result is false.
        [Test]
        public void a_failed_set_title_on_one_row_warns_and_the_next_row_still_runs()
        {
            var row1 = GivenRow(20, 1);
            var row2 = GivenRow(21, 2);
            GivenCalibreBooks(
                new CalibreBook { Id = 20, Title = "old" },
                new CalibreBook { Id = 21, Title = "old" });

            Mocker.GetMock<ICalibreProxy>()
                .Setup(s => s.SetTitle(row1, It.IsAny<string>(), It.IsAny<string>(), _settings))
                .Throws(new InvalidOperationException("500"));

            var aligned = Subject.Sync(_author, new List<BookFile> { row1, row2 }, _settings, out var failure);

            aligned.Should().BeFalse();
            failure.Should().Be("1 title(s) failed");
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.SetTitle(row2, "Mushoku Tensei (Vol. 2)", "Mushoku Tensei 0002", _settings), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_subtitle_folds_into_the_title_sent_to_calibre()
        {
            var row = GivenRow(20, 1, subtitle: "Aincrad");
            GivenCalibreBooks(new CalibreBook { Id = 20, Title = "old" });

            Subject.Sync(_author, new List<BookFile> { row }, _settings, out _);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.SetTitle(row, "Mushoku Tensei: Aincrad (Vol. 1)", "Mushoku Tensei 0001", _settings), Times.Once());
        }

        // I1-bis, fix round 2 (2026-09-23): end to end, through the REAL CalibreProxy (mocked
        // IHttpClient serving calibre-shaped JSON, real Newtonsoft deserialization) rather than an
        // in-memory CalibreBook -- the gap the re-review found (CalibreBook.Sort not reading
        // calibre's READ-side key, title_sort). A book already at the display title/sort must send
        // no write at all: IHttpClient.Execute (set-fields) is the proxy's only write call, so
        // verifying it was never made is correct however ICalibreProxy was wired into Subject.
        [Test]
        public void a_book_already_in_sync_reads_the_sort_from_title_sort_and_writes_nothing()
        {
            Mocker.SetConstant<ICalibreProxy>(Mocker.Resolve<CalibreProxy>());

            var row = GivenRow(20, 1);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<Dictionary<int, CalibreBook>>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<Dictionary<int, CalibreBook>>(new HttpResponse(r, new HttpHeader(),
                    "{\"20\": {\"application_id\": 20, \"title\": \"Mushoku Tensei (Vol. 1)\", \"title_sort\": \"Mushoku Tensei 0001\", \"format_metadata\": {}}}")));

            var aligned = Subject.Sync(_author, new List<BookFile> { row }, _settings, out var failure);

            aligned.Should().BeTrue();
            failure.Should().BeNull();
            Mocker.GetMock<IHttpClient>().Verify(v => v.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }
    }
}
