using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Localization;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Books.Calibre
{
    public interface ICalibreProxy
    {
        BookFile AddAndConvert(BookFile file, CalibreSettings settings);
        bool ConvertToFormatAndWait(int calibreId, string inputFormat, string outputFormat, CalibreSettings settings, TimeSpan timeout);
        void DeleteBook(BookFile book, CalibreSettings settings);
        void DeleteBooks(List<BookFile> books, CalibreSettings settings);
        void RemoveFormats(int calibreId, IEnumerable<string> formats, CalibreSettings settings);
        void SetFields(BookFile file, CalibreSettings settings, bool updateCover = true, bool embed = false, bool setAuthors = true);
        void SetTitle(BookFile file, string title, string sort, CalibreSettings settings);
        List<string> GetAllBookFilePaths(CalibreSettings settings);
        CalibreBook GetBook(int calibreId, CalibreSettings settings);
        List<CalibreBook> GetBooks(List<int> calibreId, CalibreSettings settings);
        void Test(CalibreSettings settings);
        bool HasWriteAccess(CalibreSettings settings);
        CalibreLibraryInfo GetLibraryInfo(CalibreSettings settings);
    }

    public class CalibreProxy : ICalibreProxy
    {
        private const int PAGE_SIZE = 750;

        private readonly IHttpClient _httpClient;
        private readonly IMapCoversToLocal _mediaCoverService;
        private readonly IRemotePathMappingService _pathMapper;
        private readonly IRootFolderWatchingService _rootFolderWatchingService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;
        private readonly ICached<CalibreBook> _bookCache;

        public CalibreProxy(IHttpClient httpClient,
                            IMapCoversToLocal mediaCoverService,
                            IRemotePathMappingService pathMapper,
                            IRootFolderWatchingService rootFolderWatchingService,
                            IMediaFileService mediaFileService,
                            IConfigService configService,
                            ICacheManager cacheManager,
                            Logger logger)
        {
            _httpClient = httpClient;
            _mediaCoverService = mediaCoverService;
            _pathMapper = pathMapper;
            _rootFolderWatchingService = rootFolderWatchingService;
            _mediaFileService = mediaFileService;
            _configService = configService;
            _bookCache = cacheManager.GetCache<CalibreBook>(GetType());
            _logger = logger;
        }

        // Final review M1 (2026-09-22): AZW3 is a text extension now, and embedding metadata
        // rewrites the EPUB, so the converted AZW3 beside it can be the older file. AZW3 sorts last
        // so an EPUB always wins; an AZW3-only book (and every manga book) is its oldest format.
        // LN PDF (2026-09-22) P1: a light-novel PDF sorts after both EPUB and AZW3 when the caller
        // says so (lightNovel: true), so a light novel's row is never repointed at its PDF while a
        // real ebook exists. A book with neither (every manga book, a PDF-only light novel) keeps
        // oldest-first exactly as before. Manga (and GetAllBookFilePaths's stock calibre roots,
        // which carry no author) never pass lightNovel, so a PDF ranks exactly as it always has.
        public static string GetOriginalFormat(Dictionary<string, CalibreBookFormat> formats, bool lightNovel = false)
        {
            return formats
                .Where(x => MediaFileExtensions.TextExtensions.Contains("." + x.Key))
                .OrderBy(f => OriginalFormatRank(f.Key, lightNovel))
                .ThenBy(f => f.Value.LastModified)
                .FirstOrDefault().Value?.Path;
        }

        private static int OriginalFormatRank(string format, bool lightNovel)
        {
            if (format.Equals("azw3", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (lightNovel && format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return 0;
        }

        // Design D2: the calibre input format comes from the file extension, not Quality.Name --
        // Quality.Name only happens to equal the calibre format token today, and a light-novel
        // import can now be either EPUB or the AZW3 fallback, both convertible to AZW3/KEPUB/EPUB.
        // internal so it is directly testable (InternalsVisibleTo("Readarr.Core.Test")).
        internal static string InputFormatOf(string path)
        {
            return Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        }

        // LN PDF (2026-09-22): calibre's PDF conversion makes poor ebooks, so a light novel's PDF is
        // stored as it is and never used as a conversion source. A manga PDF in a stock calibre root
        // converts exactly as before.
        private static bool IsLightNovelPdf(BookFile file)
        {
            return InputFormatOf(file.Path) == "PDF" && file.Author?.Value?.Library == LibraryType.LightNovel;
        }

        public BookFile AddAndConvert(BookFile file, CalibreSettings settings)
        {
            _logger.Trace($"Importing to calibre: {file.Path} calibre id: {file.CalibreId}");

            // One copy each (2026-09-20): the authors are set on a NEW calibre book only. Adding a
            // format to a book calibre already has leaves whatever authors it carries alone.
            var isNew = file.CalibreId == 0;

            if (isNew)
            {
                var import = AddBook(file, settings);
                file.CalibreId = import.Id;
            }
            else
            {
                AddFormat(file, settings);
            }

            SetFields(file, settings, true, _configService.EmbedMetadata, setAuthors: isNew);

            if (settings.OutputFormat.IsNotNullOrWhiteSpace())
            {
                // LN PDF (2026-09-22): calibre's PDF input makes poor ebooks, so a light novel's PDF
                // is stored as it is and no delivery conversion starts from it. A manga PDF in a
                // stock calibre root converts exactly as before.
                if (IsLightNovelPdf(file))
                {
                    _logger.Info("calibre's PDF conversion makes poor ebooks; the PDF is kept as is ({0})", file.Path);
                    return file;
                }

                // Final review I1 (2026-09-22): the file is in calibre once the add and SetFields
                // succeed. A conversion that cannot start is logged, not thrown -- in the upgrade
                // path the old row is already gone, so a throw here left the volume Missing while
                // calibre held the new file.
                try
                {
                    _logger.Trace($"Getting book data for {file.CalibreId}");
                    var options = GetBookData(file.CalibreId, settings);
                    var inputFormat = InputFormatOf(file.Path);

                    options.Conversion_options.Input_fmt = inputFormat;

                    var formats = settings.OutputFormat.Split(',').Select(x => x.Trim());
                    foreach (var format in formats)
                    {
                        if (string.Equals(format, inputFormat, StringComparison.OrdinalIgnoreCase) ||
                            options.Input_formats.Contains(format, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        options.Conversion_options.Output_fmt = format;

                        if (settings.OutputProfile != (int)CalibreProfile.@default)
                        {
                            options.Conversion_options.Options.Output_profile = ((CalibreProfile)settings.OutputProfile).ToString();
                        }

                        _logger.Trace($"Starting conversion to {format}");

                        _rootFolderWatchingService.ReportFileSystemChangeBeginning(Path.ChangeExtension(file.Path, format));
                        ConvertBook(file.CalibreId, options.Conversion_options, settings);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Calibre could not start the {0} conversion of {1}; the imported file is kept", settings.OutputFormat, file.Path);
                }
            }

            return file;
        }

        // Task 4 (backfill on request): the same options handling AddAndConvert applies inline for
        // a fresh import -- Input_fmt/Output_fmt/output profile -- as a public, single-format call
        // for a book already in calibre. The caller (ConvertLightNovelFormatService) already knows
        // the book lacks outputFormat, so no skip check runs here.
        // Final review I2 (2026-09-22): unlike an import's fire-and-forget conversion, this one is
        // followed to the end, so the backfill runs calibre's jobs one at a time instead of
        // submitting them all at once with a poller each against the content server KOReader and
        // Komga read. True when calibre finished it; false (logged) when it failed, its status
        // could not be read or it was still running after timeout -- the caller moves on either
        // way. Throws CalibreException when the conversion cannot be started at all.
        public bool ConvertToFormatAndWait(int calibreId, string inputFormat, string outputFormat, CalibreSettings settings, TimeSpan timeout)
        {
            var options = GetBookData(calibreId, settings);

            options.Conversion_options.Input_fmt = inputFormat;
            options.Conversion_options.Output_fmt = outputFormat;

            if (settings.OutputProfile != (int)CalibreProfile.@default)
            {
                options.Conversion_options.Options.Output_profile = ((CalibreProfile)settings.OutputProfile).ToString();
            }

            _logger.Trace($"Starting conversion of {calibreId} to {outputFormat}");

            var jobId = StartConversion(calibreId, options.Conversion_options, settings);

            return WaitForConversion(jobId, calibreId, outputFormat, settings, timeout);
        }

        private CalibreImportJob AddBook(BookFile book, CalibreSettings settings)
        {
            var jobid = (int)(DateTime.UtcNow.Ticks % 1000000000);
            var addDuplicates = 1;
            var path = book.Path;
            var filename = $"$dummy{Path.GetExtension(path)}";
            var body = File.ReadAllBytes(path);

            _logger.Trace($"Read {body.Length} bytes from {path}");

            try
            {
                var builder = GetBuilder($"cdb/add-book/{jobid}/{addDuplicates}/{filename}/{settings.Library}", settings);

                var request = builder.Build();
                request.SetContent(body);

                var response = _httpClient.Post<CalibreImportJob>(request).Resource;

                if (response.Id == 0)
                {
                    throw new CalibreException("Calibre rejected duplicate book");
                }

                return response;
            }
            catch (HttpException ex)
            {
                throw new CalibreException("Unable to add file to Calibre library: {0}", ex, ex.Message);
            }
        }

        public void DeleteBook(BookFile book, CalibreSettings settings)
        {
            var request = GetBuilder($"cdb/delete-books/{book.CalibreId}/{settings.Library}", settings).Build();
            _httpClient.Post(request);
        }

        public void DeleteBooks(List<BookFile> books, CalibreSettings settings)
        {
            var idString = books.Where(x => x.CalibreId != 0).Select(x => x.CalibreId).ConcatToString(",");
            var request = GetBuilder($"cdb/delete-books/{idString}/{settings.Library}", settings).Build();
            _httpClient.Post(request);
        }

        private void AddFormat(BookFile file, CalibreSettings settings)
        {
            var format = Path.GetExtension(file.Path);
            var bookData = Convert.ToBase64String(File.ReadAllBytes(file.Path));

            var payload = new CalibreChangesPayload
            {
                LoadedBookIds = new List<int> { file.CalibreId },
                Changes = new CalibreChanges
                {
                    AddedFormats = new List<CalibreAddFormat>
                    {
                        new CalibreAddFormat
                        {
                            Ext = format,
                            Data = bookData
                        }
                    }
                }
            };

            ExecuteSetFields(file.CalibreId, payload, settings);
        }

        public void RemoveFormats(int calibreId, IEnumerable<string> formats, CalibreSettings settings)
        {
            var payload = new CalibreChangesPayload
            {
                LoadedBookIds = new List<int> { calibreId },
                Changes = new CalibreChanges
                {
                    RemovedFormats = formats.ToList()
                }
            };

            ExecuteSetFields(calibreId, payload, settings);
        }

        public void SetFields(BookFile file, CalibreSettings settings, bool updateCover = true, bool embed = false, bool setAuthors = true)
        {
            var edition = file.Edition.Value;
            var book = edition.Book.Value;
            var serieslink = book.SeriesLinks.Value.OrderBy(x => x.SeriesPosition).FirstOrDefault(x => x.Series.Value.Title.IsNotNullOrWhiteSpace());

            var seriesTitle = serieslink?.Series.Value?.Title;
            double? seriesIndex = null;
            if (double.TryParse(serieslink?.Position, out var index))
            {
                _logger.Trace("Parsed '{0}' as '{1}'", serieslink.Position, index);
                seriesIndex = index;
            }

            // One copy each (2026-09-20): a light-novel entry IS the series and carries no series
            // links, so calibre gets the entry name and the volume number -- never a null series,
            // which would clear one set in calibre's GUI on every later SetFields. An unnumbered
            // volume sends no index (calibre keeps its own) rather than 0.
            if (file.Author.Value.Library == LibraryType.LightNovel)
            {
                seriesTitle = file.Author.Value.Name;
                seriesIndex = book.VolumeNumber > 0 ? book.VolumeNumber : (double?)null;
            }

            _logger.Trace("Book: {0} Series: {1}, Position: {2}", book, seriesTitle, seriesIndex);

            var cover = edition.Images.FirstOrDefault(x => x.CoverType == MediaCoverTypes.Cover);
            string image = null;
            if (cover != null)
            {
                var imageFile = _mediaCoverService.GetCoverPath(edition.BookId, MediaCoverEntity.Book, cover.CoverType, cover.Extension, null);

                if (File.Exists(imageFile))
                {
                    var imageData = File.ReadAllBytes(imageFile);
                    if (CalibreImageValidator.IsValidImage(imageData))
                    {
                        image = Convert.ToBase64String(imageData);
                    }
                }
            }

            var textInfo = CultureInfo.InvariantCulture.TextInfo;
            var genres = book.Genres.Select(x => textInfo.ToTitleCase(x.Replace('-', ' '))).ToList();

            // One display title (2026-09-23, the maintainer; fix round 1, L1): a light-novel book's calibre
            // Title (and sort) is "<series>[: <subtitle>] (Vol. <n>)" -- the same string
            // Audiobookshelf's title gets (AdoptedAudioSyncService) -- built from the same
            // seriesTitle SetFields already computes above. An unnumbered volume (VolumeNumber <= 0)
            // keeps edition.Title and sends no sort, the same as before this feature -- ABS skips
            // unnumbered volumes too (AdoptedAudioSyncService), so the two stores still agree. Manga
            // keeps edition.Title/no sort, unchanged. Preferred Edition (2026-09-24, D4): the series'
            // edition picks the volume label ("Tome <n>"); an English series (null) is unchanged.
            var isNumberedLightNovel = file.Author.Value.Library == LibraryType.LightNovel && book.VolumeNumber > 0;
            var title = isNumberedLightNovel ? LightNovelTitles.Display(seriesTitle, book.VolumeNumber, book.Subtitle, EditionLanguages.ReleaseLanguage(file.Author.Value.Metadata.Value)) : edition.Title;
            var sort = isNumberedLightNovel ? LightNovelTitles.SortTitle(seriesTitle, book.VolumeNumber) : null;

            var payload = new CalibreChangesPayload
            {
                LoadedBookIds = new List<int> { file.CalibreId },
                Changes = new CalibreChanges
                {
                    Title = title,
                    Sort = sort,
                    Authors = setAuthors ? new List<string> { AuthorNameFor(file) } : null,
                    Cover = updateCover ? image : null,
                    PubDate = book.ReleaseDate,
                    Publisher = edition.Publisher,
                    Languages = edition.Language.CanonicalizeLanguage(),
                    Tags = genres,
                    Comments = edition.Overview,
                    Rating = (int)(edition.Ratings.Value * 2),
                    Identifiers = new Dictionary<string, string>
                    {
                        { "isbn", edition.Isbn13 },
                        { "asin", edition.Asin },
                        { "goodreads", edition.ForeignEditionId }
                    },
                    Series = seriesTitle,
                    SeriesIndex = seriesIndex
                }
            };

            ExecuteSetFields(file.CalibreId, payload, settings);

            ApplyRenameAndPersist(file, settings);

            if (embed)
            {
                EmbedMetadata(file, settings);
            }
        }

        // One display title (2026-09-23, the maintainer); fix round 1 (C2, scope of the maintainer's approval): the
        // narrow write side of the adopted-book title exception -- "adopted files are immutable" now
        // excludes ONLY the calibre title and sort. the maintainer approved title/sort only -- NOT series:
        // adoption matches a calibre book by a normalised alias (ImportExistingLightNovelsService),
        // so an adopted book's calibre series can legitimately be a different alias or spelling, and
        // calibre's set_field for a series uses allow_case_change=True -- sending author.Name would
        // re-file the book and can re-case the series for every OTHER book that shares it. The
        // payload is CalibreTitlePayload, a dedicated DTO with only {title, sort}: no
        // NullValueHandling.Include property (unlike CalibreChanges.Series) that could leak a value
        // this call never means to send. Same rename-tracking/path-persist tail as SetFields, so a
        // title-triggered calibre rename is never lost.
        public void SetTitle(BookFile file, string title, string sort, CalibreSettings settings)
        {
            var payload = new CalibreTitlePayload
            {
                LoadedBookIds = new List<int> { file.CalibreId },
                Changes = new CalibreTitleChanges
                {
                    Title = title,
                    Sort = sort
                }
            };

            ExecuteSetFields(file.CalibreId, payload, settings);

            ApplyRenameAndPersist(file, settings);
        }

        // updating the calibre metadata may have renamed the file, so track that. Fix round 1 (L2):
        // a light-novel row picks its path the same way OffEntryFileReconciler does --
        // CalibreFormats.TrackedLightNovelFormat (EPUB, else AZW3, else PDF) -- not GetOriginalFormat,
        // which ranks every other text format (MOBI, TXT, ...) equally and ties on oldest mtime; an
        // adopted book with more than one such format could otherwise be repointed at the wrong one,
        // and the next scan would just flip it back. A manga row is unaffected (GetOriginalFormat,
        // as before).
        private void ApplyRenameAndPersist(BookFile file, CalibreSettings settings)
        {
            var updated = GetBook(file.CalibreId, settings);
            var isLightNovel = file.Author?.Value?.Library == LibraryType.LightNovel;

            string updatedPath;

            if (isLightNovel)
            {
                var key = CalibreFormats.TrackedLightNovelFormat(updated.Formats?.Keys);
                updatedPath = key != null ? updated.Formats[key].Path : null;
            }
            else
            {
                updatedPath = GetOriginalFormat(updated.Formats);
            }

            _logger.Trace("File path from Calibre: '{0}'", updatedPath);

            if (updatedPath.IsNotNullOrWhiteSpace() && updatedPath != file.Path)
            {
                _rootFolderWatchingService.ReportFileSystemChangeBeginning(updatedPath);
                file.Path = updatedPath;
            }

            var fileInfo = new FileInfo(file.Path);
            file.Size = fileInfo.Length;
            file.Modified = fileInfo.LastWriteTimeUtc;

            if (file.Id > 0)
            {
                _mediaFileService.Update(file);
            }
        }

        // One copy each (2026-09-20): a light-novel entry is a SERIES, so calibre files its EPUB
        // under the real writer (AuthorMetadata.Writer), falling back to the entry name when the
        // ladder found none; a manga entry keeps the entry name, as it always did.
        private static string AuthorNameFor(BookFile file)
        {
            var author = file.Author.Value;

            if (author.Library == LibraryType.LightNovel)
            {
                var writer = author.Metadata.Value.Writer;

                return writer.IsNotNullOrWhiteSpace() ? writer : author.Name;
            }

            return author.Name;
        }

        // object, not CalibreChangesPayload: SetTitle sends the narrow CalibreTitlePayload instead
        // (fix round 1, C2) -- only .ToJson() is needed, which both payload shapes support equally.
        private void ExecuteSetFields(int id, object payload, CalibreSettings settings)
        {
            var builder = GetBuilder($"cdb/set-fields/{id}/{settings.Library}", settings)
                .Post()
                .SetHeader("Content-Type", "application/json");

            var request = builder.Build();
            request.SetContent(payload.ToJson());
            request.ContentSummary = payload.ToJson(Formatting.None);

            _httpClient.Execute(request);
        }

        private void EmbedMetadata(BookFile file, CalibreSettings settings)
        {
            _rootFolderWatchingService.ReportFileSystemChangeBeginning(file.Path);

            var request = GetBuilder($"cdb/cmd/embed_metadata", settings)
                .AddQueryParam("library_id", settings.Library)
                .Post()
                .SetHeader("Content-Type", "application/json")
                .Build();

            request.SetContent($"[{file.CalibreId}, null]");
            _httpClient.Execute(request);

            PollEmbedStatus(file, settings);
        }

        private void PollEmbedStatus(BookFile file, CalibreSettings settings)
        {
            var previous = new FileInfo(file.Path);
            Thread.Sleep(100);

            FileInfo current = null;

            var i = 0;
            while (i++ < 20)
            {
                current = new FileInfo(file.Path);

                if (current.LastWriteTimeUtc == previous.LastWriteTimeUtc &&
                    current.LastWriteTimeUtc != file.Modified)
                {
                    break;
                }

                previous = current;
                Thread.Sleep(1000);
            }

            file.Size = current.Length;
            file.Modified = current.LastWriteTimeUtc;

            if (file.Id > 0)
            {
                _mediaFileService.Update(file);
            }
        }

        private CalibreBookData GetBookData(int calibreId, CalibreSettings settings)
        {
            try
            {
                var request = GetBuilder($"conversion/book-data/{calibreId}", settings)
                    .AddQueryParam("library_id", settings.Library)
                    .Build();

                return _httpClient.Get<CalibreBookData>(request).Resource;
            }
            catch (HttpException ex)
            {
                throw new CalibreException("Unable to add file to Calibre library: {0}", ex, ex.Message);
            }
        }

        private long ConvertBook(int calibreId, CalibreConversionOptions options, CalibreSettings settings)
        {
            var jobId = StartConversion(calibreId, options, settings);

            // Run async task to check if conversion complete
            _ = PollConvertStatus(jobId, settings);

            return jobId;
        }

        private long StartConversion(int calibreId, CalibreConversionOptions options, CalibreSettings settings)
        {
            try
            {
                var request = GetBuilder($"conversion/start/{calibreId}", settings)
                    .AddQueryParam("library_id", settings.Library)
                    .Build();
                request.SetContent(options.ToJson());

                return _httpClient.Post<long>(request).Resource;
            }
            catch (HttpException ex)
            {
                throw new CalibreException("Unable to start Calibre conversion: {0}", ex, ex.Message);
            }
        }

        public CalibreBook GetBook(int calibreId, CalibreSettings settings)
        {
            try
            {
                var builder = GetBuilder($"ajax/book/{calibreId}/{settings.Library}", settings);

                var request = builder.Build();
                var book = _httpClient.Get<CalibreBook>(request).Resource;

                foreach (var format in book.Formats.Values)
                {
                    format.Path = LocalPathOf(format.Path, settings);
                }

                return book;
            }
            catch (HttpException ex)
            {
                throw new CalibreException("Unable to connect to Calibre library: {0}", ex, ex.Message);
            }
        }

        public List<CalibreBook> GetBooks(List<int> calibreIds, CalibreSettings settings)
        {
            var builder = GetBuilder($"ajax/books/{settings.Library}", settings);
            builder.LogResponseContent = false;
            builder.AddQueryParam("ids", calibreIds.ConcatToString(","));

            var request = builder.Build();

            try
            {
                var response = _httpClient.Get<Dictionary<int, CalibreBook>>(request);

                // One copy each (2026-09-20): an id calibre no longer has comes back as
                // {"<id>": null}; the caller sees it as absent (OffEntryFileReconciler drops the row)
                var result = response.Resource.Values.Where(b => b != null).ToList();

                foreach (var book in result)
                {
                    foreach (var format in book.Formats.Values)
                    {
                        format.Path = LocalPathOf(format.Path, settings);
                    }
                }

                return result;
            }
            catch (HttpException ex)
            {
                throw new CalibreException("Unable to connect to Calibre library: {0}", ex, ex.Message);
            }
        }

        public List<string> GetAllBookFilePaths(CalibreSettings settings)
        {
            var ids = GetAllBookIds(settings);
            var result = new List<string>();

            var offset = 0;

            while (offset < ids.Count)
            {
                var builder = GetBuilder($"ajax/books/{settings.Library}", settings);
                builder.LogResponseContent = false;
                builder.AddQueryParam("ids", ids.Skip(offset).Take(PAGE_SIZE).ConcatToString(","));

                var request = builder.Build();
                try
                {
                    var response = _httpClient.Get<Dictionary<int, CalibreBook>>(request);
                    foreach (var book in response.Resource.Values)
                    {
                        var remotePath = GetOriginalFormat(book?.Formats);

                        if (remotePath == null)
                        {
                            continue;
                        }

                        var localPath = LocalPathOf(remotePath, settings);
                        result.Add(localPath);

                        _bookCache.Set(localPath, book);
                    }
                }
                catch (HttpException ex)
                {
                    throw new CalibreException("Unable to connect to Calibre library: {0}", ex, ex.Message);
                }

                offset += PAGE_SIZE;
            }

            return result;
        }

        public List<int> GetAllBookIds(CalibreSettings settings)
        {
            // the magic string is 'allbooks' converted to hex
            var builder = GetBuilder($"/ajax/category/616c6c626f6f6b73/{settings.Library}", settings);
            var offset = 0;

            var ids = new List<int>();

            while (true)
            {
                var result = GetPaged<CalibreCategory>(builder, PAGE_SIZE, offset);
                if (!result.Resource.BookIds.Any())
                {
                    break;
                }

                offset += PAGE_SIZE;
                ids.AddRange(result.Resource.BookIds);
            }

            return ids;
        }

        private HttpResponse<T> GetPaged<T>(HttpRequestBuilder builder, int count, int offset)
            where T : new()
        {
            builder.AddQueryParam("num", count, replace: true);
            builder.AddQueryParam("offset", offset, replace: true);

            var request = builder.Build();

            try
            {
                return _httpClient.Get<T>(request);
            }
            catch (HttpException ex)
            {
                throw new CalibreException("Unable to connect to Calibre library: {0}", ex, ex.Message);
            }
        }

        // Public (light-novel storage, 2026-09-22): ForConfig's default library and the storage probe read it.
        public CalibreLibraryInfo GetLibraryInfo(CalibreSettings settings)
        {
            var builder = GetBuilder($"ajax/library-info", settings);
            var request = builder.Build();
            var response = _httpClient.Get<CalibreLibraryInfo>(request);

            return response.Resource;
        }

        // Public for CalibreWriteAccessCheck (one copy each, 2026-09-20). Sent with Execute, not Get:
        // Get rewrote the verb, and calibre's router answers 405 to a GET on cdb/cmd before it looks
        // at trusted_ips, so the probe could never see the 403 it tests for.
        public bool HasWriteAccess(CalibreSettings settings)
        {
            var request = GetBuilder($"cdb/cmd/saved_searches", settings)
                .Post()
                .SetHeader("Content-Type", "application/json")
                .Build();

            request.SuppressHttpError = true;
            request.SetContent("[\"list\"]");

            var response = _httpClient.Execute(request);

            return response.StatusCode != HttpStatusCode.Forbidden;
        }

        // Light-novel storage (2026-09-22): the light-novel content server carries its own path pair;
        // a stock calibre root folder keeps Readarr's Remote Path Mappings keyed on its host, unchanged.
        private string LocalPathOf(string calibrePath, CalibreSettings settings)
        {
            if (settings is LightNovelCalibreServerSettings lightNovel)
            {
                return LightNovelPathMap.Map(calibrePath, lightNovel.RemotePath, lightNovel.LocalPath)
                    ?? throw new CalibreException($"Calibre reported '{calibrePath}', which is not under '{lightNovel.RemotePath}': Path As Calibre Sees It");
            }

            return _pathMapper.RemapRemoteToLocal(settings.Host, new OsPath(calibrePath)).FullPath;
        }

        private HttpRequestBuilder GetBuilder(string relativePath, CalibreSettings settings)
        {
            var baseUrl = HttpRequestBuilder.BuildBaseUrl(settings.UseSsl, settings.Host, settings.Port, settings.UrlBase);
            baseUrl = HttpUri.CombinePath(baseUrl, relativePath);

            var builder = new HttpRequestBuilder(baseUrl)
                .Accept(HttpAccept.Json);

            builder.LogResponseContent = true;

            if (settings.Username.IsNotNullOrWhiteSpace())
            {
                builder.NetworkCredential = new NetworkCredential(settings.Username, settings.Password);
            }

            return builder;
        }

        private HttpRequest ConversionStatusRequest(long jobId, CalibreSettings settings)
        {
            return GetBuilder($"/conversion/status/{jobId}", settings)
                .AddQueryParam("library_id", settings.Library)
                .Build();
        }

        // Final review I2: the backfill's synchronous poll -- the same status call and the same
        // 2 s interval as PollConvertStatus, bounded by timeout.
        private bool WaitForConversion(long jobId, int calibreId, string outputFormat, CalibreSettings settings, TimeSpan timeout)
        {
            var request = ConversionStatusRequest(jobId, settings);
            var stopwatch = Stopwatch.StartNew();

            while (true)
            {
                CalibreConversionStatus status;

                try
                {
                    status = _httpClient.Get<CalibreConversionStatus>(request).Resource;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Calibre conversion of book {0} to {1} (job {2}): the status check failed; moving on", calibreId, outputFormat, jobId);
                    return false;
                }

                if (!status.Running)
                {
                    if (!status.Ok)
                    {
                        _logger.Warn("Calibre conversion of book {0} to {1} failed.\n{2}\n{3}", calibreId, outputFormat, status.Traceback, status.Log);
                        return false;
                    }

                    return true;
                }

                if (stopwatch.Elapsed >= timeout)
                {
                    _logger.Warn("Calibre conversion of book {0} to {1} (job {2}) is still running after {3}; moving on to the next book", calibreId, outputFormat, jobId, timeout);
                    return false;
                }

                Thread.Sleep(2000);
            }
        }

        private async Task PollConvertStatus(long jobId, CalibreSettings settings)
        {
            var request = ConversionStatusRequest(jobId, settings);

            while (true)
            {
                CalibreConversionStatus status;

                // Final review M4 (2026-09-22): nothing awaits this task, so an exception here would
                // fault it silently; an unanswered status check is logged and polling stops.
                try
                {
                    status = _httpClient.Get<CalibreConversionStatus>(request).Resource;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Calibre conversion job {0}: the status check failed; no longer following it", jobId);
                    return;
                }

                if (!status.Running)
                {
                    if (!status.Ok)
                    {
                        _logger.Warn("Calibre conversion failed.\n{0}\n{1}", status.Traceback, status.Log);
                    }

                    return;
                }

                await Task.Delay(2000);
            }
        }

        public void Test(CalibreSettings settings)
        {
            var failures = new List<ValidationFailure> { TestCalibre(settings) };
            var validationResult = new ValidationResult(failures);
            var result = new NzbDroneValidationResult(validationResult.Errors);

            if (!result.IsValid || result.HasWarnings)
            {
                throw new ValidationException(result.Failures);
            }
        }

        private ValidationFailure TestCalibre(CalibreSettings settings)
        {
            var builder = GetBuilder("", settings);
            builder.Accept(HttpAccept.Html);
            builder.SuppressHttpError = true;
            builder.AllowAutoRedirect = true;

            var request = builder.Build();
            request.LogResponseContent = false;
            HttpResponse response;

            try
            {
                response = _httpClient.Execute(request);
            }
            catch (WebException ex)
            {
                _logger.Error(ex, "Unable to connect to Calibre");
                if (ex.Status == WebExceptionStatus.ConnectFailure)
                {
                    return new NzbDroneValidationFailure("Host", "Unable to connect")
                    {
                        DetailedDescription = "Please verify the hostname and port."
                    };
                }

                return new NzbDroneValidationFailure(string.Empty, new ServerText("Unknown exception: {0}", ex.Message));
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ValidationFailure("Host", "Could not connect");
            }

            if (response.Content.Contains(@"guac-login"))
            {
                return new ValidationFailure("Port", "Bad port. This is the container's remote Calibre GUI, not the Calibre content server.  Try mapping port 8081.");
            }

            if (response.Content.Contains("Calibre-Web"))
            {
                return new ValidationFailure("Port", "This is a Calibre-Web server, not the required Calibre content server.  See https://manual.calibre-ebook.com/server.html");
            }

            if (!response.Content.Contains(@"<title>calibre</title>"))
            {
                return new ValidationFailure("Port", "Not a valid Calibre content server.  See https://manual.calibre-ebook.com/server.html");
            }

            if (!HasWriteAccess(settings))
            {
                return new ValidationFailure("Username", "Mangarr needs write access. Configure a user or trusted IP in calibre. See https://manual.calibre-ebook.com/server.html");
            }

            var libraryInfo = GetLibraryInfo(settings);

            if (settings.Library.IsNullOrWhiteSpace())
            {
                settings.Library = libraryInfo.DefaultLibrary;
            }

            if (!libraryInfo.LibraryMap.ContainsKey(settings.Library))
            {
                return new ValidationFailure("Library", "Not a valid library in calibre");
            }

            return null;
        }
    }
}
