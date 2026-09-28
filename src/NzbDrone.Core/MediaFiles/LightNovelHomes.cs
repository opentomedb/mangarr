namespace NzbDrone.Core.MediaFiles
{
    // One copy each (2026-09-20): the dot-folder under a light-novel root folder that holds the
    // copy-in era's leftover copies (never walked by the scan). Every install-specific path and the
    // calibre library moved to Settings on 2026-09-22 (ILightNovelStorage); this name is not one.
    public static class LightNovelHomes
    {
        public const string HoldingFolder = ".adopted-holding";
    }
}
