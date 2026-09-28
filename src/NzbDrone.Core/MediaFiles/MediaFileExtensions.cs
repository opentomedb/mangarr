using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles
{
    public static class MediaFileExtensions
    {
        private static readonly Dictionary<string, Quality> _textExtensions;
        private static readonly Dictionary<string, Quality> _audioExtensions;
        private static readonly HashSet<string> _ebookExtensions;
        private static readonly HashSet<string> _audiobookExtensions;

        static MediaFileExtensions()
        {
            // Manga archive formats + the light-novel ebook formats. Ebook formats that are NOT
            // here (mobi/azw/kepub) stay non-importable; EPUB and AZW3 (a fallback when no EPUB
            // release exists) are.
            _textExtensions = new Dictionary<string, Quality>(StringComparer.OrdinalIgnoreCase)
            {
                { ".cbz", Quality.CBZ },
                { ".cbr", Quality.CBR },
                { ".zip", Quality.ZIP },
                { ".rar", Quality.RAR },
                { ".pdf", Quality.PDF },
                { ".epub", Quality.EPUB },
                { ".azw3", Quality.AZW3 },
            };

            // Audiobook formats (light-novel audio editions). Every entry here is in AllExtensions,
            // so it passes the disk-scan gate and is tag-read by AudioTagService.
            _audioExtensions = new Dictionary<string, Quality>(StringComparer.OrdinalIgnoreCase)
            {
                { ".m4b", Quality.M4B },
                // SAB sniffs some M4B posts as .mp4 (same container); accepted on the audio side and written .m4b (FileNameBuilder)
                { ".mp4", Quality.M4B },
                { ".m4a", Quality.M4B },
                { ".mp3", Quality.MP3 },
                { ".flac", Quality.FLAC },
                { ".aac", Quality.UnknownAudio },
                { ".ogg", Quality.UnknownAudio },
                { ".opus", Quality.UnknownAudio },
                { ".wma", Quality.UnknownAudio },
            };

            // Ebook formats. Classifies a file / payload by media type (Ebook): every ebook format,
            // importable (.epub) or not (mobi/azw/kepub).
            _ebookExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".epub",
                ".mobi",
                ".azw",
                ".azw3",
                ".kepub",
            };

            // Audiobook formats. Classifies a file / payload by media type (Audio); mirrors
            // _audioExtensions, which carries the per-extension quality.
            _audiobookExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".m4b",
                ".mp4",
                ".mp3",
                ".m4a",
                ".flac",
                ".aac",
                ".ogg",
                ".opus",
                ".wma",
            };
        }

        public static HashSet<string> TextExtensions => new HashSet<string>(_textExtensions.Keys, StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> AudioExtensions => new HashSet<string>(_audioExtensions.Keys, StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> AllExtensions => new HashSet<string>(_textExtensions.Keys.Concat(_audioExtensions.Keys), StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> EbookExtensions => new HashSet<string>(_ebookExtensions, StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> AudiobookExtensions => new HashSet<string>(_audiobookExtensions, StringComparer.OrdinalIgnoreCase);

        public static Quality GetQualityForExtension(string extension)
        {
            if (_textExtensions.ContainsKey(extension))
            {
                return _textExtensions[extension];
            }

            if (_audioExtensions.ContainsKey(extension))
            {
                return _audioExtensions[extension];
            }

            return Quality.Unknown;
        }
    }
}
