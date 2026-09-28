namespace NzbDrone.Core.Books
{
    // Preferred Edition (2026-09-24): one language a series' work has a release line in -- what the
    // Add form and Edit Series offer ("French · 34 vols").
    public class EditionOption
    {
        public string Language { get; set; }
        public string Name { get; set; }
        public int VolumeCount { get; set; }
        public string LineName { get; set; }
    }
}
