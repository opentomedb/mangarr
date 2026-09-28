using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Extras.Metadata.ComicInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Extras.Metadata.ComicInfo
{
    [TestFixture]
    public class ComicInfoBuilderFixture : CoreTest
    {
        private ComicInfoFields Full()
        {
            return new ComicInfoFields
            {
                Series = "Chainsaw Man",
                Title = "Chainsaw Man Vol. 16",
                Number = "16",
                Summary = "Denji vs the future.",
                Year = 2024,
                Month = 5,
                Day = 7,
                PageCount = 192,
                Publisher = "VIZ Media",
                LanguageIso = "en",
                Count = null
            };
        }

        [Test]
        public void should_write_all_owned_fields_into_a_fresh_document()
        {
            var xml = ComicInfoBuilder.Merge(null, Full());

            xml.Should().Contain("<Series>Chainsaw Man</Series>");
            xml.Should().Contain("<Number>16</Number>");
            xml.Should().Contain("<Summary>Denji vs the future.</Summary>");
            xml.Should().Contain("<Year>2024</Year>");
            xml.Should().Contain("<Month>5</Month>");
            xml.Should().Contain("<Day>7</Day>");
            xml.Should().Contain("<PageCount>192</PageCount>");
            xml.Should().Contain("<Publisher>VIZ Media</Publisher>");
            xml.Should().Contain("<LanguageISO>en</LanguageISO>");
            xml.Should().StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        }

        [Test]
        public void should_preserve_the_reading_direction_written_by_another_tool()
        {
            // mangadex-meta writes a minimal ComicInfo including the <Manga> reading direction.
            var existing =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<ComicInfo><Title>Old</Title><Series>Chainsaw Man</Series>" +
                "<Number>16</Number><Volume>16</Volume><Manga>YesAndRightToLeft</Manga>" +
                "<LanguageISO>en</LanguageISO></ComicInfo>";

            var xml = ComicInfoBuilder.Merge(existing, Full());

            xml.Should().Contain("<Manga>YesAndRightToLeft</Manga>"); // foreign field survives
            xml.Should().Contain("<Volume>16</Volume>");              // unknown field survives
            xml.Should().Contain("<Summary>Denji vs the future.</Summary>"); // our field added
            xml.Should().Contain("<Title>Chainsaw Man Vol. 16</Title>");     // our field overwrote "Old"
            xml.Should().NotContain("<Title>Old</Title>");
        }

        [Test]
        public void should_not_clear_existing_fields_we_have_no_value_for()
        {
            var existing =
                "<ComicInfo><Series>Chainsaw Man</Series><Publisher>VIZ Media</Publisher>" +
                "<Manga>YesAndRightToLeft</Manga></ComicInfo>";

            // No publisher supplied this time.
            var fields = Full();
            fields.Publisher = null;

            var xml = ComicInfoBuilder.Merge(existing, fields);

            xml.Should().Contain("<Publisher>VIZ Media</Publisher>"); // not erased
        }

        [Test]
        public void should_omit_zero_pagecount_and_count()
        {
            var fields = Full();
            fields.PageCount = 0;
            fields.Count = 0;

            var xml = ComicInfoBuilder.Merge(null, fields);

            xml.Should().NotContain("<PageCount>");
            xml.Should().NotContain("<Count>");
        }

        [Test]
        public void should_write_count_only_for_ended_series()
        {
            var fields = Full();
            fields.Count = 23; // ended: 23 total volumes

            var xml = ComicInfoBuilder.Merge(null, fields);

            xml.Should().Contain("<Count>23</Count>");
        }

        [Test]
        public void should_treat_unparseable_existing_xml_as_fresh()
        {
            var xml = ComicInfoBuilder.Merge("this is not xml <<<", Full());

            xml.Should().Contain("<Series>Chainsaw Man</Series>");
            xml.Should().Contain("<ComicInfo");
        }

        [Test]
        public void would_change_is_false_when_document_already_current()
        {
            var once = ComicInfoBuilder.Merge(null, Full());

            ComicInfoBuilder.WouldChange(once, Full()).Should().BeFalse();
        }

        [Test]
        public void would_change_is_true_when_a_field_differs()
        {
            var once = ComicInfoBuilder.Merge(null, Full());

            var changed = Full();
            changed.Summary = "A different summary.";

            ComicInfoBuilder.WouldChange(once, changed).Should().BeTrue();
        }
    }
}
