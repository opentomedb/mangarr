using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MonthlyPacks
{
    public interface IMonthlyPackMatcher
    {
        List<PackFileMatch> Match(IEnumerable<TorrentFileEntry> files, ICollection<Book> eligible);
    }

    // A monthly pack holds many series' volumes. A file is taken only when it names exactly one of the
    // eligible books' series (clean name or alias), exactly one volume, and passes the same edition-language
    // check the decision maker applies to release titles. Everything else is skipped with a logged reason.
    public class MonthlyPackMatcher : IMonthlyPackMatcher
    {
        private static readonly string[] ComicExtensions = { ".cbz", ".cbr", ".cb7", ".zip" };
        private static readonly Regex ParentheticalRegex = new (@"\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex EditionWordingRegex = new (@"\b(edition|omnibus|deluxe|collector)|\bbox\b|\bin[- ]1\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly Logger _logger;

        public MonthlyPackMatcher(Logger logger)
        {
            _logger = logger;
        }

        public List<PackFileMatch> Match(IEnumerable<TorrentFileEntry> files, ICollection<Book> eligible)
        {
            var matches = new List<PackFileMatch>();
            var series = eligible
                .GroupBy(b => b.Author.Value.Id)
                .Select(g => (Author: g.First().Author.Value, Books: g.ToList(), Keys: SeriesKeys(g.First().Author.Value)))
                .ToList();

            foreach (var file in files)
            {
                var match = MatchFile(file, series);

                if (match != null)
                {
                    matches.Add(match);
                }
            }

            return matches;
        }

        private PackFileMatch MatchFile(TorrentFileEntry file, List<(Author Author, List<Book> Books, HashSet<string> Keys)> series)
        {
            var fileName = file.Path.Split('/').Last();

            if (!ComicExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant()))
            {
                return null;
            }

            var name = Path.GetFileNameWithoutExtension(fileName);

            if (MangaVolumeParser.IsChapterOnlyFile(fileName))
            {
                return Skip(file, "a chapter file");
            }

            var info = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(name, info);

            if (info.VolumeStart.HasValue)
            {
                return Skip(file, "a volume range");
            }

            if (!MangaVolumeParser.TryParseSeriesVolume(name, out var seriesName, out var volume))
            {
                return Skip(file, "no series and volume number");
            }

            var cleanName = seriesName.CleanAuthorName();
            var strippedName = MangaVolumeParser.StripParentheticals(seriesName).CleanAuthorName();
            var owners = series.Where(s => s.Keys.Contains(cleanName) || s.Keys.Contains(strippedName)).ToList();

            if (owners.Count == 0)
            {
                return Skip(file, $"series '{seriesName}' is not among the volumes wanted");
            }

            // "Naruto (3-in-1 Edition)" is another edition of Naruto, not its volumes: the stripped name alone
            // never stands in for a parenthetical that names an edition.
            var edition = ParentheticalRegex.Matches(seriesName).Select(m => m.Value).FirstOrDefault(EditionWordingRegex.IsMatch);

            if (edition != null)
            {
                var named = owners.Where(s => s.Keys.Contains(cleanName)).ToList();

                if (named.Count == 0)
                {
                    return Skip(file, $"'{edition}' names another edition of '{owners[0].Author.Name}'");
                }

                owners = named;
            }

            if (owners.Count > 1)
            {
                return Skip(file, $"series '{seriesName}' matches {owners.Count} followed series");
            }

            var owner = owners[0];
            var books = owner.Books.Where(b => Math.Abs(b.VolumeNumber - volume) < 0.001).ToList();

            if (books.Count == 0)
            {
                return Skip(file, $"volume {MangaVolumeParser.Format(volume)} of '{owner.Author.Name}' is not wanted");
            }

            if (books.Count > 1)
            {
                return Skip(file, $"{books.Count} wanted volumes of '{owner.Author.Name}' are numbered {MangaVolumeParser.Format(volume)}");
            }

            var book = books[0];

            var probe = new RemoteBook
            {
                Release = new ReleaseInfo { Title = name },
                Author = owner.Author,
                MediaType = MediaType.Archive
            };
            var language = new EditionLanguageSpecification(_logger).IsSatisfiedBy(probe, null);

            if (!language.Accepted)
            {
                return Skip(file, language.Reason);
            }

            return new PackFileMatch(file, owner.Author, book);
        }

        private PackFileMatch Skip(TorrentFileEntry file, string reason)
        {
            _logger.Debug("Pack file '{0}' skipped: {1}", file.Path, reason);
            return null;
        }

        private static HashSet<string> SeriesKeys(Author author)
        {
            var keys = new HashSet<string>();
            var meta = author.Metadata?.Value;
            var names = new[] { author.Name, meta?.AnchorName }.Concat(meta?.Aliases ?? new List<string>());

            foreach (var n in names.Where(n => n.IsNotNullOrWhiteSpace()))
            {
                keys.Add(n.CleanAuthorName());
                keys.Add(MangaVolumeParser.StripParentheticals(n).CleanAuthorName());
            }

            keys.Remove(string.Empty);
            return keys;
        }
    }
}
