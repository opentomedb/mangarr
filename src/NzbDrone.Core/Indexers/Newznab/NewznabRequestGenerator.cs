using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.Newznab
{
    public class NewznabRequestGenerator : IIndexerRequestGenerator
    {
        protected readonly INewznabCapabilitiesProvider _capabilitiesProvider;
        public int MaxPages { get; set; }
        public int PageSize { get; set; }
        public NewznabSettings Settings { get; set; }

        public NewznabRequestGenerator(INewznabCapabilitiesProvider capabilitiesProvider)
        {
            _capabilitiesProvider = capabilitiesProvider;

            MaxPages = 30;
            PageSize = 100;
        }

        private bool SupportsSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedSearchParameters != null &&
                       capabilities.SupportedSearchParameters.Contains("q");
            }
        }

        protected virtual bool SupportsBookSearch => false;

        // Light novels (2026-09): an Audio search uses the audio categories (3000-3999) only and
        // every other search the rest -- an indexer synced with both would return audiobooks for
        // every manga search and archives for every audiobook search. An indexer with no category
        // of the searched class gets NO request for that leg (GetPagedRequests yields nothing for
        // an empty list, as for an empty category list today) -- never the other class's
        // categories, whose results the regrade would type as the searched class. RSS
        // (GetRecentRequests) keeps every category.
        private IEnumerable<int> CategoriesFor(SearchCriteriaBase searchCriteria)
        {
            var audio = searchCriteria?.MediaType == MediaType.Audio;
            var categories = Settings.Categories.Where(c => MediaTypes.IsAudioCategory(c) == audio).ToList();

            // Torrent trackers file audiobooks under Books (Nyaa's Literature; 2026-09-21: "Rascal Does
            // Not Dream Series v01-16 [Audiobook]" only ever surfaced in an EPUB search): a light-novel
            // audio search on an indexer with no audio category asks its book categories rather than
            // nothing. The audiobook wording / regrade types what comes back; an ebook-only download
            // is still refused at import.
            if (audio && categories.Count == 0 && searchCriteria?.Author?.Library == LibraryType.LightNovel)
            {
                categories = Settings.Categories.ToList();
            }

            return categories;
        }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

            if (capabilities.SupportedBookSearchParameters != null)
            {
                pageableRequests.Add(GetPagedRequests(MaxPages, Settings.Categories, "book", ""));
            }
            else if (capabilities.SupportedSearchParameters != null)
            {
                pageableRequests.Add(GetPagedRequests(MaxPages, Settings.Categories, "search", ""));
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            if (SupportsBookSearch)
            {
                AddBookPageableRequests(pageableRequests,
                    searchCriteria,
                    $"&author={NewsnabifyTitle(searchCriteria.AuthorQuery)}&title={NewsnabifyTitle(searchCriteria.FieldedTitleQuery)}");

                AddBookPageableRequests(pageableRequests,
                    searchCriteria,
                    $"&title={NewsnabifyTitle(searchCriteria.FieldedTitleQuery)}");
            }

            if (SupportsSearch)
            {
                // Manga (VolumeNumber > 0): BookQuery already embeds the series name, so the
                // author-augmented forms below would send "Series vNN Series" — and since the
                // search stops at the first tier returning results, that duplicated tier
                // suppressed the clean and alias tiers on loose-matching indexers. Skip it.
                if (searchCriteria.VolumeNumber <= 0)
                {
                    pageableRequests.AddTier();

                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        CategoriesFor(searchCriteria),
                        "search",
                        $"&q={NewsnabifyTitle(searchCriteria.BookQuery)}+{NewsnabifyTitle(searchCriteria.AuthorQuery)}"));

                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        CategoriesFor(searchCriteria),
                        "search",
                        $"&q={NewsnabifyTitle(searchCriteria.AuthorQuery)}+{NewsnabifyTitle(searchCriteria.BookQuery)}"));
                }

                // Light-novel audio (2026-09-18, D4): the subtitle tier -- "<Series> <N>: <Subtitle>"
                // and the Audible title -- is how audiobook releases are actually named, so it goes
                // first and demotes BookQuery to the second tier for LN audio only (SubtitleQueries is
                // empty for every other search, so manga tiers are byte-identical). Tiers are
                // fallbacks: the search stops at the first tier with results.
                if (searchCriteria.SubtitleQueries.Any())
                {
                    pageableRequests.AddTier();

                    foreach (var subtitleQuery in searchCriteria.SubtitleQueries)
                    {
                        pageableRequests.Add(GetPagedRequests(MaxPages,
                            CategoriesFor(searchCriteria),
                            "search",
                            $"&q={NewsnabifyTitle(subtitleQuery)}"));
                    }
                }

                pageableRequests.AddTier();

                pageableRequests.Add(GetPagedRequests(MaxPages,
                    CategoriesFor(searchCriteria),
                    "search",
                    $"&q={NewsnabifyTitle(searchCriteria.BookQuery)}"));

                // Manga recall variant: also try the unpadded volume form ("v5" vs "v05").
                if (searchCriteria.BookQueryAlt.IsNotNullOrWhiteSpace())
                {
                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        CategoriesFor(searchCriteria),
                        "search",
                        $"&q={NewsnabifyTitle(searchCriteria.BookQueryAlt)}"));
                }

                // Recall tier — fires only when everything above found nothing (tiers are
                // fallbacks): the bare-number form ("Fire Force 01") and alternate-title
                // variants (romaji / licensed names releases are actually tagged with).
                var recallQueries = new List<string>();

                if (searchCriteria.BookQueryBare.IsNotNullOrWhiteSpace())
                {
                    recallQueries.Add(searchCriteria.BookQueryBare);
                }

                recallQueries.AddRange(searchCriteria.AliasQueries);

                if (recallQueries.Any())
                {
                    pageableRequests.AddTier();

                    foreach (var recallQuery in recallQueries)
                    {
                        pageableRequests.Add(GetPagedRequests(MaxPages,
                            CategoriesFor(searchCriteria),
                            "search",
                            $"&q={NewsnabifyTitle(recallQuery)}"));
                    }
                }
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(AuthorSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            if (SupportsBookSearch)
            {
                AddBookPageableRequests(pageableRequests,
                    searchCriteria,
                    $"&author={NewsnabifyTitle(searchCriteria.AuthorQuery)}");
            }

            if (SupportsSearch)
            {
                pageableRequests.AddTier();

                pageableRequests.Add(GetPagedRequests(MaxPages,
                    CategoriesFor(searchCriteria),
                    "search",
                    $"&q={NewsnabifyTitle(searchCriteria.AuthorQuery)}"));
            }

            return pageableRequests;
        }

        private void AddBookPageableRequests(IndexerPageableRequestChain chain, SearchCriteriaBase searchCriteria, string parameters)
        {
            chain.AddTier();

            chain.Add(GetPagedRequests(MaxPages, CategoriesFor(searchCriteria), "book", $"{parameters}"));
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(int maxPages, IEnumerable<int> categories, string searchType, string parameters)
        {
            if (categories.Empty())
            {
                yield break;
            }

            var categoriesQuery = string.Join(",", categories.Distinct());

            var baseUrl =
                $"{Settings.BaseUrl.TrimEnd('/')}{Settings.ApiPath.TrimEnd('/')}?t={searchType}&cat={categoriesQuery}&extended=1{Settings.AdditionalParameters}";

            if (Settings.ApiKey.IsNotNullOrWhiteSpace())
            {
                baseUrl += "&apikey=" + Settings.ApiKey;
            }

            if (PageSize == 0)
            {
                yield return new IndexerRequest($"{baseUrl}{parameters}", HttpAccept.Rss);
            }
            else
            {
                for (var page = 0; page < maxPages; page++)
                {
                    yield return new IndexerRequest($"{baseUrl}&offset={page * PageSize}&limit={PageSize}{parameters}",
                        HttpAccept.Rss);
                }
            }
        }

        private static string NewsnabifyTitle(string title)
        {
            title = title.Replace("+", " ");
            return Uri.EscapeDataString(title);
        }
    }
}
