using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.BookTests.Calibre
{
    // One copy each (2026-09-20): the write probe behind CalibreWriteAccessCheck. Calibre's router
    // answers 405 to anything but a POST on cdb/cmd before it looks at trusted_ips, so the probe must
    // go out as the POST it builds; a 403 is "refused", anything else is "writable".
    [TestFixture]
    public class CalibreProxyFixture : CoreTest<CalibreProxy>
    {
        private CalibreSettings _settings;
        private HttpRequest _sent;

        [SetUp]
        public void Setup()
        {
            _settings = new CalibreSettings { Host = "calibre.test", Port = 8081, Library = "books" };
            _sent = null;

            Mocker.SetConstant<ICacheManager>(new CacheManager());
        }

        private void GivenProbeStatus(HttpStatusCode status)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => _sent = r)
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), string.Empty, status));
        }

        // Task 3: the calibre input format comes from the file extension on disk, not the grabbed
        // Quality name -- a light-novel EPUB and its AZW3 fallback share no single Quality that
        // always matches the file calibre is actually holding.
        [TestCase("/books/Author/Title.epub", "EPUB")]
        [TestCase("/books/Author/Title.azw3", "AZW3")]
        [TestCase("/manga/Series/Vol 01.cbz", "CBZ")]
        public void input_format_of_is_the_uppercased_file_extension(string path, string expected)
        {
            CalibreProxy.InputFormatOf(path).Should().Be(expected);
        }

        // Light-novel storage (2026-09-22): the light-novel server's own path pair maps calibre's paths;
        // Readarr's Remote Path Mappings are not consulted for it (a stock root still uses them).
        [Test]
        public void get_book_maps_a_light_novel_path_through_its_own_pair()
        {
            var settings = new LightNovelCalibreServerSettings { Host = "calibre.test", Port = 8081, Library = "books", RemotePath = "/calibre/", LocalPath = "/srv/calibre/" };

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreBook>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreBook>(new HttpResponse(r, new HttpHeader(),
                    "{\"application_id\": 7, \"format_metadata\": {\"EPUB\": {\"path\": \"/calibre/A/B (7)/B.epub\", \"size\": 3, \"mtime\": \"2026-01-01T00:00:00+00:00\"}}}")));

            Subject.GetBook(7, settings).Formats["EPUB"].Path.Should().Be("/srv/calibre/A/B (7)/B.epub");

            Mocker.GetMock<IRemotePathMappingService>().Verify(v => v.RemapRemoteToLocal(It.IsAny<string>(), It.IsAny<OsPath>()), Times.Never());
        }

        // A blank pair (light-novel storage, 2026-09-22): identity, same as a stock root's Remote Path
        // Mappings with none configured -- calibre's own path comes back unchanged.
        [Test]
        public void get_book_with_a_blank_pair_returns_calibres_path_unchanged()
        {
            var settings = new LightNovelCalibreServerSettings { Host = "calibre.test", Port = 8081, Library = "books", RemotePath = string.Empty, LocalPath = string.Empty };

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreBook>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreBook>(new HttpResponse(r, new HttpHeader(),
                    "{\"application_id\": 7, \"format_metadata\": {\"EPUB\": {\"path\": \"/books/A/B (7)/B.epub\", \"size\": 3, \"mtime\": \"2026-01-01T00:00:00+00:00\"}}}")));

            Subject.GetBook(7, settings).Formats["EPUB"].Path.Should().Be("/books/A/B (7)/B.epub");

            Mocker.GetMock<IRemotePathMappingService>().Verify(v => v.RemapRemoteToLocal(It.IsAny<string>(), It.IsAny<OsPath>()), Times.Never());
        }

        [Test]
        public void get_book_refuses_a_light_novel_path_outside_its_pair()
        {
            var settings = new LightNovelCalibreServerSettings { Host = "calibre.test", Port = 8081, Library = "books", RemotePath = "/calibre/", LocalPath = "/srv/calibre/" };

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreBook>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreBook>(new HttpResponse(r, new HttpHeader(),
                    "{\"application_id\": 7, \"format_metadata\": {\"EPUB\": {\"path\": \"/elsewhere/B.epub\", \"size\": 3, \"mtime\": \"2026-01-01T00:00:00+00:00\"}}}")));

            Assert.Throws<CalibreException>(() => Subject.GetBook(7, settings)).Message.Should().Contain("Path As Calibre Sees It");
        }

        [Test]
        public void has_write_access_should_post_the_saved_searches_list_command()
        {
            GivenProbeStatus(HttpStatusCode.OK);

            Subject.HasWriteAccess(_settings).Should().BeTrue();

            _sent.Should().NotBeNull();
            _sent.Method.Should().Be(HttpMethod.Post);
            _sent.Url.FullUri.Should().Be("http://calibre.test:8081/cdb/cmd/saved_searches");
            _sent.Headers.ContentType.Should().Be("application/json");
            Encoding.UTF8.GetString(_sent.ContentData).Should().Be("[\"list\"]");
            _sent.SuppressHttpError.Should().BeTrue();
        }

        [Test]
        public void has_write_access_should_be_false_when_calibre_answers_forbidden()
        {
            GivenProbeStatus(HttpStatusCode.Forbidden);

            Subject.HasWriteAccess(_settings).Should().BeFalse();
        }

        [Test]
        public void has_write_access_should_never_send_the_probe_as_a_get()
        {
            GivenProbeStatus(HttpStatusCode.OK);

            Subject.HasWriteAccess(_settings);

            Mocker.GetMock<IHttpClient>().Verify(v => v.Get(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(v => v.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        // One copy each (2026-09-20): the author calibre files a light-novel EPUB under is the real
        // writer (AuthorMetadata.Writer), falling back to the entry name; a manga entry keeps the
        // entry name. Adding a format to a book calibre already has leaves its authors alone.

        private List<HttpRequest> _executed;

        private void GivenCalibreAccepts(int bookId)
        {
            _executed = new List<HttpRequest>();

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => _executed.Add(r))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), string.Empty));

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<CalibreImportJob>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreImportJob>(new HttpResponse(r, new HttpHeader(), $"{{\"book_id\": {bookId}, \"id\": 1}}")));

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreBook>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreBook>(new HttpResponse(r, new HttpHeader(),
                    $"{{\"application_id\": {bookId}, \"format_metadata\": {{\"EPUB\": {{\"path\": \"/books/x.epub\", \"size\": 3, \"mtime\": \"2026-01-01T00:00:00+00:00\"}}}}}}")));
        }

        private BookFile GivenFile(string foreignAuthorId, string writer, int calibreId, double volumeNumber = 1)
        {
            var path = GetTempFilePath();
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            var author = new Author
            {
                Metadata = new AuthorMetadata { Name = "KonoSuba", ForeignAuthorId = foreignAuthorId, Writer = writer }
            };

            var book = new Book { Title = "KonoSuba Vol 1", VolumeNumber = volumeNumber, SeriesLinks = new List<SeriesBookLink>() };
            var edition = new Edition { Title = "KonoSuba Vol 1", Book = book };

            return new BookFile
            {
                Id = 0,
                Path = path,
                CalibreId = calibreId,
                Quality = new QualityModel(Quality.EPUB),
                Author = author,
                Edition = edition
            };
        }

        private List<JObject> SetFieldsBodies()
        {
            return _executed
                .Where(r => r.Url.FullUri.Contains("/cdb/set-fields/"))
                .Select(r => JObject.Parse(Encoding.UTF8.GetString(r.ContentData)))
                .ToList();
        }

        // One copy each (2026-09-20): ajax/books answers {"<id>": null} for an id the library no
        // longer has (checked live 2026-09-20); GetBooks returns the books it found and the caller
        // treats the rest as absent.
        [Test]
        public void get_books_drops_the_null_entry_calibre_returns_for_a_missing_id()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<Dictionary<int, CalibreBook>>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<Dictionary<int, CalibreBook>>(new HttpResponse(r, new HttpHeader(),
                    "{\"7\": {\"application_id\": 7, \"format_metadata\": {\"EPUB\": {\"path\": \"/books/x.epub\", \"size\": 3, \"mtime\": \"2026-01-01T00:00:00+00:00\"}}}, \"999\": null}")));

            Mocker.GetMock<IRemotePathMappingService>()
                .Setup(m => m.RemapRemoteToLocal(It.IsAny<string>(), It.IsAny<OsPath>()))
                .Returns<string, OsPath>((host, path) => path);

            var books = Subject.GetBooks(new List<int> { 7, 999 }, _settings);

            books.Should().HaveCount(1);
            books[0].Id.Should().Be(7);
            books[0].Formats["EPUB"].Path.Should().Be("/books/x.epub");
        }

        // I1-bis, fix round 2 (2026-09-23): calibre's set-fields WRITE key is `sort`
        // (CalibreChanges.Sort, C1), but its READ endpoints (ajax/book/{id}, ajax/books?ids=) report
        // the same value back under `title_sort` -- verified live in a calibre 9.15 container.
        // CalibreBook.Sort must read that key, or CalibreTitleSyncService's idempotence check (I1)
        // never sees a stored sort and re-writes every calibre-homed row on every pass.
        [Test]
        public void get_books_reads_sort_from_the_title_sort_key()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<Dictionary<int, CalibreBook>>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<Dictionary<int, CalibreBook>>(new HttpResponse(r, new HttpHeader(),
                    "{\"7\": {\"application_id\": 7, \"title\": \"Sword Art Online: Aincrad (Vol. 1)\", \"title_sort\": \"Sword Art Online 0001\", \"format_metadata\": {}}}")));

            var books = Subject.GetBooks(new List<int> { 7 }, _settings);

            books.Single().Sort.Should().Be("Sword Art Online 0001");
        }

        [Test]
        public void get_book_reads_sort_from_the_title_sort_key()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreBook>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreBook>(new HttpResponse(r, new HttpHeader(),
                    "{\"application_id\": 7, \"title\": \"Sword Art Online: Aincrad (Vol. 1)\", \"title_sort\": \"Sword Art Online 0001\", \"format_metadata\": {}}")));

            Subject.GetBook(7, _settings).Sort.Should().Be("Sword Art Online 0001");
        }

        [Test]
        public void set_fields_with_set_authors_false_sends_no_authors_key()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 7);

            Subject.SetFields(file, _settings, updateCover: false, embed: false, setAuthors: false);

            var bodies = SetFieldsBodies();
            bodies.Should().HaveCount(1);
            bodies[0]["changes"]["authors"].Should().BeNull();
            bodies[0]["changes"]["title"].Value<string>().Should().Be("KonoSuba (Vol. 1)");
        }

        [Test]
        public void set_fields_files_a_light_novel_under_its_writer()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-mushoku-tensei~ln", "Rifujin na Magonote", 7);

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            var bodies = SetFieldsBodies();
            bodies.Should().HaveCount(1);
            bodies[0]["changes"]["authors"].Values<string>().Should().Equal("Rifujin na Magonote");
        }

        [Test]
        public void set_fields_falls_back_to_the_entry_name_when_a_light_novel_has_no_writer()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-konosuba~ln", null, 7);

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            SetFieldsBodies().Single()["changes"]["authors"].Values<string>().Should().Equal("KonoSuba");
        }

        [Test]
        public void set_fields_files_a_manga_under_the_entry_name()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-konosuba", "Natsume Akatsuki", 7);

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            SetFieldsBodies().Single()["changes"]["authors"].Values<string>().Should().Equal("KonoSuba");
        }

        // One copy each (2026-09-20, final review I1): a light-novel entry is the series, so the
        // body carries the entry name and the volume number; the SeriesLinks route (empty for
        // every manga/LN entry) would send "series": null and clear one set in calibre's GUI.
        [Test]
        public void set_fields_sends_the_entry_name_as_series_and_the_volume_number_as_index_for_a_light_novel()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 7, volumeNumber: 12);

            Subject.SetFields(file, _settings, updateCover: false, embed: false, setAuthors: false);

            var changes = SetFieldsBodies().Single()["changes"];
            changes["series"].Value<string>().Should().Be("KonoSuba");
            changes["series_index"].Value<double>().Should().Be(12);
        }

        [Test]
        public void set_fields_sends_the_series_but_no_index_for_an_unnumbered_light_novel_volume()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 7, volumeNumber: 0);

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            var changes = SetFieldsBodies().Single()["changes"];
            changes["series"].Value<string>().Should().Be("KonoSuba");
            changes["series_index"].Should().BeNull();
        }

        [Test]
        public void set_fields_leaves_a_manga_s_series_to_its_series_links()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-konosuba", "Natsume Akatsuki", 7, volumeNumber: 12);

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            var changes = SetFieldsBodies().Single()["changes"];
            changes["series"].Type.Should().Be(JTokenType.Null);
            changes["series_index"].Should().BeNull();
        }

        // One display title (2026-09-23, the maintainer): calibre's Title/sort for a light novel is
        // "<series>[: <subtitle>] (Vol. <n>)" / "<series> <n padded>" -- the same string
        // AdoptedAudioSyncService sends Audiobookshelf. Fix round 1 (C1): calibre's own field is
        // `sort`, not `title_sort` -- KeyError: 'title_sort' -> HTTP 500 otherwise.
        [Test]
        public void set_fields_writes_the_display_title_and_sort_for_a_light_novel_with_a_subtitle()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-sword-art-online~ln", "Reki Kawahara", 7, volumeNumber: 1);
            file.Author.Value.Metadata.Value.Name = "Sword Art Online";
            file.Edition.Value.Book.Value.Subtitle = "Aincrad";

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            var changes = SetFieldsBodies().Single()["changes"];
            changes["title"].Value<string>().Should().Be("Sword Art Online: Aincrad (Vol. 1)");
            changes["sort"].Value<string>().Should().Be("Sword Art Online 0001");
        }

        // C1: calibre applies `changes` in JSON key order, and a title change resets the sort -- the
        // sort key must come after the title key so calibre applies the sort we just sent, not a
        // title-derived one it computes when handling `title` first.
        [Test]
        public void set_fields_serializes_sort_after_title_so_a_title_change_does_not_reset_it()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-sword-art-online~ln", "Reki Kawahara", 7, volumeNumber: 1);
            file.Author.Value.Metadata.Value.Name = "Sword Art Online";

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            var changes = (JObject)SetFieldsBodies().Single()["changes"];
            var keys = changes.Properties().Select(p => p.Name).ToList();
            keys.IndexOf("sort").Should().BeGreaterThan(keys.IndexOf("title"));
        }

        [Test]
        public void set_fields_writes_the_display_title_without_a_subtitle_and_keeps_a_colon_in_the_series_name()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-mushoku-tensei~ln", "Rifujin na Magonote", 7, volumeNumber: 14);
            file.Author.Value.Metadata.Value.Name = "Mushoku Tensei: Jobless Reincarnation";

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            var changes = SetFieldsBodies().Single()["changes"];
            changes["title"].Value<string>().Should().Be("Mushoku Tensei: Jobless Reincarnation (Vol. 14)");
            changes["sort"].Value<string>().Should().Be("Mushoku Tensei: Jobless Reincarnation 0014");
        }

        [Test]
        public void set_fields_pads_a_fractional_volume_in_the_sort()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-classroom-of-the-elite~ln", "Shogo Kinugasa", 7, volumeNumber: 11.5);
            file.Author.Value.Metadata.Value.Name = "Classroom of the Elite";

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            var changes = SetFieldsBodies().Single()["changes"];
            changes["title"].Value<string>().Should().Be("Classroom of the Elite (Vol. 11.5)");
            changes["sort"].Value<string>().Should().Be("Classroom of the Elite 0011.5");
        }

        [Test]
        public void set_fields_leaves_a_mangas_title_and_never_sends_a_sort()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-konosuba", "Natsume Akatsuki", 7, volumeNumber: 1);

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            var changes = SetFieldsBodies().Single()["changes"];
            changes["title"].Value<string>().Should().Be("KonoSuba Vol 1");
            changes["sort"].Should().BeNull();
        }

        // Fix round 1 (L1): an unnumbered volume keeps edition.Title and sends no sort -- the same
        // as a manga book, and the same as before this feature -- so calibre and ABS (which skips
        // unnumbered volumes, AdoptedAudioSyncService) still agree.
        [Test]
        public void set_fields_leaves_an_unnumbered_light_novel_volumes_title_alone_and_sends_no_sort()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 7, volumeNumber: 0);

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            var changes = SetFieldsBodies().Single()["changes"];
            changes["title"].Value<string>().Should().Be("KonoSuba Vol 1");
            changes["sort"].Should().BeNull();
        }

        // The adopted-book title exception (2026-09-23, maintainer-approved); fix round 1 (C2, scope of
        // the maintainer's approval): SetTitle is the ONLY calibre write an adopted light novel ever gets, and
        // it must carry NOTHING else -- not series/series_index either (adoption matches a calibre
        // book by a normalised alias, so an adopted book's calibre series can legitimately be a
        // different alias/spelling, and calibre's set_field for a series uses allow_case_change=True
        // -- sending author.Name could re-file the book and re-case the series for every OTHER book
        // that shares it). The payload's keys are exactly {title, sort}.
        [Test]
        public void set_title_sends_only_title_and_sort()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-sword-art-online~ln", "Reki Kawahara", 7, volumeNumber: 1);

            Subject.SetTitle(file, "Sword Art Online: Aincrad (Vol. 1)", "Sword Art Online 0001", _settings);

            var changes = (JObject)SetFieldsBodies().Single()["changes"];
            changes.Properties().Select(p => p.Name).Should().BeEquivalentTo("title", "sort");
            changes["title"].Value<string>().Should().Be("Sword Art Online: Aincrad (Vol. 1)");
            changes["sort"].Value<string>().Should().Be("Sword Art Online 0001");
        }

        [Test]
        public void set_title_serializes_sort_after_title()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-sword-art-online~ln", "Reki Kawahara", 7, volumeNumber: 1);

            Subject.SetTitle(file, "Sword Art Online: Aincrad (Vol. 1)", "Sword Art Online 0001", _settings);

            var changes = (JObject)SetFieldsBodies().Single()["changes"];
            var keys = changes.Properties().Select(p => p.Name).ToList();
            keys.IndexOf("sort").Should().BeGreaterThan(keys.IndexOf("title"));
        }

        [Test]
        public void set_title_tracks_a_rename_the_same_way_set_fields_does_and_persists_the_new_path()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-sword-art-online~ln", "Reki Kawahara", 7, volumeNumber: 1);
            file.Id = 42;
            var renamedPath = GetTempFilePath();
            File.WriteAllBytes(renamedPath, new byte[] { 1, 2, 3 });

            Mocker.GetMock<IRemotePathMappingService>()
                .Setup(m => m.RemapRemoteToLocal(It.IsAny<string>(), It.IsAny<OsPath>()))
                .Returns<string, OsPath>((host, path) => path);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreBook>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreBook>(new HttpResponse(r, new HttpHeader(),
                    $"{{\"application_id\": 7, \"format_metadata\": {{\"EPUB\": {{\"path\": \"{renamedPath.Replace("\\", "\\\\")}\", \"size\": 3, \"mtime\": \"2026-01-01T00:00:00+00:00\"}}}}}}")));

            Subject.SetTitle(file, "Sword Art Online: Aincrad (Vol. 1)", "Sword Art Online 0001", _settings);

            file.Path.Should().Be(renamedPath);
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.Is<BookFile>(f => f.Path == renamedPath)), Times.Once());
        }

        // Fix round 1 (L2): a light-novel row picks the path calibre reports for
        // CalibreFormats.TrackedLightNovelFormat (EPUB, else AZW3, else PDF) -- the same ranking
        // OffEntryFileReconciler uses -- not GetOriginalFormat, which ranks any other text format
        // (MOBI here) equally with EPUB and ties on oldest mtime; an adopted book that holds both
        // could otherwise be repointed at the MOBI.
        [Test]
        public void set_title_picks_the_tracked_light_novel_format_over_a_same_ranked_mobi()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-sword-art-online~ln", "Reki Kawahara", 7, volumeNumber: 1);

            Mocker.GetMock<IRemotePathMappingService>()
                .Setup(m => m.RemapRemoteToLocal(It.IsAny<string>(), It.IsAny<OsPath>()))
                .Returns<string, OsPath>((host, path) => path);

            var epubPath = GetTempFilePath() + ".epub";
            var mobiPath = GetTempFilePath() + ".mobi";
            File.WriteAllBytes(epubPath, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(mobiPath, new byte[] { 1, 2, 3 });

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreBook>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreBook>(new HttpResponse(r, new HttpHeader(),
                    "{\"application_id\": 7, \"format_metadata\": {" +
                    "\"mobi\": {\"path\": \"" + mobiPath + "\", \"size\": 3, \"mtime\": \"2026-01-01T12:00:00+00:00\"}, " +
                    "\"epub\": {\"path\": \"" + epubPath + "\", \"size\": 3, \"mtime\": \"2026-01-01T08:00:00+00:00\"}}}")));

            Subject.SetTitle(file, "Sword Art Online: Aincrad (Vol. 1)", "Sword Art Online 0001", _settings);

            file.Path.Should().Be(epubPath);
        }

        // LN PDF (2026-09-22) P1 (fix round 1): the lightNovel flag matters at SetFields' real call
        // site, not just in isolation. Embedding metadata can rewrite the EPUB, so a newer-looking
        // PDF can sit beside an older EPUB; a light novel's row still repoints to the EPUB.
        [Test]
        public void set_fields_repoints_a_light_novel_to_its_epub_over_a_newer_pdf()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 7);

            Mocker.GetMock<IRemotePathMappingService>()
                .Setup(m => m.RemapRemoteToLocal(It.IsAny<string>(), It.IsAny<OsPath>()))
                .Returns<string, OsPath>((host, path) => path);

            var epubPath = GetTempFilePath() + ".epub";
            var pdfPath = GetTempFilePath() + ".pdf";
            File.WriteAllBytes(epubPath, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(pdfPath, new byte[] { 1, 2, 3 });

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreBook>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreBook>(new HttpResponse(r, new HttpHeader(),
                    "{\"application_id\": 7, \"format_metadata\": {" +
                    "\"epub\": {\"path\": \"" + epubPath + "\", \"size\": 3, \"mtime\": \"2026-01-01T12:00:00+00:00\"}, " +
                    "\"pdf\": {\"path\": \"" + pdfPath + "\", \"size\": 3, \"mtime\": \"2026-01-01T08:00:00+00:00\"}}}")));

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            file.Path.Should().Be(epubPath);
        }

        // The manga twin: a manga book's row is unaffected -- a PDF still wins over a newer AZW3,
        // exactly as before this round, because a manga file never passes lightNovel: true.
        [Test]
        public void set_fields_repoints_a_manga_book_to_its_pdf_over_a_newer_azw3()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-dandadan", null, 7);

            Mocker.GetMock<IRemotePathMappingService>()
                .Setup(m => m.RemapRemoteToLocal(It.IsAny<string>(), It.IsAny<OsPath>()))
                .Returns<string, OsPath>((host, path) => path);

            var azw3Path = GetTempFilePath() + ".azw3";
            var pdfPath = GetTempFilePath() + ".pdf";
            File.WriteAllBytes(azw3Path, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(pdfPath, new byte[] { 1, 2, 3 });

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreBook>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreBook>(new HttpResponse(r, new HttpHeader(),
                    "{\"application_id\": 7, \"format_metadata\": {" +
                    "\"azw3\": {\"path\": \"" + azw3Path + "\", \"size\": 3, \"mtime\": \"2026-01-01T12:00:00+00:00\"}, " +
                    "\"pdf\": {\"path\": \"" + pdfPath + "\", \"size\": 3, \"mtime\": \"2026-01-01T08:00:00+00:00\"}}}")));

            Subject.SetFields(file, _settings, updateCover: false, embed: false);

            file.Path.Should().Be(pdfPath);
        }

        [Test]
        public void add_and_convert_sets_the_authors_on_a_new_book()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 0);

            Subject.AddAndConvert(file, _settings);

            file.CalibreId.Should().Be(7);
            Mocker.GetMock<IHttpClient>().Verify(v => v.Post<CalibreImportJob>(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/cdb/add-book/"))), Times.Once());

            var bodies = SetFieldsBodies();
            bodies.Should().HaveCount(1);
            bodies[0]["changes"]["authors"].Values<string>().Should().Equal("Natsume Akatsuki");
        }

        [Test]
        public void add_and_convert_leaves_the_authors_alone_when_adding_a_format()
        {
            GivenCalibreAccepts(7);
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 7);

            Subject.AddAndConvert(file, _settings);

            Mocker.GetMock<IHttpClient>().Verify(v => v.Post<CalibreImportJob>(It.IsAny<HttpRequest>()), Times.Never());

            var bodies = SetFieldsBodies();
            bodies.Should().HaveCount(2);
            bodies[0]["changes"]["added_formats"].Should().NotBeNull();
            bodies[1]["changes"]["authors"].Should().BeNull();
            bodies[1]["changes"]["title"].Value<string>().Should().Be("KonoSuba (Vol. 1)");
        }

        // Final review I1 (2026-09-22): the file is in calibre once add + SetFields succeed; a
        // conversion that cannot START (book-data or conversion/start refused) is logged and the
        // import still returns the file. In the upgrade path the old row is already gone, so a
        // throw here left the volume Missing while calibre held the new file.
        private static HttpException CalibreRefused(HttpRequest request)
        {
            return new HttpException(request, new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.InternalServerError));
        }

        private void GivenBookData(params string[] inputFormats)
        {
            var formats = string.Join(",", inputFormats.Select(f => "\"" + f + "\""));

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreBookData>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreBookData>(new HttpResponse(r, new HttpHeader(),
                    "{\"book_id\": 7, \"input_formats\": [" + formats + "], \"conversion_options\": {\"options\": {}}}")));
        }

        [Test]
        public void add_and_convert_keeps_the_file_when_calibre_cannot_give_the_book_data()
        {
            GivenCalibreAccepts(7);
            _settings.OutputFormat = "AZW3";
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 0);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreBookData>(It.IsAny<HttpRequest>()))
                .Throws(CalibreRefused(new HttpRequest("http://calibre.test:8081/")));

            var result = Subject.AddAndConvert(file, _settings);

            result.Should().BeSameAs(file);
            result.CalibreId.Should().Be(7);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void add_and_convert_keeps_the_file_when_calibre_refuses_to_start_the_conversion()
        {
            GivenCalibreAccepts(7);
            _settings.OutputFormat = "AZW3";
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 0);
            GivenBookData("EPUB");

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<long>(It.IsAny<HttpRequest>()))
                .Throws(CalibreRefused(new HttpRequest("http://calibre.test:8081/")));

            var result = Subject.AddAndConvert(file, _settings);

            result.Should().BeSameAs(file);
            result.CalibreId.Should().Be(7);
            ExceptionVerification.ExpectedWarns(1);
        }

        // Final review M5 (2026-09-22): the import's conversion is skipped when the delivery format
        // IS the imported file's format, and when calibre's book already holds it.
        [Test]
        public void add_and_convert_starts_no_conversion_when_the_file_is_already_the_output_format()
        {
            GivenCalibreAccepts(7);
            _settings.OutputFormat = "AZW3";
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 0);
            File.Move(file.Path, file.Path + ".azw3");
            file.Path += ".azw3";

            // input_formats left empty so only the input == output check can skip it
            GivenBookData();

            Subject.AddAndConvert(file, _settings);

            Mocker.GetMock<IHttpClient>().Verify(c => c.Post<long>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void add_and_convert_starts_no_conversion_when_calibre_already_has_the_output_format()
        {
            GivenCalibreAccepts(7);
            _settings.OutputFormat = "AZW3";
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 7);
            GivenBookData("EPUB", "AZW3");

            Subject.AddAndConvert(file, _settings);

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get<CalibreBookData>(It.IsAny<HttpRequest>()), Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(c => c.Post<long>(It.IsAny<HttpRequest>()), Times.Never());
        }

        // LN PDF (2026-09-22): calibre's PDF input makes poor ebooks. A light novel's PDF is added to
        // calibre and kept as is -- no delivery conversion starts from it. A manga PDF in a stock
        // calibre root converts exactly as before.
        [Test]
        public void add_and_convert_starts_no_conversion_from_a_light_novel_pdf()
        {
            GivenCalibreAccepts(7);
            _settings.OutputFormat = "AZW3";
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 0);
            File.Move(file.Path, file.Path + ".pdf");
            file.Path += ".pdf";
            GivenBookData("PDF");

            var result = Subject.AddAndConvert(file, _settings);

            result.Should().BeSameAs(file);
            result.CalibreId.Should().Be(7);
            Mocker.GetMock<IHttpClient>().Verify(v => v.Post<CalibreImportJob>(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/cdb/add-book/"))), Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get<CalibreBookData>(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(c => c.Post<long>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void add_and_convert_still_converts_a_manga_pdf_in_a_calibre_root()
        {
            GivenCalibreAccepts(7);
            _settings.OutputFormat = "EPUB";
            var file = GivenFile("local-dandadan", null, 0);
            File.Move(file.Path, file.Path + ".pdf");
            file.Path += ".pdf";
            GivenBookData("PDF");
            GivenConversionStarts(42);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreConversionStatus>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreConversionStatus>(new HttpResponse(r, new HttpHeader(), "{\"running\": false, \"ok\": true}")));

            Subject.AddAndConvert(file, _settings);

            Mocker.GetMock<IHttpClient>().Verify(c => c.Post<long>(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/conversion/start/7"))), Times.Once());
        }

        // Final review M4 (2026-09-22): nothing awaits the status poller, so an HTTP error there
        // used to fault the task silently; it is logged and the poller stops.
        private void GivenConversionStarts(long jobId)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<long>(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/conversion/start/"))))
                .Returns<HttpRequest>(r => new HttpResponse<long>(new HttpResponse(r, new HttpHeader(), jobId.ToString())));
        }

        [Test]
        public void a_status_check_that_fails_is_logged_and_the_poller_stops()
        {
            GivenCalibreAccepts(7);
            _settings.OutputFormat = "AZW3";
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 0);
            GivenBookData("EPUB");
            GivenConversionStarts(42);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreConversionStatus>(It.IsAny<HttpRequest>()))
                .Throws(CalibreRefused(new HttpRequest("http://calibre.test:8081/")));

            Subject.AddAndConvert(file, _settings);

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get<CalibreConversionStatus>(It.IsAny<HttpRequest>()), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_conversion_calibre_finishes_logs_nothing()
        {
            GivenCalibreAccepts(7);
            _settings.OutputFormat = "AZW3";
            var file = GivenFile("local-konosuba~ln", "Natsume Akatsuki", 0);
            GivenBookData("EPUB");
            GivenConversionStarts(42);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreConversionStatus>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreConversionStatus>(new HttpResponse(r, new HttpHeader(), "{\"running\": false, \"ok\": true}")));

            Subject.AddAndConvert(file, _settings);

            Mocker.GetMock<IHttpClient>().Verify(c => c.Post<long>(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/conversion/start/7"))), Times.Once());
        }

        // Final review I2 (2026-09-22): the backfill's conversion is followed to the end before the
        // call returns -- true when calibre finished it, false (logged) when it failed, its status
        // could not be read, or it was still running at the timeout.
        private void GivenConversionStatus(params string[] answers)
        {
            var queue = new Queue<string>(answers);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreConversionStatus>(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/conversion/status/42"))))
                .Returns<HttpRequest>(r => new HttpResponse<CalibreConversionStatus>(new HttpResponse(r, new HttpHeader(), queue.Count > 1 ? queue.Dequeue() : queue.Peek())));
        }

        [Test]
        public void convert_and_wait_returns_true_once_calibre_finished_the_job()
        {
            GivenBookData("EPUB");
            GivenConversionStarts(42);
            GivenConversionStatus("{\"running\": false, \"ok\": true}");

            Subject.ConvertToFormatAndWait(7, "EPUB", "AZW3", _settings, System.TimeSpan.FromMinutes(10)).Should().BeTrue();

            Mocker.GetMock<IHttpClient>().Verify(c => c.Post<long>(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/conversion/start/7"))), Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get<CalibreConversionStatus>(It.IsAny<HttpRequest>()), Times.Once());
        }

        [Test]
        public void convert_and_wait_returns_false_when_calibre_reports_the_job_failed()
        {
            GivenBookData("EPUB");
            GivenConversionStarts(42);
            GivenConversionStatus("{\"running\": false, \"ok\": false, \"traceback\": \"boom\"}");

            Subject.ConvertToFormatAndWait(7, "EPUB", "AZW3", _settings, System.TimeSpan.FromMinutes(10)).Should().BeFalse();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void convert_and_wait_gives_up_on_a_job_still_running_at_the_timeout()
        {
            GivenBookData("EPUB");
            GivenConversionStarts(42);
            GivenConversionStatus("{\"running\": true, \"ok\": false}");

            Subject.ConvertToFormatAndWait(7, "EPUB", "AZW3", _settings, System.TimeSpan.Zero).Should().BeFalse();

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get<CalibreConversionStatus>(It.IsAny<HttpRequest>()), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void convert_and_wait_returns_false_when_the_status_check_fails()
        {
            GivenBookData("EPUB");
            GivenConversionStarts(42);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get<CalibreConversionStatus>(It.IsAny<HttpRequest>()))
                .Throws(CalibreRefused(new HttpRequest("http://calibre.test:8081/")));

            Subject.ConvertToFormatAndWait(7, "EPUB", "AZW3", _settings, System.TimeSpan.FromMinutes(10)).Should().BeFalse();

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
