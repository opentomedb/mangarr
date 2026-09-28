using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Languages;

namespace NzbDrone.Core.Parser
{
    public static class IsoLanguages
    {
        private static readonly HashSet<IsoLanguage> All = new HashSet<IsoLanguage>
                                                           {
                                                               new IsoLanguage("en", "", "eng", "English", Language.English),
                                                               new IsoLanguage("fr", "fr", "fra", "French", Language.French),
                                                               new IsoLanguage("es", "", "spa", "Spanish", Language.Spanish),
                                                               new IsoLanguage("de", "de", "deu", "German", Language.German),
                                                               new IsoLanguage("it", "", "ita", "Italian", Language.Italian),
                                                               new IsoLanguage("da", "", "dan", "Danish", Language.Danish),
                                                               new IsoLanguage("nl", "", "nld", "Dutch", Language.Dutch),
                                                               new IsoLanguage("ja", "", "jpn", "Japanese", Language.Japanese),
                                                               new IsoLanguage("is", "", "isl", "Icelandic", Language.Icelandic),
                                                               new IsoLanguage("zh", "cn", "zho", "Chinese", Language.Chinese),

                                                               // Preferred Edition (2026-09-25, M15 polish): OpenTome's markets carry a
                                                               // "zh-TW" line count distinct from "zh" -- give it a display name so
                                                               // EditionLanguages.Name("zh-TW") isn't the raw code. TwoLetterCode alone
                                                               // ("zh") is safe to share, but CountryCode is NOT: LocalizationService.
                                                               // GetSetLanguageFileName() builds "zh_{CountryCode}" from
                                                               // IsoLanguages.Get(Language.Chinese), and only returns "zh_CN" correctly
                                                               // because this "cn" entry stays first in the set (HashSet<T> enumerates
                                                               // in insertion order with no removals -- pinned by a test, not a language
                                                               // guarantee). Never add a second Language.Chinese entry before this one.
                                                               // i18n leftovers (2026-09-28): same reasoning for "zh-HK" (OpenTome/Google
                                                               // also use it) -- added after "tw", so "cn" is still first.
                                                               new IsoLanguage("zh", "tw", "zho", "Chinese (Taiwan)", Language.Chinese),
                                                               new IsoLanguage("zh", "hk", "zho", "Chinese (Hong Kong)", Language.Chinese),
                                                               new IsoLanguage("ru", "", "rus", "Russian", Language.Russian),
                                                               new IsoLanguage("pl", "", "pol", "Polish", Language.Polish),
                                                               new IsoLanguage("vi", "", "vie", "Vietnamese", Language.Vietnamese),
                                                               new IsoLanguage("sv", "", "swe", "Swedish", Language.Swedish),
                                                               new IsoLanguage("no", "", "nor", "Norwegian", Language.Norwegian),
                                                               new IsoLanguage("nb", "", "nob", "Norwegian Bokmal", Language.Norwegian),
                                                               new IsoLanguage("fi", "", "fin", "Finnish", Language.Finnish),
                                                               new IsoLanguage("tr", "", "tur", "Turkish", Language.Turkish),
                                                               new IsoLanguage("pt", "pt", "por", "Portuguese", Language.Portuguese),
                                                               new IsoLanguage("el", "", "ell", "Greek", Language.Greek),
                                                               new IsoLanguage("ko", "", "kor", "Korean", Language.Korean),
                                                               new IsoLanguage("hu", "", "hun", "Hungarian", Language.Hungarian),
                                                               new IsoLanguage("he", "", "heb", "Hebrew", Language.Hebrew),
                                                               new IsoLanguage("cs", "", "ces", "Czech", Language.Czech),
                                                               new IsoLanguage("hi", "", "hin", "Hindi", Language.Hindi),
                                                               new IsoLanguage("th", "", "tha", "Thai", Language.Thai),
                                                               new IsoLanguage("bg", "", "bul", "Bulgarian", Language.Bulgarian),
                                                               new IsoLanguage("ro", "", "ron", "Romanian", Language.Romanian),
                                                               new IsoLanguage("pt", "br", "", "Portuguese (Brazil)", Language.PortugueseBR),
                                                               new IsoLanguage("ar", "", "ara", "Arabic", Language.Arabic)
                                                           };

        public static IsoLanguage Find(string isoCode)
        {
            var isoArray = isoCode.Split('-');

            var langCode = isoArray[0].ToLower();

            if (langCode.Length == 2)
            {
                //Lookup ISO639-1 code
                var isoLanguages = All.Where(l => l.TwoLetterCode == langCode).ToList();

                if (isoArray.Length > 1)
                {
                    isoLanguages = isoLanguages.Any(l => l.CountryCode == isoArray[1].ToLower()) ?
                        isoLanguages.Where(l => l.CountryCode == isoArray[1].ToLower()).ToList() : isoLanguages.Where(l => string.IsNullOrEmpty(l.CountryCode)).ToList();
                }

                return isoLanguages.FirstOrDefault();
            }
            else if (langCode.Length == 3)
            {
                //Lookup ISO639-2T code
                return All.FirstOrDefault(l => l.ThreeLetterCode == langCode);
            }

            return null;
        }

        public static IsoLanguage Get(Language language)
        {
            return All.FirstOrDefault(l => l.Language == language);
        }

        public static IsoLanguage FindByName(string name)
        {
            return All.FirstOrDefault(l => l.EnglishName.Equals(name.Trim(), StringComparison.InvariantCultureIgnoreCase));
        }
    }
}
