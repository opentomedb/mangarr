using System.Collections.Generic;

namespace Readarr.Api.V1.PreferredEdition
{
    public class EditionMarketResource
    {
        public string Language { get; set; }
        public string Name { get; set; }
        public int Lines { get; set; }
    }

    public class EditionMarketsResource
    {
        public List<EditionMarketResource> Languages { get; set; }
        public int OtherEditionSeries { get; set; }

        // Preferred Edition (2026-09-24): the same count split by library, so the Settings -> UI note
        // can say where those series are (one series index per library).
        public int OtherEditionManga { get; set; }
        public int OtherEditionLightNovels { get; set; }
    }
}
