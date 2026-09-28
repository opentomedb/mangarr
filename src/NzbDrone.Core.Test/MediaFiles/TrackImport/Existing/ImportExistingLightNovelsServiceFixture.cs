using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.MediaFiles;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Existing
{
    // Light novels (2026-09, D12) / one copy each (2026-09-20): the Adopt Existing pass. Calibre's
    // content server and Audiobookshelf are mocked at the HTTP layer, calibre's file paths at
    // ICalibreProxy, the disk at IDiskProvider, the import pipeline at IMakeImportDecision /
    // IImportApprovedBooks; the assertions are the decisions handed to the pipeline, the holding
    // moves and the report.
    [TestFixture]
    public class ImportExistingLightNovelsServiceFixture : CoreTest<ImportExistingLightNovelsService>
    {
        private const string CalibreUrl = "http://calibre.test:8081";
        private const string AbsUrl = "http://abs.test";
        private const string AbsLibrary = "lib-1234";

        private const string Epub101 = "/books/Kugane Maruyama/Overlord, Vol. 5 (101)/Overlord, Vol. 5 - Kugane Maruyama.epub";
        private const string Epub102 = "/books/Kugane Maruyama/Overlord, Vol. 6 (102)/Overlord, Vol. 6 - Kugane Maruyama.epub";
        private const string Copy6 = "/lightnovels/Overlord/Overlord - Vol. 6/Overlord - Vol 006.epub";
        private const string Held6 = "/lightnovels/.adopted-holding/Overlord/Overlord - Vol. 6/Overlord - Vol 006.epub";

        private static readonly string[] Abs1Files =
        {
            "/srv/audiobooks/Overlord/Overlord Vol 5/Overlord Vol 5 - 01.mp3",
            "/srv/audiobooks/Overlord/Overlord Vol 5/Overlord Vol 5 - 02.mp3",
            "/srv/audiobooks/Overlord/Overlord Vol 5/Overlord Vol 5 - 03.mp3"
        };

        private Author _author;
        private Book _vol5;
        private Book _vol6;

        // every GetImportDecisions call: the overrides it was handed and how many rows the edition's
        // lazy file list held AT THAT MOMENT (a stale copy row would trip AdoptedEditionSpecification)
        private List<(List<string> Files, IdentificationOverrides Overrides, ImportDecisionMakerConfig Config, int EditionFilesAtDecision)> _decisionCalls;
        private List<ImportDecision<LocalBook>> _imported;

        [SetUp]
        public void Setup()
        {
            LightNovelStorageMock.Setup(Mocker.GetMock<ILightNovelStorage>());

            _author = new Author
            {
                Id = 3,
                Name = "Overlord",
                Path = "/lightnovels/Overlord",
                Monitored = true,
                QualityProfileId = 1,
                Metadata = new AuthorMetadata
                {
                    Name = "Overlord",
                    ForeignAuthorId = "local-overlord~ln",
                    Aliases = new List<string> { "Overlord (Light Novel)" }
                }
            };

            _vol5 = Volume(5, epubCopy: false);
            _vol6 = Volume(6, epubCopy: true);

            _decisionCalls = new List<(List<string>, IdentificationOverrides, ImportDecisionMakerConfig, int)>();
            _imported = new List<ImportDecision<LocalBook>>();

            Mocker.GetMock<IConfigService>().SetupGet(s => s.CalibreContentServerUrl).Returns(CalibreUrl);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.AudiobookshelfUrl).Returns(AbsUrl);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.AudiobookshelfApiKey).Returns("abs-key");
            Mocker.GetMock<IConfigService>().SetupGet(s => s.AudiobookshelfLibraryId).Returns(AbsLibrary);

            // the real clients over the mocked IHttpClient (AutoMoq would hand the service a mock of
            // each client interface otherwise, and the HTTP setups below would never be reached)
            Mocker.SetConstant<ICalibreContentServerClient>(Mocker.Resolve<CalibreContentServerClient>());
            Mocker.SetConstant<IAudiobookshelfClient>(Mocker.Resolve<AudiobookshelfClient>());

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(3)).Returns(_author);
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAllAuthors()).Returns(new List<Author> { _author });
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(3)).Returns(new List<Book> { _vol5, _vol6 });

            Mocker.GetMock<IAppFolderInfo>().SetupGet(s => s.AppDataFolder).Returns("/config");

            // the entry's rows as the rescan left them: none until a case says otherwise; the shelf
            // sync aligns them unless a case says otherwise
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(It.IsAny<int>())).Returns(new List<BookFile>());
            GivenShelfSync(true, null);
            GivenCalibreTitleSync(true, null);

            // calibre's own view of each EPUB (Readarr's proxy, the settings from config)
            Mocker.GetMock<ILightNovelCalibreSettings>().Setup(s => s.ForConfig()).Returns(new CalibreSettings { Host = "calibre.test", Port = 8081, Library = "books" });
            GivenCalibreEpub(101, Epub101, 2000);
            GivenCalibreEpub(102, Epub102, 2000);

            // the originals exist and open; a copy-era row is verified against them before it is held
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(It.Is<string>(p => p.StartsWith("/books/") || p.StartsWith("/srv/audiobooks/"))))
                .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFileSize(It.IsAny<string>()))
                .Returns(2000);
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetParentFolder(It.IsAny<string>()))
                .Returns<string>(p => p.GetParentPath());
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFileInfo(It.IsAny<string>()))
                .Returns<string>(path =>
                {
                    var info = new Mock<IFileInfo>();
                    info.SetupGet(i => i.FullName).Returns(path);
                    return info.Object;
                });

            // the import pipeline: one approved decision per file, identified as the overrides say;
            // Import records what it was handed and succeeds unless a case says otherwise
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((files, overrides, info, config) =>
                    _decisionCalls.Add((files.Select(f => f.FullName).ToList(), overrides, config, overrides.Edition.BookFiles.Value.Count)))
                .Returns<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((files, overrides, info, config) =>
                    files.Select(f => new ImportDecision<LocalBook>(new LocalBook
                    {
                        Path = f.FullName,
                        Author = overrides.Author,
                        Book = overrides.Book,
                        Edition = overrides.Edition,
                        ExistingFile = !config.NewDownload
                    })).ToList());

            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                .Callback<List<ImportDecision<LocalBook>>, bool, DownloadClientItem, ImportMode>((decisions, replace, item, mode) => _imported.AddRange(decisions))
                .Returns<List<ImportDecision<LocalBook>>, bool, DownloadClientItem, ImportMode>((decisions, replace, item, mode) =>
                    decisions.Select(d => d.Approved ? new ImportResult(d) : new ImportResult(d, d.Rejections.Select(r => r.Reason).ToArray())).ToList());

            GivenCalibreSearch(101, 102, 103);
            GivenCalibreBook(101, "Overlord, Vol. 5", "Overlord", 5.0, "EPUB");
            GivenCalibreBook(102, "Overlord, Vol. 6", "Overlord", 6.0, "EPUB");
            GivenCalibreBook(103, "Overlord: The Undead King", "Overlord Manga", 1.0, "CBZ");

            GivenAudiobookshelfItems(
                AbsItem("abs-1", "/audiobooks/Overlord/Overlord Vol 5", "Overlord, Vol. 5 (Light Novel)", "Overlord #5"),
                AbsItem("abs-2", "/audiobooks/KonoSuba/KonoSuba Vol 1", "KonoSuba, Vol. 1", "KonoSuba #1"),
                AbsItem("abs-3", "/audiobooks/Overlord/Overlord Vol 9", "Overlord, Vol. 9 (Light Novel)", "Overlord #9"));

            // three parts, listed out of order, plus the cover ABS keeps beside them
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles("/srv/audiobooks/Overlord/Overlord Vol 5", true))
                .Returns(new[]
                {
                    "/srv/audiobooks/Overlord/Overlord Vol 5/cover.jpg",
                    Abs1Files[2],
                    Abs1Files[0],
                    Abs1Files[1]
                });
        }

        // epubCopy: the copy-in era's EPUB under the entry folder (an Entry row); audioFiles: the audio
        // edition's rows, none by default.
        private Book Volume(int number, bool epubCopy, bool ebookMonitored = true, bool audioMonitored = true, List<BookFile> audioFiles = null)
        {
            var volume = new Book { Id = number, VolumeNumber = number, Title = $"{_author.Name} Vol. {number}", Author = _author };

            var epubFiles = epubCopy
                ? new List<BookFile> { EntryCopy(number, $"/lightnovels/{_author.Name}/{_author.Name} - Vol. {number}/{_author.Name} - Vol {number:000}.epub", (number * 10) + 1) }
                : new List<BookFile>();

            volume.WithEdition(MediaType.Ebook, epubFiles, id: (number * 10) + 1, monitored: ebookMonitored);
            volume.WithEdition(MediaType.Audio, audioFiles, id: (number * 10) + 2, monitored: audioMonitored);

            return volume;
        }

        private static BookFile EntryCopy(int id, string path, int editionId, int part = 1)
        {
            return new BookFile { Id = id, Path = path, EditionId = editionId, Part = part, Size = 1000, Home = FileHome.Entry, Adopted = false };
        }

        // format_metadata as the live content server keys it: lowercase "epub" (checked 2026-09-20).
        private void GivenCalibreEpub(int id, string path, long size, string formatKey = "epub")
        {
            var book = new CalibreBook
            {
                Id = id,
                Formats = new Dictionary<string, CalibreBookFormat> { { formatKey, new CalibreBookFormat { Path = path, Size = size } } }
            };

            Mocker.GetMock<ICalibreProxy>().Setup(s => s.GetBook(id, It.IsAny<CalibreSettings>())).Returns(book);
        }

        // Covered volumes (2026-09-17, D4): the live entry whose Audiobookshelf items are packs. Vols 1-4,
        // no Calibre hits; the ABS items are each test's.
        private const string TbatePack1 = "The Beginning After the End: Publisher's Pack";
        private const string TbatePack2 = "The Beginning After the End: Publisher's Pack 2: The Beginning After the End, Books 3-4";

        private List<Book> GivenTbate()
        {
            _author = new Author
            {
                Id = 7,
                Name = "The Beginning After the End",
                Path = "/lightnovels/The Beginning After the End",
                Monitored = true,
                QualityProfileId = 1,
                Metadata = new AuthorMetadata
                {
                    Name = "The Beginning After the End",
                    ForeignAuthorId = "local-the-beginning-after-the-end~ln",
                    Aliases = new List<string>()
                }
            };

            var volumes = new List<Book>
            {
                Volume(1, epubCopy: false),
                Volume(2, epubCopy: false),
                Volume(3, epubCopy: false),
                Volume(4, epubCopy: false)
            };

            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(7)).Returns(volumes);
            GivenCalibreSearch();

            return volumes;
        }

        // Audible's cached products for the entry: the two packs and the singles (2026-09-17).
        private void GivenAudiblePacks()
        {
            var products = new List<AudibleProduct>
            {
                new AudibleProduct { Asin = "1774241307", Title = "The Beginning After the End: Publisher's Pack", Sequence = "1-2" },
                new AudibleProduct { Asin = "1774242036", Title = "The Beginning After the End: Publisher's Pack 2", Sequence = "3-4" }
            };

            products.AddRange(Enumerable.Range(1, 11).Select(n => new AudibleProduct { Asin = $"single-{n}", Title = $"The Beginning After the End, Book {n}", Sequence = n.ToString() }));

            Mocker.GetMock<IAudibleCatalogService>()
                .Setup(s => s.GetSeries("The Beginning After the End", It.IsAny<IEnumerable<string>>()))
                .Returns(products);
        }

        // The real service marks the covered volumes' Audio editions and hands them back; the mock does the
        // same so the report can name them.
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

        private void VerifyMarked(Book carrier, Book covered, string reason, Times times)
        {
            Mocker.GetMock<ICoveredVolumeService>()
                .Verify(v => v.MarkCoveredByImport(carrier, It.Is<IEnumerable<Book>>(c => c.Single() == covered), reason), times);
        }

        private void VerifyNothingMarked()
        {
            Mocker.GetMock<ICoveredVolumeService>()
                .Verify(v => v.MarkCoveredByImport(It.IsAny<Book>(), It.IsAny<IEnumerable<Book>>(), It.IsAny<string>()), Times.Never());
        }

        private delegate bool ShelfSync(Author author, List<BookFile> rows, out string failure);

        private void GivenShelfSync(bool aligned, string failure)
        {
            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Setup(s => s.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny))
                .Returns(new ShelfSync((Author a, List<BookFile> r, out string f) =>
                {
                    f = failure;
                    return aligned;
                }));
        }

        // L5: the adoption's calibre-side title step (CalibreTitleSyncService), same pattern as
        // GivenShelfSync above.
        private delegate bool CalibreSync(Author author, List<BookFile> rows, CalibreSettings settings, out string failure);

        private void GivenCalibreTitleSync(bool aligned, string failure)
        {
            Mocker.GetMock<ICalibreTitleSyncService>()
                .Setup(s => s.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), It.IsAny<CalibreSettings>(), out It.Ref<string>.IsAny))
                .Returns(new CalibreSync((Author a, List<BookFile> r, CalibreSettings s, out string f) =>
                {
                    f = failure;
                    return aligned;
                }));
        }

        // The decision the pipeline imported for this path, or null.
        private LocalBook Imported(string path)
        {
            return _imported.SingleOrDefault(d => d.Item.Path == path)?.Item;
        }

        private void VerifyAdopted(string path, int editionId, FileHome home, int part, int partCount, int calibreId = 0)
        {
            var item = Imported(path);

            item.Should().NotBeNull($"{path} should have been imported");
            item.Edition.Id.Should().Be(editionId);
            item.Home.Should().Be(home);
            item.Adopted.Should().BeTrue();
            item.Part.Should().Be(part);
            item.PartCount.Should().Be(partCount);
            item.CalibreId.Should().Be(calibreId);
        }

        private void VerifyNothingAdoptedFrom(string prefix)
        {
            _imported.Should().NotContain(d => d.Item.Path.StartsWith(prefix));
            _decisionCalls.Should().NotContain(c => c.Files.Any(f => f.StartsWith(prefix)));
        }

        private void VerifyNoFileCopiedOrDeleted()
        {
            Mocker.GetMock<IDiskProvider>().Verify(v => v.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(v => v.DownloadFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        private static HttpResponse JsonResponse(HttpRequest request, string json)
        {
            return new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(json));
        }

        private void GivenCalibreSearch(params int[] ids)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/ajax/search/books"))))
                .Returns<HttpRequest>(r => JsonResponse(r, "{\"total_num\":" + ids.Length + ",\"book_ids\":[" + string.Join(",", ids) + "]}"));
        }

        private void GivenCalibreBook(int id, string title, string series, double index, params string[] formats)
        {
            var json = "{\"title\":\"" + title + "\",\"series\":\"" + series + "\",\"series_index\":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                       ",\"formats\":[" + string.Join(",", formats.Select(f => "\"" + f + "\"")) + "]}";

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains($"/ajax/book/{id}/books"))))
                .Returns<HttpRequest>(r => JsonResponse(r, json));
        }

        // asin: the key is left out entirely when null, as ABS leaves it for an unmatched item (B3b).
        private static string AbsItem(string id, string path, string title, string seriesName, bool isFile = false, string asin = null)
        {
            var asinField = asin == null ? string.Empty : ",\"asin\":\"" + asin + "\"";

            return "{\"id\":\"" + id + "\",\"path\":\"" + path + "\",\"isFile\":" + (isFile ? "true" : "false") + ",\"media\":{\"metadata\":{\"title\":\"" + title + "\",\"seriesName\":\"" + seriesName + "\"" + asinField + "}}}";
        }

        private static HttpException HttpError(string url, System.Net.HttpStatusCode status)
        {
            return new HttpException(new HttpRequest(url), new HttpResponse(new HttpRequest(url), new HttpHeader(), string.Empty, status));
        }

        private void GivenAudiobookshelfItems(params string[] items)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains($"/api/libraries/{AbsLibrary}/items"))))
                .Returns<HttpRequest>(r => JsonResponse(r, "{\"results\":[" + string.Join(",", items) + "],\"total\":" + items.Length + "}"));
        }

        private void GivenAudiobookshelfFails()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/api/libraries/"))))
                .Throws(HttpError(AbsUrl, System.Net.HttpStatusCode.Unauthorized));
        }

        // (a) the original is registered in place through the normal pipeline, identified as this
        // volume's EPUB edition: nothing is downloaded, nothing is created under the entry.
        [Test]
        public void should_adopt_the_calibre_epub_of_a_volume_whose_ebook_edition_has_no_file()
        {
            var report = Subject.ImportAuthor(_author);

            var call = _decisionCalls.Single(c => c.Files.Contains(Epub101));
            call.Files.Should().Equal(Epub101);
            call.Overrides.Author.Should().BeSameAs(_author);
            call.Overrides.Book.Should().BeSameAs(_vol5);
            call.Overrides.Edition.Id.Should().Be(51);
            call.Config.Filter.Should().Be(FilterFilesType.None);
            call.Config.NewDownload.Should().BeFalse();
            call.Config.IncludeExisting.Should().BeTrue();
            call.Config.AddNewAuthors.Should().BeFalse();

            VerifyAdopted(Epub101, 51, FileHome.Calibre, 1, 1, calibreId: 101);

            Mocker.GetMock<IImportApprovedBooks>()
                .Verify(v => v.Import(It.Is<List<ImportDecision<LocalBook>>>(d => d.Any(x => x.Item.Path == Epub101)), false, null, ImportMode.Auto), Times.Once());

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBook(101, It.IsAny<CalibreSettings>()), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.CreateFolder(It.Is<string>(p => p.StartsWith("/lightnovels/Overlord"))), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(v => v.DownloadFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            report.Adopted.Should().Contain(s => s.StartsWith("calibre #101 'Overlord, Vol. 5'") && s.EndsWith(Epub101));
        }

        // (b) a copy from the copy-in era: verified against the original, moved to the holding folder,
        // its row forgotten, and only then the original adopted -- with the edition's lazy file list
        // replaced, so the import specs never see the stale copy row.
        [Test]
        public void a_copy_in_era_epub_is_held_and_the_original_adopted()
        {
            var report = Subject.ImportAuthor(_author);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.CreateFolder("/lightnovels/.adopted-holding/Overlord/Overlord - Vol. 6"), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(Copy6, Held6, false), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.Is<BookFile>(f => f.Path == Copy6), DeleteMediaFileReason.Manual), Times.Once());

            // the copy, where it went, and the original it stood in for: the maintainer approves deletions from this line
            report.Held.Should().ContainSingle(h => h == $"{Copy6} -> {Held6} (replaced by {Epub102}; 1000 / 2000)");

            var call = _decisionCalls.Single(c => c.Files.Contains(Epub102));
            call.Overrides.Edition.Id.Should().Be(61);
            call.EditionFilesAtDecision.Should().Be(0);

            VerifyAdopted(Epub102, 61, FileHome.Calibre, 1, 1, calibreId: 102);
            report.Adopted.Should().Contain(s => s.StartsWith("calibre #102") && s.EndsWith(Epub102));
            report.Errors.Should().BeEmpty();
        }

        // (c) an original that is not there keeps the copy exactly where it is: no move, no forgotten
        // row, no import -- an Errors line for the maintainer.
        [Test]
        public void a_copy_whose_original_is_missing_is_kept_and_reported()
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(Epub102)).Returns(false);

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().ContainSingle(e => e.StartsWith("calibre #102") && e.Contains("copy kept, original failed verification") && e.Contains(Epub102));
            report.Held.Should().BeEmpty();

            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            VerifyNothingAdoptedFrom(Epub102);

            // the volume with no copy still adopts
            VerifyAdopted(Epub101, 51, FileHome.Calibre, 1, 1, calibreId: 101);
        }

        [Test]
        public void a_copy_set_that_does_not_pair_with_the_original_is_kept()
        {
            // one m4b copy against a three-mp3 original: neither the count nor the extension pairs
            _vol5 = Volume(5, epubCopy: false, audioFiles: new List<BookFile> { EntryCopy(500, "/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005.m4b", 52) });
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(3)).Returns(new List<Book> { _vol5, _vol6 });

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().ContainSingle(e => e.StartsWith("abs 'Overlord, Vol. 5") && e.Contains("copy kept, original failed verification"));

            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(It.Is<string>(p => p.EndsWith(".m4b")), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.Is<BookFile>(f => f.Path.EndsWith(".m4b")), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            VerifyNothingAdoptedFrom("/srv/audiobooks/");
        }

        [Test]
        public void copy_in_era_audio_parts_are_held_in_part_order_and_the_item_adopted()
        {
            _vol5 = Volume(5, epubCopy: false, audioFiles: new List<BookFile>
            {
                EntryCopy(502, "/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005 - Part 02.mp3", 52, part: 2),
                EntryCopy(501, "/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005 - Part 01.mp3", 52, part: 1),
                EntryCopy(503, "/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005 - Part 03.mp3", 52, part: 3)
            });
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(3)).Returns(new List<Book> { _vol5, _vol6 });

            var report = Subject.ImportAuthor(_author);

            foreach (var part in new[] { 1, 2, 3 })
            {
                var copy = $"/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005 - Part {part:00}.mp3";
                var held = $"/lightnovels/.adopted-holding/Overlord/Overlord - Vol. 5/Overlord - Vol 005 - Part {part:00}.mp3";

                Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(copy, held, false), Times.Once());
                Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.Is<BookFile>(f => f.Path == copy), DeleteMediaFileReason.Manual), Times.Once());
                report.Held.Should().Contain($"{copy} -> {held} (replaced by {Abs1Files[part - 1]}; 1000 / 2000)");
            }

            // three audio parts (plus the Setup's Vol. 6 EPUB copy)
            report.Held.Where(h => h.Contains("Part")).Should().HaveCount(3);
            _decisionCalls.Single(c => c.Files.Contains(Abs1Files[0])).EditionFilesAtDecision.Should().Be(0);

            VerifyAdopted(Abs1Files[0], 52, FileHome.Audiobooks, 1, 3);
            VerifyAdopted(Abs1Files[1], 52, FileHome.Audiobooks, 2, 3);
            VerifyAdopted(Abs1Files[2], 52, FileHome.Audiobooks, 3, 3);
            report.Errors.Should().BeEmpty();
        }

        // A copy row that is not under the entry folder cannot be held; nothing of its set moves.
        [Test]
        public void a_copy_set_with_a_row_outside_the_entry_folder_is_left_where_it_is()
        {
            _vol5 = Volume(5, epubCopy: false, audioFiles: new List<BookFile>
            {
                EntryCopy(501, "/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005 - Part 01.mp3", 52, part: 1),
                EntryCopy(502, "/somewhere/else/Overlord - Vol 005 - Part 02.mp3", 52, part: 2),
                EntryCopy(503, "/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005 - Part 03.mp3", 52, part: 3)
            });
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(3)).Returns(new List<Book> { _vol5, _vol6 });

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().ContainSingle(e => e.StartsWith("abs 'Overlord, Vol. 5") && e.Contains("/somewhere/else/"));

            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(It.Is<string>(p => p.EndsWith(".mp3")), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.Is<BookFile>(f => f.Path.EndsWith(".mp3")), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            VerifyNothingAdoptedFrom("/srv/audiobooks/");

            ExceptionVerification.ExpectedWarns(1);
        }

        // (d) an edition whose file already lives off the entry is done: skipped, nothing touched.
        [Test]
        public void an_already_adopted_edition_is_skipped()
        {
            _vol6.EditionOf(MediaType.Ebook).BookFiles = new List<BookFile>
            {
                new BookFile { Id = 6, Path = Epub102, EditionId = 61, Part = 1, Size = 2000, Home = FileHome.Calibre, Adopted = true, CalibreId = 102 }
            };

            var report = Subject.ImportAuthor(_author);

            report.Skipped.Should().ContainSingle(s => s.StartsWith("calibre #102") && s.Contains("already adopted"));
            report.Held.Should().BeEmpty();

            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            VerifyNothingAdoptedFrom(Epub102);
            _decisionCalls.Should().NotContain(c => c.Overrides.Edition.Id == 61);
        }

        // (f) the pipeline's word is final: an error result is an Errors line, never an Adopted one.
        [Test]
        public void an_import_error_is_reported_and_the_other_volumes_still_adopt()
        {
            var vol7 = Volume(7, epubCopy: false);
            var vol8 = Volume(8, epubCopy: false);
            const string epub104 = "/books/Kugane Maruyama/Overlord, Vol. 7 (104)/Overlord, Vol. 7 - Kugane Maruyama.epub";
            const string epub105 = "/books/Kugane Maruyama/Overlord, Vol. 8 (105)/Overlord, Vol. 8 - Kugane Maruyama.epub";

            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(3)).Returns(new List<Book> { _vol5, _vol6, vol7, vol8 });

            GivenCalibreSearch(101, 104, 105);
            GivenCalibreBook(104, "Overlord, Vol. 7", "Overlord", 7.0, "EPUB");
            GivenCalibreBook(105, "Overlord, Vol. 8", "Overlord", 8.0, "EPUB");
            GivenCalibreEpub(104, epub104, 2000);
            GivenCalibreEpub(105, epub105, 2000);

            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(s => s.Import(It.Is<List<ImportDecision<LocalBook>>>(d => d.Any(x => x.Item.Path == epub104)), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                .Returns<List<ImportDecision<LocalBook>>, bool, DownloadClientItem, ImportMode>((decisions, replace, item, mode) =>
                    decisions.Select(d => new ImportResult(d, "Failed to import book, permissions error")).ToList());

            var report = Subject.ImportAuthor(_author);

            report.Adopted.Should().Contain(s => s.Contains("#101"));
            report.Adopted.Should().Contain(s => s.Contains("#105"));
            report.Adopted.Should().NotContain(s => s.Contains("#104"));
            report.Errors.Should().ContainSingle(e => e.StartsWith("calibre #104 'Overlord, Vol. 7'") && e.Contains("permissions error"));

            VerifyAdopted(epub105, 81, FileHome.Calibre, 1, 1, calibreId: 105);
        }

        [Test]
        public void a_rejected_decision_is_reported_and_nothing_is_adopted_for_it()
        {
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(s => s.GetImportDecisions(It.Is<List<IFileInfo>>(f => f.Any(x => x.FullName == Epub101)), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((files, overrides, info, config) =>
                    files.Select(f => new ImportDecision<LocalBook>(new LocalBook { Path = f.FullName, Author = overrides.Author, Book = overrides.Book, Edition = overrides.Edition }, new Rejection("Adopted original — manage it outside Mangarr"))).ToList());

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().ContainSingle(e => e.StartsWith("calibre #101") && e.Contains("Adopted original — manage it outside Mangarr"));
            report.Adopted.Should().NotContain(s => s.Contains("#101"));
        }

        [Test]
        public void should_list_a_calibre_hit_of_another_series_as_unmatched_and_never_adopt_it()
        {
            var report = Subject.ImportAuthor(_author);

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBook(103, It.IsAny<CalibreSettings>()), Times.Never());
            _decisionCalls.Should().NotContain(c => c.Files.Any(f => f.Contains("(103)")));

            report.Unmatched.Should().Contain(s => s.Contains("103") && s.Contains("Overlord Manga"));
        }

        [Test]
        public void should_search_calibre_for_the_name_and_every_alias()
        {
            Subject.ImportAuthor(_author);

            Mocker.GetMock<IHttpClient>()
                .Verify(v => v.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/ajax/search/books") && r.Url.FullUri.Contains("Overlord"))), Times.Exactly(2));
        }

        // (e) an ABS item's audio files are registered where they are, as the parts of the volume's
        // audio edition in file-name order; the cover beside them is not a part.
        [Test]
        public void should_adopt_the_audio_files_of_a_matched_item_in_place_as_parts()
        {
            var report = Subject.ImportAuthor(_author);

            var call = _decisionCalls.Single(c => c.Files.Contains(Abs1Files[0]));
            call.Files.Should().Equal(Abs1Files);
            call.Overrides.Author.Should().BeSameAs(_author);
            call.Overrides.Book.Should().BeSameAs(_vol5);
            call.Overrides.Edition.Id.Should().Be(52);

            VerifyAdopted(Abs1Files[0], 52, FileHome.Audiobooks, 1, 3);
            VerifyAdopted(Abs1Files[1], 52, FileHome.Audiobooks, 2, 3);
            VerifyAdopted(Abs1Files[2], 52, FileHome.Audiobooks, 3, 3);
            _imported.Should().NotContain(d => d.Item.Path.EndsWith("cover.jpg"));

            Mocker.GetMock<IDiskProvider>().Verify(v => v.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.CreateFolder(It.Is<string>(p => p.StartsWith("/lightnovels/Overlord") || p.StartsWith("/srv/audiobooks/"))), Times.Never());

            report.Adopted.Should().ContainSingle(s => s == $"abs 'Overlord, Vol. 5 (Light Novel)' -> {string.Join(", ", Abs1Files)}");
        }

        [Test]
        public void should_send_the_bearer_key_to_audiobookshelf()
        {
            Subject.ImportAuthor(_author);

            Mocker.GetMock<IHttpClient>()
                .Verify(v => v.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/api/libraries/") && r.Headers["Authorization"] == "Bearer abs-key")), Times.Once());
        }

        // Nothing is ever copied or deleted; the one move there is takes a copy-era copy to the
        // holding folder inside the root, and only that.
        [Test]
        public void should_never_copy_or_delete_a_file_and_move_only_copies_into_holding()
        {
            Subject.ImportAuthor(_author);

            VerifyNoFileCopiedOrDeleted();
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(It.IsAny<string>(), It.Is<string>(p => !p.StartsWith("/lightnovels/.adopted-holding/")), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(It.Is<string>(p => !p.StartsWith("/lightnovels/Overlord/")), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_move_nothing_when_there_is_no_copy()
        {
            _vol6 = Volume(6, epubCopy: false);
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(3)).Returns(new List<Book> { _vol5, _vol6 });

            var report = Subject.ImportAuthor(_author);

            VerifyNoFileCopiedOrDeleted();
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            report.Held.Should().BeEmpty();
        }

        [Test]
        public void should_list_an_audiobook_of_a_volume_the_entry_does_not_have_as_unmatched()
        {
            var report = Subject.ImportAuthor(_author);

            report.Unmatched.Should().Contain(s => s.Contains("Overlord, Vol. 9") && s.Contains("no volume 9"));
            report.Unmatched.Should().NotContain(s => s.Contains("KonoSuba"));
        }

        // Final review I2: an unmonitored edition is the add-time "Audiobook" / "EPUB" checkbox left
        // unticked; adopting into it would have the import re-monitor it and flip AudioAvailable.
        [Test]
        public void should_skip_a_volume_whose_audio_edition_is_unmonitored()
        {
            _vol5 = Volume(5, epubCopy: false, audioMonitored: false);
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(3)).Returns(new List<Book> { _vol5, _vol6 });

            var report = Subject.ImportAuthor(_author);

            VerifyNothingAdoptedFrom("/srv/audiobooks/");

            report.Skipped.Should().ContainSingle(s => s.Contains("abs 'Overlord, Vol. 5") && s.Contains("Audiobook edition not monitored"));

            // the EPUB side of the same volume is unaffected
            VerifyAdopted(Epub101, 51, FileHome.Calibre, 1, 1, calibreId: 101);
        }

        [Test]
        public void should_skip_a_volume_whose_ebook_edition_is_unmonitored()
        {
            _vol5 = Volume(5, epubCopy: false, ebookMonitored: false);
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(3)).Returns(new List<Book> { _vol5, _vol6 });

            var report = Subject.ImportAuthor(_author);

            VerifyNothingAdoptedFrom(Epub101);
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBook(101, It.IsAny<CalibreSettings>()), Times.Never());

            report.Skipped.Should().ContainSingle(s => s.Contains("calibre #101") && s.Contains("Ebook edition not monitored"));

            // the audio side of the same volume is unaffected
            VerifyAdopted(Abs1Files[0], 52, FileHome.Audiobooks, 1, 3);
        }

        [Test]
        public void execute_writes_one_report_per_author_and_rescans_the_author()
        {
            Subject.Execute(new ImportExistingLightNovelsCommand(3));

            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.WriteAllText("/config/logs/import-existing-3.txt", It.Is<string>(t => t.Contains("Adopted (") && t.Contains(Epub101) && t.Contains("Held (1)") && t.Contains(Held6) && t.Contains("never copied") && !t.Contains("Copied ("))), Times.Once());

            // Copy-in hold (2026-09-16, D3): the rescan is inline now, not a queued RescanFoldersCommand
            Mocker.GetMock<IDiskScanService>()
                .Verify(x => x.Scan(It.Is<List<string>>(f => f.Contains("/lightnovels/Overlord")), FilterFilesType.Matched, false, It.Is<List<int>>(ids => ids.Contains(3))), Times.Once());
        }

        [Test]
        public void execute_without_an_author_covers_every_light_novel_and_no_manga()
        {
            var manga = new Author { Id = 4, Name = "Dandadan", Path = "/manga/Dandadan", Metadata = new AuthorMetadata { Name = "Dandadan", ForeignAuthorId = "local-dandadan" } };

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAllAuthors()).Returns(new List<Author> { manga, _author });

            Subject.Execute(new ImportExistingLightNovelsCommand());

            Mocker.GetMock<IDiskProvider>().Verify(v => v.WriteAllText("/config/logs/import-existing-3.txt", It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.WriteAllText("/config/logs/import-existing-4.txt", It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IBookService>().Verify(v => v.GetBooksByAuthor(4), Times.Never());
        }

        // Tasks follow-up (2026-09-23): System -> Tasks pushes the default constructor (AuthorId
        // null, SearchAfter false) -- every light-novel entry still gets adopted, but never a search.
        [Test]
        public void a_task_created_run_covers_every_light_novel_and_queues_no_search()
        {
            var manga = new Author { Id = 4, Name = "Dandadan", Path = "/manga/Dandadan", Metadata = new AuthorMetadata { Name = "Dandadan", ForeignAuthorId = "local-dandadan" } };

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAllAuthors()).Returns(new List<Author> { manga, _author });

            Subject.Execute(new ImportExistingLightNovelsCommand());

            Mocker.GetMock<IDiskProvider>().Verify(v => v.WriteAllText("/config/logs/import-existing-3.txt", It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.IsAny<MissingBookSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void execute_with_a_manga_author_id_touches_nothing()
        {
            var manga = new Author { Id = 4, Name = "Dandadan", Path = "/manga/Dandadan", Metadata = new AuthorMetadata { Name = "Dandadan", ForeignAuthorId = "local-dandadan" } };

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(4)).Returns(manga);

            Subject.Execute(new ImportExistingLightNovelsCommand(4));

            Mocker.GetMock<IBookService>().Verify(v => v.GetBooksByAuthor(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(v => v.Get(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>().Verify(x => x.Push(It.IsAny<RescanFoldersCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void audiobookshelf_failure_is_reported_and_the_calibre_side_still_runs()
        {
            GivenAudiobookshelfFails();

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().ContainSingle(e => e.Contains("Audiobookshelf"));

            VerifyAdopted(Epub101, 51, FileHome.Calibre, 1, 1, calibreId: 101);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void an_uppercase_format_key_is_the_same_epub()
        {
            GivenCalibreEpub(101, Epub101, 2000, formatKey: "EPUB");

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted(Epub101, 51, FileHome.Calibre, 1, 1, calibreId: 101);
            report.Errors.Should().BeEmpty();
        }

        // Final review C1 (2026-09-22): an AZW3-only calibre book (the fallback when no EPUB
        // release existed) is adopted by its AZW3; a book holding both is adopted by its EPUB.
        [Test]
        public void an_azw3_only_calibre_book_is_adopted_by_its_azw3()
        {
            var azw3 = "/books/Maruyama Kugane/Overlord, Vol. 5 (101)/Overlord, Vol. 5 - Maruyama Kugane.azw3";
            GivenCalibreBook(101, "Overlord, Vol. 5", "Overlord", 5.0, "AZW3");
            GivenCalibreEpub(101, azw3, 2000, formatKey: "azw3");

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted(azw3, 51, FileHome.Calibre, 1, 1, calibreId: 101);
            report.Skipped.Should().NotContain(s => s.StartsWith("calibre #101"));
            report.Errors.Should().BeEmpty();
        }

        [Test]
        public void an_epub_and_azw3_calibre_book_is_adopted_by_its_epub()
        {
            var azw3 = "/books/Maruyama Kugane/Overlord, Vol. 5 (101)/Overlord, Vol. 5 - Maruyama Kugane.azw3";
            GivenCalibreBook(101, "Overlord, Vol. 5", "Overlord", 5.0, "AZW3", "EPUB");

            Mocker.GetMock<ICalibreProxy>().Setup(s => s.GetBook(101, It.IsAny<CalibreSettings>())).Returns(new CalibreBook
            {
                Id = 101,
                Formats = new Dictionary<string, CalibreBookFormat>
                {
                    { "azw3", new CalibreBookFormat { Path = azw3, Size = 2000 } },
                    { "epub", new CalibreBookFormat { Path = Epub101, Size = 2000 } }
                }
            });

            Subject.ImportAuthor(_author);

            VerifyAdopted(Epub101, 51, FileHome.Calibre, 1, 1, calibreId: 101);
            VerifyNothingAdoptedFrom(azw3);
        }

        // LN PDF final review (2026-09-22): the profile opt-in has to hold at adoption time -- the maintainer's
        // default profiles ship Ebook PDF (id 8) unticked, and the fixture's author has no
        // QualityProfile at all (QualityProfileFor returns null), so every case above this one is
        // implicitly the "unknown profile, let it through" path. This ticks it explicitly.
        private void GivenEbookPdfAllowed(bool allowed)
        {
            _author.QualityProfile = new QualityProfile
            {
                Items = new List<QualityProfileQualityItem> { new QualityProfileQualityItem { Quality = Quality.EbookPdf, Allowed = allowed } }
            };
        }

        // LN PDF (2026-09-22): a PDF-only calibre book is adopted by its PDF once the profile wants
        // Ebook PDF; the Skipped line for a book with none of the three names all three.
        [Test]
        public void a_pdf_only_calibre_book_is_adopted_by_its_pdf_when_ebook_pdf_is_allowed()
        {
            var pdf = "/books/Maruyama Kugane/Overlord, Vol. 5 (101)/Overlord, Vol. 5 - Maruyama Kugane.pdf";
            GivenCalibreBook(101, "Overlord, Vol. 5", "Overlord", 5.0, "PDF");
            GivenCalibreEpub(101, pdf, 2000, formatKey: "pdf");
            GivenEbookPdfAllowed(true);

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted(pdf, 51, FileHome.Calibre, 1, 1, calibreId: 101);
            report.Skipped.Should().NotContain(s => s.StartsWith("calibre #101"));
            report.Errors.Should().BeEmpty();
        }

        // LN PDF final review (2026-09-22): the unticked twin -- the maintainer's default. A PDF-only calibre
        // book is Skipped, not adopted and not an Error, until Ebook PDF is ticked in the profile.
        [Test]
        public void a_pdf_only_calibre_book_is_skipped_when_ebook_pdf_is_not_allowed()
        {
            var pdf = "/books/Maruyama Kugane/Overlord, Vol. 5 (101)/Overlord, Vol. 5 - Maruyama Kugane.pdf";
            GivenCalibreBook(101, "Overlord, Vol. 5", "Overlord", 5.0, "PDF");
            GivenCalibreEpub(101, pdf, 2000, formatKey: "pdf");
            GivenEbookPdfAllowed(false);

            var report = Subject.ImportAuthor(_author);

            VerifyNothingAdoptedFrom(pdf);
            report.Skipped.Should().Contain(s => s.StartsWith("calibre #101") && s.EndsWith("PDF only — tick Ebook PDF in the light-novel profile to adopt it"));
            report.Errors.Should().BeEmpty();
        }

        [Test]
        public void a_calibre_book_with_no_epub_azw3_or_pdf_is_skipped_naming_all_three()
        {
            GivenCalibreBook(101, "Overlord, Vol. 5", "Overlord", 5.0, "MOBI");

            var report = Subject.ImportAuthor(_author);

            report.Skipped.Should().Contain(s => s.StartsWith("calibre #101") && s.EndsWith("no EPUB, AZW3 or PDF format"));
            VerifyNothingAdoptedFrom(Epub101);
        }

        [Test]
        public void a_calibre_book_with_no_epub_path_is_reported_and_the_rest_still_adopt()
        {
            Mocker.GetMock<ICalibreProxy>()
                .Setup(s => s.GetBook(101, It.IsAny<CalibreSettings>()))
                .Returns(new CalibreBook { Id = 101, Formats = new Dictionary<string, CalibreBookFormat>() });

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().ContainSingle(e => e.StartsWith("calibre #101 'Overlord, Vol. 5'") && e.Contains("no path for its EPUB"));
            VerifyNothingAdoptedFrom(Epub101);
            VerifyAdopted(Epub102, 61, FileHome.Calibre, 1, 1, calibreId: 102);
        }

        [Test]
        public void a_calibre_book_whose_epub_path_calibre_cannot_give_is_reported_and_the_rest_still_adopt()
        {
            Mocker.GetMock<ICalibreProxy>()
                .Setup(s => s.GetBook(101, It.IsAny<CalibreSettings>()))
                .Throws(new CalibreException("Unable to connect to Calibre library: boom"));

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().ContainSingle(e => e.Contains("#101") && e.Contains("Overlord, Vol. 5") && e.Contains("boom"));
            VerifyNothingAdoptedFrom(Epub101);
            VerifyAdopted(Epub102, 61, FileHome.Calibre, 1, 1, calibreId: 102);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_missing_audiobookshelf_folder_is_reported_and_the_other_items_still_adopt()
        {
            GivenAudiobookshelfItems(
                AbsItem("abs-1", "/audiobooks/Overlord/Overlord Vol 5", "Overlord, Vol. 5 (Light Novel)", "Overlord #5"),
                AbsItem("abs-6", "/audiobooks/Overlord/Overlord Vol 6", "Overlord, Vol. 6 (Light Novel)", "Overlord #6"));

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles("/srv/audiobooks/Overlord/Overlord Vol 5", true))
                .Throws(new DirectoryNotFoundException("not on the mount"));

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles("/srv/audiobooks/Overlord/Overlord Vol 6", true))
                .Returns(new[] { "/srv/audiobooks/Overlord/Overlord Vol 6/Overlord Vol 6.mp3" });

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().ContainSingle(e => e.Contains("abs 'Overlord, Vol. 5") && e.Contains("not on the mount"));

            VerifyAdopted("/srv/audiobooks/Overlord/Overlord Vol 6/Overlord Vol 6.mp3", 62, FileHome.Audiobooks, 1, 1);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void an_audiobookshelf_path_that_escapes_the_mount_is_rejected()
        {
            GivenAudiobookshelfItems(
                AbsItem("abs-9", "/audiobooks/../../config/Overlord Vol 5", "Overlord, Vol. 5 (Light Novel)", "Overlord #5"));

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().ContainSingle(e => e.Contains("/audiobooks/../../config/Overlord Vol 5") && e.Contains("escapes"));
            report.Adopted.Should().NotContain(s => s.StartsWith("abs"));

            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            VerifyNothingAdoptedFrom("/srv/audiobooks/");
            VerifyNothingAdoptedFrom("/config/");
        }

        // Fix round 1 (2026-09-22): a blank pair maps identity before LightNovelPathMap.Map's own
        // "." / ".." check runs, so MapPath's own segment guard is the only thing standing between a
        // ".." answer and a real GetFiles call.
        [Test]
        public void an_audiobookshelf_path_with_a_dot_segment_is_rejected_even_with_a_blank_pair()
        {
            Mocker.GetMock<ILightNovelStorage>().Setup(s => s.MapFromAudiobookshelf(It.IsAny<string>())).Returns<string>(p => p);

            GivenAudiobookshelfItems(
                AbsItem("abs-9", "/audiobooks/../../config/x.m4b", "Overlord, Vol. 5 (Light Novel)", "Overlord #5"));

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().ContainSingle(e => e.Contains("/audiobooks/../../config/x.m4b") && e.Contains("escapes"));
            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void a_single_file_audiobookshelf_item_is_adopted_as_its_one_part()
        {
            GivenAudiobookshelfItems(
                AbsItem("abs-5", "/audiobooks/Overlord/Overlord Vol 5.m4b", "Overlord, Vol. 5 (Light Novel)", "Overlord #5", isFile: true));

            Subject.ImportAuthor(_author);

            VerifyAdopted("/srv/audiobooks/Overlord/Overlord Vol 5.m4b", 52, FileHome.Audiobooks, 1, 1);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void a_single_file_audiobookshelf_item_that_is_not_audio_is_not_adopted()
        {
            GivenAudiobookshelfItems(
                AbsItem("abs-5", "/audiobooks/Overlord/Overlord Vol 5.epub", "Overlord, Vol. 5 (Light Novel)", "Overlord #5", isFile: true));

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().ContainSingle(e => e.Contains("abs 'Overlord, Vol. 5") && e.Contains("no audio files"));

            VerifyNothingAdoptedFrom("/srv/audiobooks/");
        }

        [Test]
        public void a_native_script_alias_does_not_match_a_native_titled_series()
        {
            _author.Metadata.Value.Aliases = new List<string> { "オーバーロード" };

            GivenCalibreSearch(106);
            GivenCalibreBook(106, "転生したらスライムだった件 5", "転生したらスライムだった件", 5.0, "EPUB");

            GivenAudiobookshelfItems(
                AbsItem("abs-7", "/audiobooks/Honzuki/Honzuki 5", "本好きの下剋上 5", "本好きの下剋上 #5"));

            var report = Subject.ImportAuthor(_author);

            report.Adopted.Should().BeEmpty();
            report.Unmatched.Should().Contain(s => s.Contains("#106") && s.Contains("is not Overlord"));

            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBook(It.IsAny<int>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            _imported.Should().BeEmpty();
        }

        // Alias widening (2026-09-17): "(Second edition)" is the catalogue's disambiguation, not
        // Calibre's series name. No aliases here: "Overlord (Light Novel)" would match on its own.
        [Test]
        public void a_parenthetical_in_the_entry_name_does_not_block_the_calibre_match()
        {
            _author.Name = "Overlord (Second edition)";
            _author.Metadata.Value.Name = "Overlord (Second edition)";
            _author.Metadata.Value.Aliases = new List<string>();

            GivenCalibreSearch(101);
            GivenAudiobookshelfItems();

            var report = Subject.ImportAuthor(_author);

            report.Adopted.Should().HaveCount(1);
            report.Unmatched.Should().BeEmpty();

            // series: is a contains-search, so the bare name is its own query (escaped series:"Overlord")
            Mocker.GetMock<IHttpClient>()
                .Verify(v => v.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/ajax/search/books") && r.Url.FullUri.Contains("series%3A%22Overlord%22"))), Times.Once());
        }

        [Test]
        public void a_second_calibre_book_at_the_same_series_index_is_a_duplicate_skip()
        {
            GivenCalibreSearch(101, 107);
            GivenCalibreBook(107, "Overlord, Vol. 5 (reissue)", "Overlord", 5.0, "EPUB");

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted(Epub101, 51, FileHome.Calibre, 1, 1, calibreId: 101);
            Mocker.GetMock<ICalibreProxy>().Verify(v => v.GetBook(107, It.IsAny<CalibreSettings>()), Times.Never());
            _decisionCalls.Where(c => c.Overrides.Edition.Id == 51).Should().HaveCount(1);

            report.Skipped.Should().Contain(s => s.Contains("#107") && s.Contains("duplicate in Calibre (book id 101)"));
        }

        [Test]
        public void a_second_audiobookshelf_item_for_the_same_volume_is_a_duplicate_skip()
        {
            GivenAudiobookshelfItems(
                AbsItem("abs-1", "/audiobooks/Overlord/Overlord Vol 5", "Overlord, Vol. 5 (Light Novel)", "Overlord #5"),
                AbsItem("abs-4", "/audiobooks/Overlord/Overlord Vol 5 (Part 2)", "Overlord, Vol. 5 (Light Novel) Part 2", "Overlord #5"));

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted(Abs1Files[0], 52, FileHome.Audiobooks, 1, 3);
            _decisionCalls.Where(c => c.Overrides.Edition.Id == 52).Should().HaveCount(1);

            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.GetFiles("/srv/audiobooks/Overlord/Overlord Vol 5 (Part 2)", true), Times.Never());

            report.Skipped.Should().Contain(s => s.Contains("duplicate Audiobookshelf item abs-4") && s.Contains("first item abs-1 kept"));
        }

        // Copy-in (2026-09-16): ABS series fields are free text ("Solo Leveling Series #2"); when
        // the series name is not ours the item's own title still can be, and a trailing " Series"
        // word on the series name does not count. Another series entirely stays silent.
        [Test]
        public void abs_item_whose_series_field_is_not_ours_matches_on_its_title()
        {
            GivenAudiobookshelfItems(
                AbsItem("abs-5", "/audiobooks/Overlord/Overlord Vol 5.m4b", "Overlord, Vol. 5", "Overlord Novels #5", isFile: true));

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted("/srv/audiobooks/Overlord/Overlord Vol 5.m4b", 52, FileHome.Audiobooks, 1, 1);

            report.Adopted.Should().ContainSingle(s => s.StartsWith("abs"));
        }

        [Test]
        public void abs_item_with_a_trailing_series_word_matches_by_series()
        {
            GivenAudiobookshelfItems(
                AbsItem("abs-6", "/audiobooks/Overlord/Overlord Vol 6.m4b", "Something unrelated", "Overlord Series #6", isFile: true));

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted("/srv/audiobooks/Overlord/Overlord Vol 6.m4b", 62, FileHome.Audiobooks, 1, 1);

            report.Adopted.Should().ContainSingle(s => s.StartsWith("abs"));
        }

        [Test]
        public void abs_item_of_another_series_is_still_ignored()
        {
            GivenAudiobookshelfItems(
                AbsItem("abs-8", "/audiobooks/Konosuba/Konosuba Vol 5.m4b", "Konosuba, Vol. 5", "Konosuba #5", isFile: true));

            var report = Subject.ImportAuthor(_author);

            VerifyNothingAdoptedFrom("/srv/audiobooks/");

            report.Adopted.Should().NotContain(s => s.StartsWith("abs"));
            report.Unmatched.Should().NotContain(s => s.Contains("Konosuba"));
        }

        // Addendum: ABS also titles items "<Series> <N>: <Subtitle>" with no series field at all
        // (Sword Art Online's Vol 1 is "Sword Art Online 1: Aincrad"); the bare number is the last candidate.
        [Test]
        public void abs_item_titled_series_number_subtitle_matches_on_the_bare_number()
        {
            GivenAudiobookshelfItems(
                AbsItem("abs-5", "/audiobooks/Overlord/Overlord Vol 5.m4b", "Overlord 5: The Paladin of the Holy Kingdom", "", isFile: true));

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted("/srv/audiobooks/Overlord/Overlord Vol 5.m4b", 52, FileHome.Audiobooks, 1, 1);

            report.Adopted.Should().ContainSingle(s => s.StartsWith("abs"));
        }

        // Covered volumes (2026-09-17, D4): an ABS item that spans several volumes -- a range in its title
        // or series field, or a Publisher's Pack whose range only Audible's cached product carries -- is
        // adopted onto the first volume and marks the rest covered, exactly like a tracked import.
        [Test]
        public void an_abs_item_titled_with_a_books_range_adopts_onto_the_first_volume_and_covers_the_rest()
        {
            var volumes = GivenTbate();
            GivenMarksAreWritten();
            GivenAudiobookshelfItems(AbsItem("abs-p2", "/audiobooks/The Beginning After the End/Publisher's Pack 2", TbatePack2, ""));

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles("/srv/audiobooks/The Beginning After the End/Publisher's Pack 2", true))
                .Returns(new[]
                {
                    "/srv/audiobooks/The Beginning After the End/Publisher's Pack 2/Publisher's Pack 2 - 01.mp3",
                    "/srv/audiobooks/The Beginning After the End/Publisher's Pack 2/Publisher's Pack 2 - 02.mp3"
                });

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted("/srv/audiobooks/The Beginning After the End/Publisher's Pack 2/Publisher's Pack 2 - 01.mp3", 32, FileHome.Audiobooks, 1, 2);
            VerifyAdopted("/srv/audiobooks/The Beginning After the End/Publisher's Pack 2/Publisher's Pack 2 - 02.mp3", 32, FileHome.Audiobooks, 2, 2);

            VerifyMarked(volumes[2], volumes[3], $"abs '{TbatePack2}'", Times.Once());
            report.Covered.Should().ContainSingle(c => c == $"{TbatePack2} → Vol. 4 by Vol. 3");
            report.Unmatched.Should().BeEmpty();

            // the title carries the range; Audible is not asked
            Mocker.GetMock<IAudibleCatalogService>().Verify(v => v.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        // The marks are written only after the pack's own adoption went through.
        [Test]
        public void a_pack_whose_import_fails_marks_nothing()
        {
            var volumes = GivenTbate();
            GivenMarksAreWritten();
            GivenAudiobookshelfItems(AbsItem("abs-p2", "/audiobooks/The Beginning After the End/Publisher's Pack 2", TbatePack2, ""));

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles("/srv/audiobooks/The Beginning After the End/Publisher's Pack 2", true))
                .Returns(new[] { "/srv/audiobooks/The Beginning After the End/Publisher's Pack 2/Publisher's Pack 2.m4b" });

            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                .Returns<List<ImportDecision<LocalBook>>, bool, DownloadClientItem, ImportMode>((decisions, replace, item, mode) =>
                    decisions.Select(d => new ImportResult(d, "Failed to import book.")).ToList());

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().ContainSingle(e => e.StartsWith($"abs '{TbatePack2}'") && e.Contains("Failed to import book."));
            report.Adopted.Should().BeEmpty();

            VerifyNothingMarked();
            report.Covered.Should().BeEmpty();
        }

        [Test]
        public void an_abs_item_matching_a_cached_audible_pack_title_takes_the_packs_range()
        {
            var volumes = GivenTbate();
            GivenMarksAreWritten();
            GivenAudiblePacks();
            GivenAudiobookshelfItems(AbsItem("abs-p1", "/audiobooks/The Beginning After the End/Publisher's Pack", TbatePack1, ""));

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles("/srv/audiobooks/The Beginning After the End/Publisher's Pack", true))
                .Returns(new[] { "/srv/audiobooks/The Beginning After the End/Publisher's Pack/Publisher's Pack.m4b" });

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted("/srv/audiobooks/The Beginning After the End/Publisher's Pack/Publisher's Pack.m4b", 12, FileHome.Audiobooks, 1, 1);

            VerifyMarked(volumes[0], volumes[1], $"abs '{TbatePack1}'", Times.Once());
            report.Covered.Should().ContainSingle(c => c == $"{TbatePack1} → Vol. 2 by Vol. 1");
            report.Unmatched.Should().BeEmpty();

            Mocker.GetMock<IAudibleCatalogService>().Verify(v => v.GetSeries("The Beginning After the End", It.IsAny<IEnumerable<string>>()), Times.Once());
        }

        // Preferred Edition (2026-09-24, D8; M14 fix round 1): a French entry asks Audible by its English
        // anchor name; one whose Audio editions were all minted unmonitored (no English audio) asks nothing.
        private List<Book> GivenFrenchTbate(bool audioMonitored)
        {
            var volumes = GivenTbate();
            _author.Metadata.Value.Name = "Le Commencement après la fin";
            _author.Metadata.Value.AnchorName = "The Beginning After the End";
            _author.Metadata.Value.EditionLanguage = "fr";
            _author.Metadata.Value.Aliases = new List<string> { "The Beginning After the End" };
            volumes.ForEach(v => v.EditionOf(MediaType.Audio).Monitored = audioMonitored);

            GivenMarksAreWritten();
            GivenAudiblePacks();
            GivenAudiobookshelfItems(AbsItem("abs-p1", "/audiobooks/The Beginning After the End/Publisher's Pack", TbatePack1, ""));
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles("/srv/audiobooks/The Beginning After the End/Publisher's Pack", true))
                .Returns(new[] { "/srv/audiobooks/The Beginning After the End/Publisher's Pack/Publisher's Pack.m4b" });

            return volumes;
        }

        [Test]
        public void a_french_entry_asks_audible_by_its_anchor_name()
        {
            GivenFrenchTbate(audioMonitored: true);

            Subject.ImportAuthor(_author);

            Mocker.GetMock<IAudibleCatalogService>().Verify(v => v.GetSeries("The Beginning After the End", It.IsAny<IEnumerable<string>>()), Times.Once());
            Mocker.GetMock<IAudibleCatalogService>().Verify(v => v.GetSeries("Le Commencement après la fin", It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Test]
        public void a_french_entry_with_no_english_audio_does_not_ask_audible()
        {
            GivenFrenchTbate(audioMonitored: false);

            Subject.ImportAuthor(_author);

            Mocker.GetMock<IAudibleCatalogService>().Verify(v => v.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        // English: a user who unticked every audiobook still asks by the entry's name, as before.
        [Test]
        public void an_english_entry_with_every_audiobook_unticked_still_asks_audible()
        {
            var volumes = GivenTbate();
            volumes.ForEach(v => v.EditionOf(MediaType.Audio).Monitored = false);
            GivenMarksAreWritten();
            GivenAudiblePacks();
            GivenAudiobookshelfItems(AbsItem("abs-p1", "/audiobooks/The Beginning After the End/Publisher's Pack", TbatePack1, ""));
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles("/srv/audiobooks/The Beginning After the End/Publisher's Pack", true))
                .Returns(new[] { "/srv/audiobooks/The Beginning After the End/Publisher's Pack/Publisher's Pack.m4b" });

            Subject.ImportAuthor(_author);

            Mocker.GetMock<IAudibleCatalogService>().Verify(v => v.GetSeries("The Beginning After the End", It.IsAny<IEnumerable<string>>()), Times.Once());
        }

        [Test]
        public void an_abs_series_sequence_range_is_a_range()
        {
            var volumes = GivenTbate();
            GivenMarksAreWritten();
            GivenAudiobookshelfItems(AbsItem("abs-s", "/audiobooks/The Beginning After the End/Pack.m4b", "Publisher's Pack", "The Beginning After the End #1-2", isFile: true));

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted("/srv/audiobooks/The Beginning After the End/Pack.m4b", 12, FileHome.Audiobooks, 1, 1);

            VerifyMarked(volumes[0], volumes[1], "abs 'Publisher's Pack'", Times.Once());
            report.Covered.Should().ContainSingle(c => c == "Publisher's Pack → Vol. 2 by Vol. 1");

            Mocker.GetMock<IAudibleCatalogService>().Verify(v => v.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Test]
        public void a_single_volume_abs_item_marks_nothing()
        {
            var report = Subject.ImportAuthor(_author);

            VerifyAdopted(Abs1Files[0], 52, FileHome.Audiobooks, 1, 3);

            VerifyNothingMarked();
            report.Covered.Should().BeEmpty();
        }

        // A pack item of another series is not ours even when it carries a range.
        [Test]
        public void a_range_item_of_another_series_marks_nothing()
        {
            GivenTbate();
            GivenAudiblePacks();
            GivenAudiobookshelfItems(AbsItem("abs-k", "/audiobooks/KonoSuba/KonoSuba Pack", "KonoSuba: Publisher's Pack: KonoSuba, Books 1-2", ""));

            var report = Subject.ImportAuthor(_author);

            VerifyNothingAdoptedFrom("/srv/audiobooks/");
            VerifyNothingMarked();
            report.Covered.Should().BeEmpty();
            report.Unmatched.Should().BeEmpty();
        }

        // ASIN-first (B3b, 2026-09-18): an ABS item whose ASIN is the one on exactly one of the entry's
        // Audio editions (Audible's identity, B1) is that volume whatever its title says; the
        // name/sequence parse is for the rest. An ASIN nobody has, or one two editions share, is today's path.
        private Book GivenVolume3WithAsin(string asin)
        {
            var vol3 = Volume(3, epubCopy: false);
            vol3.EditionOf(MediaType.Audio).Asin = asin;
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(3)).Returns(new List<Book> { vol3, _vol5, _vol6 });

            return vol3;
        }

        [Test]
        public void an_abs_item_is_matched_by_asin_before_its_title()
        {
            GivenVolume3WithAsin("B0ASIN0003");
            GivenAudiobookshelfItems(
                AbsItem("abs-a", "/audiobooks/Overlord/Something Else 9.m4b", "Something Else #9", "", isFile: true, asin: " b0asin0003 "));

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted("/srv/audiobooks/Overlord/Something Else 9.m4b", 32, FileHome.Audiobooks, 1, 1);

            report.Adopted.Should().ContainSingle(s => s.StartsWith("abs 'Something Else #9'"));
            report.Unmatched.Should().NotContain(s => s.StartsWith("abs"));
            VerifyNothingMarked();
        }

        [Test]
        public void an_abs_item_with_an_asin_nobody_has_takes_todays_path()
        {
            GivenVolume3WithAsin("B0ASIN0003");
            GivenAudiobookshelfItems(
                AbsItem("abs-5", "/audiobooks/Overlord/Overlord Vol 5.m4b", "Overlord, Vol. 5 (Light Novel)", "Overlord #5", isFile: true, asin: "B0NOBODY"),
                AbsItem("abs-9", "/audiobooks/Overlord/Something Else 9.m4b", "Something Else #9", "", isFile: true, asin: "B0NOBODY2"));

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted("/srv/audiobooks/Overlord/Overlord Vol 5.m4b", 52, FileHome.Audiobooks, 1, 1);
            VerifyNothingAdoptedFrom("/srv/audiobooks/Overlord/Something Else 9.m4b");

            report.Adopted.Should().ContainSingle(s => s.StartsWith("abs"));
            report.Unmatched.Should().NotContain(s => s.StartsWith("abs"));
        }

        // Fix round 0 (controller ruling): a pack whose ASIN is on its carrier keeps the range the title
        // gives from that volume, so the covered marks are written exactly as for a name-matched pack.
        [Test]
        public void an_asin_matched_pack_keeps_the_parsed_range_and_covers_the_rest()
        {
            var volumes = GivenTbate();
            volumes[2].EditionOf(MediaType.Audio).Asin = "1774242036";
            GivenMarksAreWritten();
            GivenAudiobookshelfItems(AbsItem("abs-p2", "/audiobooks/The Beginning After the End/Publisher's Pack 2", TbatePack2, "", asin: "1774242036"));

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles("/srv/audiobooks/The Beginning After the End/Publisher's Pack 2", true))
                .Returns(new[] { "/srv/audiobooks/The Beginning After the End/Publisher's Pack 2/Publisher's Pack 2.m4b" });

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted("/srv/audiobooks/The Beginning After the End/Publisher's Pack 2/Publisher's Pack 2.m4b", 32, FileHome.Audiobooks, 1, 1);

            VerifyMarked(volumes[2], volumes[3], $"abs '{TbatePack2}'", Times.Once());
            report.Covered.Should().ContainSingle(c => c == $"{TbatePack2} → Vol. 4 by Vol. 3");
        }

        // The ASIN decides the volume; a range that does not start there is not this item's pack.
        [Test]
        public void an_asin_match_ignores_a_parsed_range_that_starts_elsewhere()
        {
            var volumes = GivenTbate();
            volumes[3].EditionOf(MediaType.Audio).Asin = "1774242036";
            GivenMarksAreWritten();
            GivenAudiobookshelfItems(AbsItem("abs-p2", "/audiobooks/The Beginning After the End/Publisher's Pack 2", TbatePack2, "", asin: "1774242036"));

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFiles("/srv/audiobooks/The Beginning After the End/Publisher's Pack 2", true))
                .Returns(new[] { "/srv/audiobooks/The Beginning After the End/Publisher's Pack 2/Publisher's Pack 2.m4b" });

            var report = Subject.ImportAuthor(_author);

            VerifyAdopted("/srv/audiobooks/The Beginning After the End/Publisher's Pack 2/Publisher's Pack 2.m4b", 42, FileHome.Audiobooks, 1, 1);

            VerifyNothingMarked();
            report.Covered.Should().BeEmpty();
        }

        [Test]
        public void an_asin_two_editions_share_decides_nothing()
        {
            GivenVolume3WithAsin("B0ASIN0003");
            _vol5.EditionOf(MediaType.Audio).Asin = "B0ASIN0003";
            GivenAudiobookshelfItems(
                AbsItem("abs-a", "/audiobooks/Overlord/Something Else 9.m4b", "Something Else #9", "", isFile: true, asin: "B0ASIN0003"));

            var report = Subject.ImportAuthor(_author);

            VerifyNothingAdoptedFrom("/srv/audiobooks/");
            report.Adopted.Should().NotContain(s => s.StartsWith("abs"));
        }

        // Copy-in hold (2026-09-16, D3): the command rescans inline and releases the hold only on an
        // error-free report; the search-on-add intent it carries runs once the hold is clear.
        [Test]
        public void clean_run_rescans_inline_then_clears_the_hold()
        {
            _author.CopyInPending = true;

            Subject.Execute(new ImportExistingLightNovelsCommand(3));

            Mocker.GetMock<IDiskScanService>()
                .Verify(v => v.Scan(It.Is<List<string>>(f => f.Count == 1 && f[0] == "/lightnovels/Overlord"), FilterFilesType.Matched, false, It.Is<List<int>>(ids => ids.Count == 1 && ids[0] == 3)), Times.Once());
            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.ClearCopyInPending(_author), Times.Once());
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.IsAny<RescanFoldersCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void search_after_queues_the_missing_search_once_the_hold_is_clear()
        {
            _author.CopyInPending = true;

            Subject.Execute(new ImportExistingLightNovelsCommand(3) { SearchAfter = true });

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.Is<MissingBookSearchCommand>(c => c.AuthorId == 3), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void a_report_with_errors_keeps_the_hold_and_queues_no_search()
        {
            _author.CopyInPending = true;
            GivenAudiobookshelfFails();

            Subject.Execute(new ImportExistingLightNovelsCommand(3) { SearchAfter = true });

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.ClearCopyInPending(It.IsAny<Author>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.IsAny<MissingBookSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());

            // the Audiobookshelf pass's warn from ImportAuthor + "the hold stays" from Execute
            ExceptionVerification.ExpectedWarns(2);
        }

        // One copy each (ruling): the adoption aligns the ABS shelf once, from the entry's adopted
        // audiobook rows after the rescan; a failing sync is an Errors line, never a held search.
        private List<BookFile> GivenAdoptedRowsAfterRescan()
        {
            var audio = new List<BookFile>
            {
                new BookFile { Id = 51, Path = Abs1Files[0], EditionId = 52, Home = FileHome.Audiobooks, Adopted = true },
                new BookFile { Id = 52, Path = Abs1Files[1], EditionId = 52, Home = FileHome.Audiobooks, Adopted = true },
                new BookFile { Id = 53, Path = Abs1Files[2], EditionId = 52, Home = FileHome.Audiobooks, Adopted = true }
            };

            var rows = new List<BookFile>(audio)
            {
                new BookFile { Id = 61, Path = Epub102, EditionId = 61, Home = FileHome.Calibre, Adopted = true, CalibreId = 102 },
                new BookFile { Id = 71, Path = "/lightnovels/Overlord/Overlord - Vol. 7/Overlord - Vol 007.m4b", EditionId = 72, Home = FileHome.Entry, Adopted = false }
            };

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(3)).Returns(rows);

            return audio;
        }

        [Test]
        public void execute_syncs_the_audiobookshelf_shelf_once_with_the_adopted_audiobook_rows()
        {
            _author.CopyInPending = true;
            var audio = GivenAdoptedRowsAfterRescan();

            Subject.Execute(new ImportExistingLightNovelsCommand(3) { SearchAfter = true });

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(_author, It.Is<List<BookFile>>(l => l.Count == 3 && l.All(r => audio.Contains(r))), out It.Ref<string>.IsAny), Times.Once());
            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny), Times.Once());

            // after the rescan, before the hold clears
            Mocker.GetMock<IDiskScanService>()
                .Verify(v => v.Scan(It.IsAny<List<string>>(), FilterFilesType.Matched, false, It.IsAny<List<int>>()), Times.Once());
            Mocker.GetMock<IAuthorService>().Verify(v => v.ClearCopyInPending(_author), Times.Once());
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.Is<MissingBookSearchCommand>(c => c.AuthorId == 3), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());

            // an aligned shelf is no Errors line
            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.WriteAllText("/config/logs/import-existing-3.txt", It.Is<string>(t => t.Contains("Errors (0)"))), Times.Once());
        }

        // L5, fix round 1 (2026-09-23): the adoption also sets the calibre title once, from the
        // entry's Calibre-homed rows after the rescan (adopted included) -- a newly adopted book
        // gets the one display title immediately, instead of waiting for the whole-library command.
        [Test]
        public void execute_syncs_the_calibre_title_once_with_the_entrys_calibre_homed_rows()
        {
            _author.CopyInPending = true;
            GivenAdoptedRowsAfterRescan();

            Subject.Execute(new ImportExistingLightNovelsCommand(3) { SearchAfter = true });

            Mocker.GetMock<ICalibreTitleSyncService>()
                .Verify(v => v.Sync(_author, It.Is<List<BookFile>>(l => l.Count == 1 && l[0].Path == Epub102), It.IsAny<CalibreSettings>(), out It.Ref<string>.IsAny), Times.Once());
            Mocker.GetMock<ICalibreTitleSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), It.IsAny<CalibreSettings>(), out It.Ref<string>.IsAny), Times.Once());
        }

        [Test]
        public void a_failed_calibre_title_sync_is_reported_and_the_hold_still_clears()
        {
            _author.CopyInPending = true;
            GivenAdoptedRowsAfterRescan();
            GivenCalibreTitleSync(false, "1 title(s) failed");

            Subject.Execute(new ImportExistingLightNovelsCommand(3) { SearchAfter = true });

            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.WriteAllText("/config/logs/import-existing-3.txt", It.Is<string>(t => t.Contains("Errors (1)") && t.Contains("Calibre title: 1 title(s) failed"))), Times.Once());
            Mocker.GetMock<IAuthorService>().Verify(v => v.ClearCopyInPending(_author), Times.Once());
        }

        [Test]
        public void the_calibre_title_sync_is_skipped_when_ebooks_go_to_the_entry_folder()
        {
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Entry);
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.AudioHome).Returns(LightNovelHome.Entry);
            _author.CopyInPending = true;
            GivenAdoptedRowsAfterRescan();

            Subject.Execute(new ImportExistingLightNovelsCommand(3) { SearchAfter = true });

            Mocker.GetMock<ICalibreTitleSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), It.IsAny<CalibreSettings>(), out It.Ref<string>.IsAny), Times.Never());
        }

        // The sync swallows ABS's own failures (a Warn each) and says so in its result: that is the
        // report's Errors line, never a reason to hold the search.
        [TestCase("library read failed: HTTP request failed: [401:Unauthorized]")]
        [TestCase("2 patch(es) failed")]
        public void a_shelf_the_sync_could_not_align_is_reported_and_the_hold_still_clears(string failure)
        {
            _author.CopyInPending = true;
            GivenAdoptedRowsAfterRescan();
            GivenShelfSync(false, failure);

            Subject.Execute(new ImportExistingLightNovelsCommand(3) { SearchAfter = true });

            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.WriteAllText("/config/logs/import-existing-3.txt", It.Is<string>(t => t.Contains("Errors (1)") && t.Contains($"Audiobookshelf shelf: {failure}"))), Times.Once());
            Mocker.GetMock<IAuthorService>().Verify(v => v.ClearCopyInPending(_author), Times.Once());
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.Is<MissingBookSearchCommand>(c => c.AuthorId == 3), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void a_throwing_shelf_sync_is_reported_and_the_hold_still_clears()
        {
            _author.CopyInPending = true;
            GivenAdoptedRowsAfterRescan();

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Setup(s => s.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny))
                .Throws(new System.InvalidOperationException("ABS is down"));

            Subject.Execute(new ImportExistingLightNovelsCommand(3) { SearchAfter = true });

            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.WriteAllText("/config/logs/import-existing-3.txt", It.Is<string>(t => t.Contains("Errors (1)") && t.Contains("Audiobookshelf shelf: ABS is down"))), Times.Once());
            Mocker.GetMock<IAuthorService>().Verify(v => v.ClearCopyInPending(_author), Times.Once());
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.Is<MissingBookSearchCommand>(c => c.AuthorId == 3), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }

        // Final review (T6 tied): the Held section is the maintainer's per-item deletion-approval list, so a
        // rescan that throws is an Errors line in a report that is still written; the hold stays.
        [Test]
        public void a_throwing_rescan_still_writes_the_report_with_its_held_line_and_keeps_the_hold()
        {
            _author.CopyInPending = true;

            Mocker.GetMock<IDiskScanService>()
                .Setup(s => s.Scan(It.IsAny<List<string>>(), It.IsAny<FilterFilesType>(), It.IsAny<bool>(), It.IsAny<List<int>>()))
                .Throws(new System.IO.IOException("root folder went away"));

            Subject.Execute(new ImportExistingLightNovelsCommand(3) { SearchAfter = true });

            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.WriteAllText("/config/logs/import-existing-3.txt", It.Is<string>(t => t.Contains("Held (1)") && t.Contains(Held6) && t.Contains("Errors (1)") && t.Contains("Rescan: root folder went away"))), Times.Once());
            Mocker.GetMock<IAuthorService>().Verify(v => v.ClearCopyInPending(It.IsAny<Author>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.IsAny<MissingBookSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());

            // the rescan's warn + "the hold stays"
            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void an_entry_with_no_adopted_audiobook_rows_syncs_nothing()
        {
            Subject.Execute(new ImportExistingLightNovelsCommand(3));

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny), Times.Never());
        }

        // Coverage (2026-09-23): the shelf sync now covers every Audiobooks-homed row of the entry,
        // adopted AND grabbed -- not just the ones this run adopted.
        [Test]
        public void execute_syncs_a_grabbed_audiobookshelf_homed_row_along_with_the_adopted_ones()
        {
            var audio = GivenAdoptedRowsAfterRescan();
            var grabbed = new BookFile { Id = 54, Path = "/srv/audiobooks/Overlord/Overlord Vol 8.m4b", EditionId = 52, Home = FileHome.Audiobooks, Adopted = false };

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(3))
                .Returns(new List<BookFile>(audio) { grabbed });

            Subject.Execute(new ImportExistingLightNovelsCommand(3));

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(_author, It.Is<List<BookFile>>(l => l.Count == 4 && l.Contains(grabbed) && audio.All(a => l.Contains(a))), out It.Ref<string>.IsAny), Times.Once());
        }

        [Test]
        public void an_unheld_entry_is_rescanned_and_left_alone()
        {
            Subject.Execute(new ImportExistingLightNovelsCommand(3));

            Mocker.GetMock<IDiskScanService>()
                .Verify(v => v.Scan(It.IsAny<List<string>>(), FilterFilesType.Matched, false, It.IsAny<List<int>>()), Times.Once());
            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.ClearCopyInPending(It.IsAny<Author>()), Times.Never());
        }

        // Light-novel storage (2026-09-22): a kind whose home is the entry folder has nothing to adopt
        // from calibre / Audiobookshelf -- one Skipped line each, never an Errors line -- so a user with
        // neither installed gets the copy-in hold cleared and the post-add search.
        [Test]
        public void entry_homes_are_skipped_lines_and_nothing_is_asked()
        {
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Entry);
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.AudioHome).Returns(LightNovelHome.Entry);

            var report = Subject.ImportAuthor(_author);

            report.Errors.Should().BeEmpty();
            report.Adopted.Should().BeEmpty();
            report.Skipped.Should().Contain(s => s.StartsWith("calibre:") && s.Contains("entry folder"));
            report.Skipped.Should().Contain(s => s.StartsWith("audiobookshelf:") && s.Contains("entry folder"));
            Mocker.GetMock<IHttpClient>().Verify(v => v.Get(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void entry_homes_release_the_hold_search_after_and_skip_the_shelf_sync()
        {
            _author.CopyInPending = true;
            GivenAdoptedRowsAfterRescan();
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.EbookHome).Returns(LightNovelHome.Entry);
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.AudioHome).Returns(LightNovelHome.Entry);

            Subject.Execute(new ImportExistingLightNovelsCommand(3) { SearchAfter = true });

            Mocker.GetMock<IDiskScanService>()
                .Verify(v => v.Scan(It.IsAny<List<string>>(), FilterFilesType.Matched, false, It.IsAny<List<int>>()), Times.Once());
            Mocker.GetMock<IAuthorService>().Verify(v => v.ClearCopyInPending(_author), Times.Once());
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.Is<MissingBookSearchCommand>(c => c.AuthorId == 3), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny), Times.Never());
        }
    }
}
