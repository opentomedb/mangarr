using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Organizer
{
    public class NamingConfig : ModelBase
    {
        // Beta polish (2026-09-28): a fresh install renames into one flat folder per series,
        // "<Series>/<Series> - Vol. 01.cbz". Readarr's default kept the release's file name (rename off)
        // and, once Rename Volumes was turned on, put each volume in its own "{Book Title}" subfolder,
        // which Komga and Kavita read as a series per volume. Rename on also gives light-novel audio the
        // Audiobookshelf layout it needs. Light novels keep their own "<Series> - Vol. N" folder
        // (FileNameBuilder). Only read when the NamingConfig table has no row: an install with a
        // library always has one (migration 060 writes the old values if it doesn't).
        public static NamingConfig Default => new NamingConfig
        {
            RenameBooks = true,
            ReplaceIllegalCharacters = true,
            ColonReplacementFormat = ColonReplacementFormat.Smart,
            StandardBookFormat = "{Author Name} - Vol. {Volume:00}",
            AuthorFolderFormat = "{Author Name}",
        };

        public bool RenameBooks { get; set; }
        public bool ReplaceIllegalCharacters { get; set; }
        public ColonReplacementFormat ColonReplacementFormat { get; set; }
        public string StandardBookFormat { get; set; }
        public string AuthorFolderFormat { get; set; }
    }
}
