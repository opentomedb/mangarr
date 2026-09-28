using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    public interface IIdentificationService
    {
        List<LocalEdition> Identify(List<LocalBook> localTracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config);
    }

    public class IdentificationService : IIdentificationService
    {
        private readonly ITrackGroupingService _trackGroupingService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IAugmentingService _augmentingService;
        private readonly ICandidateService _candidateService;
        private readonly Logger _logger;

        public IdentificationService(ITrackGroupingService trackGroupingService,
                                     IMetadataTagService metadataTagService,
                                     IAugmentingService augmentingService,
                                     ICandidateService candidateService,
                                     Logger logger)
        {
            _trackGroupingService = trackGroupingService;
            _metadataTagService = metadataTagService;
            _augmentingService = augmentingService;
            _candidateService = candidateService;
            _logger = logger;
        }

        public List<LocalEdition> GetLocalBookReleases(List<LocalBook> localTracks, bool singleRelease, LibraryType? library = null)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            List<LocalEdition> releases;
            if (singleRelease)
            {
                releases = SplitBySingleReleaseClass(localTracks, library);
            }
            else
            {
                releases = _trackGroupingService.GroupTracks(localTracks);
            }

            _logger.Debug($"Sorted {localTracks.Count} tracks into {releases.Count} releases in {watch.ElapsedMilliseconds}ms");

            foreach (var localRelease in releases)
            {
                try
                {
                    _augmentingService.Augment(localRelease);
                }
                catch (AugmentingFailedException)
                {
                    _logger.Warn($"Augmentation failed for {localRelease}");
                }
            }

            return releases;
        }

        // SingleRelease callers trust the caller's grouping -- a group is meant to be one
        // edition's files (e.g. ManualImportService.UpdateItems groups selected rows by
        // (BookId, EditionId) before re-identifying each group). If the caller's grouping left
        // rows of different classes in the same group -- e.g. a bulk Book reassignment that never
        // set each row's own Edition, so a light novel's EPUB row and its M4B/MP3 rows share a
        // (BookId, null) key -- GetBestRelease would resolve the whole group at once from the
        // FIRST file's class, mis-attaching or refusing the rest (mixed-folder-rescan, 2026-09-24).
        // Split defensively by class here too. A single-class list returns the original List
        // instance unchanged, so a manga group (always one class) and a pure-audio group (multi-part
        // audio is still one class) are byte-identical to before.
        private static List<LocalEdition> SplitBySingleReleaseClass(List<LocalBook> localTracks, LibraryType? library)
        {
            var byClass = localTracks.GroupBy(t => MediaTypes.OfFile(t.Path, library)).ToList();

            if (byClass.Count <= 1)
            {
                return new List<LocalEdition> { new LocalEdition(localTracks) };
            }

            return byClass.Select(g => new LocalEdition(g.ToList())).ToList();
        }

        public List<LocalEdition> Identify(List<LocalBook> localTracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config)
        {
            // 1 group localTracks so that we think they represent a single release
            // 2 get candidates given specified author, book and release.  Candidates can include extra files already on disk.
            // 3 find best candidate
            var watch = System.Diagnostics.Stopwatch.StartNew();

            _logger.Debug("Starting book identification");

            var library = idOverrides?.Author?.Library ?? LibraryTypes.Of(idOverrides?.Book);
            var releases = GetLocalBookReleases(localTracks, config.SingleRelease, library);

            var i = 0;
            foreach (var localRelease in releases)
            {
                i++;
                _logger.ProgressInfo("Identifying volume {0}/{1}", i, releases.Count);
                _logger.Debug($"Identifying book files:\n{localRelease.LocalBooks.Select(x => x.Path).ConcatToString("\n")}");

                try
                {
                    IdentifyRelease(localRelease, idOverrides, config);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Error identifying release");
                }
            }

            watch.Stop();

            _logger.Debug($"Track identification for {localTracks.Count} tracks took {watch.ElapsedMilliseconds}ms");

            return releases;
        }

        private List<LocalBook> ToLocalTrack(IEnumerable<BookFile> trackfiles, LocalEdition localRelease)
        {
            var scanned = trackfiles.Join(localRelease.LocalBooks, t => t.Path, l => l.Path, (track, localTrack) => localTrack);
            var toScan = trackfiles.ExceptBy(t => t.Path, scanned, s => s.Path, StringComparer.InvariantCulture);
            var localTracks = scanned.Concat(toScan.Select(x => new LocalBook
            {
                Path = x.Path,
                Size = x.Size,
                Modified = x.Modified,
                FileTrackInfo = _metadataTagService.ReadTags((FileInfoBase)new FileInfo(x.Path)),
                ExistingFile = true,
                AdditionalFile = true,
                Quality = x.Quality
            }))
            .ToList();

            localTracks.ForEach(x => _augmentingService.Augment(x, true));

            return localTracks;
        }

        private void IdentifyRelease(LocalEdition localBookRelease, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var usedRemote = false;

            // A forced edition (from a tracked single-book grab) is authoritative — we already know
            // the book, so don't let the text-distance heuristics second-guess it.
            localBookRelease.EditionForced = idOverrides?.Edition != null;

            IEnumerable<CandidateEdition> candidateReleases = _candidateService.GetDbCandidatesFromTags(localBookRelease, idOverrides, config.IncludeExisting);

            // convert all the TrackFiles that represent extra files to List<LocalTrack>
            // local candidates are actually a list so this is fine to enumerate
            var allLocalTracks = ToLocalTrack(candidateReleases
                .SelectMany(x => x.ExistingFiles)
                .DistinctBy(x => x.Path), localBookRelease);

            _logger.Debug($"Retrieved {allLocalTracks.Count} possible tracks in {watch.ElapsedMilliseconds}ms");

            if (!candidateReleases.Any())
            {
                _logger.Debug("No local candidates found, trying remote");
                candidateReleases = _candidateService.GetRemoteCandidates(localBookRelease, idOverrides);
                if (!config.AddNewAuthors)
                {
                    candidateReleases = candidateReleases.Where(x => x.Edition.Book.Value.Id > 0 && x.Edition.Book.Value.AuthorId > 0);
                }

                usedRemote = true;
            }

            GetBestRelease(localBookRelease, candidateReleases, allLocalTracks, out var seenCandidate);

            if (!seenCandidate)
            {
                // can't find any candidates even after using remote search
                // populate the overrides and return
                foreach (var localTrack in localBookRelease.LocalBooks)
                {
                    localTrack.Edition = idOverrides.Edition;
                    localTrack.Book = idOverrides.Book;
                    localTrack.Author = idOverrides.Author;
                }

                return;
            }

            // Author-scoped adoption fallback: on a per-author rescan (idOverrides.Author set) where the
            // text-distance match is poor — typically because the series was renamed in metadata so the
            // filenames no longer match the title — attach the file to the scoped author's book by EXACT
            // volume number. The file is physically in the author's own folder, so folder ownership is
            // authoritative. Gated on idOverrides.Author + a failing (>0.15) match, so it never fires on
            // the general/download path or on series whose files already match well.
            if (idOverrides?.Author != null && localBookRelease.Distance.NormalizedDistance() > 0.15)
            {
                var scopedVolume = ParseScopedVolume(localBookRelease, idOverrides?.Author?.Metadata?.Value);
                if (scopedVolume.HasValue)
                {
                    // Light novels (2026-09): the volume's edition of the FILE's class, never the first
                    // edition of the volume (a light-novel volume has two). LN PDF (2026-09-22): the
                    // class is read against the scoped author's library.
                    var scopedType = MediaTypes.OfFile(localBookRelease.LocalBooks.First().Path, idOverrides.Author.Library);
                    var volumeMatch = candidateReleases.FirstOrDefault(c => Math.Abs(c.Edition.Book.Value.VolumeNumber - scopedVolume.Value) < 0.001 && c.Edition.MediaType == scopedType);
                    if (volumeMatch != null)
                    {
                        _logger.Debug("Author-scoped volume match: attaching '{0}' to {1} (vol {2}) by folder ownership",
                            localBookRelease.LocalBooks.FirstOrDefault()?.Path,
                            volumeMatch.Edition,
                            scopedVolume.Value);
                        localBookRelease.Edition = volumeMatch.Edition;
                        localBookRelease.Distance = new Distance();

                        // The volume's own file(s) are already in LocalBooks; any ExistingTracks carried
                        // over from the earlier best-distance candidate belong to a DIFFERENT edition and
                        // would be re-homed onto this volume by PopulateMatch. Clear them.
                        localBookRelease.ExistingTracks = new List<LocalBook>();
                    }
                    else
                    {
                        _logger.Debug("Author-scoped fallback: no candidate book with volume {0} for '{1}'",
                            scopedVolume.Value,
                            localBookRelease.LocalBooks.FirstOrDefault()?.Path);
                    }
                }
                else
                {
                    _logger.Debug("Author-scoped fallback: could not parse a volume number from '{0}'",
                        localBookRelease.LocalBooks.FirstOrDefault()?.Path);
                }
            }

            // If the result isn't great and we haven't tried remote candidates, try looking for remote candidates
            // Goodreads may have a better edition of a local book.
            // Skip when the edition was forced — a tracked grab's book is authoritative and a remote
            // retry could replace it with a worse Goodreads match.
            if (!localBookRelease.EditionForced && localBookRelease.Distance.NormalizedDistance() > 0.15 && !usedRemote)
            {
                _logger.Debug("Match not good enough, trying remote candidates");
                candidateReleases = _candidateService.GetRemoteCandidates(localBookRelease, idOverrides);

                if (!config.AddNewAuthors)
                {
                    candidateReleases = candidateReleases.Where(x => x.Edition.Book.Value.Id > 0);
                }

                GetBestRelease(localBookRelease, candidateReleases, allLocalTracks, out _);
            }

            _logger.Debug($"Best release found in {watch.ElapsedMilliseconds}ms");

            localBookRelease.PopulateMatch(config.KeepAllEditions);

            _logger.Debug($"IdentifyRelease done in {watch.ElapsedMilliseconds}ms");
        }

        // Preferred Edition (2026-09-24): the scoped author's edition language, so its own "Tome 5" files
        // parse -- and, with the series-name matcher (M9 pre-review fix), a user's own "… T05.cbz" too; an
        // English series has no language and no matcher = today's parse.
        internal static double? ParseScopedVolume(LocalEdition localBookRelease, AuthorMetadata scopedMeta)
        {
            var editionLanguage = scopedMeta?.EditionLanguage;
            var isAcceptedSeries = EditionVolumeTokens.SeriesMatcher(scopedMeta);

            foreach (var localBook in localBookRelease.LocalBooks)
            {
                if (localBook.Path.IsNotNullOrWhiteSpace() &&
                    MangaVolumeParser.TryParseSeriesVolume(Path.GetFileNameWithoutExtension(localBook.Path), out _, out var volume, editionLanguage, isAcceptedSeries) &&
                    volume > 0)
                {
                    return volume;
                }
            }

            return null;
        }

        private void GetBestRelease(LocalEdition localBookRelease, IEnumerable<CandidateEdition> candidateReleases, List<LocalBook> extraTracksOnDisk, out bool seenCandidate)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            _logger.Debug("Matching {0} track files against candidates", localBookRelease.TrackCount);
            _logger.Trace("Processing files:\n{0}", string.Join("\n", localBookRelease.LocalBooks.Select(x => x.Path)));

            var bestDistance = localBookRelease.Edition != null ? localBookRelease.Distance.NormalizedDistance() : 1.0;
            seenCandidate = false;

            // Light novels (2026-09): a release only ever targets the edition of its own class --
            // an EPUB the Ebook edition, an audio set the Audio edition. Candidates of another
            // class are skipped outright: the distance tilt on Edition.Format is a bias, not a
            // guarantee. Manga: archive files against Archive editions, nothing is skipped.
            // LN PDF (2026-09-22): a .pdf is the ebook class in a light-novel entry and the archive
            // class in a manga one, so the file's class is read against each candidate's own library.
            // Every other extension types the same for every candidate, so the library lookup --
            // walking Book -> AuthorMetadata, a lazy load per candidate -- only runs for a .pdf
            // (fix round 1, 2026-09-22).
            var path = localBookRelease.LocalBooks.First().Path;
            var pathIsPdf = string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

            foreach (var candidateRelease in candidateReleases)
            {
                var release = candidateRelease.Edition;
                var fileType = MediaTypes.OfFile(path, pathIsPdf ? LibraryTypes.Of(release?.Book?.Value) : null);

                if (release.MediaType != fileType)
                {
                    _logger.Trace("Skipping {0}: {1} edition for a {2} file", release, release.MediaType, fileType);
                    continue;
                }

                seenCandidate = true;

                _logger.Debug($"Trying Release {release}");
                var rwatch = System.Diagnostics.Stopwatch.StartNew();

                var extraTrackPaths = candidateRelease.ExistingFiles.Select(x => x.Path).ToList();
                var extraTracks = extraTracksOnDisk.Where(x => extraTrackPaths.Contains(x.Path)).ToList();
                var allLocalTracks = localBookRelease.LocalBooks.Concat(extraTracks).DistinctBy(x => x.Path).ToList();

                var distance = DistanceCalculator.BookDistance(allLocalTracks, release);
                var currDistance = distance.NormalizedDistance();

                rwatch.Stop();
                _logger.Debug("Release {0} has distance {1} vs best distance {2} [{3}ms]",
                              release,
                              currDistance,
                              bestDistance,
                              rwatch.ElapsedMilliseconds);
                if (currDistance < bestDistance)
                {
                    bestDistance = currDistance;
                    localBookRelease.Distance = distance;
                    localBookRelease.Edition = release;
                    localBookRelease.ExistingTracks = extraTracks;
                    if (currDistance == 0.0)
                    {
                        break;
                    }
                }
            }

            watch.Stop();
            _logger.Debug($"Best release: {localBookRelease.Edition} Distance {localBookRelease.Distance.NormalizedDistance()} found in {watch.ElapsedMilliseconds}ms");
        }
    }
}
