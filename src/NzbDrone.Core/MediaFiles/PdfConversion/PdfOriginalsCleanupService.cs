using System;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.PdfConversion
{
    // Retention for the originals of converted PDFs. Off (0 days) by default: originals are kept forever.
    // When on, it deletes only .pdf files older than the set number of days (by file date) inside the
    // current originals folder; nothing else in that folder, and nothing outside it, is ever touched.
    public class PdfOriginalsCleanupService : IExecute<CleanUpPdfOriginalsCommand>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public PdfOriginalsCleanupService(IDiskProvider diskProvider, IAppFolderInfo appFolderInfo, IConfigService configService, Logger logger)
        {
            _diskProvider = diskProvider;
            _appFolderInfo = appFolderInfo;
            _configService = configService;
            _logger = logger;
        }

        public void Execute(CleanUpPdfOriginalsCommand message)
        {
            var days = _configService.PdfOriginalsCleanupDays;

            if (days <= 0)
            {
                _logger.Debug("PDF originals cleanup is off (0 days)");
                return;
            }

            var root = PdfOriginalsLocation.Root(_configService, _appFolderInfo);

            if (!_diskProvider.FolderExists(root))
            {
                return;
            }

            if (!PdfOriginalsLocation.IsDefault(_configService) && !_diskProvider.FileExists(Path.Combine(root, PdfOriginalsLocation.MarkerFileName)))
            {
                _logger.Warn("{0} is not marked as a Mangarr PDF originals folder; nothing was deleted", root);
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-days);
            var removed = 0;

            foreach (var file in _diskProvider.GetFiles(root, true))
            {
                if (!file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (_diskProvider.FileGetLastWrite(file) > cutoff)
                    {
                        continue;
                    }

                    _diskProvider.DeleteFile(file);
                    removed++;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not clean up {0}", file);
                }
            }

            _diskProvider.RemoveEmptySubfolders(root);
            _logger.Info("PDF originals cleanup removed {0} file(s) older than {1} days from {2}", removed, days, root);
        }
    }
}
