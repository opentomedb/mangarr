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
    // Beta readiness (2026-09-28): an install with a library keeps ComicInfo on all imports and the daily
    // PDF sweep through explicit rows; a fresh DB gets nothing, so the new defaults apply.
    [TestFixture]
    public class beta_defaultsFixture : MigrationTest<beta_defaults>
    {
        private const string Quality = "{\"quality\":{\"id\":1,\"name\":\"CBZ\"},\"revision\":{\"version\":1,\"real\":0,\"isRepack\":false}}";

        private static string KeyOf(string property)
        {
            return property.ToLowerInvariant();
        }

        private static void GivenAuthor(beta_defaults c)
        {
            c.Insert.IntoTable("Authors").Row(new
            {
                CleanName = "chainsawman",
                Path = "/manga/Chainsaw Man",
                Monitored = true,
                MonitorNewItems = 0,
                QualityProfileId = 1,
                MetadataProfileId = 1,
                AuthorMetadataId = 1,
                CopyInPending = false
            });
        }

        private static void GivenBookFile(beta_defaults c)
        {
            c.Insert.IntoTable("BookFiles").Row(new
            {
                Path = "/manga/Chainsaw Man/Chainsaw Man - Vol 001.cbz",
                Size = 1,
                Modified = DateTime.UtcNow,
                DateAdded = DateTime.UtcNow,
                Quality,
                EditionId = 1,
                CalibreId = 0,
                Part = 1,
                Home = 0,
                Adopted = false
            });
        }

        private static Dictionary<string, string> ConfigOf(IDirectDataMapper db)
        {
            return db.Query<ConfigRow59>("SELECT \"Key\", \"Value\" FROM \"Config\"").ToDictionary(r => r.Key, r => r.Value);
        }

        [Test]
        public void a_fresh_install_gets_nothing_written()
        {
            ConfigOf(WithMigrationTestDb()).Should().BeEmpty();
        }

        [Test]
        public void an_install_with_a_series_keeps_both_old_behaviours()
        {
            var config = ConfigOf(WithMigrationTestDb(GivenAuthor));

            config.Should().HaveCount(2);
            config[KeyOf(nameof(IConfigService.WriteComicInfo))].Should().Be("allimports");
            config[KeyOf(nameof(IConfigService.PdfToCbzSweep))].Should().Be("True");
        }

        [Test]
        public void an_install_with_only_files_keeps_both_old_behaviours()
        {
            var config = ConfigOf(WithMigrationTestDb(GivenBookFile));

            config[KeyOf(nameof(IConfigService.WriteComicInfo))].Should().Be("allimports");
            config[KeyOf(nameof(IConfigService.PdfToCbzSweep))].Should().Be("True");
        }

        [Test]
        public void a_stored_value_is_never_overwritten_and_an_empty_one_is_filled()
        {
            var db = WithMigrationTestDb(c =>
            {
                GivenAuthor(c);
                c.Insert.IntoTable("Config").Row(new { Key = "writecomicinfo", Value = "never" });
                c.Insert.IntoTable("Config").Row(new { Key = "pdftocbzsweep", Value = string.Empty });
            });

            var config = ConfigOf(db);

            config["writecomicinfo"].Should().Be("never");
            config["pdftocbzsweep"].Should().Be("True");
        }

        // The rows parse the way ConfigService reads them.
        [Test]
        public void the_written_values_parse_as_the_config_types()
        {
            Enum.Parse(typeof(WriteComicInfoType), "allimports", true).Should().Be(WriteComicInfoType.AllImports);
            Convert.ToBoolean("True").Should().BeTrue();
        }

        private class ConfigRow59
        {
            public string Key { get; set; }
            public string Value { get; set; }
        }
    }
}
