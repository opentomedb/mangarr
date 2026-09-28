using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.Messaging.Commands
{
    // UI pass (2026-09-24, SY-1): the display name System -> Tasks and the queued-task list show for a
    // command. Readarr split the class name on camel case, which leaks the Author/Book model names
    // ("Refresh Author", "Missing Book Search") and odd casing ("Convert Pdf To Cbz", "Rss Sync").
    // Display only: the API keeps TaskName/Name as the class names, so Run Now and clients that match
    // on them are unchanged.
    public static class CommandDisplayName
    {
        private static readonly Dictionary<string, string> Overrides = new ()
        {
            { "WriteComicInfo", "Embed Metadata" },
            { "ReResolveMetadata", "Re-resolve Metadata" },
            { "ReResolveEdition", "Change Edition" }
        };

        private static readonly Dictionary<string, string> Words = new ()
        {
            { "Author", "Series" },
            { "Authors", "Series" },
            { "Book", "Volume" },
            { "Books", "Volumes" },
            { "Pdf", "PDF" },
            { "Cbz", "CBZ" },
            { "Rss", "RSS" }
        };

        public static string For(string commandName)
        {
            var name = StripSuffix(commandName);

            if (Overrides.TryGetValue(name, out var display))
            {
                return display;
            }

            var words = name.SplitCamelCase().Split(' ').Select(w => Words.TryGetValue(w, out var word) ? word : w).ToList();

            // "Convert PDF To CBZ" -> "Convert PDF to CBZ": a mid-name preposition stays lower case.
            for (var i = 1; i < words.Count; i++)
            {
                if (words[i] == "To")
                {
                    words[i] = "to";
                }
            }

            return string.Join(" ", words);
        }

        // UI translations v1 (2026-09-25): the display name in the UI language is en.json's
        // TaskName<Name>, whose English is exactly what For(name) computes (a test pins every command).
        // GetLocalizedString hands back the key when it has none -- an UnknownCommand's stored contract
        // name -- and that keeps the English computation.
        public static string For(string commandName, ILocalizationService localizationService)
        {
            var key = "TaskName" + StripSuffix(commandName);
            var localized = localizationService.GetLocalizedString(key);

            return localized == key ? For(commandName) : localized;
        }

        private static string StripSuffix(string commandName)
        {
            return commandName.EndsWith("Command") ? commandName.Substring(0, commandName.Length - "Command".Length) : commandName;
        }
    }
}
