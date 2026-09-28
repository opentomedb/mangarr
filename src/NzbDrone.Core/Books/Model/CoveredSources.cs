namespace NzbDrone.Core.Books
{
    public static class CoveredSources
    {
        public const string Audible = "audible";
        public const string Import = "import";
        public const string Manual = "manual";

        // App-owned marks (an import, a user's choice) are never replaced or relabelled by a refresh;
        // only the D4a reversals and the user clear them.
        public static bool IsAppOwned(string source) => source == Import || source == Manual;
    }
}
