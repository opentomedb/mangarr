using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.Gcd;

namespace NzbDrone.Core.MetadataSource.Manga
{
    // Preferred Edition (2026-09-24, spec §1/§2.1): which edition a resolve is for. Language = a series'
    // bound edition or the Add form's choice; TomeLineId = the bound line; Chain = the global setting,
    // for a series that has none yet. BookInfoProxy builds it; null means English (today's call).
    public class EditionRequest
    {
        public string Language { get; set; }
        public string TomeLineId { get; set; }
        public IReadOnlyList<string> Chain { get; set; }

        // Preferred Edition (2026-09-24, ruling S1): the entry's stored name. Pins (overrides.json) are
        // keyed by it, so a re-resolve that kept the name keeps its pins. Null on an add or a search.
        public string StoredName { get; set; }
    }

    public class EditionResolution
    {
        public GcdSeries Line { get; set; }
        public string Language { get; set; }
        public bool FromBinding { get; set; }
    }

    public interface IEditionResolver
    {
        // Null = no line in any requested language (the caller stays English, or refuses the chosen one).
        // "en" returns the anchor itself. A bound line that vanished with no same-edition successor throws
        // EditionUnavailableException (final fix round I3, 2026-09-24).
        EditionResolution Resolve(GcdSeries anchor, EditionRequest request, LibraryType library, string name);

        // Every language the line's work has a line of >= 1 volume in (same medium family), one best
        // line each: English first, then by code.
        List<EditionOption> Options(GcdSeries anyLine, LibraryType library);

        // Collected edition by the flag, composition or page counts (MangaSeriesMetadataProvider's tell).
        bool IsCollected(GcdSeries line);
    }

    public class EditionResolver : IEditionResolver
    {
        private readonly IGcdMetadataService _gcdMetadataService;
        private readonly Logger _logger;

        public EditionResolver(IGcdMetadataService gcdMetadataService, Logger logger)
        {
            _gcdMetadataService = gcdMetadataService;
            _logger = logger;
        }

        public EditionResolution Resolve(GcdSeries anchor, EditionRequest request, LibraryType library, string name)
        {
            if (request == null)
            {
                return null;
            }

            // Bound (spec §2.1 step 4): the stored line, fetched by id -- never re-ranked by name, so a
            // global change or a new line in the market never renumbers a series. A retired id follows
            // id_redirect (entity release_line) inside FindSeriesByTomeId.
            var vanished = false;

            if (request.TomeLineId.IsNotNullOrWhiteSpace())
            {
                var bound = _gcdMetadataService.FindSeriesByTomeId(request.TomeLineId);
                vanished = bound == null && request.Language.IsNotNullOrWhiteSpace();

                if (bound != null && (request.Language.IsNullOrWhiteSpace() || bound.Language?.Trim() == request.Language.Trim()))
                {
                    return new EditionResolution { Line = bound, Language = bound.Language, FromBinding = true };
                }

                // Two different failures, named apart so the log says which one happened.
                if (bound == null)
                {
                    _logger.Warn("Bound edition line {0} for \"{1}\" is not in the catalogue any more; resolving the {2} edition again",
                        request.TomeLineId,
                        name,
                        EditionLanguages.Name(request.Language));
                }
                else
                {
                    _logger.Warn("Bound edition line {0} for \"{1}\" is bound to a different language ({2}); resolving the {3} edition again",
                        request.TomeLineId,
                        name,
                        EditionLanguages.Name(bound.Language),
                        EditionLanguages.Name(request.Language));
                }
            }

            // Preferred Edition (2026-09-24, final fix round I3): a bound line that vanished (no redirect) is
            // replaced silently only by the line that is plainly the same edition -- a same-language
            // counterpart of the anchor that is not a collected edition, so volume N stays volume N. Anything
            // else (only an omnibus left, a line of another origin, no anchor to compare with) would renumber
            // the series behind the user's back, bypassing the Change Edition check (D9): the entry is left as
            // stored instead. A NEW add still resolves a collected-only market to its omnibus (below).
            if (vanished)
            {
                var language = request.Language.Trim();
                var successor = anchor == null || anchor.TomeWorkId.IsNullOrWhiteSpace()
                    ? null
                    : PickSibling(anchor,
                                  (_gcdMetadataService.GetWorkLines(anchor.TomeWorkId) ?? new List<GcdSeries>())
                                      .Where(c => c.Language == language && IsCounterpart(anchor, c) && !IsCollected(c)),
                                  language);

                if (successor == null)
                {
                    throw new EditionUnavailableException(name, language, request.TomeLineId);
                }

                return new EditionResolution { Line = successor, Language = language };
            }

            // A bound or chosen language is the ONLY language tried: a series is never moved to another
            // market's numbering by a resolve (spec §2.3: fallback is per series, chosen once).
            var languages = request.Language.IsNotNullOrWhiteSpace()
                ? new List<string> { request.Language.Trim() }
                : (request.Chain ?? new List<string>()).ToList();

            // Step 2: the work has no English line -- rank the title in each of the chain's other languages
            // in order, as the sibling hop does, so a zero-volume stub in an earlier language falls through
            // to the next language instead of ending the resolve (Preferred Edition (2026-09-24), fix round 1).
            if (anchor == null)
            {
                foreach (var language in languages.Where(l => !EditionLanguages.IsEnglish(l)))
                {
                    var line = _gcdMetadataService.FindSeriesByTitle(name, library, new List<string> { language });

                    if (line != null && line.VolumeCount >= 1)
                    {
                        return new EditionResolution { Line = line, Language = line.Language };
                    }
                }

                return null;
            }

            // An artifact without tome_work_id (older OpenTome, or none) has no siblings to hop to.
            var work = anchor.TomeWorkId.IsNullOrWhiteSpace()
                ? new List<GcdSeries>()
                : _gcdMetadataService.GetWorkLines(anchor.TomeWorkId) ?? new List<GcdSeries>();

            foreach (var language in languages)
            {
                if (EditionLanguages.IsEnglish(language))
                {
                    return new EditionResolution { Line = anchor, Language = EditionLanguages.English };
                }

                var pick = PickSibling(anchor, work, language);

                if (pick != null)
                {
                    return new EditionResolution { Line = pick, Language = language };
                }
            }

            return null;
        }

