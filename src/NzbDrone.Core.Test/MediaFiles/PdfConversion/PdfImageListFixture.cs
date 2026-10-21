using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.PdfConversion;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.PdfConversion
{
    // `pdfimages -list` as poppler prints it (Berserk Vol. 43, 2026-10-19): two header lines, then one
    // row per image; "object ID" is two columns, so x-ppi is the 13th.
    [TestFixture]
    public class PdfImageListFixture : CoreTest
    {
        private static readonly string[] Listing =
        {
            "page   num  type   width height color comp bpc  enc interp  object ID x-ppi y-ppi size ratio",
            "--------------------------------------------------------------------------------------------",
            "   1     0 image    1804  2260  icc     3   8  jpeg   no        12  0   352   352  612K 5.1%",
            "   2     3 smask    2845  1015  gray    1   8  jpeg   no        20  0  1199  1199 88.6K 3.1%",
            "  16    17 stencil  6286  9035  -       1   1  image  no       732  0  1200  1200 3289K  47%",
            ""
        };

        [Test]
        public void reads_page_type_color_bpc_and_x_ppi()
        {
            var images = PopplerPdfImageExtractor.ParseImageList(Listing);

            images.Select(i => (i.Page, i.Type, i.Color, i.Bpc, i.XPpi)).Should().Equal(
                (1, "image", "icc", 8, 352),
                (2, "smask", "gray", 8, 1199),
                (16, "stencil", "-", 1, 1200));
        }

        [Test]
        public void skips_lines_that_are_not_image_rows()
        {
            PopplerPdfImageExtractor.ParseImageList(new[] { "Syntax Warning: something", "   x y" }).Should().BeEmpty();
        }
    }
}
