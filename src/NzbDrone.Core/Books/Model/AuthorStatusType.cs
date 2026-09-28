namespace NzbDrone.Core.Books
{
    public enum AuthorStatusType
    {
        Continuing = 0,
        Ended = 1,

        // OpenTome (2026-09-22): the licence stopped at least two volumes behind the original and
        // nothing has shipped in 24 months. New volumes are still watched for (a revived licence
        // is not an error); it is a display state, never a reason to stop monitoring.
        Stalled = 2
    }
}