        public List<EditionOption> Options(GcdSeries anyLine, LibraryType library)
        {
            if (anyLine == null || anyLine.TomeWorkId.IsNullOrWhiteSpace())
            {
                return new List<EditionOption>();
            }

            var work = _gcdMetadataService.GetWorkLines(anyLine.TomeWorkId) ?? new List<GcdSeries>();
            var novel = GcdMetadataService.IsLightNovel(anyLine.Medium);
            var family = work.Where(c => GcdMetadataService.IsLightNovel(c.Medium) == novel && c.VolumeCount >= 1 && c.Language != null).ToList();

            // The counterpart ranking is relative to the English line when the work has one.
            var anchor = family.Where(c => EditionLanguages.IsEnglish(c.Language))
                             .OrderByDescending(c => c.GcdSeriesId == anyLine.GcdSeriesId)
                             .ThenByDescending(c => c.IsMain)
                             .FirstOrDefault() ?? anyLine;

            var options = new List<EditionOption>();

            foreach (var language in family.Select(c => c.Language).Distinct().OrderBy(l => EditionLanguages.IsEnglish(l) ? string.Empty : l))
            {
                var line = language == anchor.Language ? anchor : PickSibling(anchor, work, language);

                if (line != null)
                {
                    options.Add(new EditionOption
                    {
                        Language = language,
                        Name = EditionLanguages.Name(language),
                        VolumeCount = line.VolumeCount,
                        LineName = line.LocalName.IsNotNullOrWhiteSpace() ? line.LocalName : line.Name
                    });
                }
            }

            return options;
        }

        public bool IsCollected(GcdSeries line)
        {
            return MangaSeriesMetadataProvider.IsCollectedEdition(line.IsOmnibus, _gcdMetadataService.GetVolumes(line.GcdSeriesId) ?? new List<GcdVolume>());
        }

        // Spec §2.1 step 3, in order: the counterpart (the licensed line pointing at the same origin as
        // the anchor, or the origin itself when L is the origin language), is_main equal to the anchor's,
        // not a collected edition (IsCollectedEdition -- not bare is_omnibus, which misses "Book Edition"),
        // dated_count, volume_count, id. A line with no volumes never wins. A collected edition ranks
        // last but is not excluded: a market whose only line is an omnibus still resolves to it.
        private GcdSeries PickSibling(GcdSeries anchor, IEnumerable<GcdSeries> work, string language)
        {
            var novel = GcdMetadataService.IsLightNovel(anchor.Medium);

            return work
                .Where(c => c.Language == language && c.GcdSeriesId != anchor.GcdSeriesId)
                .Where(c => GcdMetadataService.IsLightNovel(c.Medium) == novel)
                .Where(c => c.VolumeCount >= 1)
                .OrderByDescending(c => IsCounterpart(anchor, c))
                .ThenByDescending(c => c.IsMain == anchor.IsMain)
                .ThenBy(c => IsCollected(c))
                .ThenByDescending(c => c.DatedCount ?? 0)
                .ThenByDescending(c => c.VolumeCount)
                .ThenBy(c => c.GcdSeriesId)
                .FirstOrDefault();
        }

        internal static bool IsCounterpart(GcdSeries anchor, GcdSeries candidate)
        {
            return anchor.OrigSeriesId.HasValue &&
                   (candidate.OrigSeriesId == anchor.OrigSeriesId || candidate.GcdSeriesId == anchor.OrigSeriesId.Value);
        }
    }
}
