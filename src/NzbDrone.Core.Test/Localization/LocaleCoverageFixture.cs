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
    // UI translations v1 (2026-09-25): 290 en.json keys landed in 73 commits after the Readarr fork and
    // none reached fr, de or ja. A locale joins CompletedLocales in the commit that finishes its file;
    // from then on every new en key, and every token in it, is an obligation for that locale.
    [TestFixture]
    public class LocaleCoverageFixture
    {
        // Task 11 adds "fr", Task 12 "de", Task 13 "ja".
        private static readonly string[] CompletedLocales = { "fr", "de", "ja" };

        // The token rule both ends use (LocalizationService.TokenRegex, translate.ts).
        private static readonly Regex Token = new Regex(@"\{([a-z0-9]+?)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string CoreFolder => Path.Combine(TestContext.CurrentContext.TestDirectory, "Localization", "Core");

        // Ordered pairs, not a dictionary: a duplicate or a case twin must stay visible.
        private static List<KeyValuePair<string, string>> Entries(string file)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));

            return document.RootElement.EnumerateObject()
                .Select(p => new KeyValuePair<string, string>(p.Name, p.Value.GetString()))
                .ToList();
        }

        private static Dictionary<string, string> Load(string locale)
        {
            return Entries(Path.Combine(CoreFolder, locale + ".json"))
                .GroupBy(e => e.Key)
                .ToDictionary(g => g.Key, g => g.Last().Value);
        }

        // Case-sensitive: both ends look a token up by its exact name. appName is always supplied,
        // so a translation may say {appName} where the English says Mangarr.
        private static string Tokens(string value)
        {
            return string.Join(",", Token.Matches(value)
                .Select(m => m.Groups[1].Value)
                .Where(t => t != "appName")
                .Distinct()
                .OrderBy(t => t, StringComparer.Ordinal));
        }

        [Test]
        public void no_locale_file_has_case_twins_or_empty_values()
        {
            var problems = new List<string>();

            foreach (var file in Directory.GetFiles(CoreFolder, "*.json"))
            {
                var name = Path.GetFileName(file);
                var entries = Entries(file);

                problems.AddRange(entries
                    .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"{name}: case twins {string.Join(" / ", g.Select(e => e.Key))}"));

                problems.AddRange(entries
                    .Where(e => string.IsNullOrEmpty(e.Value))
                    .Select(e => $"{name}: {e.Key} is empty"));
            }

            problems.Should().BeEmpty("the backend lookup ignores case, so a twin replaces a value, and an empty value renders the raw key");
        }

        [Test]
        public void completed_locales_have_every_en_key_with_the_same_tokens()
        {
            var en = Load("en");
            var problems = new List<string>();

            foreach (var locale in CompletedLocales)
            {
                var values = Load(locale);

                foreach (var (key, english) in en)
                {
                    if (!values.TryGetValue(key, out var value))
                    {
                        problems.Add($"{locale}: {key} missing");
                        continue;
                    }

                    if (Tokens(value) != Tokens(english))
                    {
                        problems.Add($"{locale}: {key} tokens [{Tokens(value)}], en [{Tokens(english)}]");
                    }
                }
            }

            problems.Should().BeEmpty();
        }

        [Test]
        public void completed_locales_are_shipped_files()
        {
            CompletedLocales.Where(l => !File.Exists(Path.Combine(CoreFolder, l + ".json"))).Should().BeEmpty();
        }

        [Test]
        public void english_helper_reads_the_shipped_en_json()
        {
            EnglishLocalization.Create().GetLocalizedString("UiLanguage").Should().Be("UI Language");
        }
    }
}
