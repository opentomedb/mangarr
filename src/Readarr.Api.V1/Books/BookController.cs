using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Http.REST.Attributes;
using NzbDrone.SignalR;
using Readarr.Http;

namespace Readarr.Api.V1.Books
{
    [V1ApiController]
    public class BookController : BookControllerWithSignalR,
        IHandle<BookGrabbedEvent>,
        IHandle<BookEditedEvent>,
        IHandle<BookUpdatedEvent>,
        IHandle<BookDeletedEvent>,
        IHandle<BookImportedEvent>,
        IHandle<TrackImportedEvent>,
        IHandle<BookFileDeletedEvent>
    {
        protected readonly IAuthorService _authorService;
        protected readonly IEditionService _editionService;
        protected readonly IAddBookService _addBookService;
        private readonly ICoveredVolumeService _coveredVolumeService;

        public BookController(IAuthorService authorService,
                          IBookService bookService,
                          IAddBookService addBookService,
                          IEditionService editionService,
                          ICoveredVolumeService coveredVolumeService,
                          ISeriesBookLinkService seriesBookLinkService,
                          IAuthorStatisticsService authorStatisticsService,
                          IMapCoversToLocal coverMapper,
                          IUpgradableSpecification upgradableSpecification,
                          IMediaFileService mediaFileService,
                          IBroadcastSignalRMessage signalRBroadcaster,
                          QualityProfileExistsValidator qualityProfileExistsValidator,
                          MetadataProfileExistsValidator metadataProfileExistsValidator)

        : base(bookService, seriesBookLinkService, authorStatisticsService, coverMapper, upgradableSpecification, mediaFileService, signalRBroadcaster)
        {
            _authorService = authorService;
            _editionService = editionService;
            _addBookService = addBookService;
            _coveredVolumeService = coveredVolumeService;

            PostValidator.RuleFor(s => s.ForeignBookId).NotEmpty();
            PostValidator.RuleFor(s => s.Author.QualityProfileId).SetValidator(qualityProfileExistsValidator);
            PostValidator.RuleFor(s => s.Author.MetadataProfileId).SetValidator(metadataProfileExistsValidator);
            PostValidator.RuleFor(s => s.Author.RootFolderPath).IsValidPath().When(s => s.Author.Path.IsNullOrWhiteSpace());
            PostValidator.RuleFor(s => s.Author.ForeignAuthorId).NotEmpty();
        }

        [HttpGet]
        public List<BookResource> GetBooks([FromQuery]int? authorId,
            [FromQuery]List<int> bookIds,
            [FromQuery]string titleSlug,
            [FromQuery]bool includeAllAuthorBooks = false)
        {
            if (!authorId.HasValue && !bookIds.Any() && titleSlug.IsNullOrWhiteSpace())
            {
                // all editions, not only monitored ones, so mediaTypes[] / editions[] match the
                // per-author, per-book, wanted and calendar paths (an unmonitored audio edition is
                // present, flagged monitored:false); a manga volume's one edition is monitored
                var editionTask = Task.Run(() => _editionService.GetAllEditions());
                var metadataTask = Task.Run(() => _authorService.GetAllAuthors());
                var books = _bookService.GetAllBooks();

                var editions = editionTask.GetAwaiter().GetResult().GroupBy(x => x.BookId).ToDictionary(x => x.Key, y => y.ToList());

                var authors = metadataTask.GetAwaiter().GetResult().ToDictionary(x => x.AuthorMetadataId);

                foreach (var book in books)
                {
                    book.Author = authors[book.AuthorMetadataId];
                    if (editions.TryGetValue(book.Id, out var bookEditions))
                    {
                        book.Editions = bookEditions;
                    }
                    else
                    {
                        book.Editions = new List<Edition>();
                    }
                }

                return MapToResource(books, false);
            }

            if (authorId.HasValue)
            {
                var books = _bookService.GetBooksByAuthor(authorId.Value);

                var author = _authorService.GetAuthor(authorId.Value);
                var editions = _editionService.GetEditionsByAuthor(authorId.Value)
                    .GroupBy(x => x.BookId)
                    .ToDictionary(x => x.Key, y => y.ToList());

                foreach (var book in books)
                {
                    book.Author = author;
                    if (editions.TryGetValue(book.Id, out var bookEditions))
                    {
                        book.Editions = bookEditions;
                    }
                    else
                    {
                        book.Editions = new List<Edition>();
                    }
                }

                return MapToResource(books, false);
            }

            if (titleSlug.IsNotNullOrWhiteSpace())
            {
                var book = _bookService.FindBySlug(titleSlug);

                if (book == null)
                {
                    return MapToResource(new List<Book>(), false);
                }

                if (includeAllAuthorBooks)
                {
                    return MapToResource(_bookService.GetBooksByAuthor(book.AuthorId), false);
                }
                else
                {
                    return MapToResource(new List<Book> { book }, false);
                }
            }

            return MapToResource(_bookService.GetBooks(bookIds), false);
        }

        [HttpGet("{id:int}/overview")]
        public object Overview(int id)
        {
            var overview = BookExtensions.PrimaryOf(_editionService.GetEditionsByBook(id))?.Overview;
            return new
            {
                id,
                overview
            };
        }

        [RestPostById]
        public ActionResult<BookResource> AddBook(BookResource bookResource)
        {
            var book = _addBookService.AddBook(bookResource.ToModel());

            return Created(book.Id);
        }

        [RestPutById]
        public ActionResult<BookResource> UpdateBook(BookResource bookResource)
        {
            var book = _bookService.GetBook(bookResource.Id);

            // Covered volumes (2026-09-17, D4a): the Edit Book modal saves its editions here, not
            // through EditionService.SetMonitored(int, bool), so the covering volume's Audio edition
            // going monitored -> unmonitored releases the volumes it covers here too — one rule for
            // both UI paths. Read against the stored rows before ToModel swaps them for the incoming
            // ones; a manga volume has no Audio edition and never loads them.
            var unmonitorsAudio = bookResource.Editions.Any(e => e.MediaType == MediaType.Audio && !e.Monitored && book.Editions.Value.Any(s => s.Id == e.Id && s.Monitored));

            // Covered volumes (2026-09-17, B3a): the stored rows, snapshotted for the same reason —
            // ToModel(resource, book) returns this same book with its editions swapped for the incoming
            // ones, so read after it would compare the incoming list with itself.
            var storedEditions = book.Editions.Value.ToDictionary(e => e.Id);

            var model = bookResource.ToModel(book);

            // A save that changes the Audio edition's mark is the user's mark — source "manual" (or
            // cleared); an unchanged mark keeps its stored source. The client never chooses the
            // source. The target must be another volume of the series, and not one whose audiobook
            // this volume already covers (no cycles). Only the Audio edition carries a mark: every
            // other edition keeps its stored values whatever the request sent.
            var volumes = _bookService.GetBooksByAuthorMetadataId(book.AuthorMetadataId);

            foreach (var edition in model.Editions.Value.Where(e => storedEditions.ContainsKey(e.Id)))
            {
                var was = storedEditions[edition.Id];

                if (was.MediaType != MediaType.Audio)
                {
                    edition.CoveredByVolume = was.CoveredByVolume;
                    edition.CoveredSource = was.CoveredSource;
                }
                else if (edition.CoveredByVolume != was.CoveredByVolume)
                {
                    if (edition.CoveredByVolume.HasValue)
                    {
                        var target = volumes.FirstOrDefault(v => v.Id != book.Id && Math.Abs(v.VolumeNumber - edition.CoveredByVolume.Value) < 0.001);
                        var targetCoveredBy = target?.Editions.Value.FirstOrDefault(e => e.MediaType == MediaType.Audio)?.CoveredByVolume;

                        if (target == null || (targetCoveredBy.HasValue && Math.Abs(targetCoveredBy.Value - book.VolumeNumber) < 0.001))
                        {
                            throw new ValidationException(new[] { new NzbDroneValidationFailure("coveredByVolume", "Covered-by volume must be another volume of this series and not one covered by this volume") });
                        }
                    }

                    edition.CoveredSource = edition.CoveredByVolume.HasValue ? CoveredSources.Manual : null;
                }
                else
                {
                    edition.CoveredSource = was.CoveredSource;
                }
            }

            // Editions first: BookService.UpdateBook publishes BookEditedEvent, which drops the author
            // statistics cache and re-maps the resource for the broadcast — both must see the rows.
            _editionService.UpdateMany(model.Editions.Value);
            _bookService.UpdateBook(model);

            if (unmonitorsAudio)
            {
                _coveredVolumeService.ClearCoveredBy(model, "unmonitored");
            }

            BroadcastResourceChange(ModelAction.Updated, model.Id);

            return Accepted(model.Id);
        }

        [RestDeleteById]
        public void DeleteBook(int id, bool deleteFiles = false, bool addImportListExclusion = false)
        {
            _bookService.DeleteBook(id, deleteFiles, addImportListExclusion);
        }

        [HttpPut("monitor")]
        public IActionResult SetBooksMonitored([FromBody]BooksMonitoredResource resource)
        {
            _bookService.SetMonitored(resource.BookIds, resource.Monitored);

            if (resource.BookIds.Count == 1)
            {
                _bookService.SetBookMonitored(resource.BookIds.First(), resource.Monitored);
            }
            else
            {
                _bookService.SetMonitored(resource.BookIds, resource.Monitored);
            }

            return Accepted(MapToResource(_bookService.GetBooks(resource.BookIds), false));
        }

        [NonAction]
        public void Handle(BookGrabbedEvent message)
        {
            foreach (var book in message.Book.Books)
            {
                var resource = book.ToResource();
                resource.Grabbed = true;

                BroadcastResourceChange(ModelAction.Updated, resource);
            }
        }

        [NonAction]
        public void Handle(BookEditedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, MapToResource(message.Book, true));
        }

        [NonAction]
        public void Handle(BookUpdatedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, MapToResource(message.Book, true));
        }

        [NonAction]
        public void Handle(BookDeletedEvent message)
        {
            BroadcastResourceChange(ModelAction.Deleted, message.Book.ToResource());
        }

        [NonAction]
        public void Handle(BookImportedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, MapToResource(message.Book, true));
        }

        [NonAction]
        public void Handle(TrackImportedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, message.BookInfo.Book.ToResource());
        }

        [NonAction]
        public void Handle(BookFileDeletedEvent message)
        {
            if (message.Reason == DeleteMediaFileReason.Upgrade)
            {
                return;
            }

            BroadcastResourceChange(ModelAction.Updated, MapToResource(message.BookFile.Edition.Value.Book.Value, true));
        }
    }
}
