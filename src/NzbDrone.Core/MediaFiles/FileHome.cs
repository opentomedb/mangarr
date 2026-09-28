namespace NzbDrone.Core.MediaFiles
{
    // Where a light-novel file lives. Manga is always Entry.
    public enum FileHome
    {
        Entry = 0,
        Calibre = 1,
        Audiobooks = 2
    }
}
