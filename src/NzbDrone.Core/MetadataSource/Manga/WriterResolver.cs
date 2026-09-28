using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Existing;

namespace NzbDrone.Core.MetadataSource.Manga
{
    // One copy each (2026-09-20): the real author of a light novel (AuthorMetadata.Writer), resolved
    // on every refresh. An "Author" entry is the series; the person who wrote it is what calibre
    // files a book under and what the audio tags carry, so it comes from, in order:
    //
    //   1. a pin -- overrides.json "<pinKey>": { "*": { "author": ... } } (the operator's word);
    //   2. calibre -- the author of the entry's first calibre-homed file (adopted or grabbed): the
    //      spelling the library already files that series under. The entry's own name read back
    //      is rung 4's fallback, not an answer: it falls through, or the fallback would seal
    //      itself in on the next refresh;
    //   3. the catalogue -- OpenTome's author column, in calibre's own spelling when calibre
    //      already has that person (so "rifujin na magonote" joins "Rifujin na Magonote" rather
    //      than minting a second author);
    //   4. nothing -- null: the caller files under the series name, said once per refresh.
    //
    // Every calibre call is best-effort: a failure is a Warn and "no answer" for that rung, never
    // a failed refresh -- and a stored Writer outlives a calibre outage on the file rung.
    public interface IWriterResolver
    {
        string Resolve(Author existing, string pinKey, string catalogueWriter);
    }

    public class WriterResolver : IWriterResolver
    {
        private readonly IMetadataOverridesService _overrides;
        private readonly IMediaFileService _mediaFileService;
        private readonly ICalibreProxy _calibre;
        private readonly ILightNovelCalibreSettings _settings;
        private readonly ICalibreContentServerClient _contentServer;
        private readonly ILightNovelStorage _storage;
        private readonly Logger _logger;

        public WriterResolver(IMetadataOverridesService overrides,
                              IMediaFileService mediaFileService,
                              ICalibreProxy calibre,
                              ILightNovelCalibreSettings settings,
                              ICalibreContentServerClient contentServer,
                              ILightNovelStorage storage,
                              Logger logger)
        {
            _overrides = overrides;
            _mediaFileService = mediaFileService;
            _calibre = calibre;
            _settings = settings;
            _contentServer = contentServer;
            _storage = storage;
            _logger = logger;
        }

        public string Resolve(Author existing, string pinKey, string catalogueWriter)
        {
            var writer = FromPin(pinKey) ?? FromCalibreFile(existing) ?? FromCatalogue(catalogueWriter);

            if (writer == null)
            {
                _logger.Info("no writer known for {0}; calibre files it under the series name until pinned", existing?.Name ?? pinKey);
            }

            return writer;
        }

        // 1. pin: overrides.json "<pinKey>": { "*": { "author": ... } }
        private string FromPin(string pinKey)
        {
            var pinned = _overrides.GetSeriesAuthor(pinKey);

            return pinned.IsNotNullOrWhiteSpace() ? pinned : null;
        }

        // 2. calibre: the author of the entry's first calibre-homed file (adopted or grabbed)
        private string FromCalibreFile(Author existing)
        {
            if (!(existing?.Id > 0))
            {
                return null;
            }

            var calibreFile = _mediaFileService.GetFilesByAuthor(existing.Id).FirstOrDefault(f => f.Home == FileHome.Calibre && f.CalibreId > 0);

            if (calibreFile == null)
            {
                return null;
            }

            try
            {
                var book = _calibre.GetBook(calibreFile.CalibreId, _settings.ForConfig());
                var author = book?.Authors?.FirstOrDefault(a => a.IsNotNullOrWhiteSpace())?.Trim();

                if (author != null && IsSeriesName(author, existing))
                {
                    // An EPUB filed under the series name (no writer was known when it was added)
                    // read back would return that fallback as the writer and shut the catalogue out
                    // for good (2026-09-20 final review I2): not an answer.
                    _logger.Debug("calibre files {0} under its own series name; not a writer", existing.Name);
                    return null;
                }

                return author;
            }
            catch (Exception ex)
            {
                // No answer (an outage, not "a book with no author"): the Writer an earlier pass stored
                // survives it -- the refresh upserts the whole row, so null here would file the next
                // grab under the series name until calibre is back (2026-09-20 review). With nothing
                // stored, the catalogue is next.
                _logger.Warn(ex, "Could not read calibre book {0} for the writer of {1}; keeping the stored writer", calibreFile.CalibreId, existing.Name);
                return existing.Metadata?.Value?.Writer;
            }
        }

        // The entry name as calibre would file it, with or without the "(light novel)" qualifier
        // OpenTome puts on a novel line (the same strip the entry name went through), without case.
        private static bool IsSeriesName(string author, Author existing)
        {
            var name = existing.Name;

            if (name.IsNullOrWhiteSpace())
            {
                return false;
            }

            return string.Equals(author, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(MangaSeriesMetadataProvider.StripLightNovelQualifier(author), MangaSeriesMetadataProvider.StripLightNovelQualifier(name), StringComparison.OrdinalIgnoreCase);
        }

        // 3. catalogue: OpenTome's author, in calibre's own spelling when calibre already has that author.
        // The name is the answer either way; calibre only gets to correct its case.
        private string FromCatalogue(string catalogueWriter)
        {
            if (catalogueWriter.IsNullOrWhiteSpace())
            {
                return null;
            }

            var name = catalogueWriter.Trim();

            // calibre's spelling only matters while calibre is the ebook home (light-novel storage, 2026-09-22).
            if (_storage.EbookHome != LightNovelHome.Calibre)
            {
                return name;
            }

            try
            {
                var ids = _contentServer.SearchAuthor(name);            // ajax/search authors:"=name" (case-insensitive)

                if (!ids.Any())
                {
                    return name;
                }

                var spelled = _calibre.GetBook(ids.First(), _settings.ForConfig())?.Authors?
                    .FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

                return spelled ?? name;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not ask calibre how it spells '{0}'; using the catalogue's spelling", name);
                return name;
            }
        }
    }
}
