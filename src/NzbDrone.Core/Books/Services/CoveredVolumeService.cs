using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface ICoveredVolumeService
    {
        // A spanning audio file landed on `carrier`; every other volume in `covered` (same author) whose
        // Audio edition has no file gets CoveredByVolume = carrier.VolumeNumber, CoveredSource = "import".
        // Returns the editions marked (for logging/verification). Volumes that already carry a mark
        // (any source) or an audio file are left alone. Pass repository-loaded books (lazies prepared):
        // each marked book is republished in a BookEditedEvent, whose readers need Book.Author.
        List<Edition> MarkCoveredByImport(Book carrier, IEnumerable<Book> covered, string reason);

        // D4a: the covering volume's Audio edition lost its last file / was unmonitored → every Audio
        // edition of the same author with CoveredByVolume == that volume's number is reset (both
        // sources). Returns the editions cleared.
        List<Edition> ClearCoveredBy(Book covering, string reason);
    }

    // Covered volumes (2026-09-17, D4a): a mark has one owner and reverses. Imports write "import"
    // marks here; the refresh writes "audible" ones (Edition.UseMetadataFrom); the Edit Book save
    // writes "manual" ones (BookController.UpdateBook, B3a). All three are cleared here,
    // and only here, when the covering volume's last audio file goes away or its Audio edition is
    // unmonitored — the covered volumes become wanted again — and on the covered volume itself when
    // it gains an audio file of its own. Every branch is on the Audio edition of
    // a light-novel volume; a manga volume has no Audio edition, so nothing here runs for one.
    //
    // Depends on the repositories, not IBookService/IEditionService: EditionService calls in here
    // and BookService depends on EditionService, so either would close a DI cycle.
    public class CoveredVolumeService : ICoveredVolumeService,
        IHandle<BookFileDeletedEvent>,
        IHandle<BookFileAddedEvent>,
        IHandle<BookDeletedEvent>
    {
        private readonly IEditionRepository _editionRepository;
        private readonly IMediaFileService _mediaFileService;
        private readonly IBookRepository _bookRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public CoveredVolumeService(IEditionRepository editionRepository,
                                    IMediaFileService mediaFileService,
                                    IBookRepository bookRepository,
                                    IEventAggregator eventAggregator,
                                    Logger logger)
        {
            _editionRepository = editionRepository;
            _mediaFileService = mediaFileService;
            _bookRepository = bookRepository;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public List<Edition> MarkCoveredByImport(Book carrier, IEnumerable<Book> covered, string reason)
        {
            var marked = new List<Edition>();

            foreach (var book in covered)
            {
                if (book.Id == carrier.Id || book.AuthorMetadataId != carrier.AuthorMetadataId)
                {
                    continue;
                }

                var edition = book.EditionOf(MediaType.Audio)
                              ?? _editionRepository.FindByBook(new[] { book.Id }).FirstOrDefault(e => e.MediaType == MediaType.Audio);

                if (edition == null || edition.CoveredByVolume.HasValue || _mediaFileService.GetFilesByEdition(edition.Id).Any())
                {
                    continue;
                }

                edition.CoveredByVolume = carrier.VolumeNumber;
                edition.CoveredSource = CoveredSources.Import;
                _editionRepository.SetFields(edition, e => e.CoveredByVolume, e => e.CoveredSource);
                _logger.Info("Import: {0} Vol. {1} audio covered by Vol. {2} ({3})", SeriesName(carrier), book.VolumeNumber, carrier.VolumeNumber, reason);

                // The covered row's audio status changes; announce the book as edited so it and the
                // author's statistics refresh (AuthorStatisticsService drops its cache on BookEditedEvent).
                _eventAggregator.PublishEvent(new BookEditedEvent(book, book));
                marked.Add(edition);
            }

            return marked;
        }

        public List<Edition> ClearCoveredBy(Book covering, string reason)
        {
            var cleared = _editionRepository.GetCoveredBy(covering.AuthorMetadataId, covering.VolumeNumber);

            foreach (var edition in cleared)
            {
                edition.CoveredByVolume = null;
                edition.CoveredSource = null;
                _editionRepository.SetFields(edition, e => e.CoveredByVolume, e => e.CoveredSource);

                var book = _bookRepository.Get(edition.BookId);
                _logger.Info("Covered mark cleared on {0} Vol. {1} (covering Vol. {2} {3})", SeriesName(covering), book.VolumeNumber, covering.VolumeNumber, reason);
                _eventAggregator.PublishEvent(new BookEditedEvent(book, book));
            }

            return cleared;
        }

        // The covering volume's Audio edition lost its last file (a part-file delete leaves the mark
        // while parts remain). An Upgrade delete clears too: the replacement's import re-marks what
        // it spans, and a narrower replacement must not leave stale marks behind. A non-audio file
        // (every manga CBZ of a purge) returns on its quality alone, as the add handler does.
        public void Handle(BookFileDeletedEvent message)
        {
            if (MediaTypes.OfQuality(message.BookFile.Quality?.Quality) != MediaType.Audio)
            {
                return;
            }

            var file = message.BookFile;
            var edition = ResolveEdition(file);

            if (edition == null)
            {
                _logger.Debug("Deleted file {0} has no edition; no covered marks to check", file);
                return;
            }

            if (edition.MediaType != MediaType.Audio || _mediaFileService.GetFilesByEdition(edition.Id).Any())
            {
                return;
            }

            var book = _bookRepository.Find(edition.BookId);

            if (book == null)
            {
                _logger.Debug("Deleted file {0} has no book; no covered marks to check", file);
                return;
            }

            ClearCoveredBy(book, "file deleted");
        }

        // An Audio edition that gains its own file drops its own mark: a single imported by hand (or
        // copied in) for a covered volume takes over from the pack. A non-audio file (every manga CBZ
        // of a rescan) returns on its quality alone; an audio file costs one indexed lookup.
        public void Handle(BookFileAddedEvent message)
        {
            if (MediaTypes.OfQuality(message.BookFile.Quality?.Quality) != MediaType.Audio)
            {
                return;
            }

            var edition = ResolveEdition(message.BookFile);

            if (edition == null || edition.MediaType != MediaType.Audio || !edition.CoveredByVolume.HasValue)
            {
                return;
            }

            edition.CoveredByVolume = null;
            edition.CoveredSource = null;
            _editionRepository.SetFields(edition, e => e.CoveredByVolume, e => e.CoveredSource);

            var book = _bookRepository.Get(edition.BookId);
            _logger.Info("Covered mark cleared on {0} Vol. {1} (own audio file imported)", SeriesName(book), book.VolumeNumber);
            _eventAggregator.PublishEvent(new BookEditedEvent(book, book));
        }

        // The covering volume itself is deleted: no BookFileDeletedEvent reaches here for its files
        // (the book row goes first, so the file queries joined on Books find nothing), and a refresh
        // re-adds the volume fileless — the volumes it covered must be wanted again. A manga volume
        // has no marks to clear, so it never costs the lookup.
        public void Handle(BookDeletedEvent message)
        {
            if (LibraryOf(message.Book) == LibraryType.Manga)
            {
                return;
            }

            ClearCoveredBy(message.Book, "book deleted");
        }

        // The deleted book's library from what the event's copy already carries — loading a lazy to
        // decide would be the query the gate is here to save. The Author when loaded (DeleteBook
        // loads it), else its metadata's foreign id; neither loaded = unknown, and the clear runs.
        private static LibraryType? LibraryOf(Book book)
        {
            if (book.Author != null && book.Author.IsLoaded)
            {
                return book.Author.Value?.Library;
            }

            if (book.AuthorMetadata != null && book.AuthorMetadata.IsLoaded)
            {
                return LibraryTypes.Parse(book.AuthorMetadata.Value?.ForeignAuthorId);
            }

            return null;
        }

        // The file's Edition lazy may already be gone after the delete: the stored row first, the
        // event's copy as the fallback.
        private Edition ResolveEdition(BookFile file)
        {
            return _editionRepository.Find(file.EditionId) ?? file.Edition?.Value;
        }

        private static string SeriesName(Book book)
        {
            return book.Author?.Value?.Name ?? book.AuthorMetadata?.Value?.Name ?? book.Title;
        }
    }
}
