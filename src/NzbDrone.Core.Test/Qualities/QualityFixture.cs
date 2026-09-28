using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Qualities
{
    [TestFixture]
    public class QualityFixture : CoreTest
    {
        public static object[] FromIntCases =
                {
                        new object[] { 0, Quality.Unknown },
                        new object[] { 1, Quality.PDF },
                        new object[] { 2, Quality.CBZ },
                        new object[] { 3, Quality.CBR },
                        new object[] { 4, Quality.ZIP },
                        new object[] { 8, Quality.EbookPdf },
                        new object[] { 10, Quality.MP3 },
                        new object[] { 11, Quality.FLAC },
                };

        public static object[] ToIntCases =
                {
                        new object[] { Quality.Unknown, 0 },
                        new object[] { Quality.PDF, 1 },
                        new object[] { Quality.CBZ, 2 },
                        new object[] { Quality.CBR, 3 },
                        new object[] { Quality.ZIP, 4 },
                        new object[] { Quality.EbookPdf, 8 },
                        new object[] { Quality.MP3, 10 },
                        new object[] { Quality.FLAC, 11 },
                };

        [Test]
        [TestCaseSource(nameof(FromIntCases))]
        public void should_be_able_to_convert_int_to_qualityTypes(int source, Quality expected)
        {
            var quality = (Quality)source;
            quality.Should().Be(expected);
        }

        [Test]
        [TestCaseSource(nameof(ToIntCases))]
        public void should_be_able_to_convert_qualityTypes_to_int(Quality source, int expected)
        {
            var i = (int)source;
            i.Should().Be(expected);
        }

        public static List<QualityProfileQualityItem> GetDefaultQualities(params Quality[] allowed)
        {
            var qualities = new List<Quality>
            {
                Quality.Unknown,
                Quality.CBZ,
                Quality.CBR,
                Quality.ZIP,
                Quality.MP3,
                Quality.FLAC
            };

            if (allowed.Length == 0)
            {
                allowed = qualities.ToArray();
            }

            var items = qualities
                .Except(allowed)
                .Concat(allowed)
                .Select(v => new QualityProfileQualityItem { Quality = v, Allowed = allowed.Contains(v) }).ToList();

            return items;
        }

        // LN PDF (2026-09-22): Ebook PDF sits directly below AZW3 -- a last-resort light-novel ebook,
        // upgraded away by AZW3 or EPUB. The definitions' insertion order is the order
        // QualityProfileService.GetDefaultProfile builds a profile's items in.
        [Test]
        public void ebook_pdf_is_weighted_directly_below_azw3()
        {
            var weights = Quality.DefaultQualityDefinitions.ToDictionary(d => d.Quality, d => d.Weight);

            weights[Quality.EbookPdf].Should().Be(22);
            weights[Quality.EbookPdf].Should().BeGreaterThan(weights[Quality.CBZ]);
            weights[Quality.EbookPdf].Should().BeLessThan(weights[Quality.AZW3]);
            weights.Values.Should().OnlyHaveUniqueItems();

            var order = Quality.DefaultQualityDefinitions.Select(d => d.Quality).ToList();
            order.IndexOf(Quality.EbookPdf).Should().Be(order.IndexOf(Quality.AZW3) - 1);

            Quality.FindById(8).Should().Be(Quality.EbookPdf);
            Quality.EbookPdf.Name.Should().Be("Ebook PDF");
        }
    }
}
