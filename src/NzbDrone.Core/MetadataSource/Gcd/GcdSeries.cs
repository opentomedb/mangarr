namespace NzbDrone.Core.MetadataSource.Gcd
{
    // One release line from the bundled metadata artifact. The artifact stores localized English
    // editions and omnibus/3-in-1 lines as separate series, so each is its own row; an omnibus line
    // points at its original-language base via OrigSeriesId. Cross-IDs feed the live-source fallback chain.
    public class GcdSeries
    {
        public int GcdSeriesId { get; set; }
        public string Name { get; set; }
        public int? YearBegan { get; set; }
        public string Publisher { get; set; }
        public string Language { get; set; }
        public string Country { get; set; }
        public bool IsOmnibus { get; set; }
        public int VolumeCount { get; set; }
        public string Status { get; set; }
        public int? OrigSeriesId { get; set; }
        public int? AnilistId { get; set; }
        public long? MangaUpdatesId { get; set; }
        public string MangaDexId { get; set; }

        // Optional columns (OpenTome artifacts, schema_version 2). Null when the artifact predates
        // them; the ranking treats null as "manga, unknown dated count" so old artifacts still work.
        public string Medium { get; set; }
        public int? DatedCount { get; set; }
        public bool IsMain { get; set; }

        // OpenTome artifacts from 2026-09-04: the gcd_series_id of the line this one belongs to in
        // the same market and medium (an arc under its parent line, a side story under the line
        // named after the work). Null at the top of a collection or on older artifacts.
        public int? ParentSeriesId { get; set; }

        // OpenTome artifacts from 2026-09-20: the work's author (first name of the tier-0 claim).
        // Null on older artifacts.
        public string Author { get; set; }

        // OpenTome artifacts from 2026-09-24: a DISPLAY-ONLY AniList id, set only where anilist_id is
        // null -- no AniList entry is this line, but this one (its parent work, or the same work in
        // the other medium; DisplayAnilistVia 'parent' | 'medium') is a fair picture to show. Never a
        // binding. Null on older artifacts.
        public int? DisplayAnilistId { get; set; }
        public string DisplayAnilistVia { get; set; }

        // Preferred Edition (2026-09-24): the OpenTome release-line id (rl_..., the public contract a
        // series binds to), the work id every market line of the work shares (the sibling hop), and the
        // line's title in its own language (OpenTome v0; null on older artifacts and for English lines).
        public string TomeId { get; set; }
        public string TomeWorkId { get; set; }
        public string LocalName { get; set; }
    }
}
