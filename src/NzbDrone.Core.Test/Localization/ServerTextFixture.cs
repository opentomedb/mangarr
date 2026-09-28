using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Localization;

namespace NzbDrone.Core.Test.Localization
{
    // Server messages (2026-09-26): the value every carrier keeps beside its English.
    [TestFixture]
    public class ServerTextFixture
    {
        private static string Readable(string skeleton)
        {
            return skeleton.Replace(ServerText.Marker, '•');
        }

        [Test]
        public void english_is_the_old_string_format_call()
        {
            new ServerText("Only {0} minutes old, minimum age is {1} minutes", 5, 10).English
                .Should().Be(string.Format("Only {0} minutes old, minimum age is {1} minutes", 5, 10));

            new ServerText("{0:0.0} GB of {1:N0}", 1.25, 1234567).English
                .Should().Be(string.Format("{0:0.0} GB of {1:N0}", 1.25, 1234567));
        }

        [TestCase("{0} is smaller than minimum allowed {1}", "• is smaller than minimum allowed •")]
        [TestCase("Size {1:N0} of {0,-5}", "Size • of •")]
        [TestCase("Path '{path}' does not exist", "Path '•' does not exist")]
        [TestCase("'{PropertyName}' must not be empty.", "'•' must not be empty.")]
        [TestCase("Literal {{braces}} and {0}", "Literal {braces} and •")]
        [TestCase("Not a hole { x } nor {}", "Not a hole { x } nor {}")]
        [TestCase("Sample", "Sample")]
        public void a_skeleton_reduces_every_placeholder_to_one_marker(string template, string skeleton)
        {
            Readable(ServerText.SkeletonOf(template)).Should().Be(skeleton);
        }

        [Test]
        public void a_positional_call_site_and_its_named_en_template_share_a_skeleton()
        {
            ServerText.SkeletonOf("{1} is older than {0:N0} days").Should().Be(ServerText.SkeletonOf("{title} is older than {age} days"));
        }

        // Fix round 1 (2026-09-26): no arguments means no formatting, matching the non-formatting
        // Decision.Reject(string)/Rejection(string) overloads.
        [Test]
        public void with_no_args_the_template_is_kept_verbatim()
        {
            new ServerText("a {{literal}}").English.Should().Be("a {{literal}}");
        }

        [Test]
        public void with_no_args_a_stray_brace_does_not_throw()
        {
            new ServerText("Path contains { or } chars").English.Should().Be("Path contains { or } chars");
        }

        [Test]
        public void parse_reads_each_hole_in_order()
        {
            var holes = ServerText.Parse("{1,-5:N0} then {name} then {0}").Holes;

            holes.Select(h => h.Index).Should().Equal(1, null, 0);
            holes.Select(h => h.Name).Should().Equal(null, "name", null);
            holes[0].FormatSpec.Should().Be("{0,-5:N0}");
            holes[1].FormatSpec.Should().Be("{0}");
        }

        [Test]
        public void arguments_are_kept_as_values_or_as_the_text_they_rendered()
        {
            var nested = new ServerText("Sample");
            var text = new ServerText("{0} {1} {2} {3}", 5, "path", new Uri("http://host/"), nested);

            text.Args[0].Should().Be(5);
            text.Args[1].Should().Be("path");
            text.Args[2].Should().Be("http://host/");
            text.Args[3].Should().BeSameAs(nested);
        }

        [Test]
        public void with_english_keeps_the_english_a_logger_rendered()
        {
            var text = ServerText.WithEnglish("Moving 3 series", "Moving {Count} series", 3);

            text.English.Should().Be("Moving 3 series");
            Readable(text.Skeleton).Should().Be("Moving • series");
        }
    }
}
