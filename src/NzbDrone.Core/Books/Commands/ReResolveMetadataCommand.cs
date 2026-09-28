using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books.Commands
{
    // Repair action: wrong-but-plausible dates/pages survive refresh forever because the
    // ratchet keeps stored values when a fresh resolve returns null, and the lookup cache
    // re-supplies the same wrong edition. This command nulls the target fields first and
    // busts the cache, so the following refresh has nothing stale to keep.
    //
    // Rebind (D6, 2026-09-15): recompute every series' AniList binding (or one author's) with
    // the stored id cleared, log old -> new, write and refresh only the ones that moved. Dates
    // and pages are not touched on that path. AuthorId is optional so the pass can be library-
    // wide: POST /api/v1/command {"name":"ReResolveMetadata","rebind":true}.
    public class ReResolveMetadataCommand : Command
    {
        public int? AuthorId { get; set; }
        public int? BookId { get; set; }
        public bool Rebind { get; set; }

        public ReResolveMetadataCommand()
        {
        }

        public ReResolveMetadataCommand(int authorId, int? bookId = null)
        {
            AuthorId = authorId;
            BookId = bookId;
        }

        public override bool SendUpdatesToClient => true;
        public override string CompletionMessage => "Completed";
    }
}
