using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books
{
    public class ReResolveMetadataService : IExecute<ReResolveMetadataCommand>
    {
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IGoogleBooksService _googleBooksService;
        private readonly IAuthorService _authorService;
        private readonly IAuthorMetadataService _authorMetadataService;
        private readonly IMangaSeriesMetadataProvider _mangaMetadataProvider;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public ReResolveMetadataService(IBookService bookService,
                                        IEditionService editionService,
                                        IGoogleBooksService googleBooksService,
                                        IAuthorService authorService,
                                        IAuthorMetadataService authorMetadataService,
                                        IMangaSeriesMetadataProvider mangaMetadataProvider,
                                        IManageCommandQueue commandQueueManager,
                                        Logger logger)
        {
            _bookService = bookService;
            _editionService = editionService;
            _googleBooksService = googleBooksService;
            _authorService = authorService;
            _authorMetadataService = authorMetadataService;
            _mangaMetadataProvider = mangaMetadataProvider;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Execute(ReResolveMetadataCommand message)
        {
            if (message.Rebind)
            {
                Rebind(message.AuthorId);
                return;
            }

            if (!message.AuthorId.HasValue)
            {
                _logger.Warn("ReResolveMetadata needs an author id unless rebind is set; nothing to do");
                return;
            }

            var books = message.BookId.HasValue
                ? new List<Book> { _bookService.GetBook(message.BookId.Value) }
                : _bookService.GetBooksByAuthor(message.AuthorId.Value);

            _logger.Info("Re-resolving metadata for {0} book(s), clearing stored dates/pages first", books.Count);

            foreach (var book in books)
            {
                book.ReleaseDate = null;
            }

            _bookService.UpdateMany(books);

            var editions = _editionService.GetEditionsByBook(books.Select(b => b.Id));

            foreach (var edition in editions)
            {
                edition.ReleaseDate = null;
                edition.PageCount = 0;
            }

            _editionService.UpdateMany(editions);

            // Fresh answers, not the 7-day cache's copy of the same wrong edition.
            _googleBooksService.ClearCache();

            _commandQueueManager.Push(new RefreshAuthorCommand(message.AuthorId.Value));
        }

        // D6: for each series, the D1-D4 pick with the stored id cleared; write the id and queue a
        // refresh only when it moved (null -> set counts as moved). A pass that resolves nothing
        // keeps whatever is stored — a transport failure or an unplaceable name must never
        // un-bind an entry. The refresh then fetches the new id and rewrites poster/overview/aliases.
        // Each author is isolated: a throw anywhere in its step — the resolve (the AniList legs
        // are fail-soft, the catalogue's SQLite hint is not) or the write/refresh — is logged,
        // counted as failed, and the pass moves on with the cached entry untouched, so a re-run
        // retries it. The summary line is the deploy check, so it must always be reached.
        private void Rebind(int? authorId)
        {
            var authors = authorId.HasValue
                ? new List<Author> { _authorService.GetAuthor(authorId.Value) }
                : _authorService.GetAllAuthors().OrderBy(a => a.Name).ToList();

            var changed = 0;
            var unchanged = 0;
            var unresolved = 0;
            var failed = 0;

            foreach (var author in authors)
            {
                try
                {
                    var meta = author.Metadata.Value;
                    var oldId = meta.AniListId?.ToString() ?? "null";
                    // Preferred Edition (2026-09-24, final fix round Minor 4): a series named in its edition's
                    // language (D3) is asked about by its English anchor name, as its refresh does (plan A1);
                    // AniList does not know "L'Attaque des Titans". AnchorName is null on every English series.
                    var lookupName = meta.AnchorName.IsNotNullOrWhiteSpace() ? meta.AnchorName : author.Name;
                    var pick = _mangaMetadataProvider.ResolveAniList(lookupName, author.Library, BookInfoProxy.DisplayName(meta.ForeignAuthorId));

                    if (pick?.Id == null)
                    {
                        unresolved++;
                        _logger.Info("Rebind {0}: {1} -> unresolved, keeping {1}", author.Name, oldId);
                        continue;
                    }

                    if (pick.Id == meta.AniListId)
                    {
                        unchanged++;
                        _logger.Info("Rebind {0}: {1} unchanged via {2}", author.Name, oldId, pick.MatchedVia);
                        continue;
                    }

                    _logger.Info("Rebind {0}: {1} -> {2} via {3}", author.Name, oldId, pick.Id, pick.MatchedVia);

                    // Write from a copy: meta is the live instance in AuthorService's 30 s cache,
                    // and a failed write must leave it as it was — otherwise a retry inside that
                    // window reads the unwritten id back, logs "unchanged" and never hits the DB.
                    var write = meta.JsonClone();
                    write.AniListId = pick.Id;
                    _authorMetadataService.Upsert(write);
                    meta.AniListId = pick.Id;
                    // The refresh's cached lookup is keyed by the same name it asks by (the anchor, above).
                    _mangaMetadataProvider.ForgetLookup(lookupName);
                    _commandQueueManager.Push(new RefreshAuthorCommand(author.Id));
                    changed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.Warn(ex, "Rebind {0}: failed — {1}", author.Name, ex.Message);
                }
            }

            _logger.Info("Rebind pass: {0} changed, {1} unchanged, {2} unresolved, {3} failed of {4}", changed, unchanged, unresolved, failed, authors.Count);
        }
    }
}
