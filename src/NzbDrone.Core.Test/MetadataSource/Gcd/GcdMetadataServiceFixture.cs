using System.Data.SQLite;
using System.IO;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Gcd
{
    [TestFixture]
    public class GcdMetadataServiceFixture : CoreTest<GcdMetadataService>
    {
        private string _appData;

        [SetUp]
        public void Setup()
        {
            _appData = Path.Combine(TempFolder, "appdata");

            // Mocker is lazy and its AutoMoqer constructor is what registers the sqlite3 →
            // libsqlite3.so.0 resolver, so it must be touched before the first SQLiteConnection or
            // this fixture fails in Setup whenever it is the first one to run in the process.
            Mocker.GetMock<IAppFolderInfo>()
                .SetupGet(c => c.AppDataFolder)
                .Returns(_appData);

            var metaDir = Path.Combine(_appData, "metadata");
            Directory.CreateDirectory(metaDir);
            CreateArtifact(Path.Combine(metaDir, GcdMetadataService.ArtifactName));
        }

        [Test]
        public void available_when_artifact_present()
        {
            Subject.Available.Should().BeTrue();
        }

        [Test]
        public void find_by_title_prefers_standard_english_line_over_omnibus()
        {
            // "Naruto" is an alias of both the 72-vol singles (25323) and the 24-vol 3-in-1 (76512).
            var series = Subject.FindSeriesByTitle("Naruto");

            series.Should().NotBeNull();
            series.GcdSeriesId.Should().Be(25323);
            series.IsOmnibus.Should().BeFalse();
            series.VolumeCount.Should().Be(72);
        }

        [Test]
        public void find_by_title_matches_alias_case_insensitively()
        {
            Subject.FindSeriesByTitle("chainsaw man").GcdSeriesId.Should().Be(154385);
        }

        [Test]
        public void find_by_title_matches_via_normalized_alias()
        {
            // A folder-derived query ("Demon Slayer Kimetsu No Yaiba") resolves to the punctuated
            // GCD name ("Demon Slayer: Kimetsu no Yaiba") via the stored normalized alias.
            Subject.FindSeriesByTitle("Demon Slayer Kimetsu No Yaiba").GcdSeriesId.Should().Be(99);
        }

        [Test]
        public void find_by_id_returns_omnibus_with_original_mapping()
        {
            var omnibus = Subject.FindSeriesById(76512);

            omnibus.Should().NotBeNull();
            omnibus.IsOmnibus.Should().BeTrue();
            omnibus.VolumeCount.Should().Be(24);
            omnibus.OrigSeriesId.Should().Be(25323);
        }

        [Test]
        public void get_volumes_parses_omnibus_composition()
        {
            var volumes = Subject.GetVolumes(76512);

            volumes.Should().HaveCount(2);
            volumes.Find(v => v.VolumeNumber == 1).Composition.Should().Equal(1, 2, 3);
            volumes.Find(v => v.VolumeNumber == 24).Composition.Should().Equal(70, 71, 72);
        }

        // Alias retry (D3): the line's series_alias rows, raw and normalised forms alike, in
        // stored order. The provider hands them to the AniList strict pass after the name.
        [Test]
        public void get_aliases_returns_the_line_s_alias_rows_in_stored_order()
        {
            Subject.GetAliases(99).Should().Equal("Demon Slayer: Kimetsu no Yaiba", "demon slayer kimetsu no yaiba");
        }

        [Test]
        public void get_aliases_is_empty_for_an_unknown_line()
        {
            Subject.GetAliases(424242).Should().BeEmpty();
        }

        [Test]
        public void get_volumes_carries_per_volume_enrichment()
        {
            var first = Subject.GetVolumes(154385).Find(v => v.VolumeNumber == 1);

            first.Should().NotBeNull();
            first.ReleaseDate.Should().Be("2020-10-06");
            first.Isbn13.Should().Be("9781974709939");
            first.PageCount.Should().Be(192);
            first.Composition.Should().BeNull();
        }

        [Test]
        public void find_by_title_prefers_exactly_named_line_over_alias_only_spinoff()
        {
            // "Attack on Titan" is an alias of the main line AND of the "Before the Fall" spin-off
            // (a work's titles fan out to its lines). The line NAMED "Attack on Titan" must win even
            // though the spin-off is not flagged omnibus and the main line is.
            var series = Subject.FindSeriesByTitle("attack on titan");

            series.Should().NotBeNull();
            series.GcdSeriesId.Should().Be(1001);
            series.VolumeCount.Should().Be(34);
        }

        [Test]
        public void find_by_title_prefers_manga_over_light_novel_of_same_name()
        {
            // Two English lines both named "Tokyo Ghoul": the 14-volume manga and a 3-volume light
            // novel. Medium decides, not volume count and not the omnibus flag.
            var series = Subject.FindSeriesByTitle("Tokyo Ghoul");

            series.Should().NotBeNull();
            series.GcdSeriesId.Should().Be(2001);
            series.Medium.Should().Be("manga");
        }

        // One copy each (2026-09-20): the artifact's author column (the work's author) rides on the
        // line; the writer ladder takes it for a light novel. Older artifacts have no column.
        [Test]
        public void find_by_title_loads_author_when_the_column_exists()
        {
            var novel = Subject.FindSeriesByTitle("Tokyo Ghoul", LibraryType.LightNovel);

            novel.GcdSeriesId.Should().Be(2002);
            novel.Author.Should().Be("Shin Towada");
            Subject.FindSeriesByTitle("Tokyo Ghoul").Author.Should().BeNull();
        }

        [Test]
        public void find_by_title_leaves_author_null_on_an_older_artifact()
        {
            var v1 = Path.Combine(TempFolder, "v1appdata");
            var metaDir = Path.Combine(v1, "metadata");
            Directory.CreateDirectory(metaDir);
            CreateArtifact(Path.Combine(metaDir, GcdMetadataService.ArtifactName), optionalColumns: false);
            Mocker.GetMock<IAppFolderInfo>()
                .SetupGet(c => c.AppDataFolder)
                .Returns(v1);

            var series = Subject.FindSeriesByTitle("Tokyo Ghoul");

            series.Should().NotBeNull();
            series.Author.Should().BeNull();
        }

        [Test]
        public void find_by_title_returns_null_when_only_non_english_lines_match()
        {
            // A Japanese-only line must not be handed to an app that labels every edition English.
            Subject.FindSeriesByTitle("Kingdom").Should().BeNull();
        }

        [Test]
        public void find_by_subtitle_resolves_franchise_prefixed_folder()
        {
            var full = "Re:ZERO -Starting Life in Another World-, Chapter 1: A Day in the Capital";

            Subject.FindSeriesByTitle(full).Should().BeNull();
            Subject.FindSeriesBySubtitle(full).GcdSeriesId.Should().Be(3001);
        }

        [Test]
        public void find_by_subtitle_ignores_short_generic_tails()
        {
            // "Side Story" / "Extra" must never bind a franchise folder to an unrelated line.
            Subject.FindSeriesBySubtitle("Some Franchise: Extra").Should().BeNull();
            Subject.FindSeriesBySubtitle("Some Franchise, Side Story").Should().BeNull();
        }

        [Test]
        public void get_volumes_carries_isbn_keyed_cover_when_the_artifact_has_one()
        {
            var volumes = Subject.GetVolumes(154385);

            volumes.Find(v => v.VolumeNumber == 2).CoverUrl.Should().Be("https://covers.openlibrary.org/b/id/1-L.jpg");
            volumes.Find(v => v.VolumeNumber == 2).CoverSource.Should().Be("openlibrary");
            volumes.Find(v => v.VolumeNumber == 1).CoverUrl.Should().BeNull();
        }

        [Test]
        public void works_with_a_schema_v1_artifact_without_optional_columns()
        {
            var v1 = Path.Combine(TempFolder, "v1appdata");
            var metaDir = Path.Combine(v1, "metadata");
            Directory.CreateDirectory(metaDir);
            CreateArtifact(Path.Combine(metaDir, GcdMetadataService.ArtifactName), optionalColumns: false);
            Mocker.GetMock<IAppFolderInfo>()
                .SetupGet(c => c.AppDataFolder)
                .Returns(v1);

            var series = Subject.FindSeriesByTitle("Naruto");

            series.Should().NotBeNull();
            series.GcdSeriesId.Should().Be(25323);
            series.Medium.Should().BeNull();
            series.DatedCount.Should().BeNull();

            // v1 has no cover columns: volumes still load, covers are simply absent.
            var first = Subject.GetVolumes(154385).Find(v => v.VolumeNumber == 1);
            first.Should().NotBeNull();
            first.CoverUrl.Should().BeNull();
        }

        // Display fallback (OpenTome 2026-09-24): display_anilist_id / display_anilist_via are read
        // when the artifact has them and are null (no crash) when it predates them.
        private string GivenDisplayArtifact()
        {
            var appData = Path.Combine(TempFolder, "displayappdata");
            var metaDir = Path.Combine(appData, "metadata");
            Directory.CreateDirectory(metaDir);
            CreateArtifact(Path.Combine(metaDir, GcdMetadataService.ArtifactName), displayColumns: true);

            return appData;
        }

        private void GivenAppData(string appData)
        {
            Mocker.GetMock<IAppFolderInfo>()
                .SetupGet(c => c.AppDataFolder)
                .Returns(appData);
        }

        [Test]
        public void display_anilist_id_is_read_when_the_artifact_has_the_columns()
        {
            GivenAppData(GivenDisplayArtifact());

            var arc = Subject.FindSeriesBySubtitle("Re:ZERO -Starting Life in Another World-, Chapter 1: A Day in the Capital");
            arc.GcdSeriesId.Should().Be(3001);
            arc.DisplayAnilistId.Should().Be(85737);
            arc.DisplayAnilistVia.Should().Be("parent");

            var byId = Subject.FindSeriesById(3001);
            byId.DisplayAnilistId.Should().Be(85737);
            byId.DisplayAnilistVia.Should().Be("parent");

            // A line OpenTome gave no display id reads null.
            Subject.FindSeriesByTitle("Naruto").DisplayAnilistId.Should().BeNull();
        }

        [Test]
        public void display_anilist_id_is_null_on_an_artifact_without_the_columns()
        {
            var series = Subject.FindSeriesByTitle("Naruto");

            series.Should().NotBeNull();
            series.GcdSeriesId.Should().Be(25323);
            series.DisplayAnilistId.Should().BeNull();
            series.DisplayAnilistVia.Should().BeNull();
            Subject.FindSeriesById(3001).DisplayAnilistId.Should().BeNull();
            Subject.GetChildren(25323).Should().BeEmpty();
        }

        [Test]
        public void display_anilist_id_is_null_on_a_schema_v1_artifact()
        {
            var v1 = Path.Combine(TempFolder, "v1appdata");
            var metaDir = Path.Combine(v1, "metadata");
            Directory.CreateDirectory(metaDir);
            CreateArtifact(Path.Combine(metaDir, GcdMetadataService.ArtifactName), optionalColumns: false);
            GivenAppData(v1);

            var series = Subject.FindSeriesByTitle("Naruto");

            series.Should().NotBeNull();
            series.DisplayAnilistId.Should().BeNull();
        }

        [Test]
        public void a_reload_reads_the_new_artifacts_display_columns()
        {
            // First load: the fixture's artifact, which predates the columns.
            Subject.FindSeriesById(3001).DisplayAnilistId.Should().BeNull();

            // The updater lands a newer artifact and reloads: the next query reads the columns.
            GivenAppData(GivenDisplayArtifact());
            Subject.Reload();

            Subject.FindSeriesById(3001).DisplayAnilistId.Should().Be(85737);
        }

        [Test]
        public void an_older_artifact_copied_over_the_file_without_a_reload_still_answers()
        {
            // A hand revert: the display-shape artifact is loaded, then an older one replaces the
            // file in place and nobody calls Reload. The display columns are an optional read, so
            // the older file still answers every lookup (with null display ids).
            var appData = GivenDisplayArtifact();
            GivenAppData(appData);
            Subject.FindSeriesById(3001).DisplayAnilistId.Should().Be(85737);

            var file = Path.Combine(appData, "metadata", GcdMetadataService.ArtifactName);
            SQLiteConnection.ClearAllPools();
            File.Delete(file);
            CreateArtifact(file);
            File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(5));

            var naruto = Subject.FindSeriesByTitle("Naruto");
            naruto.Should().NotBeNull();
            naruto.DisplayAnilistId.Should().BeNull();

            var arc = Subject.FindSeriesById(3001);
            arc.Should().NotBeNull();
            arc.DisplayAnilistId.Should().BeNull();
        }

        [Test]
        public void degrades_cleanly_when_no_artifact()
        {
            var empty = Path.Combine(TempFolder, "empty");
            Directory.CreateDirectory(empty);
            Mocker.GetMock<IAppFolderInfo>()
                .SetupGet(c => c.AppDataFolder)
                .Returns(empty);

            Subject.Available.Should().BeFalse();
            Subject.FindSeriesById(76512).Should().BeNull();
            Subject.FindSeriesByTitle("Naruto").Should().BeNull();
            Subject.GetVolumes(76512).Should().BeEmpty();
        }

        private static void CreateArtifact(string path, bool optionalColumns = true, bool displayColumns = false)
        {
            var connectionString = new SQLiteConnectionStringBuilder { DataSource = path }.ConnectionString;
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                var extraCols = optionalColumns ? ", medium TEXT, dated_count INTEGER, is_main INTEGER, author TEXT" : string.Empty;
                extraCols += displayColumns ? ", display_anilist_id INTEGER, display_anilist_via TEXT" : string.Empty;
                var volumeExtraCols = optionalColumns ? ", cover_url TEXT, cover_source TEXT" : string.Empty;
                conn.Execute(@"
                    CREATE TABLE series (
                        gcd_series_id INTEGER PRIMARY KEY, name TEXT, year_began INTEGER,
                        publisher TEXT, language TEXT, country TEXT, is_omnibus INTEGER,
                        volume_count INTEGER, status TEXT, orig_series_id INTEGER,
                        anilist_id INTEGER, mangaupdates_id INTEGER, mangadex_id TEXT" + extraCols + @");
                    CREATE TABLE volumes (
                        id INTEGER PRIMARY KEY, gcd_series_id INTEGER, volume_number INTEGER,
                        title TEXT, release_date TEXT, isbn13 TEXT, isbn10 TEXT,
                        page_count INTEGER, composition TEXT" + volumeExtraCols + @");
                    CREATE TABLE series_alias (gcd_series_id INTEGER, alias TEXT);");

                if (optionalColumns)
                {
                    conn.Execute(
                        @"INSERT INTO volumes (gcd_series_id, volume_number, cover_url, cover_source)
                          VALUES (154385, 2, 'https://covers.openlibrary.org/b/id/1-L.jpg', 'openlibrary')");
                }

                var rows = new[]
                {
                    // id, name, language, omnibus, count, orig, medium, dated, main, author (2026-09-20 artifacts: the work's author)
                    new { Id = 25323, Name = "Naruto", Lang = "en", Omni = 0, Vc = 72, Orig = (int?)null, Medium = "manga", Dated = 72, Main = 1, Author = (string)null },
                    new { Id = 76512, Name = "Naruto (3-in-1 Edition)", Lang = "en", Omni = 1, Vc = 24, Orig = (int?)25323, Medium = "manga", Dated = 24, Main = 0, Author = (string)null },
                    new { Id = 154385, Name = "Chainsaw Man", Lang = "en", Omni = 0, Vc = 24, Orig = (int?)null, Medium = "manga", Dated = 22, Main = 1, Author = (string)null },
                    new { Id = 99, Name = "Demon Slayer: Kimetsu no Yaiba", Lang = "en", Omni = 0, Vc = 23, Orig = (int?)null, Medium = "manga", Dated = 23, Main = 1, Author = (string)null },

                    // main line flagged omnibus (a real 2-in-1 run inside it) vs an un-flagged spin-off
                    new { Id = 1001, Name = "Attack on Titan", Lang = "en", Omni = 1, Vc = 34, Orig = (int?)null, Medium = "manga", Dated = 34, Main = 1, Author = (string)null },
                    new { Id = 1002, Name = "Attack on Titan (Before the Fall)", Lang = "en", Omni = 0, Vc = 17, Orig = (int?)null, Medium = "manga", Dated = 17, Main = 0, Author = (string)null },
                    new { Id = 1003, Name = "Attack on Titan", Lang = "ja", Omni = 0, Vc = 34, Orig = (int?)null, Medium = "manga", Dated = 34, Main = 1, Author = (string)null },

                    // same name, manga vs light novel
                    new { Id = 2001, Name = "Tokyo Ghoul", Lang = "en", Omni = 1, Vc = 14, Orig = (int?)null, Medium = "manga", Dated = 14, Main = 1, Author = (string)null },
                    new { Id = 2002, Name = "Tokyo Ghoul", Lang = "en", Omni = 0, Vc = 3, Orig = (int?)null, Medium = "light_novel", Dated = 3, Main = 1, Author = (string)"Shin Towada" },
                    new { Id = 2003, Name = "Tokyo Ghoul:re", Lang = "en", Omni = 0, Vc = 16, Orig = (int?)null, Medium = "manga", Dated = 16, Main = 0, Author = (string)null },

                    // Japanese-only work
                    new { Id = 4001, Name = "Kingdom", Lang = "ja", Omni = 0, Vc = 70, Orig = (int?)null, Medium = "manga", Dated = 70, Main = 1, Author = (string)null },

                    // franchise arc reachable only by its arc title
                    new { Id = 3001, Name = "Re:Zero (A Day in the Capital)", Lang = "en", Omni = 0, Vc = 2, Orig = (int?)null, Medium = "manga", Dated = 2, Main = 0, Author = (string)null },
                };

                if (optionalColumns)
                {
                    conn.Execute(
                        @"INSERT INTO series (gcd_series_id, name, language, is_omnibus, volume_count, status, orig_series_id, medium, dated_count, is_main, author)
                          VALUES (@Id, @Name, @Lang, @Omni, @Vc, 'completed', @Orig, @Medium, @Dated, @Main, @Author)", rows);
                }
                else
                {
                    conn.Execute(
                        @"INSERT INTO series (gcd_series_id, name, language, is_omnibus, volume_count, status, orig_series_id)
                          VALUES (@Id, @Name, @Lang, @Omni, @Vc, 'completed', @Orig)", rows);
                }

                if (displayColumns)
                {
                    // The Re:Zero arc line has no AniList entry of its own; its parent work's stands in.
                    conn.Execute("UPDATE series SET display_anilist_id = 85737, display_anilist_via = 'parent' WHERE gcd_series_id = 3001");
                }

                conn.Execute(
                    @"INSERT INTO volumes (gcd_series_id, volume_number, title, release_date, isbn13, page_count, composition)
                      VALUES (@Series, @Num, @Title, @Date, @Isbn, @Pages, @Comp)",
                    new[]
                    {
                        new { Series = 76512, Num = 1, Title = "Naruto 3-in-1 Vol. 1", Date = (string)null, Isbn = (string)null, Pages = (int?)null, Comp = "[1,2,3]" },
                        new { Series = 76512, Num = 24, Title = "Naruto 3-in-1 Vol. 24", Date = (string)null, Isbn = (string)null, Pages = (int?)null, Comp = "[70,71,72]" },
                        new { Series = 154385, Num = 1, Title = "Chainsaw Man, Vol. 1", Date = "2020-10-06", Isbn = "9781974709939", Pages = (int?)192, Comp = (string)null }
                    });

                conn.Execute(
                    "INSERT INTO series_alias (gcd_series_id, alias) VALUES (@Id, @Alias)",
                    new[]
                    {
                        new { Id = 25323, Alias = "Naruto" },
                        new { Id = 76512, Alias = "Naruto" },
                        new { Id = 154385, Alias = "Chainsaw Man" },
                        new { Id = 99, Alias = "Demon Slayer: Kimetsu no Yaiba" },
                        new { Id = 99, Alias = "demon slayer kimetsu no yaiba" },
                        new { Id = 1001, Alias = "attack on titan" },
                        new { Id = 1002, Alias = "attack on titan" },
                        new { Id = 1002, Alias = "attack on titan before the fall" },
                        new { Id = 1003, Alias = "attack on titan" },
                        new { Id = 2001, Alias = "tokyo ghoul" },
                        new { Id = 2002, Alias = "tokyo ghoul" },
                        new { Id = 2003, Alias = "tokyo ghoul re" },
                        new { Id = 4001, Alias = "kingdom" },
                        new { Id = 3001, Alias = "a day in the capital" },
                        new { Id = 3001, Alias = "re zero a day in the capital" }
                    });
            }
        }
    }
}
