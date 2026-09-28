using NzbDrone.Core.Books;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch
{
    public class AuthorSearchCommand : Command
    {
        public int AuthorId { get; set; }

        // Light novels (2026-09): one edition class only; null (every caller before, and every
        // manga search) means every monitored class, the search it always was.
        public MediaType? MediaType { get; set; }

        public override bool SendUpdatesToClient => true;
    }
}
