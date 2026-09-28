using System.Collections.Generic;
using NzbDrone.Core.Books;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch
{
    public class BookSearchCommand : Command
    {
        public List<int> BookIds { get; set; }

        // Light novels (2026-09): one edition class only; null (every caller before, and every
        // manga search) means every monitored class, the search it always was.
        public MediaType? MediaType { get; set; }

        public override bool SendUpdatesToClient => true;

        public BookSearchCommand()
        {
        }

        public BookSearchCommand(List<int> bookIds)
        {
            BookIds = bookIds;
        }

        public BookSearchCommand(List<int> bookIds, MediaType? mediaType)
        {
            BookIds = bookIds;
            MediaType = mediaType;
        }
    }
}
