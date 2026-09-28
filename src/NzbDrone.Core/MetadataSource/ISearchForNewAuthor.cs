using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public interface ISearchForNewAuthor
    {
        List<Author> SearchForNewAuthor(string title);
        List<Author> SearchForNewAuthor(string title, LibraryType library);
    }
}
