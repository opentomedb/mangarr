using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.PdfConversion;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.PdfConversion
{
    [TestFixture]
    public class PageOrderNormalizerFixture : CoreTest
    {
        [Test]
        public void should_detect_reversed_when_page_one_is_a_colophon()
        {
            // The real Fragrant Flower v1 page-1 text (Kodansha colophon).
            var text = "The Fragrant Flower Blooms With Dignity 1\nKodansha Digital Edition\n" +
                       "copyright (c) 2022 Saka Mikami\nAll rights reserved.\nFirst published in Japan in 2022 by Kodansha\n" +
                       "ISBN: 9798889336358\nNo portion of this book may be reproduced";

            PageOrderNormalizer.IsReversed(text).Should().BeTrue();
        }

        [Test]
        public void should_not_flag_a_cover_page_with_no_text()
        {
            PageOrderNormalizer.IsReversed(null).Should().BeFalse();
            PageOrderNormalizer.IsReversed("").Should().BeFalse();
            PageOrderNormalizer.IsReversed("The Fragrant Flower Blooms With Dignity").Should().BeFalse(); // cover title only
        }

        [Test]
        public void should_not_flag_on_a_single_incidental_marker()
        {
            // A title page that merely carries a lone copyright glyph is not a colophon.
            PageOrderNormalizer.IsReversed("Chapter 1\n(c) the author").Should().BeFalse();
        }

        [Test]
        public void should_detect_with_two_distinct_markers()
        {
            PageOrderNormalizer.IsReversed("ISBN 978-1-64651 ... all rights reserved").Should().BeTrue();
        }

        [Test]
        public void should_detect_reversed_by_brightness_for_image_only_pdf()
        {
            // No text layer (image-only), but page 1 near-white (colophon) and last page a dark
            // colorful cover -> reversed.
            PageOrderNormalizer.IsReversed(null, firstBrightness: 0.96, lastBrightness: 0.62).Should().BeTrue();
        }

        [Test]
        public void should_detect_reversed_by_saturation_when_cover_is_bright()
        {
            // Vinland Saga v12: first page = light-gray runic endpaper (bright AND colorless),
            // last page = the true cover — too bright for the brightness bands but strongly
            // saturated. The saturation vote catches what brightness can't.
            PageOrderNormalizer.IsReversed(null,
                                           firstBrightness: 0.86,
                                           lastBrightness: 0.82,
                                           firstSaturation: 0.01,
                                           lastSaturation: 0.32).Should().BeTrue();
        }

        [Test]
        public void should_not_flag_correct_order_by_saturation()
        {
            // Correctly ordered: colorful cover FIRST, near-white back matter LAST.
            PageOrderNormalizer.IsReversed(null,
                                           firstBrightness: 0.55,
                                           lastBrightness: 0.93,
                                           firstSaturation: 0.35,
                                           lastSaturation: 0.01).Should().BeFalse();
        }

        [Test]
        public void should_not_flag_fully_grayscale_book_by_saturation()
        {
            PageOrderNormalizer.IsReversed(null,
                                           firstBrightness: 0.88,
                                           lastBrightness: 0.90,
                                           firstSaturation: 0.01,
                                           lastSaturation: 0.02).Should().BeFalse();
        }

        [Test]
        public void unknown_saturation_falls_back_to_brightness_rule()
        {
            PageOrderNormalizer.IsReversed(null,
                                           firstBrightness: 0.96,
                                           lastBrightness: 0.62,
                                           firstSaturation: -1,
                                           lastSaturation: -1).Should().BeTrue();
        }

        [Test]
        [TestCase(0.754, 0.650)] // Blue Box v7 — grayish colophon, missed by the old >0.85 gate
        [TestCase(0.787, 0.658)] // Blue Box v12
        [TestCase(0.821, 0.571)] // Blue Box v13
        [TestCase(0.891, 0.742)] // Blue Box v11
        [TestCase(0.769, 0.727)] // Blue Box v18 — small first-minus-last gap, missed by the 0.06 gate
        public void should_detect_reversed_when_page_one_is_only_grayish(double first, double last)
        {
            PageOrderNormalizer.IsReversed(null, firstBrightness: first, lastBrightness: last).Should().BeTrue();
        }

        [Test]
        public void should_not_flag_light_cover_with_white_colophon_last()
        {
            // Correctly ordered book with a LIGHT cover: last page is the white colophon (>= 0.80),
            // so it must not be flagged even though page 1 is fairly bright.
            PageOrderNormalizer.IsReversed(null, firstBrightness: 0.82, lastBrightness: 0.93).Should().BeFalse();
        }

        [Test]
        public void should_not_flag_normal_order_by_brightness()
        {
            // Correctly ordered: colorful cover first (dark), white back-matter last -> not reversed.
            PageOrderNormalizer.IsReversed(null, firstBrightness: 0.62, lastBrightness: 0.96).Should().BeFalse();
        }

        [Test]
        public void should_not_flag_when_both_pages_are_white()
        {
            // Two near-white pages (e.g. text volume) -> difference too small, no flip.
            PageOrderNormalizer.IsReversed(null, firstBrightness: 0.95, lastBrightness: 0.93).Should().BeFalse();
        }

        [Test]
        public void should_not_flag_when_brightness_unknown()
        {
            PageOrderNormalizer.IsReversed(null, firstBrightness: -1, lastBrightness: -1).Should().BeFalse();
        }
    }
}
