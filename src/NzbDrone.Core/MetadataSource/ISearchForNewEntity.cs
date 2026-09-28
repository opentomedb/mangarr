using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public interface ISearchForNewEntity
    {
        List<object> SearchForNewEntity(string title);
        List<object> SearchForNewEntity(string title, LibraryType library);
    }
}
