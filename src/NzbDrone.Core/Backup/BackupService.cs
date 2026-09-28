using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.Manga;

namespace NzbDrone.Core.Backup
{
    public interface IBackupService
    {
        void Backup(BackupType backupType);
        List<Backup> GetBackups();
        void Restore(string backupFileName);
        string GetBackupFolder();
        string GetBackupFolder(BackupType backupType);
    }

    public class BackupService : IBackupService, IExecute<BackupCommand>
    {
        private readonly IMainDatabase _maindDb;
        private readonly IMakeDatabaseBackup _makeDatabaseBackup;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IDiskProvider _diskProvider;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IArchiveService _archiveService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        private string _backupTempFolder;

        public static readonly Regex BackupFileRegex = new Regex(@"(?:readarr|mangarr)_backup_(v[0-9.]+_)?[._0-9]+\.zip", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public BackupService(IMainDatabase maindDb,
                             IMakeDatabaseBackup makeDatabaseBackup,
                             IDiskTransferService diskTransferService,
                             IDiskProvider diskProvider,
                             IAppFolderInfo appFolderInfo,
                             IArchiveService archiveService,
                             IConfigService configService,
                             Logger logger)
        {
            _maindDb = maindDb;
            _makeDatabaseBackup = makeDatabaseBackup;
            _diskTransferService = diskTransferService;
            _diskProvider = diskProvider;
            _appFolderInfo = appFolderInfo;
            _archiveService = archiveService;
            _configService = configService;
            _logger = logger;

            _backupTempFolder = Path.Combine(_appFolderInfo.TempFolder, "mangarr_backup");
        }

        public void Backup(BackupType backupType)
        {
            _logger.ProgressInfo("Starting Backup");

            var backupFolder = GetBackupFolder(backupType);

            _diskProvider.EnsureFolder(_backupTempFolder);
            _diskProvider.EnsureFolder(backupFolder);

            if (!_diskProvider.FolderWritable(backupFolder))
            {
                throw new UnauthorizedAccessException($"Backup folder {backupFolder} is not writable");
            }

            var dateNow = DateTime.Now;
            var backupFilename = $"mangarr_backup_v{BuildInfo.Version}_{dateNow:yyyy.MM.dd_HH.mm.ss}.zip";
            var backupPath = Path.Combine(backupFolder, backupFilename);

            Cleanup();

            if (backupType != BackupType.Manual)
            {
                CleanupOldBackups(backupType);
            }

            BackupConfigFile();
            BackupDatabase();
            BackupMetadataOverrides();
            CreateVersionInfo(dateNow);

            _logger.ProgressDebug("Creating backup zip");

            // Delete journal file created during database backup
            _diskProvider.DeleteFile(Path.Combine(_backupTempFolder, "readarr.db-journal"));

            _archiveService.CreateZip(backupPath, _diskProvider.GetFiles(_backupTempFolder, false));

            Cleanup();

            _logger.ProgressDebug("Backup zip created");
        }

        public List<Backup> GetBackups()
        {
            var backups = new List<Backup>();

            foreach (var backupType in Enum.GetValues(typeof(BackupType)).Cast<BackupType>())
            {
                var folder = GetBackupFolder(backupType);

                if (_diskProvider.FolderExists(folder))
                {
                    backups.AddRange(GetBackupFiles(folder).Select(b => new Backup
                    {
                        Name = Path.GetFileName(b),
                        Type = backupType,
                        Size = _diskProvider.GetFileSize(b),
                        Time = _diskProvider.FileGetLastWrite(b)
                    }));
                }
            }

            return backups;
        }

        public void Restore(string backupFileName)
        {
            if (backupFileName.EndsWith(".zip"))
            {
                var restoredFile = false;
                string overridesFile = null;
                var temporaryPath = Path.Combine(_appFolderInfo.TempFolder, "mangarr_backup_restore");

                _archiveService.Extract(backupFileName, temporaryPath);

                foreach (var file in _diskProvider.GetFiles(temporaryPath, false))
                {
                    var fileName = Path.GetFileName(file);

                    if (fileName.Equals("Config.xml", StringComparison.InvariantCultureIgnoreCase))
                    {
                        _diskProvider.MoveFile(file, _appFolderInfo.GetConfigPath(), true);
                        restoredFile = true;
                    }

                    if (fileName.Equals("readarr.db", StringComparison.InvariantCultureIgnoreCase))
                    {
                        _diskProvider.MoveFile(file, _appFolderInfo.GetDatabaseRestore(), true);
                        restoredFile = true;
                    }

                    if (fileName.Equals(MetadataOverridesService.OverridesFileName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        overridesFile = file;
                    }
                }

                if (!restoredFile)
                {
                    throw new RestoreBackupFailedException(HttpStatusCode.NotFound, "Unable to restore database file from backup");
                }

                // Beta readiness (2026-09-28, S5): the metadata pins come back with the database -- after the
                // check above that the zip holds a database file. Only when the backup has them (an older backup
                // leaves today's pins alone); MetadataOverridesService reloads the file by its modified time.
                if (overridesFile != null)
                {
                    var overridesPath = MetadataOverridesService.OverridesPathIn(_appFolderInfo.AppDataFolder);

                    _diskProvider.EnsureFolder(Path.GetDirectoryName(overridesPath));

                    // Review M6: today's pins are kept beside it, so a restore of an older backup can't
                    // silently lose pins made since (hand-made, in no other copy).
                    if (_diskProvider.FileExists(overridesPath))
                    {
                        _diskProvider.CopyFile(overridesPath, overridesPath + ".pre-restore", true);
                    }

                    _diskProvider.MoveFile(overridesFile, overridesPath, true);
                }

                _diskProvider.DeleteFolder(temporaryPath, true);

                return;
            }

            _diskProvider.MoveFile(backupFileName, _appFolderInfo.GetDatabaseRestore(), true);
        }

        public string GetBackupFolder()
        {
            var backupFolder = _configService.BackupFolder;

            if (Path.IsPathRooted(backupFolder))
            {
                return backupFolder;
            }

            return Path.Combine(_appFolderInfo.GetAppDataPath(), backupFolder);
        }

        public string GetBackupFolder(BackupType backupType)
        {
            return Path.Combine(GetBackupFolder(), backupType.ToString().ToLower());
        }

        private void Cleanup()
        {
            if (_diskProvider.FolderExists(_backupTempFolder))
            {
                _diskProvider.EmptyFolder(_backupTempFolder);
            }
        }

        private void BackupDatabase()
        {
            if (_maindDb.DatabaseType == DatabaseType.SQLite)
            {
                _logger.ProgressDebug("Backing up database");

                _makeDatabaseBackup.BackupDatabase(_maindDb, _backupTempFolder);
            }
        }

        private void BackupConfigFile()
        {
            _logger.ProgressDebug("Backing up config.xml");

            var configFile = _appFolderInfo.GetConfigPath();
            var tempConfigFile = Path.Combine(_backupTempFolder, Path.GetFileName(configFile));

            _diskTransferService.TransferFile(configFile, tempConfigFile, TransferMode.Copy);
        }

        // Beta readiness (2026-09-28, S5): <appdata>/metadata/overrides.json holds the user's metadata pins
        // (dates, ISBNs, covers, writers) -- hand-made and in no other copy -- so it rides in the zip.
        private void BackupMetadataOverrides()
        {
            var overridesPath = MetadataOverridesService.OverridesPathIn(_appFolderInfo.AppDataFolder);

            if (!_diskProvider.FileExists(overridesPath))
            {
                return;
            }

            _logger.ProgressDebug("Backing up metadata pins");

            _diskTransferService.TransferFile(overridesPath, Path.Combine(_backupTempFolder, MetadataOverridesService.OverridesFileName), TransferMode.Copy);
        }

        private void CreateVersionInfo(DateTime dateNow)
        {
            var tempFile = Path.Combine(_backupTempFolder, "INFO");

            var builder = new StringBuilder();
            builder.AppendLine($"v{BuildInfo.Version}");
            builder.AppendLine($"{dateNow:yyyy-MM-dd HH:mm:ss}");

            _diskProvider.WriteAllText(tempFile, builder.ToString());
        }

        private void CleanupOldBackups(BackupType backupType)
        {
            var retention = _configService.BackupRetention;

            _logger.Debug("Cleaning up backup files older than {0} days", retention);
            var files = GetBackupFiles(GetBackupFolder(backupType));

            foreach (var file in files)
            {
                var lastWriteTime = _diskProvider.FileGetLastWrite(file);

                if (lastWriteTime.AddDays(retention) < DateTime.UtcNow)
                {
                    _logger.Debug("Deleting old backup file: {0}", file);
                    _diskProvider.DeleteFile(file);
                }
            }

            _logger.Debug("Finished cleaning up old backup files");
        }

        private IEnumerable<string> GetBackupFiles(string path)
        {
            var files = _diskProvider.GetFiles(path, false);

            return files.Where(f => BackupFileRegex.IsMatch(f));
        }

        public void Execute(BackupCommand message)
        {
            Backup(message.Type);
        }
    }
}
