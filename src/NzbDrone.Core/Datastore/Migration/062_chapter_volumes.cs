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
    // Volumes built from chapter packs (2026-10-21): "CBZ (chapters)" (9) is the quality of a volume
    // assembled from chapter releases. Every profile gets it as the lowest item (AllQualitiesValidator
    // requires every quality on every saved profile): allowed where the profile already lists CBZ,
    // disallowed elsewhere (light novel, audio), so those behave as before. Authors.BuildFromChapters
    // (default on) is the per-series switch; Editions.Chapters holds the chapter numbers a volume
    // covers (JSON, NULL = unknown). Idempotent per profile.
    [Migration(062)]
    public class chapter_volumes : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Editions").AddColumn("Chapters").AsString().Nullable();
            Alter.Table("Authors").AddColumn("BuildFromChapters").AsBoolean().WithDefaultValue(true);

            Execute.WithConnection(InsertCbzChapters);
        }

        private void InsertCbzChapters(IDbConnection conn, IDbTransaction tran)
        {
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<ProfileItem62>>(new QualityIntConverter()));
            var updater = new ProfileUpdater62(conn, tran);

            updater.InsertCbzChapters();

            updater.Commit();
        }

        public class Profile62
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Cutoff { get; set; }
            public List<ProfileItem62> Items { get; set; }
        }

        // Mirrors QualityProfileQualityItem's stored shape: a flat item has "quality"; a group item
        // has "id" + "name" and its qualities in "items" (no "quality" key). Kept whole so the
        // rewrite hands a group back unchanged.
        public class ProfileItem62
        {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public int Id { get; set; }
            public string Name { get; set; }
            public int? Quality { get; set; }
            public bool Allowed { get; set; }
            public List<ProfileItem62> Items { get; set; } = new List<ProfileItem62>();
        }

        public class ProfileUpdater62
        {
            private readonly IDbConnection _connection;
            private readonly IDbTransaction _transaction;

            private readonly List<Profile62> _profiles;
            private readonly HashSet<Profile62> _changedProfiles = new HashSet<Profile62>();

            public ProfileUpdater62(IDbConnection conn, IDbTransaction tran)
            {
                _connection = conn;
                _transaction = tran;

                _profiles = _connection.Query<Profile62>(@"SELECT ""Id"", ""Name"", ""Cutoff"", ""Items"" FROM ""QualityProfiles""",
                    transaction: _transaction).ToList();
            }

            public void Commit()
            {
                var sql = "UPDATE \"QualityProfiles\" SET \"Name\" = @Name, \"Cutoff\" = @Cutoff, \"Items\" = @Items WHERE \"Id\" = @Id";
                _connection.Execute(sql, _changedProfiles, transaction: _transaction);

                _changedProfiles.Clear();
            }

            public void InsertCbzChapters()
            {
                foreach (var profile in _profiles)
                {
                    // Already listed, flat or inside a group (a group item's own Quality is null).
                    if (profile.Items.Any(v => v.Quality == 9 || v.Items.Any(i => i.Quality == 9)))
                    {
                        continue;
                    }

                    // Allowed only on a profile that already takes CBZ (2), flat or grouped.
                    var allowed = profile.Items.Any(v => v.Quality == 2 || v.Items.Any(i => i.Quality == 2));

                    // Index 0 is the lowest rank: a built volume never beats a real CBZ.
                    profile.Items.Insert(0, new ProfileItem62
                    {
                        Quality = 9,
                        Allowed = allowed
                    });

                    _changedProfiles.Add(profile);
                }
            }
        }
    }
}
