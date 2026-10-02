using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Books
{
    // KR/CN piece 2 (2026-10-02, M4): where a work comes from -- as AniList's countryOfOrigin spells it
    // (JP / KR / CN / TW) and as OpenTome's medium implies it. Only manhwa / manhua imply an origin: "manga"
    // is the catalogue's default label and 8 works carry it beside manhwa/manhua lines, so it is no evidence
    // of a Japanese origin (spec §3.1). CN and TW are one market for this purpose.
    public static class Origin
    {
        public const string Japan = "JP";
        public const string Korea = "KR";
        public const string China = "CN";
        public const string Taiwan = "TW";

        public static string OfMedium(string medium)
        {
            switch (medium?.Trim().ToLowerInvariant())
            {
                case "manhwa":
                    return Korea;
                case "manhua":
                    return China;
                default:
                    return null;
            }
        }

        public static bool IsKoreanOrChinese(string origin)
        {
            var code = Normalize(origin);

            return code == Korea || code == China || code == Taiwan;
        }

        // True when either side is unknown: a missing origin never rejects anything.
        public static bool Agrees(string expected, string actual)
        {
            var e = Normalize(expected);
            var a = Normalize(actual);

            if (e == null || a == null)
            {
                return true;
            }

            return e == a || (IsChinese(e) && IsChinese(a));
        }

        private static bool IsChinese(string code)
        {
            return code == China || code == Taiwan;
        }

        private static string Normalize(string code)
        {
            return code.IsNullOrWhiteSpace() ? null : code.Trim().ToUpperInvariant();
        }
    }
}
