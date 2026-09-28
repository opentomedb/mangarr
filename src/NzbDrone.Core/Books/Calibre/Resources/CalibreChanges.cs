using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.Books.Calibre
{
    public class CalibreChangesPayload
    {
        public CalibreChanges Changes { get; set; }
        [JsonProperty("loaded_book_ids")]
        public List<int> LoadedBookIds { get; set; }
    }

    public class CalibreChanges
    {
        public string Title { get; set; }

        // calibre's own field name is `sort`, not `title_sort` (fix round 1, C1: calibre raises
        // KeyError: 'title_sort' -> HTTP 500 on every light-novel SetFields). Declared right after
        // Title: calibre applies `changes` in JSON order, and a *different* title resets the sort to
        // a title-derived value, so the sort must be written after the title it belongs to.
        // Light-novel books only -- LightNovelTitles.SortTitle.
        [JsonProperty("sort")]
        public string Sort { get; set; }
        public List<string> Authors { get; set; }
        public string Cover { get; set; }
        [JsonProperty("pubdate")]
        public DateTime? PubDate { get; set; }
        public string Publisher { get; set; }
        public string Languages { get; set; }
        public List<string> Tags { get; set; }
        public string Comments { get; set; }

        // One display title (2026-09-23): nullable so AddFormat/RemoveFormats' partial payloads can
        // omit it -- decimal's own default (0) would otherwise serialize as "rating": 0
        // (DefaultValueHandling.Include, Json.cs) and clear a book's rating in calibre on every
        // format add/remove.
        public decimal? Rating { get; set; }
        public Dictionary<string, string> Identifiers { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public string Series { get; set; }
        [JsonProperty("series_index")]
        public double? SeriesIndex { get; set; }
        [JsonProperty("added_formats")]
        public List<CalibreAddFormat> AddedFormats { get; set; }
        [JsonProperty("removed_formats")]
        public List<string> RemovedFormats { get; set; }
    }

    public class CalibreAddFormat
    {
        public string Ext { get; set; }
        [JsonProperty("data_url")]
        public string Data { get; set; }
    }

    // One display title, fix round 1 (2026-09-23, C2): the adopted-book title exception is title
    // and sort ONLY -- the maintainer approved nothing else. A dedicated DTO (rather than a mostly-null
    // CalibreChanges) means there is no NullValueHandling.Include property (Series) that could leak
    // a stray "series": null and re-file/re-case an adopted book's series.
    public class CalibreTitlePayload
    {
        public CalibreTitleChanges Changes { get; set; }
        [JsonProperty("loaded_book_ids")]
        public List<int> LoadedBookIds { get; set; }
    }

    public class CalibreTitleChanges
    {
        public string Title { get; set; }

        // Declared after Title -- see CalibreChanges.Sort: calibre applies `changes` in JSON order,
        // and a different title resets the sort.
        [JsonProperty("sort")]
        public string Sort { get; set; }
    }
}
