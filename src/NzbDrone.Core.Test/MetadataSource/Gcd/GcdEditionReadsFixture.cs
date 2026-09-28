using System.Collections.Generic;
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
    // Preferred Edition (2026-09-24): the edition reads against a v0 artifact (tome ids, local names,
    // alias language/kind, date precision, markets, redirects) and against an older one (none of it).
    [TestFixture]
    public class GcdEditionReadsFixture : CoreTest<GcdMetadataService>
    {
        private void GivenArtifact(bool v0)
        {
            var appData = Path.Combine(TempFolder, v0 ? "v0" : "old");
            var metaDir = Path.Combine(appData, "metadata");
            Directory.CreateDirectory(metaDir);

            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.AppDataFolder).Returns(appData);

            var cs = new SQLiteConnectionStringBuilder { DataSource = Path.Combine(metaDir, GcdMetadataService.ArtifactName) }.ConnectionString;
            using var conn = new SQLiteConnection(cs);
            conn.Open();

            var seriesCols = v0 ? ", tome_id TEXT, tome_work_id TEXT, local_name TEXT" : string.Empty;
            var volumeCols = v0 ? ", release_date_raw TEXT, release_date_precision TEXT, release_date_type TEXT" : string.Empty;
            var aliasCols = v0 ? ", language TEXT, kind TEXT" : string.Empty;

            conn.Execute(@"
                CREATE TABLE series (gcd_series_id INTEGER PRIMARY KEY, name TEXT, year_began INTEGER, publisher TEXT,
                    language TEXT, country TEXT, is_omnibus INTEGER, volume_count INTEGER, status TEXT, orig_series_id INTEGER,
                    anilist_id INTEGER, mangaupdates_id INTEGER, mangadex_id TEXT, medium TEXT, dated_count INTEGER,
                    is_main INTEGER, author TEXT" + seriesCols + @");
                CREATE TABLE volumes (id INTEGER PRIMARY KEY, gcd_series_id INTEGER, volume_number INTEGER, title TEXT,
                    release_date TEXT, isbn13 TEXT, isbn10 TEXT, page_count INTEGER, composition TEXT, cover_url TEXT,
                    cover_source TEXT" + volumeCols + @");
                CREATE TABLE series_alias (gcd_series_id INTEGER, alias TEXT" + aliasCols + @");
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);");

            conn.Execute(@"INSERT INTO series (gcd_series_id, name, language, is_omnibus, volume_count, status, orig_series_id, medium, dated_count, is_main)
                           VALUES (1003, 'Attack on Titan', 'ja', 0, 34, 'completed', NULL, 'manga', 34, 1),
                                  (1001, 'Attack on Titan', 'en', 0, 34, 'completed', 1003, 'manga', 34, 1),
                                  (1004, 'Attack on Titan', 'fr', 0, 20, 'stalled', 1003, 'manga', 20, 1),
                                  (1005, 'Attack on Titan', 'de', 0, 34, 'completed', 1003, 'manga', 30, 1)");
            conn.Execute("INSERT INTO series_alias (gcd_series_id, alias) VALUES (1004, 'Attack on Titan'), (1004, 'L''Attaque des Titans'), (1001, 'attack on titan')");
            conn.Execute("INSERT INTO volumes (gcd_series_id, volume_number, release_date, isbn13) VALUES (1005, 1, NULL, '9783551799111'), (1005, 2, '2014-05-02', '9783551799128')");

            if (v0)
            {
                conn.Execute(@"UPDATE series SET tome_work_id = 'w_aot', tome_id = CASE gcd_series_id
                                 WHEN 1003 THEN 'rl_ja' WHEN 1001 THEN 'rl_en' WHEN 1004 THEN 'rl_fr' ELSE 'rl_de' END");
                conn.Execute("UPDATE series SET local_name = 'L''Attaque des Titans' WHERE gcd_series_id = 1004");
                conn.Execute("UPDATE series SET local_name = 'Attack on Titan' WHERE gcd_series_id = 1005");
                conn.Execute("UPDATE series_alias SET language = 'fr', kind = 'official' WHERE alias = 'L''Attaque des Titans'");
                conn.Execute("UPDATE series_alias SET kind = 'line' WHERE alias = 'Attack on Titan'");
                conn.Execute("UPDATE volumes SET release_date_raw = '2013', release_date_precision = 'year', release_date_type = 'published' WHERE volume_number = 1");
                conn.Execute("INSERT INTO meta VALUES ('markets', '{\"de\": 1, \"en\": 1, \"fr\": 1, \"ja\": 1}')");

                // The artifact's own id_redirect shape (OpenTome export/to_mangarr.py). A retired volume id
                // also redirects to its line there; a series lookup must not follow it.
                conn.Execute(@"CREATE TABLE id_redirect (old_tome_id TEXT PRIMARY KEY, new_tome_id TEXT NOT NULL, entity TEXT NOT NULL,
                                   reason TEXT, old_series_id INTEGER, new_series_id INTEGER);
                               INSERT INTO id_redirect VALUES ('rl_fr_old', 'rl_fr', 'release_line', 'correction', NULL, 1004),
                                                              ('vol_fr_old', 'rl_fr', 'volume', 'retired', NULL, 1004)");
            }
        }

        [Test]
        public void a_v0_artifact_carries_tome_ids_and_the_local_name()
        {
            GivenArtifact(v0: true);

            var fr = Subject.FindSeriesById(1004);

            fr.TomeId.Should().Be("rl_fr");
            fr.TomeWorkId.Should().Be("w_aot");
            fr.LocalName.Should().Be("L'Attaque des Titans");
        }

        [Test]
        public void find_by_tome_id_follows_a_redirect()
        {
            GivenArtifact(v0: true);

            Subject.FindSeriesByTomeId("rl_fr").GcdSeriesId.Should().Be(1004);
            Subject.FindSeriesByTomeId("rl_fr_old").GcdSeriesId.Should().Be(1004);
            Subject.FindSeriesByTomeId("rl_gone").Should().BeNull();
        }

        [Test]
        public void find_by_tome_id_follows_only_release_line_redirects()
        {
            GivenArtifact(v0: true);

            Subject.FindSeriesByTomeId("vol_fr_old").Should().BeNull();
        }

        [Test]
        public void work_lines_are_every_language_of_the_work()
        {
            GivenArtifact(v0: true);

            Subject.GetWorkLines("w_aot").Should().HaveCount(4);
            Subject.GetWorkLines("w_unknown").Should().BeEmpty();
        }

        [Test]
        public void a_title_in_chosen_languages_ranks_by_chain_order()
        {
            GivenArtifact(v0: true);

            Subject.FindSeriesByTitle("Attack on Titan", LibraryType.Manga, new[] { "de", "fr" }).GcdSeriesId.Should().Be(1005);
            Subject.FindSeriesByTitle("Attack on Titan", LibraryType.Manga, new[] { "fr", "de" }).GcdSeriesId.Should().Be(1004);
            Subject.FindSeriesByTitle("Attack on Titan", LibraryType.Manga, new[] { "it" }).Should().BeNull();
        }

        [Test]
        public void the_english_title_lookup_is_unchanged()
        {
            GivenArtifact(v0: true);

            Subject.FindSeriesByTitle("Attack on Titan", LibraryType.Manga).GcdSeriesId.Should().Be(1001);
        }

        [Test]
        public void alias_rows_carry_language_and_kind()
        {
            GivenArtifact(v0: true);

            var rows = Subject.GetAliasRows(1004);

            rows.Should().ContainEquivalentOf(new GcdAlias { Alias = "L'Attaque des Titans", Language = "fr", Kind = "official" });
            rows.Should().ContainEquivalentOf(new GcdAlias { Alias = "Attack on Titan", Language = null, Kind = "line" });
        }

        [Test]
        public void volumes_carry_the_raw_date_its_precision_and_type()
        {
            GivenArtifact(v0: true);

            var v1 = Subject.GetVolumes(1005, includeEditionDates: true).Find(v => v.VolumeNumber == 1);

            v1.ReleaseDate.Should().BeNull();
            v1.ReleaseDateRaw.Should().Be("2013");
            v1.ReleaseDatePrecision.Should().Be("year");
            v1.ReleaseDateType.Should().Be("published");
        }

        [Test]
        public void the_english_volume_read_leaves_the_edition_dates_unread()
        {
            // Ruling S5: GetVolumes(int), the English path, issues no new SQL -- even on a v0 artifact.
            GivenArtifact(v0: true);

            var v1 = Subject.GetVolumes(1005).Find(v => v.VolumeNumber == 1);

            v1.ReleaseDateRaw.Should().BeNull();
            v1.ReleaseDatePrecision.Should().BeNull();
            v1.ReleaseDateType.Should().BeNull();
        }

        [Test]
        public void markets_come_from_meta()
        {
            GivenArtifact(v0: true);

            Subject.Markets().Should().BeEquivalentTo(new Dictionary<string, int> { { "de", 1 }, { "en", 1 }, { "fr", 1 }, { "ja", 1 } });
        }

        [Test]
        public void an_older_artifact_answers_every_edition_read_with_nothing()
        {
            GivenArtifact(v0: false);

            var fr = Subject.FindSeriesById(1004);
            fr.TomeId.Should().BeNull();
            fr.LocalName.Should().BeNull();

            Subject.FindSeriesByTomeId("rl_fr").Should().BeNull();
            Subject.GetWorkLines("w_aot").Should().BeEmpty();
            Subject.GetAliasRows(1004).Should().OnlyContain(a => a.Language == null && a.Kind == null);
            Subject.GetAliasRows(1004).Should().HaveCount(2);
            Subject.GetVolumes(1005, includeEditionDates: true).Find(v => v.VolumeNumber == 1).ReleaseDatePrecision.Should().BeNull();

            // Computed from series.language when meta has no markets row.
            Subject.Markets().Should().BeEquivalentTo(new Dictionary<string, int> { { "de", 1 }, { "en", 1 }, { "fr", 1 }, { "ja", 1 } });
        }
    }
}
