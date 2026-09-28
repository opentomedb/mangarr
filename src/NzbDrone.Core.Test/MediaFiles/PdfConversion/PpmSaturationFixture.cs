using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.PdfConversion;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.PdfConversion
{
    // MeanSaturation parses a binary PPM (P6) and returns the mean per-pixel saturation
    // ((max-min)/max) in 0..1 — the color counterpart of MeanBrightness. Covers are saturated;
    // endpapers, colophons and B&W interiors are near zero.
    [TestFixture]
    public class PpmSaturationFixture : CoreTest
    {
        private static byte[] Ppm(int width, int height, int maxval, byte[] rgb)
        {
            var header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n{maxval}\n");
            var bytes = new List<byte>(header);
            bytes.AddRange(rgb);
            return bytes.ToArray();
        }

        [Test]
        public void pure_red_page_is_fully_saturated()
        {
            var rgb = new byte[] { 255, 0, 0, 255, 0, 0 };
            PopplerPdfImageExtractor.MeanSaturation(Ppm(2, 1, 255, rgb)).Should().BeApproximately(1.0, 0.01);
        }

        [Test]
        public void gray_page_has_zero_saturation()
        {
            var rgb = new byte[] { 128, 128, 128, 128, 128, 128 };
            PopplerPdfImageExtractor.MeanSaturation(Ppm(2, 1, 255, rgb)).Should().BeApproximately(0.0, 0.01);
        }

        [Test]
        public void mixed_page_averages_pixel_saturation()
        {
            // one fully saturated red pixel + one gray pixel -> mean 0.5
            var rgb = new byte[] { 255, 0, 0, 128, 128, 128 };
            PopplerPdfImageExtractor.MeanSaturation(Ppm(2, 1, 255, rgb)).Should().BeApproximately(0.5, 0.01);
        }

        [Test]
        public void black_pixels_count_as_zero_saturation()
        {
            var rgb = new byte[] { 0, 0, 0, 0, 0, 0 };
            PopplerPdfImageExtractor.MeanSaturation(Ppm(2, 1, 255, rgb)).Should().BeApproximately(0.0, 0.01);
        }

        [Test]
        public void non_ppm_header_returns_minus_one()
        {
            var pgm = Encoding.ASCII.GetBytes("P5\n1 1\n255\nÿ");
            PopplerPdfImageExtractor.MeanSaturation(pgm).Should().Be(-1);
        }
    }
}
