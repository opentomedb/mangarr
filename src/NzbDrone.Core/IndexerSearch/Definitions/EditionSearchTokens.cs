using System;
using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    // Preferred Edition (2026-09-24, spec §3): how a market's releases name a volume. Padded is the
    // BookQuery tier's first query, unpadded its second -- the pair v05 / v5 had. Tokens are data: a
    // new market is one entry here (and one in Parser/EditionVolumeTokens). Query form: '+' joins words.
    public static class EditionSearchTokens
    {
        public static bool Has(string language)
        {
            switch (EditionLanguages.BaseCode(language))
            {
                case "fr":
                case "de":
                case "ja":
                case "ko":
                case "zh":
                    return true;
                default:
                    return false;
            }
        }

        public static string Token(string language, string number, bool padded)
        {
            switch (EditionLanguages.BaseCode(language))
            {
                case "fr":
                    return padded ? $"T{number}" : $"Tome+{number}";
                case "de":
                    return padded ? $"Band+{number}" : $"Bd+{number}";
                case "ja":
                    return $"第{number}巻";
                case "ko":
                    // KR/CN piece 2 (2026-10-02, M5): "05권" is how Korean releases are numbered; "제5권" the formal form.
                    return padded ? $"{number}권" : $"제{number}권";
                case "zh":
                    return padded ? $"第{number}卷" : $"{number}卷";
                default:
                    return $"v{number}";
            }
        }

        // An edition's own function words: a French alias keeps "de/la/les", a German one "der/die/das".
        public static IReadOnlyCollection<string> FunctionWords(string language)
        {
            switch (EditionLanguages.BaseCode(language))
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
