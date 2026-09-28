using System.Collections.Generic;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Test.Framework
{
    // Light novels (2026-09): decisions resolve the edition of their media type, so a test Book
    // needs editions. Adds one edition of the given media type (with its files, never null) and
    // returns it. Ids default to <bookId>*10 + mediaType + 1 so two editions of one book differ.
    public static class BookEditionTestExtensions
    {
        public static Edition WithEdition(this Book book, MediaType mediaType, List<BookFile> files = null, int id = 0, bool monitored = true)
        {
            var edition = new Edition
            {
                Id = id > 0 ? id : (book.Id * 10) + (int)mediaType + 1,
                BookId = book.Id,
                MediaType = mediaType,
                Monitored = monitored,
                Book = book,
                BookFiles = files ?? new List<BookFile>()
            };

            var editions = book.Editions != null && book.Editions.IsLoaded && book.Editions.Value != null
                ? book.Editions.Value
                : new List<Edition>();

            editions.Add(edition);
            book.Editions = editions;

            return edition;
        }
    }
}
