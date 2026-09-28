using System.Collections.Concurrent;
using NzbDrone.Common.Localization;

namespace NzbDrone.Core.Validation
{
    // Server messages (2026-09-26): a FluentValidation rule message built from values fixed when the rule is
    // built (a seed-ratio minimum, a URL-base example). FluentValidation carries only the rendered string, so
    // the template is remembered by that exact English; the set is small and bounded (one entry per distinct
    // rule message).
    public static class ServerRuleMessages
    {
        private static readonly ConcurrentDictionary<string, ServerText> Templates = new ConcurrentDictionary<string, ServerText>();

        public static string Format(string template, params object[] args)
        {
            var text = new ServerText(template, args);

            Templates.TryAdd(text.English, text);

            return text.English;
        }

        public static ServerText Find(string english)
        {
            return english != null && Templates.TryGetValue(english, out var text) ? text : null;
        }
    }
}
