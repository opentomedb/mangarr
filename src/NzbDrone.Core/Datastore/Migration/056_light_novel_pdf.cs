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
    // LN PDF (2026-09-22): Ebook PDF (8) is a light novel's PDF, the last-resort ebook below AZW3.
    // Every existing quality profile gets a flat Ebook PDF item, UNTICKED, inserted directly
    // before (worse than) the top-level item holding AZW3 (7) -- flat, or the group holding it --
    // else before the one holding EPUB (6), else at index 0. Unticked everywhere: nobody's
    // grabbing changes until they tick it (spec §6.2), and a manga profile never ALLOWS an ebook
    // quality anyway. Cutoff and profile names are untouched. Idempotent per profile.
    [Migration(056)]
    public class light_novel_pdf : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(InsertEbookPdfBelowAzw3);
        }

        private void InsertEbookPdfBelowAzw3(IDbConnection conn, IDbTransaction tran)
        {
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<ProfileItem56>>(new QualityIntConverter()));
            var updater = new ProfileUpdater56(conn, tran);

            updater.InsertEbookPdf();

            updater.Commit();
        }

        public class Profile56
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Cutoff { get; set; }
            public List<ProfileItem56> Items { get; set; }
        }

        // Mirrors QualityProfileQualityItem's stored shape (see ProfileItem54): a flat item has
        // "quality"; a group item has "id" + "name" and its qualities in "items". Kept whole so the
        // rewrite hands a group back unchanged.
        public class ProfileItem56
        {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public int Id { get; set; }
            public string Name { get; set; }
            public int? Quality { get; set; }
            public bool Allowed { get; set; }
            public List<ProfileItem56> Items { get; set; } = new List<ProfileItem56>();
        }

        public class ProfileUpdater56
        {
            private const int EbookPdf = 8;
            private const int Azw3 = 7;
            private const int Epub = 6;

            private readonly IDbConnection _connection;
            private readonly IDbTransaction _transaction;

            private readonly List<Profile56> _profiles;
            private readonly HashSet<Profile56> _changedProfiles = new HashSet<Profile56>();

            public ProfileUpdater56(IDbConnection conn, IDbTransaction tran)
            {
                _connection = conn;
                _transaction = tran;

                _profiles = _connection.Query<Profile56>(@"SELECT ""Id"", ""Name"", ""Cutoff"", ""Items"" FROM ""QualityProfiles""",
                    transaction: _transaction).ToList();
            }

            public void Commit()
            {
                var sql = "UPDATE \"QualityProfiles\" SET \"Name\" = @Name, \"Cutoff\" = @Cutoff, \"Items\" = @Items WHERE \"Id\" = @Id";
                _connection.Execute(sql, _changedProfiles, transaction: _transaction);

                _changedProfiles.Clear();
            }

            public void InsertEbookPdf()
            {
                foreach (var profile in _profiles)
                {
                    // Already listed, flat or inside a group (a group item's own Quality is null).
                    if (IndexHolding(profile.Items, EbookPdf) >= 0)
                    {
                        continue;
                    }

                    var insertIndex = IndexHolding(profile.Items, Azw3);

                    if (insertIndex < 0)
                    {
                        insertIndex = IndexHolding(profile.Items, Epub);
                    }

                    if (insertIndex < 0)
                    {
                        insertIndex = 0;
                    }

                    profile.Items.Insert(insertIndex, new ProfileItem56
                    {
                        Quality = EbookPdf,
                        Allowed = false
                    });

                    _changedProfiles.Add(profile);
                }
            }

            // The top-level item holding the quality: the flat item itself, or the group that
            // nests it (the runtime resolves a grouped quality to its group's index).
            private static int IndexHolding(List<ProfileItem56> items, int quality)
            {
                return items.FindIndex(i => i.Quality == quality || i.Items.Any(n => n.Quality == quality));
            }
        }
    }
}
