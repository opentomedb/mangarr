namespace NzbDrone.Core.MetadataSource.Gcd
{
    // What the settings page shows about the loaded metadata artifact: identity, provenance and
    // the attribution its sources require. Read from the artifact's `meta` table plus two counts.
    public class GcdArtifactInfo
    {
        public bool Available { get; set; }
        public string Path { get; set; }
        public string Version { get; set; }        // meta.gcd_dump
        public string Generator { get; set; }      // "opentome" | "gcd" | null
        public string Source { get; set; }
        public string Attribution { get; set; }
        public string Licence { get; set; }
        public string GeneratedAt { get; set; }
        public string SchemaVersion { get; set; }
        public int SeriesCount { get; set; }
        public int VolumeCount { get; set; }
    }
}
