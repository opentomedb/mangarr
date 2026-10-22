namespace NzbDrone.Core.Parser.Model
{
    // Volumes built from chapter packs (2026-10-21): the chapters a chapter release carries
    // ("Series c001-073 (Digital)" = 1-73; a single chapter has Start == End). IsDigital = the
    // title says "(Digital)"; IsScanlation = it says scan / scans / scanlation.
    public class ChapterRange
    {
        public double Start { get; set; }
        public double End { get; set; }
        public bool IsDigital { get; set; }
        public bool IsScanlation { get; set; }

        public override string ToString()
        {
            return Start == End
                ? $"chapter {MangaVolumeParser.Format(Start)}"
                : $"chapters {MangaVolumeParser.Format(Start)}-{MangaVolumeParser.Format(End)}";
        }
    }
}
