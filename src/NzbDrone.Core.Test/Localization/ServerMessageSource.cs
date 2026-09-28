using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace NzbDrone.Core.Test.Localization
{
    public enum ServerMessageArgKind
    {
        None,
        Literal,
        Interpolated,
        Concatenation,
        Unresolved
    }

    public class ServerMessageSite
    {
        public string File { get; set; }
        public int Line { get; set; }
        public string Kind { get; set; }
        public string Category { get; set; }
        public ServerMessageArgKind ArgKind { get; set; }
        public string Expression { get; set; }
        public string Template { get; set; }
        public string ClassName { get; set; }

        public override string ToString()
        {
            return $"{File}:{Line} [{Category}]";
        }
    }

    // Server messages (2026-09-26): every call site that builds a user-visible server message (spec
    // section 3 plus plan ruling R9), with its message argument read from the source. Comments and the
    // insides of string literals are masked before matching, so a commented-out call is not a site.
    public static class ServerMessageSource
    {
        private static readonly string[] Roots = { "src/NzbDrone.Core/", "src/Readarr.Api.V1/", "src/Readarr.Http/" };

        private static readonly string[] TestOtherRoots =
        {
            "src/NzbDrone.Core/Indexers/", "src/NzbDrone.Core/ImportLists/", "src/NzbDrone.Core/Notifications/",
            "src/NzbDrone.Core/Extras/", "src/NzbDrone.Core/Books/Calibre/"
        };

        private static readonly Regex Identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");
        private static readonly Regex ClassDeclaration = new Regex(@"\bclass\s+(?<name>\w+)");

        // Words that can stand before a name followed by ',' or ')' without declaring it (an argument).
        private static readonly HashSet<string> NotATypeName = new HashSet<string> { "ref", "out", "in", "return", "await", "throw", "new", "else", "yield", "case" };

        // Argument: >= 0 the call's argument index; -1 the expression after the pattern up to its end;
        // -2 an exception (NzbDroneClientException's and RestoreBackupFailedException's message is their 2nd
        // argument, the others' their 1st). Task 8 (2026-09-27): the exceptions that reach the API with their
        // own text (BookInfo, Goodreads, RestoreBackupFailed, MethodNotAllowed, UnsupportedMediaType), and an
        // exception constructor's own ": base(HttpStatusCode.X, …)" message (exceptionbase).
        private static readonly (string Kind, Regex Pattern, int Argument)[] Patterns =
        {
            ("reject", new Regex(@"\bDecision\.Reject\("), 0),
            ("rejection", new Regex(@"\bnew Rejection\("), 0),
            ("importresult", new Regex(@"\bnew ImportResult\("), 1),
            // Fix round 1 (2026-09-26): any receiver ENDING in trackedDownload/TrackedDownload, not only that
            // exact identifier -- a field like _trackedDownload.Warn( has no boundary before its "t" (the
            // preceding "_" is a word char too), so the old \b[tT]rackedDownload anchor missed it.
            ("warn", new Regex(@"\b\w*[tT]rackedDownload\.Warn\("), 0),
            ("progress", new Regex(@"\.Progress(?:Info|Debug|Trace)\("), 0),
            ("completion", new Regex(@"\bCompletionMessage\s*=>\s*"), -1),
            ("result", new Regex(@"\bResultMessage\s*=(?![=>])\s*"), -1),
            ("setresult", new Regex(@"\.SetResultMessage\("), 0),
            ("withmessage", new Regex(@"\.WithMessage\("), 0),
            ("defaulttemplate", new Regex(@"\bGetDefaultMessageTemplate\(\)\s*=>\s*"), -1),
            ("validationfailure", new Regex(@"\bnew (?:NzbDrone)?ValidationFailure\(|\bnew \((?=\s*""[^""\n]*""\s*,)"), 1),
            ("exception", new Regex(@"\bthrow new (?<type>BadRequest|NotFound|NzbDroneClient|Api|BookInfo|Goodreads|RestoreBackupFailed|MethodNotAllowed|UnsupportedMediaType)Exception\("), -2),
            ("exceptionbase", new Regex(@":\s*base\(HttpStatusCode\.\w+,\s*"), 0),
            ("servertext", new Regex(@"\bnew ServerText\("), 0)
        };

        public static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src", "NzbDrone.Core")) &&
                    File.Exists(Path.Combine(dir.FullName, "scripts", "i18n", "en_keys.py")))
                {
                    return dir.FullName;
                }
            }

            // the maintainer's test script rsyncs the whole tree (minus .git) to /src and the tests run from
            // /src/_tests/net6.0: the root is two levels up. Never skip: a guard that cannot see is a hole.
            Assert.Fail($"no repo root (src/NzbDrone.Core + scripts/i18n/en_keys.py) above {TestContext.CurrentContext.TestDirectory}");
            return null;
        }

        public static List<ServerMessageSite> Scan(string repoRoot)
        {
            var sites = new List<ServerMessageSite>();

            foreach (var root in Roots)
            {
                foreach (var file in Directory.EnumerateFiles(Path.Combine(repoRoot, root), "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

                    if (relative.Contains("/obj/") || relative.Contains("/bin/"))
                    {
                        continue;
                    }

                    sites.AddRange(ScanText(relative, File.ReadAllText(file)));
                }
            }

            return sites;
        }

        public static List<ServerMessageSite> ScanText(string relativePath, string source)
        {
            var masked = Mask(source);
            var sites = new List<ServerMessageSite>();

            foreach (var (kind, pattern, argument) in Patterns)
            {
                foreach (Match match in pattern.Matches(masked))
                {
                    var start = match.Index + match.Length;
                    var site = new ServerMessageSite
                    {
                        File = relativePath,
                        Line = 1 + source.Take(match.Index).Count(c => c == '\n'),
                        Kind = kind,
                        Category = Category(kind, relativePath)
                    };

                    string expression;

                    if (argument == -1)
                    {
                        expression = source.Substring(start, ExpressionEnd(source, start) - start);
                    }
                    else
                    {
                        var args = SplitArguments(source, start);
                        var type = match.Groups["type"].Value;
                        var index = argument == -2 ? (type == "NzbDroneClient" || type == "RestoreBackupFailed" ? 1 : 0) : argument;
                        expression = index < args.Count ? args[index] : null;
                    }

                    if (kind == "defaulttemplate")
                    {
                        site.ClassName = ClassDeclaration.Matches(masked.Substring(0, match.Index)).LastOrDefault()?.Groups["name"].Value;
                    }

                    site.Expression = expression == null ? null : Collapse(expression);

                    if (string.IsNullOrWhiteSpace(expression) || site.Expression == "null")
                    {
                        site.ArgKind = ServerMessageArgKind.None;
                    }
                    else
                    {
                        (site.ArgKind, site.Template) = Read(expression.Trim(), source, masked, match.Index, true);
                    }

                    sites.Add(site);
                }
            }

            return sites.OrderBy(s => s.Line).ToList();
        }

        private static string Category(string kind, string path)
        {
            switch (kind)
            {
                case "reject":
                case "rejection":
                    return path.StartsWith("src/NzbDrone.Core/DecisionEngine/", StringComparison.Ordinal) ? "decision" : "import";
                case "importresult":
                    return "import";
                case "warn":
                    return "queue";
                case "progress":
                case "completion":
                case "result":
                case "setresult":
                    return "progress";
                case "withmessage":
                case "defaulttemplate":
                    return "validation";
                case "validationfailure":
                    if (path.StartsWith("src/NzbDrone.Core/Download/", StringComparison.Ordinal))
                    {
                        return "test-download";
                    }

                    return TestOtherRoots.Any(r => path.StartsWith(r, StringComparison.Ordinal)) ? "test-other" : "validation";
                case "exception":
                case "exceptionbase":
                    return "api";
                default:
                    return kind;
            }
        }

        // The message argument, read as the guard needs it: a literal's text, or why there is none.
        private static (ServerMessageArgKind Kind, string Template) Read(string expression, string source, string masked, int at, bool resolve)
        {
            var literal = ReadLiteral(expression, 0);

            if (literal != null && literal.End == expression.Length)
            {
                return literal.Interpolated ? (ServerMessageArgKind.Interpolated, null) : (ServerMessageArgKind.Literal, literal.Text);
            }

            foreach (var (call, templateArgument) in new[] { ("new ServerText(", 0), ("string.Format(", 0), ("ServerText.WithEnglish(", 1), ("ServerRuleMessages.Format(", 0) })
            {
                if (expression.StartsWith(call, StringComparison.Ordinal))
                {
                    var args = SplitArguments(expression, call.Length);

                    return templateArgument < args.Count
                        ? Read(args[templateArgument].Trim(), source, masked, at, resolve)
                        : (ServerMessageArgKind.Unresolved, null);
                }
            }

            if (expression.Contains('"') && HasTopLevelPlus(expression))
            {
                return (ServerMessageArgKind.Concatenation, null);
            }

            if (resolve && Identifier.IsMatch(expression))
            {
                var initializer = Declaration(source, masked, at, expression);

                if (initializer != null)
                {
                    return Read(initializer.Trim(), source, masked, at, false);
                }
            }

            return (ServerMessageArgKind.Unresolved, null);
        }

        // The initializer of the nearest "const string|string|var|ServerText <name> =" before the site. Fix
        // round 1 (2026-09-26): no fallback to a later declaration -- that let an unrelated same-named
        // declaration elsewhere in the file (a method parameter has no declaration this regex matches at
        // all) stand in for a real one. With none preceding, the site is Unresolved, same as any other name
        // the guard can't trace. Task 8 (2026-09-27): nor does an earlier declaration in another method -- a
        // parameter of the same name ("string foreignBookId)") between that declaration and the site shadows it,
        // and the site is Unresolved (BookInfoProxy's PollBook parameter was read as a local minted 900 lines up).
        private static string Declaration(string source, string masked, int at, string name)
        {
            var declaration = new Regex(@"\b(?:const\s+string|string|var|ServerText)\s+" + Regex.Escape(name) + @"\s*=(?![=>])\s*");
            var match = declaration.Matches(masked).LastOrDefault(m => m.Index < at);

            if (match == null)
            {
                return null;
            }

            // A '>', ']' or '?' ends a type only right after a type character (List<int>, string[], int?): the
            // lookbehind rejects "x => id" and "a ?? id".
            var parameter = new Regex(@"(?<type>\b\w+|(?<=[\w>\[\]])[>\]?])\s+" + Regex.Escape(name) + @"\s*[,)]");

            if (parameter.Matches(masked).Any(m => m.Index > match.Index && m.Index < at && !NotATypeName.Contains(m.Groups["type"].Value)))
            {
                return null;
            }

            var start = match.Index + match.Length;

            return source.Substring(start, ExpressionEnd(source, start) - start);
        }

        private static string Collapse(string text)
        {
            return Regex.Replace(text.Trim(), @"\s+", " ");
        }

        // Comments and literal contents become spaces (newlines kept), so patterns only match code.
        public static string Mask(string source)
        {
            var masked = source.ToCharArray();
            var i = 0;

            while (i < source.Length)
            {
                var c = source[i];

                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    var end = source.IndexOf('\n', i);
                    end = end < 0 ? source.Length : end;
                    Blank(masked, i, end);
                    i = end;
                    continue;
                }

                if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    end = end < 0 ? source.Length : end + 2;
                    Blank(masked, i, end);
                    i = end;
                    continue;
                }

                if (c == '\'')
                {
                    i = SkipChar(source, i);
                    continue;
                }

                if (c == '"' || c == '$' || c == '@')
                {
                    var literal = ReadLiteral(source, i);

                    if (literal != null)
                    {
                        Blank(masked, source.IndexOf('"', i) + 1, literal.End - 1);
                        i = literal.End;
                        continue;
                    }
                }

                i++;
            }

            return new string(masked);
        }

        private static void Blank(char[] text, int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                if (text[i] != '\n')
                {
                    text[i] = ' ';
                }
            }
        }

        public sealed class Literal
        {
            public int End { get; set; }
            public bool Interpolated { get; set; }
            public string Text { get; set; }
        }

        // A C# string literal ("…", @"…", $"…", $@"…", @$"…") at start, decoded; null if there is none.
        // An interpolated literal's holes are skipped (their text is "{}"): the guard only needs to know it
        // is interpolated.
        public static Literal ReadLiteral(string s, int start)
        {
            var i = start;
            var interpolated = false;
            var verbatim = false;

            while (i < s.Length && (s[i] == '$' || s[i] == '@'))
            {
                interpolated |= s[i] == '$';
                verbatim |= s[i] == '@';
                i++;
            }

            if (i >= s.Length || s[i] != '"')
            {
                return null;
            }

            i++;
            var text = new StringBuilder();

            while (i < s.Length)
            {
                var c = s[i];

                if (interpolated && c == '{')
                {
                    if (i + 1 < s.Length && s[i + 1] == '{')
                    {
                        text.Append("{{");
                        i += 2;
                        continue;
                    }

                    i = SkipHole(s, i + 1);
                    text.Append("{}");
                    continue;
                }

                if (interpolated && c == '}' && i + 1 < s.Length && s[i + 1] == '}')
                {
                    text.Append("}}");
                    i += 2;
                    continue;
                }

                if (verbatim)
                {
                    if (c == '"')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '"')
                        {
                            text.Append('"');
                            i += 2;
                            continue;
                        }

                        return new Literal { End = i + 1, Interpolated = interpolated, Text = text.ToString() };
                    }

                    text.Append(c);
                    i++;
                    continue;
                }

                if (c == '\\' && i + 1 < s.Length)
                {
                    i = Unescape(s, i, text);
                    continue;
                }

                if (c == '"')
                {
                    return new Literal { End = i + 1, Interpolated = interpolated, Text = text.ToString() };
                }

                text.Append(c);
                i++;
            }

            return null;
        }

        private static int Unescape(string s, int i, StringBuilder text)
        {
            switch (s[i + 1])
            {
                case 'n':
                    text.Append('\n');
                    return i + 2;
                case 't':
                    text.Append('\t');
                    return i + 2;
                case 'r':
                    text.Append('\r');
                    return i + 2;
                case '0':
                    text.Append('\0');
                    return i + 2;
                case 'u':
                    text.Append((char)Convert.ToInt32(s.Substring(i + 2, 4), 16));
                    return i + 6;
                default:
                    text.Append(s[i + 1]);
                    return i + 2;
            }
        }

        // i is just after a hole's '{'; returns the index after its closing '}'.
        private static int SkipHole(string s, int i)
        {
            var depth = 0;

            while (i < s.Length)
            {
                var c = s[i];

                if (c == '"' || c == '$' || c == '@')
                {
                    var inner = ReadLiteral(s, i);

                    if (inner != null)
                    {
                        i = inner.End;
                        continue;
                    }
                }

                if (c == '\'')
                {
                    i = SkipChar(s, i);
                    continue;
                }

                if (c == '{' || c == '(' || c == '[')
                {
                    depth++;
                }
                else if (c == ')' || c == ']')
                {
                    depth--;
                }
                else if (c == '}')
                {
                    if (depth == 0)
                    {
                        return i + 1;
                    }

                    depth--;
                }

                i++;
            }

            return i;
        }

        private static int SkipChar(string s, int i)
        {
            var j = i + 1;

            j += j < s.Length && s[j] == '\\' ? 2 : 1;

            while (j < s.Length && s[j] != '\'')
            {
                j++;
            }

            return Math.Min(j + 1, s.Length);
        }

        // The top-level arguments of the call whose '(' ends just before start. Generic type arguments with a
        // comma inside an argument are not tracked; no message argument of a scanned call has one.
        public static List<string> SplitArguments(string s, int start)
        {
            var args = new List<string>();
            var depth = 0;
            var from = start;
            var i = start;

            while (i < s.Length)
            {
                var c = s[i];

                if (c == '"' || c == '$' || c == '@')
                {
                    var literal = ReadLiteral(s, i);

                    if (literal != null)
                    {
                        i = literal.End;
                        continue;
                    }
                }

                if (c == '\'')
                {
                    i = SkipChar(s, i);
                    continue;
                }

                if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                }
                else if (c == ')' || c == ']' || c == '}')
                {
                    if (depth == 0)
                    {
                        args.Add(s.Substring(from, i - from));
                        break;
                    }

                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    args.Add(s.Substring(from, i - from));
                    from = i + 1;
                }

                i++;
            }

            return args.Count == 1 && string.IsNullOrWhiteSpace(args[0]) ? new List<string>() : args;
        }

        // Where an expression that starts at start ends: its ';', a ',' at its own depth, or the bracket that
        // closes around it.
        private static int ExpressionEnd(string s, int start)
        {
            var depth = 0;
            var i = start;

            while (i < s.Length)
            {
                var c = s[i];

                if (c == '"' || c == '$' || c == '@')
                {
                    var literal = ReadLiteral(s, i);

                    if (literal != null)
                    {
                        i = literal.End;
                        continue;
                    }
                }

                if (c == '\'')
                {
                    i = SkipChar(s, i);
                    continue;
                }

                if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                }
                else if (c == ')' || c == ']' || c == '}')
                {
                    if (depth == 0)
                    {
                        return i;
                    }

                    depth--;
                }
                else if ((c == ';' || c == ',') && depth == 0)
                {
                    return i;
                }

                i++;
            }

            return s.Length;
        }

        private static bool HasTopLevelPlus(string s)
        {
            var depth = 0;
            var i = 0;

            while (i < s.Length)
            {
                var c = s[i];

                if (c == '"' || c == '$' || c == '@')
                {
                    var literal = ReadLiteral(s, i);

                    if (literal != null)
                    {
                        i = literal.End;
                        continue;
                    }
                }

                if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                }
                else if (c == ')' || c == ']' || c == '}')
                {
                    depth--;
                }
                else if (c == '+' && depth == 0)
                {
                    return true;
                }

                i++;
            }

            return false;
        }
    }
}
