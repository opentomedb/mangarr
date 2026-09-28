using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Commands
{
    // One display title (2026-09-23, the maintainer): re-align calibre's Title/sort and Audiobookshelf's
    // title with the entry -- no calibre/ABS matching, no rescan, no report -- for one light-novel
    // entry (AuthorId) or (AuthorId == null) the whole library. AuthorId null is registered as a
    // manual-only System -> Tasks entry (settings tidy, 2026-09-23; TaskManager, Interval 0) so every
    // existing calibre/ABS item can catch up without waiting for its next import/retag/adoption pass.
    public class SyncLightNovelTitlesCommand : Command
    {
        public int? AuthorId { get; set; }

        public SyncLightNovelTitlesCommand()
        {
        }

        public SyncLightNovelTitlesCommand(int? authorId)
        {
            AuthorId = authorId;
        }

        public override string CompletionMessage => "Completed";

        // A per-entry run must not stamp the library-wide task's LastExecution on System -> Tasks.
        public override bool UpdateScheduledTask => !AuthorId.HasValue;
    }
}
