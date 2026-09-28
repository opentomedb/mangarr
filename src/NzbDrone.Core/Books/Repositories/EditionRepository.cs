using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface IEditionRepository : IBasicRepository<Edition>
    {
        List<Edition> GetAllMonitoredEditions();
        Edition FindByForeignEditionId(string foreignEditionId);
        List<Edition> FindByBook(IEnumerable<int> ids);
        List<Edition> FindByAuthor(int id);
        List<Edition> FindByAuthorMetadataId(int id, bool onlyMonitored);
        Edition FindByTitle(int authorMetadataId, string title);
        List<Edition> GetEditionsForRefresh(int bookId, List<string> foreignEditionIds);
        List<Edition> SetMonitored(Edition edition);
        List<Edition> GetCoveredBy(int authorMetadataId, double coveringVolume);
    }

    public class EditionRepository : BasicRepository<Edition>, IEditionRepository
    {
        public EditionRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<Edition> GetAllMonitoredEditions()
        {
            return Query(x => x.Monitored == true);
        }

        public Edition FindByForeignEditionId(string foreignEditionId)
        {
            var edition = Query(x => x.ForeignEditionId == foreignEditionId).SingleOrDefault();

            return edition;
        }

        public List<Edition> GetEditionsForRefresh(int bookId, List<string> foreignEditionIds)
        {
            return Query(r => r.BookId == bookId || foreignEditionIds.Contains(r.ForeignEditionId));
        }

        public List<Edition> FindByBook(IEnumerable<int> ids)
        {
            // populate the books and author metadata also
            // this hopefully speeds up the track matching a lot
            var builder = new SqlBuilder(_database.DatabaseType)
                .LeftJoin<Edition, Book>((e, b) => e.BookId == b.Id)
                .LeftJoin<Book, AuthorMetadata>((b, a) => b.AuthorMetadataId == a.Id)
                .Where<Edition>(r => ids.Contains(r.BookId));

            return _database.QueryJoined<Edition, Book, AuthorMetadata>(builder, (edition, book, metadata) =>
                    {
                        if (book != null)
                        {
                            book.AuthorMetadata = metadata;
                            edition.Book = book;
                        }

                        return edition;
                    }).ToList();
        }

        public List<Edition> FindByAuthor(int id)
        {
            return Query(Builder().Join<Edition, Book>((e, b) => e.BookId == b.Id)
                         .Join<Book, Author>((b, a) => b.AuthorMetadataId == a.AuthorMetadataId)
                         .Where<Author>(a => a.Id == id));
        }

        public List<Edition> FindByAuthorMetadataId(int authorMetadataId, bool onlyMonitored)
        {
            var builder = Builder().Join<Edition, Book>((e, b) => e.BookId == b.Id)
                .Where<Book>(b => b.AuthorMetadataId == authorMetadataId);

            if (onlyMonitored)
            {
                builder = builder.OrWhere<Edition>(e => e.Monitored == true);
                builder = builder.OrWhere<Book>(b => b.AnyEditionOk == true);
            }

            return Query(builder);
        }

        public Edition FindByTitle(int authorMetadataId, string title)
        {
            return Query(Builder().Join<Edition, Book>((e, b) => e.BookId == b.Id)
                .Where<Book>(b => b.AuthorMetadataId == authorMetadataId)
                .Where<Edition>(e => e.Monitored == true)
                .Where<Edition>(e => e.Title == title))
                .FirstOrDefault();
        }

        // Monitor `edition` and unmonitor only its SAME-media-type siblings: a light-novel volume
        // keeps its Ebook and Audio editions monitored side by side (at most one per media type).
        // A manga volume has one edition, so for it this is exactly the old "only this one".
        public List<Edition> SetMonitored(Edition edition)
        {
            var allEditions = FindByBook(new[] { edition.BookId });
            var mediaType = allEditions.FirstOrDefault(r => r.Id == edition.Id)?.MediaType ?? edition.MediaType;

            allEditions.Where(r => r.MediaType == mediaType).ToList().ForEach(r => r.Monitored = r.Id == edition.Id);
            Ensure.That(allEditions.GroupBy(x => x.MediaType).All(g => g.Count(x => x.Monitored) <= 1)).IsTrue();
            UpdateMany(allEditions);
            return allEditions;
        }

        // Covered volumes (2026-09-17, D4a): this author's Audio editions whose mark points at
        // `coveringVolume` — what CoveredVolumeService resets when that volume's file goes away.
        public List<Edition> GetCoveredBy(int authorMetadataId, double coveringVolume)
        {
            return Query(Builder().Join<Edition, Book>((e, b) => e.BookId == b.Id)
                .Where<Book>(b => b.AuthorMetadataId == authorMetadataId)
                .Where<Edition>(e => e.MediaType == MediaType.Audio)
                .Where<Edition>(e => e.CoveredByVolume == coveringVolume));
        }
    }
}
