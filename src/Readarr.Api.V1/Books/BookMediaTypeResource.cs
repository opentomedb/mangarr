using System;
using NzbDrone.Core.Books;

namespace Readarr.Api.V1.Books
{
    // One entry per edition of a book: a manga volume has one (archive), a light-novel volume two
    // (ebook, audio). `pending` = an audio edition of a series with no audio yet ("not yet", not
    // Missing). `cutoffNotMet` is judged by the profile of this media type. `coveredByVolume` /
    // `coveredSource` (D8): the audio edition sits inside that volume's file — satisfied, not missing.
    public class BookMediaTypeResource
    {
        public MediaType MediaType { get; set; }
        public bool Monitored { get; set; }
        public bool HasFile { get; set; }
        public int FileCount { get; set; }
        public bool Pending { get; set; }
        public bool Unreleased { get; set; }        // audio Audible lists for later / not yet (series audio available)
        public DateTime? AudioReleaseDate { get; set; }
        public bool CutoffNotMet { get; set; }
        public double? CoveredByVolume { get; set; }
        public string CoveredSource { get; set; }
    }
}
