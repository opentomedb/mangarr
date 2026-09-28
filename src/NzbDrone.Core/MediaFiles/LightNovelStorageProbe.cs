using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.MediaFiles
{
    public class LightNovelLibraryOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class LightNovelLibraryList
    {
        public List<LightNovelLibraryOption> Libraries { get; set; } = new List<LightNovelLibraryOption>();
        public string DefaultLibrary { get; set; }
        public List<string> Problems { get; set; } = new List<string>();
    }

    public interface ILightNovelStorageProbe
    {
        List<string> TestCalibre(CalibreConnection connection);
        List<string> TestAudiobookshelf(AudiobookshelfConnection connection);
        LightNovelLibraryList CalibreLibraries(CalibreConnection connection);
        LightNovelLibraryList AudiobookshelfLibraries(AudiobookshelfConnection connection);
    }

    // Light-novel storage (2026-09-22): the Test buttons, the library dropdowns and both health checks
    // ask the same questions in the same order -- is it set, does it answer, does it take the login,
    // is the library there, may Mangarr write, does the mapped folder exist here -- and answer in
    // plain words that name the setting to fix (en.json LightNovelStorage*). Never throws.
    public class LightNovelStorageProbe : ILightNovelStorageProbe
    {
        private readonly ICalibreProxy _calibreProxy;
        private readonly IAudiobookshelfClient _audiobookshelf;
        private readonly IDiskProvider _diskProvider;
        private readonly INamingConfigService _namingConfigService;
        private readonly ILocalizationService _localizationService;
        private readonly Logger _logger;

        public LightNovelStorageProbe(ICalibreProxy calibreProxy,
                                      IAudiobookshelfClient audiobookshelf,
                                      IDiskProvider diskProvider,
                                      INamingConfigService namingConfigService,
                                      ILocalizationService localizationService,
                                      Logger logger)
        {
            _calibreProxy = calibreProxy;
            _audiobookshelf = audiobookshelf;
            _diskProvider = diskProvider;
            _namingConfigService = namingConfigService;
            _localizationService = localizationService;
            _logger = logger;
        }

        public List<string> TestCalibre(CalibreConnection connection)
        {
            var problems = new List<string>();
            var info = ReadCalibre(connection, problems, out var settings);

            if (info == null)
            {
                return problems;
            }

            var library = connection.Library.IsNullOrWhiteSpace() ? info.DefaultLibrary : connection.Library.Trim();

            if (library.IsNullOrWhiteSpace() || info.LibraryMap == null || !info.LibraryMap.ContainsKey(library))
            {
                problems.Add(Say("LightNovelStorageCalibreLibraryMissing", library));
            }
            else
            {
                settings.Library = library;

                try
                {
                    if (!_calibreProxy.HasWriteAccess(settings))
                    {
                        problems.Add(Say("LightNovelStorageCalibreNoWriteAccess"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "calibre write probe failed at {0}", connection.Url);
                    problems.Add(Say("LightNovelStorageCalibreUnreachable", connection.Url));
                }
            }

            if (LightNovelPathMap.IsSet(connection.RemotePath, connection.LocalPath) && !_diskProvider.FolderExists(connection.LocalPath))
            {
                problems.Add(Say("LightNovelStorageCalibrePathMissing", connection.LocalPath));
            }

            return problems;
        }

        public LightNovelLibraryList CalibreLibraries(CalibreConnection connection)
        {
            var list = new LightNovelLibraryList();
            var info = ReadCalibre(connection, list.Problems, out _);

            if (info != null)
            {
                list.DefaultLibrary = info.DefaultLibrary;
                list.Libraries = (info.LibraryMap ?? new Dictionary<string, string>())
                    .Select(l => new LightNovelLibraryOption { Id = l.Key, Name = l.Value.IsNullOrWhiteSpace() ? l.Key : l.Value })
                    .ToList();
            }

            return list;
        }

        public List<string> TestAudiobookshelf(AudiobookshelfConnection connection)
        {
            var problems = TestAudiobookshelfConnection(connection);

            // Beta readiness (2026-09-28, F6): the Audiobookshelf layout ("<Series> - Vol. N" folders) comes
            // from the file namer, so with Rename Volumes off every audio import into Audiobookshelf is
            // refused (UpgradeMediaFileService). The Test button and the health check say so up front,
            // whatever the connection's state.
            if (!_namingConfigService.GetConfig().RenameBooks)
            {
                problems.Add(Say("LightNovelStorageAudiobookshelfRenameOff"));
            }

            return problems;
        }

        private List<string> TestAudiobookshelfConnection(AudiobookshelfConnection connection)
        {
            var problems = new List<string>();
            var libraries = ReadAudiobookshelf(connection, problems);

            if (libraries == null)
            {
                return problems;
            }

            if (connection.LibraryId.IsNullOrWhiteSpace())
            {
                problems.Add(Say("LightNovelStorageAudiobookshelfLibraryNotChosen"));
                return problems;
            }

            var library = libraries.FirstOrDefault(l => l.Id == connection.LibraryId);

            if (library == null)
            {
                problems.Add(Say("LightNovelStorageAudiobookshelfLibraryMissing", connection.LibraryId));
                return problems;
            }

            // Mangarr writes into the Mangarr side of the pair; that folder must be (inside) the library's.
            if (LightNovelPathMap.IsSet(connection.RemotePath, connection.LocalPath) &&
                !library.Folders.Any(f => LightNovelPathMap.IsUnder(connection.RemotePath, f)))
            {
                problems.Add(Say("LightNovelStorageAudiobookshelfFolderNotMapped", connection.RemotePath, string.Join(", ", library.Folders)));
            }

            var root = LightNovelStorage.AudiobookshelfRootOf(connection, library);

            if (root == null || !_diskProvider.FolderExists(root))
            {
                problems.Add(Say("LightNovelStorageAudiobookshelfPathMissing", root ?? "?"));
            }

            return problems;
        }

        public LightNovelLibraryList AudiobookshelfLibraries(AudiobookshelfConnection connection)
        {
            var list = new LightNovelLibraryList();
            var libraries = ReadAudiobookshelf(connection, list.Problems);

            if (libraries != null)
            {
                list.Libraries = libraries
                    .Where(l => l.MediaType.IsNullOrWhiteSpace() || l.MediaType == "book")
                    .Select(l => new LightNovelLibraryOption { Id = l.Id, Name = l.Name })
                    .ToList();
            }

            return list;
        }

        private CalibreLibraryInfo ReadCalibre(CalibreConnection connection, List<string> problems, out CalibreSettings settings)
        {
            settings = null;

            if (connection.Url.IsNullOrWhiteSpace())
            {
                problems.Add(Say("LightNovelStorageCalibreUrlMissing"));
                return null;
            }

            try
            {
                settings = LightNovelCalibreSettings.Build(connection, null);
            }
            catch (UriFormatException)
            {
                problems.Add(Say("LightNovelStorageCalibreUrlInvalid", connection.Url));
                return null;
            }

            try
            {
                return _calibreProxy.GetLibraryInfo(settings);
            }
            catch (HttpException ex) when (ex.Response?.StatusCode == HttpStatusCode.Unauthorized || ex.Response?.StatusCode == HttpStatusCode.Forbidden)
            {
                problems.Add(Say("LightNovelStorageCalibreLoginRejected"));
                return null;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "calibre did not answer at {0}", connection.Url);
                problems.Add(Say("LightNovelStorageCalibreUnreachable", connection.Url));
                return null;
            }
        }

        private List<AudiobookshelfLibrary> ReadAudiobookshelf(AudiobookshelfConnection connection, List<string> problems)
        {
            if (connection.Url.IsNullOrWhiteSpace())
            {
                problems.Add(Say("LightNovelStorageAudiobookshelfUrlMissing"));
                return null;
            }

            try
            {
                return _audiobookshelf.GetLibraries(connection.Url.Trim(), connection.ApiKey);
            }
            catch (HttpException ex) when (ex.Response?.StatusCode == HttpStatusCode.Unauthorized || ex.Response?.StatusCode == HttpStatusCode.Forbidden)
            {
                problems.Add(Say("LightNovelStorageAudiobookshelfKeyRejected"));
                return null;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Audiobookshelf did not answer at {0}", connection.Url);
                problems.Add(Say("LightNovelStorageAudiobookshelfUnreachable", connection.Url));
                return null;
            }
        }

        private string Say(string key, params object[] args)
        {
            var template = _localizationService.GetLocalizedString(key);

            return args.Length == 0 ? template : string.Format(template, args);
        }
    }
}
