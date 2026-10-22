using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;

namespace NzbDrone.Core.MonthlyPacks
{
    public interface IMonthlyPackService
    {
        List<Book> Search(IEnumerable<Book> wanted);
        List<Book> ProcessReleases(IEnumerable<ReleaseInfo> releases, IEnumerable<Book> wanted);
    }

    // Monthly multi-series volume packs ("... Volumes Update ... (Digital)"): one pack query per run, each
    // candidate's file list read from its .torrent (cached by info hash), files matched to the wanted volumes,
    // each volume taken from the best pack that holds it, and one grab per pack with only those files selected.
    public class MonthlyPackService : IMonthlyPackService
    {
        private const int DaysAfterRelease = 3;
        private const int MaximumPackAgeDays = 365;
        private const int MaximumFetchesPerRun = 10;
        private const int MaximumRedirects = 10;

        private static readonly Regex VolumesUpdateRegex = new (@"\bVolumes\s+Update\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DigitalRegex = new (@"\(Digital\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ScanRegex = new (@"\bscan(s|lation)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly IConfigService _configService;
        private readonly IIndexerFactory _indexerFactory;
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IDownloadService _downloadService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IQueueService _queueService;
        private readonly IHistoryService _historyService;
        private readonly IBlocklistService _blocklistService;
        private readonly IPackFileListRepository _packFileListRepository;
        private readonly ITorrentFileInfoReader _torrentFileInfoReader;
        private readonly IMonthlyPackMatcher _matcher;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public MonthlyPackService(IConfigService configService,
                                  IIndexerFactory indexerFactory,
                                  IProvideDownloadClient downloadClientProvider,
                                  IDownloadService downloadService,
                                  IEditionService editionService,
                                  IMediaFileService mediaFileService,
                                  IQueueService queueService,
                                  IHistoryService historyService,
                                  IBlocklistService blocklistService,
                                  IPackFileListRepository packFileListRepository,
                                  ITorrentFileInfoReader torrentFileInfoReader,
                                  IMonthlyPackMatcher matcher,
                                  IHttpClient httpClient,
                                  Logger logger)
        {
            _configService = configService;
            _indexerFactory = indexerFactory;
            _downloadClientProvider = downloadClientProvider;
            _downloadService = downloadService;
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _queueService = queueService;
            _historyService = historyService;
            _blocklistService = blocklistService;
            _packFileListRepository = packFileListRepository;
            _torrentFileInfoReader = torrentFileInfoReader;
            _matcher = matcher;
            _httpClient = httpClient;
            _logger = logger;
        }

        public List<Book> Search(IEnumerable<Book> wanted)
        {
            var eligible = Prepare(wanted);

            if (eligible.Count == 0)
            {
                return new List<Book>();
            }

            // No series in the query, so the indexer-tag filter of a series search does not apply.
            var indexers = _indexerFactory.AutomaticSearchEnabled()
                .Where(i => i.Protocol == DownloadProtocol.Torrent)
                .Where(i =>
                {
                    if (IsMyAnonamouse(i.Definition as IndexerDefinition))
                    {
                        _logger.Debug("Monthly pack search skips indexer '{0}': MyAnonamouse is never used for packs", i.Definition.Name);
                        return false;
                    }

                    return true;
                })
                .ToList();

            _logger.Debug("Searching {0} indexer(s) for monthly volume packs for {1} wanted volume(s)", indexers.Count, eligible.Count);

            var criteria = new PackSearchCriteria();
            var batches = Task.WhenAll(indexers.Select(i => FetchPacks(i, criteria))).GetAwaiter().GetResult();

            return Process(batches.SelectMany(b => b).ToList(), eligible, ReleaseSourceType.Search);
        }

        public List<Book> ProcessReleases(IEnumerable<ReleaseInfo> releases, IEnumerable<Book> wanted)
        {
            var eligible = Prepare(wanted);

            if (eligible.Count == 0)
            {
                return new List<Book>();
            }

            return Process(releases.ToList(), eligible, ReleaseSourceType.Rss);
        }

        private async Task<IList<ReleaseInfo>> FetchPacks(IIndexer indexer, PackSearchCriteria criteria)
        {
            try
            {
                return await indexer.FetchPacks(criteria);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while searching '{0}' for monthly packs", indexer.Definition.Name);
            }

            return Array.Empty<ReleaseInfo>();
        }

        private List<Book> Prepare(IEnumerable<Book> wanted)
        {
            if (!_configService.UseMonthlyVolumePacks)
            {
                _logger.Debug("Monthly volume packs are switched off");
                return new List<Book>();
            }

            if (_downloadClientProvider.GetDownloadClient(DownloadProtocol.Torrent) is not ISelectTorrentFiles { CanSelectFiles: true })
            {
                _logger.Debug("Monthly volume packs skipped: no torrent download client that can download selected files");
                return new List<Book>();
            }

            return Eligible(wanted);
        }

        private List<Book> Eligible(IEnumerable<Book> wanted)
        {
            var books = wanted.GroupBy(b => b.Id).Select(g => g.First()).ToList();

            if (books.Count == 0)
            {
                return books;
            }

            var ids = books.Select(b => b.Id).ToList();
            var archives = _editionService.GetEditionsByBook(ids)
                .Where(e => e.MediaType == MediaType.Archive)
                .ToLookup(e => e.BookId);
            var files = _mediaFileService.GetFilesByBooks(ids).ToLookup(f => f.EditionId);
            var queued = _queueService.GetQueue().Where(q => q.Book != null).Select(q => q.Book.Id).ToHashSet();
            var releasedBy = DateTime.UtcNow.AddDays(-DaysAfterRelease);
            var eligible = new List<Book>();

            foreach (var book in books)
            {
                var archive = archives[book.Id].FirstOrDefault();
                var reason = book.Author?.Value?.Library != LibraryType.Manga ? "not a manga volume"
                    : !book.Monitored ? "not monitored"
                    : archive == null || !archive.Monitored ? "archive edition not monitored"
                    : !book.ReleaseDate.HasValue || book.ReleaseDate.Value > releasedBy ? $"released less than {DaysAfterRelease} days ago"
                    : files[archive.Id].Any(f => f.Quality?.Quality != Quality.CbzChapters) ? "already has a file"
                    : queued.Contains(book.Id) ? "already in the queue"
                    : null;

                if (reason != null)
                {
                    _logger.Debug("Monthly packs skip '{0}': {1}", book, reason);
                    continue;
                }

                eligible.Add(book);
            }

            return eligible;
        }

        private List<Book> Process(List<ReleaseInfo> releases, List<Book> eligible, ReleaseSourceType source)
        {
            var packs = new List<Pack>();
            var fetches = 0;

            foreach (var release in Candidates(releases, eligible))
            {
                var list = Cached(release);

                if (list == null)
                {
                    if (fetches >= MaximumFetchesPerRun)
                    {
                        _logger.Debug("Monthly pack '{0}' skipped: already fetched {1} uncached packs this run", release.Title, MaximumFetchesPerRun);
                        continue;
                    }

                    fetches++;
                    list = Fetch(release);

                    if (list == null)
                    {
                        continue;
                    }
                }

                var matches = Match(release, list, eligible);

                if (matches.Count > 0)
                {
                    packs.Add(new Pack(release, list, matches));
                }
            }

            return Grab(Choose(packs), source);
        }

        // The title rule every pack candidate must pass (a scanlation is rejected later, with a logged reason).
        public static bool LooksLikePack(string title)
        {
            return title != null && VolumesUpdateRegex.IsMatch(title) && DigitalRegex.IsMatch(title);
        }

        private List<ReleaseInfo> Candidates(List<ReleaseInfo> releases, List<Book> eligible)
        {
            var oldest = DateTime.UtcNow.AddDays(-MaximumPackAgeDays);
            var candidates = new List<ReleaseInfo>();

            // A failed pack is blocklisted under the series of its grab; any wanted series' entry rules it out.
            var authorIds = eligible.Select(b => b.AuthorId).Distinct().ToList();

            foreach (var release in releases)
            {
                if (!LooksLikePack(release.Title))
                {
                    _logger.Trace("'{0}' is not a monthly volume pack", release.Title);
                    continue;
                }

                var reason = ScanRegex.IsMatch(release.Title) ? "a scanlation"
                    : release.DownloadProtocol != DownloadProtocol.Torrent ? "not a torrent"
                    : IsMyAnonamouse(release) ? "MyAnonamouse is never used for packs"
                    : release.PublishDate < oldest ? $"published more than {MaximumPackAgeDays} days ago"
                    : BelowMinimumSeeders(release)
                    ?? (authorIds.Any(id => _blocklistService.Blocklisted(id, release)) ? "blocklisted" : null);

                if (reason != null)
                {
                    _logger.Debug("Monthly pack '{0}' skipped: {1}", release.Title, reason);
                    continue;
                }

                candidates.Add(release);
            }

            return candidates.OrderByDescending(r => r.PublishDate).ToList();
        }

        // As TorrentSeedingSpecification: the indexer's own minimum, when the release reports its seeders.
        private string BelowMinimumSeeders(ReleaseInfo release)
        {
            var seeders = TorrentInfo.GetSeeders(release);
            var definition = release.IndexerId > 0 ? _indexerFactory.Find(release.IndexerId) : null;
            var minimumSeeders = (definition?.Settings as ITorrentIndexerSettings)?.MinimumSeeders;

            return seeders.HasValue && seeders < minimumSeeders ? $"{seeders} seeders, the indexer's minimum is {minimumSeeders}" : null;
        }

        private bool IsMyAnonamouse(ReleaseInfo release)
        {
            return Contains(release.Indexer) || IsMyAnonamouse(release.IndexerId > 0 ? _indexerFactory.Find(release.IndexerId) : null);
        }

        private static bool IsMyAnonamouse(IndexerDefinition definition)
        {
            return definition != null && (Contains(definition.Name) || Contains((definition.Settings as IIndexerSettings)?.BaseUrl));
        }

        private static bool Contains(string value)
        {
            return value?.Contains("myanonamouse", StringComparison.OrdinalIgnoreCase) == true;
        }

        private PackFileList Cached(ReleaseInfo release)
        {
            // A release that reports no info hash is fetched; its file list is then stored under the hash of its bytes.
            var hash = (release as TorrentInfo)?.InfoHash;

            return hash.IsNotNullOrWhiteSpace() ? _packFileListRepository.FindByHash(hash.ToUpperInvariant()) : null;
        }

        private PackFileList Fetch(ReleaseInfo release)
        {
            try
            {
                var definition = release.IndexerId > 0 ? _indexerFactory.Find(release.IndexerId) : null;
                var indexer = definition != null ? _indexerFactory.GetInstance(definition) : null;
                var bytes = Download(release, indexer, release.DownloadUrl);

                if (bytes == null)
                {
                    return null;
                }

                var hash = _torrentFileInfoReader.GetHashFromTorrentFile(bytes).ToUpperInvariant();
                var existing = _packFileListRepository.FindByHash(hash);

                if (existing != null)
                {
                    _logger.Debug("Monthly pack '{0}' reports info hash '{1}' but its .torrent is '{2}', already read; using the stored file list",
                        release.Title,
                        (release as TorrentInfo)?.InfoHash ?? "none",
                        hash);
                    return existing;
                }

                var (name, files) = _torrentFileInfoReader.GetFilesFromTorrentFile(bytes);

                _logger.Debug("Read monthly pack '{0}': {1} files", release.Title, files.Count);

                return _packFileListRepository.Insert(new PackFileList
                {
                    InfoHash = hash,
                    Title = release.Title,
                    Name = name,
                    Files = files,
                    Torrent = bytes,
                    Fetched = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Monthly pack '{0}' skipped: its .torrent could not be read", release.Title);
                return null;
            }
        }

        // Mirrors TorrentClientBase.DownloadFromWebUrl; a magnet has no file list, so it ends the candidate.
        private byte[] Download(ReleaseInfo release, IIndexer indexer, string url)
        {
            for (var redirects = 0; redirects <= MaximumRedirects; redirects++)
            {
                if (url.IsNullOrWhiteSpace() || url.StartsWith("magnet:"))
                {
                    _logger.Debug("Monthly pack '{0}' skipped: no .torrent file to read the file list from", release.Title);
                    return null;
                }

                var request = indexer?.GetDownloadRequest(url) ?? new HttpRequest(url);
                request.RateLimitKey = release.IndexerId.ToString();
                request.Headers.Accept = "application/x-bittorrent";
                request.AllowAutoRedirect = false;

                var response = _httpClient.GetAsync(request).GetAwaiter().GetResult();

                if (response.StatusCode != HttpStatusCode.MovedPermanently &&
                    response.StatusCode != HttpStatusCode.Found &&
                    response.StatusCode != HttpStatusCode.SeeOther)
                {
                    return response.ResponseData;
                }

                var location = response.Headers.GetSingleValue("Location");

                if (location == null)
                {
                    throw new WebException("Remote website tried to redirect without providing a location.");
                }

                url = location.StartsWith("magnet:") ? location : (request.Url + new HttpUri(location)).ToString();
            }

            _logger.Debug("Monthly pack '{0}' skipped: too many redirects", release.Title);
            return null;
        }

        private List<PackFileMatch> Match(ReleaseInfo release, PackFileList list, List<Book> eligible)
        {
            var matches = new List<PackFileMatch>();
            var maximumSize = _configService.MaximumSize.Megabytes();

            foreach (var group in _matcher.Match(list.Files, eligible).GroupBy(m => m.Book.Id))
            {
                var match = group.First();
                var reason = group.Count() > 1 ? $"{group.Count()} files match it"
                    : GrabbedFrom(match.Book, list.InfoHash) ? "already grabbed from this pack"
                    : maximumSize > 0 && match.File.Length > maximumSize ? $"'{match.File.Path}' is {match.File.Length.SizeSuffix()}, maximum size is {maximumSize.SizeSuffix()}"
                    : null;

                if (reason != null)
                {
                    _logger.Debug("Monthly pack '{0}' not used for '{1}': {2}", release.Title, match.Book, reason);
                    continue;
                }

                matches.Add(match);
            }

            return matches;
        }

        private bool GrabbedFrom(Book book, string hash)
        {
            return _historyService.GetByBook(book.Id, EntityHistoryEventType.Grabbed)
                .Any(h => string.Equals(h.DownloadId, hash, StringComparison.OrdinalIgnoreCase) ||
                          (h.Data.TryGetValue("TorrentInfoHash", out var grabbedHash) && string.Equals(grabbedHash, hash, StringComparison.OrdinalIgnoreCase)));
        }

        // One pack per volume: most seeders, then newest. The same torrent seen on two indexers counts once.
        private List<Pack> Choose(List<Pack> packs)
        {
            var ranked = packs
                .OrderByDescending(p => TorrentInfo.GetSeeders(p.Release) ?? 0)
                .ThenByDescending(p => p.Release.PublishDate)
                .GroupBy(p => p.List.InfoHash, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var chosen = ranked
                .SelectMany(p => p.Matches.Select(m => (Pack: p, Match: m)))
                .GroupBy(x => x.Match.Book.Id)
                .Select(g => g.First())
                .ToList();

            return ranked
                .Select(p => new Pack(p.Release, p.List, chosen.Where(x => x.Pack == p).Select(x => x.Match).ToList()))
                .Where(p => p.Matches.Count > 0)
                .ToList();
        }

        private List<Book> Grab(List<Pack> packs, ReleaseSourceType source)
        {
            var grabbed = new List<Book>();

            foreach (var pack in packs)
            {
                var books = pack.Matches.Select(m => m.Book).ToList();
                var remoteBook = new RemoteBook
                {
                    Release = pack.Release,
                    ParsedBookInfo = new ParsedBookInfo
                    {
                        ReleaseTitle = pack.Release.Title,
                        Quality = QualityParser.ParseQuality(pack.Matches[0].File.Path)
                    },
                    Author = pack.Matches[0].Author,
                    Books = books,
                    MediaType = MediaType.Archive,
                    DownloadAllowed = true,
                    ReleaseSource = source,
                    PackFiles = pack.Matches.ToDictionary(m => m.Book.Id, m => m.File),
                    PackTorrent = pack.List.Torrent,
                    PackPreviouslyGrabbed = _historyService.Find(pack.List.InfoHash, EntityHistoryEventType.Grabbed).Any()
                };

                try
                {
                    _downloadService.DownloadReport(remoteBook, null).GetAwaiter().GetResult();
                    grabbed.AddRange(books);
                    _logger.Info("Grabbed {0} volume(s) from monthly pack '{1}': {2}", books.Count, pack.Release.Title, string.Join(", ", books));
                }
                catch (Exception ex)
                {
                    // e.g. the client DownloadService picks for this indexer cannot download selected files.
                    _logger.Warn(ex, "Couldn't grab monthly pack '{0}': {1}", pack.Release.Title, ex.Message);
                }
            }

            return grabbed;
        }

        private record Pack(ReleaseInfo Release, PackFileList List, List<PackFileMatch> Matches);
    }
}
