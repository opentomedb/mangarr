using NzbDrone.Common.Extensions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Books
{
    // One display title (2026-09-23, the maintainer): every light-novel volume shows the SAME title in
    // calibre and Audiobookshelf, computed here once and used by both writers (CalibreProxy.SetFields
    // /SetTitle, AdoptedAudioSyncService) so "the same string in both places" holds by construction.
    public static class LightNovelTitles
    {
        // The LN branch of CalibreProxy.SetFields' seriesTitle: a light-novel entry IS the series
        // (it carries no SeriesLinks), so the entry name goes to calibre's series field and here.
        // Today that is exactly Author.Name -- no "(light novel)" stripping exists anywhere on this
        // path (searched the repo for it 2026-09-23); if one is ever added, it belongs here so
        // calibre and ABS never diverge.
        public static string SeriesOf(Author author)
        {
            return author.Name;
        }

        // "<series>: <subtitle> (Vol. <n>)" when the catalogue gave this volume a subtitle
        // (Book.Subtitle, SubtitleOf), else "<series> (Vol. <n>)". <n> is MangaVolumeParser.Format
        // (the same "3.5"/"14" text calibre's series_index and ABS's sequence use). Fix round 3
        // (2026-09-24, defence in depth): Subtitles.IsJunk runs again here on whatever Book.Subtitle
        // holds -- a value stored before the filter existed (a re-stated "Vol. 1", "(Light Novel)",
        // or the series' own name) falls back to the plain title instead of ever being shown, even
        // before a refresh has had the chance to clear the stored value itself.
        //
        // Full-title subtitles (2026-09-24): a subtitle that is the volume's whole title, opening with
        // the series name (Subtitles.IsFullTitle: "Rascal Does Not Dream of Petite Devil Kohai"),
        // renders "<full title> (Vol. <n>)" -- never "<series>: <series> of ...".
        //
        // Preferred Edition (2026-09-24, D4): a non-English edition's label is its own ("Tome <n>",
        // "Band <n>", "第<n>巻") and its subtitle filter knows its words; null/"en" is "Vol. <n>" and
        // today's filter, so every English calibre/Audiobookshelf title is byte-identical.
        public static string Display(string series, double volumeNumber, string subtitle, string editionLanguage = null)
        {
            var n = MangaVolumeParser.Format(volumeNumber);
            var label = EditionLanguages.VolumeLabel(editionLanguage, n);
            var shown = ShownSubtitle(series, subtitle, editionLanguage);

            if (shown == null)
            {
                return $"{series} ({label})";
            }

            return Subtitles.IsFullTitle(shown, series, editionLanguage)
                ? $"{shown} ({label})"
                : $"{series}: {shown} ({label})";
        }

        // The subtitle exactly as Display shows it: trimmed, null when blank or junk. The one source
        // for both the display title and the API's displaySubtitle (the series page row), so the row
        // and calibre/Audiobookshelf can never disagree about which subtitle is shown.
        // A stored "<series>: X" (written before Subtitles.Derive cut the prefix) shows as "X" --
        // the same Subtitles.WithoutSeriesPrefix cut Derive makes.
        public static string ShownSubtitle(string series, string subtitle, string editionLanguage = null)
        {
            var trimmed = Subtitles.WithoutSeriesPrefix(subtitle?.Trim(), series);

            return trimmed.IsNullOrWhiteSpace() || Subtitles.IsJunk(trimmed, series, editionLanguage: editionLanguage) ? null : trimmed;
        }

        // UI pass (2026-09-24, V1): the volume page's title for a light-novel volume -- Display on the
        // same inputs calibre and Audiobookshelf get, so the page shows the same string they do (not
        // Audible's product title). Null for a manga volume or a book with no author: its own title stands.
        public static string DisplayOf(Author author, Book book)
        {
            if (author == null || book == null || author.Library != LibraryType.LightNovel)
            {
                return null;
            }

            return Display(SeriesOf(author), book.VolumeNumber, book.Subtitle, author.Metadata?.Value?.EditionLanguage);
        }

        // Full-title subtitles (2026-09-24): the series page row's subtitle ("Vol. 2: <this>") --
        // ShownSubtitle on the same inputs DisplayOf uses; BookResource.DisplaySubtitle carries it
        // verbatim. Null for a manga volume or a book with no author. Review fix (2026-09-24): ""
        // (not null) for a light-novel volume with no (or a junk) subtitle -- the API drops null
        // fields and the frontend store MERGES an updated book, so a null would leave a cleared
        // subtitle showing until a full reload.
        public static string DisplaySubtitleOf(Author author, Book book)
        {
            if (author == null || book == null || author.Library != LibraryType.LightNovel)
            {
                return null;
            }

            return ShownSubtitle(SeriesOf(author), book.Subtitle, author.Metadata?.Value?.EditionLanguage) ?? string.Empty;
        }

        // calibre title_sort (2026-09-23): "<series> <n>" with n's INTEGER part zero-padded to 4
        // digits so a flat A-Z title-sort column follows volume order ("Sword Art Online 0001" sorts
        // before "Sword Art Online 0011.5") -- any decimal part (a side-story volume) is kept as-is.
        public static string SortTitle(string series, double volumeNumber)
        {
            var formatted = MangaVolumeParser.Format(volumeNumber);
            var dot = formatted.IndexOf('.');
            var whole = dot < 0 ? formatted : formatted.Substring(0, dot);
            var fraction = dot < 0 ? string.Empty : formatted.Substring(dot);

            return $"{series} {whole.PadLeft(4, '0')}{fraction}";
        }
    }
}
