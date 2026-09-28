using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.AuthorStats
{
    public class AuthorStatistics : ResultSet
    {
        public int AuthorId { get; set; }
        public int BookFileCount { get; set; }
        public int BookCount { get; set; }
        public int AvailableBookCount { get; set; }
        public int TotalBookCount { get; set; }
        public long SizeOnDisk { get; set; }
        public int AudioBookCount { get; set; }
        public int AudioBookFileCount { get; set; }

        // Any audio file at all: what "not yet" turns into "x / y" on. (The editable flag is
        // Author.AudioAvailable; this is what the files say.)
        public bool AudioAvailable { get; set; }
        public List<BookStatistics> BookStatistics { get; set; }
    }
}
