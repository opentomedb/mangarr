using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.PdfConversion
{
    // Converts a manga library's PDF volumes to CBZ (so they carry ComicInfo and read as first-class
    // manga). It rewrites owned files and moves the originals aside. Runs as a daily scheduled task
    // (TaskManager, also Run Now on System -> Tasks), per series from a volume, or automatically
    // per-author when a download import lands a PDF (PdfImportConversionService). AuthorId null = whole library; set it to convert
    // one series. UpdateScheduledTask must stay at the base-class true: TaskManager only records
    // LastExecution for such commands, and without that the scheduler treats the daily task as
    // permanently overdue and refires it every tick.
    public class ConvertPdfToCbzCommand : Command
    {
        public int? AuthorId { get; set; }

        public ConvertPdfToCbzCommand()
        {
        }

        public ConvertPdfToCbzCommand(int? authorId)
        {
            AuthorId = authorId;
        }

        public override bool SendUpdatesToClient => true;

        public override string CompletionMessage => "Completed";
    }
}
