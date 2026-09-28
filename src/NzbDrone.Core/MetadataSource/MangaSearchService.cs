using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MetadataSource
{
    // Phase 3b: search-driven volume discovery. Queries the configured indexers for a
    // manga series title and derives the highest volume number that exists from the
    // parsed release titles (singles and "Volumes 1-13" packs). This is what makes the
    // volume universe come from real search results instead of a hardcoded count.
    //
    // DI-safe: the IndexerSearch/Indexers layer has no dependency on the metadata layer,
    // so injecting IIndexerFactory into the metadata proxy introduces no cycle.
    public interface IMangaSearchService
    {
        // Returns the highest volume number found across indexer results for the series
        // title, or 0 if nothing usable was found (caller then falls back to a default/cap).
        int FindHighestVolume(string seriesTitle);
    }

    public class MangaSearchService : IMangaSearchService
    {
        // Hard ceiling on how long volume discovery may block. Reached from disk rescans that hold
        // the exclusive disk-access lock, so this bounds how long one metadata lookup can freeze the
        // whole command queue. Overridable so tests don't wait real seconds.
        internal static TimeSpan DiscoveryTimeout { get; set; } = TimeSpan.FromSeconds(20);

        private readonly IIndexerFactory _indexerFactory;
        private readonly Logger _logger;

        public MangaSearchService(IIndexerFactory indexerFactory, Logger logger)
        {
            _indexerFactory = indexerFactory;
            _logger = logger;
        }

        public int FindHighestVolume(string seriesTitle)
        {
            if (seriesTitle.IsNullOrWhiteSpace())
            {
                return 0;
            }

            var indexers = _indexerFactory.AutomaticSearchEnabled();
            if (indexers.Count == 0)
            {
                _logger.Debug("Manga discovery: no search-enabled indexers for '{0}'", seriesTitle);
                return 0;
            }

            // Reuse Servarr's AuthorSearchCriteria — its query is built from Author.Name,
            // which for manga is the series title. A bare metadata Author carries the name.
            var criteria = new AuthorSearchCriteria
            {
                Author = new Author { Metadata = new AuthorMetadata { Name = seriesTitle } }
            };

            // Query every indexer CONCURRENTLY under a hard overall deadline. This method can be
            // reached from a disk rescan (new-file identification), and a rescan holds the exclusive
            // disk-access command lock — so a single slow/unresponsive indexer here would serially
            // stall the whole command queue (imports, refreshes) until a restart. Parallel + a bounded
            // wait caps the cost at one slow indexer's window, and a straggler that misses the
            // deadline is simply dropped (volume count then comes from the other metadata sources and
            // self-corrects on the next refresh) rather than blocking.
            var tasks = indexers
                .Where(i => i.SupportsSearch)
                .Select(indexer => Task.Run(() =>
                {
                    try
                    {
                        return HighestVolumeIn(indexer.Fetch(criteria).GetAwaiter().GetResult());
                    }
                    catch (Exception ex)
                    {
                        // One bad indexer must not break discovery — mirror ReleaseSearchService's
                        // per-indexer swallow.
                        _logger.Warn(ex, "Manga discovery: indexer '{0}' failed for '{1}'", indexer.Definition?.Name, seriesTitle);
                        return 0;
                    }
                }))
                .ToList();

            try
            {
                Task.WaitAll(tasks.ToArray(), DiscoveryTimeout);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Manga discovery for '{0}' hit an error waiting on indexers", seriesTitle);
            }

            var completed = tasks.Where(t => t.Status == TaskStatus.RanToCompletion).ToList();
            var highest = completed.Any() ? completed.Max(t => t.Result) : 0;

            if (completed.Count < tasks.Count)
            {
                _logger.Debug("Manga discovery for '{0}': {1}/{2} indexers answered within {3}s",
                    seriesTitle, completed.Count, tasks.Count, DiscoveryTimeout.TotalSeconds);
            }

            _logger.Debug("Manga discovery for '{0}': highest volume found = {1}", seriesTitle, highest);
            return highest;
        }

        private static int HighestVolumeIn(IEnumerable<ReleaseInfo> releases)
        {
            var highest = 0;

            foreach (var release in releases)
            {
                var parsed = new ParsedBookInfo();
                MangaVolumeParser.ParseVolume(release.Title, parsed);

                // A pack ("Volumes 1-13") reaches further than its end; a single is its number.
                // Volume count is whole, so a fractional side-story volume (3.5) truncates to 3.
                if (parsed.VolumeEnd.HasValue)
                {
                    highest = Math.Max(highest, (int)parsed.VolumeEnd.Value);
                }
                else if (parsed.VolumeNumber.HasValue)
                {
                    highest = Math.Max(highest, (int)parsed.VolumeNumber.Value);
                }
            }

            return highest;
        }
    }
}
