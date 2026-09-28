using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.Test.Localization
{
    // Server messages (2026-09-26): every user-visible server message has a Server* en.json template with the
    // same skeleton, or a scripts/i18n/server-keep.json entry saying why it stays English (spec section 3).
    [TestFixture]
    public class ServerMessageGuardFixture
    {
        // A category is enforced from the task that translates it: Task 2 adds "decision", Task 3 "import" and
        // "queue", Task 4 "progress", Task 5 "validation", Task 6 "test-download", Task 7 "test-other", Task 8
        // "api". "servertext" is on from the start: a new ServerText(...) exists only because a task converted
        // a call site, and that task adds its key in the same commit. Since Task 8 every category is enforced.
        private static readonly string[] Enforced = { "servertext", "decision", "import", "queue", "progress", "validation", "test-download", "test-other", "api" };

        private static List<ServerMessageSite> _sites;
        private static Dictionary<string, string> _en;
        private static ServerMessageIndex _index;
        private static List<KeepEntry> _keep;

        public class KeepEntry
        {
            public string File { get; set; }
            public string Arg { get; set; }
            public string Text { get; set; }
            public string Why { get; set; }
        }

        [OneTimeSetUp]
        public void Load()
        {
            var root = ServerMessageSource.RepoRoot();

            _sites = ServerMessageSource.Scan(root);
            _en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "src/NzbDrone.Core/Localization/Core/en.json")));
            _index = ServerMessageIndex.Build(_en);
            _keep = JsonSerializer.Deserialize<List<KeepEntry>>(File.ReadAllText(Path.Combine(root, "scripts/i18n/server-keep.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        private static bool Matches(KeepEntry entry, ServerMessageSite site)
        {
            return entry.File == site.File &&
                   (entry.Arg == "*" ||
                    (entry.Arg != null && entry.Arg == site.Expression) ||
                    (entry.Text != null && entry.Text == site.Template));
        }

        private static string Problem(ServerMessageSite site)
        {
            return Problem(site, _en, _index, _keep);
        }

        // Fix round 1 (2026-09-26): the pass/fail logic itself, parameterized so a self-test can exercise it
        // against a small fake en dictionary and keep list instead of the real repo's.
        private static string Problem(ServerMessageSite site, Dictionary<string, string> en, ServerMessageIndex index, List<KeepEntry> keep)
        {
            switch (site.ArgKind)
            {
                case ServerMessageArgKind.None:
                    return null;
                case ServerMessageArgKind.Interpolated:
                    return "interpolated: convert to a template with arguments";
                case ServerMessageArgKind.Concatenation:
                    return "concatenation: convert to a template with arguments";
                case ServerMessageArgKind.Unresolved:
                    return keep.Any(k => Matches(k, site)) ? null : "not a literal: trace it to a template, or add a server-keep.json entry";
            }

            if (keep.Any(k => Matches(k, site)))
            {
                return null;
            }

            var skeleton = ServerText.SkeletonOf(site.Template);

            if (site.Kind == "defaulttemplate")
            {
                var key = ServerMessageIndex.CustomValidatorKey(site.ClassName ?? string.Empty);

                return en.TryGetValue(key, out var value) && ServerText.SkeletonOf(value) == skeleton ? null : $"needs {key} with this template";
            }

            return index.KeyBySkeleton.ContainsKey(skeleton) ? null : "no Server* key has this template";
        }

        [Test]
        public void every_enforced_call_site_has_a_server_key_or_a_keep_entry()
        {
            var problems = _sites
                .Where(s => Enforced.Contains(s.Category))
                .Select(s => (Site: s, Problem: Problem(s)))
                .Where(p => p.Problem != null)
                .Select(p => $"{p.Site.File}:{p.Site.Line} [{p.Site.Category}] {p.Problem}: {p.Site.Template ?? p.Site.Expression}")
                .ToList();

            if (problems.Any())
            {
                Assert.Fail($"{problems.Count} server message call site(s):\n" + string.Join("\n", problems));
            }
        }

        // Fix round 1 (2026-09-26): the guard's own pass/fail logic, proven against fake data instead of the
        // real repo's en.json -- an unkeyed literal must be flagged, a keyed one (same skeleton as a fake
        // en entry) must not.
        [Test]
        public void the_guard_flags_an_unkeyed_literal_and_passes_a_keyed_one()
        {
            var sites = ServerMessageSource.ScanText("src/NzbDrone.Core/DecisionEngine/Specifications/X.cs",
                "trackedDownload.Warn(new ServerText(\"Unkeyed guard self-test literal {0}\", x));\ntrackedDownload.Warn(new ServerText(\"Keyed guard self-test literal {0}\", x));")
                .Where(s => s.Kind == "servertext")
                .ToList();

            var en = new Dictionary<string, string> { { "ServerTestGuardSelfTest", "Keyed guard self-test literal {value}" } };
            var index = ServerMessageIndex.Build(en);
            var keep = new List<KeepEntry>();

            sites.Should().HaveCount(2);
            Problem(sites[0], en, index, keep).Should().NotBeNull();
            Problem(sites[1], en, index, keep).Should().BeNull();
        }

        [Test]
        public void server_keep_entries_all_match_a_call_site()
        {
            _keep.Where(k => !_sites.Any(s => Matches(k, s)))
                .Select(k => $"{k.File}: {k.Arg ?? k.Text}")
                .Should().BeEmpty("a keep entry that matches nothing hides nothing and rots");
        }

        [Test]
        public void no_two_server_templates_share_a_skeleton()
        {
            _index.Duplicates.Should().BeEmpty("the localizer finds a message's key by its template's skeleton");
        }

        [Test]
        public void server_templates_use_named_alphanumeric_tokens_only()
        {
            _en.Where(e => ServerMessageIndex.IsServerKey(e.Key))
                .Where(e => e.Value.Contains("{{") ||
                            e.Value.Contains("}}") ||
                            ServerText.Parse(e.Value).Holes.Any(h => h.Name == null || !Regex.IsMatch(h.Name, "^[A-Za-z0-9]+$") || h.Format != null || h.Alignment != null))
                .Select(e => e.Key)
                .Should().BeEmpty("LocalizationService fills only {name} tokens of letters and digits; formats live at the call site");
        }

        // The scanner silently finding nothing would pass every check above.
        [TestCase("reject", 70)]
        [TestCase("rejection", 15)]
        [TestCase("warn", 8)]
        [TestCase("progress", 70)]
        [TestCase("completion", 12)]
        [TestCase("withmessage", 65)]
        [TestCase("defaulttemplate", 34)]
        [TestCase("validationfailure", 190)]
        [TestCase("exception", 45)]
        public void the_scan_sees_the_known_call_sites(string kind, int atLeast)
        {
            _sites.Count(s => s.Kind == kind).Should().BeGreaterOrEqualTo(atLeast);
        }
    }
}
