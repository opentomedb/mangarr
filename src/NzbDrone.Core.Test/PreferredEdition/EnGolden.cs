using System;
using System.IO;
using System.Text;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace NzbDrone.Core.Test.PreferredEdition
{
    // Preferred Edition (2026-09-24): the ship rule's goldens. Each section is the JSON of what the
    // code produced for representative ENGLISH series before the feature existed; every task of the
    // feature must leave it byte-identical. A missing golden fails with the actual JSON as one
    // base64 line (a console logger may re-indent multi-line messages), which is how Task M1
    // captured them -- never hand-write one.
    public static class EnGolden
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateFormatString = "yyyy-MM-ddTHH:mm:ssZ",
            NullValueHandling = NullValueHandling.Include,
            Formatting = Formatting.Indented
        };

        public static void Pin(string section, object actual)
        {
            var json = JsonConvert.SerializeObject(actual, Settings).Replace("\r\n", "\n");
            var name = $"en-{section}.json";
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "PreferredEdition", name);

            if (!File.Exists(path))
            {
                Assert.Fail($"GOLDEN MISSING {name} base64:{Convert.ToBase64String(Encoding.UTF8.GetBytes(json))}");
            }

            json.Should().Be(File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n'),
                $"the English output for '{section}' must stay byte-identical (Preferred Edition ship rule)");
        }
    }
}
