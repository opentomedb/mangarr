using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Chapters;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Qualities;
using SixLabors.ImageSharp;

namespace NzbDrone.Core.MediaFiles.Chapters
{
    public class AssembledVolume
    {
        public Book Book { get; set; }
        public string CbzPath { get; set; }
        public double First { get; set; }
        public double Last { get; set; }
    }

    public class ChapterAssemblyResult
    {
        public List<AssembledVolume> Volumes { get; set; } = new List<AssembledVolume>();
        // Why a mapped volume was not built, in English ("Vol. 9: chapter 75 missing"); SkippedTexts carries
        // the same reasons for the queue in the UI language, SkippedBooks the volume each one is about.
        public List<string> Skipped { get; set; } = new List<string>();
        public List<ServerText> SkippedTexts { get; set; } = new List<ServerText>();
        public List<Book> SkippedBooks { get; set; } = new List<Book>();
    }

    public interface IChapterVolumeAssembler
    {
        ChapterAssemblyResult Assemble(string downloadPath, Author author, List<Book> books, string workFolder);
    }

    // Volumes built from chapter packs (2026-10-21): turns the chapter files a download actually holds into
    // one CBZ per volume (of the ones the grab mapped to) that they complete. Which chapters exist is read
    // from the files, never the release title. Pages are copied byte for byte in chapter order, stored
    // (no recompression) as 0001.ext...; each volume is built to a temp file, verified, then placed in the
    // work folder. The download folder is only read (it may still be seeding). Around the untouched chapter
    // pages go two additions: the volume's cover first, when Mangarr already holds one (copied as is, never
    // downloaded here), and a generated closing page last saying which chapters the volume was built from.
    public class ChapterVolumeAssembler : IChapterVolumeAssembler
    {
        private static readonly HashSet<string> ChapterExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cbz", ".zip" };
        public static readonly HashSet<string> PageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif", ".bmp", ".jxl" };
        private static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        private static readonly Regex Digits = new Regex(@"\d+", RegexOptions.Compiled);

        private readonly IDiskProvider _diskProvider;
        private readonly IEditionService _editionService;
        private readonly IMapCoversToLocal _mediaCoverService;
        private readonly ILocalizationService _localizationService;
        private readonly Logger _logger;

        public ChapterVolumeAssembler(IDiskProvider diskProvider, IEditionService editionService, IMapCoversToLocal mediaCoverService, ILocalizationService localizationService, Logger logger)
        {
            _diskProvider = diskProvider;
            _editionService = editionService;
            _mediaCoverService = mediaCoverService;
            _localizationService = localizationService;
            _logger = logger;
        }

