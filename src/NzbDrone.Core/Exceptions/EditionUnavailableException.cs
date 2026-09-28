using NzbDrone.Common.Exceptions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Exceptions
{
    // Preferred Edition (2026-09-24): a series bound to (or added in) a non-English edition the
    // catalogue has no line of now. Like NotInCatalogueException it is NOT an AuthorNotFoundException:
    // a refresh catches it as a Warn (RefreshAuthorService) and leaves the entry exactly as stored --
    // never English volumes in place of the edition's -- and the add turns it into a validation message.
    public class EditionUnavailableException : NzbDroneException
    {
        public string Name { get; }
        public string Language { get; }

        // The bound line that could not be found (null on an add, which has none) -- named in the Warn
        // the search and refresh paths log (M5 pre-review fix).
        public string TomeLineId { get; }

        public EditionUnavailableException(string name, string language)
            : base("No {0} edition of '{1}' in the catalogue", EditionLanguages.Name(language), name)
        {
            Name = name;
            Language = language;
        }

        public EditionUnavailableException(string name, string language, string tomeLineId)
            : this(name, language)
        {
            TomeLineId = tomeLineId;
        }
    }
}
