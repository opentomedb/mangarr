using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Equ;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.Books
{
    public class Edition : Entity<Edition>
    {
        public Edition()
        {
            Overview = string.Empty;
            Images = new List<MediaCover.MediaCover>();
            Links = new List<Links>();
            Ratings = new Ratings();
        }

        // These correspond to columns in the Books table
        // These are metadata entries
        public int BookId { get; set; }
        public string ForeignEditionId { get; set; }
        public string TitleSlug { get; set; }
        public string Isbn13 { get; set; }
        public string Asin { get; set; }

        // Audiobook identity (2026-09-17, D1): what Audible calls the audio edition, its runtime,
        // release date and the volume it covers. Null = no identity (manga always). Columns land
        // in 051; a Google/ISBN-only pass never sets them, so UseMetadataFrom keeps the local values.
        public string AudiobookTitle { get; set; }
        public string AudiobookSubtitle { get; set; }
        public int? RuntimeMinutes { get; set; }
        public DateTime? AudioReleaseDate { get; set; }

        // Unreleased audio (2026-09-21): for a series whose audio IS available, an Audio edition
        // Audible lists for a future date (a pre-order -- SAO 23, KonoSuba 15) or does not list at
        // all yet (SAO 24-28). Not missing, not searched automatically, not counted; each refresh
        // re-asks Audible, so the volume becomes wanted the day it is out. Callers check the
        // series flag and the file: an edition with a file is never unreleased.
        public bool IsUnreleasedAudio(DateTime now)
        {
            return MediaType == MediaType.Audio && (Asin.IsNullOrWhiteSpace() || !AudioReleaseDate.HasValue || AudioReleaseDate.Value > now);
        }
        public double? CoveredByVolume { get; set; }

        // Covered volumes (2026-09-17, D4a): who wrote CoveredByVolume — CoveredSources.Audible,
        // .Import or .Manual; null when uncovered. Column lands in 052.
        public string CoveredSource { get; set; }
        public string Title { get; set; }
        public string Language { get; set; }
        public string Overview { get; set; }
        public string Format { get; set; }
        public bool IsEbook { get; set; }

        // Archive (manga) | Ebook | Audio. Minted by BookInfoProxy per volume; the column lands in 048.
        public MediaType MediaType { get; set; }
        public string Disambiguation { get; set; }
        public string Publisher { get; set; }
        public int PageCount { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public List<MediaCover.MediaCover> Images { get; set; }
        public List<Links> Links { get; set; }
        public Ratings Ratings { get; set; }

        // These are Mangarr generated/config
        public bool Monitored { get; set; }
        public bool ManualAdd { get; set; }

        // D5 (2026-09-17): set by BookInfoProxy on a remote edition whose volume's Google ISBN record
        // was rejected (its title does not name the series) and no other source gave a blurb:
        // UseMetadataFrom clears the stored text instead of keeping it. Never a column (ignored in
        // TableMapping) and never stored, so it stays out of the up-to-date comparison too.
        [MemberwiseEqualityIgnore]
        public bool OverviewRejected { get; set; }

        // Covered volumes (2026-09-17, D4a): set by BookInfoProxy when Audible answered this pass —
        // the remote's CoveredByVolume (or its absence) is then an assertion the ratchet applies to
        // an "audible" mark; an "import" mark is never touched by a refresh. Not a column.
        [MemberwiseEqualityIgnore]
        public bool CoveredAsserted { get; set; }

        // B3b (2026-09-18): set by BookInfoProxy on a remote edition minted with NO image because
        // Google did not answer for its volume this pass (quota) and no other source had a cover.
        // UseDbFieldsFrom then carries the local images onto it so the row compares equal and is
        // not rewritten every quota-out pass (UseMetadataFrom would keep them anyway). Not a column.
        [MemberwiseEqualityIgnore]
        public bool CoverMissed { get; set; }

        // These are dynamically queried from other tables
        [MemberwiseEqualityIgnore]
        public LazyLoaded<Book> Book { get; set; }
        [MemberwiseEqualityIgnore]
        public LazyLoaded<List<BookFile>> BookFiles { get; set; }

        public override string ToString()
        {
            return string.Format("[{0}][{1}]", ForeignEditionId, Title.NullSafe());
        }

        public override void UseMetadataFrom(Edition other)
        {
            var localTitle = Title;

            ForeignEditionId = other.ForeignEditionId;
            TitleSlug = other.TitleSlug;

            // Metadata ratchet: a refresh where a source comes back empty (quota blip, delisted
            // edition, gap) must not erase a real value we already know — page counts and release
            // dates of published volumes don't un-exist. Same keep-local pattern as Overview/Images.
            Isbn13 = other.Isbn13.IsNullOrWhiteSpace() ? Isbn13 : other.Isbn13;

            // Audiobook identity (2026-09-17, D1): only a pass Audible answered carries these; a
            // Google/ISBN-only refresh has none and must not erase an identity we hold. The ASIN was
            // copied unconditionally before, which would have cleared it on exactly that pass.
            // Null keeps the local value; a zero runtime is a value and replaces it.
            Asin = other.Asin.IsNullOrWhiteSpace() ? Asin : other.Asin;
            AudiobookTitle = other.AudiobookTitle.IsNullOrWhiteSpace() ? AudiobookTitle : other.AudiobookTitle;
            AudiobookSubtitle = other.AudiobookSubtitle.IsNullOrWhiteSpace() ? AudiobookSubtitle : other.AudiobookSubtitle;
            RuntimeMinutes = other.RuntimeMinutes ?? RuntimeMinutes;
            AudioReleaseDate = other.AudioReleaseDate ?? AudioReleaseDate;

            // Covered volumes (2026-09-17, D4a): a mark has one owner. A pass Audible answered asserts
            // its mark (or its absence) over an "audible" mark; an app-owned mark (`import`, a
            // spanning file; `manual`, the user's choice) is only cleared by the D4a reversals or the
            // user, never by a refresh. A pass Audible did not answer carries no assertion: the local
            // mark is kept, a remote one taken — but never re-labelled: the remote's value may be the
            // local app-owned mark UseDbFieldsFrom carried over, and calling it "audible" would hand
            // the next answering pass a mark it may clear.
            if (other.CoveredAsserted)
            {
                if (!CoveredSources.IsAppOwned(CoveredSource))
                {
                    CoveredByVolume = other.CoveredByVolume;
                    CoveredSource = other.CoveredByVolume.HasValue ? CoveredSources.Audible : null;
                }
            }
            else
            {
                CoveredByVolume = other.CoveredByVolume ?? CoveredByVolume;
                if (other.CoveredByVolume.HasValue && !CoveredSources.IsAppOwned(CoveredSource))
                {
                    CoveredSource = CoveredSources.Audible;
                }
            }

            Title = other.Title;
            Language = other.Language;
            // D3 (2026-09-16): the "<Series>, volume N." placeholder earlier builds minted when no
            // source had a description is not local data worth keeping — a real description
            // replaces it and an empty remote clears it. Checked against the local title before the
            // remote's is adopted above (same value on every refresh; the stored name never changes).
            // The same goes for a stored text that fails D2 (a "Notebook" listing, CJK script, a
            // stub under 40 chars — what older builds stored unvalidated): a real remote replaces
            // it, an empty remote clears it. The stored text carries no language of its own, so a stored
            // blurb in another Latin-script language is not caught here (the reason D2 has it).
            // Preferred Edition (2026-09-24, M6b pre-review fix): the gate's EXPECTED language is the
            // edition's (the remote's Language, adopted above) -- a stored Japanese/Korean/Chinese blurb is
            // that edition's, not CJK junk. "eng", null and unknown codes keep the English rule exactly.
            var localOverview = IsPlaceholderOverview(Overview, localTitle) || !GoogleBooksService.IsAcceptableDescription(Overview, null, EditionLanguages.FromIso3(Language), out _)
                ? string.Empty
                : Overview;
            // D5 (2026-09-17): a rejected remote clears the text — the source that gave it was the
            // ISBN record that no longer passes, and a wrong blurb is worse than none.
            Overview = other.OverviewRejected ? string.Empty : (other.Overview.IsNullOrWhiteSpace() ? localOverview : other.Overview);
            Format = other.Format;
            IsEbook = other.IsEbook;
            MediaType = other.MediaType;
            Disambiguation = other.Disambiguation;
            Publisher = other.Publisher;
            PageCount = other.PageCount > 0 ? other.PageCount : PageCount;
            ReleaseDate = other.ReleaseDate ?? ReleaseDate;
            Images = other.Images.Any() ? other.Images : Images;
            Links = other.Links;
            Ratings = other.Ratings;
        }

        // The placeholder had exactly one form — the edition title with " Vol. N" turned into
        // ", volume N." (BookInfoProxy.MintEdition before 2026-09-16; 464 of 464 in the live library
        // matched it, no real blurb contains ", volume "). Matched exactly: a real description that
        // happens to end in ", volume 3." is not it.
        private static readonly Regex VolumeSuffix = new Regex(@" Vol\. (\S+)$", RegexOptions.Compiled);

        public static bool IsPlaceholderOverview(string overview, string title)
        {
            if (overview.IsNullOrWhiteSpace() || title.IsNullOrWhiteSpace() || !VolumeSuffix.IsMatch(title))
            {
                return false;
            }

            return overview == VolumeSuffix.Replace(title, ", volume $1.");
        }

        public override void UseDbFieldsFrom(Edition other)
        {
            Id = other.Id;
            BookId = other.BookId;
            Book = other.Book;
            Monitored = other.Monitored;
            ManualAdd = other.ManualAdd;

            // Covered volumes (2026-09-17, D4a): an app-owned mark (`import`, `manual`) is data the
            // refresh never changes — carry it onto the remote where Monitored already goes, so
            // equality agrees with what UseMetadataFrom will do and an unchanged covered row stays
            // UpToDate.
            if (CoveredSources.IsAppOwned(other.CoveredSource))
            {
                CoveredByVolume = other.CoveredByVolume;
                CoveredSource = other.CoveredSource;
            }

            // B3b (2026-09-18): a pass that could not fetch the cover (Google quota, no other
            // source) mints no image, so the remote compared unequal to a stored row that has one
            // and was rewritten every quota-out pass. UseMetadataFrom keeps the local images on an
            // empty remote anyway — carrying them here makes the comparison say so, and the row
            // stays UpToDate. A remote that did not miss Google is left as minted.
            if (CoverMissed && !Images.Any() && other.Images.Any())
            {
                Images = other.Images;
            }
        }

        public override void ApplyChanges(Edition other)
        {
            ForeignEditionId = other.ForeignEditionId;
            Monitored = other.Monitored;
        }
    }
}
