using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    // Beta readiness (2026-09-28, E6): the one reachable path to the dead api.bookinfo.club host --
    // import identification by a file's Goodreads id tag -- answers "no candidates" without a request;
    // so do the ISBN/ASIN lookups, which asked Goodreads (fix round).
    // The search prefixes (isbn:, asin:, edition:) never get that far: SearchForNewBook returns from
    // its SPIKE branch before parsing them.
    [TestFixture]
    public class BookInfoProxyRetiredHostFixture : CoreTest<BookInfoProxy>
    {
        [TestCase(true)]
        [TestCase(false)]
        public void goodreads_book_id_lookup_makes_no_request(bool getAllEditions)
        {
            Subject.SearchByGoodreadsBookId(1128601, getAllEditions).Should().BeEmpty();

            Mocker.GetMock<IMetadataRequestBuilder>().Verify(s => s.GetRequestBuilder(), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(s => s.Get(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(s => s.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        // Beta readiness fix round (2026-09-28, E6): import identification by an ISBN/ASIN tag never asks
        // Goodreads (goodreads.com/book/auto_complete through IGoodreadsSearchProxy).
        [Test]
        public void isbn_and_asin_lookups_make_no_request()
        {
            Subject.SearchByIsbn("9781975300005").Should().BeEmpty();
            Subject.SearchByAsin("B0192CTMYG").Should().BeEmpty();

            Mocker.GetMock<IGoodreadsSearchProxy>().Verify(s => s.Search(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ICachedHttpResponseService>().VerifyNoOtherCalls();
            Mocker.GetMock<IMetadataRequestBuilder>().Verify(s => s.GetRequestBuilder(), Times.Never());
            Mocker.GetMock<IHttpClient>().VerifyNoOtherCalls();
        }
    }
}
