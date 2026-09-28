using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Books
{
    public interface IBookRepository : IBasicRepository<Book>
    {
        List<Book> GetBooks(int authorId);
        List<Book> GetLastBooks(IEnumerable<int> authorMetadataIds);
        List<Book> GetNextBooks(IEnumerable<int> authorMetadataIds);
        List<Book> GetBooksByAuthorMetadataId(int authorMetadataId);
        List<Book> GetBooksForRefresh(int authorMetadataId, List<string> foreignIds);
        List<Book> GetBooksByFileIds(IEnumerable<int> fileIds);
        Book FindByTitle(int authorMetadataId, string title);
        Book FindById(string foreignBookId);
        Book FindBySlug(string titleSlug);
        PagingSpec<Book> BooksWithoutFiles(PagingSpec<Book> pagingSpec, LibraryType? library = null, WantedMediaTypeFilter? mediaType = null);
        PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, List<QualitiesBelowCutoff> qualitiesBelowCutoff, LibraryType? library = null, WantedMediaTypeFilter? mediaType = null);
        List<Book> BooksBetweenDates(DateTime startDate, DateTime endDate, bool includeUnmonitored);
        List<Book> AuthorBooksBetweenDates(Author author, DateTime startDate, DateTime endDate, bool includeUnmonitored);
        void SetMonitoredFlat(Book book, bool monitored);
        void SetMonitored(IEnumerable<int> ids, bool monitored);
        List<Book> GetAuthorBooksWithFiles(Author author);
    }

    public class BookRepository : BasicRepository<Book>, IBookRepository
    {
        public BookRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<Book> GetBooks(int authorId)
        {
            return Query(Builder().Join<Book, Author>((l, r) => l.AuthorMetadataId == r.AuthorMetadataId).Where<Author>(a => a.Id == authorId));
        }

        public List<Book> GetLastBooks(IEnumerable<int> authorMetadataIds)
        {
            var now = DateTime.UtcNow;

            var inner = Builder()
                .Select("MIN(\"Books\".\"Id\") as id, MAX(\"Books\".\"ReleaseDate\") as date")
                .Where<Book>(x => authorMetadataIds.Contains(x.AuthorMetadataId) && x.ReleaseDate < now)
                .GroupBy<Book>(x => x.AuthorMetadataId)
                .AddSelectTemplate(typeof(Book));

            var outer = Builder()
                .Join($"({inner.RawSql}) ids on ids.id = \"Books\".\"Id\" and ids.date = \"Books\".\"ReleaseDate\"")
                .AddParameters(inner.Parameters);

            return Query(outer);
        }

        public List<Book> GetNextBooks(IEnumerable<int> authorMetadataIds)
        {
            var now = DateTime.UtcNow;

            var inner = Builder()
                .Select("MIN(\"Books\".\"Id\") as id, MIN(\"Books\".\"ReleaseDate\") as date")
                .Where<Book>(x => authorMetadataIds.Contains(x.AuthorMetadataId) && x.ReleaseDate > now)
                .GroupBy<Book>(x => x.AuthorMetadataId)
                .AddSelectTemplate(typeof(Book));

            var outer = Builder()
                .Join($"({inner.RawSql}) ids on ids.id = \"Books\".\"Id\" and ids.date = \"Books\".\"ReleaseDate\"")
                .AddParameters(inner.Parameters);

            return Query(outer);
        }

        public List<Book> GetBooksByAuthorMetadataId(int authorMetadataId)
        {
            return Query(s => s.AuthorMetadataId == authorMetadataId);
        }

        public List<Book> GetBooksForRefresh(int authorMetadataId, List<string> foreignIds)
        {
            return Query(a => a.AuthorMetadataId == authorMetadataId || foreignIds.Contains(a.ForeignBookId));
        }

        public List<Book> GetBooksByFileIds(IEnumerable<int> fileIds)
        {
            return Query(new SqlBuilder(_database.DatabaseType)
                         .Join<Book, Edition>((b, e) => b.Id == e.BookId)
                         .Join<Edition, BookFile>((l, r) => l.Id == r.EditionId)
                         .Where<BookFile>(f => fileIds.Contains(f.Id)))
                .DistinctBy(x => x.Id)
                .ToList();
        }

        public Book FindById(string foreignBookId)
        {
            return Query(s => s.ForeignBookId == foreignBookId).SingleOrDefault();
        }

        public Book FindBySlug(string titleSlug)
        {
            return Query(s => s.TitleSlug == titleSlug).SingleOrDefault();
        }

        // Wanted (2026-09, light novels): a row per MONITORED, NON-PENDING edition without a file.
        // Pending = an Audio edition of a series whose AudioAvailable is still false ("not yet":
        // shown grey, not Missing, searched only by the weekly probe). A manga volume has one
        // Archive edition, so the extra clause is always true for it and its rows are what they
        // were. Rows are grouped by "Books"."Id" because a light-novel volume missing both editions
        // would otherwise appear twice -- the same GroupBy<Album>(x => x.Id) Lidarr's
        // AlbumsWithoutFilesBuilder uses for multi-track releases, and the form that keeps the paged
        // ORDER BY free to name "AuthorMetadata"."SortName" (SELECT DISTINCT would not on PostgreSQL).
        private string NotPendingClause()
        {
            var falseIndicator = _database.DatabaseType == DatabaseType.PostgreSQL ? "false" : "0";

            return $"NOT (\"Editions\".\"MediaType\" = {(int)MediaType.Audio} AND \"Authors\".\"AudioAvailable\" = {falseIndicator})";
        }

        // Unreleased audio (2026-09-21): the series has audio, but Audible lists this volume's
        // audiobook for a future date or not at all yet (Edition.IsUnreleasedAudio). Not missing.
        // The BookFile join is already NULL here, so no file test is needed.
        private string NotUnreleasedAudioClause()
        {
            var trueIndicator = _database.DatabaseType == DatabaseType.PostgreSQL ? "true" : "1";

            return $"NOT (\"Editions\".\"MediaType\" = {(int)MediaType.Audio} AND \"Authors\".\"AudioAvailable\" = {trueIndicator} AND (\"Editions\".\"Asin\" IS NULL OR \"Editions\".\"AudioReleaseDate\" IS NULL OR \"Editions\".\"AudioReleaseDate\" > @unreleasedNow))";
        }

        // Covered volumes (2026-09-17, D8): satisfied, not missing. An Audio edition inside another
        // volume's file (CoveredByVolume set, by an import or by Audible) is neither wanted nor below
        // cutoff. A manga volume's Archive edition never carries a mark, so the clause is always true for it.
        private string NotCoveredClause()
        {
            return $"NOT (\"Editions\".\"MediaType\" = {(int)MediaType.Audio} AND \"Editions\".\"CoveredByVolume\" IS NOT NULL)";
        }

        // Wanted filter presets (2026-09-21): the Missing and Cutoff Unmet pages can narrow to one
        // library and to one edition class. The library a volume belongs to is its series', read off
        // AuthorMetadata.ForeignAuthorId -- the column Author.Library itself parses, and the only
        // one that carries the suffix (Authors.ForeignAuthorId is a proxy onto it, not a column).
        // Both builders already join AuthorMetadata for the sort, so the clause is free.
        private SqlBuilder WithLibrary(SqlBuilder builder, LibraryType? library)
        {
            if (library == null)
            {
                return builder;
            }

            // LibraryTypes.Parse is a Contains, so the pattern is one too: a row can never be filed
            // in one library by the page and in the other by the model.
            var pattern = $"'%{LibraryTypes.LightNovelSuffix}%'";

            return builder.Where(library == LibraryType.LightNovel
                ? $"\"AuthorMetadata\".\"ForeignAuthorId\" LIKE {pattern}"
                : $"\"AuthorMetadata\".\"ForeignAuthorId\" NOT LIKE {pattern}");
        }

        // Ebook/Audio drop the rows of the other class. "Both" keeps the volumes whose EPUB AND
        // audiobook are both in the list, which is a property of the whole group rather than of one
        // row, so it is a HAVING over the GROUP BY "Books"."Id" both builders already carry -- and
        // the /**having**/ slot is in the paged select template as well as the count template, so
        // the rows and the total stay in step.
        private SqlBuilder WithMediaType(SqlBuilder builder, WantedMediaTypeFilter? mediaType)
        {
            switch (mediaType)
            {
                case WantedMediaTypeFilter.Ebook:
                    return builder.Where($"\"Editions\".\"MediaType\" = {(int)MediaType.Ebook}");
                case WantedMediaTypeFilter.Audio:
                    return builder.Where($"\"Editions\".\"MediaType\" = {(int)MediaType.Audio}");
                case WantedMediaTypeFilter.Both:
                    return builder.Having($"SUM(CASE WHEN \"Editions\".\"MediaType\" = {(int)MediaType.Ebook} THEN 1 ELSE 0 END) > 0 AND SUM(CASE WHEN \"Editions\".\"MediaType\" = {(int)MediaType.Audio} THEN 1 ELSE 0 END) > 0");
                default:
                    return builder;
            }
        }

        private SqlBuilder WithWantedFilters(SqlBuilder builder, LibraryType? library, WantedMediaTypeFilter? mediaType)
        {
            return WithMediaType(WithLibrary(builder, library), mediaType);
        }

        //x.Id == null is converted to SQL, so warning incorrect
#pragma warning disable CS0472
        private SqlBuilder BooksWithoutFilesBuilder(DateTime currentTime) => Builder()
            .Join<Book, Author>((l, r) => l.AuthorMetadataId == r.AuthorMetadataId)
            .Join<Author, AuthorMetadata>((l, r) => l.AuthorMetadataId == r.Id)
            .Join<Book, Edition>((b, e) => b.Id == e.BookId)
            .LeftJoin<Edition, BookFile>((t, f) => t.Id == f.EditionId)
            .Where<BookFile>(f => f.Id == null)
            .Where<Edition>(e => e.Monitored == true)
            .Where(NotPendingClause())
            .Where(NotCoveredClause())
            .Where(NotUnreleasedAudioClause(), new { unreleasedNow = currentTime })
            .Where<Book>(a => a.ReleaseDate <= currentTime)
            .GroupBy<Book>(x => x.Id);
#pragma warning restore CS0472

        public PagingSpec<Book> BooksWithoutFiles(PagingSpec<Book> pagingSpec, LibraryType? library = null, WantedMediaTypeFilter? mediaType = null)
        {
            var currentTime = DateTime.UtcNow;

            pagingSpec.Records = GetPagedRecords(WithWantedFilters(BooksWithoutFilesBuilder(currentTime), library, mediaType), pagingSpec, PagedQuery);

            var countTemplate = $"SELECT COUNT(*) FROM (SELECT /**select**/ FROM \"{TableMapping.Mapper.TableNameMapping(typeof(Book))}\" /**join**/ /**innerjoin**/ /**leftjoin**/ /**where**/ /**groupby**/ /**having**/) AS \"Inner\"";
            pagingSpec.TotalRecords = GetPagedRecordCount(WithWantedFilters(BooksWithoutFilesBuilder(currentTime), library, mediaType).Select(typeof(Book)), pagingSpec, countTemplate);

            return pagingSpec;
        }

        private SqlBuilder BooksWhereCutoffUnmetBuilder(List<QualitiesBelowCutoff> qualitiesBelowCutoff) => Builder()
            .Join<Book, Author>((l, r) => l.AuthorMetadataId == r.AuthorMetadataId)
            .Join<Author, AuthorMetadata>((l, r) => l.AuthorMetadataId == r.Id)
            .Join<Book, Edition>((b, e) => b.Id == e.BookId)
            .LeftJoin<Edition, BookFile>((t, f) => t.Id == f.EditionId)
            .Where<Edition>(e => e.Monitored == true)
            .Where(NotCoveredClause())
            .Where(BuildQualityCutoffWhereClause(qualitiesBelowCutoff))
            .GroupBy<Book>(x => x.Id);

        // Cutoff (2026-09, light novels): a file is judged against the profile of ITS edition's media
        // type -- the series' audio profile (falling back to the main one when it is NULL or 0,
        // exactly like Author.QualityProfileIdFor) for Audio editions, the main profile otherwise.
        // For a manga volume (Archive) only the first alternative can match, so the clause is the
        // old one.
        private string BuildQualityCutoffWhereClause(List<QualitiesBelowCutoff> qualitiesBelowCutoff)
        {
            var clauses = new List<string>();
            var audio = (int)MediaType.Audio;

            foreach (var profile in qualitiesBelowCutoff)
            {
                foreach (var belowCutoff in profile.QualityIds)
                {
                    clauses.Add(string.Format(
                        "(((\"Editions\".\"MediaType\" <> {2} AND \"Authors\".\"QualityProfileId\" = {0}) OR (\"Editions\".\"MediaType\" = {2} AND COALESCE(NULLIF(\"Authors\".\"AudioQualityProfileId\", 0), \"Authors\".\"QualityProfileId\") = {0})) AND \"BookFiles\".\"Quality\" LIKE '%_quality_: {1},%')",
                        profile.ProfileId,
                        belowCutoff,
                        audio));
                }
            }

            return string.Format("({0})", string.Join(" OR ", clauses));
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, List<QualitiesBelowCutoff> qualitiesBelowCutoff, LibraryType? library = null, WantedMediaTypeFilter? mediaType = null)
        {
            pagingSpec.Records = GetPagedRecords(WithWantedFilters(BooksWhereCutoffUnmetBuilder(qualitiesBelowCutoff), library, mediaType), pagingSpec, PagedQuery);

            var countTemplate = $"SELECT COUNT(*) FROM (SELECT /**select**/ FROM \"{TableMapping.Mapper.TableNameMapping(typeof(Book))}\" /**join**/ /**innerjoin**/ /**leftjoin**/ /**where**/ /**groupby**/ /**having**/) AS \"Inner\"";
            pagingSpec.TotalRecords = GetPagedRecordCount(WithWantedFilters(BooksWhereCutoffUnmetBuilder(qualitiesBelowCutoff), library, mediaType).Select(typeof(Book)), pagingSpec, countTemplate);

            return pagingSpec;
        }

        public List<Book> BooksBetweenDates(DateTime startDate, DateTime endDate, bool includeUnmonitored)
        {
            var builder = Builder().Where<Book>(rg => rg.ReleaseDate >= startDate && rg.ReleaseDate <= endDate);

            if (!includeUnmonitored)
            {
                builder = builder.Where<Book>(e => e.Monitored == true)
                    .Join<Book, Author>((l, r) => l.AuthorMetadataId == r.AuthorMetadataId)
                    .Where<Author>(e => e.Monitored == true);
            }

            return Query(builder);
        }

        public List<Book> AuthorBooksBetweenDates(Author author, DateTime startDate, DateTime endDate, bool includeUnmonitored)
        {
            var builder = Builder().Where<Book>(rg => rg.ReleaseDate >= startDate &&
                                                 rg.ReleaseDate <= endDate &&
                                                 rg.AuthorMetadataId == author.AuthorMetadataId);

            if (!includeUnmonitored)
            {
                builder = builder.Where<Book>(e => e.Monitored == true)
                    .Join<Book, Author>((l, r) => l.AuthorMetadataId == r.AuthorMetadataId)
                    .Where<Author>(e => e.Monitored == true);
            }

            return Query(builder);
        }

        public void SetMonitoredFlat(Book book, bool monitored)
        {
            book.Monitored = monitored;
            SetFields(book, p => p.Monitored);

            ModelUpdated(book, true);
        }

        public void SetMonitored(IEnumerable<int> ids, bool monitored)
        {
            var books = ids.Select(x => new Book { Id = x, Monitored = monitored }).ToList();
            SetFields(books, p => p.Monitored);
        }

        public Book FindByTitle(int authorMetadataId, string title)
        {
            var cleanTitle = Parser.Parser.CleanAuthorName(title);

            if (string.IsNullOrEmpty(cleanTitle))
            {
                cleanTitle = title;
            }

            return Query(s => (s.CleanTitle == cleanTitle || s.Title == title) && s.AuthorMetadataId == authorMetadataId)
                .ExclusiveOrDefault();
        }

        public List<Book> GetAuthorBooksWithFiles(Author author)
        {
            return Query(Builder()
                         .Join<Book, Edition>((b, e) => b.Id == e.BookId)
                         .Join<Edition, BookFile>((t, f) => t.Id == f.EditionId)
                         .Where<Book>(x => x.AuthorMetadataId == author.AuthorMetadataId)
                         .Where<Edition>(e => e.Monitored == true));
        }
    }
}
