using System.Collections.Generic;
using NzbDrone.Core.MetadataSource.Gcd;

namespace NzbDrone.Core.Test.MetadataSource.Gcd
{
    // Line safety (2026-09-28): the Trapped in a Dating Sim work as the catalogue has it (ids and counts
    // from the 2026-09-28 incident), plus a manga line and a French line of the same work so the market and
    // medium rules have something to exclude. Fresh objects per call: tests may change them.
    public static class OtomeLines
    {
        public const string WorkId = "w_37e31f1b2001";
        public const string MainId = "rl_224c4428cda7";
        public const string SpinOffId = "rl_b816b634db1a";
        public const string MainName = "Trapped in a Dating Sim: The World of Otome Games Is Tough for Mobs";
        // The real catalogue name (staging, 2026-09-28): its parenthetical repeats the shared prefix.
        public const string SpinOffName = "Trapped in a Dating Sim: The World of Otome Games Is Tough for Mobs (Trapped in a Dating Sim: Otome Games Are Tough For Us, Too!)";

        public static GcdSeries MainLine() => new GcdSeries
        {
            GcdSeriesId = 5101, Name = MainName + " (light novel)", Publisher = "Seven Seas Entertainment", Language = "en",
            VolumeCount = 13, Medium = "light_novel", IsMain = true, TomeId = MainId, TomeWorkId = WorkId, AnilistId = 101
        };

        public static GcdSeries SpinOff() => new GcdSeries
        {
            GcdSeriesId = 5102, Name = SpinOffName, Publisher = "Seven Seas Entertainment", Language = "en",
            VolumeCount = 6, Medium = "light_novel", IsMain = false, TomeId = SpinOffId, TomeWorkId = WorkId, AnilistId = 102
        };

        public static GcdSeries Manga() => new GcdSeries
        {
            GcdSeriesId = 5103, Name = MainName, Publisher = "Seven Seas Entertainment", Language = "en",
            VolumeCount = 12, Medium = "manga", IsMain = true, TomeId = "rl_otome_manga", TomeWorkId = WorkId
        };

        public static GcdSeries French() => new GcdSeries
        {
            GcdSeriesId = 5104, Name = MainName, LocalName = "Otome Game Sekai wa Mob ni Kibishii Sekai desu", Language = "fr",
            VolumeCount = 8, Medium = "light_novel", IsMain = true, TomeId = "rl_otome_fr", TomeWorkId = WorkId
        };

        public static List<GcdSeries> Work() => new List<GcdSeries> { MainLine(), SpinOff(), Manga(), French() };
    }
}
