using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Books
{
    // Preferred Edition (2026-09-24): OpenTome's language codes (MARKET_LANG: "en", "fr", "de", "ja",
    // "pt-BR", ...) as Mangarr stores and compares them. English is the default everywhere: a null or
    // blank code IS English, which is how every existing series reads (spec §4).
    public static class EditionLanguages
    {
        public const string English = "en";

        private static readonly Regex Code = new Regex(@"^[a-z]{2}(?:-[A-Z]{2})?$", RegexOptions.Compiled);

        public static bool IsEnglish(string code)
        {
            return code.IsNullOrWhiteSpace() || code.Trim().Equals(English, StringComparison.OrdinalIgnoreCase);
        }

        public static string Of(AuthorMetadata metadata)
        {
            return IsEnglish(metadata?.EditionLanguage) ? English : metadata.EditionLanguage.Trim();
        }

        // The global setting (Settings -> UI -> Preferred Edition): ordered, de-duplicated, malformed
        // entries dropped; nothing left -> English.
        public static IReadOnlyList<string> ParseChain(string csv)
        {
            var codes = (csv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => Code.IsMatch(c))
                .Distinct()
                .ToList();

            return codes.Any() ? codes : new List<string> { English };
        }

        public static bool IsValidChain(string csv)
        {
            if (csv.IsNullOrWhiteSpace())
            {
                return false;
            }

            var parts = csv.Split(',').Select(c => c.Trim()).ToList();

            return parts.All(c => Code.IsMatch(c)) && parts.Distinct().Count() == parts.Count;
        }

        public static bool IsEnglishOnly(IReadOnlyList<string> chain)
        {
            return chain == null || chain.All(IsEnglish);
        }

        // ISO 639-3 as editions store it (Edition.Language, calibre `languages`, MetadataProfile).
        public static string ToIso3(string code)
        {
            return IsEnglish(code) ? "eng" : code.CanonicalizeLanguage() ?? "eng";
        }

        // Preferred Edition (2026-09-24, M6b pre-review fix): the reverse of ToIso3, for an edition's stored
        // Edition.Language ("jpn" -> "ja", "zho" -> "zh"). Null, blank, "eng" or an unknown code -> "en",
        // which keeps every English edition (and anything unrecognised) on the English rules.
        public static string FromIso3(string iso3)
        {
            if (iso3.IsNullOrWhiteSpace())
            {
                return English;
            }

            var code = iso3.Trim();

            return code.Length == 3 ? IsoLanguages.Find(code)?.TwoLetterCode ?? English : English;
        }

        // D4: the volume label belongs to the edition ("Tome 5"), never to the UI language.
        public static string VolumeLabel(string code, string volumeToken)
        {
            switch (IsEnglish(code) ? English : code.Trim())
            {
                case "fr":
                    return $"Tome {volumeToken}";
                case "de":
                    return $"Band {volumeToken}";
                case "ja":
                    return $"第{volumeToken}巻";
                default:
                    return $"Vol. {volumeToken}";
            }
        }

        public static string Name(string code)
        {
            return IsEnglish(code) ? "English" : IsoLanguages.Find(code.Trim())?.EnglishName ?? code.Trim();
        }
    }
}
