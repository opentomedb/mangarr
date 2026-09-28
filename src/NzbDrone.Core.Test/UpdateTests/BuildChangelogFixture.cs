using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.UpdateTests
{
    // The Updates page shows "what's in this build": deploy.sh bakes recent commits into
    // changelog.json and BuildChangelog turns them into one UpdatePackage for the running
    // version — fix* subjects under Fixed, everything else under New.
    [TestFixture]
    public class BuildChangelogFixture : CoreTest
    {
        private const string ValidJson = @"{
            ""generated"": ""2026-07-17T20:00:00Z"",
            ""commits"": [
                { ""hash"": ""abc1234"", ""date"": ""2026-07-17T15:00:00-05:00"", ""subject"": ""fix(parser): bridge dotted publisher names"" },
                { ""hash"": ""def5678"", ""date"": ""2026-07-17T14:00:00-05:00"", ""subject"": ""feat(ui): flip page order action"" },
                { ""hash"": ""aaa9999"", ""date"": ""2026-07-17T13:00:00-05:00"", ""subject"": ""chore(brand): mangarr log names"" }
            ]
        }";

        [Test]
        public void parses_the_baked_changelog_into_one_package_for_the_running_build()
        {
            var version = new Version(10, 0, 0, 12345);

            var package = BuildChangelog.Parse(ValidJson, version, "mangarr-main");

            package.Should().NotBeNull();
            package.Version.Should().Be(version);
            package.Branch.Should().Be("mangarr-main");
            package.ReleaseDate.Should().Be(new DateTime(2026, 7, 17, 20, 0, 0, DateTimeKind.Utc));
            package.Hash.Should().Be("abc1234");
        }

        [Test]
        public void fix_subjects_go_under_fixed_and_the_rest_under_new()
        {
            var package = BuildChangelog.Parse(ValidJson, new Version(10, 0), "mangarr-main");

            package.Changes.Fixed.Should().Equal("fix(parser): bridge dotted publisher names");
            package.Changes.New.Should().Equal("feat(ui): flip page order action", "chore(brand): mangarr log names");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json at all")]
        [TestCase(@"{ ""generated"": ""2026-07-17T20:00:00Z"", ""commits"": [] }")]
        public void unusable_changelog_yields_null(string json)
        {
            BuildChangelog.Parse(json, new Version(10, 0), "mangarr-main").Should().BeNull();
        }
    }
}
