using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles.Chapters
{
    public interface IChapterWorkFolder
    {
        string For(string downloadId);
        void Remove(string folder);
    }

    // Volumes built from chapter packs (2026-10-21): where volumes are built, one sub-folder per download id.
    // Under the app data folder, not /tmp, which sits in the container's image layer on typical setups. Each
    // import removes its own folder; one left by a crash or a kill is removed at the next start. Only a strict
    // child of the work root is ever deleted, whatever a download id looks like.
    public class ChapterWorkFolder : IChapterWorkFolder, IHandle<ApplicationStartedEvent>
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public ChapterWorkFolder(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        private string Root => Path.GetFullPath(Path.Combine(_appFolderInfo.AppDataFolder, "chapter-work"));

        public string For(string downloadId)
        {
            return Path.Combine(Root, FolderName(downloadId));
        }

        public void Remove(string folder)
        {
            if (!IsWorkFolder(folder))
            {
                _logger.Warn("Refusing to delete {0}: not a chapter work folder", folder);
                return;
            }

            if (_diskProvider.FolderExists(folder))
            {
                _diskProvider.DeleteFolder(folder, true);
            }
        }

        public void Handle(ApplicationStartedEvent message)
        {
            string root = null;

            try
            {
                root = Root;

                if (!_diskProvider.FolderExists(root))
                {
                    return;
                }

                foreach (var folder in _diskProvider.GetDirectories(root).ToList())
                {
                    _logger.Debug("Removing leftover chapter work folder {0}", folder);
                    Remove(folder);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not clear leftover chapter work folders in {0}", root);
            }
        }

        // One path segment, never empty, "." or "..": the id with path-unsafe characters replaced, or a hash of
        // it when nothing usable is left.
        private static string FolderName(string downloadId)
        {
            var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\' }).ToHashSet();
            var name = new string((downloadId ?? string.Empty).Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();

            if (name.Length > 0 && name.Trim('.').Length > 0)
            {
                return name;
            }

            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(downloadId ?? string.Empty));

            return "id-" + string.Concat(hash.Select(b => b.ToString("x2")));
        }

        private bool IsWorkFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return false;
            }

            var full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(full);

            return string.Equals(Path.GetDirectoryName(full), root, StringComparison.Ordinal) &&
                   name.Length > 0 && name != "." && name != "..";
        }
    }
}
