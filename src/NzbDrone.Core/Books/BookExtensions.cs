using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Books
{
    public static class BookExtensions
    {
        // The edition of this media type, if the book has one (a manga volume has no Audio edition).
        public static Edition EditionOf(this Book book, MediaType mediaType)
        {
            return book?.Editions?.Value?.FirstOrDefault(e => e.MediaType == mediaType);
        }

        // The ONE edition that supplies book-level display fields (title, cover, overview,
        // resource foreignEditionId, search title): the monitored Ebook or Archive edition, else
        // the first monitored one, else the first. Every former Single(x => x.Monitored) site
        // that needs one edition uses this; with one edition per book (manga) it is that edition.
        public static Edition PrimaryEdition(this Book book)
        {
            return PrimaryOf(book?.Editions?.Value);
        }

        public static Edition PrimaryOf(IEnumerable<Edition> editions)
        {
            if (editions == null)
            {
                return null;
            }

            var list = editions.ToList();

            return list.FirstOrDefault(e => e.Monitored && e.MediaType != MediaType.Audio)
                   ?? list.FirstOrDefault(e => e.Monitored)
                   ?? list.FirstOrDefault();
        }
    }
}
