using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    public static class DistanceCalculator
    {
        // The volume the files name: the file name first (what the uploader or the user called it),
        // else the book-title tag. 0 when neither carries a volume token (plain books, audio tracks).
        public static double FileVolume(IEnumerable<LocalBook> localTracks)
        {
            return FileVolume(localTracks, null, null);
        }

        // Preferred Edition (2026-09-24): a series of another edition reads its own tokens in file names
        // ("L'Attaque des Titans T05.cbz"); null = today's parse.
        public static double FileVolume(IEnumerable<LocalBook> localTracks, string editionLanguage, Func<string, bool> isAcceptedSeries)
        {
            var volumes = localTracks
                .Select(t =>
                {
                    if (t.Path.IsNotNullOrWhiteSpace() &&
                        MangaVolumeParser.TryParseSeriesVolume(System.IO.Path.GetFileNameWithoutExtension(t.Path), out _, out var v, editionLanguage, isAcceptedSeries) &&
                        v > 0)
                    {
                        return v;
                    }

                    return MangaVolumeParser.ParseSingleVolume(t.FileTrackInfo?.BookTitle, editionLanguage);
                })
                .Where(v => v > 0)
                .ToList();

            return volumes.Any() ? volumes.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key : 0;
        }

        // Preferred Edition (2026-09-24, M9 pre-review fix): the author options of a non-English edition's
        // series -- its name first, then the anchor and the aliases (alias language is a hint, never a filter).
        // Fix round 1: the anchor and aliases take the parser's 4-character cleaned floor (EditionVolumeTokens.
        // SeriesMatcher), so a two-letter abbreviation cannot pull an unrelated file close; the name itself is
        // always an option, as it is for an English series.
        private static List<string> EditionAuthorOptions(AuthorMetadata meta)
        {
            return new[] { meta.Name }
                .Concat(new[] { meta.AnchorName }
                    .Concat(meta.Aliases ?? new List<string>())
                    .Where(o => o.IsNotNullOrWhiteSpace() && o.CleanAuthorName().Length >= 4))
                .Distinct()
                .ToList();
        }

        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(DistanceCalculator));

        public static readonly List<string> VariousAuthorIds = new List<string> { "89ad4ac3-39f7-470e-963a-56509c546377" };

        private static readonly RegexReplace StripSeriesRegex = new RegexReplace(@"\([^\)].+?\)$", string.Empty, RegexOptions.Compiled);

        private static readonly RegexReplace CleanTitleCruft = new RegexReplace(@"\((?:unabridged)\)", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly List<string> EbookFormats = new List<string> { "Kindle Edition", "Nook", "ebook" };

        private static readonly List<string> AudiobookFormats = new List<string> { "Audiobook", "Audio CD", "Audio Cassette", "Audible Audio", "CD-ROM", "MP3 CD" };

        public static Distance BookDistance(List<LocalBook> localTracks, Edition edition)
        {
            var dist = new Distance();

            // the most common list of authors reported by a file
            var fileAuthors = localTracks.Select(x => x.FileTrackInfo.Authors.Where(a => a.IsNotNullOrWhiteSpace()).ToList())
                .GroupBy(x => x.ConcatToString())
                .OrderByDescending(x => x.Count())
                .First()
                .First();

            var authors = GetAuthorVariants(fileAuthors);

            // Preferred Edition (2026-09-24, M9 pre-review fix): a series of another edition is also known by its
            // English anchor and its aliases, so an English-named file ("Attack on Titan v05") can identify
            // against "L'Attaque des Titans". An English series keeps the one-name call, byte for byte.
            var authorMeta = edition.Book.Value.AuthorMetadata.Value;
            if (EditionLanguages.IsEnglish(authorMeta.EditionLanguage))
            {
                dist.AddString("author", authors, authorMeta.Name);
                Logger.Trace("author: '{0}' vs '{1}'; {2}", authors.ConcatToString("' or '"), edition.Book.Value.AuthorMetadata.Value.Name, dist.NormalizedDistance());
            }
            else
            {
                var authorOptions = EditionAuthorOptions(authorMeta);
                dist.AddString("author", authors, authorOptions);
                Logger.Trace("author: '{0}' vs '{1}'; {2}", authors.ConcatToString("' or '"), authorOptions.ConcatToString("' or '"), dist.NormalizedDistance());
            }

            var title = localTracks.MostCommon(x => x.FileTrackInfo.BookTitle) ?? "";
            var titleOptions = new List<string> { edition.Title };
            if (titleOptions[0].Contains("#"))
            {
                titleOptions.Add(StripSeriesRegex.Replace(titleOptions[0]));
            }

            var (maintitle, _) = edition.Title.SplitBookTitle(edition.Book.Value.AuthorMetadata.Value.Name);
            if (!titleOptions.Contains(maintitle))
            {
                titleOptions.Add(maintitle);
            }

            if (edition.Book.Value.SeriesLinks?.Value?.Any() ?? false)
            {
                foreach (var l in edition.Book.Value.SeriesLinks.Value)
                {
                    if (l.Series?.Value?.Title?.IsNotNullOrWhiteSpace() ?? false)
                    {
                        titleOptions.Add($"{l.Series.Value.Title} {l.Position} {edition.Title}");
                        titleOptions.Add($"{l.Series.Value.Title} Book {l.Position} {edition.Title}");
                        titleOptions.Add($"{edition.Title} {l.Series.Value.Title} {l.Position}");
                        titleOptions.Add($"{edition.Title} {l.Series.Value.Title} Book {l.Position}");
                    }
                }
            }

            var fileTitles = new[] { title, CleanTitleCruft.Replace(title) }.Distinct().ToList();

            dist.AddString("book", fileTitles, titleOptions);
            Logger.Trace("book: '{0}' vs '{1}'; {2}", fileTitles.ConcatToString("' or '"), titleOptions.ConcatToString("' or '"), dist.NormalizedDistance());

            // Volume number (2026-09-22): the title distance above cannot tell "Classroom of the Elite -
            // Volume 11.5" from "... Vol. 11" -- a pack carrying both put the side volume on volume 11's
            // edition and dropped the real one. When the file names a volume and the book has one, a
            // different number is a hard mismatch (weighted like an ISBN), so the file matches its own
            // volume or nothing. Ebooks and archives only: audiobook releases number their parts
            // ("Classroom.of.the.Elite,.Vol..3.02.mp4" is a part of volume 2), which a volume parse
            // misreads -- measured 2026-09-22 against 4,107 live and imported paths: the only
            // mismatches were that 11.5 file and three such audio parts.
            var isAudioFile = localTracks.Any(t => t.Path.IsNotNullOrWhiteSpace() &&
                                               MediaTypes.OfExtension(System.IO.Path.GetExtension(t.Path)) == MediaType.Audio);
            var fileVolume = isAudioFile ? 0 : FileVolume(localTracks, authorMeta.EditionLanguage, EditionVolumeTokens.SeriesMatcher(authorMeta));
            var bookVolume = edition.Book.Value.VolumeNumber;
            if (fileVolume > 0 && bookVolume > 0)
            {
                dist.AddBool("volume", Math.Abs(fileVolume - (double)bookVolume) > 0.001);
                Logger.Trace("volume: {0} vs {1}; {2}", fileVolume, bookVolume, dist.NormalizedDistance());
            }

            var isbn = localTracks.MostCommon(x => x.FileTrackInfo.Isbn);
            if (isbn.IsNotNullOrWhiteSpace() && edition.Isbn13.IsNotNullOrWhiteSpace())
            {
                dist.AddBool("isbn", isbn != edition.Isbn13);
                Logger.Trace("isbn: '{0}' vs '{1}'; {2}", isbn, edition.Isbn13, dist.NormalizedDistance());
            }
            else if (isbn.IsNullOrWhiteSpace() != edition.Isbn13.IsNullOrWhiteSpace())
            {
                dist.AddBool("isbn_missing", true);
                Logger.Trace("isbn: '{0}' vs '{1}'; {2}", isbn, edition.Isbn13, dist.NormalizedDistance());
            }

            var asin = localTracks.MostCommon(x => x.FileTrackInfo.Asin);
            if (asin.IsNotNullOrWhiteSpace() && edition.Asin.IsNotNullOrWhiteSpace())
            {
                dist.AddBool("asin", asin != edition.Asin);
                Logger.Trace("asin: '{0}' vs '{1}'; {2}", asin, edition.Asin, dist.NormalizedDistance());
            }
            else if (asin.IsNullOrWhiteSpace() != edition.Asin.IsNullOrWhiteSpace())
            {
                dist.AddBool("asin_missing", true);
                Logger.Trace("asin: '{0}' vs '{1}'; {2}", asin, edition.Asin, dist.NormalizedDistance());
            }

            // Year
            var localYear = localTracks.MostCommon(x => x.FileTrackInfo.Year);
            if (localYear > 0 && edition.ReleaseDate.HasValue)
            {
                var bookYear = edition.ReleaseDate?.Year ?? 0;
                if (localYear == bookYear)
                {
                    dist.Add("year", 0.0);
                }
                else
                {
                    var remoteYear = bookYear;
                    var diff = Math.Abs(localYear - remoteYear);
                    var diff_max = Math.Abs(DateTime.Now.Year - remoteYear);
                    dist.AddRatio("year", diff, diff_max);
                }

                Logger.Trace($"year: {localYear} vs {edition.ReleaseDate?.Year}; {dist.NormalizedDistance()}");
            }

            // Language - only if set for both the local book and remote edition
            var localLanguage = localTracks.MostCommon(x => x.FileTrackInfo.Language).CanonicalizeLanguage();
            var editionLanguage = edition.Language.CanonicalizeLanguage();
            if (localLanguage.IsNotNullOrWhiteSpace() && editionLanguage.IsNotNullOrWhiteSpace())
            {
                dist.AddBool("language", localLanguage != editionLanguage);
                Logger.Trace($"language: {localLanguage} vs {editionLanguage}; {dist.NormalizedDistance()}");
            }

            // Publisher - only if set for both the local book and remote edition
            var localPublisher = localTracks.MostCommon(x => x.FileTrackInfo.Publisher);
            var editionPublisher = edition.Publisher;
            if (localPublisher.IsNotNullOrWhiteSpace() && editionPublisher.IsNotNullOrWhiteSpace())
            {
                dist.AddString("publisher", localPublisher, editionPublisher);
                Logger.Trace($"publisher: {localPublisher} vs {editionPublisher}; {dist.NormalizedDistance()}");
            }

            // try to tilt it towards the correct "type" of release
            var isAudio = MediaFileExtensions.AudioExtensions.Contains(localTracks.First().Path.GetPathExtension());

            if (edition.Format.IsNotNullOrWhiteSpace())
            {
                if (!isAudio)
                {
                    // text books should prefer ebook formats
                    dist.AddBool("ebook_format", !EbookFormats.Contains(edition.Format));

                    // text books should not match audio entries
                    dist.AddBool("wrong_format", AudiobookFormats.Contains(edition.Format));
                }
                else
                {
                    // audio books should prefer audio formats
                    dist.AddBool("audio_format", !AudiobookFormats.Contains(edition.Format));
                }
            }

            return dist;
        }

        public static List<string> GetAuthorVariants(List<string> fileAuthors)
        {
            var authors = new List<string>(fileAuthors);

            if (fileAuthors.Count == 1)
            {
                authors.AddRange(SplitAuthor(fileAuthors[0]));
            }

            foreach (var author in fileAuthors)
            {
                if (author.Contains(','))
                {
                    var split = author.Split(',', 2).Select(x => x.Trim());
                    if (!split.First().Contains(' '))
                    {
                        authors.Add(split.Reverse().ConcatToString(" "));
                    }
                }
            }

            return authors;
        }

        private static List<string> SplitAuthor(string input)
        {
            var seps = new[] { ';', '/' };
            foreach (var sep in seps)
            {
                if (input.Contains(sep))
                {
                    return input.Split(sep).Select(x => x.Trim()).ToList();
                }
            }

            var andSeps = new List<string> { " and ", " & " };
            foreach (var sep in andSeps)
            {
                if (input.Contains(sep))
                {
                    var result = new List<string>();
                    foreach (var s in input.Split(sep).Select(x => x.Trim()))
                    {
                        var s2 = SplitAuthor(s);
                        if (s2.Any())
                        {
                            result.AddRange(s2);
                        }
                        else
                        {
                            result.Add(s);
                        }
                    }

                    return result;
                }
            }

            if (input.Contains(','))
            {
                var split = input.Split(',').Select(x => x.Trim()).ToList();
                if (split[0].Contains(' '))
                {
                    return split;
                }
            }

            return new List<string>();
        }
    }
}
