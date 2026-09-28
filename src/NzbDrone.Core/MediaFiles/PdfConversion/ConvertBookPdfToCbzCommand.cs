using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.PdfConversion
{
    // Book Details action: convert ONE volume's PDF to CBZ on demand. Deliberately a separate
    // command from ConvertPdfToCbzCommand: (1) queue dedupe pools by Name first, so this can
    // never be swallowed by a queued/running sweep via CommandEqualityComparer's both-null
    // short-circuit, and a non-nullable BookId compares correctly between two per-book pushes;
    // (2) TaskManager matches scheduled tasks by command TypeName, so per-book runs don't
    // reset the nightly sweep's LastExecution.
    public class ConvertBookPdfToCbzCommand : Command
    {
        public int BookId { get; set; }

        public ConvertBookPdfToCbzCommand()
        {
        }

        public ConvertBookPdfToCbzCommand(int bookId)
        {
            BookId = bookId;
        }

        public override bool SendUpdatesToClient => true;
        public override string CompletionMessage => "Completed";
    }
}
