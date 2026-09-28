using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Common.Localization
{
    // Server messages (UI translations v2, 2026-09-26): a message as its call site built it -- the format
    // template and its arguments -- so the API boundary can show it in the UI language. Every carrier keeps
    // the English string it always produced. English here is string.Format (current culture) when there are
    // arguments, or the template verbatim when there are none -- what Decision.Reject(string)/Rejection(string)
    // do. TrackedDownload.Warn(string, params object[]) formats even with no arguments, so a carrier that must
    // reproduce such a call passes its old English through WithEnglish instead of relying on this rule.
    public sealed class ServerText
    {
        // What a skeleton puts where a placeholder was. Never in real text.
        public const char Marker = '\u0001';

        // One placeholder body: {0}, {1,-5:N0}, or a named {size} / {PropertyName} (FluentValidation) /
        // {@value} (NLog), with an optional alignment and format.
        private static readonly Regex HoleBody = new Regex(@"^(?:(?<index>\d+)|(?<name>[@$]?[A-Za-z_][A-Za-z0-9_]*))(?:,(?<align>-?\d+))?(?::(?<format>[^{}]*))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public ServerText(string template, params object[] args)
            : this(null, template, args)
        {
        }

        private ServerText(string english, string template, object[] args)
        {
            Template = template ?? throw new ArgumentNullException(nameof(template));
            args ??= Array.Empty<object>();

            // Fix round 1 (2026-09-26): no arguments means no formatting, matching the non-formatting
            // Decision.Reject(string)/Rejection(string) overloads -- and it can't throw on a stray brace in a
            // literal (string.Format would).
            English = english ?? (args.Length == 0 ? template : string.Format(template, args));
            Args = args.Select(Snapshot).ToArray();
        }

        public string Template { get; }
        public object[] Args { get; }
        public string English { get; }

        public string Skeleton => Parse(Template).Skeleton;

        // A message whose English was rendered elsewhere (NLog's FormattedMessage for a progress message): its
        // template may hold named holes that string.Format cannot render.
        public static ServerText WithEnglish(string english, string template, params object[] args)
        {
            return new ServerText(english ?? throw new ArgumentNullException(nameof(english)), template, args);
        }

        public static string SkeletonOf(string template)
        {
            return Parse(template).Skeleton;
        }

        // The template's literal text (escaped braces unescaped, each placeholder one Marker) and its
        // placeholders in order. Used for call-site templates and for en.json's named-token values alike.
        public static ParsedTemplate Parse(string template)
        {
            var literal = new StringBuilder();
            var holes = new List<TemplateHole>();

            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];

                if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    literal.Append('{');
                    i++;
                    continue;
                }

                if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
                {
                    literal.Append('}');
                    i++;
                    continue;
                }

                if (c == '{')
                {
                    var end = template.IndexOf('}', i + 1);
                    var match = end > i + 1 ? HoleBody.Match(template.Substring(i + 1, end - i - 1)) : Match.Empty;

                    if (match.Success)
                    {
                        holes.Add(new TemplateHole(
                            match.Groups["index"].Success ? int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture) : (int?)null,
                            match.Groups["name"].Success ? match.Groups["name"].Value.TrimStart('@', '$') : null,
                            match.Groups["align"].Success ? match.Groups["align"].Value : null,
                            match.Groups["format"].Success ? match.Groups["format"].Value : null));

                        literal.Append(Marker);
                        i = end;
                        continue;
                    }
                }

                literal.Append(c);
            }

            return new ParsedTemplate(literal.ToString(), holes);
        }

        public override string ToString()
        {
            return English;
        }

        // Only what a later render needs: a string, an IFormattable value (numbers, dates, enums) or a nested
        // ServerText is kept; anything else becomes the text it rendered as, so a carrier never holds a live
        // object graph (a TrackedDownload lives as long as its download).
        private static object Snapshot(object arg)
        {
            return arg is null || arg is string || arg is ServerText || arg is IFormattable ? arg : arg.ToString();
        }
    }

    public sealed class ParsedTemplate
    {
        public ParsedTemplate(string skeleton, IReadOnlyList<TemplateHole> holes)
        {
            Skeleton = skeleton;
            Holes = holes;
        }

        public string Skeleton { get; }
        public IReadOnlyList<TemplateHole> Holes { get; }
    }

    public sealed class TemplateHole
    {
        public TemplateHole(int? index, string name, string alignment, string format)
        {
            Index = index;
            Name = name;
            Alignment = alignment;
            Format = format;
        }

        public int? Index { get; }
        public string Name { get; }
        public string Alignment { get; }
        public string Format { get; }

        // The hole re-based on argument 0: "{0,-5:N0}" for "{3,-5:N0}", "{0}" for "{size}".
        public string FormatSpec => "{0" + (Alignment == null ? string.Empty : "," + Alignment) + (Format == null ? string.Empty : ":" + Format) + "}";
    }
}
