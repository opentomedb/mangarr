using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Books
{
    public interface IAddAuthorService
    {
        Author AddAuthor(Author newAuthor, bool doRefresh = true);
        List<Author> AddAuthors(List<Author> newAuthors, bool doRefresh = true);
    }

    public class AddAuthorService : IAddAuthorService
    {
        private readonly IAuthorService _authorService;
        private readonly IAuthorMetadataService _authorMetadataService;
        private readonly IProvideAuthorInfo _authorInfo;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IAddAuthorValidator _addAuthorValidator;
        private readonly IRootFolderService _rootFolderService;
        private readonly Logger _logger;

        public AddAuthorService(IAuthorService authorService,
                                IAuthorMetadataService authorMetadataService,
                                IProvideAuthorInfo authorInfo,
                                IBuildFileNames fileNameBuilder,
                                IAddAuthorValidator addAuthorValidator,
                                IRootFolderService rootFolderService,
                                Logger logger)
        {
            _authorService = authorService;
            _authorMetadataService = authorMetadataService;
            _authorInfo = authorInfo;
            _fileNameBuilder = fileNameBuilder;
            _addAuthorValidator = addAuthorValidator;
            _rootFolderService = rootFolderService;
            _logger = logger;
        }

        public Author AddAuthor(Author newAuthor, bool doRefresh = true)
        {
            Ensure.That(newAuthor, () => newAuthor).IsNotNull();

            // Reject re-adds before the remote resolve: a duplicate POST otherwise spends the
            // full skyhook round-trip and holds DB writers mid-add, and a client retrying a
            // slow add can race the guard in SetPropertiesAndValidate (2026-08-06 Marriagetoxin
            // double-add). Same rejection, just before the expensive work; the post-resolve
            // guard stays as backstop for name variants only discovered during the resolve.
            var existing = _authorService.FindById(newAuthor.Metadata.Value.ForeignAuthorId)
                           ?? _authorService.FindByName(newAuthor.Metadata.Value.Name, newAuthor.Library);
            if (existing != null)
            {
                throw new ValidationException(new List<ValidationFailure>
                {
                    new NzbDroneValidationFailure("Name", new ServerText("This series has already been added as '{0}'", existing.Name))
                });
            }

            newAuthor = AddSkyhookData(newAuthor);
            newAuthor = SetPropertiesAndValidate(newAuthor);

            _logger.Info("Adding Author {0} Path: [{1}]", newAuthor, newAuthor.Path);

            // add metadata
            _authorMetadataService.Upsert(newAuthor.Metadata.Value);
            newAuthor.AuthorMetadataId = newAuthor.Metadata.Value.Id;

            // add the author itself
            return _authorService.AddAuthor(newAuthor, doRefresh);
        }

        public List<Author> AddAuthors(List<Author> newAuthors, bool doRefresh = true)
        {
            var added = DateTime.UtcNow;
            var authorsToAdd = new List<Author>();
            var cleanNamesInBatch = new HashSet<string>();

            foreach (var s in newAuthors)
            {
                try
                {
                    var author = AddSkyhookData(s);
                    author = SetPropertiesAndValidate(author);
                    author.Added = added;

                    // SetPropertiesAndValidate only probes the DB, so two items in the same
                    // batch that clean to the same name would both pass — and InsertMany is
                    // transactional, so one IX_Authors_CleanName violation would abort the
                    // whole batch. Keep the first, skip the rest.
                    if (!cleanNamesInBatch.Add(author.CleanName))
                    {
                        _logger.Warn("Skipping duplicate series in add batch: {0} ({1})", author.Name, author.CleanName);
                        continue;
                    }

                    authorsToAdd.Add(author);
                }
                catch (Exception ex)
                {
                    // Catch Import Errors for now until we get things fixed up
                    _logger.Error(ex, "Failed to import id: {0} - {1}", s.Metadata.Value.ForeignAuthorId, s.Metadata.Value.Name);
                }
            }

            // add metadata
            _authorMetadataService.UpsertMany(authorsToAdd.Select(x => x.Metadata.Value).ToList());
            authorsToAdd.ForEach(x => x.AuthorMetadataId = x.Metadata.Value.Id);

            return _authorService.AddAuthors(authorsToAdd, doRefresh);
        }

        private Author AddSkyhookData(Author newAuthor)
        {
            Author author;

            try
            {
                // Build the volume STRUCTURE only (no per-volume metadata HTTP) so the add returns fast;
                // the post-add refresh imports files immediately and queues a background full resolve.
                // Preferred Edition (2026-09-24): the Add form's edition rides in AddOptions (null = Auto,
                // the chain -- today's three-argument call).
                var requestedEdition = newAuthor.AddOptions?.EditionLanguage;
                // Staging fix S1 (2026-10-01, spec §3.2): an Auto add of a fallback candidate carries the line the
                // search chose (EditionFallback + TomeLineId + EditionLanguage in the posted metadata); the resolve
                // binds it by id, since the work's name may not find the line again.
                var posted = newAuthor.Metadata?.Value;
                var carriesFallbackLine = requestedEdition.IsNullOrWhiteSpace() && posted != null && posted.EditionFallback &&
                    posted.TomeLineId.IsNotNullOrWhiteSpace() && posted.EditionLanguage.IsNotNullOrWhiteSpace();

                if (carriesFallbackLine)
                {
                    author = _authorInfo.GetAuthorInfo(posted.ForeignAuthorId, false, false, posted.EditionLanguage, posted.TomeLineId);
                }
                else
                {
                    author = requestedEdition.IsNullOrWhiteSpace()
                        ? _authorInfo.GetAuthorInfo(newAuthor.Metadata.Value.ForeignAuthorId, false, resolveVolumeDetails: false)
                        : _authorInfo.GetAuthorInfo(newAuthor.Metadata.Value.ForeignAuthorId, false, false, requestedEdition);
                }
            }
            catch (NotInCatalogueException ex)
            {
                // Light novels are catalogue-only (D11): refuse, and let the UI point at OpenTome.
                // Preferred Edition (2026-09-24, spec §2.3): a chain request that found no novel line in
                // any of its languages says so; the English refusal text is unchanged.
                throw new ValidationException(new List<ValidationFailure>
                {
                    ex.NoChainLine
                        ? new ValidationFailure("ForeignAuthorId", "No novel line in any chain language", newAuthor.Metadata.Value.ForeignAuthorId)
                        : new ValidationFailure("ForeignAuthorId", "Not in the catalogue as a light novel yet", newAuthor.Metadata.Value.ForeignAuthorId)
                });
            }
            catch (EditionUnavailableException ex)
            {
                throw new ValidationException(new List<ValidationFailure>
                {
                    new NzbDroneValidationFailure("AddOptions.EditionLanguage", new ServerText("No {0} edition of this series in the catalogue", new ServerText(EditionLanguages.Name(ex.Language))))
                    {
                        AttemptedValue = ex.Language
                    }
                });
            }
            catch (AuthorNotFoundException)
            {
                _logger.Error("MangarrId {0} was not found, it may have been removed from Goodreads.", newAuthor.Metadata.Value.ForeignAuthorId);

                throw new ValidationException(new List<ValidationFailure>
                {
                    new ValidationFailure("ForeignAuthorId", "A series with this ID was not found", newAuthor.Metadata.Value.ForeignAuthorId)
                });
            }

            author.ApplyChanges(newAuthor);

            return author;
        }

        private Author SetPropertiesAndValidate(Author newAuthor)
        {
            var path = newAuthor.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                var folderName = _fileNameBuilder.GetAuthorFolder(newAuthor);
                path = Path.Combine(newAuthor.RootFolderPath, folderName);
            }

            // Disambiguate author path if it exists already
            if (_authorService.AuthorPathExists(path))
            {
                if (newAuthor.Metadata.Value.Disambiguation.IsNotNullOrWhiteSpace())
                {
                    path += $" ({newAuthor.Metadata.Value.Disambiguation})";
                }

                if (_authorService.AuthorPathExists(path))
                {
                    var basepath = path;
                    var i = 0;
                    do
                    {
                        i++;
                        path = basepath + $" ({i})";
                    }
                    while (_authorService.AuthorPathExists(path));
                }
            }

            newAuthor.Path = path;

            // The root folder's default tags (Settings → Media Management → root folder) apply to a
            // series ADDED there too, not only to one detected on disk: a light novel added to
            // /lightnovels carries the tag that scopes a rate-limited indexer to it (2026-09-22,
            // Classroom of the Elite was added without the private tracker's tag and never searched there).
            var rootFolder = _rootFolderService.GetBestRootFolder(path);
            if (rootFolder?.DefaultTags != null && rootFolder.DefaultTags.Any())
            {
                newAuthor.Tags = new HashSet<int>((newAuthor.Tags ?? new HashSet<int>()).Union(rootFolder.DefaultTags));
            }

            newAuthor.CleanName = LibraryTypes.CleanNameFor(newAuthor.Metadata.Value.Name.CleanAuthorName(), newAuthor.Library);
            newAuthor.Added = DateTime.UtcNow;

            // Title-variant dedup: the foreignAuthorId is slugified from the search term, so the
            // same series searched two different ways ("re:zero" vs its full arc title) yields two
            // different ids and the id-keyed dedup misses it. Guard on the normalized title instead
            // (CleanName) so a series that already exists can't be re-added under a variant id.
            // CleanAuthorName strips punctuation/articles, so genuinely distinct works (Re:ZERO
            // Chapter 1 vs Chapter 3, "My Hero Academia" vs "...: Vigilantes") stay separate.
            // Per library: the manga and the light novel of one name are different entries.
            var existingByName = _authorService.FindByName(newAuthor.Metadata.Value.Name, newAuthor.Library);
            if (existingByName != null)
            {
                _logger.Debug("Series '{0}' already exists as '{1}' ({2}); rejecting duplicate add",
                    newAuthor.Metadata.Value.Name, existingByName.Name, existingByName.Metadata.Value.ForeignAuthorId);

                throw new ValidationException(new List<ValidationFailure>
                {
                    new NzbDroneValidationFailure("Name", new ServerText("This series has already been added as '{0}'", existingByName.Name))
                });
            }

            if (newAuthor.AddOptions != null && newAuthor.AddOptions.Monitor == MonitorTypes.None)
            {
                newAuthor.Monitored = false;
            }

            var validationResult = _addAuthorValidator.Validate(newAuthor);

            if (!validationResult.IsValid)
            {
                throw new ValidationException(validationResult.Errors);
            }

            return newAuthor;
        }
    }
}