        public ChapterAssemblyResult Assemble(string downloadPath, Author author, List<Book> books, string workFolder)
        {
            var result = new ChapterAssemblyResult();
            var mediaType = MediaTypes.OfQuality(Quality.CbzChapters);
            var language = EditionLanguages.ReleaseLanguage(author.Metadata?.Value);

            // The series' own chapter lists, fresh: the mapped books may come from a history rehydration
            // without their editions, and a half chapter another volume lists belongs to that volume.
            // Planned on local copies: the mapped books belong to the tracked download and are not touched.
            var editions = _editionService.GetEditionsByAuthor(author.Id).Where(e => e.MediaType == mediaType).ToList();
            var bookIds = books.Select(b => b.Id).ToHashSet();
            var originals = books.GroupBy(b => b.Id).ToDictionary(g => g.Key, g => g.First());
            var copies = originals.Values
                .Select(b => new Book { Id = b.Id, VolumeNumber = b.VolumeNumber, Editions = editions.Where(e => e.BookId == b.Id).ToList() })
                .ToList();

            // The series' other volumes take part in planning only so an overlap with them is seen.
            var others = editions
                .Where(e => !bookIds.Contains(e.BookId))
                .GroupBy(e => e.BookId)
                .Select(g => new Book { Id = g.Key, Editions = g.ToList() });

            var files = ChapterFiles(downloadPath, language);
            var listedHere = HalvesOf(editions.Where(e => bookIds.Contains(e.BookId)));
            var listedElsewhere = HalvesOf(editions.Where(e => !bookIds.Contains(e.BookId)));
            var available = files.Keys.Where(c => listedHere.Contains(c) || !listedElsewhere.Contains(c)).ToList();

            var series = copies.Concat(others).ToList();
            var planned = ChapterVolumePlanner.Plan(available, series, mediaType);
            var overlapping = ChapterVolumePlanner.Overlapping(series, mediaType);

            foreach (var book in copies.OrderBy(b => b.VolumeNumber))
            {
                var volume = planned.FirstOrDefault(p => p.Book == book);
                var original = originals[book.Id];

                if (overlapping.Contains(book.Id))
                {
                    Skip(result, original, new ServerText("Vol. {0}: its chapters overlap another volume's in the catalogue", Number(book.VolumeNumber)));
                    continue;
                }

                if (volume == null)
                {
                    var missing = book.EditionOf(mediaType)?.Chapters?.Where(c => c == Math.Floor(c) && !files.ContainsKey(Key(c))).OrderBy(c => c).ToList();

                    if (missing != null && missing.Any())
                    {
                        Skip(result, original, new ServerText("Vol. {0}: chapter {1} missing", Number(book.VolumeNumber), Number(missing.First())));
                    }
                    else
                    {
                        _logger.Debug("{0} Vol. {1} has no chapter list; nothing to build", author, Number(book.VolumeNumber));
                    }

                    continue;
                }

                // Two files claiming one chapter: which one is the chapter is a guess, and a guess is never built.
                var doubled = volume.Chapters.Where(c => files[Key(c)].Count > 1).ToList();

                if (doubled.Any())
                {
                    Skip(result, original, new ServerText("Vol. {0}: chapter {1} has more than one file", Number(book.VolumeNumber), Number(doubled.First())));
                    continue;
                }

                var name = $"{FileNameBuilder.CleanFileName(author.Name)} Vol. {book.VolumeNumber.ToString("00.####", CultureInfo.InvariantCulture)} (Built from chapters {Number(volume.First)}–{Number(volume.Last)}).cbz";
                var path = Path.Combine(workFolder, name);
                var reason = Build(
                    volume.Chapters.Select(c => files[Key(c)].Single()).ToList(),
                    path,
                    LocalCover(original, editions.Where(e => e.BookId == book.Id)),
                    EndPageLines(language, book.VolumeNumber, volume.First, volume.Last));

                if (reason != null)
                {
                    Skip(result, original, new ServerText("Vol. {0}: could not be built ({1})", Number(book.VolumeNumber), reason));
                    continue;
                }

                _logger.Debug("Built {0} from {1} chapter file(s)", name, volume.Chapters.Count);
                result.Volumes.Add(new AssembledVolume { Book = original, CbzPath = path, First = volume.First, Last = volume.Last });
            }

            return result;
        }

        // The pages of the built file, as a reader counts them.
        protected virtual int CountPages(string cbzPath)
        {
            using var zip = ZipFile.OpenRead(cbzPath);

            return zip.Entries.Count(e => !e.FullName.EndsWith("/"));
        }

        // Every chapter archive in the download by its chapter number (the LAST chapter token of its name;
        // a file with none, or with a volume token, is not a chapter). Several files can claim one number.
        private Dictionary<double, List<string>> ChapterFiles(string downloadPath, string language)
        {
            var paths = _diskProvider.FolderExists(downloadPath)
                ? _diskProvider.GetFiles(downloadPath, true).Where(p => !IsMacMetadata(Path.GetRelativePath(downloadPath, p))).ToList()
                : new List<string> { downloadPath };

            return paths
                .Where(p => ChapterExtensions.Contains(Path.GetExtension(p)))
                .Select(p => (Path: p, Chapter: ChapterReleaseParser.ParseFile(Path.GetFileName(p), language)))
                .Where(x => x.Chapter.HasValue)
                .GroupBy(x => Key(x.Chapter.Value))
                .ToDictionary(g => g.Key, g => g.Select(x => x.Path).OrderBy(p => p, StringComparer.Ordinal).ToList());
        }

        private static HashSet<double> HalvesOf(IEnumerable<Edition> editions)
        {
            return editions.SelectMany(e => e.Chapters ?? new List<double>())
                .Where(c => c != Math.Floor(c))
                .Select(Key)
                .ToHashSet();
        }

