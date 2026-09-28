using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // Light-novel storage (2026-09-22): the dev server's old code defaults are written into Config only on an
    // install that used them (a calibre-homed or Audiobookshelf-homed file), never over a stored row.
    [TestFixture]
    public class light_novel_storageFixture : MigrationTest<light_novel_storage>
    {
        private const string Quality = "{\"quality\":{\"id\":6,\"name\":\"EPUB\"},\"revision\":{\"version\":1,\"real\":0,\"isRepack\":false}}";

        private static string KeyOf(string property)
        {
            return property.ToLowerInvariant();
        }

        private static void GivenBookFile(light_novel_storage c, int home, string path)
        {
            c.Insert.IntoTable("BookFiles").Row(new
            {
                Path = path,
                Size = 1,
                Modified = DateTime.UtcNow,
                DateAdded = DateTime.UtcNow,
                Quality,
                EditionId = 1,
                CalibreId = home == 1 ? 101 : 0,
                Part = 1,
                Home = home,
                Adopted = home != 0
            });
        }

        private static void GivenConfig(light_novel_storage c, string key, string value)
        {
            c.Insert.IntoTable("Config").Row(new { Key = key, Value = value });
        }

        private static Dictionary<string, string> ConfigOf(IDirectDataMapper db)
        {
            return db.Query<ConfigRow55>("SELECT \"Key\", \"Value\" FROM \"Config\"").ToDictionary(r => r.Key, r => r.Value);
        }

        [Test]
        public void a_calibre_homed_file_carries_the_calibre_values_over_and_nothing_else()
        {
            var db = WithMigrationTestDb(c => GivenBookFile(c, 1, "/books/Kugane Maruyama/Overlord, Vol. 5 (101)/Overlord, Vol. 5 - Kugane Maruyama.epub"));

            var config = ConfigOf(db);

            config.Should().HaveCount(5);
            config[KeyOf(nameof(IConfigService.LightNovelEbookHome))].Should().Be("calibre");
            config[KeyOf(nameof(IConfigService.CalibreContentServerUrl))].Should().Be(light_novel_storage.CalibreUrl);
            config[KeyOf(nameof(IConfigService.CalibreLibrary))].Should().Be(light_novel_storage.CalibreLibrary);
            config[KeyOf(nameof(IConfigService.CalibreRemotePath))].Should().Be(light_novel_storage.CalibrePath);
            config[KeyOf(nameof(IConfigService.CalibreLocalPath))].Should().Be(light_novel_storage.CalibrePath);
        }

        [Test]
        public void an_audiobooks_homed_file_carries_the_audiobookshelf_values_over_and_nothing_else()
        {
            var db = WithMigrationTestDb(c => GivenBookFile(c, 2, light_novel_storage.AudiobookshelfLocalPath + "Overlord - Vol. 5/Overlord - Vol 005.m4b"));

            var config = ConfigOf(db);

            config.Should().HaveCount(5);
            config[KeyOf(nameof(IConfigService.LightNovelAudioHome))].Should().Be("audiobookshelf");
            config[KeyOf(nameof(IConfigService.AudiobookshelfUrl))].Should().Be(light_novel_storage.AudiobookshelfUrl);
            config[KeyOf(nameof(IConfigService.AudiobookshelfLibraryId))].Should().Be(light_novel_storage.AudiobookshelfLibraryId);
            config[KeyOf(nameof(IConfigService.AudiobookshelfRemotePath))].Should().Be(light_novel_storage.AudiobookshelfRemotePath);
            config[KeyOf(nameof(IConfigService.AudiobookshelfLocalPath))].Should().Be(light_novel_storage.AudiobookshelfLocalPath);
        }

        [Test]
        public void an_install_with_only_entry_homed_files_gets_nothing_written()
        {
            var db = WithMigrationTestDb(c => GivenBookFile(c, 0, "/manga/Chainsaw Man/Chainsaw Man - Vol 001.cbz"));

            ConfigOf(db).Should().BeEmpty();
        }

        [Test]
        public void a_fresh_install_gets_nothing_written()
        {
            var db = WithMigrationTestDb();

            ConfigOf(db).Should().BeEmpty();
        }

        // The ABS key is stored on the dev server and must stay; any key already stored is left byte-identical.
        [Test]
        public void a_stored_row_is_never_overwritten()
        {
            var db = WithMigrationTestDb(c =>
            {
                GivenBookFile(c, 1, "/books/A/B (1)/B.epub");
                GivenBookFile(c, 2, light_novel_storage.AudiobookshelfLocalPath + "A - Vol. 1/A - Vol 001.m4b");
                GivenConfig(c, "audiobookshelfapikey", "stored-secret");
                GivenConfig(c, "calibrelibrary", "manga");
                GivenConfig(c, "rsssyncinterval", "15");
            });

            var config = ConfigOf(db);

            config.Should().HaveCount(3 + 9);
            config["audiobookshelfapikey"].Should().Be("stored-secret");
            config["calibrelibrary"].Should().Be("manga");
            config["rsssyncinterval"].Should().Be("15");
            config[KeyOf(nameof(IConfigService.LightNovelEbookHome))].Should().Be("calibre");
            config[KeyOf(nameof(IConfigService.LightNovelAudioHome))].Should().Be("audiobookshelf");
        }

        // GetValue treats a stored "" the same as no row (reads fall through to the default), so an
        // install that used the old default with the key saved as "" was still effectively using it.
        // The carry-over must fill that row in, not skip it -- but a genuinely non-empty stored value
        // still stays untouched.
        [Test]
        public void an_empty_stored_value_is_filled_in_but_a_non_empty_one_stays()
        {
            var db = WithMigrationTestDb(c =>
            {
                GivenBookFile(c, 1, "/books/A/B (1)/B.epub");
                GivenConfig(c, "calibrecontentserverurl", string.Empty);
                GivenConfig(c, "calibrelibrary", "manga");
            });

            var config = ConfigOf(db);

            config[KeyOf(nameof(IConfigService.CalibreContentServerUrl))].Should().Be(light_novel_storage.CalibreUrl);
            config[KeyOf(nameof(IConfigService.CalibreLibrary))].Should().Be("manga");
        }

        // The OpenTome link is not carried over: its old default answered nothing (spec §6.4).
        [Test]
        public void the_opentome_url_is_never_written()
        {
            var db = WithMigrationTestDb(c =>
            {
                GivenBookFile(c, 1, "/books/A/B (1)/B.epub");
                GivenBookFile(c, 2, light_novel_storage.AudiobookshelfLocalPath + "A - Vol. 1/A - Vol 001.m4b");
            });

            ConfigOf(db).Keys.Should().NotContain(KeyOf(nameof(IConfigService.OpenTomeUrl)));
        }

        private class ConfigRow55
        {
            public string Key { get; set; }
            public string Value { get; set; }
        }
    }
}
