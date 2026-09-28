using System;
using System.IO;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Books
{
    // Which format an edition / file / release is. A light-novel volume has an Ebook edition and
    // an Audio edition; a manga volume has one Archive edition. Stored on Editions.MediaType (048);
    // a file inherits it through its edition, or from its extension.
    public enum MediaType
    {
        Archive = 0,
        Ebook = 1,
        Audio = 2
    }

    public static class MediaTypes
    {
        public static MediaType OfQuality(Quality quality)
        {
            if (quality == null)
            {
                return MediaType.Archive;
            }

            if (quality == Quality.EPUB || quality == Quality.AZW3 || quality == Quality.EbookPdf)
            {
                return MediaType.Ebook;
            }

            if (quality == Quality.MP3 || quality == Quality.FLAC || quality == Quality.M4B || quality == Quality.UnknownAudio)
            {
                return MediaType.Audio;
            }

            return MediaType.Archive;
        }

        // .epub/.mobi/.azw/.azw3/.kepub -> Ebook; every audio extension -> Audio; else Archive.
        public static MediaType OfExtension(string extension)
        {
            if (extension.IsNullOrWhiteSpace())
            {
                return MediaType.Archive;
            }

            if (MediaFileExtensions.EbookExtensions.Contains(extension))
            {
                return MediaType.Ebook;
            }

            if (MediaFileExtensions.AudioExtensions.Contains(extension) || MediaFileExtensions.AudiobookExtensions.Contains(extension))
            {
                return MediaType.Audio;
            }

            return MediaType.Archive;
        }

        // LN PDF (2026-09-22): ".pdf" is the manga archive wherever the library is unknown (the
        // extension map, ReadPdf, the release parser). A caller that knows the entry's library
        // types a light novel's .pdf as its ebook; every other extension, and every manga or
        // unknown-library .pdf, is OfExtension exactly.
        public static MediaType OfFile(string path, LibraryType? library)
        {
            var extension = Path.GetExtension(path ?? string.Empty);

            if (library == LibraryType.LightNovel && string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return MediaType.Ebook;
            }

            return OfExtension(extension);
        }

        // LN PDF (2026-09-22): every .pdf is read as the manga PDF where the author is unknown (the
        // extension map, EbookTagService.ReadPdf). Once the file's entry is known, a light novel's
        // PDF becomes Ebook PDF -- the revision and the rest of the model are kept. Anything else (a
        // manga PDF, an unknown library, another quality) is left exactly as it was.
        public static void RegradePdfForLibrary(QualityModel quality, string path, LibraryType? library)
        {
            if (quality?.Quality == Quality.PDF && OfFile(path, library) == MediaType.Ebook)
            {
                quality.Quality = Quality.EbookPdf;
            }
        }

        // Edition.Format strings DistanceCalculator already scores (its EbookFormats /
        // AudiobookFormats lists), so identification routes an .epub to the Ebook edition and an
        // .m4b to the Audio one with no new code.
        public static string EditionFormat(MediaType type)
        {
            switch (type)
            {
                case MediaType.Ebook: return "ebook";
                case MediaType.Audio: return "Audiobook";
                default: return "Paperback";
            }
        }

        public static string EditionIdSuffix(MediaType type)
        {
            return type == MediaType.Audio ? "-audio-ed" : "-ed";
        }

        // Newznab: the 3000 family is audio.
        public static bool IsAudioCategory(int category)
        {
            return category >= 3000 && category < 4000;
        }
    }
}
