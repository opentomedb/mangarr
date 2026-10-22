using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaFiles.Chapters;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.Chapters
{
    [TestFixture]
    public class ChapterWorkFolderFixture : CoreTest<ChapterWorkFolder>
    {
        private string _appData;
        private string _root;

        [SetUp]
        public void Setup()
        {
            _appData = Path.Combine(Path.GetTempPath(), "mangarr-appdata-" + Path.GetRandomFileName());
            _root = Path.Combine(_appData, "chapter-work");
            Directory.CreateDirectory(_appData);

            Mocker.GetMock<IAppFolderInfo>().SetupGet(s => s.AppDataFolder).Returns(_appData);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns<string>(Directory.Exists);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetDirectories(It.IsAny<string>())).Returns<string>(Directory.GetDirectories);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>())).Callback<string, bool>((p, r) => Directory.Delete(p, r));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_appData))
            {
                Directory.Delete(_appData, true);
            }
        }

        [Test]
        public void a_download_gets_a_folder_of_its_own_under_app_data()
        {
            Subject.For("SABnzbd_nzo_abc").Should().Be(Path.Combine(_root, "SABnzbd_nzo_abc"));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase(" ")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("a/../..")]
        [TestCase("../..")]
        [TestCase("a\\..\\..")]
        public void any_download_id_gets_a_folder_strictly_inside_the_work_root(string downloadId)
        {
            var folder = Path.GetFullPath(Subject.For(downloadId));

            Path.GetDirectoryName(folder).Should().Be(Path.GetFullPath(_root));
            Path.GetFileName(folder).Should().NotBeNullOrWhiteSpace().And.NotBe(".").And.NotBe("..");
        }

        [Test]
        public void a_work_folder_is_removed()
        {
            var folder = Subject.For("abc123");
            Directory.CreateDirectory(folder);

            Subject.Remove(folder);

            Directory.Exists(folder).Should().BeFalse();
        }

        [Test]
        public void the_root_or_anything_outside_it_is_never_deleted()
        {
            var outside = Path.Combine(_appData, "outside");
            Directory.CreateDirectory(outside);
            Directory.CreateDirectory(Path.Combine(_root, "abc123"));

            Subject.Remove(_root);
            Subject.Remove(_root + Path.DirectorySeparatorChar);
            Subject.Remove(Path.Combine(_root, "."));
            Subject.Remove(Path.Combine(_root, ".."));
            Subject.Remove(Path.Combine(_root, "abc123", "..", ".."));
            Subject.Remove(_appData);
            Subject.Remove(outside);
            Subject.Remove(string.Empty);

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Directory.Exists(outside).Should().BeTrue();
            Directory.Exists(Path.Combine(_root, "abc123")).Should().BeTrue();

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void leftover_work_folders_are_removed_at_startup()
        {
            var orphan = Path.Combine(_root, "abc123");
            Directory.CreateDirectory(orphan);
            File.WriteAllText(Path.Combine(orphan, "Sample Series Vol. 09 (Built from chapters 72–81).cbz.mangarr-tmp"), "x");

            Subject.Handle(new ApplicationStartedEvent());

            Directory.Exists(orphan).Should().BeFalse();
            Directory.Exists(_root).Should().BeTrue();
        }

        [Test]
        public void no_work_root_is_nothing_to_do()
        {
            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }
    }
}
