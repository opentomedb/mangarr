using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.Localization;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    // UI translations v1 (2026-09-25): the ten messages move to en.json keys. Written against the old
    // literals first, through a real English LocalizationService, so every branch pins its exact English.
    // The three "not a valid path" branches need a relative path: on Linux OsPath.IsValid means "starts with /".
    [TestFixture]
    public class CalibreRootFolderCheckFixture : CoreTest<CalibreRootFolderCheck>
    {
        private const string Relative = "calibre/Series/Vol 1/v01.epub";
        private const string Absolute = "/calibre/Series/Vol 1/v01.epub";

        private void Given(string host, bool docker, string file, bool folderExists = true, bool fileExists = true, string rootPath = "/books")
        {
            Mocker.SetConstant<ILocalizationService>(EnglishLocalization.Create());

            var settings = new CalibreSettings { Host = host };

            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder>
            {
                new RootFolder { Name = "Calibre Library", Path = rootPath, IsCalibreLibrary = true, CalibreSettings = settings }
            });
            Mocker.GetMock<ICalibreProxy>().Setup(s => s.GetAllBookFilePaths(settings)).Returns(new List<string> { file });
            Mocker.GetMock<IOsInfo>().SetupGet(s => s.Name).Returns("Linux");
            Mocker.GetMock<IOsInfo>().SetupGet(s => s.IsDocker).Returns(docker);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists("/calibre")).Returns(folderExists);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(Absolute)).Returns(fileExists);
        }

        private void ShouldSay(string message)
        {
            var result = Subject.Check();

            result.Type.Should().Be(HealthCheckResult.Error);
            result.Message.Should().Be(message);
        }

        [Test]
        public void remote_calibre_reporting_an_invalid_path()
        {
            Given("calibre.lan", false, Relative);
            ShouldSay("Remote Calibre for root folder Calibre Library reports files in calibre but this is not a valid Linux path.  Review your remote path mappings and root folder settings.");
        }

        [Test]
        public void docker_calibre_reporting_an_invalid_path()
        {
            Given("localhost", true, Relative);
            ShouldSay("You are using docker; Calibre for root folder Calibre Library reports files in calibre but this is not a valid Linux path.  Review your remote path mappings and download client settings.");
        }

        [Test]
        public void local_calibre_reporting_an_invalid_path()
        {
            Given("127.0.0.1", false, Relative);
            ShouldSay("Local Calibre server for root folder Calibre Library reports files in calibre but this is not a valid Linux path.  Review your download client settings.");
        }

        [Test]
        public void docker_folder_missing()
        {
            Given("calibre.lan", true, Absolute, folderExists: false);
            ShouldSay("You are using docker; Calibre server for root folder Calibre Library places downloads in /calibre but this directory does not appear to exist inside the container.  Review your remote path mappings and container volume settings.");
        }

        [Test]
        public void remote_folder_missing()
        {
            Given("calibre.lan", false, Absolute, folderExists: false);
            ShouldSay("Remote Calibre server for root folder Calibre Library places downloads in /calibre but this directory does not appear to exist.  Likely missing or incorrect remote path mapping.");
        }

        [Test]
        public void local_folder_not_visible()
        {
            Given("localhost", false, Absolute, folderExists: false);
            ShouldSay("Calibre server for root folder Calibre Library places downloads in /calibre but Mangarr cannot see this directory.  You may need to adjust the folder's permissions or add a remote path mapping if Calibre is running in docker");
        }

        [Test]
        public void docker_file_missing()
        {
            Given("calibre.lan", true, Absolute, fileExists: false);
            ShouldSay("You are using docker; Calibre server for root folder Calibre Library listed file /calibre/Series/Vol 1/v01.epub but this file does not appear to exist inside the container.  Review permissions for /calibre and PUID/PGID container settings");
        }

        [Test]
        public void remote_file_missing()
        {
            Given("calibre.lan", false, Absolute, fileExists: false);
            ShouldSay("Remote Calibre server for root folder Calibre Library listed file /calibre/Series/Vol 1/v01.epub but this file does not appear to exist.  Review permissions for /calibre");
        }

        [Test]
        public void local_file_not_visible()
        {
            Given("localhost", false, Absolute, fileExists: false);
            ShouldSay("Calibre server for root folder Calibre Library listed file /calibre/Series/Vol 1/v01.epub but Mangarr cannot see this file.  Review permissions for /calibre");
        }

        [Test]
        public void library_is_not_the_root_folder()
        {
            Given("localhost", false, Absolute);
            ShouldSay("Calibre for root folder Calibre Library reports files in /calibre but this is not the same as the root folder path /books you chose.  You may need to edit any remote path mapping or delete the root folder and re-create with the correct path");
        }

        [Test]
        public void matching_library_is_ok()
        {
            Given("localhost", false, Absolute, rootPath: "/calibre");
            Subject.Check().ShouldBeOk();
        }
    }
}
