using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.Gcd
{
    // One volume of a release line. Composition is the list of original tankoubon volumes this
    // volume contains (omnibus only, e.g. [1,2,3]); null for a plain single volume.
    public class GcdVolume
    {
        public int VolumeNumber { get; set; }
        public string Title { get; set; }
        public string ReleaseDate { get; set; }
        public string Isbn13 { get; set; }
        public string Isbn10 { get; set; }
        public int? PageCount { get; set; }
        public List<int> Composition { get; set; }

        // Optional (OpenTome artifacts): a cover looked up by THIS edition's ISBN, never by a
        // title search -- which is how a volume ends up wearing another volume's face. Null
        // means "no ISBN-keyed cover known"; the provider then falls back exactly as before.
        public string CoverUrl { get; set; }
        public string CoverSource { get; set; }

        // Preferred Edition (2026-09-24, D6): the coarse date the artifact keeps beside the day-only
        // release_date ("2019", "2026-11"), its precision (day | month | year) and milestone type
        // (published | on_sale | projected | unknown). Null on older artifacts, and whenever the
        // caller reads volumes without includeEditionDates (the English path).
        public string ReleaseDateRaw { get; set; }
        public string ReleaseDatePrecision { get; set; }
        public string ReleaseDateType { get; set; }
    }
}
