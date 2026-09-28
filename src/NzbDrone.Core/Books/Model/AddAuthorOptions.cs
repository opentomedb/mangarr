namespace NzbDrone.Core.Books
{
    public class AddAuthorOptions : MonitoringOptions
    {
        public bool SearchForMissingBooks { get; set; }

        // Preferred Edition (2026-09-24): the edition the Add form picked (null = Auto, the chain).
        // AddAuthorService hands it to the resolve; persisted with the rest of AddOptions.
        public string EditionLanguage { get; set; }
    }
}
