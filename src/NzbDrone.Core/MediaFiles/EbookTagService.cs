using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Azw;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using PdfSharpCore.Pdf.IO;
using VersOne.Epub;
using VersOne.Epub.Schema;

namespace NzbDrone.Core.MediaFiles
{
    public interface IEBookTagService
    {
        ParsedTrackInfo ReadTags(IFileInfo file);
        void WriteTags(BookFile trackfile, bool newDownload, bool force = false);
        void SyncTags(List<Edition> books);
        List<RetagBookFilePreview> GetRetagPreviewsByAuthor(int authorId);
        List<RetagBookFilePreview> GetRetagPreviewsByBook(int bookId);
        void RetagFiles(RetagFilesCommand message);
        void RetagAuthor(RetagAuthorCommand message);
    }

    public class EBookTagService : IEBookTagService
    {
        private readonly IAuthorService _authorService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IConfigService _configService;
        private readonly ICalibreProxy _calibre;
        private readonly ILightNovelCalibreSettings _lightNovelCalibre;
        private readonly Logger _logger;

        public EBookTagService(IAuthorService authorService,
            IMediaFileService mediaFileService,
            IRootFolderService rootFolderService,
            IConfigService configService,
            ICalibreProxy calibre,
            ILightNovelCalibreSettings lightNovelCalibre,
            Logger logger)
        {
            _authorService = authorService;
            _mediaFileService = mediaFileService;
            _rootFolderService = rootFolderService;
            _configService = configService;
            _calibre = calibre;
            _lightNovelCalibre = lightNovelCalibre;

            _logger = logger;
        }

        public ParsedTrackInfo ReadTags(IFileInfo file)
        {
            var extension = file.Extension.ToLower();
            _logger.Trace($"Got extension '{extension}'");

            switch (extension)
            {
                case ".pdf":
                    return ReadPdf(file.FullName);
                case ".epub":
                case ".kepub":
                    return ReadEpub(file.FullName);
                case ".azw3":
                case ".mobi":
                    return ReadAzw3(file.FullName);
                default:
                {
                    var parsed = Parser.Parser.ParseTitle(file.FullName) ?? new ParsedTrackInfo();

                    // Manga archives (CBZ/CBR/ZIP/RAR) carry no embedded tags, so identity
                    // has to come from the file name. When a volume parses, overlay the series
                    // as the author and "{Series} Vol. {N}" as the book title so the import /
                    // identification pipeline can match it to the series' volumes (this unlocks
                    // both library adoption and pack imports). Guard: if no volume parses, leave
                    // ParseTitle's result untouched so non-manga files behave exactly as before.
                    if (Parser.MangaVolumeParser.TryParseSeriesVolume(Path.GetFileNameWithoutExtension(file.Name), out var series, out var volume))
                    {
                        parsed.Authors = new List<string> { series };
                        parsed.BookTitle = $"{series} Vol. {Parser.MangaVolumeParser.Format(volume)}";
                        _logger.Debug("Manga: parsed '{0}' as series '{1}' volume {2}", file.Name, series, volume);
                    }

                    return parsed;
                }
            }
        }

        public void WriteTags(BookFile bookFile, bool newDownload, bool force = false)
        {
            if (!force)
            {
                if (_configService.WriteBookTags == WriteBookTagsType.NewFiles && !newDownload)
                {
                    return;
                }
            }

            _logger.Debug($"Writing tags for {bookFile}");

            WriteTagsInternal(bookFile, _configService.UpdateCovers, _configService.EmbedMetadata);
        }

        public void SyncTags(List<Edition> editions)
        {
            if (_configService.WriteBookTags != WriteBookTagsType.Sync)
            {
                return;
            }

            // get the tracks to update
            foreach (var edition in editions)
            {
                var bookFiles = edition.BookFiles.Value;

                _logger.Debug($"Syncing ebook tags for {edition}");

                foreach (var file in bookFiles.Where(x => x.CalibreId != 0))
                {
                    // populate tracks (which should also have release/book/author set) because
                    // not all of the updates will have been committed to the database yet
                    file.Edition = edition;

                    WriteTagsInternal(file, _configService.UpdateCovers, _configService.EmbedMetadata);
                }
            }
        }

