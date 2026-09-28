using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books.Calibre;

namespace NzbDrone.Core.MediaFiles.BookImport.Existing
{
    // Light novels (2026-09, D12): the READ-ONLY view of the live Calibre library through its
    // content server (the server, library and login in Settings): search by series or author, read
    // one book's metadata. Never calibredb, never the library folder (the standing Calibre rule).
    // The EPUB's path (one copy each, 2026-09-20) comes from Readarr's own CalibreProxy; nothing is
    // downloaded any more.
    public class CalibreContentBook
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Series { get; set; }
        public double? SeriesIndex { get; set; }
        public List<string> Formats { get; set; }
    }

    public interface ICalibreContentServerClient
    {
        List<int> SearchSeries(string seriesName);

        // The books by exactly this author (calibre's =name match: whole name, case-insensitive).
        // At most five: the writer ladder only needs one to read calibre's spelling of the name.
        List<int> SearchAuthor(string name);
        CalibreContentBook GetBook(int calibreId);
    }

    public class CalibreContentServerClient : ICalibreContentServerClient
    {
        private readonly IHttpClient _httpClient;
        private readonly ILightNovelCalibreSettings _settings;

        public CalibreContentServerClient(IHttpClient httpClient, ILightNovelCalibreSettings settings)
        {
            _httpClient = httpClient;
            _settings = settings;
        }

        // Light-novel storage (2026-09-22): the same server, library and login as every other calibre
        // call (ForConfig), instead of its own URL read and a second hard-coded library id.
        private HttpRequestBuilder Builder(Func<string, string> resource)
        {
            var settings = _settings.ForConfig();
            var builder = new HttpRequestBuilder(HttpRequestBuilder.BuildBaseUrl(settings.UseSsl, settings.Host, settings.Port, settings.UrlBase))
                .Resource(resource(settings.Library));

            if (settings.Username.IsNotNullOrWhiteSpace())
            {
                builder.NetworkCredential = new NetworkCredential(settings.Username, settings.Password);
            }

            return builder;
        }

        // GET /ajax/search/books?query=series:"<name>"&num=500 -> { "total_num": n, "book_ids": [...] }
        public List<int> SearchSeries(string seriesName)
        {
            var request = Builder(library => $"ajax/search/{library}")
                .AddQueryParam("query", $"series:\"{seriesName.Replace("\"", string.Empty)}\"")
                .AddQueryParam("num", "500")
                .Build();
            request.RequestTimeout = TimeSpan.FromSeconds(30);

            var response = _httpClient.Get(request);
            var json = JObject.Parse(response.Content);

            return json["book_ids"]?.Select(t => (int)t).ToList() ?? new List<int>();
        }

        // GET /ajax/search/books?query=authors:"=<name>"&num=5 -> { "total_num": n, "book_ids": [...] }
        // (one copy each, 2026-09-20). Quoted like SearchSeries: an embedded quote is dropped rather
        // than escaped, so a name can never break out of the query string.
        public List<int> SearchAuthor(string name)
        {
            var request = Builder(library => $"ajax/search/{library}")
                .AddQueryParam("query", $"authors:\"={name.Replace("\"", string.Empty)}\"")
                .AddQueryParam("num", "5")
                .Build();
            request.RequestTimeout = TimeSpan.FromSeconds(30);

            var response = _httpClient.Get(request);
            var json = JObject.Parse(response.Content);

            return json["book_ids"]?.Select(t => (int)t).ToList() ?? new List<int>();
        }

        // GET /ajax/book/<id>/books -> { "title", "series", "series_index", "formats": ["EPUB", ...], ... }
        public CalibreContentBook GetBook(int calibreId)
        {
            var request = Builder(library => $"ajax/book/{calibreId}/{library}")
                .Build();
            request.RequestTimeout = TimeSpan.FromSeconds(30);

            var response = _httpClient.Get(request);
            var json = JObject.Parse(response.Content);
            var series = json["series"];
            var index = json["series_index"];

            return new CalibreContentBook
            {
                Id = calibreId,
                Title = json["title"]?.ToString(),
                Series = series == null || series.Type == JTokenType.Null ? null : series.ToString(),
                SeriesIndex = index != null && (index.Type == JTokenType.Float || index.Type == JTokenType.Integer) ? (double?)index : null,
                Formats = json["formats"]?.Select(t => t.ToString()).ToList() ?? new List<string>()
            };
        }
    }
}
