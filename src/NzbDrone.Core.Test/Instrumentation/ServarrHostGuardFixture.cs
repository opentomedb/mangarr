using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Localization;

namespace NzbDrone.Core.Test.Instrumentation
{
    // Beta readiness (2026-09-28, E4): Mangarr sends nothing to Servarr. Upstream Readarr reported crashes
    // to sentry.servarr.com (backend and frontend) and polled its service host; both are gone. This guard
    // scans the shipped source -- every non-test C# project under src/ and frontend/src -- for any
    // servarr.com host, so neither can come back unnoticed. Each remaining mention is allow-listed below
    // with its reason.
    [TestFixture]
    public class ServarrHostGuardFixture
    {
        private static readonly Regex ServarrHost = new Regex(@"[a-z0-9.-]*servarr\.com", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] FrontendExtensions = { ".js", ".jsx", ".ts", ".tsx", ".html", ".ejs" };

        private class Allowed
        {
            public string Host { get; set; }

            // Repo-relative paths ('/' separators); null = any file.
            public string[] Files { get; set; }
            public string Reason { get; set; }
        }

        private static readonly Allowed[] AllowList =
        {
            new Allowed
            {
                Host = "wiki.servarr.com",
                Files = null,
                Reason = "Readarr wiki documentation links (help links, System -> Status 'Wiki (based on Readarr)'). The user's browser opens them on a click; Mangarr never requests them."
            },
            new Allowed
            {
                Host = "auth.servarr.com",
                Files = new[]
                {
                    "src/NzbDrone.Core/ImportLists/Goodreads/GoodreadsSettingsBase.cs",
                    "src/NzbDrone.Core/Notifications/Goodreads/GoodreadsSettingsBase.cs"
                },
                Reason = "Upstream Goodreads OAuth signing endpoint. Neither Add Import List nor Add Notification offers Goodreads (both pickers reject /^Goodreads/), so it is reachable only through a Goodreads provider already stored in the database or created through the API."
            },
            new Allowed
            {
                Host = "readarr.servarr.com",
                Files = new[] { "src/NzbDrone.Core/Update/UpdatePackageProvider.cs" },
                Reason = "A comment naming the upstream update feed that the disabled in-app updater deliberately does not use; no code."
            }
        };

        private static bool IsTestProject(string projectFolder)
        {
            return projectFolder.Split('.').Any(part => part == "Test");
        }

        private static IEnumerable<string> ShippedSourceFiles(string root)
        {
            foreach (var project in Directory.GetDirectories(Path.Combine(root, "src")))
            {
                if (IsTestProject(Path.GetFileName(project)))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
                {
                    yield return file;
                }
            }

            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "frontend", "src"), "*", SearchOption.AllDirectories))
            {
                if (FrontendExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }

        private static List<(string File, int Line, string Host)> Mentions()
        {
            var root = ServerMessageSource.RepoRoot();
            var found = new List<(string File, int Line, string Host)>();

            foreach (var file in ShippedSourceFiles(root))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

                if (relative.Contains("/obj/") || relative.Contains("/bin/"))
                {
                    continue;
                }

                var lines = File.ReadAllLines(file);

                for (var i = 0; i < lines.Length; i++)
                {
                    foreach (Match match in ServarrHost.Matches(lines[i]))
                    {
                        found.Add((relative, i + 1, match.Value.ToLowerInvariant()));
                    }
                }
            }

            return found;
        }

        private static bool IsAllowed(Allowed allowed, string file, string host)
        {
            return allowed.Host == host && (allowed.Files == null || allowed.Files.Contains(file));
        }

        [Test]
        public void scanner_sees_the_shipped_source()
        {
            var root = ServerMessageSource.RepoRoot();
            var files = ShippedSourceFiles(root).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToList();

            files.Should().Contain("src/NzbDrone.Common/Instrumentation/NzbDroneLogger.cs");
            files.Should().Contain("frontend/src/Store/Middleware/middlewares.js");
            files.Should().NotContain(f => f.StartsWith("src/NzbDrone.Core.Test/"));
        }

        [Test]
        public void no_servarr_host_outside_the_allow_list()
        {
            var unexpected = Mentions()
                .Where(m => !AllowList.Any(a => IsAllowed(a, m.File, m.Host)))
                .Select(m => $"{m.File}:{m.Line} {m.Host}")
                .ToList();

            unexpected.Should().BeEmpty("Mangarr sends nothing to Servarr; a new servarr.com host needs a reason in AllowList");
        }

        [Test]
        public void every_allow_list_entry_still_matches_a_mention()
        {
            var mentions = Mentions();

            AllowList.Where(a => !mentions.Any(m => IsAllowed(a, m.File, m.Host)))
                .Select(a => a.Host + (a.Files == null ? string.Empty : " in " + string.Join(", ", a.Files)))
                .Should().BeEmpty("a stale allow-list entry would silently permit a future mention");

            AllowList.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Reason));
        }
    }
}
