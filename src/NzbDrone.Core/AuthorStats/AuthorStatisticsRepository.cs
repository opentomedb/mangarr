using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.AuthorStats
{
    public interface IAuthorStatisticsRepository
    {
        List<BookStatistics> AuthorStatistics();
        List<BookStatistics> AuthorStatistics(int authorId);
    }

    public class AuthorStatisticsRepository : IAuthorStatisticsRepository
    {
        private const string _selectTemplate = "SELECT /**select**/ FROM \"Editions\" /**join**/ /**innerjoin**/ /**leftjoin**/ /**where**/ /**groupby**/ /**having**/ /**orderby**/";

        private readonly IMainDatabase _database;

        public AuthorStatisticsRepository(IMainDatabase database)
        {
            _database = database;
        }

        public List<BookStatistics> AuthorStatistics()
        {
            return Query(Builder());
        }

        public List<BookStatistics> AuthorStatistics(int authorId)
        {
            return Query(Builder().Where<Author>(x => x.Id == authorId));
        }

        private List<BookStatistics> Query(SqlBuilder builder)
        {
            var sql = builder.AddTemplate(_selectTemplate).LogQuery();

            using (var conn = _database.OpenConnection())
            {
                return conn.Query<BookStatistics>(sql.RawSql, sql.Parameters).ToList();
            }
        }

        private SqlBuilder Builder()
        {
            var trueIndicator = _database.DatabaseType == DatabaseType.PostgreSQL ? "true" : "1";
            var falseIndicator = _database.DatabaseType == DatabaseType.PostgreSQL ? "false" : "0";
            var audio = (int)MediaType.Audio;

            // Per (author, book) over the book's MONITORED editions. "Primary" = every non-Audio
            // edition: a manga volume's single Archive edition (so BookFileCount and
            // AvailableBookCount are exactly what they were), a light novel's Ebook edition. Audio
            // editions get their own 0/1 pair so the UI can say "EPUB x / y - Audio x / y";
            // SizeOnDisk and BookCount span every edition.
            return new SqlBuilder(_database.DatabaseType)
            .Select($@"""Authors"".""Id"" AS ""AuthorId"",
                     ""Books"".""Id"" AS ""BookId"",
                     SUM(COALESCE(""BookFiles"".""Size"", 0)) AS ""SizeOnDisk"",
                     1 AS ""TotalBookCount"",
                     MAX(CASE WHEN ""Editions"".""MediaType"" <> {audio} AND ""BookFiles"".""Id"" IS NOT NULL THEN 1 ELSE 0 END) AS ""AvailableBookCount"",
                     CASE WHEN (""Books"".""Monitored"" = {trueIndicator} AND (""Books"".""ReleaseDate"" < @currentDate) OR ""Books"".""ReleaseDate"" IS NULL) OR MIN(""BookFiles"".""Id"") IS NOT NULL THEN 1 ELSE 0 END AS ""BookCount"",
                     COUNT(CASE WHEN ""Editions"".""MediaType"" <> {audio} THEN ""BookFiles"".""Id"" END) AS ""BookFileCount"",
                     -- unreleased audio (2026-09-21): an available series counts an audio edition only once
                     -- Audible lists it with a past date (or it has a file / is covered); a pending series
                     -- (AudioAvailable off) counts as before so its not-yet row keeps its total.
                     CASE WHEN MAX(CASE WHEN ""Editions"".""MediaType"" = {audio} THEN 1 ELSE 0 END) = 1
                          AND (((""Books"".""Monitored"" = {trueIndicator} AND (""Books"".""ReleaseDate"" < @currentDate) OR ""Books"".""ReleaseDate"" IS NULL)
                                AND (""Authors"".""AudioAvailable"" = {falseIndicator}
                                     OR MAX(CASE WHEN ""Editions"".""MediaType"" = {audio} AND ""Editions"".""Asin"" IS NOT NULL AND ""Editions"".""AudioReleaseDate"" IS NOT NULL AND ""Editions"".""AudioReleaseDate"" <= @currentDate THEN 1 ELSE 0 END) = 1))
                               OR MAX(CASE WHEN ""Editions"".""MediaType"" = {audio} AND (""BookFiles"".""Id"" IS NOT NULL OR ""Editions"".""CoveredByVolume"" IS NOT NULL) THEN 1 ELSE 0 END) = 1)
                          THEN 1 ELSE 0 END AS ""AudioBookCount"",
                     -- covered volumes (2026-09-17, D8): a covered Audio edition is satisfied
                     MAX(CASE WHEN ""Editions"".""MediaType"" = {audio} AND (""BookFiles"".""Id"" IS NOT NULL OR ""Editions"".""CoveredByVolume"" IS NOT NULL) THEN 1 ELSE 0 END) AS ""AudioBookFileCount""")
            .Join<Edition, Book>((e, b) => e.BookId == b.Id)
            .Join<Book, Author>((book, author) => book.AuthorMetadataId == author.AuthorMetadataId)
            .LeftJoin<Edition, BookFile>((t, f) => t.Id == f.EditionId)
            .Where<Edition>(x => x.Monitored == true)
            .GroupBy<Author>(x => x.Id)
            .GroupBy<Book>(x => x.Id)
            .AddParameters(new Dictionary<string, object> { { "currentDate", DateTime.UtcNow } });
        }
    }
}
