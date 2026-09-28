using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json.Serialization;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Light novels (2026-09): formats are editions of ONE series entry.
    //   Editions.MediaType             which format an edition is: 0 Archive (every existing row,
    //                                  i.e. every manga volume), 1 Ebook, 2 Audio
    //   Authors.AudioQualityProfileId  the audiobook profile; null = QualityProfileId judges audio too
    //   Authors.AudioAvailable         false = audio editions are wanted but PENDING ("not yet")
    //   Authors.LastAudioSearch        when the weekly best-effort audio search last ran
    // plus EPUB (6) and the audiobook qualities (13, 10, 11, 12) prepended, disallowed, to every
    // existing quality profile so the quality comparer never meets a quality a profile does not
    // list (prepended = lowest rank, so the Manga ladder is visually unchanged). Idempotent per
    // quality. Additive: every live manga row keeps its values. The two Light Novel profiles are
    // created by QualityProfileService on startup (by name, on every install).
    [Migration(048)]
    public class light_novel_editions : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Editions").AddColumn("MediaType").AsInt32().NotNullable().WithDefaultValue(0);

            Alter.Table("Authors").AddColumn("AudioQualityProfileId").AsInt32().Nullable();
            Alter.Table("Authors").AddColumn("AudioAvailable").AsBoolean().NotNullable().WithDefaultValue(false);
            Alter.Table("Authors").AddColumn("LastAudioSearch").AsDateTimeOffset().Nullable();

            Execute.WithConnection(PrependLightNovelQualities);
        }

        private void PrependLightNovelQualities(IDbConnection conn, IDbTransaction tran)
        {
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<ProfileItem48>>(new QualityIntConverter()));
            var updater = new ProfileUpdater48(conn, tran);

            // Insert in reverse so the stored order reads 6, 13, 10, 11, 12.
            foreach (var quality in new[] { 12, 11, 10, 13, 6 })
            {
                updater.PrependQuality(quality);
            }

            updater.Commit();
        }

        public class Profile48
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Cutoff { get; set; }
            public List<ProfileItem48> Items { get; set; }
        }

        // Mirrors QualityProfileQualityItem's stored shape: a flat item has "quality"; a group item
        // has "id" + "name" and its qualities in "items" (no "quality" key). Kept whole so the
        // rewrite hands a group back unchanged.
        public class ProfileItem48
        {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public int Id { get; set; }
            public string Name { get; set; }
            public int? Quality { get; set; }
            public bool Allowed { get; set; }
            public List<ProfileItem48> Items { get; set; } = new List<ProfileItem48>();
        }

        public class ProfileUpdater48
        {
            private readonly IDbConnection _connection;
            private readonly IDbTransaction _transaction;

            private readonly List<Profile48> _profiles;
            private readonly HashSet<Profile48> _changedProfiles = new HashSet<Profile48>();

            public ProfileUpdater48(IDbConnection conn, IDbTransaction tran)
            {
                _connection = conn;
                _transaction = tran;

                _profiles = _connection.Query<Profile48>(@"SELECT ""Id"", ""Name"", ""Cutoff"", ""Items"" FROM ""QualityProfiles""",
                    transaction: _transaction).ToList();
            }

            public void Commit()
            {
                var sql = "UPDATE \"QualityProfiles\" SET \"Name\" = @Name, \"Cutoff\" = @Cutoff, \"Items\" = @Items WHERE \"Id\" = @Id";
                _connection.Execute(sql, _changedProfiles, transaction: _transaction);

                _changedProfiles.Clear();
            }

            public void PrependQuality(int quality)
            {
                foreach (var profile in _profiles)
                {
                    // Already listed, flat or inside a group (a group item's own Quality is null).
                    if (profile.Items.Any(v => v.Quality == quality || v.Items.Any(i => i.Quality == quality)))
                    {
                        continue;
                    }

                    profile.Items.Insert(0, new ProfileItem48
                    {
                        Quality = quality,
                        Allowed = false
                    });

                    _changedProfiles.Add(profile);
                }
            }
        }
    }
}
