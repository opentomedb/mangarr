using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MediaFiles.BookImport.Existing
{
    // Light novels (2026-09, D12): the READ-ONLY view of the Audiobookshelf "Audiobooks" library --
    // one page of every item with its container path and series/title metadata. The files are
    // read from Mangarr's own read-only mount of the same share, never through ABS.
    public class AudiobookshelfItem
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public bool IsFile { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }     // media.metadata.subtitle (titles match calibre, 2026-09-23)
        public string SeriesName { get; set; }
        public string Asin { get; set; }        // media.metadata.asin: the Audible product ABS matched the item to; null when it carries none (B3b, 2026-09-18)
    }

    // Light-novel storage (2026-09-22): one library as GET /api/libraries lists it; Folders are the
    // library's folder paths as Audiobookshelf's container sees them.
    public class AudiobookshelfLibrary
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string MediaType { get; set; }
        public List<string> Folders { get; set; } = new List<string>();
    }

    public interface IAudiobookshelfClient
    {
        List<AudiobookshelfItem> GetLibraryItems();
        void ScanLibrary();
        AudiobookshelfItem FindItemByPath(string absPath);
        bool PatchMetadata(string itemId, string seriesName, string sequence, string title, string subtitle);
        List<AudiobookshelfLibrary> GetLibraries(string url, string apiKey);
    }

    public class AudiobookshelfClient : IAudiobookshelfClient
    {
        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public AudiobookshelfClient(IHttpClient httpClient, IConfigService configService, Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;
        }

        // GET /api/libraries/{id}/items?limit=2000 (Bearer key) -> { "results": [ { "id", "path",
        // "isFile", "media": { "metadata": { "title", "subtitle", "seriesName", "asin" } } }, ... ] }
        public List<AudiobookshelfItem> GetLibraryItems()
        {
            var request = new HttpRequestBuilder(_configService.AudiobookshelfUrl.TrimEnd('/'))
                .Resource($"api/libraries/{_configService.AudiobookshelfLibraryId}/items")
                .AddQueryParam("limit", "2000")
                .SetHeader("Authorization", $"Bearer {_configService.AudiobookshelfApiKey}")
                .Build();
            request.RequestTimeout = TimeSpan.FromSeconds(60);

            var response = _httpClient.Get(request);
            var json = JObject.Parse(response.Content);
            var results = json["results"] as JArray ?? new JArray();

            _logger.Debug("Audiobookshelf returned {0} items", results.Count);

            return results.Select(item => new AudiobookshelfItem
            {
                Id = item["id"]?.ToString(),
                Path = item["path"]?.ToString(),
                IsFile = item["isFile"]?.Type == JTokenType.Boolean && (bool)item["isFile"],
                Title = item["media"]?["metadata"]?["title"]?.ToString(),
                Subtitle = item["media"]?["metadata"]?["subtitle"]?.Type == JTokenType.Null ? null : item["media"]?["metadata"]?["subtitle"]?.ToString(),
                SeriesName = item["media"]?["metadata"]?["seriesName"]?.Type == JTokenType.Null ? null : item["media"]?["metadata"]?["seriesName"]?.ToString(),
                Asin = AsinOf(item["media"]?["metadata"]?["asin"])
            }).ToList();
        }

        // GET /api/libraries (Bearer key) -> { "libraries": [ { "id", "name", "mediaType",
        // "folders": [ { "fullPath" } ] } ] }. Takes the address and key explicitly: the Settings form
        // asks with values it has not saved yet. A refused key is the caller's HttpException (401).
        public List<AudiobookshelfLibrary> GetLibraries(string url, string apiKey)
        {
            var request = new HttpRequestBuilder(url.TrimEnd('/'))
                .Resource("api/libraries")
                .SetHeader("Authorization", $"Bearer {apiKey}")
                .Build();
            request.RequestTimeout = TimeSpan.FromSeconds(30);

            var json = JObject.Parse(_httpClient.Get(request).Content);

            return (json["libraries"] as JArray ?? new JArray()).Select(library => new AudiobookshelfLibrary
            {
                Id = library["id"]?.ToString(),
                Name = library["name"]?.ToString(),
                MediaType = library["mediaType"]?.ToString(),
                Folders = (library["folders"] as JArray ?? new JArray())
                    .Select(folder => folder["fullPath"]?.ToString())
                    .Where(path => path.IsNotNullOrWhiteSpace())
                    .ToList()
            }).ToList();
        }

        // One copy each (2026-09-20): the write side. Mangarr now puts light-novel audio into ABS's
        // own tree, so after a move it asks ABS to scan the library and, once the item exists, sets
        // its series sequence, title and subtitle so the ABS shelf matches the entry.

        // POST /api/libraries/{id}/scan (Bearer key); a non-2xx is the caller's HttpException.
        public void ScanLibrary()
        {
            var request = Builder($"api/libraries/{_configService.AudiobookshelfLibraryId}/scan")
                .Post()
                .Build();
            request.RequestTimeout = TimeSpan.FromSeconds(60);

            _httpClient.Post(request);
        }

        // The list endpoint's path is the ABS container path (/audiobooks/...), compared as-is.
        public AudiobookshelfItem FindItemByPath(string absPath)
        {
            return GetLibraryItems().FirstOrDefault(i => i.Path == absPath);
        }

        // GET /api/items/{id} -> media.metadata.series = [ { "id", "name", "sequence" }, ... ], then
        // PATCH /api/items/{id}/media with { "metadata": { "series": [...], "title", "subtitle" } }.
        // ABS 2.36.1 matches series by name (case-insensitive) and REMOVES any series missing from
        // the payload, so the full list always goes back: the named series with its stored id, name
        // and the new sequence, or appended as { name, sequence } when the item does not have it yet.
        // A range the item already carries ("3-4", a pack adopted onto its first volume) whose first
        // number is ours is kept as it is -- ABS's record is right and richer than ours (the same
        // rule AdoptedAudioSyncService.HasSeries applies). Titles match calibre (2026-09-23): title
        // and subtitle are always compared against the item's current values and sent whenever either
        // differs -- subtitle null/empty means "no subtitle" and is sent to CLEAR an existing one
        // (e.g. a stray "Light Novel"), never left alone the way a null title used to be. Returns
        // false, sending nothing, when nothing would change.
        public bool PatchMetadata(string itemId, string seriesName, string sequence, string title, string subtitle)
        {
            var get = Builder($"api/items/{itemId}").Build();
            get.RequestTimeout = TimeSpan.FromSeconds(60);

            var item = JObject.Parse(_httpClient.Get(get).Content);
            var metadata = item["media"]?["metadata"] as JObject ?? new JObject();
            var current = metadata["series"] as JArray ?? new JArray();

            var merged = new JArray();
            var found = false;
            var sequenceChanged = false;

            foreach (var entry in current.OfType<JObject>())
            {
                var copy = (JObject)entry.DeepClone();

                if (string.Equals(entry["name"]?.ToString(), seriesName, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;

                    var existing = entry["sequence"]?.ToString();

                    if (existing != sequence && !StartsOurRange(existing, sequence))
                    {
                        sequenceChanged = true;
                        copy["sequence"] = sequence;
                    }
                }

                merged.Add(copy);
            }

            if (!found)
            {
                merged.Add(new JObject { ["name"] = seriesName, ["sequence"] = sequence });
            }

            var seriesUnchanged = found && !sequenceChanged;
            var titleUnchanged = title == metadata["title"]?.ToString();
            var currentSubtitle = metadata["subtitle"]?.Type == JTokenType.Null ? null : metadata["subtitle"]?.ToString();
            var subtitleUnchanged = string.Equals(subtitle ?? string.Empty, currentSubtitle ?? string.Empty, StringComparison.Ordinal);

            if (seriesUnchanged && titleUnchanged && subtitleUnchanged)
            {
                _logger.Debug("Audiobookshelf item {0} already has {1} #{2}, title and subtitle; nothing to patch", itemId, seriesName, sequence);
                return false;
            }

            var payload = new JObject { ["series"] = merged };

            if (!titleUnchanged)
            {
                payload["title"] = title;
            }

            if (!subtitleUnchanged)
            {
                payload["subtitle"] = subtitle;
            }

            var request = Builder($"api/items/{itemId}/media")
                .SetHeader("Content-Type", "application/json")
                .Build();
            request.Method = HttpMethod.Patch;
            request.RequestTimeout = TimeSpan.FromSeconds(60);
            request.SetContent(new JObject { ["metadata"] = payload }.ToString(Formatting.None));

            _httpClient.Execute(request);

            _logger.Debug("Audiobookshelf item {0} patched: {1} #{2}{3}{4}", itemId, seriesName, sequence, titleUnchanged ? string.Empty : $", title '{title}'", subtitleUnchanged ? string.Empty : $", subtitle '{subtitle}'");

            return true;
        }

        // "3-4" (or "3") for our "3": the item's own sequence stays
        private static bool StartsOurRange(string current, string sequence)
        {
            var range = AudibleSequence.Parse(current);

            return range.HasValue && MangaVolumeParser.Format(range.Value.From) == sequence;
        }

        private HttpRequestBuilder Builder(string resource)
        {
            return new HttpRequestBuilder(_configService.AudiobookshelfUrl.TrimEnd('/'))
                .Resource(resource)
                .SetHeader("Authorization", $"Bearer {_configService.AudiobookshelfApiKey}");
        }

        // ASIN-first copy-in (B3b, 2026-09-18): absent, null and blank are all "no ASIN".
        private static string AsinOf(JToken token)
        {
            var asin = token == null || token.Type == JTokenType.Null ? null : token.ToString().Trim();

            return asin.IsNullOrWhiteSpace() ? null : asin;
        }
    }
}
