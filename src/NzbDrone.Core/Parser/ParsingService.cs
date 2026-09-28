using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Parser
{
    public interface IParsingService
    {
        Author GetAuthor(string title);
        RemoteBook Map(ParsedBookInfo parsedBookInfo, SearchCriteriaBase searchCriteria = null);
        RemoteBook Map(ParsedBookInfo parsedBookInfo, int authorId, IEnumerable<int> bookIds);
        List<Book> GetBooks(ParsedBookInfo parsedBookInfo, Author author, SearchCriteriaBase searchCriteria = null);

        ParsedBookInfo ParseBookTitleFuzzy(string title);

        // Music stuff here
        Book GetLocalBook(string filename, Author author);
    }

    public class ParsingService : IParsingService
    {
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly Logger _logger;

        public ParsingService(IAuthorService authorService,
                              IBookService bookService,
                              IEditionService editionService,
                              IMediaFileService mediaFileService,
                              Logger logger)
        {
            _bookService = bookService;
            _editionService = editionService;
            _authorService = authorService;
            _mediaFileService = mediaFileService;
            _logger = logger;
        }

        public Author GetAuthor(string title)
        {
            var parsedBookInfo = Parser.ParseBookTitle(title);

            if (parsedBookInfo != null && !parsedBookInfo.AuthorName.IsNullOrWhiteSpace())
            {
                title = parsedBookInfo.AuthorName;
            }

            var authorInfo = _authorService.FindByName(title);

            if (authorInfo == null)
            {
                _logger.Debug("Trying inexact author match for {0}", title);
                authorInfo = _authorService.FindByNameInexact(title);
            }

            return authorInfo;
        }

        public RemoteBook Map(ParsedBookInfo parsedBookInfo, SearchCriteriaBase searchCriteria = null)
        {
            var remoteBook = new RemoteBook
            {
                ParsedBookInfo = parsedBookInfo,
            };

            var author = GetAuthor(parsedBookInfo, searchCriteria);

            remoteBook.MediaType = ResolveMediaType(parsedBookInfo, author, searchCriteria);

            if (author == null)
            {
                return remoteBook;
            }

            remoteBook.Author = author;
            remoteBook.Books = GetBooks(parsedBookInfo, author, searchCriteria);

            return remoteBook;
        }

        // Light novels (2026-09): the release's quality class picks the edition it targets. Unknown
        // quality on a light-novel author follows the search that asked for it (an audio search wants
        // the audio edition) — unless the name carries manga wording (D1), which is the archive class
        // whatever was searched; everywhere else Unknown means the manga archive class, as before.
        // The decision maker threads the indexer title in; other callers read the parsed one.
        public static MediaType ResolveMediaType(ParsedBookInfo parsedBookInfo, Author author, SearchCriteriaBase searchCriteria, string releaseTitle = null, List<int> categories = null)
        {
            var quality = parsedBookInfo?.Quality?.Quality ?? Quality.Unknown;

            if (quality == Quality.Unknown && searchCriteria != null && author?.Library == LibraryType.LightNovel)
            {
                var name = releaseTitle ?? parsedBookInfo?.ReleaseTitle ?? parsedBookInfo?.BookTitle;

                if (QualityParser.LooksLikeManga(name))
                {
                    return MediaType.Archive;
                }

                // 2026-09-21: a book-category release with no audiobook wording is the ebook class
                // even when the search asked for audio (see RegradeForLibrary).
                if (searchCriteria.MediaType == MediaType.Audio &&
                    categories != null && categories.Any() && !categories.Any(MediaTypes.IsAudioCategory) &&
                    !QualityParser.LooksLikeAudiobook(name))
                {
                    return MediaType.Ebook;
                }

                return searchCriteria.MediaType;
            }

            return MediaTypes.OfQuality(quality);
        }

        // Light novels (2026-09): a volume-tokened title with no format token grades CBZ by the
        // manga default (the quality parser has no author). For a light-novel author that is the
        // wrong class, so re-grade by library: audiobook wording, an audio search or an audio
        // category -> Unknown Audio; otherwise EPUB. An explicit archive token (cbz/cbr/zip/rar; a
        // lone pdf is below) or manga wording ("...Vol.06.Manga...Comic.eBook", D1) is kept and the
        // media-type spec rejects it as today; Unknown (mobi/azw, or no volume token) stays
        // ResolveMediaType's business; a manga author never enters, so its CBZ default is untouched.
        // Returns true when the quality changed, so the caller re-resolves the type.
        public static bool RegradeForLibrary(ParsedBookInfo parsedBookInfo, string title, Author author, SearchCriteriaBase searchCriteria, List<int> categories)
        {
            var quality = parsedBookInfo?.Quality?.Quality;

            // LN PDF (2026-09-22): a light novel's "[PDF]" release is its ebook -- quality Ebook PDF,
            // which the light-novel profile offers unticked below AZW3. Only a PDF grade whose one
            // archive token is "pdf" and whose name has no manga wording: "... (Manga) [PDF]" or
            // "[PDF] [CBZ]" keeps the manga PDF, which the media-type spec refuses for a light
            // novel as before. A manga author never enters. Only the quality changes; the revision
            // stays.
            if (author?.Library == LibraryType.LightNovel &&
                quality == Quality.PDF &&
                QualityParser.HasOnlyPdfArchiveToken(title) &&
                !QualityParser.LooksLikeManga(title))
            {
                parsedBookInfo.Quality.Quality = Quality.EbookPdf;

                return true;
            }

            if (author?.Library != LibraryType.LightNovel ||
                quality == null ||
                quality == Quality.Unknown ||
                MediaTypes.OfQuality(quality) != MediaType.Archive ||
                QualityParser.HasExplicitArchiveToken(title) ||
                QualityParser.LooksLikeManga(title))
            {
                return false;
            }

            // 2026-09-21: an audio search now reaches book-only indexers (Nyaa), so the search
            // asking for audio is no longer evidence -- "Sentenced to Be a Hero v01-05 [Yen Press]
            // [Stick]" (an EPUB batch) was graded Unknown Audio and grabbed by the audio leg. With
            // categories known and none of them audio, only audiobook wording says audio.
            var audioCategory = categories?.Any(MediaTypes.IsAudioCategory) ?? false;
            var bookCategoryOnly = categories != null && categories.Any() && !audioCategory;
            var audio = QualityParser.LooksLikeAudiobook(title) ||
                        audioCategory ||
                        (searchCriteria?.MediaType == MediaType.Audio && !bookCategoryOnly);

            parsedBookInfo.Quality.Quality = audio ? Quality.UnknownAudio : Quality.EPUB;

            return true;
        }

        public List<Book> GetBooks(ParsedBookInfo parsedBookInfo, Author author, SearchCriteriaBase searchCriteria = null)
        {
            var bookTitle = parsedBookInfo.BookTitle;
            var result = new List<Book>();

            if (parsedBookInfo.BookTitle == null)
            {
                return new List<Book>();
            }

            Book bookInfo = null;

            if (parsedBookInfo.Discography)
            {
                if (parsedBookInfo.DiscographyStart > 0)
                {
                    return _bookService.AuthorBooksBetweenDates(author,
                        new DateTime(parsedBookInfo.DiscographyStart, 1, 1),
                        new DateTime(parsedBookInfo.DiscographyEnd, 12, 31),
                        false);
                }

                if (parsedBookInfo.DiscographyEnd > 0)
                {
                    return _bookService.AuthorBooksBetweenDates(author,
                        new DateTime(1800, 1, 1),
                        new DateTime(parsedBookInfo.DiscographyEnd, 12, 31),
                        false);
                }

                return _bookService.GetBooksByAuthor(author.Id);
            }

            // Preferred Edition (2026-09-24, M9 fix round 1 ⚠1): the grab path parses generically first, so
            // "L'Attaque des Titans - Tome 5 [FR]" arrives here as book "Tome 5" with no volume and would fall to
            // the title lookups below (FindByTitleInexact can pick any volume). For a series of another edition
            // the release title is read again through its own tokens, and a numbered collected release is
            // rejected (ruling A9) unless the series is bound to a collected line. English: never entered.
            if (!ReadEditionVolume(parsedBookInfo, author))
            {
                return new List<Book>();
            }

            // Manga pack fan-out (Increment 4a): a release parsed to a volume range (e.g.
            // "Volumes 1-13") satisfies every volume in [start, end]. Mirror the Discography
            // branch above — map the one release to the many matching Volume rows so the
            // decision/grab pipeline treats the pack as covering all of them, and the per-book
            // dedupe suppresses redundant single-volume grabs. Only triggers for manga
            // (VolumeStart/VolumeEnd set by MangaVolumeParser); non-manga releases are unaffected.
            if (parsedBookInfo.VolumeStart.HasValue && parsedBookInfo.VolumeEnd.HasValue)
            {
                var start = parsedBookInfo.VolumeStart.Value;
                var end = parsedBookInfo.VolumeEnd.Value;

                var authorBooks = _bookService.GetBooksByAuthor(author.Id);

                // A tokenless whole-series batch parses as 1-9999; cap its claim at the
                // highest RELEASED volume so an open-ended batch can never cover unreleased
                // volumes — and, tied on count, it loses to an honest bounded pack via the
                // explicit-volume-evidence comparison in DownloadDecisionComparer.
                if (end >= 9999)
                {
                    var now = DateTime.UtcNow;
                    var highestReleased = authorBooks
                        .Where(b => b.ReleaseDate.HasValue && b.ReleaseDate.Value <= now)
                        .Select(b => b.VolumeNumber)
                        .DefaultIfEmpty(0)
                        .Max();

                    if (highestReleased > 0)
                    {
                        end = highestReleased;
                    }
                }

                var packBooks = new List<Book>();
                foreach (var book in authorBooks)
                {
                    if (book.VolumeNumber >= start && book.VolumeNumber <= end)
                    {
                        packBooks.Add(book);
                    }
                }

                if (packBooks.Count > 0)
                {
                    return packBooks;
                }

                _logger.Debug("No volumes in range {0}-{1} for {2}", start, end, author);
                return result;
            }

            // Manga single-volume mapping: a release parsed to a single VolumeNumber maps to the
            // series volume with that number (more reliable than matching "v25" against the edition
            // title "Vol. 25"). Gated on VolumeNumber, so non-manga releases (null) are unaffected;
            // falls through to title resolution when the volume isn't in the library.
            if (parsedBookInfo.VolumeNumber.HasValue)
            {
                var target = parsedBookInfo.VolumeNumber.Value;
                var volumeBook = _bookService.GetBooksByAuthor(author.Id)
                    .FirstOrDefault(b => Math.Abs(b.VolumeNumber - target) < 0.001);

                if (volumeBook != null)
                {
                    return new List<Book> { volumeBook };
                }
            }

            if (searchCriteria != null)
            {
                var cleanTitle = Parser.CleanAuthorName(parsedBookInfo.BookTitle);
                bookInfo = searchCriteria.Books.ExclusiveOrDefault(e => e.Title == bookTitle || e.CleanTitle == cleanTitle);
            }

            if (bookInfo == null)
            {
                // TODO: Search by Title and Year instead of just Title when matching
                bookInfo = _bookService.FindByTitle(author.AuthorMetadataId, parsedBookInfo.BookTitle);
            }

            if (bookInfo == null)
            {
                var edition = _editionService.FindByTitle(author.AuthorMetadataId, parsedBookInfo.BookTitle);
                bookInfo = edition?.Book.Value;
            }

            if (bookInfo == null)
            {
                _logger.Debug("Trying inexact book match for {0}", parsedBookInfo.BookTitle);
                bookInfo = _bookService.FindByTitleInexact(author.AuthorMetadataId, parsedBookInfo.BookTitle);
            }

            if (bookInfo == null)
            {
                _logger.Debug("Trying inexact edition match for {0}", parsedBookInfo.BookTitle);
                var edition = _editionService.FindByTitleInexact(author.AuthorMetadataId, parsedBookInfo.BookTitle);
                bookInfo = edition?.Book.Value;
            }

            if (bookInfo != null)
            {
                result.Add(bookInfo);
            }
            else
            {
                _logger.Debug("Unable to find {0}", parsedBookInfo);
            }

            return result;
        }

        // False = reject the release (a numbered collected edition). Only a non-English series with a parsed
        // release title reads anything; a volume the generic parse already found is kept.
        private bool ReadEditionVolume(ParsedBookInfo parsedBookInfo, Author author)
        {
            var meta = author.Metadata?.Value;

            if (EditionLanguages.IsEnglish(meta?.EditionLanguage) || parsedBookInfo.ReleaseTitle.IsNullOrWhiteSpace())
            {
                return true;
            }

            if (!parsedBookInfo.VolumeNumber.HasValue && !parsedBookInfo.VolumeStart.HasValue)
            {
                var editionTitle = EditionVolumeTokens.Rewrite(parsedBookInfo.ReleaseTitle, meta.EditionLanguage, EditionVolumeTokens.SeriesMatcher(meta));
                MangaVolumeParser.ParseVolume(editionTitle, parsedBookInfo);
            }

            if ((parsedBookInfo.VolumeNumber.HasValue || parsedBookInfo.VolumeStart.HasValue) &&
                EditionVolumeTokens.IsCollectedEdition(parsedBookInfo.ReleaseTitle) &&
                !EditionVolumeTokens.IsCollectedSeries(meta))
            {
                _logger.Debug("Manga release '{0}': a numbered collected edition for '{1}', rejecting", parsedBookInfo.ReleaseTitle, author.Name);
                return false;
            }

            return true;
        }

        public RemoteBook Map(ParsedBookInfo parsedBookInfo, int authorId, IEnumerable<int> bookIds)
        {
            return new RemoteBook
            {
                ParsedBookInfo = parsedBookInfo,
                Author = _authorService.GetAuthor(authorId),
                Books = _bookService.GetBooks(bookIds),
                MediaType = ResolveMediaType(parsedBookInfo, null, null)
            };
        }

        private Author GetAuthor(ParsedBookInfo parsedBookInfo, SearchCriteriaBase searchCriteria)
        {
            Author author = null;

            if (searchCriteria != null)
            {
                var authorClean = searchCriteria.Author.CleanName;

                // Manga authority: when the release itself is a volume-tokened manga release, its
                // parsed SERIES is its true identity (releases credit the mangaka — "Gyo Vol.1-2 by
                // Junji Ito" — so we match by series, not the parsed author). This runs BEFORE the
                // AuthorName check because the generic ParseBookTitle mis-splits "Tokyo Ghoul - re
                // v01" into AuthorName "Tokyo Ghoul", which would otherwise match the searched author.
                if (parsedBookInfo.ReleaseTitle.IsNotNullOrWhiteSpace() &&
                    MangaVolumeParser.TryParseSeries(parsedBookInfo.ReleaseTitle, out var releaseSeries))
                {
                    var seriesClean = releaseSeries.CleanAuthorName();
                    var seriesStripped = MangaVolumeParser.StripParentheticals(releaseSeries).CleanAuthorName();

                    // The release's series — or either half of a "Romaji / English" dual title —
                    // matches the searched series -> attribute it.
                    if (MangaVolumeParser.ExpandDualTitles(releaseSeries).Any(s =>
                            authorClean == s.CleanAuthorName() ||
                            authorClean == MangaVolumeParser.StripParentheticals(s).CleanAuthorName()))
                    {
                        return searchCriteria.Author;
                    }

                    // The release's series EXTENDS the searched author with extra tokens -> it is a
                    // DIFFERENT series (a sequel/spin-off like "Tokyo Ghoul:re" parsed as "Tokyo Ghoul
                    // - re"). Reject rather than letting the ParseBookTitle mis-parse grab the wrong
                    // series. Abbreviated names (series SHORTER than the author) fall through unchanged.
                    if (seriesClean.StartsWith(authorClean) || seriesStripped.StartsWith(authorClean))
                    {
                        _logger.Debug("Manga release '{0}': series '{1}' extends searched author '{2}', rejecting (different series)",
                            parsedBookInfo.ReleaseTitle, releaseSeries, searchCriteria.Author.Name);
                        return null;
                    }
                }

                if (authorClean == parsedBookInfo.AuthorName.CleanAuthorName())
                {
                    return searchCriteria.Author;
                }
            }

            // Light novels (2026-09, final review I4): the manga and the light novel of one name are
            // different entries (CleanName "<clean>" / "<clean>~ln"), so a name lookup is per library.
            // A release whose class is already known to be ebook / audio -- its parsed quality, or
            // light-novel / audiobook wording in the title -- belongs to the light-novel entry, so
            // that is tried first. Anything else (every archive release, and the tokenless titles the
            // manga default grades CBZ) tries the manga entry first, the lookup it always was, and
            // falls back to the light-novel entry only on a miss -- so a tokenless title still
            // resolves to the manga twin when both exist. The inexact fallback is unchanged.
            var libraries = LooksLikeLightNovelRelease(parsedBookInfo)
                ? new[] { LibraryType.LightNovel, LibraryType.Manga }
                : new[] { LibraryType.Manga, LibraryType.LightNovel };

            author = libraries.Select(library => _authorService.FindByName(parsedBookInfo.AuthorName, library)).FirstOrDefault(a => a != null);

            if (author == null)
            {
                _logger.Debug("Trying inexact author match for {0}", parsedBookInfo.AuthorName);
                author = _authorService.FindByNameInexact(parsedBookInfo.AuthorName);
            }

            if (author == null)
            {
                _logger.Debug("No matching author {0}", parsedBookInfo.AuthorName);
                return null;
            }

            return author;
        }

        // Whether the release's class is already known to be ebook / audio: the parsed quality says
        // so, or the title carries light-novel / audiobook wording (a tokenless LN title grades CBZ
        // by the manga default until RegradeForLibrary sees its author).
        private static bool LooksLikeLightNovelRelease(ParsedBookInfo parsedBookInfo)
        {
            return MediaTypes.OfQuality(parsedBookInfo.Quality?.Quality) != MediaType.Archive ||
                   QualityParser.LooksLikeEbook(parsedBookInfo.ReleaseTitle) ||
                   QualityParser.LooksLikeAudiobook(parsedBookInfo.ReleaseTitle);
        }

        public ParsedBookInfo ParseBookTitleFuzzy(string title)
        {
            var bestScore = 0.0;

            Author bestAuthor = null;
            Book bestBook = null;

            var possibleAuthors = _authorService.GetReportCandidates(title);

            foreach (var author in possibleAuthors)
            {
                _logger.Trace($"Trying possible author {author}");

                var authorMatch = title.FuzzyMatch(author.Metadata.Value.Name, 0.5);
                var possibleBooks = _bookService.GetCandidates(author.AuthorMetadataId, title);

                foreach (var book in possibleBooks)
                {
                    var bookMatch = title.FuzzyMatch(book.Title, 0.5);
                    var score = (authorMatch.Item3 + bookMatch.Item3) / 2;

                    _logger.Trace($"Book {book} has score {score}");

                    // bestScore must actually track the winner (it previously never updated,
                    // so the LAST nonzero candidate won, not the best), the book term itself
                    // must have matched, and a floor keeps author-only half-matches from
                    // adopting RSS releases into the wrong series.
                    if (bookMatch.Item3 > 0 && score >= 0.6 && score > bestScore)
                    {
                        bestScore = score;
                        bestAuthor = author;
                        bestBook = book;
                    }
                }

                var possibleEditions = _editionService.GetCandidates(author.AuthorMetadataId, title);
                foreach (var edition in possibleEditions)
                {
                    var editionMatch = title.FuzzyMatch(edition.Title, 0.5);
                    var score = (authorMatch.Item3 + editionMatch.Item3) / 2;

                    _logger.Trace($"Edition {edition} has score {score}");

                    if (editionMatch.Item3 > 0 && score >= 0.6 && score > bestScore)
                    {
                        bestScore = score;
                        bestAuthor = author;
                        bestBook = edition.Book.Value;
                    }
                }
            }

            _logger.Trace($"Best match: {bestAuthor} {bestBook}");

            if (bestAuthor != null)
            {
                return Parser.ParseBookTitleWithSearchCriteria(title, bestAuthor, new List<Book> { bestBook });
            }

            return null;
        }

        public Book GetLocalBook(string filename, Author author)
        {
            if (Path.HasExtension(filename))
            {
                filename = Path.GetDirectoryName(filename);
            }

            var tracksInBook = _mediaFileService.GetFilesByAuthor(author.Id)
                .FindAll(s => Path.GetDirectoryName(s.Path) == filename)
                .DistinctBy(s => s.EditionId)
                .ToList();

            return tracksInBook.Count == 1 ? _bookService.GetBook(tracksInBook.First().EditionId) : null;
        }
    }
}
