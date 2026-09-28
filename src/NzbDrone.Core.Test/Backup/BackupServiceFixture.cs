using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Backup
{
    // Beta readiness (2026-09-28, S5): the backup zip carries <appdata>/metadata/overrides.json (the
    // user's metadata pins) beside config.xml and the database, and a restore puts it back.
    [TestFixture]
    public class BackupServiceFixture : CoreTest<BackupService>
    {
        private string _appData;
        private string _overrides;
        private string _backupTemp;
        private string _restoreTemp;

        [SetUp]
        public void Setup()
        {
            _appData = "/config".AsOsAgnostic();
            _overrides = Path.Combine(_appData, "metadata", "overrides.json");
            _backupTemp = Path.Combine("/tmp".AsOsAgnostic(), "mangarr_backup");
            _restoreTemp = Path.Combine("/tmp".AsOsAgnostic(), "mangarr_backup_restore");

            Mocker.GetMock<IAppFolderInfo>().SetupGet(s => s.AppDataFolder).Returns(_appData);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(s => s.TempFolder).Returns("/tmp".AsOsAgnostic());
            Mocker.GetMock<IConfigService>().SetupGet(s => s.BackupFolder).Returns("Backups");
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderWritable(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(It.IsAny<string>(), It.IsAny<bool>())).Returns(new string[0]);
            Mocker.GetMock<IMainDatabase>().SetupGet(s => s.DatabaseType).Returns(DatabaseType.SQLite);
        }

        private void GivenOverrides(bool exists)
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(_overrides)).Returns(exists);
        }

        private void GivenBackupHolds(params string[] names)
        {
            var files = new string[names.Length];

            for (var i = 0; i < names.Length; i++)
            {
                files[i] = Path.Combine(_restoreTemp, names[i]);
            }

            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(_restoreTemp, false)).Returns(files);
        }

        [Test]
        public void a_backup_copies_the_metadata_pins_into_the_zip()
        {
            GivenOverrides(true);

            Subject.Backup(BackupType.Manual);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(s => s.TransferFile(_overrides, Path.Combine(_backupTemp, "overrides.json"), TransferMode.Copy, false), Times.Once());
        }

        [Test]
        public void a_backup_without_pins_copies_only_config()
        {
            GivenOverrides(false);

            Subject.Backup(BackupType.Manual);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(s => s.TransferFile(_overrides, It.IsAny<string>(), It.IsAny<TransferMode>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskTransferService>()
                  .Verify(s => s.TransferFile(It.IsAny<string>(), It.IsAny<string>(), TransferMode.Copy, false), Times.Once());
        }

        [Test]
        public void a_restore_puts_the_pins_back()
        {
            GivenBackupHolds("config.xml", "readarr.db", "overrides.json");

            Subject.Restore("/backups/mangarr_backup_v10.0.0.760_2026.09.28_12.00.00.zip".AsOsAgnostic());

            Mocker.GetMock<IDiskProvider>().Verify(s => s.EnsureFolder(Path.Combine(_appData, "metadata")), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.MoveFile(Path.Combine(_restoreTemp, "overrides.json"), _overrides, true), Times.Once());
        }

        [Test]
        public void a_restore_keeps_todays_pins_as_pre_restore_first()
        {
            GivenBackupHolds("config.xml", "readarr.db", "overrides.json");
            GivenOverrides(true);

            Subject.Restore("/backups/mangarr_backup_v10.0.0.760_2026.09.28_12.00.00.zip".AsOsAgnostic());

            Mocker.GetMock<IDiskProvider>().Verify(s => s.CopyFile(_overrides, _overrides + ".pre-restore", true), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(s => s.MoveFile(Path.Combine(_restoreTemp, "overrides.json"), _overrides, true), Times.Once());
        }

        [Test]
        public void a_restore_without_current_pins_copies_nothing_aside()
        {
            GivenBackupHolds("config.xml", "readarr.db", "overrides.json");
            GivenOverrides(false);

            Subject.Restore("/backups/mangarr_backup_v10.0.0.760_2026.09.28_12.00.00.zip".AsOsAgnostic());

            Mocker.GetMock<IDiskProvider>().Verify(s => s.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void a_restore_of_an_older_backup_leaves_todays_pins_alone()
        {
            GivenBackupHolds("config.xml", "readarr.db");

            Subject.Restore("/backups/mangarr_backup_v10.0.0.700_2026.09.01_12.00.00.zip".AsOsAgnostic());

            Mocker.GetMock<IDiskProvider>().Verify(s => s.MoveFile(It.IsAny<string>(), _overrides, It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void a_backup_without_a_database_restores_no_pins()
        {
            GivenBackupHolds("overrides.json");

            Assert.Throws<RestoreBackupFailedException>(() => Subject.Restore("/backups/mangarr_backup_v10.0.0.760_2026.09.28_12.00.00.zip".AsOsAgnostic()));

            Mocker.GetMock<IDiskProvider>().Verify(s => s.MoveFile(It.IsAny<string>(), _overrides, It.IsAny<bool>()), Times.Never());
        }
    }
}
