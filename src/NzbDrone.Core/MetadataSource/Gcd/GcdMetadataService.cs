using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using Dapper;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource.Gcd
{
    // Reads the bundled, read-only GCD metadata artifact (manga-metadata.sqlite) — the per-volume
    // "spine" with English-edition + omnibus awareness. Fail-soft: when no artifact is present the
    // service simply reports nothing, so callers fall back cleanly to the live sources (AniList /
    // MangaUpdates / MangaDex / Google Books). Convention-registered (DryIoc RegisterMany).
    public interface IGcdMetadataService
    {
        bool Available { get; }
        GcdSeries FindSeriesById(int gcdSeriesId);

        // The lines whose parent_series_id is this series (collection members: arcs, side
        // stories, spin-off editions). Empty when the artifact predates the column.
        List<GcdSeries> GetChildren(int gcdSeriesId);
        GcdSeries FindSeriesByTitle(string title);
        GcdSeries FindSeriesBySubtitle(string title);

        // Light novels (2026-09): the same lookups restricted to novel lines (light_novel / novel /
        // artbook). The one-argument forms mean Manga and are unchanged.
        GcdSeries FindSeriesByTitle(string title, LibraryType library);
        GcdSeries FindSeriesBySubtitle(string title, LibraryType library);
        List<GcdVolume> GetVolumes(int gcdSeriesId);

        // The line's series_alias rows (raw and normalised forms), stored order, never null. The
        // AniList alias retry (D3) tries them after the name and the entry's own de-slugged id.
        List<string> GetAliases(int gcdSeriesId);

        // Preferred Edition (2026-09-24). Every read is guarded: an artifact without the column or
        // table answers null / empty, never throws -- the English path never calls these.

        // The line with this OpenTome id, following id_redirect (entity release_line) for a retired id.
        // Null when neither.
        GcdSeries FindSeriesByTomeId(string tomeId);

        // Every line of the work, every language and medium. Empty when the artifact has no tome_work_id.
        List<GcdSeries> GetWorkLines(string tomeWorkId);

        // KR/CN consumer (2026-09-29, spec §3.1): the lines carrying this AniList id, widened to every line of
        // their works (tome_work_id), library-gated like Rank; English first, then is_main, then Rank's tail.
        // A KR/CN work is often catalogued under a German or French name, so the English title misses it.
        GcdSeries FindSeriesByAnilistId(int anilistId, LibraryType library);

        // FindSeriesByTitle restricted to these languages, the chain order ranked first (spec §2.1
        // step 2: a work with no English line).
        GcdSeries FindSeriesByTitle(string title, LibraryType library, IReadOnlyList<string> languages);

        // series_alias rows with language/kind, stored order.
        List<GcdAlias> GetAliasRows(int gcdSeriesId);

        // Lines per language: meta 'markets', else counted from series.language. Empty without an artifact.
        Dictionary<string, int> Markets();

        // GetVolumes plus, when includeEditionDates, the guarded release_date_raw / _precision / _type
        // reads (D6). A separate overload, not a default argument: GetVolumes(int) is mocked in
        // expression trees, and the English path must issue no new SQL (Global Constraints).
        List<GcdVolume> GetVolumes(int gcdSeriesId, bool includeEditionDates);

        // Returns the gcd_dump version string stored in the artifact's meta table, or null if the
        // artifact is absent or has no meta row. Used by MetadataUpdateService to compare versions.
        string LocalDumpVersion();

        // Identity, provenance and attribution of the loaded artifact, for the settings page.
        GcdArtifactInfo ArtifactInfo();

        // Drops the resolved-path cache so the next query re-resolves from disk. Call after a new
        // artifact has been written to the override directory.
        void Reload();

        // Canonical path where the updater should write a downloaded artifact. Always under
        // {AppData}/metadata/ so it is picked up by FindArtifact()'s override search.
        string OverrideArtifactPath { get; }
    }

    public class GcdMetadataService : IGcdMetadataService
    {
        public const string ArtifactName = "manga-metadata.sqlite";

        private const string SeriesSelect =
            @"SELECT gcd_series_id AS GcdSeriesId, name AS Name, year_began AS YearBegan,
                     publisher AS Publisher, language AS Language, country AS Country,
                     is_omnibus AS IsOmnibus, volume_count AS VolumeCount, status AS Status,
                     orig_series_id AS OrigSeriesId, anilist_id AS AnilistId,
                     mangaupdates_id AS MangaUpdatesId, mangadex_id AS MangaDexId
              FROM series";

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;
        private readonly object _resolveLock = new object();
        private bool _resolved;
        private string _dbPath;

        public GcdMetadataService(IAppFolderInfo appFolderInfo, Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _logger = logger;
        }

        public bool Available => ResolvePath() != null;

        public GcdSeries FindSeriesById(int gcdSeriesId)
        {
            return Query(conn =>
            {
                var row = conn.QueryFirstOrDefault<GcdSeries>(
                    SeriesSelect + " WHERE gcd_series_id = @id", new { id = gcdSeriesId });
                if (row != null)
                {
                    LoadOptionalColumns(conn, new List<GcdSeries> { row }, gcdSeriesId.ToString());
                }

                return row;
            });
        }

        public List<GcdSeries> GetChildren(int gcdSeriesId)
        {
            return Query(conn =>
            {
                try
                {
                    var rows = conn.Query<GcdSeries>(
                        SeriesSelect + " WHERE parent_series_id = @id ORDER BY name", new { id = gcdSeriesId }).ToList();
                    if (rows.Count > 0)
                    {
                        LoadOptionalColumns(conn, rows, string.Join(",", rows.Select(r => r.GcdSeriesId)));
                    }

                    return rows;
                }
                catch (SQLiteException)
                {
                    return new List<GcdSeries>();   // artifact without parent_series_id
                }
            }) ?? new List<GcdSeries>();
        }

        public GcdSeries FindSeriesByTitle(string title)
        {
            return FindSeriesByTitle(title, LibraryType.Manga);
        }

        public GcdSeries FindSeriesByTitle(string title, LibraryType library)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return null;
            }

            var normalized = Normalize(title);

            return Query(conn =>
            {
                var candidates = TitleCandidates(conn, title.Trim(), normalized);

                return candidates == null ? null : Rank(candidates, normalized, library);
            });
        }

        public GcdSeries FindSeriesByTitle(string title, LibraryType library, IReadOnlyList<string> languages)
        {
            if (title.IsNullOrWhiteSpace() || languages == null || languages.Count == 0)
            {
                return null;
            }

            var normalized = Normalize(title);

            return Query(conn =>
            {
                var candidates = TitleCandidates(conn, title.Trim(), normalized);

                return candidates == null ? null : RankInLanguages(candidates, normalized, library, languages);
            });
        }

        // The candidate lines for a title: raw name/alias matches plus the normalized alias match, with
        // the optional columns loaded. Null when nothing matches. (Extracted unchanged from
        // FindSeriesByTitle for the Preferred Edition overload, 2026-09-24.)
        private List<GcdSeries> TitleCandidates(SQLiteConnection conn, string trimmed, string normalized)
        {
            // Match the raw title against names/aliases, and a normalized form against the
            // normalized aliases the artifact stores (so folder-derived names with different
            // punctuation/subtitles still resolve to GCD's English series name).
            var ids = conn.Query<int>(
                @"SELECT gcd_series_id FROM series WHERE name = @t COLLATE NOCASE
                  UNION SELECT gcd_series_id FROM series_alias WHERE alias = @t COLLATE NOCASE
                  UNION SELECT gcd_series_id FROM series_alias WHERE alias = @n COLLATE NOCASE",
                new { t = trimmed, n = normalized }).Distinct().ToList();

            if (ids.Count == 0)
            {
                return null;
            }

            // Build the IN list from the int ids directly (injection-safe — they're ints).
            // Dapper's "IN @ids" list-parameter expansion fails to prepare against the bundled
            // SQLite the app links at runtime (3.34.1: "SQL logic error"), even though the same
            // SQL is valid there via plain sqlite3. A literal int list sidesteps the expansion.
            var inList = string.Join(",", ids);
            var candidates = conn.Query<GcdSeries>(
                SeriesSelect + " WHERE gcd_series_id IN (" + inList + ")").ToList();
            LoadOptionalColumns(conn, candidates, inList);

            return candidates;
        }

        // Pick one release line for a title. Measured against the live library with a multi-market
        // artifact (13,001 lines): the old order — language, then !omnibus, then volume count — bound
        // "Attack on Titan" to its 17-volume "Before the Fall" spin-off, "Tokyo Ghoul" to the
        // 3-volume light novel and "Solo Leveling" to an undated line with a bigger count, because a
        // work's aliases fan out to every line of the work and count alone is not a tell.
        //
        //   1. English lines only. The artifact carries ja/fr/de lines too, but this app hard-codes
        //      the edition language as English (BookInfoProxy), so a Japanese line's dates and ISBNs
        //      would be presented as English data. No English line -> null -> the live-source chain,
        //      exactly what the English-only artifact did before.
        //   2. A line NAMED what was asked for beats one that merely carries the name as an alias.
        //   3. The work's main line for the market, then manga before light novels of the same name.
        //   4. Non-omnibus (the standard tankoubon-equivalent people collect), then the line with
        //      the most dated volumes, then the most volumes, then the lowest id (deterministic).
        //   0. Library gate (2026-09): a light-novel entry binds to a novel line (light_novel /
        //      novel / artbook) or to nothing -- a manga line's volumes are the wrong facts (other
        //      ISBNs, dates, page counts). Manga entries are unchanged: prefer a manga line, fall
        //      back to a novel line when that is all the name has (existing entries must not re-bind).
        internal static GcdSeries Rank(IEnumerable<GcdSeries> candidates, string normalizedQuery, LibraryType library = LibraryType.Manga)
        {
            var wantNovel = library == LibraryType.LightNovel;

            return candidates
                .Where(s => s.Language == "en")
                .Where(s => !wantNovel || IsLightNovel(s.Medium))
                .OrderByDescending(s => Normalize(s.Name) == normalizedQuery)
                .ThenByDescending(s => s.IsMain)
                .ThenBy(s => IsLightNovel(s.Medium))
                .ThenBy(s => s.IsOmnibus)
                .ThenByDescending(s => s.DatedCount ?? 0)
                .ThenByDescending(s => s.VolumeCount)
                .ThenBy(s => s.GcdSeriesId)
                .FirstOrDefault();
        }

        // Preferred Edition (2026-09-24, spec §2.1 step 2): Rank's order within the chosen languages, the
        // chain's order first. Used only when the work has no English line and the chain names another.
        internal static GcdSeries RankInLanguages(IEnumerable<GcdSeries> candidates, string normalizedQuery, LibraryType library, IReadOnlyList<string> languages)
        {
            var wantNovel = library == LibraryType.LightNovel;
            var order = languages.ToList();

            return candidates
                .Where(s => s.Language != null && order.Contains(s.Language))
                .Where(s => !wantNovel || IsLightNovel(s.Medium))
                .OrderBy(s => order.IndexOf(s.Language))
                .ThenByDescending(s => Normalize(s.Name) == normalizedQuery)
                .ThenByDescending(s => s.IsMain)
                .ThenBy(s => IsLightNovel(s.Medium))
                .ThenBy(s => s.IsOmnibus)
                .ThenByDescending(s => s.DatedCount ?? 0)
                .ThenByDescending(s => s.VolumeCount)
                .ThenBy(s => s.GcdSeriesId)
                .FirstOrDefault();
        }

        internal static bool IsLightNovel(string medium)
        {
            return medium != null &&
                   (medium.Contains("novel", StringComparison.OrdinalIgnoreCase) ||
                    medium.Equals("artbook", StringComparison.OrdinalIgnoreCase));
        }

        // Columns that only newer artifacts carry. Read separately so an older artifact (GCD-derived,
        // schema_version 1) keeps working: a missing column there is expected, not an error.
        private void LoadOptionalColumns(SQLiteConnection conn, List<GcdSeries> candidates, string inList)
        {
            if (candidates.Count == 0)
            {
                return;
            }

            try
            {
                var extras = conn.Query<OptionalRow>(
                        @"SELECT gcd_series_id AS Id, medium AS Medium, dated_count AS DatedCount,
                                 is_main AS IsMain
                          FROM series WHERE gcd_series_id IN (" + inList + ")")
                    .ToDictionary(r => r.Id);

                foreach (var s in candidates)
                {
                    if (extras.TryGetValue(s.GcdSeriesId, out var e))
                    {
                        s.Medium = e.Medium;
                        s.DatedCount = e.DatedCount;
                        s.IsMain = e.IsMain != 0;
                    }
                }
            }
            catch (SQLiteException)
            {
                // schema_version 1 artifact: no medium / dated_count / is_main columns.
            }

            // Newer still (2026-09-04): parent_series_id. Its own guarded query, so an artifact that
            // has medium but not this keeps its medium.
            try
            {
                var parents = conn.Query<ParentRow>(
                        "SELECT gcd_series_id AS Id, parent_series_id AS ParentSeriesId FROM series WHERE gcd_series_id IN (" + inList + ")")
                    .ToDictionary(r => r.Id);

                foreach (var s in candidates)
                {
                    if (parents.TryGetValue(s.GcdSeriesId, out var p))
                    {
                        s.ParentSeriesId = p.ParentSeriesId;
                    }
                }
            }
            catch (SQLiteException)
            {
                // no parent_series_id column
            }

            // Newer still (2026-09-20): author, the work's author for the writer ladder. Guarded on
            // its own for the same reason.
            try
            {
                var authors = conn.Query<AuthorRow>(
                        "SELECT gcd_series_id AS Id, author AS Author FROM series WHERE gcd_series_id IN (" + inList + ")")
                    .ToDictionary(r => r.Id);

                foreach (var s in candidates)
                {
                    if (authors.TryGetValue(s.GcdSeriesId, out var a))
                    {
                        s.Author = a.Author;
                    }
                }
            }
            catch (SQLiteException)
            {
                // no author column
            }

            // Newer still (2026-09-24): the display-only AniList id OpenTome sets where anilist_id is
            // null (the parent work's / other medium's entry — a picture, never a binding). Guarded on
            // its own like the others: a missing column leaves both null.
            try
            {
                var displays = conn.Query<DisplayRow>(
                        "SELECT gcd_series_id AS Id, display_anilist_id AS DisplayAnilistId, display_anilist_via AS DisplayAnilistVia FROM series WHERE gcd_series_id IN (" + inList + ")")
                    .ToDictionary(r => r.Id);

                foreach (var s in candidates)
                {
                    if (displays.TryGetValue(s.GcdSeriesId, out var d))
                    {
                        s.DisplayAnilistId = d.DisplayAnilistId;
                        s.DisplayAnilistVia = d.DisplayAnilistVia;
                    }
                }
            }
            catch (SQLiteException)
            {
                // no display_anilist_id / display_anilist_via columns
            }

            // Preferred Edition (2026-09-24): tome ids (every OpenTome artifact) and the local name (v0),
            // each guarded on its own so an artifact with tome ids but no local_name keeps its ids.
            try
            {
                var tomes = conn.Query<TomeRow>(
                        "SELECT gcd_series_id AS Id, tome_id AS TomeId, tome_work_id AS TomeWorkId FROM series WHERE gcd_series_id IN (" + inList + ")")
                    .ToDictionary(r => r.Id);

                foreach (var s in candidates)
                {
                    if (tomes.TryGetValue(s.GcdSeriesId, out var t))
                    {
                        s.TomeId = t.TomeId;
                        s.TomeWorkId = t.TomeWorkId;
                    }
                }
            }
            catch (SQLiteException)
            {
                // no tome_id / tome_work_id columns
            }

            try
            {
                var locals = conn.Query<LocalNameRow>(
                        "SELECT gcd_series_id AS Id, local_name AS LocalName FROM series WHERE gcd_series_id IN (" + inList + ")")
                    .ToDictionary(r => r.Id);

                foreach (var s in candidates)
                {
                    if (locals.TryGetValue(s.GcdSeriesId, out var l))
                    {
                        s.LocalName = l.LocalName;
                    }
                }
            }
            catch (SQLiteException)
            {
                // no local_name column (OpenTome before v0)
            }
        }

        // Fallback for folder names that carry a franchise prefix ("Re:ZERO -Starting Life in
        // Another World-, Chapter 1: A Day in the Capital"): when the full name matches nothing, try
        // the arc title after the last ':' or ',' — but only a specific one (3+ words), so "Extra" or
        // "Side Story" can never bind a franchise folder to some unrelated line. Exact alias
        // equality is still required; this only changes WHAT is looked up, not how strictly.
        public GcdSeries FindSeriesBySubtitle(string title)
        {
            return FindSeriesBySubtitle(title, LibraryType.Manga);
        }

        public GcdSeries FindSeriesBySubtitle(string title, LibraryType library)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return null;
            }

            foreach (var sep in new[] { ':', ',' })
            {
                var idx = title.LastIndexOf(sep);
                if (idx < 0 || idx == title.Length - 1)
                {
                    continue;
                }

                var tail = title.Substring(idx + 1).Trim().TrimEnd('-', '–').Trim();
                if (Normalize(tail).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 3)
                {
                    continue;
                }

                var hit = FindSeriesByTitle(tail, library);
                if (hit != null)
                {
                    _logger.Debug("Resolved '{0}' via its arc title '{1}' -> {2}", title, tail, hit.Name);
                    return hit;
                }
            }

            return null;
        }

        private class CoverRow
        {
            public int VolumeNumber { get; set; }
            public string CoverUrl { get; set; }
            public string CoverSource { get; set; }
        }

        private class MetaRow
        {
            public string Key { get; set; }
            public string Value { get; set; }
        }

        private class OptionalRow
        {
            public int Id { get; set; }
            public string Medium { get; set; }
            public int? DatedCount { get; set; }
            public int IsMain { get; set; }
        }

        private class ParentRow
        {
            public int Id { get; set; }
            public int? ParentSeriesId { get; set; }
        }

        private class AuthorRow
        {
            public int Id { get; set; }
            public string Author { get; set; }
        }

        private class DisplayRow
        {
            public int Id { get; set; }
            public int? DisplayAnilistId { get; set; }
            public string DisplayAnilistVia { get; set; }
        }

        private class TomeRow
        {
            public int Id { get; set; }
            public string TomeId { get; set; }
            public string TomeWorkId { get; set; }
        }

        private class LocalNameRow
        {
            public int Id { get; set; }
            public string LocalName { get; set; }
        }

        private class DateRow
        {
            public int VolumeNumber { get; set; }
            public string ReleaseDateRaw { get; set; }
            public string ReleaseDatePrecision { get; set; }
        }

        private class DateTypeRow
        {
            public int VolumeNumber { get; set; }
            public string ReleaseDateType { get; set; }
        }

        private class MarketRow
        {
            public string Language { get; set; }
            public int Lines { get; set; }
        }

        public List<string> GetAliases(int gcdSeriesId)
        {
            return Query(conn => conn.Query<string>(
                    "SELECT alias FROM series_alias WHERE gcd_series_id = @id ORDER BY rowid",
                    new { id = gcdSeriesId })
                .Where(a => a.IsNotNullOrWhiteSpace())
                .ToList()) ?? new List<string>();
        }

        public GcdSeries FindSeriesByTomeId(string tomeId)
        {
            if (tomeId.IsNullOrWhiteSpace())
            {
                return null;
            }

            return Query(conn =>
            {
                int? id;

                try
                {
                    id = conn.QueryFirstOrDefault<int?>("SELECT gcd_series_id FROM series WHERE tome_id = @t", new { t = tomeId });
                }
                catch (SQLiteException)
                {
                    return null;   // no tome_id column
                }

                if (id == null)
                {
                    try
                    {
                        // A retired id resolves forever (OpenTome's contract): follow the artifact's
                        // id_redirect, whose chains are already collapsed to ids present in this build.
                        // Only release-line redirects: a retired volume id also redirects to a line
                        // there, but a series never binds a volume id.
                        var successor = conn.QueryFirstOrDefault<string>(
                            "SELECT new_tome_id FROM id_redirect WHERE old_tome_id = @t AND entity = 'release_line'",
                            new { t = tomeId });
                        id = successor == null ? null : conn.QueryFirstOrDefault<int?>("SELECT gcd_series_id FROM series WHERE tome_id = @t", new { t = successor });
                    }
                    catch (SQLiteException)
                    {
                        // no id_redirect table
                    }
                }

                if (id == null)
                {
                    return null;
                }

                var row = conn.QueryFirstOrDefault<GcdSeries>(SeriesSelect + " WHERE gcd_series_id = @id", new { id = id.Value });

                if (row != null)
                {
                    LoadOptionalColumns(conn, new List<GcdSeries> { row }, id.Value.ToString());
                }

                return row;
            });
        }

        public List<GcdSeries> GetWorkLines(string tomeWorkId)
        {
            if (tomeWorkId.IsNullOrWhiteSpace())
            {
                return new List<GcdSeries>();
            }

            return Query(conn =>
            {
                List<int> ids;

                try
                {
                    ids = conn.Query<int>("SELECT gcd_series_id FROM series WHERE tome_work_id = @w", new { w = tomeWorkId }).ToList();
                }
                catch (SQLiteException)
                {
                    return new List<GcdSeries>();   // no tome_work_id column
                }

                if (ids.Count == 0)
                {
                    return new List<GcdSeries>();
                }

                var inList = string.Join(",", ids);
                var rows = conn.Query<GcdSeries>(SeriesSelect + " WHERE gcd_series_id IN (" + inList + ")").ToList();
                LoadOptionalColumns(conn, rows, inList);

                return rows;
            }) ?? new List<GcdSeries>();
        }

        public GcdSeries FindSeriesByAnilistId(int anilistId, LibraryType library)
        {
            return Query(conn =>
            {
                var ids = conn.Query<int>("SELECT gcd_series_id FROM series WHERE anilist_id = @a", new { a = anilistId }).ToList();

                if (ids.Count == 0)
                {
                    return null;
                }

                try
                {
                    var workIds = conn.Query<int>(
                        "SELECT gcd_series_id FROM series WHERE tome_work_id IN (SELECT tome_work_id FROM series WHERE anilist_id = @a AND tome_work_id IS NOT NULL)",
                        new { a = anilistId }).ToList();
                    ids = ids.Union(workIds).ToList();
                }
                catch (SQLiteException)
                {
                    // no tome_work_id column: the matched rows alone
                }

                var inList = string.Join(",", ids);
                var rows = conn.Query<GcdSeries>(SeriesSelect + " WHERE gcd_series_id IN (" + inList + ")").ToList();
                LoadOptionalColumns(conn, rows, inList);

                var wantNovel = library == LibraryType.LightNovel;

                return rows
                    .Where(s => wantNovel ? IsLightNovel(s.Medium) : true)
                    .OrderByDescending(s => s.AnilistId == anilistId)
                    .ThenByDescending(s => s.Language == "en")
                    .ThenByDescending(s => s.IsMain)
                    .ThenBy(s => IsLightNovel(s.Medium))
                    .ThenBy(s => s.IsOmnibus)
                    .ThenByDescending(s => s.DatedCount ?? 0)
                    .ThenByDescending(s => s.VolumeCount)
                    .ThenBy(s => s.GcdSeriesId)
                    .FirstOrDefault();
            });
        }

        public List<GcdAlias> GetAliasRows(int gcdSeriesId)
        {
            return Query(conn =>
            {
                try
                {
                    return conn.Query<GcdAlias>(
                        "SELECT alias AS Alias, language AS Language, kind AS Kind FROM series_alias WHERE gcd_series_id = @id ORDER BY rowid",
                        new { id = gcdSeriesId }).ToList();
                }
                catch (SQLiteException)
                {
                    return conn.Query<GcdAlias>(
                        "SELECT alias AS Alias FROM series_alias WHERE gcd_series_id = @id ORDER BY rowid",
                        new { id = gcdSeriesId }).ToList();
                }
            })?.Where(a => a.Alias.IsNotNullOrWhiteSpace()).ToList() ?? new List<GcdAlias>();
        }

        public Dictionary<string, int> Markets()
        {
            return Query(conn =>
            {
                try
                {
                    var json = conn.QueryFirstOrDefault<string>("SELECT value FROM meta WHERE key = 'markets'");

                    if (json.IsNotNullOrWhiteSpace())
                    {
                        return JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                    }
                }
                catch (Exception)
                {
                    // no meta table, or a malformed value: count instead
                }

                return conn.Query<MarketRow>(
                        "SELECT language AS Language, COUNT(*) AS Lines FROM series WHERE language IS NOT NULL GROUP BY language")
                    .ToDictionary(r => r.Language, r => r.Lines);
            }) ?? new Dictionary<string, int>();
        }

        public List<GcdVolume> GetVolumes(int gcdSeriesId)
        {
            return GetVolumes(gcdSeriesId, false);
        }

        public List<GcdVolume> GetVolumes(int gcdSeriesId, bool includeEditionDates)
        {
            return Query(conn =>
            {
                var volumes = conn.Query<VolumeRow>(
                        @"SELECT volume_number AS VolumeNumber, title AS Title, release_date AS ReleaseDate,
                                 isbn13 AS Isbn13, isbn10 AS Isbn10, page_count AS PageCount,
                                 composition AS Composition
                          FROM volumes WHERE gcd_series_id = @id ORDER BY volume_number",
                        new { id = gcdSeriesId })
                    .Select(r => r.ToVolume())
                    .ToList();

                // Optional columns (schema_version 2). Read separately so a v1 artifact, which
                // does not have them, keeps working: a missing column there is expected.
                try
                {
                    var covers = conn.Query<CoverRow>(
                            @"SELECT volume_number AS VolumeNumber, cover_url AS CoverUrl,
                                     cover_source AS CoverSource
                              FROM volumes WHERE gcd_series_id = @id AND cover_url IS NOT NULL",
                            new { id = gcdSeriesId })
                        .ToDictionary(c => c.VolumeNumber);

                    foreach (var v in volumes)
                    {
                        if (covers.TryGetValue(v.VolumeNumber, out var c))
                        {
                            v.CoverUrl = c.CoverUrl;
                            v.CoverSource = c.CoverSource;
                        }
                    }
                }
                catch (SQLiteException)
                {
                    // schema_version 1 artifact: no cover columns.
                }

                // Preferred Edition (2026-09-24, D6): the coarse date and its precision (every OpenTome
                // artifact), then the milestone type (the DNB round) -- guarded separately. Only when
                // asked: the English path reads volumes with no new SQL (Global Constraints, ruling S5).
                if (includeEditionDates)
                {
                    try
                    {
                        var dates = conn.Query<DateRow>(
                                @"SELECT volume_number AS VolumeNumber, release_date_raw AS ReleaseDateRaw,
                                         release_date_precision AS ReleaseDatePrecision
                                  FROM volumes WHERE gcd_series_id = @id",
                                new { id = gcdSeriesId })
                            .GroupBy(d => d.VolumeNumber)
                            .ToDictionary(g => g.Key, g => g.First());

                        foreach (var v in volumes)
                        {
                            if (dates.TryGetValue(v.VolumeNumber, out var d))
                            {
                                v.ReleaseDateRaw = d.ReleaseDateRaw;
                                v.ReleaseDatePrecision = d.ReleaseDatePrecision;
                            }
                        }
                    }
                    catch (SQLiteException)
                    {
                        // no release_date_raw / release_date_precision columns
                    }

                    try
                    {
                        var types = conn.Query<DateTypeRow>(
                                "SELECT volume_number AS VolumeNumber, release_date_type AS ReleaseDateType FROM volumes WHERE gcd_series_id = @id",
                                new { id = gcdSeriesId })
                            .GroupBy(d => d.VolumeNumber)
                            .ToDictionary(g => g.Key, g => g.First());

                        foreach (var v in volumes)
                        {
                            if (types.TryGetValue(v.VolumeNumber, out var t))
                            {
                                v.ReleaseDateType = t.ReleaseDateType;
                            }
                        }
                    }
                    catch (SQLiteException)
                    {
                        // no release_date_type column (OpenTome before the DNB round)
                    }
                }

                return volumes;
            }) ?? new List<GcdVolume>();
        }

        public string LocalDumpVersion()
        {
            return Query(conn =>
            {
                try
                {
                    return conn.QueryFirstOrDefault<string>(
                        "SELECT value FROM meta WHERE key = 'gcd_dump' LIMIT 1");
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }

        public GcdArtifactInfo ArtifactInfo()
        {
            var path = ResolvePath();
            if (path == null)
            {
                return new GcdArtifactInfo { Available = false };
            }

            var info = Query(conn =>
            {
                var meta = conn.Query<MetaRow>("SELECT key AS Key, value AS Value FROM meta")
                    .GroupBy(r => r.Key)
                    .ToDictionary(g => g.Key, g => g.First().Value);

                string Get(params string[] keys)
                {
                    foreach (var k in keys)
                    {
                        if (meta.TryGetValue(k, out var v) && v.IsNotNullOrWhiteSpace())
                        {
                            return v;
                        }
                    }

                    return null;
                }

                return new GcdArtifactInfo
                {
                    Available = true,
                    Path = path,
                    Version = Get("gcd_dump"),
                    Generator = Get("generator") ?? (Get("source") == "GCD" ? "gcd" : null),
                    Source = Get("source"),
                    Attribution = Get("attribution"),
                    Licence = Get("licence", "license"),
                    GeneratedAt = Get("generated_at"),
                    SchemaVersion = Get("schema_version"),
                    SeriesCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM series"),
                    VolumeCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM volumes")
                };
            });

            return info ?? new GcdArtifactInfo { Available = true, Path = path };
        }

        public void Reload()
        {
            lock (_resolveLock)
            {
                _resolved = false;
                _dbPath = null;
            }
        }

        public string OverrideArtifactPath =>
            Path.Combine(_appFolderInfo.AppDataFolder, "metadata", ArtifactName);

        private T Query<T>(Func<SQLiteConnection, T> query)
        {
            var path = ResolvePath();
            if (path == null)
            {
                return default;
            }

            try
            {
                var connectionString = new SQLiteConnectionStringBuilder
                {
                    DataSource = path,
                    ReadOnly = true,
                    FailIfMissing = true
                }.ConnectionString;

                using (var conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    return query(conn);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "GCD metadata query failed against {0}", path);
                return default;
            }
        }

        // Resolve once: a user-dropped override in {AppData}/metadata/*.sqlite beats the baked-in
        // artifact next to the binary. null => no artifact present (fall back to live sources).
        private string ResolvePath()
        {
            if (_resolved)
            {
                return _dbPath;
            }

            lock (_resolveLock)
            {
                if (_resolved)
                {
                    return _dbPath;
                }

                _dbPath = FindArtifact();
                _resolved = true;

                if (_dbPath != null)
                {
                    _logger.Info("GCD metadata artifact: {0}", _dbPath);
                }
                else
                {
                    _logger.Debug("No GCD metadata artifact found; using live metadata sources only");
                }

                return _dbPath;
            }
        }

        private string FindArtifact()
        {
            var overrideDir = Path.Combine(_appFolderInfo.AppDataFolder, "metadata");
            if (Directory.Exists(overrideDir))
            {
                // The canonical name wins. This used to take the alphabetically first
                // *.sqlite, so a stray backup.sqlite or custom.sqlite ("b"/"c" < "m")
                // permanently shadowed every artifact the updater downloaded -- silently,
                // because a stale artifact still answers queries perfectly well.
                var canonical = Path.Combine(overrideDir, ArtifactName);
                if (File.Exists(canonical))
                {
                    return canonical;
                }

                var other = Directory.GetFiles(overrideDir, "*.sqlite").OrderBy(x => x).FirstOrDefault();
                if (other != null)
                {
                    _logger.Warn(
                        "No {0} in {1}; falling back to {2}. The updater writes {0}, so this file " +
                        "will not receive updates.",
                        ArtifactName, overrideDir, Path.GetFileName(other));
                    return other;
                }
            }

            var baked = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "metadata", ArtifactName);
            return File.Exists(baked) ? baked : null;
        }

        // Mirror of the ingest's _norm (tools/metadata-ingest/build_metadata_db.py): lowercase,
        // collapse every run of non-[a-z0-9] to a single space, trim. Must stay in sync so a runtime
        // query normalizes to the same string the artifact stored as a normalized alias.
        public static string Normalize(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length);
            var pendingSpace = false;
            foreach (var ch in value.ToLowerInvariant())
            {
                if (ch < 128 && char.IsLetterOrDigit(ch))
                {
                    if (pendingSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                    }

                    sb.Append(ch);
                    pendingSpace = false;
                }
                else
                {
                    pendingSpace = true;
                }
            }

            return sb.ToString();
        }

        private class VolumeRow
        {
            public int VolumeNumber { get; set; }
            public string Title { get; set; }
            public string ReleaseDate { get; set; }
            public string Isbn13 { get; set; }
            public string Isbn10 { get; set; }
            public int? PageCount { get; set; }
            public string Composition { get; set; }

            public GcdVolume ToVolume()
            {
                List<int> composition = null;
                if (!Composition.IsNullOrWhiteSpace())
                {
                    try
                    {
                        composition = JsonConvert.DeserializeObject<List<int>>(Composition);
                    }
                    catch (Exception)
                    {
                        composition = null;
                    }
                }

                return new GcdVolume
                {
                    VolumeNumber = VolumeNumber,
                    Title = Title,
                    ReleaseDate = ReleaseDate,
                    Isbn13 = Isbn13,
                    Isbn10 = Isbn10,
                    PageCount = PageCount,
                    Composition = composition
                };
            }
        }
    }
}
