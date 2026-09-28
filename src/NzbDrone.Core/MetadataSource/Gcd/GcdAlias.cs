namespace NzbDrone.Core.MetadataSource.Gcd
{
    // Preferred Edition (2026-09-24): one series_alias row with OpenTome's v0 language / kind (line |
    // official | alias | abbreviation | romanized | correction). Both null on an older artifact.
    public class GcdAlias
    {
        public string Alias { get; set; }
        public string Language { get; set; }
        public string Kind { get; set; }
    }
}
