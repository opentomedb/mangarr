using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class RootFolderWritableCheckFixture : CoreTest<RootFolderWritableCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Not writable: {0}");

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.All())
                  .Returns(new List<RootFolder> { new RootFolder { Path = "/manga" }, new RootFolder { Path = "/lightnovels" } });

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderWritable(It.IsAny<string>())).Returns(true);
        }

        [Test]
        public void ok_when_every_root_folder_is_writable()
        {
            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void error_naming_the_root_folder_that_is_not_writable()
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderWritable("/lightnovels")).Returns(false);

            var result = Subject.Check();

            result.ShouldBeError("Not writable: /lightnovels");
        }

        [Test]
        public void a_missing_root_folder_is_left_to_the_missing_folder_check()
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists("/lightnovels")).Returns(false);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderWritable("/lightnovels")).Returns(false);

            Subject.Check().ShouldBeOk();
        }
    }
}
