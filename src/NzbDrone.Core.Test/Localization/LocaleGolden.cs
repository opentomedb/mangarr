using System.IO;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace NzbDrone.Core.Test.Localization
{
    // UI translations v1 (2026-09-25): English captured from the code before a routing change, then held
    // byte-identical. A missing golden is written next to the tests (golden-out/<name>.json, fetched
    // back from the dev server) and the test fails -- how the golden is captured; never hand-write one.
    public static class LocaleGolden
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        public static void Pin(string name, object actual)
        {
            var json = JsonConvert.SerializeObject(actual, Settings).Replace("\r\n", "\n");
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Localization", name + ".json");

            if (!File.Exists(path))
            {
                var outFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, "golden-out");
                Directory.CreateDirectory(outFolder);
                File.WriteAllText(Path.Combine(outFolder, name + ".json"), json + "\n");

                Assert.Fail($"GOLDEN MISSING {name}.json (written to golden-out/{name}.json)");
            }

            json.Should().Be(File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n'),
                $"the English of '{name}' must stay byte-identical (UI translations ship rule)");
        }
    }
}
