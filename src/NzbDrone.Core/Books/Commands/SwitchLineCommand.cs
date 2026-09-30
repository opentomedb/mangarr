using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Commands
{
    // Line safety (2026-09-28): rebind one series to another release line of the same work (Fix Match's
    // Switch Line). The run re-checks everything the list showed, writes the binding and the line's name,
    // and queues the refresh that re-mints the volumes; files stay on the volume with the same number.
    // The last four properties are the run's answer, read by the client off the completed command: the
    // files now on the new line's volumes (for the optional calibre/Audiobookshelf update), the volumes
    // whose files had no counterpart, whether to offer that update, and the refresh to wait for first.
    public class SwitchLineCommand : Command
    {
        public int AuthorId { get; set; }
        public string TomeLineId { get; set; }

        public bool Switched { get; set; }
        public List<int> MovedBookFileIds { get; set; } = new List<int>();
        public List<string> KeptVolumes { get; set; } = new List<string>();
        public bool OfferExternalSync { get; set; }
        public int? RefreshCommandId { get; set; }

        public override bool SendUpdatesToClient => true;
        public override string CompletionMessage => "Completed";

        // Two switches must not interleave their writes (as ReResolveEditionCommand).
        public override bool IsTypeExclusive => true;
    }
}