        public List<RetagBookFilePreview> GetRetagPreviewsByAuthor(int authorId)
        {
            var files = _mediaFileService.GetFilesByAuthor(authorId);

            return GetPreviews(files).ToList();
        }

        public List<RetagBookFilePreview> GetRetagPreviewsByBook(int bookId)
        {
            var files = _mediaFileService.GetFilesByBook(bookId);

            return GetPreviews(files).ToList();
        }

        public void RetagFiles(RetagFilesCommand message)
        {
            var author = _authorService.GetAuthor(message.AuthorId);
            var files = _mediaFileService.Get(message.Files);

            _logger.ProgressInfo("Re-tagging {0} ebook files for {1}", files.Count, author.Name);

            foreach (var file in files.Where(x => x.CalibreId != 0))
            {
                WriteTagsInternal(file, message.UpdateCovers, message.EmbedMetadata);
            }

            _logger.ProgressInfo("Selected ebook files re-tagged for {0}", author.Name);
        }

        public void RetagAuthor(RetagAuthorCommand message)
        {
            _logger.Debug("Re-tagging all ebook files for selected authors");
            var authorsToRename = _authorService.GetAuthors(message.AuthorIds);

            foreach (var author in authorsToRename)
            {
                var files = _mediaFileService.GetFilesByAuthor(author.Id);

                _logger.ProgressInfo("Re-tagging all ebook files for series: {0}", author.Name);

                foreach (var file in files.Where(x => x.CalibreId != 0))
                {
                    WriteTagsInternal(file, message.UpdateCovers, message.EmbedMetadata);
                }

                _logger.ProgressInfo("All ebook files re-tagged for {0}", author.Name);
            }
        }

        private void WriteTagsInternal(BookFile file, bool updateCover, bool embedMetadata)
        {
            if (file.CalibreId == 0)
            {
                // Light novels (2026-09): an EPUB in a plain root folder (the light-novel root) has
                // no Calibre record to write to. Unreachable before .epub became importable.
                _logger.Trace($"No calibre id for {file.Path}, skipping writing tags");
                return;
            }

            // One copy each (2026-09-20): an adopted original is never written into.
            if (file.Adopted)
            {
                _logger.Debug("{0}: adopted original, calibre metadata left alone", file);
                return;
            }

            // A Calibre-homed row (a light-novel EPUB in calibre's own library) is reached through
            // the content server named in config -- no root folder resolves from /books -- and its
            // author is never re-sent: it is calibre's (the Writer at add time, or whatever the
            // user corrected it to). A stock calibre root's file keeps its root folder's settings
            // and upstream's author write.
            var settings = _lightNovelCalibre.For(file);

            if (settings == null)
            {
                throw new Exception($"File '{file.Path}' is not in a root folder.");
            }

            _calibre.SetFields(file, settings, updateCover, embedMetadata, setAuthors: file.Home != FileHome.Calibre);
        }

