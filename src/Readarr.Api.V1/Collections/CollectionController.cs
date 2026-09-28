using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Collections;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.RootFolders;
using Readarr.Http;

namespace Readarr.Api.V1.Collections
{
    // Collections: the library's series grouped by the line they belong to, per the metadata
    // artifact's parent_series_id (an arc under its parent line, a side story under the main line).
    //
    //   GET /api/v1/collection     every collection touching the library: its root, the members
    //                              already in the library, and the members the artifact knows
    //                              that are not (with what is needed to add them)
    //
    // Read-only. Adding members goes through POST /api/v1/author/import (the standard bulk add);
    // searching goes through the MissingBookSearch command per member -- both existing paths.
    [V1ApiController("collection")]
    public class CollectionController : Controller
    {
        private readonly IAuthorService _authorService;
        private readonly IGcdMetadataService _gcd;
        private readonly ICollectionPosterService _posters;
        private readonly IRootFolderService _rootFolderService;

        public CollectionController(IAuthorService authorService,
                                    IGcdMetadataService gcdMetadataService,
                                    IRootFolderService rootFolderService,
                                    ICollectionPosterService collectionPosterService)
        {
            _authorService = authorService;
            _gcd = gcdMetadataService;
            _posters = collectionPosterService;
            _rootFolderService = rootFolderService;
        }

        [HttpGet]
        public List<CollectionResource> All()
        {
            var authors = _authorService.GetAllAuthors();

            // A library series is found by the id this app would give the name IN ITS LIBRARY, else
            // by normalized name or alias in that library -- legacy manga entries carry an id slugged
            // from the search term, not the name. The manga and the light novel of one name are
            // different entries, so both maps carry the library.
            var byId = new Dictionary<string, NzbDrone.Core.Books.Author>();
            var byName = new Dictionary<(string Name, LibraryType Library), NzbDrone.Core.Books.Author>();
            foreach (var a in authors)
            {
                byId[a.Metadata.Value.ForeignAuthorId] = a;
                byName[(GcdMetadataService.Normalize(a.Name), a.Library)] = a;
                foreach (var alias in a.Metadata.Value.Aliases ?? new List<string>())
                {
                    byName.TryAdd((GcdMetadataService.Normalize(alias), a.Library), a);
                }
            }

            NzbDrone.Core.Books.Author Find(string name, LibraryType library)
            {
                if (name.IsNullOrWhiteSpace())
                {
                    return null;
                }

                return byId.GetValueOrDefault(BookInfoProxy.ForeignAuthorIdFor(name, library))
                    ?? byName.GetValueOrDefault((GcdMetadataService.Normalize(name), library));
            }

            var collections = new Dictionary<string, CollectionResource>();

            // One collection per (line, library): the manga "Sword Art Online" collection and the
            // light-novel one are separate, keyed by the root's id in that library.
            CollectionResource Collection(string parentName, LibraryType library)
            {
                var root = Find(parentName, library);
                var key = root?.Metadata.Value.ForeignAuthorId ?? BookInfoProxy.ForeignAuthorIdFor(parentName, library);
                if (collections.TryGetValue(key, out var existing))
                {
                    return existing;
                }

                var c = new CollectionResource
                {
                    ForeignAuthorId = key,
                    Name = root?.Name ?? parentName,
                    Library = library,
                    AuthorId = root?.Id,
                    TitleSlug = root?.Metadata.Value.TitleSlug,
                    Members = new List<CollectionMemberResource>()
                };
                collections[key] = c;

                // Every member the artifact knows in this library's medium, in library or not.
                var line = _gcd.Available
                    ? (_gcd.FindSeriesByTitle(root?.Name, library) ?? _gcd.FindSeriesByTitle(parentName, library))
                    : null;
                if (line != null)
                {
                    // A line outside the library has no MediaCover: the catalogue / AniList poster
                    // stands in (cached), so the card is not a placeholder.
                    if (root == null)
                    {
                        c.RemotePoster = _posters.PosterFor(line);
                    }

                    foreach (var child in _gcd.GetChildren(line.GcdSeriesId))
                    {
                        var member = Find(child.Name, library);
                        AddMember(c, child.Name, member, child, member == null ? _posters.PosterFor(child) : null);
                    }
                }

                return c;
            }

            // 1. every library series that says what it is part of
            foreach (var a in authors.Where(x => x.Metadata.Value.ParentName.IsNotNullOrWhiteSpace()))
            {
                AddMember(Collection(a.Metadata.Value.ParentName, a.Library), a.Name, a, null);
            }

            // 2. every library series that is itself the top of a collection
            if (_gcd.Available)
            {
                foreach (var a in authors)
                {
                    var line = _gcd.FindSeriesByTitle(a.Name, a.Library);
                    if (line != null && _gcd.GetChildren(line.GcdSeriesId).Any())
                    {
                        Collection(a.Name, a.Library);
                    }
                }
            }

            var rootFolders = _rootFolderService.All();
            foreach (var c in collections.Values)
            {
                c.Members = c.Members.OrderBy(m => !m.InLibrary).ThenBy(m => m.Name).ToList();
                var sample = c.AuthorId.HasValue
                    ? authors.First(a => a.Id == c.AuthorId.Value)
                    : authors.FirstOrDefault(a => c.Members.Any(m => m.AuthorId == a.Id));
                if (sample != null)
                {
                    c.Defaults = new CollectionDefaultsResource
                    {
                        RootFolderPath = _rootFolderService.GetBestRootFolderPath(sample.Path, rootFolders),
                        QualityProfileId = sample.QualityProfileId,
                        MetadataProfileId = sample.MetadataProfileId,
                        MonitorNewItems = sample.MonitorNewItems.ToString().ToLowerInvariant()
                    };
                }
            }

            return collections.Values.OrderBy(c => c.Name).ToList();
        }

