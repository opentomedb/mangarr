using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Commands
{
    // Adopt Existing (one copy each, 2026-09-20; the copy-in of 2026-09, D12, before it): register
    // what the maintainer already owns -- EPUBs in Calibre (found through its content server) and audiobooks
    // in Audiobookshelf (through its API + the mount) -- with the light-novel entry IN PLACE, then
    // rescan. Nothing is copied; a copy left over from the copy-in era is moved to the holding
    // folder, never deleted. One light-novel entry (AuthorId) or all of them, registered as a
    // manual-only System -> Tasks entry (settings tidy follow-up, 2026-09-23; TaskManager,
    // Interval 0). The command name stays: the frontend and AuthorScannedHandler reference it.
    public class ImportExistingLightNovelsCommand : Command
    {
        public int? AuthorId { get; set; }

        // Copy-in hold (2026-09-16, D2): the user's "search on add" intent, run by the command once
        // the hold is clear (never while the copy-in is still running or has errors).
        public bool SearchAfter { get; set; }

        public ImportExistingLightNovelsCommand()
        {
        }

        public ImportExistingLightNovelsCommand(int? authorId)
        {
            AuthorId = authorId;
        }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override string CompletionMessage => "Completed";

        // A per-entry run must not stamp the library-wide task's LastExecution on System -> Tasks.
        public override bool UpdateScheduledTask => !AuthorId.HasValue;
    }
}
