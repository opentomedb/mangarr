using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Books.Calibre
{
    public class CalibreFormatsResult
    {
        public List<string> Formats { get; set; }
        public string Error { get; set; }
    }

    // Task 5 (D4): the volume page shows every format calibre actually holds for a book, read from
    // calibre on demand. A file that isn't calibre-homed (LightNovelCalibreSettings.For returns
    // null, or the file was never actually added -- CalibreId <= 0) just shows its own extension,
    // and so does anything calibre fails to answer -- the row is never blank and this never 500s.
    // settingsFor/getBook are passed in so the decision is testable without a calibre server; the
    // controller wires them to ILightNovelCalibreSettings.For and ICalibreProxy.GetBook.
    public static class CalibreFormats
    {
        public static CalibreFormatsResult Of(BookFile file, Func<BookFile, CalibreSettings> settingsFor, Func<int, CalibreSettings, CalibreBook> getBook)
        {
            var ownExtension = OwnExtension(file.Path);

            try
            {
                var settings = settingsFor(file);

                if (settings == null || file.CalibreId <= 0)
                {
                    return new CalibreFormatsResult { Formats = ownExtension };
                }

                var book = getBook(file.CalibreId, settings);
                var formats = book?.Formats?.Keys.Select(k => k.ToUpperInvariant()).ToList() ?? new List<string>();

                if (!formats.Any())
                {
                    return new CalibreFormatsResult { Formats = ownExtension };
                }

                // The file's own (imported) format leads, everything else follows alphabetically --
                // "EPUB, AZW3", not "AZW3, EPUB".
                var own = ownExtension.FirstOrDefault();
                var ordered = formats
                    .OrderBy(f => own != null && string.Equals(f, own, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new CalibreFormatsResult { Formats = ordered };
            }
            catch (Exception)
            {
                return new CalibreFormatsResult { Formats = new List<string>(), Error = "Calibre did not answer" };
            }
        }

        // Final review C1 (2026-09-22): the calibre format a light-novel ebook row is tracked by --
        // EPUB when the book holds one, else the AZW3 fallback, else (LN PDF, 2026-09-22) a PDF
        // imported as Ebook PDF; null when it holds none of them. Keys match without case (the live
        // server keys format_metadata "epub"). Never the row's own extension: a book holding EPUB
        // plus a converted AZW3 is tracked by its EPUB.
        public static string TrackedLightNovelFormat(IEnumerable<string> formats)
        {
            var list = formats?.ToList() ?? new List<string>();

            return list.FirstOrDefault(f => string.Equals(f, "EPUB", StringComparison.OrdinalIgnoreCase)) ??
                   list.FirstOrDefault(f => string.Equals(f, "AZW3", StringComparison.OrdinalIgnoreCase)) ??
                   list.FirstOrDefault(f => string.Equals(f, "PDF", StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> OwnExtension(string path)
        {
            var extension = Path.GetExtension(path ?? string.Empty).TrimStart('.');

            return string.IsNullOrWhiteSpace(extension) ? new List<string>() : new List<string> { extension.ToUpperInvariant() };
        }
    }
}
