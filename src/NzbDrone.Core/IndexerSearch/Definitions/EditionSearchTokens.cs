using System;
using System.Collections.Generic;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    // Preferred Edition (2026-09-24, spec §3): how a market's releases name a volume. Padded is the
    // BookQuery tier's first query, unpadded its second -- the pair v05 / v5 had. Tokens are data: a
    // new market is one entry here (and one in Parser/EditionVolumeTokens). Query form: '+' joins words.
    public static class EditionSearchTokens
    {
        public static bool Has(string language)
        {
            return language == "fr" || language == "de" || language == "ja";
        }

        public static string Token(string language, string number, bool padded)
        {
            switch (language)
            {
                case "fr":
                    return padded ? $"T{number}" : $"Tome+{number}";
                case "de":
                    return padded ? $"Band+{number}" : $"Bd+{number}";
                case "ja":
                    return $"第{number}巻";
                default:
                    return $"v{number}";
            }
        }

        // An edition's own function words: a French alias keeps "de/la/les", a German one "der/die/das".
        public static IReadOnlyCollection<string> FunctionWords(string language)
        {
            switch (language)
            {
                case "fr":
                    return new[] { "de", "del", "des", "la", "le", "les", "du" };
                case "de":
                    return new[] { "der", "die", "das", "dem", "den", "des", "und", "ein", "eine" };
                default:
                    return Array.Empty<string>();
            }
        }
    }
}
