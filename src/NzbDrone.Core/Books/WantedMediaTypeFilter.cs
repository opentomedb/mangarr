namespace NzbDrone.Core.Books
{
    // Which editions a Wanted page is asking about (the "mediaType" query parameter of
    // wanted/missing and wanted/cutoff). Ebook and Audio narrow to one class; Both keeps only the
    // volumes wanting BOTH editions, which is a light-novel shape -- a manga volume has a single
    // Archive edition, so no manga row can ever satisfy it.
    public enum WantedMediaTypeFilter
    {
        Ebook = 0,
        Audio = 1,
        Both = 2
    }
}
