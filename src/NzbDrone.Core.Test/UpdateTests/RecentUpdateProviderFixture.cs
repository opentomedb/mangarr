using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class RecentUpdateProviderFixture : CoreTest<RecentUpdateProvider>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAppFolderInfo>()
                .Setup(x => x.StartUpFolder)
                .Returns(@"/app/bin");

            Mocker.GetMock<IConfigFileProvider>()
                .Setup(x => x.Branch)
                .Returns("mangarr-main");
        }

        [Test]
        public void should_surface_the_baked_changelog_as_the_installed_build()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists("/app/bin/changelog.json"))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.ReadAllText("/app/bin/changelog.json"))
                .Returns(@"{ ""generated"": ""2026-07-17T20:00:00Z"", ""commits"": [
                    { ""hash"": ""abc1234"", ""date"": ""2026-07-17T15:00:00-05:00"", ""subject"": ""fix(parser): thing"" },
                    { ""hash"": ""def5678"", ""date"": ""2026-07-17T14:00:00-05:00"", ""subject"": ""feat(ui): other"" }
                ] }");

            var packages = Subject.GetRecentUpdatePackages();

            packages.Should().HaveCount(1);
            packages[0].Version.Should().Be(BuildInfo.Version);
            packages[0].Branch.Should().Be("mangarr-main");
            packages[0].Changes.Fixed.Should().HaveCount(1);
            packages[0].Changes.New.Should().HaveCount(1);
        }

        [Test]
        public void should_be_empty_when_no_changelog_is_baked()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists("/app/bin/changelog.json"))
                .Returns(false);

            Subject.GetRecentUpdatePackages().Should().BeEmpty();
        }
    }
}
