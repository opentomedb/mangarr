using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.MangaDex;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests.MangaDex
{
    [TestFixture]
    public class MangaDexParserFixture : CoreTest<MangaDexParser>
    {
        private ImportListResponse GivenResponse(string json)
        {
            var request = new ImportListRequest("https://api.mangadex.org/user/follows/manga", HttpAccept.Json);
            var response = new HttpResponse(request.HttpRequest, new HttpHeader(), Encoding.UTF8.GetBytes(json));

            return new ImportListResponse(request, response);
        }

        [Test]
        public void should_prefer_english_title()
        {
            var json = @"{""result"":""ok"",""data"":[{""id"":""abc"",""attributes"":{""title"":{""en"":""Kaiju No. 8"",""ja"":""怪獣8号""},""altTitles"":[]}}],""total"":1}";

            var items = Subject.ParseResponse(GivenResponse(json));

            items.Should().HaveCount(1);
            items.First().Author.Should().Be("Kaiju No. 8");
        }

        [Test]
        public void should_fall_back_to_english_alt_title()
        {
            var json = @"{""result"":""ok"",""data"":[{""id"":""abc"",""attributes"":{""title"":{""ja"":""怪獣8号""},""altTitles"":[{""ja-ro"":""Kaijuu 8-gou""},{""en"":""Monster #8""}]}}],""total"":1}";

            var items = Subject.ParseResponse(GivenResponse(json));

            items.Should().HaveCount(1);
            items.First().Author.Should().Be("Monster #8");
        }

        [Test]
        public void should_fall_back_to_romanized_title()
        {
            var json = @"{""result"":""ok"",""data"":[{""id"":""abc"",""attributes"":{""title"":{""ja"":""怪獣8号"",""ja-ro"":""Kaijuu 8-gou""},""altTitles"":[]}}],""total"":1}";

            var items = Subject.ParseResponse(GivenResponse(json));

            items.Should().HaveCount(1);
            items.First().Author.Should().Be("Kaijuu 8-gou");
        }

        [Test]
        public void should_skip_manga_with_no_usable_title()
        {
            var json = @"{""result"":""ok"",""data"":[{""id"":""abc"",""attributes"":{""title"":{},""altTitles"":[]}}],""total"":1}";

            var items = Subject.ParseResponse(GivenResponse(json));

            items.Should().BeEmpty();
        }

        [Test]
        public void should_handle_empty_page()
        {
            var json = @"{""result"":""ok"",""data"":[],""total"":150}";

            var items = Subject.ParseResponse(GivenResponse(json));

            items.Should().BeEmpty();
        }
    }
}
