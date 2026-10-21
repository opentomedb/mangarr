using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.PdfConversion;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.PdfConversion
{
    [TestFixture]
    public class PageDifferenceFixture : CoreTest
    {
        private static byte[] Pgm(int width, int height, byte[] pixels)
        {
            var bytes = new List<byte>(Encoding.ASCII.GetBytes($"P5\n{width} {height}\n255\n"));
            bytes.AddRange(pixels);
            return bytes.ToArray();
        }

        [Test]
        public void parses_a_pgm_into_its_size_and_pixels()
        {
            var parsed = PopplerPdfImageExtractor.ParsePgm(Pgm(2, 1, new byte[] { 10, 200 }));

            parsed.Width.Should().Be(2);
            parsed.Height.Should().Be(1);
            parsed.Pixels.Should().Equal(10, 200);
        }

        [Test]
        public void a_non_pgm_parses_to_null()
        {
            PopplerPdfImageExtractor.ParsePgm(Encoding.ASCII.GetBytes("P6\n1 1\n255\nabc")).Should().BeNull();
        }

        [Test]
        public void the_difference_is_the_mean_absolute_pixel_difference()
        {
            PopplerPdfImageExtractor.MeanAbsoluteDifference(new byte[] { 0, 100, 255 }, new byte[] { 10, 100, 245 }).Should().BeApproximately(20.0 / 3, 0.001);
        }
    }
}