        // Build -> verify -> place, as PdfToCbzConverter does. Null on success, else why not; nothing is left
        // behind on failure.
        private string Build(List<string> chapterFiles, string path, (byte[] Bytes, string Extension)? cover, string[] endLines)
        {
            var temp = path + ".mangarr-tmp";

            try
            {
                Cleanup(temp);

                var pages = 0;
                int? width = null;
                int? height = null;

                using (var output = File.Create(temp))
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
                {
                    if (cover.HasValue)
                    {
                        pages++;

                        using var coverCopy = zip.CreateFileEntry(pages.ToString("D4") + cover.Value.Extension, CompressionLevel.NoCompression).Open();
                        coverCopy.Write(cover.Value.Bytes, 0, cover.Value.Bytes.Length);
                    }

                    foreach (var chapterFile in chapterFiles)
                    {
                        using var chapter = ZipFile.OpenRead(chapterFile);
                        var entries = chapter.Entries
                            .Where(e => e.Length > 0 && PageExtensions.Contains(Path.GetExtension(e.FullName)) && !IsMacMetadata(e.FullName))
                            .OrderBy(e => NaturalKey(e.FullName), StringComparer.Ordinal)
                            .ToList();

                        if (entries.Count == 0)
                        {
                            return $"{Path.GetFileName(chapterFile)} has no pages";
                        }

                        foreach (var entry in entries)
                        {
                            pages++;

                            var page = zip.CreateFileEntry(pages.ToString("D4") + Path.GetExtension(entry.FullName).ToLowerInvariant(), CompressionLevel.NoCompression);

                            using var input = entry.Open();
                            using var copy = page.Open();

                            if (width == null)
                            {
                                // The first chapter page sets the closing page's size.
                                using var first = new MemoryStream();
                                input.CopyTo(first);
                                first.WriteTo(copy);
                                (width, height) = Dimensions(first);
                            }
                            else
                            {
                                input.CopyTo(copy);
                            }
                        }
                    }

                    if (AddEndPage(zip, pages + 1, width, height, endLines))
                    {
                        pages++;
                    }
                }

                var count = CountPages(temp);

                if (count != pages)
                {
                    return $"built file has {count} pages, expected {pages}";
                }

                if (_diskProvider.FileExists(path))
                {
                    _diskProvider.DeleteFile(path);
                }

                File.Move(temp, path);

                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                _logger.Warn(ex, "Could not build {0}", Path.GetFileName(path));
                return ex.Message;
            }
            finally
            {
                Cleanup(temp);
            }
        }

        // The volume's cover as Mangarr already holds it (the MediaCover cache), or null: no file, or not an
        // image ImageSharp can identify, or whose extension is not a page extension. Its bytes are read once
        // here, so a cover refreshed or deleted meanwhile cannot cost the volume. Nothing is downloaded here.
        private (byte[] Bytes, string Extension)? LocalCover(Book book, IEnumerable<Edition> editions)
        {
            var extensions = editions
                .SelectMany(e => e.Images ?? new List<MediaCover.MediaCover>())
                .Where(i => i.CoverType == MediaCoverTypes.Cover && i.Extension.IsNotNullOrWhiteSpace())
                .Select(i => i.Extension)
                .Concat(CoverExtensions)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var extension in extensions)
            {
                var path = _mediaCoverService.GetCoverPath(book.Id, MediaCoverEntity.Book, MediaCoverTypes.Cover, extension);

                if (path.IsNullOrWhiteSpace() || !PageExtensions.Contains(Path.GetExtension(path)) || !_diskProvider.FileExists(path))
                {
                    continue;
                }

                try
                {
                    var bytes = File.ReadAllBytes(path);
                    Image.Identify(bytes);

                    return (bytes, Path.GetExtension(path).ToLowerInvariant());
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "The cover {0} is not an image; building without a cover page", path);
                }
            }

