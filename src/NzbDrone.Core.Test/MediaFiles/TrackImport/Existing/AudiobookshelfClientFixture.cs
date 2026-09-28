using System.Linq;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Existing
{
    // One copy each (2026-09-20); ABS titles match calibre (2026-09-23): the write side of the
    // Audiobookshelf client -- library scan and the series/title/subtitle patch -- exercised over a
    // mocked IHttpClient; the assertions are the verb, the path, the Bearer header and the JSON body
    // actually sent. Title and subtitle are always compared (never a "leave alone" null); most of
    // these cases hold the title/subtitle the item already carries so only the field under test moves.
    [TestFixture]
    public class AudiobookshelfClientFixture : CoreTest<AudiobookshelfClient>
    {
        private const string AbsUrl = "http://abs.test";
        private const string AbsLibrary = "lib-1234";
        private const string ItemId = "item-1";

        private HttpRequest _patch;

        [SetUp]
        public void Setup()
        {
            _patch = null;

            Mocker.GetMock<IConfigService>().SetupGet(s => s.AudiobookshelfUrl).Returns(AbsUrl);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.AudiobookshelfApiKey).Returns("abs-key");
            Mocker.GetMock<IConfigService>().SetupGet(s => s.AudiobookshelfLibraryId).Returns(AbsLibrary);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => JsonResponse(r, "{}"));

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => _patch = r)
                .Returns<HttpRequest>(r => JsonResponse(r, "{}"));
        }

        private static HttpResponse JsonResponse(HttpRequest request, string json)
        {
            return new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(json));
        }

        private static string Series(string id, string name, string sequence)
        {
            return "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"sequence\":\"" + sequence + "\"}";
        }

        private static string JsonString(string value)
        {
            return value == null ? "null" : "\"" + value + "\"";
        }

        // GET /api/items/{id}: the expanded item, series as [{id,name,sequence}] (seriesName is null here).
        private void GivenItem(string title, string subtitle, params string[] series)
        {
            var json = "{\"id\":\"" + ItemId + "\",\"media\":{\"metadata\":{\"title\":\"" + title + "\",\"subtitle\":" + JsonString(subtitle) +
                       ",\"seriesName\":null,\"series\":[" + string.Join(",", series) + "]}}}";

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri == $"{AbsUrl}/api/items/{ItemId}")))
                .Returns<HttpRequest>(r => JsonResponse(r, json));
        }

        private void GivenLibraryItems(params string[] items)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.Contains($"/api/libraries/{AbsLibrary}/items"))))
                .Returns<HttpRequest>(r => JsonResponse(r, "{\"results\":[" + string.Join(",", items) + "],\"total\":" + items.Length + "}"));
        }

        private static string LibraryItem(string id, string path, string subtitle = null)
        {
            return "{\"id\":\"" + id + "\",\"path\":\"" + path + "\",\"isFile\":false,\"media\":{\"metadata\":{\"title\":\"" + id + "\",\"subtitle\":" + JsonString(subtitle) + ",\"seriesName\":null}}}";
        }

        private JObject PatchedMetadata()
        {
            _patch.Should().NotBeNull();
            _patch.Method.Should().Be(HttpMethod.Patch);
            _patch.Url.FullUri.Should().Be($"{AbsUrl}/api/items/{ItemId}/media");
            _patch.Headers["Authorization"].Should().Be("Bearer abs-key");
            _patch.Headers.ContentType.Should().Be("application/json");

            return (JObject)JObject.Parse(Encoding.UTF8.GetString(_patch.ContentData))["metadata"];
        }

        [Test]
        public void scan_library_should_post_to_the_library_scan_endpoint_with_the_bearer_key()
        {
            Subject.ScanLibrary();

            Mocker.GetMock<IHttpClient>()
                .Verify(v => v.Post(It.Is<HttpRequest>(r => r.Url.FullUri == $"{AbsUrl}/api/libraries/{AbsLibrary}/scan" && r.Headers["Authorization"] == "Bearer abs-key")), Times.Once());
        }

        [Test]
        public void find_item_by_path_should_match_the_container_path_exactly()
        {
            GivenLibraryItems(LibraryItem("abs-1", "/audiobooks/Overlord - Vol. 5"), LibraryItem("abs-2", "/audiobooks/Overlord - Vol. 6"));

            Subject.FindItemByPath("/audiobooks/Overlord - Vol. 6").Id.Should().Be("abs-2");
            Subject.FindItemByPath("/audiobooks/overlord - vol. 6").Should().BeNull();
            Subject.FindItemByPath("/audiobooks/Overlord - Vol. 7").Should().BeNull();
        }

        // Titles match calibre (2026-09-23): the list endpoint's subtitle feeds AdoptedAudioSyncService's
        // comparison the same way title already did.
        [Test]
        public void get_library_items_should_read_the_subtitle()
        {
            GivenLibraryItems(LibraryItem("abs-1", "/audiobooks/Overlord - Vol. 5", subtitle: "Light Novel"), LibraryItem("abs-2", "/audiobooks/Overlord - Vol. 6"));

            var items = Subject.GetLibraryItems();

            items.Single(i => i.Id == "abs-1").Subtitle.Should().Be("Light Novel");
            items.Single(i => i.Id == "abs-2").Subtitle.Should().BeNull();
        }

        [Test]
        public void patch_metadata_should_replace_the_sequence_of_the_named_series_keeping_its_id_and_casing()
        {
            GivenItem("Mushoku Tensei Vol 2", null, Series("s1", "Other", "3"), Series("s2", "mushoku tensei", "1"));

            Subject.PatchMetadata(ItemId, "Mushoku Tensei", "2", "Mushoku Tensei Vol 2", null).Should().BeTrue();

            var metadata = PatchedMetadata();
            metadata.ContainsKey("title").Should().BeFalse();
            metadata.ContainsKey("subtitle").Should().BeFalse();
            metadata["series"].ToString(Newtonsoft.Json.Formatting.None)
                .Should().Be("[{\"id\":\"s1\",\"name\":\"Other\",\"sequence\":\"3\"},{\"id\":\"s2\",\"name\":\"mushoku tensei\",\"sequence\":\"2\"}]");

            Mocker.GetMock<IHttpClient>().Verify(v => v.Get(It.Is<HttpRequest>(r => r.Url.FullUri == $"{AbsUrl}/api/items/{ItemId}" && r.Method == HttpMethod.Get && r.Headers["Authorization"] == "Bearer abs-key")), Times.Once());
        }

        [Test]
        public void patch_metadata_should_append_a_series_the_item_does_not_have()
        {
            GivenItem("Mushoku Tensei Vol 2", null, Series("s1", "Other", "3"));

            Subject.PatchMetadata(ItemId, "Mushoku Tensei", "2", "Mushoku Tensei Vol 2", null).Should().BeTrue();

            PatchedMetadata()["series"].ToString(Newtonsoft.Json.Formatting.None)
                .Should().Be("[{\"id\":\"s1\",\"name\":\"Other\",\"sequence\":\"3\"},{\"name\":\"Mushoku Tensei\",\"sequence\":\"2\"}]");
        }

        [Test]
        public void patch_metadata_should_include_the_title_when_it_changes()
        {
            GivenItem("Old Title", null, Series("s2", "Mushoku Tensei", "1"));

            Subject.PatchMetadata(ItemId, "Mushoku Tensei", "2", "Mushoku Tensei - Vol. 2", null).Should().BeTrue();

            var metadata = PatchedMetadata();
            metadata["title"].ToString().Should().Be("Mushoku Tensei - Vol. 2");
            metadata.ContainsKey("subtitle").Should().BeFalse();
            metadata["series"].ToString(Newtonsoft.Json.Formatting.None)
                .Should().Be("[{\"id\":\"s2\",\"name\":\"Mushoku Tensei\",\"sequence\":\"2\"}]");
        }

        [Test]
        public void patch_metadata_should_send_only_the_title_change_when_the_series_already_matches()
        {
            GivenItem("Old Title", null, Series("s2", "Mushoku Tensei", "2"));

            Subject.PatchMetadata(ItemId, "Mushoku Tensei", "2", "Mushoku Tensei - Vol. 2", null).Should().BeTrue();

            PatchedMetadata()["title"].ToString().Should().Be("Mushoku Tensei - Vol. 2");
        }

        [Test]
        public void patch_metadata_should_include_the_subtitle_when_it_changes()
        {
            GivenItem("Mushoku Tensei Vol. 1", null, Series("s2", "Mushoku Tensei", "1"));

            Subject.PatchMetadata(ItemId, "Mushoku Tensei", "1", "Mushoku Tensei Vol. 1", "Aincrad").Should().BeTrue();

            var metadata = PatchedMetadata();
            metadata["subtitle"].ToString().Should().Be("Aincrad");
            metadata.ContainsKey("title").Should().BeFalse();
        }

        [Test]
        public void patch_metadata_should_clear_an_existing_subtitle_when_none_is_given()
        {
            GivenItem("Mushoku Tensei: Jobless Reincarnation, Vol. 14", "Light Novel", Series("s2", "Mushoku Tensei", "14"));

            Subject.PatchMetadata(ItemId, "Mushoku Tensei", "14", "Mushoku Tensei: Jobless Reincarnation, Vol. 14", null).Should().BeTrue();

            var metadata = PatchedMetadata();
            metadata["subtitle"].Type.Should().Be(JTokenType.Null);
            metadata.ContainsKey("title").Should().BeFalse();
        }

        [Test]
        public void patch_metadata_should_not_patch_when_series_title_and_subtitle_all_already_match()
        {
            GivenItem("Mushoku Tensei Vol 2", "Aincrad", Series("s1", "Other", "3"), Series("s2", "mushoku tensei", "2"));

            Subject.PatchMetadata(ItemId, "Mushoku Tensei", "2", "Mushoku Tensei Vol 2", "Aincrad").Should().BeFalse();

            _patch.Should().BeNull();
            Mocker.GetMock<IHttpClient>().Verify(v => v.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        // Final review M3: a pack adopted onto its first volume carries a range on the shelf; the
        // range whose first number is ours is kept (HasSeries' rule), the title/subtitle may still change.
        [Test]
        public void patch_metadata_should_keep_a_range_that_starts_with_our_sequence()
        {
            GivenItem("Mushoku Tensei Vol 3-4", null, Series("s2", "Mushoku Tensei", "3-4"));

            Subject.PatchMetadata(ItemId, "Mushoku Tensei", "3", "Mushoku Tensei Vol 3-4", null).Should().BeFalse();

            _patch.Should().BeNull();
        }

        [Test]
        public void patch_metadata_should_patch_the_title_but_keep_a_range_that_starts_with_our_sequence()
        {
            GivenItem("Old Title", null, Series("s2", "Mushoku Tensei", "3-4"));

            Subject.PatchMetadata(ItemId, "Mushoku Tensei", "3", "Mushoku Tensei - Vol. 3", null).Should().BeTrue();

            var metadata = PatchedMetadata();
            metadata["title"].ToString().Should().Be("Mushoku Tensei - Vol. 3");
            metadata["series"].ToString(Newtonsoft.Json.Formatting.None)
                .Should().Be("[{\"id\":\"s2\",\"name\":\"Mushoku Tensei\",\"sequence\":\"3-4\"}]");
        }

        [Test]
        public void patch_metadata_should_replace_a_range_that_starts_elsewhere()
        {
            GivenItem("Mushoku Tensei Vol 3-4", null, Series("s2", "Mushoku Tensei", "3-4"));

            Subject.PatchMetadata(ItemId, "Mushoku Tensei", "4", "Mushoku Tensei Vol 3-4", null).Should().BeTrue();

            PatchedMetadata()["series"].ToString(Newtonsoft.Json.Formatting.None)
                .Should().Be("[{\"id\":\"s2\",\"name\":\"Mushoku Tensei\",\"sequence\":\"4\"}]");
        }

        // Light-novel storage (2026-09-22): the library list for the Settings dropdown, the Test button
        // and the unmapped root -- asked with the address and key given (the form's, saved or not).
        [Test]
        public void get_libraries_reads_every_library_with_its_folders_from_the_given_server()
        {
            HttpRequest sent = null;

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.Is<HttpRequest>(r => r.Url.FullUri.EndsWith("/api/libraries"))))
                .Callback<HttpRequest>(r => sent = r)
                .Returns<HttpRequest>(r => JsonResponse(r,
                    "{\"libraries\":[{\"id\":\"lib-1\",\"name\":\"Audiobooks\",\"mediaType\":\"book\",\"folders\":[{\"id\":\"f1\",\"fullPath\":\"/audiobooks\"}]}," +
                    "{\"id\":\"lib-2\",\"name\":\"Podcasts\",\"mediaType\":\"podcast\",\"folders\":[]}]}"));

            var libraries = Subject.GetLibraries("http://other.test/", "typed-key");

            sent.Url.FullUri.Should().Be("http://other.test/api/libraries");
            sent.Headers["Authorization"].Should().Be("Bearer typed-key");
            libraries.Should().HaveCount(2);
            libraries[0].Id.Should().Be("lib-1");
            libraries[0].Name.Should().Be("Audiobooks");
            libraries[0].MediaType.Should().Be("book");
            libraries[0].Folders.Should().Equal("/audiobooks");
            libraries[1].MediaType.Should().Be("podcast");
            libraries[1].Folders.Should().BeEmpty();
        }
    }
}
