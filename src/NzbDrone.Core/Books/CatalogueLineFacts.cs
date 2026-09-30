namespace NzbDrone.Core.Books
{
    // Line safety (2026-09-28): what a catalogue-backed Add result shows under its title -- the bound
    // line's volume count, its publisher (null when the catalogue has none) and, when the line is not its
    // work's main line in that market and medium, the main line's name. Read from the local catalogue only.
    public class CatalogueLineFacts
    {
        public int VolumeCount { get; set; }
        public string Publisher { get; set; }
        public string SpinOffOf { get; set; }
    }
}
