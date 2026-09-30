using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Commands
{
    public class RefreshAuthorCommand : Command
    {
        public int? AuthorId { get; set; }
        public bool IsNewAuthor { get; set; }

        // Review fixes (2026-09-28, I1): the refresh Switch Line queues writes no file tags, even with Write
        // Audio/Book Tags = Sync (RefreshEditionService); the switch's prompt is the only calibre/ABS/tag write.
        public bool SkipTagSync { get; set; }

        public RefreshAuthorCommand()
        {
        }

        public RefreshAuthorCommand(int? authorId, bool isNewAuthor = false)
        {
            AuthorId = authorId;
            IsNewAuthor = isNewAuthor;
        }

        public override bool SendUpdatesToClient => true;

        public override bool UpdateScheduledTask => !AuthorId.HasValue;

        public override string CompletionMessage => "Completed";
    }
}
