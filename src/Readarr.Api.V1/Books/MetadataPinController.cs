using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Messaging.Commands;
using Readarr.Http;

namespace Readarr.Api.V1.Books
{
    // Metadata pins: per-volume values that win over every provider and every plausibility
    // guard, stored in <appdata>/metadata/overrides.json.
    //
    //   GET    /api/v1/metadatapin?authorId=7        every pin for that series
    //   PUT    /api/v1/metadatapin                   set one (body: MetadataPinResource)
    //   DELETE /api/v1/metadatapin?authorId=7&volumeNumber=10
    //
    // Writing a pin queues a ReResolveMetadata for the series, so the value appears without
    // the user having to know that a second, separate action exists.
    [V1ApiController("metadatapin")]
    public class MetadataPinController : Controller
    {
        private readonly IMetadataOverridesService _overrides;
        private readonly IAuthorService _authorService;
        private readonly IManageCommandQueue _commandQueue;

        public MetadataPinController(IMetadataOverridesService overrides,
                                     IAuthorService authorService,
                                     IManageCommandQueue commandQueue)
        {
            _overrides = overrides;
            _authorService = authorService;
            _commandQueue = commandQueue;
        }

        [HttpGet]
        public List<MetadataPinResource> GetPins(int authorId)
        {
            var name = SeriesName(authorId);

            return _overrides.Get(name)
                .Select(kv => MetadataPinResource.From(authorId, name, kv.Key, kv.Value))
                .OrderBy(p => p.SortKey)
                .ToList();
        }

        [HttpPut]
        public ActionResult<MetadataPinResource> SetPin([FromBody] MetadataPinResource resource)
        {
            if (resource == null || resource.VolumeNumber.IsNullOrWhiteSpace())
            {
                return BadRequest("volumeNumber is required");
            }

            // The series row (one copy each, 2026-09-20) carries the light novel's author and nothing
            // else: a volume value there would be written and never applied (Apply keys by volume number).
            if (resource.VolumeNumber.Trim() == MetadataOverridesService.SeriesKey &&
                (resource.ReleaseDate.IsNotNullOrWhiteSpace() || resource.PageCount > 0 || resource.Isbn13.IsNotNullOrWhiteSpace() ||
                 resource.CoverUrl.IsNotNullOrWhiteSpace() || resource.AudiobookTitle.IsNotNullOrWhiteSpace()))
            {
                return BadRequest("the series row pins the author only");
            }

            if (resource.CoverUrl.IsNotNullOrWhiteSpace() && !VolumeOverride.IsCoverUrl(resource.CoverUrl.Trim()))
            {
                return BadRequest("coverUrl must be an http(s) URL");
            }

            var name = SeriesName(resource.AuthorId);
            _overrides.Set(name, resource.VolumeNumber, resource.ToOverride());
            ReResolve(resource.AuthorId);

            resource.SeriesName = name;
            return Accepted(resource);
        }

        [HttpDelete]
        public ActionResult DeletePin(int authorId, string volumeNumber)
        {
            if (volumeNumber.IsNullOrWhiteSpace())
            {
                return BadRequest("volumeNumber is required");
            }

            _overrides.Remove(SeriesName(authorId), volumeNumber);
            ReResolve(authorId);

            return Ok();
        }

        // Pins are keyed by the series' DISPLAY name, which is what the resolver looks up. The
        // light-novel line of a name is a different bucket ("<name> (light novel)") from the
        // manga line's; see LibraryTypes.PinKey.
        private string SeriesName(int authorId)
        {
            var author = _authorService.GetAuthor(authorId);
            return LibraryTypes.PinKey(author.Name ?? author.Metadata?.Value?.Name, author.Library);
        }

        private void ReResolve(int authorId)
        {
            _commandQueue.Push(new ReResolveMetadataCommand(authorId));
        }
    }

    public class MetadataPinResource
    {
        public int AuthorId { get; set; }
        public string SeriesName { get; set; }
        public string VolumeNumber { get; set; }
        public string ReleaseDate { get; set; }
        public int? PageCount { get; set; }
        public string Isbn13 { get; set; }
        public string CoverUrl { get; set; }   // B3b (2026-09-18): the volume's cover pin, round-tripped so a UI save keeps it
        public string AudiobookTitle { get; set; }   // 2026-09-19: the Audible product title pin, round-tripped the same way
        public string Author { get; set; }   // one copy each (2026-09-20): the light novel's author, on the series row ("*") only

        // Where the value came from. Not used by the resolver — a pin without a source is a
        // guess, and a pin that carries one can be promoted into an OpenTome correction
        // (corrections/volumes.json takes the same three fields).
        public string Source { get; set; }
        public string Checked { get; set; }

        public double SortKey =>
            double.TryParse(VolumeNumber, out var n) ? n : double.MaxValue;

        public static MetadataPinResource From(int authorId, string seriesName, string volume, VolumeOverride pin)
        {
            return new MetadataPinResource
            {
                AuthorId = authorId,
                SeriesName = seriesName,
                VolumeNumber = volume,
                ReleaseDate = pin.ReleaseDate,
                PageCount = pin.PageCount,
                Isbn13 = pin.Isbn13,
                CoverUrl = pin.CoverUrl,
                AudiobookTitle = pin.AudiobookTitle,
                Author = pin.Author,
                Source = pin.Source,
                Checked = pin.Checked
            };
        }

        public VolumeOverride ToOverride()
        {
            return new VolumeOverride
            {
                ReleaseDate = ReleaseDate.IsNullOrWhiteSpace() ? null : ReleaseDate.Trim(),
                PageCount = PageCount.HasValue && PageCount.Value > 0 ? PageCount : null,
                Isbn13 = Isbn13.IsNullOrWhiteSpace() ? null : Isbn13.Trim(),
                CoverUrl = CoverUrl.IsNullOrWhiteSpace() ? null : CoverUrl.Trim(),
                AudiobookTitle = AudiobookTitle.IsNullOrWhiteSpace() ? null : AudiobookTitle.Trim(),
                Author = Author.IsNullOrWhiteSpace() ? null : Author.Trim(),
                Source = Source.IsNullOrWhiteSpace() ? null : Source.Trim(),
                Checked = Checked.IsNullOrWhiteSpace()
                    ? DateTime.UtcNow.ToString("yyyy-MM-dd")
                    : Checked.Trim()
            };
        }
    }
}
