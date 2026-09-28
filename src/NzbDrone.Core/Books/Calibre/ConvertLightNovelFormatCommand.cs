using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Calibre
{
    // Design D3 (backfill on request, never automatic): runs calibre conversions, one book at a time, to the
    // configured delivery format for light-novel ebooks that do not already have it. AuthorId null
    // = the whole light-novel library, registered as a manual-only System -> Tasks entry (settings
    // tidy, 2026-09-23; TaskManager, Interval 0, always listed there); set it to convert one series,
    // pushed from the series toolbar's "Convert to <FORMAT>" button (not offered when the setting is
    // EPUB). Either way the service itself refuses with a ResultMessage when the setting is EPUB or
    // ebooks go to the entry folder (ConvertLightNovelFormatService.Execute).
    public class ConvertLightNovelFormatCommand : Command
    {
        public int? AuthorId { get; set; }

        public ConvertLightNovelFormatCommand()
        {
        }

        public ConvertLightNovelFormatCommand(int? authorId)
        {
            AuthorId = authorId;
        }

        public override bool SendUpdatesToClient => true;

        // A per-series run must not stamp the library-wide task's LastExecution on System -> Tasks.
        public override bool UpdateScheduledTask => !AuthorId.HasValue;
    }
}
