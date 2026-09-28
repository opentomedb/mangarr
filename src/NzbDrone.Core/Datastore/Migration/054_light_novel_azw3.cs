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
    // Light-novel formats (2026-09-22): AZW3 (7) is a fallback ebook quality, grabbed only when no
    // EPUB (6) release exists. Every existing quality profile gets a flat AZW3 item inserted
    // directly before its EPUB item (top-level, or before the group holding EPUB if EPUB is
    // grouped), copying the allowed flag of that top-level entry — EPUB's own flag when it's flat,
    // or its GROUP's flag when EPUB is grouped (the runtime only ever consults the group's own
    // allowed flag for a quality nested inside it) — so the Light Novel EPUB profile allows AZW3
    // and every other (e.g. manga) profile gets it disallowed, same as today. A profile with no
    // EPUB item at all gets AZW3 disallowed at index 0. Cutoff and profile names are untouched.
    // Idempotent per profile.
    [Migration(054)]
    public class light_novel_azw3 : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(InsertAzw3BeforeEpub);
        }

        private void InsertAzw3BeforeEpub(IDbConnection conn, IDbTransaction tran)
        {
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<ProfileItem54>>(new QualityIntConverter()));
            var updater = new ProfileUpdater54(conn, tran);

            updater.InsertAzw3();

            updater.Commit();
        }

        public class Profile54
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Cutoff { get; set; }
            public List<ProfileItem54> Items { get; set; }
        }

        // Mirrors QualityProfileQualityItem's stored shape: a flat item has "quality"; a group item
        // has "id" + "name" and its qualities in "items" (no "quality" key). Kept whole so the
        // rewrite hands a group back unchanged.
        public class ProfileItem54
        {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public int Id { get; set; }
            public string Name { get; set; }
            public int? Quality { get; set; }
            public bool Allowed { get; set; }
            public List<ProfileItem54> Items { get; set; } = new List<ProfileItem54>();
        }

        public class ProfileUpdater54
        {
            private readonly IDbConnection _connection;
            private readonly IDbTransaction _transaction;

            private readonly List<Profile54> _profiles;
            private readonly HashSet<Profile54> _changedProfiles = new HashSet<Profile54>();

            public ProfileUpdater54(IDbConnection conn, IDbTransaction tran)
            {
                _connection = conn;
                _transaction = tran;

                _profiles = _connection.Query<Profile54>(@"SELECT ""Id"", ""Name"", ""Cutoff"", ""Items"" FROM ""QualityProfiles""",
                    transaction: _transaction).ToList();
            }

            public void Commit()
            {
                var sql = "UPDATE \"QualityProfiles\" SET \"Name\" = @Name, \"Cutoff\" = @Cutoff, \"Items\" = @Items WHERE \"Id\" = @Id";
                _connection.Execute(sql, _changedProfiles, transaction: _transaction);

                _changedProfiles.Clear();
            }

            public void InsertAzw3()
            {
                foreach (var profile in _profiles)
                {
                    // Already listed, flat or inside a group (a group item's own Quality is null).
                    if (profile.Items.Any(v => v.Quality == 7 || v.Items.Any(i => i.Quality == 7)))
                    {
                        continue;
                    }

                    var insertIndex = 0;
                    var allowed = false;

                    for (var i = 0; i < profile.Items.Count; i++)
                    {
                        var item = profile.Items[i];

                        if (item.Quality == 6)
                        {
                            insertIndex = i;
                            allowed = item.Allowed;
                            break;
                        }

                        var nested = item.Items.FirstOrDefault(x => x.Quality == 6);

                        if (nested != null)
                        {
                            // The runtime resolves a grouped quality to its GROUP's index
                            // (QualityProfile.GetIndex) and reads allowed from there
                            // (QualityAllowedByProfileSpecification) — not from the nested item —
                            // so AZW3's allowed flag has to come from the group, not from EPUB
                            // itself.
                            insertIndex = i;
                            allowed = item.Allowed;
                            break;
                        }
                    }

                    profile.Items.Insert(insertIndex, new ProfileItem54
                    {
                        Quality = 7,
                        Allowed = allowed
                    });

                    _changedProfiles.Add(profile);
                }
            }
        }
    }
}
