using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using NzbDrone.Common;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles
{
    public interface IMediaFileRepository : IBasicRepository<BookFile>
    {
        List<BookFile> GetFilesByAuthor(int authorId);
        List<BookFile> GetFilesByAuthorMetadataId(int authorMetadataId);
        List<BookFile> GetFilesByBook(int bookId);
        List<BookFile> GetFilesByBooks(IEnumerable<int> bookIds);
        List<BookFile> GetFilesByEdition(int editionId);
        List<BookFile> GetUnmappedFiles();
        int GetPdfFileCount();
        List<BookFile> GetFilesWithBasePath(string path);
        List<BookFile> GetFileWithPath(List<string> paths);
        BookFile GetFileWithPath(string path);
        void DeleteFilesByBook(int bookId);
        void UnlinkFilesByBook(int bookId);
    }

    public class MediaFileRepository : BasicRepository<BookFile>, IMediaFileRepository
    {
        public MediaFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        // always join with all the other good stuff
        // needed more often than not so better to load it all now
        protected override SqlBuilder Builder() => new SqlBuilder(_database.DatabaseType)
            .LeftJoin<BookFile, Edition>((b, e) => b.EditionId == e.Id)
            .LeftJoin<Edition, Book>((e, b) => e.BookId == b.Id)
            .LeftJoin<Book, Author>((book, author) => book.AuthorMetadataId == author.AuthorMetadataId)
            .LeftJoin<Author, AuthorMetadata>((a, m) => a.AuthorMetadataId == m.Id);

        protected override List<BookFile> Query(SqlBuilder builder) => Query(_database, builder).ToList();

        public static IEnumerable<BookFile> Query(IDatabase database, SqlBuilder builder)
        {
            return database.QueryJoined<BookFile, Edition, Book, Author, AuthorMetadata>(builder, (file, edition, book, author, metadata) => Map(file, edition, book, author, metadata));
        }

        private static BookFile Map(BookFile file, Edition edition, Book book, Author author, AuthorMetadata metadata)
        {
            file.Edition = edition;

            if (edition != null)
            {
                edition.Book = book;
            }

            if (author != null)
            {
                author.Metadata = metadata;
            }

            file.Author = author;

            return file;
        }

        public List<BookFile> GetFilesByAuthor(int authorId)
        {
            return Query(Builder().Where<Author>(a => a.Id == authorId));
        }

        public List<BookFile> GetFilesByAuthorMetadataId(int authorMetadataId)
        {
            return Query(Builder().Where<Book>(b => b.AuthorMetadataId == authorMetadataId));
        }

        public List<BookFile> GetFilesByBook(int bookId)
        {
            return Query(Builder().Where<Book>(b => b.Id == bookId));
        }

        public List<BookFile> GetFilesByBooks(IEnumerable<int> bookIds)
        {
            var ids = bookIds.ToList();

            if (!ids.Any())
            {
                return new List<BookFile>();
            }

            return Query(Builder().Where<Book>(b => ids.Contains(b.Id)));
        }

        public List<BookFile> GetFilesByEdition(int editionId)
        {
            return Query(Builder().Where<BookFile>(f => f.EditionId == editionId));
        }

        public List<BookFile> GetUnmappedFiles()
        {
            return _database.Query<BookFile>(new SqlBuilder(_database.DatabaseType).Select(typeof(BookFile))
                                              .Where<BookFile>(t => t.EditionId == 0)).ToList();
        }

        // Powers the manga index's Convert PDFs button, which only appears when there is something
        // to convert. It must count exactly what the sweep touches: PdfConversionService walks
        // MANGA authors -> books -> GetFilesByBook, whose join chain drops unmapped files (EditionId
        // 0), so the same joins are applied here. DISTINCT because two Author rows can share an
        // AuthorMetadataId and would otherwise count the same file twice. LN PDF (2026-09-22): a
        // light novel's PDF is its ebook and never converted, so it is not counted -- the library is
        // read off AuthorMetadata.ForeignAuthorId the way LibraryTypes.Parse reads it (a Contains).
        public int GetPdfFileCount()
        {
            var lightNovel = $"'%{LibraryTypes.LightNovelSuffix}%'";
            var sql = $@"SELECT COUNT(DISTINCT ""BookFiles"".""Id"") FROM ""{_table}""
                         INNER JOIN ""Editions"" ON ""BookFiles"".""EditionId"" = ""Editions"".""Id""
                         INNER JOIN ""Books"" ON ""Editions"".""BookId"" = ""Books"".""Id""
                         INNER JOIN ""Authors"" ON ""Books"".""AuthorMetadataId"" = ""Authors"".""AuthorMetadataId""
                         INNER JOIN ""AuthorMetadata"" ON ""Authors"".""AuthorMetadataId"" = ""AuthorMetadata"".""Id""
                         WHERE LOWER(""BookFiles"".""Path"") LIKE '%.pdf'
                           AND ""AuthorMetadata"".""ForeignAuthorId"" NOT LIKE {lightNovel}";

            using (var conn = _database.OpenConnection())
            {
                return conn.ExecuteScalar<int>(sql);
            }
        }

        public void DeleteFilesByBook(int bookId)
        {
            var fileIds = GetFilesByBook(bookId).Select(x => x.Id).ToList();
            Delete(x => fileIds.Contains(x.Id));
        }

        public void UnlinkFilesByBook(int bookId)
        {
            var files = GetFilesByBook(bookId);
            files.ForEach(x => x.EditionId = 0);
            SetFields(files, f => f.EditionId);
        }

        public List<BookFile> GetFilesWithBasePath(string path)
        {
            // ensure path ends with a single trailing path separator to avoid matching partial paths
            var safePath = path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return _database.Query<BookFile>(new SqlBuilder(_database.DatabaseType).Where<BookFile>(x => x.Path.StartsWith(safePath))).ToList();
        }

        public BookFile GetFileWithPath(string path)
        {
            return Query(x => x.Path == path).SingleOrDefault();
        }

        public List<BookFile> GetFileWithPath(List<string> paths)
        {
            // use more limited join for speed
            var builder = new SqlBuilder(_database.DatabaseType)
                .LeftJoin<BookFile, Edition>((f, t) => f.EditionId == t.Id);

            var all = _database.QueryJoined<BookFile, Edition>(builder, (file, book) => MapTrack(file, book)).ToList();

            var joined = all.Join(paths, x => x.Path, x => x, (file, path) => file, PathEqualityComparer.Instance).ToList();
            return joined;
        }

        private BookFile MapTrack(BookFile file, Edition book)
        {
            file.Edition = book;
            return file;
        }
    }
}
