using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MetadataSource.Manga
{
    public interface IMetadataOverridesService
    {
        void Apply(string seriesName, List<MangaVolumeMetadata> volumes);

        // Read/write access for the Metadata Pins UI. Editing the file by hand still
        // works — it is watched by mtime and re-read on change either way.
        Dictionary<string, VolumeOverride> Get(string seriesName);
        void Set(string seriesName, string volumeNumber, VolumeOverride pin);
        void Remove(string seriesName, string volumeNumber);
        string OverridesPath { get; }

        // One copy each (2026-09-20): the series row's author pin (the "*" key), trimmed, or null
        // when the series has no row or the row has no author. Never throws.
        string GetSeriesAuthor(string seriesName);
    }

    // One pinned volume. `Source` and `Checked` are not used by the resolver: they are
    // there because a pin without a source is a guess, and because a pin that records
    // where its value came from can be promoted into an OpenTome correction verbatim
    // (corrections/volumes.json has the same three fields).
    public class VolumeOverride
    {
        public string ReleaseDate { get; set; }
        public int? PageCount { get; set; }
        public string Isbn13 { get; set; }
        public string CoverUrl { get; set; }   // the volume's cover, the operator's word over every provider (B3b, 2026-09-18)
        public string AudiobookTitle { get; set; }   // the Audible product title as it should be tagged (2026-09-19): Audible's own data has typos ("Jobless Reincarnatio")
        public string Author { get; set; }   // the light novel's real author, on the series row ("*") only (one copy each, 2026-09-20)
        public string Source { get; set; }
        public string Checked { get; set; }

        // Computed, never persisted: without this the flag is written into every entry
        // of a file that people edit by hand.
        [JsonIgnore]
        public bool IsEmpty =>
            ReleaseDate.IsNullOrWhiteSpace() && Isbn13.IsNullOrWhiteSpace() && !PageCount.HasValue && CoverUrl.IsNullOrWhiteSpace() && AudiobookTitle.IsNullOrWhiteSpace() && Author.IsNullOrWhiteSpace();

        // A cover pin is an absolute http(s) URL: anything else would only surface as a per-pass
        // Error from MediaCoverService. Checked once here and at the PUT that writes it.
        public static bool IsCoverUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }

    // Local metadata pins: <appdata>/metadata/overrides.json maps a series name to per-volume
    // corrections that win over every provider and every plausibility guard — the instant
    // alternative to editing the metadata artifact generator and waiting for a release cycle.
    // Shape: { "berserk": { "10": { "releaseDate": "2006-01-25", "pageCount": 240, "coverUrl": "https://..." } } }
    // Dates are calendar dates (yyyy-MM-dd), stored as UTC like every other release date.
    public class MetadataOverridesService : IMetadataOverridesService
    {
        // The series row: { "<series>": { "*": { "author": "..." } } }. Never a volume number, so
        // Apply's per-volume lookup can never match it.
        public const string SeriesKey = "*";

        public const string OverridesFileName = "overrides.json";

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        // Property names camelCase; DICTIONARY KEYS ARE LEFT ALONE. The app-wide
        // serializer uses CamelCasePropertyNamesContractResolver, whose naming strategy
        // also processes dictionary keys — writing a pin through it renamed the user's
        // own series keys ("Berserk" -> "berserk", "Vinland Saga" -> "vinland Saga").
        // Lookups are case-insensitive so nothing broke, which is exactly why it would
        // have gone unnoticed in a file people edit by hand.
        private static readonly JsonSerializerSettings WriteSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy(false, false)
            }
        };

        private readonly object _mutex = new object();
        private DateTime _loadedMtime = DateTime.MinValue;
        private Dictionary<string, Dictionary<string, VolumeOverride>> _overrides =
            new Dictionary<string, Dictionary<string, VolumeOverride>>(StringComparer.OrdinalIgnoreCase);

        public MetadataOverridesService(IAppFolderInfo appFolderInfo,
                                        IDiskProvider diskProvider,
                                        Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public string OverridesPath => OverridesPathIn(_appFolderInfo.AppDataFolder);

        // Also where BackupService takes the pins from and restores them to (beta readiness S5).
        public static string OverridesPathIn(string appDataFolder)
        {
            return Path.Combine(appDataFolder, "metadata", OverridesFileName);
        }


        public void Apply(string seriesName, List<MangaVolumeMetadata> volumes)
        {
            var map = Load();

            if (seriesName == null || !map.TryGetValue(seriesName.Trim(), out var vols))
            {
                return;
            }

            var applied = 0;

            // Keyed by the formatted volume number: the series row (SeriesKey, "*") never matches here.
            foreach (var volume in volumes)
            {
                if (!vols.TryGetValue(MangaVolumeParser.Format(volume.VolumeNumber), out var pin))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(pin.ReleaseDate) &&
                    DateTime.TryParseExact(pin.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    // Local midnight in UTC, the value MangaSeriesMetadataProvider.ParseDate produces (2026-09-17).
                    volume.ReleaseDate = DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime();
                    volume.ReleaseDatePrecision = null;   // Preferred Edition (2026-09-24, D6): a pinned date is a day
                }

                if (pin.PageCount.HasValue && pin.PageCount.Value > 0)
                {
                    volume.PageCount = pin.PageCount.Value;
                }

                if (!string.IsNullOrWhiteSpace(pin.Isbn13))
                {
                    volume.Isbn13 = pin.Isbn13.Trim();
                }

                // Cover pin (B3b, 2026-09-18): the operator's word over whatever Google, MangaDex or the
                // artifact chose (Apply runs after the per-volume loop); a pinned volume 1 is the poster.
                // A value that is not an http(s) URL (a hand edit) is skipped and said so once.
                if (!string.IsNullOrWhiteSpace(pin.CoverUrl))
                {
                    var coverUrl = pin.CoverUrl.Trim();

                    if (!VolumeOverride.IsCoverUrl(coverUrl))
                    {
                        _logger.Warn("Cover pin for {0} Vol. {1} ignored: not an http(s) URL: '{2}'", seriesName, MangaVolumeParser.Format(volume.VolumeNumber), coverUrl);
                        continue;
                    }

                    volume.CoverUrl = coverUrl;
                    volume.CoverSource = "pin";
                }

                // Audiobook title pin (2026-09-19): the operator's word over Audible's product title —
                // what the additive tag write puts in album/title. Apply runs after the Audible step,
                // so it only lands on a volume that has an audio identity; the ratchet on the edition
                // keeps it (a non-blank remote value always wins there).
                if (!string.IsNullOrWhiteSpace(pin.AudiobookTitle) && volume.Audio != null)
                {
                    volume.Audio.Title = pin.AudiobookTitle.Trim();
                }

                applied++;
            }

            if (applied > 0)
            {
                _logger.Debug("Applied {0} metadata override(s) for {1}", applied, seriesName);
            }
        }

        public Dictionary<string, VolumeOverride> Get(string seriesName)
        {
            var map = Load();

            if (seriesName.IsNotNullOrWhiteSpace() && map.TryGetValue(seriesName.Trim(), out var vols))
            {
                return new Dictionary<string, VolumeOverride>(vols, StringComparer.OrdinalIgnoreCase);
            }

            return new Dictionary<string, VolumeOverride>(StringComparer.OrdinalIgnoreCase);
        }

        public void Set(string seriesName, string volumeNumber, VolumeOverride pin)
        {
            if (seriesName.IsNullOrWhiteSpace() || volumeNumber.IsNullOrWhiteSpace())
            {
                throw new ArgumentException("A pin needs a series and a volume number");
            }

            // An empty pin is a removal, not a row of nulls: leaving {} behind would
            // make the file grow with entries that override nothing.
            if (pin == null || pin.IsEmpty)
            {
                Remove(seriesName, volumeNumber);
                return;
            }

            Mutate(all =>
            {
                if (!all.TryGetValue(seriesName.Trim(), out var vols))
                {
                    vols = new Dictionary<string, VolumeOverride>(StringComparer.OrdinalIgnoreCase);
                    all[seriesName.Trim()] = vols;
                }

                vols[volumeNumber.Trim()] = pin;
            });

            _logger.Info("Pinned metadata for {0} Vol. {1}", seriesName, volumeNumber);
        }

        public string GetSeriesAuthor(string seriesName)
        {
            if (Get(seriesName).TryGetValue(SeriesKey, out var pin) && pin?.Author.IsNotNullOrWhiteSpace() == true)
            {
                return pin.Author.Trim();
            }

            return null;
        }

        public void Remove(string seriesName, string volumeNumber)
        {
            Mutate(all =>
            {
                if (all.TryGetValue(seriesName.Trim(), out var vols))
                {
                    vols.Remove(volumeNumber.Trim());

                    if (vols.Count == 0)
                    {
                        all.Remove(seriesName.Trim());
                    }
                }
            });

            _logger.Info("Removed metadata pin for {0} Vol. {1}", seriesName, volumeNumber);
        }

        // Read-modify-write under the same lock the reader uses, then re-read. The file is
        // also hand-editable, so the read happens INSIDE the lock rather than from the cache:
        // a pin written on top of a stale cache would silently drop an edit made by hand.
        private void Mutate(Action<Dictionary<string, Dictionary<string, VolumeOverride>>> change)
        {
            lock (_mutex)
            {
                var path = OverridesPath;
                var all = ReadFile(path);

                change(all);

                var dir = Path.GetDirectoryName(path);

                if (!_diskProvider.FolderExists(dir))
                {
                    _diskProvider.CreateFolder(dir);
                }

                // Write beside the target and move into place: a half-written overrides.json
                // is read as corrupt and every pin in it is lost.
                var temp = path + ".tmp";
                // Indented: this file is edited by hand as often as through the UI.
                // Trailing newline: most editors add one, and without it every hand edit
                // shows up as a spurious whole-last-line diff.
                _diskProvider.WriteAllText(temp, JsonConvert.SerializeObject(all, WriteSettings) + Environment.NewLine);
                _diskProvider.MoveFile(temp, path, true);

                _overrides = new Dictionary<string, Dictionary<string, VolumeOverride>>(all, StringComparer.OrdinalIgnoreCase);
                _loadedMtime = _diskProvider.FileExists(path) ? _diskProvider.FileGetLastWrite(path) : DateTime.MinValue;
            }
        }

        private Dictionary<string, Dictionary<string, VolumeOverride>> ReadFile(string path)
        {
            if (!_diskProvider.FileExists(path))
            {
                return new Dictionary<string, Dictionary<string, VolumeOverride>>(StringComparer.OrdinalIgnoreCase);
            }

            var raw = Json.Deserialize<Dictionary<string, Dictionary<string, VolumeOverride>>>(_diskProvider.ReadAllText(path))
                      ?? new Dictionary<string, Dictionary<string, VolumeOverride>>();

            return new Dictionary<string, Dictionary<string, VolumeOverride>>(
                raw.ToDictionary(kv => kv.Key,
                                 kv => new Dictionary<string, VolumeOverride>(kv.Value ?? new Dictionary<string, VolumeOverride>(),
                                                                             StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, Dictionary<string, VolumeOverride>> Load()
        {
            var path = OverridesPath;

            lock (_mutex)
            {
                if (!_diskProvider.FileExists(path))
                {
                    return _overrides;
                }

                var mtime = _diskProvider.FileGetLastWrite(path);

                if (mtime == _loadedMtime)
                {
                    return _overrides;
                }

                // Advance the mtime marker unconditionally so a broken file is parsed
                // (and warned about) once per edit, not once per series lookup forever.
                _loadedMtime = mtime;

                try
                {
                    _overrides = ReadFile(path);
                    _logger.Info("Loaded metadata overrides for {0} series from {1}", _overrides.Count, path);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to read metadata overrides at {0}; keeping previous set", path);
                }

                return _overrides;
            }
        }
    }
}