        private static void AddMember(CollectionResource c, string name, NzbDrone.Core.Books.Author author, GcdSeries line, string remotePoster = null)
        {
            // Members are keyed in the collection's library, so "Add missing" on a light-novel
            // collection posts "~ln" ids and the adds resolve as light novels.
            var foreignId = author?.Metadata.Value.ForeignAuthorId ?? BookInfoProxy.ForeignAuthorIdFor(name, c.Library);
            var member = c.Members.FirstOrDefault(m => m.ForeignAuthorId == foreignId);
            if (member == null)
            {
                member = new CollectionMemberResource { ForeignAuthorId = foreignId };
                c.Members.Add(member);
            }

            member.Name = author?.Name ?? member.Name ?? name;
            member.InLibrary = author != null;
            member.AuthorId = author?.Id;
            member.TitleSlug = author?.Metadata.Value.TitleSlug;
            if (line != null)
            {
                member.Medium = line.Medium;
                member.VolumeCount = line.VolumeCount;
                member.Publisher = line.Publisher;
            }

            if (remotePoster != null)
            {
                member.RemotePoster = remotePoster;
            }
        }
    }

    public class CollectionResource
    {
        public string ForeignAuthorId { get; set; }
        public string Name { get; set; }
        public LibraryType Library { get; set; }
        public int? AuthorId { get; set; }          // the root series' library id, when it is in the library
        public string TitleSlug { get; set; }
        public string RemotePoster { get; set; }    // the root's catalogue / AniList cover when it is NOT in the library
        public CollectionDefaultsResource Defaults { get; set; }
        public List<CollectionMemberResource> Members { get; set; }
    }

    public class CollectionMemberResource
    {
        public string ForeignAuthorId { get; set; }
        public string Name { get; set; }
        public int? AuthorId { get; set; }
        public string TitleSlug { get; set; }
        public bool InLibrary { get; set; }
        public string Medium { get; set; }
        public int VolumeCount { get; set; }
        public string Publisher { get; set; }
        public string RemotePoster { get; set; }    // catalogue / AniList cover for a member NOT in the library
    }

    // What "add the missing members" should use: the root folder and profiles of a sibling
    // that is already in the library, so an arc lands next to its parent.
    public class CollectionDefaultsResource
    {
        public string RootFolderPath { get; set; }
        public int QualityProfileId { get; set; }
        public int MetadataProfileId { get; set; }
        public string MonitorNewItems { get; set; }
    }
}
