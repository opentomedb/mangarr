using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.MediaFiles;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Existing
{
    // One display title (2026-09-23, the maintainer): the whole-library / single-entry trigger for the calibre
    // title/sort sync (ICalibreTitleSyncService, shared with ImportExistingLightNovelsService's
    // adoption-time step) and the Audiobookshelf title sync (IAdoptedAudioSyncService) -- nothing
    // else attached here, no calibre/ABS matching, no rescan, no report. The payload-shape/
    // idempotence details of each sync live in their own fixtures (CalibreTitleSyncServiceFixture,
    // AdoptedAudioSyncServiceFixture); this fixture is wiring only -- which rows reach which sync,
    // and the storage gates.
    [TestFixture]
    public class SyncLightNovelTitlesServiceFixture : CoreTest<SyncLightNovelTitlesService>
    {
        private Author _lightNovel;
        private Author _manga;
        private CalibreSettings _calibreSettings;

        [SetUp]
        public void Setup()
        {
            _lightNovel = new Author { Id = 3, Metadata = new AuthorMetadata { Name = "Mushoku Tensei", ForeignAuthorId = "local-mushoku-tensei~ln" } };
            _manga = new Author { Id = 4, Metadata = new AuthorMetadata { Name = "One Piece", ForeignAuthorId = "local-one-piece" } };
            _calibreSettings = new CalibreSettings { Host = "calibre.test", Port = 8081, Library = "books" };

            LightNovelStorageMock.Setup(Mocker.GetMock<ILightNovelStorage>());

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Setup(s => s.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny))
                .Returns(true);

            Mocker.GetMock<ILightNovelCalibreSettings>().Setup(s => s.ForConfig()).Returns(_calibreSettings);
            Mocker.GetMock<ICalibreTitleSyncService>()
                .Setup(s => s.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), It.IsAny<CalibreSettings>(), out It.Ref<string>.IsAny))
                .Returns(true);
        }

        private BookFile GivenAudiobooksRow(int id, Author author)
        {
            return new BookFile { Id = id, Path = $"/srv/audiobooks/{author.Name}/Vol {id}.m4b", Home = FileHome.Audiobooks, Author = author };
        }

        private BookFile GivenCalibreRow(int id, int calibreId, Author author)
        {
            return new BookFile { Id = id, CalibreId = calibreId, Path = $"/lightnovels/{author.Name}/Vol {id}.epub", Home = FileHome.Calibre, Author = author };
        }

        [Test]
        public void one_author_syncs_only_that_authors_audiobookshelf_rows()
        {
            var row = GivenAudiobooksRow(1, _lightNovel);
            var calibreRow = GivenCalibreRow(2, 20, _lightNovel);

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(3)).Returns(_lightNovel);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(3)).Returns(new List<BookFile> { row, calibreRow });

            Subject.Execute(new SyncLightNovelTitlesCommand(3));

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(_lightNovel, It.Is<List<BookFile>>(l => l.Count == 1 && l[0] == row), out It.Ref<string>.IsAny), Times.Once());
        }

        [Test]
        public void no_author_id_syncs_every_light_novel_entry_and_skips_manga()
        {
            var lnRow = GivenAudiobooksRow(1, _lightNovel);

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAllAuthors()).Returns(new List<Author> { _lightNovel, _manga });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(3)).Returns(new List<BookFile> { lnRow });

            Subject.Execute(new SyncLightNovelTitlesCommand());

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(_lightNovel, It.Is<List<BookFile>>(l => l.Count == 1 && l[0] == lnRow), out It.Ref<string>.IsAny), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.GetFilesByAuthor(_manga.Id), Times.Never());
        }

        [Test]
        public void an_entry_with_no_audiobookshelf_rows_is_never_synced()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(3)).Returns(_lightNovel);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(3)).Returns(new List<BookFile>());

            Subject.Execute(new SyncLightNovelTitlesCommand(3));

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny), Times.Never());
        }

        [Test]
        public void the_ab_s_pass_is_skipped_when_audio_goes_to_the_entry_folder()
        {
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.AudioHome).Returns(LightNovelHome.Entry);
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(3)).Returns(_lightNovel);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(3)).Returns(new List<BookFile> { GivenAudiobooksRow(1, _lightNovel) });

            Subject.Execute(new SyncLightNovelTitlesCommand(3));

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny), Times.Never());
        }

        [Test]
        public void one_author_syncs_only_that_authors_calibre_rows()
        {
            var row = GivenCalibreRow(2, 20, _lightNovel);
            var audioRow = GivenAudiobooksRow(1, _lightNovel);

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(3)).Returns(_lightNovel);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(3)).Returns(new List<BookFile> { row, audioRow });

            Subject.Execute(new SyncLightNovelTitlesCommand(3));

            Mocker.GetMock<ICalibreTitleSyncService>()
                .Verify(v => v.Sync(_lightNovel, It.Is<List<BookFile>>(l => l.Count == 1 && l[0] == row), _calibreSettings, out It.Ref<string>.IsAny), Times.Once());
        }

        [Test]
        public void no_author_id_calibre_pass_covers_every_light_novel_entry_and_skips_manga()
        {
            var row = GivenCalibreRow(2, 20, _lightNovel);

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAllAuthors()).Returns(new List<Author> { _lightNovel, _manga });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(3)).Returns(new List<BookFile> { row });

            Subject.Execute(new SyncLightNovelTitlesCommand());

            Mocker.GetMock<ICalibreTitleSyncService>()
                .Verify(v => v.Sync(_lightNovel, It.Is<List<BookFile>>(l => l.Count == 1 && l[0] == row), _calibreSettings, out It.Ref<string>.IsAny), Times.Once());
        }

        [Test]
        public void an_entry_with_no_calibre_rows_is_never_synced()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(3)).Returns(_lightNovel);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(3)).Returns(new List<BookFile>());

            Subject.Execute(new SyncLightNovelTitlesCommand(3));

            Mocker.GetMock<ICalibreTitleSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), It.IsAny<CalibreSettings>(), out It.Ref<string>.IsAny), Times.Never());
        }

        [Test]
        public void the_calibre_pass_is_skipped_when_ebooks_go_to_the_entry_folder()
        {
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Entry);
            var row = GivenCalibreRow(2, 20, _lightNovel);
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(3)).Returns(_lightNovel);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(3)).Returns(new List<BookFile> { row });

            Subject.Execute(new SyncLightNovelTitlesCommand(3));

            Mocker.GetMock<ICalibreTitleSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), It.IsAny<CalibreSettings>(), out It.Ref<string>.IsAny), Times.Never());
        }

        [Test]
        public void a_manga_entry_is_never_touched()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAllAuthors()).Returns(new List<Author> { _manga });

            Subject.Execute(new SyncLightNovelTitlesCommand());

            Mocker.GetMock<IMediaFileService>().Verify(v => v.GetFilesByAuthor(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void a_failed_calibre_sync_warns()
        {
            var row = GivenCalibreRow(2, 20, _lightNovel);
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(3)).Returns(_lightNovel);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(3)).Returns(new List<BookFile> { row });
            Mocker.GetMock<ICalibreTitleSyncService>()
                .Setup(s => s.Sync(_lightNovel, It.IsAny<List<BookFile>>(), _calibreSettings, out It.Ref<string>.IsAny))
                .Returns(new CalibreTitleSync((Author a, List<BookFile> f, CalibreSettings s, out string failure) =>
                {
                    failure = "library read failed: calibre is down";
                    return false;
                }));

            Subject.Execute(new SyncLightNovelTitlesCommand(3));

            ExceptionVerification.ExpectedWarns(1);
        }

        // Moq's out-parameter Returns needs a delegate matching ICalibreTitleSyncService.Sync's
        // signature exactly (the same pattern ImportExistingLightNovelsServiceFixture.ShelfSync uses).
        private delegate bool CalibreTitleSync(Author author, List<BookFile> files, CalibreSettings settings, out string failure);
    }
}