            return null;
        }

        private static (int? Width, int? Height) Dimensions(MemoryStream image)
        {
            try
            {
                image.Position = 0;
                var info = Image.Identify(image);

                return (info.Width, info.Height);
            }
            catch (Exception)
            {
                return (ChapterVolumeEndPage.FallbackWidth, ChapterVolumeEndPage.FallbackHeight);
            }
        }

        // A page that cannot be drawn is not a reason to lose the volume: false, and the volume has no closing
        // page.
        protected virtual byte[] RenderEndPage(int width, int height, string[] lines)
        {
            return ChapterVolumeEndPage.Render(width, height, lines);
        }

        private bool AddEndPage(ZipArchive zip, int number, int? width, int? height, string[] lines)
        {
            byte[] png;

            try
            {
                png = RenderEndPage(width ?? ChapterVolumeEndPage.FallbackWidth, height ?? ChapterVolumeEndPage.FallbackHeight, lines);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not draw the closing page; building the volume without it");
                return false;
            }

            var entry = zip.CreateFileEntry(number.ToString("D4") + ".png", CompressionLevel.NoCompression);

            using var stream = entry.Open();
            stream.Write(png, 0, png.Length);

            return true;
        }

        // The closing page's three lines in the series' edition language, or in English when any of them has
        // no translation or the translation needs a glyph Roboto lacks (never tofu boxes).
        private string[] EndPageLines(string language, double volume, double first, double last)
        {
            var tokens = new Dictionary<string, string> { { "volume", Number(volume) }, { "first", Number(first) }, { "last", Number(last) } };
            var english = new[]
            {
                new ServerText("End of Vol. {0}", Number(volume)).English,
                new ServerText("Built from chapters {0}–{1}", Number(first), Number(last)).English,
                new ServerText("Mangarr will replace this copy when the official volume is released.").English
            };

            if (EditionLanguages.IsEnglish(language))
            {
                return english;
            }

            try
            {
                var dictionary = _localizationService.GetLocalizationDictionary(language.Trim().Replace('-', '_'));
                var source = _localizationService.GetLocalizationDictionary("en");
                var translated = EndPageKeys.Select(k => Translation(k, dictionary, source, tokens)).ToArray();

                return translated.All(t => t != null) && translated.All(ChapterVolumeEndPage.CanDraw) ? translated : english;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "No closing page translation for {0}; using English", language);
                return english;
            }
        }

        private static readonly string[] EndPageKeys =
        {
            "ServerQueueChapterVolumeEndTitle", "ServerQueueChapterVolumeEndBuilt", "ServerQueueChapterVolumeEndNote"
        };

        // The translated text of a key with its tokens filled, or null when this language has none (its value
        // is then the English one the locale file inherits).
        public static string Translation(string key, IReadOnlyDictionary<string, string> dictionary, IReadOnlyDictionary<string, string> english, IReadOnlyDictionary<string, string> tokens)
        {
            if (dictionary == null || english == null || !dictionary.TryGetValue(key, out var value) || !english.TryGetValue(key, out var source) || value == source)
            {
                return null;
            }

            return Regex.Replace(value, @"\{(?<token>[A-Za-z0-9]+)\}", m => tokens.TryGetValue(m.Groups["token"].Value, out var t) ? t : m.Value);
        }

        private void Cleanup(string temp)
        {
            if (_diskProvider.FileExists(temp))
            {
                _diskProvider.DeleteFile(temp);
            }
        }

        // macOS debris: AppleDouble "._name" files (often .jpg- or .cbz-named, a few KB) and the __MACOSX folder
        // they usually sit in, at any depth -- inside a chapter archive or in the download folder itself. Never a
        // page, never a chapter.
        private static bool IsMacMetadata(string entryName)
        {
            var parts = entryName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            return parts.Any(p => p.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)) ||
                   (parts.Length > 0 && parts.Last().StartsWith("._", StringComparison.Ordinal));
        }

        // "p2.jpg" before "p10.jpg": digit runs compared as numbers.
        private static string NaturalKey(string name)
        {
            return Digits.Replace(name, m => m.Value.PadLeft(10, '0'));
        }

        private void Skip(ChapterAssemblyResult result, Book book, ServerText reason)
        {
            _logger.Debug("Not building from chapters: {0}", reason.English);
            result.Skipped.Add(reason.English);
            result.SkippedTexts.Add(reason);
            result.SkippedBooks.Add(book);
        }

        // A chapter number as the planner keys it.
        private static double Key(double chapter) => Math.Round(chapter, 2);

        private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