        private IEnumerable<RetagBookFilePreview> GetPreviews(List<BookFile> files)
        {
            // One copy each (2026-09-20): an adopted original is never written, so it is not
            // previewed either; Calibre-homed rows batch through the config settings (group
            // "config"); the rest by their root folder, as before.
            var calibreFiles = files.Where(x => x.CalibreId > 0 && !x.Adopted).OrderBy(x => x.Edition.Value.Title).ToList();

            var rootFolderPairs = calibreFiles.Select(x =>
            {
                if (x.Home == FileHome.Calibre)
                {
                    return (File: x, Key: "config", Settings: _lightNovelCalibre.ForConfig());
                }

                var rootFolder = _rootFolderService.GetBestRootFolder(x.Path);
                return (File: x, Key: rootFolder.Path, Settings: rootFolder.CalibreSettings);
            });

            var rootFolderGroups = rootFolderPairs.GroupBy(x => x.Key);

            var calibreBooks = new List<CalibreBook>();
            foreach (var group in rootFolderGroups)
            {
                var settings = group.First().Settings;
                var books = _calibre.GetBooks(group.Select(x => x.File.CalibreId).ToList(), settings);
                calibreBooks.AddRange(books);
            }

            var dict = calibreBooks.ToDictionary(x => x.Id);

            foreach (var file in calibreFiles)
            {
                var edition = file.Edition.Value;
                var book = edition.Book.Value;
                var serieslink = book.SeriesLinks.Value.OrderBy(x => x.SeriesPosition).FirstOrDefault(x => x.Series.Value.Title.IsNotNullOrWhiteSpace());

                var seriesTitle = serieslink?.Series.Value?.Title;
                double? seriesIndex = null;
                if (double.TryParse(serieslink?.Position, out var index))
                {
                    _logger.Trace($"Parsed {serieslink?.Position} as {index}");
                    seriesIndex = index;
                }

                // The preview mirrors what the write sends (CalibreProxy.SetFields): a light-novel
                // entry is the series and its volume number the index; a Calibre-homed row's
                // author is never re-sent, so it is not a difference either.
                if (file.Author.Value.Library == LibraryType.LightNovel)
                {
                    seriesTitle = file.Author.Value.Name;
                    seriesIndex = book.VolumeNumber > 0 ? book.VolumeNumber : (double?)null;
                }

                var oldTags = dict[file.CalibreId];

                var textInfo = CultureInfo.InvariantCulture.TextInfo;
                var genres = book.Genres.Select(x => textInfo.ToTitleCase(x.Replace('-', ' '))).ToList();

                var newTags = new CalibreBook
                {
                    Title = edition.Title,
                    Authors = file.Home == FileHome.Calibre ? oldTags.Authors : new List<string> { file.Author.Value.Name },
                    PubDate = book.ReleaseDate,
                    Publisher = edition.Publisher,
                    Languages = new List<string> { edition.Language.CanonicalizeLanguage() },
                    Tags = genres,
                    Comments = edition.Overview,
                    Rating = (int)(edition.Ratings.Value * 2) / 2.0,
                    Identifiers = new Dictionary<string, string>
                    {
                        { "isbn", edition.Isbn13 },
                        { "asin", edition.Asin },
                        { "goodreads", edition.ForeignEditionId }
                    },
                    Series = seriesTitle,
                    Position = seriesIndex
                };

                var diff = oldTags.Diff(newTags);

                if (diff.Any())
                {
                    yield return new RetagBookFilePreview
                    {
                        AuthorId = file.Author.Value.Id,
                        BookId = file.Edition.Value.Id,
                        BookFileId = file.Id,
                        Path = file.Path,
                        Changes = diff
                    };
                }
            }
        }

