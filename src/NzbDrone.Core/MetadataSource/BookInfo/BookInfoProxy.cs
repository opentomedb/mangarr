using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using LazyCache;
using LazyCache.Providers;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Http;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Parser;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    public class BookInfoProxy : IProvideAuthorInfo, IProvideBookInfo, ISearchForNewBook, ISearchForNewAuthor, ISearchForNewEntity
    {
        private static readonly JsonSerializerOptions SerializerSettings = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            Converters = { new STJUtcConverter() }
        };

        private readonly IHttpClient _httpClient;
        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly IGoodreadsSearchProxy _goodreadsSearchProxy;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IMangaSeriesMetadataProvider _mangaMetadataProvider;
        private readonly IWriterResolver _writerResolver;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;
        private readonly IMetadataRequestBuilder _requestBuilder;
        private readonly IConfigService _configService;
        private readonly ICached<HashSet<string>> _cache;
        private readonly CachingService _authorCache;

        public BookInfoProxy(IHttpClient httpClient,
                             ICachedHttpResponseService cachedHttpClient,
                             IGoodreadsSearchProxy goodreadsSearchProxy,
                             IAuthorService authorService,
                             IBookService bookService,
                             IEditionService editionService,
                             IMangaSeriesMetadataProvider mangaMetadataProvider,
                             IWriterResolver writerResolver,
                             IDiskProvider diskProvider,
                             IMetadataRequestBuilder requestBuilder,
                             Logger logger,
                             ICacheManager cacheManager,
                             IConfigService configService)
        {
            _httpClient = httpClient;
            _cachedHttpClient = cachedHttpClient;
            _goodreadsSearchProxy = goodreadsSearchProxy;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _mangaMetadataProvider = mangaMetadataProvider;
            _writerResolver = writerResolver;
            _diskProvider = diskProvider;
            _requestBuilder = requestBuilder;
            _configService = configService;
            _cache = cacheManager.GetCache<HashSet<string>>(GetType());
            _logger = logger;

            _authorCache = new CachingService(new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 })));
            _authorCache.DefaultCachePolicy = new CacheDefaults
            {
                DefaultCacheDurationSeconds = 60
            };
        }

        // ===== SPIKE(search-driven): local stand-in for the bookinfo metadata server =====
        // Proves Mangarr discovery can be fed from a local list (a stand-in for indexer
        // search results) instead of the external metadata server. One "author" (= manga
        // series) with N "books" (= volumes), each with exactly one monitored edition.
        // Throwaway: reverted before any real implementation.
        // static readonly (not const) so the original method bodies below stay "reachable"
        // to the compiler — no CS0162 unreachable-code warnings.
        private static readonly bool SpikeEnabled = true;

        // The id this app gives a series with that title in that library -- what BuildFakeAuthor
        // derives, exposed so a collection member named in the artifact can be matched to (or added
        // as) a library series. Manga: "local-<slug>" (unchanged); light novel: "local-<slug>~ln".
        public static string ForeignAuthorIdFor(string title, LibraryType library = LibraryType.Manga)
        {
            return LibraryTypes.WithType("local-" + Slug(SeriesIdentity(title)), library);
        }

        // The display fallback's AniList fetch did not answer and this pass found no overview: the
        // stored non-empty one stays (null = take the provider's).
        private static string KeptOnDisplayMiss(MangaSeriesMetadata series, string storedOverview)
        {
            return series.DisplayFetchFailed && series.Overview.IsNullOrWhiteSpace() && storedOverview.IsNotNullOrWhiteSpace()
                ? storedOverview
                : null;
        }

        private static string Slug(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in (s ?? string.Empty).ToLowerInvariant())
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            }

            var slug = sb.ToString().Trim('-');
            while (slug.Contains("--"))
            {
                slug = slug.Replace("--", "-");
            }

            return slug.IsNullOrWhiteSpace() ? "series" : slug;
        }

        // A series' identity must stay stable across search-term variations. Manga searches often
        // include a volume token ("Mushoku Tensei Vol 1"); strip it so every per-volume search of a
        // series collapses to ONE id instead of creating a separate "...-vol-N" author per volume
        // (which fragments one series into a bogus author for each volume).
        private static string SeriesIdentity(string title)
        {
            return MangaVolumeParser.TryParseSeriesVolume(title, out var series, out _) && series.IsNotNullOrWhiteSpace()
                ? series
                : title;
        }

        // Preferred Edition (2026-09-24, M9 pre-review fix): under a non-English chain a search term may carry
        // that edition's token ("L'Attaque des Titans Tome 5", a French book tag), so the slug and the query
        // de-slugged from it drop it too. Today's "Vol." reading goes first; only a term it leaves whole tries
        // each chain language's tokens (EditionVolumeTokens -- a token needs its number, so a name like "Tome of
        // the Dead" stays). An English-only chain is exactly the one-argument identity.
        internal static string SeriesIdentity(string title, IReadOnlyList<string> chain)
        {
            if (EditionLanguages.IsEnglishOnly(chain) ||
                (MangaVolumeParser.TryParseSeriesVolume(title, out var series, out _) && series.IsNotNullOrWhiteSpace()))
            {
                return SeriesIdentity(title);
            }

            foreach (var language in chain.Where(l => !EditionLanguages.IsEnglish(l)))
            {
                if (MangaVolumeParser.TryParseSeriesVolume(title, out series, out _, language) && series.IsNotNullOrWhiteSpace())
                {
                    return series;
                }
            }

            return title;
        }

        private static string Clean(string s)
        {
            var cleaned = new string((s ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            return cleaned.IsNullOrWhiteSpace() ? "series" : cleaned;
        }

        // The de-slugged id as a title ("local-kaiju-no-8" -> "Kaiju No 8"): the initial add's
        // search term, and the entry's own first alias for the AniList retry (D3). Public for the
        // rebind pass, which has only the stored id and name.
        public static string DisplayName(string foreignAuthorId)
        {
            // The suffix is not part of the name ("... Ln" would otherwise be title-cased into it).
            var slug = LibraryTypes.BaseId(foreignAuthorId);
            slug = slug.StartsWith("local-") ? slug.Substring("local-".Length) : slug;
            slug = slug.Replace('-', ' ').Trim();
            if (slug.IsNullOrWhiteSpace())
            {
                slug = "series";
            }

            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug);
        }

        // A stored name is curated (the maintainer's, OpenTome's, or the add's) and NEVER changes on refresh
        // or rebind (2026-09-15): a refresh updates the binding, poster, overview, aliases, status
        // and volumes, not the name — so the clean name, the book titles and the folder stay put
        // when an entry is re-bound to an AniList entry titled differently (Mushoku Tensei ->
        // "Mushoku Tensei: Jobless Reincarnation"). The resolver's title names an entry only when
        // it is being created. Pure + static so it can be unit-tested directly.
        public static string ResolveDisplayName(string resolved, string existingName)
        {
            return existingName.IsNotNullOrWhiteSpace() ? existingName : resolved;
        }

        // Preferred Edition (2026-09-24, spec §1/§4): which edition this resolve is for. An existing series
        // keeps its own (null = English, whatever the chain says now -- a chain change only affects new
        // adds); an add names one or takes the chain; a search candidate takes the chain. Null = the
        // English (six-argument) call -- the ONLY gate keeping EditionResolver (and its new catalogue
        // reads) off every English series, including one that already records a TomeLineId (A3).
        private EditionRequest EditionRequestFor(Author existing, string requestedEdition)
        {
            if (existing != null)
            {
                var bound = existing.Metadata?.Value?.EditionLanguage;

                // Ruling S1: the stored name rides along -- the provider keys pins by it.
                return EditionLanguages.IsEnglish(bound)
                    ? null
                    : new EditionRequest { Language = bound.Trim(), TomeLineId = existing.Metadata.Value.TomeLineId, StoredName = existing.Name };
            }

            if (requestedEdition.IsNotNullOrWhiteSpace())
            {
                return EditionLanguages.IsEnglish(requestedEdition) ? null : new EditionRequest { Language = requestedEdition.Trim() };
            }

            var chain = EditionLanguages.ParseChain(_configService.PreferredEditionLanguages);

            return EditionLanguages.IsEnglishOnly(chain) ? null : new EditionRequest { Chain = chain };
        }

        // Preferred Edition (2026-09-24, M5 pre-review fix): search and identification are read-only, so a
        // bound edition line the catalogue lost answers them from the English anchor -- the call an English
        // series makes (anchorOnly: no EditionRequest) -- with one Warn; the stored entry is left alone
        // (its refresh is where the loss is handled). Never an exception out of the search box or import.
        private Author BuildFakeAuthorForTitle(string title, LibraryType library)
        {
            try
            {
                return BuildFakeAuthor(title, resolveVolumeDetails: false, library);
            }
            catch (EditionUnavailableException e)
            {
                _logger.Warn("\"{0}\" is bound to {1} line {2}, which is not in the catalogue any more; answering from the English anchor",
                    e.Name,
                    EditionLanguages.Name(e.Language),
                    e.TomeLineId ?? "(none)");

                return BuildFakeAuthor(title, resolveVolumeDetails: false, library, anchorOnly: true);
            }
        }

        // Preferred Edition (2026-09-24): a search candidate lists the languages its work has a line in (the
        // Add form's Edition picker). The two search entry points only -- identification (SearchForNewBook),
        // an add and a refresh never need it -- and only for a candidate bound to a catalogue line: an
        // unbound one has no work to ask about, so it costs no catalogue read (an English-chain search of
        // a bound candidate adds two local artifact reads: its line by tome id, then the work's lines).
        private Author WithEditionOptions(Author author, LibraryType library)
        {
            var meta = author?.Metadata?.Value;

            if (meta != null && meta.TomeLineId.IsNotNullOrWhiteSpace())
            {
                meta.EditionOptions = _mangaMetadataProvider.EditionOptions(meta.TomeLineId, library) ?? new List<EditionOption>();
            }

            return author;
        }

        private Author BuildFakeAuthor(string idOrTitle, bool resolveVolumeDetails = true, LibraryType library = LibraryType.Manga, string requestedEdition = null, bool anchorOnly = false)
        {
            // The library rides in the id when we were handed one (add / refresh / import) and comes
            // from the caller when we were handed a title (the Add page's library toggle). Lookups
            // use the BASE id and name; every id minted below gets the suffix back.
            // By id = an entry being added or refreshed; by title = a search candidate.
            var isEntryResolve = idOrTitle.IsNotNullOrWhiteSpace() && idOrTitle.StartsWith("local-");

            if (isEntryResolve)
            {
                library = LibraryTypes.Parse(idOrTitle);
            }

            var baseId = idOrTitle.IsNullOrWhiteSpace() ? "local-series"
                : idOrTitle.StartsWith("local-") ? LibraryTypes.BaseId(idOrTitle)
                : "local-" + Slug(SeriesIdentity(idOrTitle, EditionLanguages.ParseChain(_configService.PreferredEditionLanguages)));
            var foreignAuthorId = LibraryTypes.WithType(baseId, library);

            // FindById returns null on the initial add (series not yet persisted); on refresh it
            // carries the user's MaxVolume cap (if any). The stored id carries the suffix.
            var existing = _authorService.FindById(foreignAuthorId);

            // Query the metadata sources with the stored (curated) series name when one exists —
            // the de-slugged id form strips the punctuation the sources' search engines need
            // (AniList returns ZERO results for a long unpunctuated arc name, which silently
            // downgrades status/volume data). Initial add has no stored name and keeps the de-slug.
            // Preferred Edition (2026-09-24, plan A1): a series named in its edition's language (D3) is
            // asked about by its English anchor name. Every English series has no AnchorName and keeps
            // the stored (curated) name -- today's term.
            var anchorName = existing?.Metadata?.Value?.AnchorName;
            var name = anchorName.IsNotNullOrWhiteSpace() ? anchorName
                : existing?.Name.IsNotNullOrWhiteSpace() == true ? existing.Name : DisplayName(foreignAuthorId);

            // Resolve the series + per-volume metadata (GCD spine when a bundled artifact is present,
            // else the live AniList/MangaUpdates/MangaDex/Google Books chain). foreignAuthorId/TitleSlug
            // stay derived from the slug — only display fields switch.
            // Bound by id (D4): the stored AniList id is fetched as-is and the title search is
            // skipped; null runs the search (catalogue id, ranked strict, alias retry). The
            // de-slugged id rides along as the first alias (D3) — on refresh it differs from the
            // curated name ("Mushoku Tensei Jobless Reincarnation" vs "Mushoku Tensei").
            // Preferred Edition: a non-English edition takes the seven-argument overload; the English
            // call is byte-identical to before (EnEditionPinning).
            var snapshotId = existing?.Metadata?.Value?.AniListId;
            var edition = anchorOnly ? null : EditionRequestFor(existing, requestedEdition);

            // Preferred Edition (2026-09-24, M13 pre-review fix, ruling (c)): an English entry that still
            // carries an AnchorName (localized, then changed back to English without Rename) keeps its pins
            // under its stored name -- the six-argument call, unchanged, inside a scope naming it. No
            // AnchorName (every never-localized series): no scope, today's key.
            var englishPinName = edition == null && anchorName.IsNotNullOrWhiteSpace() ? existing.Name : null;
            MangaSeriesMetadata series;

            using (englishPinName != null ? MangaSeriesMetadataProvider.PinsUnder(englishPinName) : null)
            {
                series = edition == null
                    ? _mangaMetadataProvider.GetSeries(name, existing?.MaxVolume ?? 0, resolveVolumeDetails, library, snapshotId, DisplayName(foreignAuthorId))
                    : _mangaMetadataProvider.GetSeries(name, existing?.MaxVolume ?? 0, resolveVolumeDetails, library, snapshotId, DisplayName(foreignAuthorId), edition);
            }

            // Light novels are catalogue-only (D11): refuse rather than mint an empty shell. The
            // add turns this into a validation message, the search paths into an empty result, the
            // refresh into a logged skip (never a delete -- see NotInCatalogueException).
            // Preferred Edition: an edition request's refusal names the chain (spec §2.3); the English
            // refusal is unchanged.
            if (library == LibraryType.LightNovel && series.NotInCatalogue)
            {
                throw edition == null ? new NotInCatalogueException(name) : new NotInCatalogueException(name, true);
            }

            // A typed search query resolves to its canonical series title (relaxed provider
            // match) and the identity must derive from THAT: an id slugged from the raw query
            // ("local-apothecary-diaries") de-slugs back to a name the strict add/refresh match
            // can never resolve, so the added series would stay an empty shell forever. Deriving
            // from the canonical title also makes an already-added series surface as such.
            if (!idOrTitle.IsNullOrWhiteSpace() && !idOrTitle.StartsWith("local-") && series.DisplayName.IsNotNullOrWhiteSpace())
            {
                // Preferred Edition: the identity is the anchor's name, never the localized one (spec
                // §2.2 Identity) -- a French add is "local-attack-on-titan", so "already in library" holds.
                var canonicalId = LibraryTypes.WithType("local-" + Slug(SeriesIdentity(series.IdentityName ?? series.DisplayName)), library);

                if (canonicalId != foreignAuthorId)
                {
                    foreignAuthorId = canonicalId;
                    existing = _authorService.FindById(foreignAuthorId) ?? existing;
                }
            }

            var displayName = ResolveDisplayName(series.DisplayName, existing?.Name);

            // The binding: what this resolve found, else what is stored. The refresh upsert
            // (AuthorMetadataRepository.UpsertMany) writes this whole row — there is no ratchet
            // on that path (AuthorMetadata.UseMetadataFrom is not called by the refresh) — so a
            // pass where AniList missed or the id fetch failed must carry the stored id forward
            // itself, or the entry would silently un-bind.
            // Re-read at write time, not from the snapshot above: the resolve takes minutes and
            // Fix Match (or the rebind pass) can write a different id meanwhile — the add flow's
            // background full refresh is the usual window, and the PUT's corrective refresh is
            // deduplicated against the one already running. A stored id that differs from the
            // snapshot is that newer write (a bind or an unbind) and wins over this pass;
            // otherwise the rule is the resolved id, else the stored one. On the canonical-rename
            // branch above the snapshot is null and the current read is the canonical entry's.
            var storedMeta = _authorService.FindById(foreignAuthorId)?.Metadata?.Value;
            var storedNow = storedMeta?.AniListId;
            var boundId = storedNow != snapshotId ? storedNow : (series.AniListId ?? storedNow);

            // Preferred Edition (2026-09-24): the edition binding carries forward exactly like AniListId --
            // a re-resolve that wrote while this pass ran wins (stored differs from the snapshot);
            // otherwise this pass's line, else what is stored. An English series binds only TomeLineId
            // (its EditionLanguage and AnchorName stay null; plan A3). The anchor-only answer to a search
            // for an entry whose bound line vanished keeps all three stored values together, so the
            // in-memory binding never mixes the anchor's line with the stored language (fix round 1).
            // AnchorName is written only when the entry is CREATED, and only while the name is not the
            // anchor (plan A1, ruling S3: a line with no local name stores none). A refresh keeps the
            // stored value -- this pass's IdentityName drifts (relaxed add vs strict refresh, an AniList
            // retitle) and a curated stored name is not a localized one. A re-resolve writes it (M13).
            var snapshotMeta = existing?.Metadata?.Value;
            var editionMoved = storedMeta != null && snapshotMeta != null &&
                               (storedMeta.EditionLanguage != snapshotMeta.EditionLanguage || storedMeta.TomeLineId != snapshotMeta.TomeLineId);
            var keepStoredEdition = editionMoved || anchorOnly;
            var boundEdition = keepStoredEdition ? storedMeta?.EditionLanguage : series.EditionLanguage ?? storedMeta?.EditionLanguage;
            var boundLine = keepStoredEdition ? storedMeta?.TomeLineId : series.TomeLineId ?? storedMeta?.TomeLineId;
            var boundCollected = keepStoredEdition || series.TomeLineId == null ? storedMeta?.EditionCollected ?? false : series.EditionCollected;
            var boundAnchor = keepStoredEdition ? storedMeta?.AnchorName
                : existing == null && series.EditionLanguage != null ? (displayName != series.IdentityName ? series.IdentityName : null)
                : storedMeta?.AnchorName;

            // A bound entry whose by-id fetch failed this pass (429, transport: the provider
            // returns no match at all, MatchedVia null) keeps its stored presentation — the
            // provider's result would otherwise blank the overview, drop the AniList poster for
            // the MangaDex fallback (or none), rebuild the aliases without AniList, zero the
            // rating and re-derive status/total from the other sources. Spec §5: unreachable /
            // rate-limited keeps local data, no id change. An unbound entry keeps today's path.
            var stored = boundId.HasValue && series.MatchedVia == null ? existing?.Metadata?.Value : null;

            // Series rating (AniList averageScore, 0-5). Votes>0 keeps Popularity above the
            // metadata-profile MinPopularity filter; Value=0 when there is no score.
            var seriesRating = stored?.Ratings?.Votes > 0
                ? stored.Ratings.JsonClone()
                : new Ratings { Votes = 1000, Value = series.RatingValue };

            var meta = new AuthorMetadata
            {
                ForeignAuthorId = foreignAuthorId,
                TitleSlug = foreignAuthorId,
                Name = displayName,
                SortName = displayName.ToLowerInvariant(),
                NameLastFirst = displayName,
                SortNameLastFirst = displayName.ToLowerInvariant(),
                Overview = stored?.Overview ?? KeptOnDisplayMiss(series, existing?.Metadata?.Value?.Overview) ?? series.Overview,
                Status = stored?.Status ?? series.Status,
                TotalVolumes = stored?.TotalVolumes ?? series.JapaneseTotal,
                Aliases = stored?.Aliases?.Any() == true ? stored.Aliases : series.AltTitles ?? new List<string>(),
                Ratings = seriesRating.JsonClone(),
                ParentName = series.ParentName,
                ParentForeignAuthorId = series.ParentName.IsNullOrWhiteSpace() ? null : ForeignAuthorIdFor(series.ParentName, library),
                AniListId = boundId,
                EditionLanguage = boundEdition,
                TomeLineId = boundLine,
                AnchorName = boundAnchor,
                EditionCollected = boundCollected,

                // One copy each (2026-09-20): a light novel's real author, from the writer ladder (pin,
                // calibre file, catalogue, else null -> filed under the series name). Only on an add
                // or a refresh: the ladder asks calibre, a search candidate's Writer is never used
                // (the add re-resolves), and a lookup that skips the ladder carries the stored value
                // forward rather than erasing it. Manga: none.
                Writer = library != LibraryType.LightNovel ? null
                    : isEntryResolve ? _writerResolver.Resolve(existing, LibraryTypes.PinKey(displayName, library), series.Writer)
                    : existing?.Metadata?.Value?.Writer
            };

            // An empty stored list is not "local data" to keep — fall to the provider's fallback as before.
            // The poster also ratchets the way a volume cover does (Edition.UseMetadataFrom keeps
            // Images on an empty remote): when volume 1's Google lookup did not answer this pass and
            // the poster fell below Google's tier only for that reason, the stored poster stays
            // rather than flipping to AniList for a day. Only the images — the rest of the
            // presentation is the provider's (AniList resolved fine).
            // The same ratchet when the display fallback (OpenTome display_anilist_id) was needed for the
            // poster and its AniList fetch did not answer: keep the stored poster, never blank it.
            var storedImages = stored != null ? stored.Images
                : series.PosterFellBackOnMiss ? existing?.Metadata?.Value?.Images
                : series.DisplayFetchFailed && series.CoverUrl.IsNullOrWhiteSpace() ? existing?.Metadata?.Value?.Images
                : null;

            if (storedImages?.Any() == true)
            {
                meta.Images = storedImages.JsonClone();
            }
            else if (series.CoverUrl.IsNotNullOrWhiteSpace())
            {
                meta.Images.Add(new MediaCover.MediaCover { CoverType = MediaCoverTypes.Poster, Url = series.CoverUrl });
            }

            var author = new Author
            {
                Metadata = meta,
                CleanName = LibraryTypes.CleanNameFor(Clean(displayName), library),
                Monitored = true,
                Series = new List<Series>()
            };

            // Auto-discover volumes no metadata source knows about — fractional side-story volumes
            // (3.5) and any whole volume beyond the metadata count — from the files in the series
            // folder. Whole-numbered extras get per-volume metadata via the same Google Books path
            // as the main loop (fractionals stay bare); all become real, adoptable volumes that
            // survive refresh (re-discovered every cycle).
            var volumes = new List<MangaVolumeMetadata>(series.Volumes);
            var knownVolumes = new HashSet<string>(volumes.Select(v => MangaVolumeParser.Format(v.VolumeNumber)));
            foreach (var extra in DiscoverExtraVolumes(existing))
            {
                if (knownVolumes.Add(MangaVolumeParser.Format(extra)))
                {
                    // Preferred Edition (2026-09-24): a non-English edition's extra is looked up in its language.
                    volumes.Add(EditionLanguages.IsEnglish(series.EditionLanguage)
                        ? _mangaMetadataProvider.ResolveVolume(displayName, extra, resolveVolumeDetails)
                        : _mangaMetadataProvider.ResolveVolume(displayName, extra, resolveVolumeDetails, series.EditionLanguage));
                }
            }

            var books = new List<Book>();
            foreach (var vol in volumes)
            {
                var i = vol.VolumeNumber;
                var volToken = MangaVolumeParser.Format(i);
                var foreignBookId = $"{foreignAuthorId}-v{volToken}";
                // Preferred Edition (2026-09-24, D4): a non-English edition's volume label is its own
                // ("Tome 5", "Band 5", "第5巻"); English is unchanged.
                var title = EditionLanguages.IsEnglish(series.EditionLanguage)
                    ? $"{displayName} Vol. {volToken}"
                    : $"{displayName} {EditionLanguages.VolumeLabel(series.EditionLanguage, volToken)}";

                // Per-volume cover when a source had one; otherwise fall back to the series cover so
                // every volume row still shows art — unless Google did not answer for this volume
                // this pass: then no image is minted at all, so Edition.UseMetadataFrom's ratchet
                // (Images kept on an empty remote) preserves the cover a previous pass fetched
                // instead of swapping it for the poster for a day. (A brand-new volume stays
                // art-less until the next pass answers.) A rejected record (D5, 2026-09-17) is not
                // a miss: the poster is minted and replaces the cover that record gave.
                var volumeCoverUrl = vol.CoverUrl.IsNotNullOrWhiteSpace() ? vol.CoverUrl
                    : vol.GoogleMissed ? null
                    : series.VolumeCoverUrl;

                // A manga volume is ONE Archive edition ("-ed", Format "Paperback": byte-identical to
                // before). A light-novel volume is TWO editions, both monitored at mint: the Ebook
                // edition ("-ed", Format "ebook") and the Audio edition ("-audio-ed", Format
                // "Audiobook"). DistanceCalculator already scores those Format strings, so an .epub
                // routes to the first and an .m4b to the second with no new identification code.
                // Both are re-minted on every refresh, which is what keeps SortChildren from
                // deleting them.
                var mediaTypes = library == LibraryType.LightNovel
                    ? new[] { MediaType.Ebook, MediaType.Audio }
                    : new[] { MediaType.Archive };

                var editions = mediaTypes
                    .Select(mediaType => MintEdition(foreignBookId, title, mediaType, vol, seriesRating, volumeCoverUrl, series.AudibleAnswered, series.EditionLanguage, series.AudioSkipped))
                    .ToList();

                var book = new Book
                {
                    ForeignBookId = foreignBookId,
                    Title = title,

                    // Audiobook identity B1 (2026-09-17, D3): the derived subtitle; null for manga and
                    // for a volume without one (the ratchet keeps a stored subtitle on null unless
                    // SubtitleRejected says a candidate was rejected this pass -- fix round 3,
                    // 2026-09-24 -- in which case UseMetadataFrom clears it instead).
                    Subtitle = vol.Subtitle,
                    SubtitleRejected = vol.SubtitleRejected,
                    VolumeNumber = i,
                    TitleSlug = foreignBookId,
                    CleanTitle = Clean(title),
                    ReleaseDate = vol.ReleaseDate,
                    ReleaseDatePrecision = vol.ReleaseDatePrecision,
                    Monitored = true,
                    AnyEditionOk = true,
                    Ratings = seriesRating.JsonClone(),
                    Editions = editions,
                    SeriesLinks = new List<SeriesBookLink>(),
                    Author = author,
                    AuthorMetadata = meta
                };

                editions.ForEach(e => e.Book = book);
                books.Add(book);
            }

            author.Books = books;
            return author;
        }

        // D3 (2026-09-16): a volume with no usable description has an EMPTY overview — no
        // "<Series>, volume N." placeholder — so the ratchet in Edition.UseMetadataFrom can tell
        // "nothing found" from a description, and the page shows nothing rather than a stub.
        private static Edition MintEdition(string foreignBookId, string title, MediaType mediaType, MangaVolumeMetadata vol, Ratings seriesRating, string volumeCoverUrl, bool audibleAnswered, string editionLanguage, bool audioSkipped)
        {
            var foreignEditionId = foreignBookId + MediaTypes.EditionIdSuffix(mediaType);

            var edition = new Edition
            {
                ForeignEditionId = foreignEditionId,
                TitleSlug = foreignEditionId,
                Title = title,
                // Preferred Edition (2026-09-24): "eng" unless the series has another edition (calibre `languages`
                // follows). Final fix round Minor 2: a light novel's Audio edition is the English audiobook (D8)
                // in every edition, so it is always "eng".
                Language = mediaType == MediaType.Audio ? EditionLanguages.ToIso3(EditionLanguages.English) : EditionLanguages.ToIso3(editionLanguage),
                Overview = vol.Overview ?? string.Empty,

                // D5 (2026-09-17): the ISBN record was rejected and no other source gave a blurb —
                // the ratchet clears the stored text instead of keeping it (both LN editions).
                // A pass that could not ask Google (quota) keeps the cover -- keep the blurb too;
                // the next answering pass decides.
                OverviewRejected = vol.GoogleRejected && !vol.GoogleMissed && vol.Overview.IsNullOrWhiteSpace(),

                // B3b (2026-09-18): the image-less mint below (Google missed, nothing else had a
                // cover) — Edition.UseDbFieldsFrom carries the stored images onto it so the row is
                // not rewritten every quota-out pass. Both editions of the volume share the cover.
                CoverMissed = vol.GoogleMissed && volumeCoverUrl.IsNullOrWhiteSpace(),
                Format = MediaTypes.EditionFormat(mediaType),
                IsEbook = mediaType == MediaType.Ebook,
                MediaType = mediaType,
                Isbn13 = vol.Isbn13,
                PageCount = vol.PageCount,
                ReleaseDate = vol.ReleaseDate,

                // Preferred Edition (2026-09-24, D8): an edition with no English-audio counterpart mints its
                // Audio edition unmonitored (nothing wanted, nothing searched). Every other edition as before.
                Monitored = !(audioSkipped && mediaType == MediaType.Audio),
                ManualAdd = true,
                Ratings = seriesRating.JsonClone()
            };

            if (volumeCoverUrl.IsNotNullOrWhiteSpace())
            {
                edition.Images.Add(new MediaCover.MediaCover { CoverType = MediaCoverTypes.Cover, Url = volumeCoverUrl });
            }

            // Audiobook identity B1 (2026-09-17, D1): the Audio edition alone carries the Audible
            // product — null fields when the volume has none (the ratchet keeps what an earlier pass
            // stored). The Ebook edition and a manga's Archive edition never do (D7).
            if (mediaType == MediaType.Audio)
            {
                edition.Asin = vol.Audio?.Asin;
                edition.AudiobookTitle = vol.Audio?.Title;
                edition.AudiobookSubtitle = vol.Audio?.Subtitle;
                edition.RuntimeMinutes = vol.Audio?.RuntimeMinutes;
                edition.AudioReleaseDate = vol.Audio?.ReleaseDate;
                edition.CoveredByVolume = vol.CoveredByVolume;

                // Covered volumes B2 (D4a): a mark the provider derived is Audible's; a pass Audible
                // answered (even with nothing) asserts it — or its absence — over the stored
                // "audible" mark (Edition.UseMetadataFrom). An "import" mark is the services' alone.
                edition.CoveredSource = vol.CoveredByVolume.HasValue ? CoveredSources.Audible : null;
                edition.CoveredAsserted = audibleAnswered;
            }

            return edition;
        }

        // Volume numbers present as CBZ/CBR files in the series folder but absent from metadata — the
        // source of fractional side-story volumes (and any whole volume beyond the metadata count). It
        // is intentionally disk-only: a volume that exists on disk is re-discovered every refresh (so it
        // never gets pruned), and once imported its BookFile makes RefreshBookService.ShouldDelete keep
        // it regardless. Re-adding *all* DB volumes would instead wrongly resurrect volumes a metadata
        // correction legitimately dropped. Empty until the author is persisted with a path (from the
        // first refresh after add onward), so the search path (existing == null) is untouched.
        private IEnumerable<double> DiscoverExtraVolumes(Author existing)
        {
            if (existing == null || existing.Path.IsNullOrWhiteSpace() || !_diskProvider.FolderExists(existing.Path))
            {
                return Array.Empty<double>();
            }

            var found = new List<double>();

            // Preferred Edition (2026-09-24): a series of another edition reads its own tokens in the files
            // Mangarr named ("… Tome 5.cbz") and, with its series-name matcher (fix round 1), a user's own
            // "… T05.cbz"; an English series has no EditionLanguage and no matcher -- today's parse.
            foreach (var file in _diskProvider.GetFiles(existing.Path, true))
            {
                if (MediaFileExtensions.TextExtensions.Contains(Path.GetExtension(file)) &&
                    MangaVolumeParser.TryParseSeriesVolume(Path.GetFileNameWithoutExtension(file), out _, out var volume, existing.Metadata?.Value?.EditionLanguage, EditionVolumeTokens.SeriesMatcher(existing.Metadata?.Value)) &&
                    volume > 0)
                {
                    found.Add(volume);
                }
            }

            return found;
        }

        public HashSet<string> GetChangedAuthors(DateTime startTime)
        {
            // SPIKE(search-driven): there is no metadata server. The "author/changed" route hits
            // the dead bookinfo host and the DNS/connection failure is NOT suppressed by
            // SuppressHttpError (that only covers HTTP status errors), so this threw and aborted
            // the entire scheduled library-wide RefreshAuthor run every day. Returning null mirrors
            // the GetAuthorInfo/GetBookInfo SPIKE guards; the bulk refresh tolerates null and falls
            // back to per-author ShouldRefresh.
            if (SpikeEnabled)
            {
                return null;
            }

            var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                .SetSegment("route", "author/changed")
                .AddQueryParam("since", startTime.ToString("o"))
                .Build();

            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<RecentUpdatesResource>(httpRequest);

            if (httpResponse.Resource == null || httpResponse.Resource.Limited)
            {
                return null;
            }

            return new HashSet<string>(httpResponse.Resource.Ids.Select(x => x.ToString()));
        }

        public Author GetAuthorInfo(string foreignAuthorId, bool useCache = true, bool resolveVolumeDetails = true)
        {
            // SPIKE(search-driven): no metadata server — rebuild the series + volumes locally from the id.
            // resolveVolumeDetails=false emits volume STRUCTURE only (no per-volume HTTP) for a fast add;
            // covers/dates/descriptions are filled by a background full refresh (see RefreshAuthorService).
            if (SpikeEnabled)
            {
                return BuildFakeAuthor(foreignAuthorId, resolveVolumeDetails);
            }

            _logger.Debug("Getting Author details GoodreadsId of {0}", foreignAuthorId);

            try
            {
                if (useCache)
                {
                    return PollAuthor(foreignAuthorId);
                }

                return PollAuthorUncached(foreignAuthorId);
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Unexpected error getting author info: {foreignAuthorId}", foreignAuthorId);
                throw;
            }
        }

        public Author GetAuthorInfo(string foreignAuthorId, bool useCache, bool resolveVolumeDetails, string editionLanguage)
        {
            // Preferred Edition (2026-09-24): the Add form's chosen edition (AddAuthorService). The library
            // rides in the id, as on every by-id resolve.
            return BuildFakeAuthor(foreignAuthorId, resolveVolumeDetails, LibraryType.Manga, editionLanguage);
        }

        public HashSet<string> GetChangedBooks(DateTime startTime)
        {
            return _cache.Get("ChangedBooks", () => GetChangedBooksUncached(startTime), TimeSpan.FromMinutes(30));
        }

        private HashSet<string> GetChangedBooksUncached(DateTime startTime)
        {
            return null;
        }

        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string foreignBookId)
        {
            // SPIKE(search-driven): no metadata server — rebuild the volume + its series locally.
            if (SpikeEnabled)
            {
                // "-v005" comes off; the "~ln" suffix stays on the series id and BuildFakeAuthor parses it.
                var authorId = System.Text.RegularExpressions.Regex.Replace(foreignBookId ?? "local-series", "-v[0-9.]+$", string.Empty);
                var fakeAuthor = BuildFakeAuthor(authorId);
                var book = fakeAuthor.Books.Value.Find(b => b.ForeignBookId == foreignBookId)
                           ?? fakeAuthor.Books.Value.FirstOrDefault();
                if (book == null)
                {
                    // The series resolved zero volumes (every metadata source missed). Fail soft like the
                    // non-spike path below rather than indexing into an empty list.
                    throw new BookInfoException("No volumes resolved for series '{0}'", authorId);
                }

                return Tuple.Create(authorId, book, new List<AuthorMetadata> { fakeAuthor.Metadata.Value });
            }

            try
            {
                return PollBook(foreignBookId);
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Unexpected error getting book info: {foreignBookId}", foreignBookId);
                throw;
            }
        }

        public List<object> SearchForNewEntity(string title)
        {
            return SearchForNewEntity(title, LibraryType.Manga);
        }

        public List<object> SearchForNewEntity(string title, LibraryType library)
        {
            // Ignore 1-char / blank terms: they match nothing useful but each fires a full (slow)
            // 3-provider metadata lookup — the source of the 40s 'L'/'N' lookups that froze the
            // search box as the user typed. A real manga title is always at least two characters.
            if (title == null || title.Trim().Length < 2)
            {
                return new List<object>();
            }

            // SPIKE(search-driven): lightweight lookup — series + volume STRUCTURE only, no per-volume
            // external metadata (that's resolved on add/refresh). Keeps the search bar responsive.
            Author fakeAuthor;
            try
            {
                fakeAuthor = SpikeEnabled ? WithEditionOptions(BuildFakeAuthorForTitle(title, library), library) : null;
            }
            catch (NotInCatalogueException)
            {
                // Light novels are catalogue-only: nothing to offer. The Add page renders the
                // catalogue-miss notice with the OpenTome link on an empty result.
                return new List<object>();
            }

            var books = SpikeEnabled ? fakeAuthor.Books.Value : SearchForNewBook(title, null, false);

            var result = new List<object>();
            foreach (var book in books)
            {
                var author = book.Author.Value;

                if (!result.Contains(author))
                {
                    result.Add(author);
                }

                result.Add(book);
            }

            // A series only the indexer knows resolves to 0 volumes on the lightweight path -> no
            // books to derive the author from. Surface the series itself so entity search still finds it.
            if (fakeAuthor != null && !result.Contains(fakeAuthor))
            {
                result.Insert(0, fakeAuthor);
            }

            return result;
        }

        public List<Author> SearchForNewAuthor(string title)
        {
            return SearchForNewAuthor(title, LibraryType.Manga);
        }

        public List<Author> SearchForNewAuthor(string title, LibraryType library)
        {
            // Same 1-char / blank guard as SearchForNewEntity: skip the slow lookup for terms too
            // short to be a real series.
            if (title == null || title.Trim().Length < 2)
            {
                return new List<Author>();
            }

            // SPIKE(search-driven): the add-series lookup the user hits. Return the candidate series
            // directly from the lightweight build (no per-volume external metadata) — fast, and the
            // author surfaces even when the volume count resolves to 0. Full volumes resolve on add.
            if (SpikeEnabled)
            {
                try
                {
                    return new List<Author> { WithEditionOptions(BuildFakeAuthorForTitle(title, library), library) };
                }
                catch (NotInCatalogueException)
                {
                    return new List<Author>();
                }
            }

            var books = SearchForNewBook(title, null);

            return books
                .Select(x => x.Author.Value)
                .DistinctBy(x => x.ForeignAuthorId)
                .ToList();
        }

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
            // SPIKE(search-driven): no metadata server — synthesize a series + volumes from the query term.
            if (SpikeEnabled)
            {
                // Structure-only: this entry point serves identification (manual-import GET +
                // queue-refresh imports), which only needs volume titles/numbers to match files.
                // The per-volume Google Books/OpenLibrary enrichment (a 43-volume series = up to
                // ~170 rate-limited calls ≈ minutes on a cold cache, run synchronously on the
                // request thread) belongs to the post-add/refresh pipeline, which re-resolves
                // fully and persists real dates/covers. This also inherits GetSeries's
                // relaxed+cached provider matching — acceptable here because the identification
                // distance gate rejects wrong-franchise candidates.
                return BuildFakeAuthorForTitle(title, LibraryType.Manga).Books.Value;
            }

            var q = title.ToLower().Trim();
            if (author != null)
            {
                q += " " + author;
            }

            try
            {
                var lowerTitle = title.ToLowerInvariant();

                var split = lowerTitle.Split(':');
                var prefix = split[0];

                if (split.Length == 2 && new[] { "author", "work", "edition", "isbn", "asin" }.Contains(prefix))
                {
                    var slug = split[1].Trim();

                    if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace))
                    {
                        return new List<Book>();
                    }

                    if (prefix == "author" || prefix == "work" || prefix == "edition")
                    {
                        var isValid = int.TryParse(slug, out var searchId);
                        if (!isValid)
                        {
                            return new List<Book>();
                        }

                        if (prefix == "author")
                        {
                            return SearchByGoodreadsAuthorId(searchId);
                        }

                        if (prefix == "work")
                        {
                            return SearchByGoodreadsWorkId(searchId);
                        }

                        if (prefix == "edition")
                        {
                            return SearchByGoodreadsBookId(searchId, getAllEditions);
                        }
                    }

                    // to handle isbn / asin
                    q = slug;
                }

                return Search(q, getAllEditions);
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex, ex.Message);
                throw new GoodreadsException("Search for '{0}' failed. Unable to communicate with Goodreads.", ex, title);
            }
            catch (Exception ex) when (ex is not BookInfoException)
            {
                _logger.Warn(ex, ex.Message);
                throw new GoodreadsException("Search for '{0}' failed. Invalid response received from Goodreads.", ex, title);
            }
        }

        // SPIKE(search-driven) / beta readiness fix round (2026-09-28, E6): import identification
        // (CandidateService.GetRemoteCandidates) calls these for a file with an ISBN/ASIN tag and no local
        // match. Search() asks Goodreads' public auto_complete (goodreads.com) and maps the hits through the
        // SPIKE builders by Goodreads ids, which are not Mangarr's. No request: no candidates, and the file
        // falls through to the name search / no-match handling like any untagged file.
        public List<Book> SearchByIsbn(string isbn)
        {
            if (SpikeEnabled)
            {
                return new List<Book>();
            }

            return Search(isbn, true);
        }

        public List<Book> SearchByAsin(string asin)
        {
            if (SpikeEnabled)
            {
                return new List<Book>();
            }

            return Search(asin, true);
        }

        private List<Book> Search(string query, bool getAllEditions)
        {
            List<SearchJsonResource> result;
            try
            {
                result = _goodreadsSearchProxy.Search(query);
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Error searching for {0}", query);
                return new List<Book>();
            }

            var books = new List<Book>();

            if (getAllEditions)
            {
                // Slower but more exhaustive, less intensive on metadata API
                var bookIds = result.Select(x => x.WorkId).ToList();

                var idMap = result.Select(x => new { AuthorId = x.Author.Id, BookId = x.WorkId })
                    .GroupBy(x => x.AuthorId)
                    .ToDictionary(x => x.Key, x => x.Select(i => i.BookId.ToString()).ToList());

                List<Book> authorBooks;
                foreach (var author in idMap.Keys)
                {
                    authorBooks = SearchByGoodreadsAuthorId(author);
                    books.AddRange(authorBooks.Where(b => idMap[author].Contains(b.ForeignBookId)));
                }

                var missingBooks = bookIds.ExceptBy(x => x.ToString(), books, x => x.ForeignBookId, StringComparer.Ordinal).ToList();
                foreach (var book in missingBooks)
                {
                    books.AddRange(SearchByGoodreadsWorkId(book));
                }

                return books;
            }
            else
            {
                // Use sparingly, hits metadata API quite hard
                var ids = result.Select(x => x.BookId).ToList();

                if (ids.Count == 0)
                {
                    return new List<Book>();
                }

                if (ids.Count == 1)
                {
                    return SearchByGoodreadsBookId(ids[0], false);
                }

                try
                {
                    return MapSearchResult(ids);
                }
                catch (HttpException ex)
                {
                    _logger.Warn(ex);
                    throw new BookInfoException("Search for '{0}' failed. Unable to communicate with ReadarrAPI, returning status code: {1}.", ex, query, ex.Response.StatusCode);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Error mapping search results");

                    return new List<Book>();
                }
            }
        }

        private List<Book> SearchByGoodreadsAuthorId(int id)
        {
            try
            {
                var authorId = id.ToString();
                var result = GetAuthorInfo(authorId);
                var books = result.Books.Value;
                var authors = new Dictionary<string, AuthorMetadata> { { authorId, result.Metadata.Value } };

                foreach (var book in books)
                {
                    AddDbIds(authorId, book, authors);
                }

                return books;
            }
            catch (AuthorNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Error searching by author id");
                return new List<Book>();
            }
        }

        public List<Book> SearchByGoodreadsWorkId(int id)
        {
            try
            {
                var tuple = GetBookInfo(id.ToString());
                AddDbIds(tuple.Item1, tuple.Item2, tuple.Item3.ToDictionary(x => x.ForeignAuthorId));
                return new List<Book> { tuple.Item2 };
            }
            catch (BookNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Error searching by work id");
                return new List<Book>();
            }
        }

        public List<Book> SearchByGoodreadsBookId(int id, bool getAllEditions)
        {
            // SPIKE(search-driven) / beta readiness (2026-09-28, E6): no metadata server. Import identification
            // (CandidateService.GetRemoteCandidates) calls this for a file whose tags carry a Goodreads id (a
            // calibre-written EPUB can); GetEditionInfo would GET api.bookinfo.club, whose DNS failure is not a
            // BookInfoException and escaped to fail the file's identification. No candidates instead.
            if (SpikeEnabled)
            {
                return new List<Book>();
            }

            try
            {
                var book = GetEditionInfo(id, getAllEditions);

                return new List<Book> { book };
            }
            catch (AuthorNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookNotFoundException)
            {
                return new List<Book>();
            }
            catch (EditionNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Error searching by book id");
                return new List<Book>();
            }
        }

        private Book GetEditionInfo(int id, bool getAllEditions)
        {
            HttpRequest httpRequest;
            HttpResponse httpResponse;

            while (true)
            {
                httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", $"book/{id}")
                    .Build();

                httpRequest.SuppressHttpError = true;

                // we expect a redirect
                httpResponse = _httpClient.Get(httpRequest);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    WaitUntilRetry(httpResponse);
                }
                else
                {
                    break;
                }
            }

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                throw new EditionNotFoundException(id.ToString());
            }

            if (!httpResponse.HasHttpRedirect)
            {
                throw new BookInfoException("Unexpected response from {0}", httpRequest.Url);
            }

            var location = httpResponse.Headers.GetSingleValue("Location");
            var split = location.Split('/').Reverse().ToList();
            var newId = split[0];
            var type = split[1];

            Book book;
            List<AuthorMetadata> authors;

            if (type == "author")
            {
                var author = PollAuthor(newId);

                book = author.Books.Value.FirstOrDefault(b => b.Editions.Value.Any(e => e.ForeignEditionId == id.ToString()));
                authors = new List<AuthorMetadata> { author.Metadata.Value };
            }
            else if (type == "work")
            {
                var tuple = PollBook(newId);

                book = tuple.Item2;
                authors = tuple.Item3;
            }
            else
            {
                throw new NotImplementedException($"Unexpected response from {httpResponse.Request.Url}");
            }

            if (book == null || book.Editions.Value.All(e => e.ForeignEditionId != id.ToString()))
            {
                throw new EditionNotFoundException(id.ToString());
            }

            if (!getAllEditions)
            {
                var trimmed = new Book();
                trimmed.UseMetadataFrom(book);
                trimmed.Author.Value.Metadata = book.AuthorMetadata.Value;
                trimmed.AuthorMetadata = book.AuthorMetadata.Value;
                trimmed.SeriesLinks = book.SeriesLinks;
                var edition = book.Editions.Value.SingleOrDefault(e => e.ForeignEditionId == id.ToString());
                if (edition != null)
                {
                    edition.Monitored = true;
                }

                trimmed.Editions = new List<Edition> { edition };
                book = trimmed;
            }

            var authorDict = authors.ToDictionary(x => x.ForeignAuthorId);
            AddDbIds(book.AuthorMetadata.Value.ForeignAuthorId, book, authorDict);

            return book;
        }

        private List<Book> MapSearchResult(List<int> ids)
        {
            HttpResponse<BulkBookResource> httpResponse;

            while (true)
            {
                var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", "book/bulk")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                httpRequest.SetContent(ids.ToJson());
                httpRequest.ContentSummary = ids.ToJson(Formatting.None);

                httpRequest.AllowAutoRedirect = true;
                httpRequest.SuppressHttpErrorStatusCodes = new[] { HttpStatusCode.TooManyRequests };

                httpResponse = _httpClient.Post<BulkBookResource>(httpRequest);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    WaitUntilRetry(httpResponse);
                }
                else
                {
                    break;
                }
            }

            var mapped = MapBulkBook(httpResponse.Resource);

            var idStr = ids.Select(x => x.ToString()).ToList();

            return mapped.OrderBy(b => idStr.IndexOf(b.Editions.Value.First().ForeignEditionId)).ToList();
        }

        private List<Book> MapBulkBook(BulkBookResource resource)
        {
            var books = new List<Book>();

            if (resource == null)
            {
                return books;
            }

            var authors = resource.Authors.Select(MapAuthorMetadata).ToDictionary(x => x.ForeignAuthorId, x => x);
            var series = resource.Series.Select(MapSeries).ToList();

            foreach (var work in resource.Works)
            {
                var book = MapBook(work);
                var authorId = work.Books.OrderByDescending(b => b.AverageRating * b.RatingCount).First().Contributors.First().ForeignId.ToString();

                AddDbIds(authorId, book, authors);

                books.Add(book);
            }

            MapSeriesLinks(series, books, resource.Series);

            return books;
        }

        private void AddDbIds(string authorId, Book book, Dictionary<string, AuthorMetadata> authors)
        {
            var dbBook = _bookService.FindById(book.ForeignBookId);
            if (dbBook != null)
            {
                book.UseDbFieldsFrom(dbBook);

                var editions = _editionService.GetEditionsByBook(dbBook.Id).ToDictionary(x => x.ForeignEditionId);

                // If we have any database editions, exactly one will be monitored.
                // So unmonitor all the found editions and let the UseDbFieldsFrom set
                // the monitored status
                foreach (var edition in book.Editions.Value)
                {
                    edition.Monitored = false;
                    if (editions.TryGetValue(edition.ForeignEditionId, out var dbEdition))
                    {
                        edition.UseDbFieldsFrom(dbEdition);
                    }
                }

                // Double check at least one edition is monitored
                if (book.Editions.Value.Any() && !book.Editions.Value.Any(x => x.Monitored))
                {
                    var mostPopular = book.Editions.Value.OrderByDescending(x => x.Ratings.Popularity).First();
                    mostPopular.Monitored = true;
                }
            }

            var author = _authorService.FindById(authorId);

            if (author == null)
            {
                if (!authors.TryGetValue(authorId, out var metadata))
                {
                    throw new BookInfoException("Expected author metadata for id [{0}] in book data {1}", authorId, book);
                }

                author = new Author
                {
                    CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                    Metadata = metadata
                };
            }

            book.Author = author;
            book.AuthorMetadata = author.Metadata.Value;
            book.AuthorMetadataId = author.AuthorMetadataId;
        }

        private Author PollAuthor(string foreignAuthorId)
        {
            return _authorCache.GetOrAdd(foreignAuthorId,
                () => PollAuthorUncached(foreignAuthorId),
                new LazyCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                    ImmediateAbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                    Size = 1,
                    SlidingExpiration = TimeSpan.FromMinutes(1),
                    ExpirationMode = ExpirationMode.ImmediateEviction
                }.RegisterPostEvictionCallback((key, value, reason, state) => _logger.Debug($"Clearing cache for {key} due to {reason}")));
        }

        private Author PollAuthorUncached(string foreignAuthorId)
        {
            AuthorResource resource = null;

            var useCache = true;

            for (var i = 0; i < 60; i++)
            {
                var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", $"author/{foreignAuthorId}")
                    .Build();

                httpRequest.AllowAutoRedirect = true;
                httpRequest.SuppressHttpError = true;

                var httpResponse = _cachedHttpClient.Get(httpRequest, useCache, TimeSpan.FromMinutes(30));

                if (httpResponse.HasHttpError)
                {
                    if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        WaitUntilRetry(httpResponse);
                        continue;
                    }
                    else if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        throw new AuthorNotFoundException(foreignAuthorId);
                    }
                    else if (httpResponse.StatusCode == HttpStatusCode.BadRequest)
                    {
                        throw new BadRequestException(foreignAuthorId);
                    }
                    else
                    {
                        throw new BookInfoException("Unexpected error fetching author data");
                    }
                }

                resource = JsonSerializer.Deserialize<AuthorResource>(httpResponse.Content, SerializerSettings);

                if (resource.Works != null)
                {
                    resource.Works ??= new List<WorkResource>();
                    resource.Series ??= new List<SeriesResource>();
                    break;
                }

                useCache = false;
                Thread.Sleep(2000);
            }

            if (resource?.Works == null)
            {
                throw new BookInfoException("Failed to get works for {0}", foreignAuthorId);
            }

            return MapAuthor(resource);
        }

        private Tuple<string, Book, List<AuthorMetadata>> PollBook(string foreignBookId)
        {
            WorkResource resource = null;

            for (var i = 0; i < 60; i++)
            {
                var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", $"work/{foreignBookId}")
                    .Build();

                httpRequest.SuppressHttpError = true;

                // this may redirect to an author
                var httpResponse = _httpClient.Get(httpRequest);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    WaitUntilRetry(httpResponse);
                    continue;
                }

                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new BookNotFoundException(foreignBookId);
                }

                if (httpResponse.HasHttpRedirect)
                {
                    var location = httpResponse.Headers.GetSingleValue("Location");
                    var split = location.Split('/').Reverse().ToList();
                    var newId = split[0];
                    var type = split[1];

                    if (type == "author")
                    {
                        var author = PollAuthor(newId);
                        var authorBook = author.Books.Value.SingleOrDefault(x => x.ForeignBookId == foreignBookId);

                        if (authorBook == null)
                        {
                            throw new BookNotFoundException(foreignBookId);
                        }

                        var authorMetadata = new List<AuthorMetadata> { author.Metadata.Value };

                        return Tuple.Create(author.ForeignAuthorId, authorBook, authorMetadata);
                    }
                    else
                    {
                        throw new NotImplementedException($"Unexpected response from {httpResponse.Request.Url}");
                    }
                }

                if (httpResponse.HasHttpError)
                {
                    if (httpResponse.StatusCode == HttpStatusCode.BadRequest)
                    {
                        throw new BadRequestException(foreignBookId);
                    }
                    else
                    {
                        throw new BookInfoException("Unexpected response fetching book data");
                    }
                }

                resource = JsonSerializer.Deserialize<WorkResource>(httpResponse.Content, SerializerSettings);

                if (resource.Books != null)
                {
                    break;
                }

                Thread.Sleep(2000);
            }

            if (resource?.Books == null || resource?.Authors == null || (!resource?.Authors?.Any() ?? false))
            {
                throw new BookInfoException("Failed to get books for {0}", foreignBookId);
            }

            var book = MapBook(resource);
            var authorId = GetAuthorId(resource).ToString();
            var metadata = resource.Authors.Select(MapAuthorMetadata).ToList();

            var series = resource.Series.Select(MapSeries).ToList();
            MapSeriesLinks(series, new List<Book> { book }, resource.Series);

            return Tuple.Create(authorId, book, metadata);
        }

        private void WaitUntilRetry(HttpResponse response)
        {
            var seconds = 5;

            if (response.Headers.ContainsKey("Retry-After"))
            {
                var retryAfter = response.Headers["Retry-After"];

                if (!int.TryParse(retryAfter, out seconds))
                {
                    seconds = 5;
                }
            }

            _logger.Info("BookInfo returned 429, backing off for {0}s", seconds);

            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }

        private static AuthorMetadata MapAuthorMetadata(AuthorResource resource)
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = resource.ForeignId.ToString(),
                TitleSlug = resource.ForeignId.ToString(),
                Name = resource.Name.CleanSpaces(),
                Overview = resource.Description,
                Ratings = new Ratings { Votes = resource.RatingCount, Value = (decimal)resource.AverageRating },
                Status = AuthorStatusType.Continuing
            };

            metadata.SortName = metadata.Name.ToLower();
            metadata.NameLastFirst = metadata.Name.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLower();

            if (resource.ImageUrl.IsNotNullOrWhiteSpace())
            {
                metadata.Images.Add(new MediaCover.MediaCover
                {
                    Url = resource.ImageUrl,
                    CoverType = MediaCoverTypes.Poster
                });
            }

            if (resource.Url.IsNotNullOrWhiteSpace())
            {
                metadata.Links.Add(new Links { Url = resource.Url, Name = "Goodreads" });
            }

            return metadata;
        }

        private static Author MapAuthor(AuthorResource resource)
        {
            var metadata = MapAuthorMetadata(resource);

            var books = resource.Works
                .Where(x => x.ForeignId > 0 && GetAuthorId(x) == resource.ForeignId)
                .Select(MapBook)
                .ToList();

            books.ForEach(x => x.AuthorMetadata = metadata);

            var series = resource.Series.Select(MapSeries).ToList();

            MapSeriesLinks(series, books, resource.Series);

            var result = new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                Books = books,
                Series = series
            };

            return result;
        }

        private static void MapSeriesLinks(List<Series> series, List<Book> books, List<SeriesResource> resource)
        {
            var bookDict = books.ToDictionary(x => x.ForeignBookId);
            var seriesDict = series.ToDictionary(x => x.ForeignSeriesId);

            foreach (var book in books)
            {
                book.SeriesLinks = new List<SeriesBookLink>();
            }

            // only take series where there are some works
            foreach (var s in resource.Where(x => x.LinkItems.Any()))
            {
                if (seriesDict.TryGetValue(s.ForeignId.ToString(), out var curr))
                {
                    curr.LinkItems = s.LinkItems.Where(x => x.ForeignWorkId != 0 && bookDict.ContainsKey(x.ForeignWorkId.ToString())).Select(l => new SeriesBookLink
                    {
                        Book = bookDict[l.ForeignWorkId.ToString()],
                        Series = curr,
                        IsPrimary = l.Primary,
                        Position = l.PositionInSeries,
                        SeriesPosition = l.SeriesPosition
                    }).ToList();

                    foreach (var l in curr.LinkItems.Value)
                    {
                        l.Book.Value.SeriesLinks.Value.Add(l);
                    }
                }
            }
        }

        private static Series MapSeries(SeriesResource resource)
        {
            var series = new Series
            {
                ForeignSeriesId = resource.ForeignId.ToString(),
                Title = resource.Title,
                Description = resource.Description
            };

            return series;
        }

        private static Book MapBook(WorkResource resource)
        {
            var book = new Book
            {
                ForeignBookId = resource.ForeignId.ToString(),
                Title = resource.Title,
                TitleSlug = resource.ForeignId.ToString(),
                CleanTitle = Parser.Parser.CleanAuthorName(resource.Title),
                ReleaseDate = resource.ReleaseDate,
                Genres = resource.Genres,
                RelatedBooks = resource.RelatedWorks
            };

            book.Links.Add(new Links { Url = resource.Url, Name = "Goodreads Editions" });

            if (resource.Books != null)
            {
                book.Editions = resource.Books.Select(x => MapEdition(x)).ToList();

                // monitor the most popular release
                var mostPopular = book.Editions.Value.MaxBy(x => x.Ratings.Popularity);
                if (mostPopular != null)
                {
                    mostPopular.Monitored = true;

                    // fix work title if missing
                    if (book.Title.IsNullOrWhiteSpace())
                    {
                        book.Title = mostPopular.Title;
                    }
                }
            }
            else
            {
                book.Editions = new List<Edition>();
            }

            // If we are missing the book release date, set as the earliest edition release date
            if (!book.ReleaseDate.HasValue)
            {
                var editionReleases = book.Editions.Value
                    .Where(x => x.ReleaseDate.HasValue && x.ReleaseDate.Value.Month != 1 && x.ReleaseDate.Value.Day != 1)
                    .ToList();

                if (editionReleases.Any())
                {
                    book.ReleaseDate = editionReleases.Min(x => x.ReleaseDate.Value);
                }
                else
                {
                    editionReleases = book.Editions.Value.Where(x => x.ReleaseDate.HasValue).ToList();
                    if (editionReleases.Any())
                    {
                        book.ReleaseDate = editionReleases.Min(x => x.ReleaseDate.Value);
                    }
                }
            }

            Debug.Assert(!book.Editions.Value.Any() || book.Editions.Value.Count(x => x.Monitored) == 1, "one edition monitored");

            book.AnyEditionOk = true;

            var ratingCount = book.Editions.Value.Sum(x => x.Ratings.Votes);

            if (ratingCount > 0)
            {
                book.Ratings = new Ratings
                {
                    Votes = ratingCount,
                    Value = book.Editions.Value.Sum(x => x.Ratings.Votes * x.Ratings.Value) / ratingCount
                };
            }
            else
            {
                book.Ratings = new Ratings { Votes = 0, Value = 0 };
            }

            return book;
        }

        private static Edition MapEdition(BookResource resource)
        {
            var edition = new Edition
            {
                ForeignEditionId = resource.ForeignId.ToString(),
                TitleSlug = resource.ForeignId.ToString(),
                Isbn13 = resource.Isbn13,
                Asin = resource.Asin,
                Title = resource.Title.CleanSpaces(),
                Language = resource.Language,
                Overview = resource.Description,
                Format = resource.Format,
                IsEbook = resource.IsEbook,
                Disambiguation = resource.EditionInformation,
                Publisher = resource.Publisher,
                PageCount = resource.NumPages ?? 0,
                ReleaseDate = resource.ReleaseDate,
                Ratings = new Ratings { Votes = resource.RatingCount, Value = (decimal)resource.AverageRating }
            };

            if (resource.ImageUrl.IsNotNullOrWhiteSpace())
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    Url = resource.ImageUrl,
                    CoverType = MediaCoverTypes.Cover
                });
            }

            edition.Links.Add(new Links { Url = resource.Url, Name = "Goodreads Book" });

            return edition;
        }

        private static int GetAuthorId(WorkResource b)
        {
            return b.Books.OrderByDescending(x => x.RatingCount * x.AverageRating).FirstOrDefault(x => x.Contributors.Any())?.Contributors.First().ForeignId ?? 0;
        }
    }
}
