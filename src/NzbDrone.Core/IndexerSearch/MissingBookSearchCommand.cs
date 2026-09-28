using NzbDrone.Core.Books;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch
{
    public class MissingBookSearchCommand : Command
    {
        public int? AuthorId { get; set; }

        // Wanted edition filter (2026-09): one edition class only; null (the scheduled run and
        // every caller before) means every wanted class, the search it always was.
        public MediaType? MediaType { get; set; }

        // Search All scope (2026-09-21): one library only; null (the scheduled run and every
        // caller before) means both libraries, the search it always was.
        public LibraryType? Library { get; set; }

        // The Both preset (2026-09-21): true searches only the volumes whose EPUB AND audiobook
        // are both missing -- exactly what that page lists. False (the scheduled run and every
        // caller before) is every wanted volume, the search it always was.
        public bool BothEditions { get; set; }

        public override bool SendUpdatesToClient => true;

        public MissingBookSearchCommand()
        {
        }

        public MissingBookSearchCommand(int authorId)
        {
            AuthorId = authorId;
        }
    }
}
