using System;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Books
{
    // Which library an entry belongs to. Encoded in the foreign id, never stored: a manga series
    // keeps its historical id ("local-<slug>"); a light novel adds "~ln" ("local-<slug>~ln").
    // The manga and the light novel of one name are different catalogue lines, so they need
    // different ids, and every unique index that hangs off the id (AuthorMetadata.ForeignAuthorId/
    // TitleSlug, Books.TitleSlug, Editions.ForeignEditionId, Authors.CleanName (046)) keeps working.
    // '~' never appears in a slug (BookInfoProxy.Slug maps every non-alphanumeric to '-').
    public enum LibraryType
    {
        Manga = 0,
        LightNovel = 1
    }

    public static class LibraryTypes
    {
        public const string LightNovelSuffix = "~ln";

        public static LibraryType Parse(string foreignId)
        {
            return foreignId.IsNotNullOrWhiteSpace() && foreignId.Contains(LightNovelSuffix, StringComparison.Ordinal)
                ? LibraryType.LightNovel
                : LibraryType.Manga;
        }

        public static string Suffix(LibraryType type)
        {
            return type == LibraryType.LightNovel ? LightNovelSuffix : string.Empty;
        }

        // The id without the suffix, wherever it sits ("local-x~ln-v5" -> "local-x-v5").
        public static string BaseId(string foreignId)
        {
            return foreignId?.Replace(LightNovelSuffix, string.Empty);
        }

        // A SERIES id in the given library (the suffix goes at the end of the series id).
        public static string WithType(string foreignAuthorId, LibraryType type)
        {
            return BaseId(foreignAuthorId) + Suffix(type);
        }

        // Authors.CleanName is UNIQUE (046) and is what release titles resolve against, so the
        // manga entry keeps the plain clean name and the light-novel entry carries the suffix.
        public static string CleanNameFor(string cleanName, LibraryType type)
        {
            return cleanName + Suffix(type);
        }

        // Metadata pins (overrides.json) are keyed by display name; the light-novel line of a
        // name is a different bucket from the manga line.
        public static string PinKey(string displayName, LibraryType type)
        {
            return type == LibraryType.LightNovel ? displayName + " (light novel)" : displayName;
        }

        // LN PDF (2026-09-22): the book's library off its AuthorMetadata's foreign id; null when not
        // loaded/known. Shared by every author-aware MediaTypes.OfFile call site that only has a
        // Book (or, via its Book, an Edition) in hand -- IdentificationService.GetBestRelease,
        // RefreshBookService.MergeTargetEdition -- so neither duplicates its own private copy.
        public static LibraryType? Of(Book book)
        {
            var foreignAuthorId = book?.AuthorMetadata?.Value?.ForeignAuthorId;

            return foreignAuthorId == null ? (LibraryType?)null : Parse(foreignAuthorId);
        }
    }
}
