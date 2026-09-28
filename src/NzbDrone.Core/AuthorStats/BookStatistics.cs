using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.AuthorStats
{
    public class BookStatistics : ResultSet
    {
        public int AuthorId { get; set; }
        public int BookId { get; set; }
        public int BookFileCount { get; set; }
        public int BookCount { get; set; }
        public int AvailableBookCount { get; set; }
        public int TotalBookCount { get; set; }
        public long SizeOnDisk { get; set; }

        // Light novels (2026-09): the Audio edition's side, 0/1 per book. BookFileCount and
        // AvailableBookCount keep meaning "the primary (non-audio) edition has a file".
        public int AudioBookCount { get; set; }
        public int AudioBookFileCount { get; set; }
    }
}