        private ParsedTrackInfo ReadEpub(string file)
        {
            _logger.Trace($"Reading {file}");
            var result = new ParsedTrackInfo
            {
                Quality = new QualityModel
                {
                    Quality = Quality.EPUB,
                    QualityDetectionSource = QualityDetectionSource.TagLib
                }
            };

            try
            {
                using (var bookRef = EpubReader.OpenBook(file))
                {
                    result.Authors = bookRef.AuthorList;
                    result.BookTitle = bookRef.Title;

                    var meta = bookRef.Schema.Package.Metadata;

                    _logger.Trace(meta.ToJson());

                    result.Isbn = GetIsbn(meta?.Identifiers);
                    result.Asin = meta?.Identifiers?.FirstOrDefault(x => x.Scheme?.ToLower().Contains("asin") ?? false)?.Identifier;
                    result.Language = meta?.Languages?.FirstOrDefault();
                    result.Publisher = meta?.Publishers?.FirstOrDefault();
                    result.Disambiguation = meta?.Description;

                    result.SeriesTitle = meta?.MetaItems?.FirstOrDefault(x => x.Name == "calibre:series")?.Content;
                    result.SeriesIndex = meta?.MetaItems?.FirstOrDefault(x => x.Name == "calibre:series_index")?.Content;
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error reading epub");
                result.Quality.QualityDetectionSource = QualityDetectionSource.Extension;
            }

            _logger.Trace($"Got:\n{result.ToJson()}");

            return result;
        }

        private ParsedTrackInfo ReadAzw3(string file)
        {
            _logger.Trace($"Reading {file}");
            var result = new ParsedTrackInfo();

            try
            {
                var book = new Azw3File(file);
                result.Authors = book.Authors;
                result.BookTitle = book.Title;
                result.Isbn = StripIsbn(book.Isbn);
                result.Asin = book.Asin;
                result.Language = book.Language;
                result.Disambiguation = book.Description;
                result.Publisher = book.Publisher;
                result.Label = book.Imprint;
                result.Source = book.Source;

                result.Quality = new QualityModel
                {
                    Quality = QualityOfKindleFile(Path.GetExtension(file), book.Version),
                    QualityDetectionSource = QualityDetectionSource.TagLib
                };
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error reading file");

                result.Quality = new QualityModel
                {
                    Quality = Path.GetExtension(file).Equals(".azw3", StringComparison.OrdinalIgnoreCase) ? Quality.AZW3
                        : Path.GetExtension(file) == ".cbz" ? Quality.CBZ : Quality.ZIP,
                    QualityDetectionSource = QualityDetectionSource.Extension
                };
            }

            _logger.Trace($"Got {result.ToJson()}");

            return result;
        }

        // AZW3 (2026-09, id 7): a light-novel .azw3 file imports as its own quality whatever the
        // MOBI container version inside it says - the old book.Version <= 6 -> CBZ, else ZIP split
        // was a manga-container heuristic that never applied to a real light-novel AZW3. .mobi is
        // not imported (ReadTags routes it here for metadata scraping only) and keeps that legacy
        // heuristic exactly.
        internal static Quality QualityOfKindleFile(string extension, uint version)
        {
            return extension.Equals(".azw3", StringComparison.OrdinalIgnoreCase)
                ? Quality.AZW3
                : version <= 6 ? Quality.CBZ : Quality.ZIP;
        }

        private ParsedTrackInfo ReadPdf(string file)
        {
            _logger.Trace($"Reading {file}");
            var result = new ParsedTrackInfo
            {
                Quality = new QualityModel
                {
                    Quality = Quality.PDF,
                    QualityDetectionSource = QualityDetectionSource.TagLib
                }
            };

            try
            {
                var book = PdfReader.Open(file, PdfDocumentOpenMode.InformationOnly);
                if (book.Info != null)
                {
                    result.Authors = new List<string> { book.Info.Author };
                    result.BookTitle = book.Info.Title;

                    _logger.Trace(book.Info.ToJson());
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error reading pdf");
                result.Quality.QualityDetectionSource = QualityDetectionSource.Extension;
            }

            _logger.Trace($"Got:\n{result.ToJson()}");

            return result;
        }

        public string GetIsbn(IEnumerable<EpubMetadataIdentifier> ids)
        {
            var candidates = ids.Select(x => StripIsbn(x?.Identifier))
                .Where(x => x != null)
                .OrderByDescending(x => x.Length);

            return candidates.FirstOrDefault(x => x.StartsWith("978"))
                ?? candidates.FirstOrDefault(x => x.StartsWith("979"))
                ?? candidates.FirstOrDefault();
        }

        private string GetIsbnChars(string input)
        {
            if (input == null)
            {
                return null;
            }

            return new string(input.Where(c => char.IsDigit(c) || c == 'X' || c == 'x').ToArray());
        }

        private string StripIsbn(string input)
        {
            var isbn = GetIsbnChars(input);

            if (isbn == null)
            {
                return null;
            }
            else if ((isbn.Length == 10 && ValidateIsbn10(isbn)) ||
                (isbn.Length == 13 && ValidateIsbn13(isbn)))
            {
                return isbn;
            }

            return null;
        }

        private static char Isbn10Checksum(string isbn)
        {
            var sum = 0;
            for (var i = 0; i < 9; i++)
            {
                sum += int.Parse(isbn[i].ToString()) * (10 - i);
            }

            var result = sum % 11;

            if (result == 0)
            {
                return '0';
            }
            else if (result == 1)
            {
                return 'X';
            }

            return (11 - result).ToString()[0];
        }

        private static char Isbn13Checksum(string isbn)
        {
            var result = 0;
            for (var i = 0; i < 12; i++)
            {
                result += int.Parse(isbn[i].ToString()) * ((i % 2 == 0) ? 1 : 3);
            }

            result %= 10;

            return result == 0 ? '0' : (10 - result).ToString()[0];
        }

        private static bool ValidateIsbn10(string isbn)
        {
            return ulong.TryParse(isbn.Substring(0, 9), out _) && isbn[9] == Isbn10Checksum(isbn);
        }

        private static bool ValidateIsbn13(string isbn)
        {
            return ulong.TryParse(isbn, out _) && isbn[12] == Isbn13Checksum(isbn);
        }
    }
}
