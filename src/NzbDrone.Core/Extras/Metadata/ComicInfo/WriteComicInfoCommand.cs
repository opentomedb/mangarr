using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Extras.Metadata.ComicInfo
{
    // Backfills ComicInfo.xml into EXISTING library files. This is the only path that rewrites
    // files the user already owns, so it is manual/opt-in — never scheduled. AuthorId null = whole
    // library; set it to sync (or trial) a single series first.
    public class WriteComicInfoCommand : Command
    {
        public int? AuthorId { get; set; }

        public WriteComicInfoCommand()
        {
        }

        public WriteComicInfoCommand(int? authorId)
        {
            AuthorId = authorId;
        }

        public override bool SendUpdatesToClient => true;

        // UI pass (2026-09-24): the library-wide run (AuthorId null) is the manual "Embed Metadata"
        // task on System -> Tasks and records its Last Execution; a per-series run does not.
        public override bool UpdateScheduledTask => !AuthorId.HasValue;

        public override string CompletionMessage => "Completed";
    }
}
