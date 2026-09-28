using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles.PdfConversion
{
    // Some digital manga PDFs (notably Kodansha editions) store pages in fully reversed order — the
    // front cover is the LAST page and the colophon (copyright/ISBN back-matter) is page 1 — so a
    // left-to-right reader shows the book back-to-front. Detect that from the FIRST page's text:
    // a cover is artwork with little extractable text, whereas a colophon is text-heavy with ISBN /
    // copyright / publication markers. When page 1 reads as a colophon, the file is reversed and the
    // converter flips the page sequence. Western-order editions put the cover (image) first, so this
    // does not misfire on them.
    public static class PageOrderNormalizer
    {
        private static readonly Regex ColophonMarkers = new Regex(
            @"\bISBN\b|copyright|©|all rights reserved|first published|electronic publishing|digital edition|no portion of this book",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Two independent signals, either of which flips the book:
        //  - text: page 1 reads as a colophon (>=2 distinct markers) — for text-layer PDFs.
        //  - brightness: the colorful COVER sits at the BACK. In a manga the cover is the darkest
        //    page; every other page (story art on white, colophon, blanks) is lighter. So a reversed
        //    file has a cover-like (dark) LAST page while its FIRST page is a non-cover page. Test:
        //      last  < 0.80  (last page is cover-like dark)   AND
        //      first >= 0.75 (first page is NOT the dark cover — it's a story/colophon page) AND
        //      first  > last (the cover really is the darker end)
        //    A correctly-ordered book has the dark cover FIRST (first < 0.75) and a white colophon
        //    LAST (last >= 0.80), so it fails both bands — the asymmetry keeps it safe. This works
        //    even when page 1 is only a grayish 0.77 colophon (which a page-1-must-be-white gate,
        //    or a large first-minus-last margin, both missed on real files).
        public static bool IsReversed(string firstPageText, double firstBrightness = -1, double lastBrightness = -1, double firstSaturation = -1, double lastSaturation = -1)
        {
            if (HasColophonText(firstPageText))
            {
                return true;
            }

            // Saturation vote: a cover is colorful, an endpaper/colophon/B&W interior page is
            // near-grayscale. When the LAST page is clearly saturated and the FIRST is essentially
            // colorless, the cover sits at the back regardless of how bright it is — the Vinland
            // Saga v12 case, where a bright cover defeated the brightness bands below. Strict
            // thresholds keep it safe: correct-order books have the colorful cover FIRST (first
            // saturation high), and fully grayscale books have no saturation at either end.
            if (firstSaturation >= 0 && lastSaturation >= 0 &&
                firstSaturation < 0.05 && lastSaturation >= 0.15)
            {
                return true;
            }

            return firstBrightness >= 0.75 &&
                   lastBrightness >= 0 &&
                   lastBrightness < 0.80 &&
                   firstBrightness - lastBrightness > 0.03;
        }

        private static bool HasColophonText(string firstPageText)
        {
            if (firstPageText.IsNullOrWhiteSpace())
            {
                return false;
            }

            // Need at least two distinct colophon signals so an incidental "©" on a title page
            // doesn't flip a correctly-ordered book.
            var distinct = new System.Collections.Generic.HashSet<string>();
            foreach (Match m in ColophonMarkers.Matches(firstPageText))
            {
                distinct.Add(m.Value.ToLowerInvariant());
            }

            return distinct.Count >= 2;
        }
    }
}
