using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.ImportLists.MangaDex
{
    public class MangaDexAuthResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
    }

    public class MangaDexFollowsResponse
    {
        public string Result { get; set; }
        public List<MangaDexManga> Data { get; set; }
        public int Total { get; set; }
    }

    public class MangaDexManga
    {
        public string Id { get; set; }
        public MangaDexMangaAttributes Attributes { get; set; }
    }

    public class MangaDexMangaAttributes
    {
        public Dictionary<string, string> Title { get; set; }
        public List<Dictionary<string, string>> AltTitles { get; set; }
    }
}
