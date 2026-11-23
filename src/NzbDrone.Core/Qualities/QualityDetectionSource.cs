namespace NzbDrone.Core.Qualities
{
    public enum QualityDetectionSource
    {
        Name,
        Extension,
        TagLib,
        Category,

        // The manga default (2026-11-23): a volume-tokened release with no format token is graded by its
        // wording, CBZ for manga; the grade is a guess, so it never counts as an upgrade over a file on disk.
        Guess
    }
}
