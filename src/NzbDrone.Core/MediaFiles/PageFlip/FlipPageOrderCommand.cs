using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.PageFlip
{
    // Rewrites one volume's CBZ with its page order reversed. Manual, per-volume, and its own
    // undo (flipping is a pure reorder — running it twice restores the original sequence), so a
    // mis-detected page order can be corrected or reverted from the UI without touching backups.
    public class FlipPageOrderCommand : Command
    {
        public int BookFileId { get; set; }

        public FlipPageOrderCommand()
        {
        }

        public FlipPageOrderCommand(int bookFileId)
        {
            BookFileId = bookFileId;
        }

        public override bool SendUpdatesToClient => true;

        public override bool UpdateScheduledTask => false;

        public override string CompletionMessage => "Completed";
    }
}
