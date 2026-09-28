using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Core.Test.Localization
{
    // Server messages (2026-09-26), spec section 5: the 41 locale files other than en/fr/de/ja after the
    // rebrand. A value whose key's English changed meaning since upstream Readarr (0b79d300, committed as
    // Files/Localization/readarr-upstream-en.json) is gone, so the key falls back to English.
    [TestFixture]
    public class OtherLocalesFixture
    {
        private static readonly string[] Finished = { "en", "fr", "de", "ja" };

        private static string CoreFolder => Path.Combine(TestContext.CurrentContext.TestDirectory, "Localization", "Core");

        private static Dictionary<string, string> Load(string path)
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }

        private static List<string> OtherLocales()
        {
            return Directory.GetFiles(CoreFolder, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(l => !Finished.Contains(l))
                .OrderBy(l => l, StringComparer.Ordinal)
                .ToList();
        }

        // As scripts/i18n/other_locales.py folds: the app name, v1's one typo fix, letter case and spacing
        // never change a meaning.
        private static string Fold(string text)
        {
            text = text.Replace("{appName}", "Readarr").Replace("Mangarr", "Readarr").Replace(" the the ", " the ");

            return string.Join(" ", text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        }

        [Test]
        public void there_are_41_other_locales()
        {
            OtherLocales().Should().HaveCount(41);
        }

        [Test]
        public void no_other_locale_keeps_a_value_whose_english_changed_meaning()
        {
            var en = Load(Path.Combine(CoreFolder, "en.json"));
            var upstream = Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Localization", "readarr-upstream-en.json"));
            var problems = new List<string>();

            foreach (var locale in OtherLocales())
            {
                foreach (var key in Load(Path.Combine(CoreFolder, locale + ".json")).Keys)
                {
                    if (!en.ContainsKey(key))
                    {
                        problems.Add($"{locale}: {key} is not in en.json");
                    }
                    else if (upstream.TryGetValue(key, out var old) && Fold(old) != Fold(en[key]))
                    {
                        problems.Add($"{locale}: {key}");
                    }
                }
            }

            problems.Should().BeEmpty();
        }

        [TestCase("es", @"\bAutor")]
        [TestCase("es", "Libros")]
        [TestCase("pt_BR", "Livros")]
        [TestCase("zh_CN", "作者")]
        [TestCase("zh_CN", "书籍")]
        public void a_named_readarr_era_word_is_gone(string locale, string pattern)
        {
            Load(Path.Combine(CoreFolder, locale + ".json"))
                .Where(e => Regex.IsMatch(e.Value, pattern))
                .Select(e => e.Key)
                .Should().BeEmpty();
        }

        [Test]
        public void no_other_locale_says_readarr()
        {
            OtherLocales()
                .SelectMany(l => Load(Path.Combine(CoreFolder, l + ".json")).Where(e => e.Value.Contains("Readarr", StringComparison.OrdinalIgnoreCase)).Select(e => $"{l}: {e.Key}"))
                .Should().BeEmpty();
        }

        // Task 9 review round (2026-09-27): a value naming a different *arr app (radarr/lidarr/sonarr,
        // any case) is a stale seed from the app it was translated for before being reused for Readarr
        // (cs ReplaceIllegalCharactersHelpText still said "radarr will remove them instead").
        [Test]
        public void no_other_locale_names_another_arr_app()
        {
            var otherArr = new Regex(@"radarr|lidarr|sonarr", RegexOptions.IgnoreCase);

            OtherLocales()
                .SelectMany(l => Load(Path.Combine(CoreFolder, l + ".json")).Where(e => otherArr.IsMatch(e.Value)).Select(e => $"{l}: {e.Key}"))
                .Should().BeEmpty();
        }
    }
}
