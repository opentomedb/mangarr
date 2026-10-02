using System;
using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource.Manga
{
    // Resolved series metadata for one manga series: the volume structure (count + per-volume data)
    // plus presentation fields. Produced by IMangaSeriesMetadataProvider from the GCD spine (when a
    // bundled artifact is present) or the live sources, and consumed by BookInfoProxy to assemble the
    // Author + volume Books.
    public class MangaSeriesMetadata
    {
        public string DisplayName { get; set; }
        public AuthorStatusType Status { get; set; }
        public int VolumeCount { get; set; }       // English-available (grabbable) volumes
        public int OriginTotal { get; set; }        // the original market's print total (was JapaneseTotal; KR/CN piece 2, 2026-10-02, M6); >= VolumeCount
        public string Overview { get; set; }
        public string CoverUrl { get; set; }         // series poster: the edition's volume-1 cover (English for an English series, D4), AniList's art as the last resort
        public string PosterSource { get; set; }     // pin | mangadex-en | mangadex-<lang> (a non-English edition's locale art, Preferred Edition 2026-09-24) | opentome | google | google-thumbnail | anilist | mangadex | anilist-display | none (D10; pin = a pinned volume-1 cover, B3b; anilist-display = the catalogue's display-only AniList entry, 2026-09-24; google-thumbnail = Google's 128 px volume-1 thumbnail asked at poster size, 2026-09-24)
        public bool PosterFellBackOnMiss { get; set; } // volume 1's Google lookup did not answer and the poster landed below Google's tier: BookInfoProxy keeps the stored poster this pass
        public bool DisplayFetchFailed { get; set; }   // the display fallback was needed and its AniList fetch did not answer: BookInfoProxy keeps the stored poster (when this pass has none) and the stored overview (when this pass has none)
        public string VolumeCoverUrl { get; set; }   // the per-volume last resort (= CoverUrl, except an anilist-display poster, which is never a volume's; applied by BookInfoProxy to a volume without its own)
        public decimal RatingValue { get; set; }     // 0-5; 0 when no score
        public List<MangaVolumeMetadata> Volumes { get; set; } = new List<MangaVolumeMetadata>();

        // Latin alternate titles (romaji / licensed variants) for indexer search recall —
        // releases are often named by these rather than the English display title.
        public List<string> AltTitles { get; set; } = new List<string>();

        // The line this series belongs to (artifact parent_series_id resolved to its name): the
        // collection it is part of. Null when it is a top-level line or the artifact has no parent data.
        public string ParentName { get; set; }

        // Light novels are catalogue-only (D11): true when the artifact has no English novel line
        // for the name. Volumes are empty and the caller refuses the add (BookInfoProxy throws
        // NotInCatalogueException; the Add page shows the OpenTome link). Never true for manga.
        public bool NotInCatalogue { get; set; }

        // The AniList entry the presentation came from (D4) and how it was found (D7): primary |
        // synonym | alias | catalogue | id | relaxed. AniListId is set only for the first five —
        // a relaxed (search-box) hit is never pinned, so a later refresh can still improve it —
        // and null when AniList missed. BookInfoProxy stores it on AuthorMetadata.
        public int? AniListId { get; set; }
        public string MatchedVia { get; set; }

        public bool AudibleAnswered { get; set; }   // Audible answered (even with nothing) this pass: the volumes' CoveredByVolume are assertions (D4a)
        public bool AudioSkipped { get; set; }      // Preferred Edition (2026-09-24, D8): a non-English light novel not 1:1 with the English line -- no Audible step; its Audio editions are minted unmonitored

        // One copy each (2026-09-20): the catalogue's author for a light novel; null for manga.
        public string Writer { get; set; }

        // Preferred Edition (2026-09-24). IdentityName: the anchor's (English) display name -- what the
        // id slug and the metadata sources' search term are made of; equals DisplayName on the English
        // path. EditionLanguage: the edition this pass took its structure from (null = English).
        // TomeLineId: the OpenTome id of the line the structure came from (bound by BookInfoProxy).
        public string IdentityName { get; set; }
        public string EditionLanguage { get; set; }
        public string TomeLineId { get; set; }

        // 2026-09-26: the TomeLineId line is a collected edition (IsCollectedEdition over its volumes).
        public bool EditionCollected { get; set; }

        // KR/CN consumer (2026-09-29): this pass's edition line came from the new-entry fallback, or the entry is
        // already a fallback series (EditionRequest.Fallback).
        public bool EditionFallback { get; set; }
    }

    public class MangaVolumeMetadata
    {
        public double VolumeNumber { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string ReleaseDatePrecision { get; set; }  // Preferred Edition (2026-09-24, D6): null = day, "month" / "year" = an edition's coarse date (EditionDates)
        public string Isbn13 { get; set; }
        public int PageCount { get; set; }
        public string CoverUrl { get; set; }        // the first cover in the edition's language (D5): artifact ISBN cover, MangaDex en (mangadex-<lang> for a non-English edition), Google; null -> the series poster. A pin (overrides.json coverUrl) wins over all of them
        public string CoverSource { get; set; }     // pin | opentome | mangadex-en | mangadex-<lang> (Preferred Edition 2026-09-24) | google | series (D10)
        public bool GoogleMissed { get; set; }      // Google did not answer for this volume this pass (429 / 5xx / transport / the quota breaker): with no CoverUrl, BookInfoProxy substitutes no poster so the ratchet keeps the local cover
        public bool GoogleRejected { get; set; }    // the ISBN record did not name the series (D5): nothing was taken from it and BookInfoProxy clears what an earlier pass took. False again when the Audible step showed the title is the volume's own subtitle and the record was re-admitted (B3b)
        public string Overview { get; set; }        // the validated description (D1/D2); null -> empty overview, no placeholder (D3)
        public string OverviewSource { get; set; }  // isbn | title | none (D10)

        // Audiobook identity B1 (2026-09-17, D1-D3): light novels on the refresh path only — a manga
        // volume never carries any of these (D7), nor does a lookup-path or disk-discovered volume.
        public string Subtitle { get; set; }              // Subtitles.Derive: the catalogue's volume title, else the Audible product's, else the accepted ISBN record's; null = none
        public bool SubtitleRejected { get; set; }         // fix round 3 (2026-09-24): a candidate existed this pass and Subtitles.Derive's junk filter rejected it -- Book.UseMetadataFrom clears the stored value instead of keeping it (unlike a null Subtitle with no candidate at all, e.g. a Google quota miss, which keeps what is stored)
        public string ArtifactTitle { get; set; }         // the catalogue's (OpenTome) volume title for this line, raw; input to Subtitle
        public AudiobookIdentity Audio { get; set; }      // the Audible product this volume carries (its own, or the pack it opens); null = none
        public double? CoveredByVolume { get; set; }      // inside another volume's pack: that volume's number; null otherwise
    }

    // The Audible product a light-novel volume carries (D1): what BookInfoProxy mints onto the Audio
    // edition. CoversFrom/To are set for a pack ("1-2") only — the volumes it spans; a single has neither.
    public class AudiobookIdentity
    {
        public string Asin { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public int? RuntimeMinutes { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public double? CoversFrom { get; set; }
        public double? CoversTo { get; set; }
    }
}
