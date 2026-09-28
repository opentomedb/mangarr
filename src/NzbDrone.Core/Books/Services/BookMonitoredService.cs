using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Books
{
    public interface IBookMonitoredService
    {
        void SetBookMonitoredStatus(Author author, MonitoringOptions monitoringOptions);
    }

    public class BookMonitoredService : IBookMonitoredService
    {
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly Logger _logger;

        public BookMonitoredService(IAuthorService authorService, IBookService bookService, IEditionService editionService, Logger logger)
        {
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _logger = logger;
        }

        public void SetBookMonitoredStatus(Author author, MonitoringOptions monitoringOptions)
        {
            if (monitoringOptions != null)
            {
                _logger.Debug("[{0}] Setting book monitored status.", author.Name);

                var books = _bookService.GetBooksByAuthor(author.Id);

                var booksWithFiles = _bookService.GetAuthorBooksWithFiles(author);

                var booksWithoutFiles = books.Where(c => !booksWithFiles.Select(e => e.Id).Contains(c.Id) && c.ReleaseDate <= DateTime.UtcNow).ToList();

                var monitoredBooks = monitoringOptions.BooksToMonitor;

                // If specific books are passed use those instead of the monitoring options.
                if (monitoredBooks.Any())
                {
                    ToggleBooksMonitoredState(
                        books.Where(s => monitoredBooks.Contains(s.ForeignBookId)), true);
                    ToggleBooksMonitoredState(
                        books.Where(s => !monitoredBooks.Contains(s.ForeignBookId)), false);
                }
                else
                {
                    switch (monitoringOptions.Monitor)
                    {
                        case MonitorTypes.All:
                            ToggleBooksMonitoredState(books, true);
                            break;
                        case MonitorTypes.Future:
                            _logger.Debug("Unmonitoring Books with Files");
                            ToggleBooksMonitoredState(books.Where(e => booksWithFiles.Select(c => c.Id).Contains(e.Id)), false);
                            _logger.Debug("Unmonitoring Books without Files");
                            ToggleBooksMonitoredState(books.Where(e => booksWithoutFiles.Select(c => c.Id).Contains(e.Id)), false);
                            break;
                        case MonitorTypes.None:
                            ToggleBooksMonitoredState(books, false);
                            break;
                        case MonitorTypes.Missing:
                            _logger.Debug("Unmonitoring Books with Files");
                            ToggleBooksMonitoredState(books.Where(e => booksWithFiles.Select(c => c.Id).Contains(e.Id)), false);
                            _logger.Debug("Monitoring Books without Files");
                            ToggleBooksMonitoredState(books.Where(e => booksWithoutFiles.Select(c => c.Id).Contains(e.Id)), true);
                            break;
                        case MonitorTypes.Existing:
                            _logger.Debug("Monitoring Books with Files");
                            ToggleBooksMonitoredState(books.Where(e => booksWithFiles.Select(c => c.Id).Contains(e.Id)), true);
                            _logger.Debug("Unmonitoring Books without Files");
                            ToggleBooksMonitoredState(books.Where(e => booksWithoutFiles.Select(c => c.Id).Contains(e.Id)), false);
                            break;
                        case MonitorTypes.Latest:
                            ToggleBooksMonitoredState(books, false);
                            ToggleBooksMonitoredState(
                                books.OrderByDescending(b => b.VolumeNumber > 0)
                                     .ThenByDescending(b => b.VolumeNumber)
                                     .ThenByDescending(b => b.ReleaseDate)
                                     .Take(1),
                                true);
                            break;
                        case MonitorTypes.First:
                            ToggleBooksMonitoredState(books, false);
                            ToggleBooksMonitoredState(
                                books.OrderByDescending(b => b.VolumeNumber > 0)
                                     .ThenBy(b => b.VolumeNumber)
                                     .ThenBy(b => b.ReleaseDate)
                                     .Take(1),
                                true);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                ApplyMonitorMediaTypes(author, books, monitoringOptions.MonitorMediaTypes);

                // Use individual update to ensure updates are sent to frontend
                foreach (var book in books)
                {
                    _bookService.UpdateBook(book);
                }
            }

            _authorService.UpdateAuthor(author);
        }

        private void ToggleBooksMonitoredState(IEnumerable<Book> books, bool monitored)
        {
            foreach (var book in books)
            {
                book.Monitored = monitored;
            }
        }

        // Light novels (2026-09): the Add modal's EPUB / Audiobook checkboxes. Editions whose media
        // type was not ticked are unmonitored; ticked ones keep their mint state; null or empty
        // leaves every edition alone (manga adds, and callers that do not send it). The book flag
        // is the Monitor option's: a book stays monitored while any of its editions is.
        private void ApplyMonitorMediaTypes(Author author, List<Book> books, List<string> monitorMediaTypes)
        {
            if (monitorMediaTypes == null || !monitorMediaTypes.Any())
            {
                return;
            }

            var wanted = new HashSet<MediaType>();
            foreach (var name in monitorMediaTypes)
            {
                if (Enum.TryParse<MediaType>(name, true, out var mediaType) && Enum.IsDefined(typeof(MediaType), mediaType))
                {
                    wanted.Add(mediaType);
                }
                else
                {
                    _logger.Warn("[{0}] Ignoring unknown media type '{1}' in the formats to monitor", author.Name, name);
                }
            }

            // A list with no recognised name must not read as "monitor nothing".
            if (!wanted.Any())
            {
                _logger.Warn("[{0}] No recognised media type in the formats to monitor; leaving every edition as is", author.Name);
                return;
            }

            var toUpdate = new List<Edition>();
            foreach (var book in books)
            {
                var editions = book.Editions?.Value;
                if (editions == null)
                {
                    continue;
                }

                foreach (var edition in editions.Where(e => e.Monitored && !wanted.Contains(e.MediaType)))
                {
                    _logger.Debug("[{0}] Unmonitoring the {1} edition of {2}: not in the formats to monitor", author.Name, edition.MediaType, book);
                    edition.Monitored = false;
                    toUpdate.Add(edition);
                }
            }

            if (toUpdate.Any())
            {
                _editionService.UpdateMany(toUpdate);
            }
        }
    }
}
