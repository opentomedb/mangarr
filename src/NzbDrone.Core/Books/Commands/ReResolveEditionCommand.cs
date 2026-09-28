using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Commands
{
    // Preferred Edition (2026-09-24, spec §4): change the edition of these series (Edit Series, or the
    // series list's Change Edition). Each is previewed again at run time and skipped when blocked
    // (D9: different numbering with files attached). Rename = the preview's "Rename to" box (manga).
    public class ReResolveEditionCommand : Command
    {
        public List<int> AuthorIds { get; set; } = new List<int>();
        public string Language { get; set; }
        public bool Rename { get; set; }

        public override bool SendUpdatesToClient => true;
        public override string CompletionMessage => "Completed";

        // Fix round 1 (2026-09-24, I1): two changes to one series must not interleave their writes.
        public override bool IsTypeExclusive => true;
    }
}
