using System;
using System.Globalization;
using System.Text;

namespace NzbDrone.Core.MetadataSource.Manga
{
    // The one title fold TitleMatcher.Normalize (the equality key) and TitleNormalizer.ForSearch (the
    // outbound search term) share -- a mirror of OpenTome's export/resolve_anilist.py fold()
    // (2026-09-24, main e7435fe). Two steps, in this order:
    //
    //   StripLatinMarks: NFD; a combining mark (Mn) is dropped ONLY when its base letter is
    //     Latin-script; then NFC. "Fushigi Yûgi" -> "Fushigi Yugi", "Übel Blatt" -> "Ubel Blatt",
    //     "Ōoku" -> "Ooku". Kana voicing marks (ゲ stays ゲ), Hangul, Cyrillic (й), CJK keep
    //     theirs; a string with nothing to drop comes back as it was, byte for byte (an
    //     unconditional NFC would turn the compatibility ideograph U+F900 into U+8C48).
    //   FoldNumeric: a code point in Unicode category No / Nl becomes its NFKC form, and the
    //     fraction slash U+2044 becomes "/": "Ranma ½" -> "Ranma 1/2" (AniList's own romaji;
    //     the key then drops the "/": ranma12), "Ⅱ" -> "II", "x²" -> "x2". Nothing else is
    //     NFKC'd -- no full-width, no ligatures.
    public static class TitleFold
    {
        // "Latin-script letter" -- Python reads it off the Unicode name (isalpha() and "LATIN" in
        // unicodedata.name(c)); .NET has no name table, so the set is spelled out here. It is
        // Python's set at Unicode 13.0 (Python 3.9, where it was generated) CLIPPED to these blocks,
        // range for range. OpenTome's CI runs Python 3.12 / Unicode 15, whose set also has
        // A7C0-A7C1, A7D0-A7D9 and Latin Extended-G (1DF00+); they are not in this table, and no
        // real title uses them.
        //   Basic Latin letters             0041-005A 0061-007A
        //   Latin-1 Supplement letters      00C0-00D6 00D8-00F6 00F8-00FF (not ª U+00AA, µ U+00B5, º U+00BA)
        //   Latin Extended-A, -B, IPA Ext.  0100-02AF
        //   Latin Extended Additional       1E00-1EFF
        //   Latin Extended-C                2C60-2C7C 2C7E-2C7F (not U+2C7D, a modifier letter)
        //   Latin Extended-D                A722-A76F A771-A787 A78B-A7BF A7C2-A7CA A7F5-A7F7 A7FA-A7FF
        //                                   (not the modifier letters/tone letters A720-A721 A770 A788-A78A A7F8-A7F9)
        //   Latin Extended-E                AB30-AB5A AB60-AB64 AB66-AB68 (not the modifiers AB5B-AB5F AB69-AB6B)
        // Deliberate divergences -- letters Python also calls LATIN that are NOT in this table, so a
        // mark on them is kept here and dropped there: full-width Latin (FF21-FF3A FF41-FF5A),
        // Phonetic Extensions (1D00-1D25 1D62-1D65 1D6B-1D77 1D79-1D9A), super/subscript letters
        // (2071 207F 2090-209C), U+2184, the ligatures FB00-FB06, the Glagolitic "latinate myslite"
        // (2C2E 2C5E), and anything Unicode added after 13.0. A title spelling one of those WITH a
        // combining mark has never been seen; the table stays fixed whatever ICU the host runs.
        private static readonly int[] LatinRanges =
        {
            0x0041, 0x005A,
            0x0061, 0x007A,
            0x00C0, 0x00D6,
            0x00D8, 0x00F6,
            0x00F8, 0x02AF,
            0x1E00, 0x1EFF,
            0x2C60, 0x2C7C,
            0x2C7E, 0x2C7F,
            0xA722, 0xA76F,
            0xA771, 0xA787,
            0xA78B, 0xA7BF,
            0xA7C2, 0xA7CA,
            0xA7F5, 0xA7F7,
            0xA7FA, 0xA7FF,
            0xAB30, 0xAB5A,
            0xAB60, 0xAB64,
            0xAB66, 0xAB68
        };

        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s) || IsAscii(s))
            {
                return s ?? string.Empty;
            }

            try
            {
                return FoldNumeric(StripLatinMarks(s));
            }
            catch (ArgumentException)
            {
                // string.Normalize throws on ill-formed UTF-16 (a lone surrogate in an indexer
                // title): such a title is compared unfolded rather than breaking the caller.
                return s;
            }
        }

        public static bool IsLatin(int codePoint)
        {
            for (var i = 0; i < LatinRanges.Length; i += 2)
            {
                if (codePoint < LatinRanges[i])
                {
                    return false;
                }

                if (codePoint <= LatinRanges[i + 1])
                {
                    return true;
                }
            }

            return false;
        }

        public static string StripLatinMarks(string s)
        {
            var nfd = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(nfd.Length);
            var baseIsLatin = false;
            var dropped = false;

            for (var i = 0; i < nfd.Length; i += char.IsSurrogatePair(nfd, i) ? 2 : 1)
            {
                var width = char.IsSurrogatePair(nfd, i) ? 2 : 1;

                if (CharUnicodeInfo.GetUnicodeCategory(nfd, i) == UnicodeCategory.NonSpacingMark)
                {
                    if (baseIsLatin)
                    {
                        dropped = true;
                        continue;
                    }
                }
                else
                {
                    baseIsLatin = width == 1 && IsLatin(nfd[i]);
                }

                sb.Append(nfd, i, width);
            }

            return dropped ? sb.ToString().Normalize(NormalizationForm.FormC) : s;
        }

        public static string FoldNumeric(string s)
        {
            var sb = new StringBuilder(s.Length);

            for (var i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1)
            {
                var width = char.IsSurrogatePair(s, i) ? 2 : 1;
                var category = CharUnicodeInfo.GetUnicodeCategory(s, i);

                if (category == UnicodeCategory.OtherNumber || category == UnicodeCategory.LetterNumber)
                {
                    sb.Append(s.Substring(i, width).Normalize(NormalizationForm.FormKC));
                }
                else
                {
                    sb.Append(s, i, width);
                }
            }

            return sb.Replace('⁄', '/').ToString();
        }

        private static bool IsAscii(string s)
        {
            foreach (var c in s)
            {
                if (c > 0x7F)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
