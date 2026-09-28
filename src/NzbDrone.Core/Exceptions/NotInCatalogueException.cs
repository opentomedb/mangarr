using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.Exceptions
{
    // A light novel the metadata catalogue has no English novel line for (D11). Deliberately NOT
    // an AuthorNotFoundException: that one makes RefreshAuthorService treat the author as gone
    // (deleted when it has no files); this one lands in the refresh's generic catch and leaves the
    // entry alone. The add path turns it into "Not in the catalogue as a light novel yet".
    public class NotInCatalogueException : NzbDroneException
    {
        public string Name { get; }

        // Preferred Edition (2026-09-24, spec §2.3): the refusal came from a non-English chain request
        // that found a novel line in none of its languages -- the add says "no novel line in any chain
        // language" instead of today's English-only text.
        public bool NoChainLine { get; }

        public NotInCatalogueException(string name)
            : base("'{0}' is not in the catalogue as a light novel yet", name)
        {
            Name = name;
        }

        public NotInCatalogueException(string name, bool noChainLine)
            : base(noChainLine ? "'{0}': no novel line in any chain language" : "'{0}' is not in the catalogue as a light novel yet", name)
        {
            Name = name;
            NoChainLine = noChainLine;
        }
    }
}
