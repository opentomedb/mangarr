using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using Readarr.Http;

namespace Readarr.Api.V1.PreferredEdition
{
    // Preferred Edition (2026-09-24). GET markets: the languages the loaded catalogue has lines in (its
    // meta 'markets', else counted) with line counts, English always listed -- the Settings -> UI list
    // editor's choices -- plus how many series are bound to another edition (the note linking to the
    // series list's Change Edition). A GET of its own, never a UiConfigResource property: SaveConfig
    // writes every resource property back. Named PreferredEditionController because Editions/ already
    // has an EditionController on api/v1/edition; the routes don't overlap (that one is a bare GET and
    // PUT {id:int}).
    [V1ApiController("edition")]
    public class PreferredEditionController : Controller
    {
        private readonly IGcdMetadataService _gcdMetadataService;
        private readonly IAuthorService _authorService;
        private readonly IMangaSeriesMetadataProvider _mangaMetadataProvider;
        private readonly IEditionPreviewService _previewService;
        private readonly IServerMessageLocalizer _messages;

        public PreferredEditionController(IGcdMetadataService gcdMetadataService,
                                          IAuthorService authorService,
                                          IMangaSeriesMetadataProvider mangaMetadataProvider,
                                          IEditionPreviewService previewService,
                                          IServerMessageLocalizer messages)
        {
            _gcdMetadataService = gcdMetadataService;
            _authorService = authorService;
            _mangaMetadataProvider = mangaMetadataProvider;
            _previewService = previewService;
            _messages = messages;
        }

        public class AuthorEditionsResource
        {
            public string Current { get; set; }
            public List<EditionOption> Options { get; set; }
        }

        public class EditionPreviewRequestResource
        {
            public List<int> AuthorIds { get; set; }
            public string Language { get; set; }
        }

        [HttpGet("markets")]
        public EditionMarketsResource GetMarkets()
        {
            var markets = _gcdMetadataService.Markets();

            var languages = markets.Keys
                .Union(new[] { EditionLanguages.English })
                .OrderBy(l => EditionLanguages.IsEnglish(l) ? string.Empty : EditionLanguages.Name(l))
                .Select(l => new EditionMarketResource
                {
                    Language = l,
                    Name = EditionLanguages.Name(l),
                    Lines = markets.TryGetValue(l, out var lines) ? lines : 0
                })
                .ToList();

            // Kept cheap: the cached author list only, never a catalogue read per series.
            var otherEdition = _authorService.GetAllAuthors()
                .Where(a => !EditionLanguages.IsEnglish(a.Metadata?.Value?.EditionLanguage))
                .ToList();

            return new EditionMarketsResource
            {
                Languages = languages,
                OtherEditionSeries = otherEdition.Count,
                OtherEditionManga = otherEdition.Count(a => a.Library == LibraryType.Manga),
                OtherEditionLightNovels = otherEdition.Count(a => a.Library == LibraryType.LightNovel)
            };
        }

        // Preferred Edition (2026-09-24, M13). Edit Series: the series' edition and the languages its work
        // has a line in (empty until a refresh has bound its line). Concrete languages only -- no "Auto"
        // (plan A10: an existing series has an edition, it does not follow the chain).
        [HttpGet("author/{id:int}")]
        public AuthorEditionsResource GetAuthorEditions(int id)
        {
            var author = _authorService.GetAuthor(id);

            return new AuthorEditionsResource
            {
                Current = EditionLanguages.Of(author.Metadata.Value),
                Options = _mangaMetadataProvider.EditionOptions(author.Metadata.Value.TomeLineId, author.Library) ?? new List<EditionOption>()
            };
        }

        // The Change Edition preview for one or many series (spec §4). Fix round 1: a series deleted since
        // the list was loaded is left out, not a failed POST for the whole selection.
        [HttpPost("preview")]
        public List<EditionPreview> Preview([FromBody] EditionPreviewRequestResource request)
        {
            var previews = new List<EditionPreview>();

            foreach (var id in request?.AuthorIds ?? new List<int>())
            {
                NzbDrone.Core.Books.Author author;

                try
                {
                    author = _authorService.GetAuthor(id);
                }
                catch (ModelNotFoundException)
                {
                    continue;
                }

                if (author == null)
                {
                    continue;
                }

                var preview = _previewService.Preview(author, request.Language);

                // Polish: the series list never renames (plan A7 / bulk), so a rename-only row there is a no-op
                // -- shown blocked, never as an eligible row that Apply would skip.
                if (request.AuthorIds.Count > 1 && preview.RenameOnly && preview.BlockedReason == null)
                {
                    preview.BlockedReason = $"Already the {EditionLanguages.Name(preview.ToLanguage)} edition (rename from Edit Series)";
                }

                previews.Add(preview.ToResource(_messages));
            }

            return previews;
        }
    }

    // i18n leftovers (2026-09-28): EditionPreview doubles as its own API resource (no separate Resource
    // type), so this is its "map to a copy, localize BlockedReason" step -- the same thing QueueResourceMapper
    // does for StatusMessages/ErrorMessage. BlockedReasonText is null for a reason built elsewhere
    // (EditionChangeChecks, or the "(rename from Edit Series)" suffix above): Localize falls back to English
    // there, unchanged from before this round.
    public static class EditionPreviewResourceMapper
    {
        public static EditionPreview ToResource(this EditionPreview preview, IServerMessageLocalizer messages)
        {
            if (preview == null)
            {
                return null;
            }

            return new EditionPreview
            {
                AuthorId = preview.AuthorId,
                Name = preview.Name,
                FromLanguage = preview.FromLanguage,
                ToLanguage = preview.ToLanguage,
                FromLineName = preview.FromLineName,
                ToLineName = preview.ToLineName,
                FromVolumes = preview.FromVolumes,
                ToVolumes = preview.ToVolumes,
                Compatible = preview.Compatible,
                BlockedReason = messages.Localize(preview.BlockedReason, preview.BlockedReasonText),
                FilesAffected = preview.FilesAffected,
                NewName = preview.NewName,
                ToTomeLineId = preview.ToTomeLineId,
                RenameBlockedReason = preview.RenameBlockedReason,
                RenameOnly = preview.RenameOnly
            };
        }
    }
}
