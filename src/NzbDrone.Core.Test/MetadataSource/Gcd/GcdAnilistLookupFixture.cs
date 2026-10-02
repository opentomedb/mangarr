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
    // KR/CN consumer (2026-09-29, spec §3.1): an AniList id finds its line in any language.
    [TestFixture]
    public class GcdAnilistLookupFixture : CoreTest<GcdMetadataService>
    {
        private void GivenArtifact(bool workIds = true, bool sideLineOnly = false)
        {
            var appData = Path.Combine(TempFolder, workIds ? "v0" : "old");
            var metaDir = Path.Combine(appData, "metadata");
            Directory.CreateDirectory(metaDir);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.AppDataFolder).Returns(appData);

            var cs = new SQLiteConnectionStringBuilder { DataSource = Path.Combine(metaDir, GcdMetadataService.ArtifactName) }.ConnectionString;
            using var conn = new SQLiteConnection(cs);
            conn.Open();

            var cols = workIds ? ", tome_id TEXT, tome_work_id TEXT, local_name TEXT" : string.Empty;
            conn.Execute(@"
                CREATE TABLE series (gcd_series_id INTEGER PRIMARY KEY, name TEXT, year_began INTEGER, publisher TEXT,
                    language TEXT, country TEXT, is_omnibus INTEGER, volume_count INTEGER, status TEXT, orig_series_id INTEGER,
                    anilist_id INTEGER, mangaupdates_id INTEGER, mangadex_id TEXT, medium TEXT, dated_count INTEGER,
                    is_main INTEGER, author TEXT" + cols + @");
                CREATE TABLE volumes (id INTEGER PRIMARY KEY, gcd_series_id INTEGER, volume_number INTEGER, title TEXT,
                    release_date TEXT, isbn13 TEXT, isbn10 TEXT, page_count INTEGER, composition TEXT, cover_url TEXT, cover_source TEXT);
                CREATE TABLE series_alias (gcd_series_id INTEGER, alias TEXT);
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);");

            // Overgeared: the AniList id sits on the German line only; the English line has none.
            // A second, unrelated work wrongly carries the same id on a German side line (not main).
            conn.Execute(@"INSERT INTO series (gcd_series_id, name, language, is_omnibus, volume_count, anilist_id, medium, dated_count, is_main)
                           VALUES (5001, 'Overgeared', 'de', 0, 10, 115321, 'manhwa', 10, 1),
                                  (5002, 'Overgeared', 'en', 0, 11, NULL, 'manhwa', 5, 1),
                                  (5003, 'Overgeared Side', 'de', 0, 2, 115321, 'manhwa', 2, 0),
                                  (6001, 'Some Novel', 'de', 0, 4, 222, 'novel', 4, 1)");

            if (workIds)
            {
                conn.Execute(@"UPDATE series SET tome_work_id = CASE WHEN gcd_series_id IN (5001, 5002) THEN 'w_og'
                                 WHEN gcd_series_id = 5003 THEN 'w_other' ELSE 'w_novel' END,
                                 tome_id = 'rl_' || gcd_series_id");
            }

            if (sideLineOnly)
            {
                // Only the side line carries the id, and it belongs to Overgeared's work.
                conn.Execute("UPDATE series SET anilist_id = NULL WHERE gcd_series_id = 5001");
                conn.Execute("UPDATE series SET tome_work_id = 'w_og' WHERE gcd_series_id = 5003");
            }
        }

        // Final fix wave C1: a line that carries the id DIRECTLY comes first -- the id names that series (a
        // spin-off's id must not answer with its work's main line). EditionResolver.ResolveFallback takes the
        // work's English counterpart from there.
        [Test]
        public void an_id_on_the_german_line_returns_the_german_line_directly()
        {
            GivenArtifact();

            Subject.FindSeriesByAnilistId(115321, LibraryType.Manga).GcdSeriesId.Should().Be(5001);
        }

        [Test]
        public void two_works_sharing_an_id_answer_deterministically_direct_then_main()
        {
            GivenArtifact();

            // 5001 (main, direct) over 5003 (not main, direct) over 5002 (widened English).
            Subject.FindSeriesByAnilistId(115321, LibraryType.Manga).GcdSeriesId.Should().Be(5001);
            Subject.FindSeriesByAnilistId(115321, LibraryType.Manga).GcdSeriesId.Should().Be(5001);
        }

        [Test]
        public void a_direct_side_line_beats_the_works_widened_english_main_line()
        {
            GivenArtifact(sideLineOnly: true);

            Subject.FindSeriesByAnilistId(115321, LibraryType.Manga).GcdSeriesId.Should().Be(5003);
        }

        [Test]
        public void a_light_novel_request_takes_novel_lines_only()
        {
            GivenArtifact();

            Subject.FindSeriesByAnilistId(115321, LibraryType.LightNovel).Should().BeNull();
            Subject.FindSeriesByAnilistId(222, LibraryType.LightNovel).GcdSeriesId.Should().Be(6001);
        }

        [Test]
        public void an_artifact_without_work_ids_answers_from_the_matched_rows()
        {
            GivenArtifact(workIds: false);

            // No work to hop to: the main German line itself.
            Subject.FindSeriesByAnilistId(115321, LibraryType.Manga).GcdSeriesId.Should().Be(5001);
        }

        [Test]
        public void an_unknown_id_is_null()
        {
            GivenArtifact();

            Subject.FindSeriesByAnilistId(999999, LibraryType.Manga).Should().BeNull();
        }
    }
}
