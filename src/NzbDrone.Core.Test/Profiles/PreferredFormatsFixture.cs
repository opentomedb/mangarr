using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles
{
    // The set MediaManagementConfigController validates a preferred light-novel format save
    // against, and the calibre OutputFormat each one maps to.
    [TestFixture]
    public class PreferredFormatsFixture : CoreTest
    {
        [TestCase("epub")]
        [TestCase("EPUB")]
        [TestCase("azw3")]
        [TestCase("AZW3")]
        [TestCase("kepub")]
        [TestCase("KEPUB")]
        [TestCase(null)]
        public void known_light_novel_formats_are_accepted(string format)
        {
            PreferredFormats.IsKnownLightNovelFormat(format).Should().BeTrue();
        }

        [TestCase("pdf")]
        [TestCase("mobi")]
        [TestCase("any")]
        [TestCase("")]
        public void any_other_light_novel_format_is_rejected(string format)
        {
            PreferredFormats.IsKnownLightNovelFormat(format).Should().BeFalse();
        }

        [TestCase("epub")]
        [TestCase("EPUB")]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void the_light_novel_output_format_is_null_for_epub_or_unset(string format)
        {
            PreferredFormats.LightNovelOutputFormat(format).Should().BeNull();
        }

        // ForConfig() is never run through the IsKnownLightNovelFormat validator (a stale stored
        // value or a direct DB edit reaches it unchecked), so an unknown value must map to null --
        // never pass through to calibre as an arbitrary OutputFormat.
        [TestCase("pdf")]
        [TestCase("mobi")]
        [TestCase("any")]
        public void an_unknown_light_novel_output_format_is_null(string format)
        {
            PreferredFormats.LightNovelOutputFormat(format).Should().BeNull();
        }

        [TestCase("azw3", "AZW3")]
        [TestCase("AZW3", "AZW3")]
        [TestCase("kepub", "KEPUB")]
        [TestCase("KEPUB", "KEPUB")]
        public void the_light_novel_output_format_maps_to_its_calibre_format(string format, string expected)
        {
            PreferredFormats.LightNovelOutputFormat(format).Should().Be(expected);
        }
    }
}
