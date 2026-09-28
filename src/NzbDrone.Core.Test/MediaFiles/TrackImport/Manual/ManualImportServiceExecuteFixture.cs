using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Manual
{
    // Light novels (2026-09): the manual-import command imports one volume's EPUB and its audio
    // parts as two groups, so PartCount counts the parts of ONE edition and never the EPUB.
    [TestFixture]
    public class ManualImportServiceExecuteFixture : CoreTest<ManualImportService>
    {
        private Author _author;
        private Book _volume;
        private Edition _ebook;
        private Edition _audio;
        private List<List<ImportDecision<LocalBook>>> _imports;
        private List<DownloadClientItem> _items;

        [SetUp]
        public void Setup()
        {
            _author = new Author { Id = 1, Name = "Overlord", Path = "/books/Overlord" };
            _volume = new Book { Id = 5, Title = "Overlord Vol. 5", ForeignBookId = "1-v5" };
            _ebook = _volume.WithEdition(MediaType.Ebook, id: 51);
            _ebook.ForeignEditionId = "1-v5-ed";
            _audio = _volume.WithEdition(MediaType.Audio, id: 52);
            _audio.ForeignEditionId = "1-v5-audio-ed";
            _imports = new List<List<ImportDecision<LocalBook>>>();
            _items = new List<DownloadClientItem>();

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.GetAuthor(_author.Id))
                .Returns(_author);

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBook(_volume.Id))
                .Returns(_volume);

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionByForeignEditionId(_ebook.ForeignEditionId))
                .Returns(_ebook);

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionByForeignEditionId(_audio.ForeignEditionId))
                .Returns(_audio);

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder(It.IsAny<string>()))
                .Returns(new RootFolder { Path = "/books" });

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFileInfo(It.IsAny<string>()))
                .Returns<string>(path =>
                {
                    var info = new Mock<IFileInfo>();
                    info.SetupGet(i => i.FullName).Returns(path);
                    info.SetupGet(i => i.Length).Returns(100);
                    info.SetupGet(i => i.LastWriteTimeUtc).Returns(DateTime.UtcNow);
                    return info.Object;
                });

            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                .Callback<List<ImportDecision<LocalBook>>, bool, DownloadClientItem, ImportMode>((decisions, replace, item, mode) =>
                {
                    _imports.Add(decisions);
                    _items.Add(item);
                })
                .Returns(new List<ImportResult>());
        }

        private ManualImportFile File(string path, Edition edition)
        {
            return new ManualImportFile
            {
                Path = path,
                AuthorId = _author.Id,
                BookId = edition.BookId,
                ForeignEditionId = edition.ForeignEditionId
            };
        }

        // Beta readiness (2026-09-28, review M4): the service path behind Write ComicInfo To = New Downloads. A
        // Manual Import of loose files (no DownloadId on the row, i.e. not opened from the Queue) passes no
        // download-client item, so its BookImportedEvent has no DownloadId; a row from a queue item passes the
        // tracked download's item, whose DownloadId the event carries (ImportApprovedTracksFixture).
        [Test]
        public void execute_passes_no_download_client_item_for_loose_files()
        {
            Subject.Execute(new ManualImportCommand
            {
                Files = new List<ManualImportFile> { File("/books/Overlord/Overlord - Vol 005.epub", _ebook) },
                ImportMode = ImportMode.Auto
            });

            _items.Should().ContainSingle().Which.Should().BeNull();
        }

        [Test]
        public void execute_passes_the_tracked_downloads_item_for_a_queue_item()
        {
            var item = new DownloadClientItem { DownloadId = "SABnzbd_nzo_abc123", Title = "Overlord v05", CanMoveFiles = true };

            Mocker.GetMock<ITrackedDownloadService>()
                .Setup(s => s.Find("SABnzbd_nzo_abc123"))
                .Returns(new TrackedDownload { DownloadItem = item, ImportItem = item });

            var file = File("/downloads/Overlord/Overlord - Vol 005.epub", _ebook);
            file.DownloadId = "SABnzbd_nzo_abc123";

            Subject.Execute(new ManualImportCommand { Files = new List<ManualImportFile> { file }, ImportMode = ImportMode.Auto });

            _items.Should().ContainSingle().Which.DownloadId.Should().Be("SABnzbd_nzo_abc123");
        }

        [Test]
        public void execute_imports_each_edition_of_a_volume_as_its_own_group()
        {
            Subject.Execute(new ManualImportCommand
            {
                Files = new List<ManualImportFile>
                {
                    File("/downloads/Overlord/Overlord - Vol 005.epub", _ebook),
                    File("/downloads/Overlord/Overlord - Vol 005 - 01.mp3", _audio),
                    File("/downloads/Overlord/Overlord - Vol 005 - 02.mp3", _audio)
                },
                ImportMode = ImportMode.Auto
            });

            _imports.Should().HaveCount(2);

            var ebookGroup = _imports.Single(g => g.All(d => d.Item.Edition == _ebook));
            var audioGroup = _imports.Single(g => g.All(d => d.Item.Edition == _audio));

            ebookGroup.Should().HaveCount(1);
            ebookGroup.Should().OnlyContain(d => d.Item.PartCount == 1);

            audioGroup.Should().HaveCount(2);
            audioGroup.Should().OnlyContain(d => d.Item.PartCount == 2);
        }

        // Preferred Edition (2026-09-24, M5 fix round 1): an edition that has to be minted from the
        // catalogue for a series whose bound line vanished skips that file with one Warn; the other
        // files still import.
        [Test]
        public void execute_skips_a_file_whose_edition_is_unavailable()
        {
            var missing = new ManualImportFile { Path = "/downloads/Overlord/Overlord - Vol 005 - 03.mp3", AuthorId = _author.Id, BookId = _volume.Id, ForeignEditionId = "1-v5-gone-ed" };

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(s => s.GetBookInfo(_volume.ForeignBookId))
                .Throws(new EditionUnavailableException("Overlord", "fr", "rl_fr"));

            Subject.Execute(new ManualImportCommand
            {
                Files = new List<ManualImportFile>
                {
                    File("/downloads/Overlord/Overlord - Vol 005.epub", _ebook),
                    missing
                },
                ImportMode = ImportMode.Auto
            });

            _imports.SelectMany(g => g).Should().ContainSingle(d => d.Item.Edition == _ebook);
            _imports.SelectMany(g => g).Should().NotContain(d => d.Item.Path == missing.Path);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void execute_keeps_one_group_per_manga_volume()
        {
            var manga = new Book { Id = 6, Title = "Dandadan Vol. 1", ForeignBookId = "1-v1" };
            var archive = manga.WithEdition(MediaType.Archive, id: 61);
            archive.ForeignEditionId = "1-v1-ed";

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBook(manga.Id))
                .Returns(manga);

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionByForeignEditionId(archive.ForeignEditionId))
                .Returns(archive);

            Subject.Execute(new ManualImportCommand
            {
                Files = new List<ManualImportFile>
                {
                    File("/downloads/Dandadan/Dandadan - Vol 001.cbz", archive)
                },
                ImportMode = ImportMode.Auto
            });

            _imports.Should().HaveCount(1);
            _imports[0].Should().HaveCount(1);
            _imports[0].Should().OnlyContain(d => d.Item.Edition == archive && d.Item.PartCount == 1);
        }

        // LN PDF (2026-09-22): the command carries the modal's quality, which stays the manga PDF
        // after a reassignment (UpdateItems only replaces Unknown). Execute knows the author, so a
        // light novel's PDF is imported as Ebook PDF; a manga PDF stays PDF.
        [Test]
        public void execute_imports_a_light_novel_pdf_as_ebook_pdf()
        {
            _author.Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord~ln" };

            var file = File("/downloads/Overlord/Overlord - Vol 005.pdf", _ebook);
            file.Quality = new QualityModel(Quality.PDF, new Revision(version: 2));

            Subject.Execute(new ManualImportCommand { Files = new List<ManualImportFile> { file }, ImportMode = ImportMode.Auto });

            var imported = _imports.Single().Single().Item;
            imported.Quality.Quality.Should().Be(Quality.EbookPdf);
            imported.Quality.Revision.Version.Should().Be(2);
        }

        [Test]
        public void execute_keeps_a_manga_pdf_as_pdf()
        {
            var manga = new Book { Id = 6, Title = "Dandadan Vol. 1", ForeignBookId = "1-v1" };
            var archive = manga.WithEdition(MediaType.Archive, id: 61);
            archive.ForeignEditionId = "1-v1-ed";

            Mocker.GetMock<IBookService>().Setup(s => s.GetBook(manga.Id)).Returns(manga);
            Mocker.GetMock<IEditionService>().Setup(s => s.GetEditionByForeignEditionId(archive.ForeignEditionId)).Returns(archive);

            var file = File("/downloads/Dandadan/Dandadan - Vol 001.pdf", archive);
            file.Quality = new QualityModel(Quality.PDF);

            Subject.Execute(new ManualImportCommand { Files = new List<ManualImportFile> { file }, ImportMode = ImportMode.Auto });

            _imports.Single().Single().Item.Quality.Quality.Should().Be(Quality.PDF);
        }

        // Fix round 1 (2026-09-22): the reverse also has to hold -- a volume already graded Ebook
        // PDF (an earlier light-novel regrade) reassigned to a manga entry in the modal must demote
        // back to the manga PDF, since the manga profile never offers Ebook PDF.
        [Test]
        public void execute_demotes_an_ebook_pdf_reassigned_to_a_manga_entry()
        {
            var manga = new Book { Id = 6, Title = "Dandadan Vol. 1", ForeignBookId = "1-v1" };
            var archive = manga.WithEdition(MediaType.Archive, id: 61);
            archive.ForeignEditionId = "1-v1-ed";

            Mocker.GetMock<IBookService>().Setup(s => s.GetBook(manga.Id)).Returns(manga);
            Mocker.GetMock<IEditionService>().Setup(s => s.GetEditionByForeignEditionId(archive.ForeignEditionId)).Returns(archive);

            var file = File("/downloads/Dandadan/Dandadan - Vol 001.pdf", archive);
            file.Quality = new QualityModel(Quality.EbookPdf, new Revision(version: 3));

            Subject.Execute(new ManualImportCommand { Files = new List<ManualImportFile> { file }, ImportMode = ImportMode.Auto });

            var imported = _imports.Single().Single().Item;
            imported.Quality.Quality.Should().Be(Quality.PDF);
            imported.Quality.Revision.Version.Should().Be(3);
        }
    }
}
