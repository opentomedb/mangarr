using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Gcd;

namespace NzbDrone.Core.Collections
{
    // The poster of a catalogue line that is NOT in the library (a collection member the user has
    // not added). A library entry has its own MediaCover; a catalogue-only line had nothing, so the
    // Collections page showed a placeholder for every arc and spin-off. Sources, in order: the
    // artifact's own cover for the lowest volume that has one (an ISBN-keyed OpenLibrary / openBD
    // cover, no network), else AniList's art by the line's AniList id. AniList answers are kept on
    // disk, so a page load costs one call per line ONCE; a miss is asked again after a week.
    public interface ICollectionPosterService
    {
        string PosterFor(GcdSeries line);
    }

    public class CollectionPosterService : ICollectionPosterService
    {
        private const string CacheFileName = "collection-posters.json";
        private static readonly TimeSpan MissLifetime = TimeSpan.FromDays(7);

        private readonly IGcdMetadataService _gcd;
        private readonly IAniListService _aniList;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;
        private readonly object _storeMutex = new object();
        private Dictionary<string, CacheEntry> _store;

        public class CacheEntry
        {
            public DateTime FetchedAt { get; set; }
            public string Url { get; set; }
        }

        public CollectionPosterService(IGcdMetadataService gcdMetadataService,
                                       IAniListService aniListService,
                                       IAppFolderInfo appFolderInfo,
                                       IDiskProvider diskProvider,
                                       Logger logger)
        {
            _gcd = gcdMetadataService;
            _aniList = aniListService;
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public string PosterFor(GcdSeries line)
        {
            if (line == null)
            {
                return null;
            }

            var artifactCover = _gcd.GetVolumes(line.GcdSeriesId)
                                    .Where(v => v.CoverUrl.IsNotNullOrWhiteSpace())
                                    .OrderBy(v => v.VolumeNumber)
                                    .Select(v => v.CoverUrl)
                                    .FirstOrDefault();

            if (artifactCover != null)
            {
                return artifactCover;
            }

            return line.AnilistId.HasValue ? AniListCover(line.AnilistId.Value) : null;
        }

        private string AniListCover(int anilistId)
        {
            var key = anilistId.ToString();

            lock (_storeMutex)
            {
                var store = LoadStore();

                if (store.TryGetValue(key, out var stored) &&
                    (stored.Url.IsNotNullOrWhiteSpace() || stored.FetchedAt + MissLifetime > DateTime.UtcNow))
                {
                    return stored.Url;
                }

                string url = null;

                try
                {
                    url = _aniList.GetById(anilistId)?.CoverImageUrl;
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "AniList id {0}: could not fetch a cover for the collection card", anilistId);
                }

                store[key] = new CacheEntry { FetchedAt = DateTime.UtcNow, Url = url };
                Save(store);

                return url;
            }
        }

        private string CachePath => Path.Combine(_appFolderInfo.AppDataFolder, "metadata", CacheFileName);

        private void Save(Dictionary<string, CacheEntry> store)
        {
            try
            {
                var path = CachePath;
                var dir = Path.GetDirectoryName(path);

                if (!_diskProvider.FolderExists(dir))
                {
                    _diskProvider.CreateFolder(dir);
                }

                var temp = path + ".tmp";
                _diskProvider.WriteAllText(temp, store.ToJson(Formatting.None));
                _diskProvider.MoveFile(temp, path, true);
            }
            catch (Exception ex)
            {
                // The store is an optimisation: a write failure costs the answer its next process, not this lookup.
                _logger.Debug(ex, "Could not write the collection poster cache");
            }
        }

        // Read once per process. A file that does not parse is treated as empty.
        private Dictionary<string, CacheEntry> LoadStore()
        {
            if (_store != null)
            {
                return _store;
            }

            var store = new Dictionary<string, CacheEntry>();

            try
            {
                var path = CachePath;

                if (_diskProvider.FileExists(path))
                {
                    var raw = Json.Deserialize<Dictionary<string, CacheEntry>>(_diskProvider.ReadAllText(path)) ?? new Dictionary<string, CacheEntry>();

                    foreach (var pair in raw.Where(p => p.Value != null))
                    {
                        store[pair.Key] = pair.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not read the collection poster cache, starting empty");
            }

            _store = store;
            return _store;
        }
    }
}
