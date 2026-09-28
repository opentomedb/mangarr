using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Books
{
    public interface IAddBookService
    {
        Book AddBook(Book book, bool doRefresh = true);
        List<Book> AddBooks(List<Book> books, bool doRefresh = true);
    }

    public class AddBookService : IAddBookService
    {
        private readonly IAuthorService _authorService;
        private readonly IAddAuthorService _addAuthorService;
        private readonly IBookService _bookService;
        private readonly IProvideBookInfo _bookInfo;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly Logger _logger;

        public AddBookService(IAuthorService authorService,
                               IAddAuthorService addAuthorService,
                               IBookService bookService,
                               IProvideBookInfo bookInfo,
                               IImportListExclusionService importListExclusionService,
                               Logger logger)
        {
            _authorService = authorService;
            _addAuthorService = addAuthorService;
            _bookService = bookService;
            _bookInfo = bookInfo;
            _importListExclusionService = importListExclusionService;
            _logger = logger;
        }

        public Book AddBook(Book book, bool doRefresh = true)
        {
            _logger.Debug($"Adding book {book}");

            book = AddSkyhookData(book);

            // we allow adding extra editions, so check if the book already exists
            var dbBook = _bookService.FindById(book.ForeignBookId);
            if (dbBook != null)
            {
                book.UseDbFieldsFrom(dbBook);
            }

            // Remove any import list exclusions preventing addition
            _importListExclusionService.Delete(book.ForeignBookId);
            _importListExclusionService.Delete(book.AuthorMetadata.Value.ForeignAuthorId);

            // Note it's a manual addition so it's not deleted on next refresh
            book.AddOptions.AddType = BookAddType.Manual;
            foreach (var edition in book.Editions.Value.Where(x => x.Monitored))
            {
                edition.ManualAdd = true;
            }

            // Add the author if necessary
            var dbAuthor = _authorService.FindById(book.AuthorMetadata.Value.ForeignAuthorId);
            if (dbAuthor == null)
            {
                var author = book.Author.Value;

                author.Metadata.Value.ForeignAuthorId = book.AuthorMetadata.Value.ForeignAuthorId;

                dbAuthor = _addAuthorService.AddAuthor(author, false);
            }

            book.Author = dbAuthor;
            book.AuthorMetadataId = dbAuthor.AuthorMetadataId;
            _bookService.AddBook(book, doRefresh);

            return book;
        }

        public List<Book> AddBooks(List<Book> books, bool doRefresh = true)
        {
            var added = DateTime.UtcNow;
            var addedBooks = new List<Book>();

            foreach (var a in books)
            {
                a.Added = added;
                try
                {
                    addedBooks.Add(AddBook(a, doRefresh));
                }
                catch (Exception ex)
                {
                    // Could be a bad id from an import list
                    _logger.Error(ex, "Failed to import id: {0} - {1}", a.ForeignBookId, a.Title);
                }
            }

            return addedBooks;
        }

        private Book AddSkyhookData(Book newBook)
        {
            // Every monitored edition the caller chose (one for a manga volume, up to one per media
            // type for a light novel) is re-selected on the freshly resolved book below.
            var monitoredEditionIds = newBook.Editions.Value.Where(x => x.Monitored).Select(x => x.ForeignEditionId).ToList();

            Tuple<string, Book, List<AuthorMetadata>> tuple = null;
            try
            {
                tuple = _bookInfo.GetBookInfo(newBook.ForeignBookId);
            }
            catch (BookNotFoundException)
            {
                _logger.Error("Book with Foreign Id {0} was not found, it may have been removed from Goodreads.", newBook.ForeignBookId);

                throw new ValidationException(new List<ValidationFailure>
                                              {
                                                  new ValidationFailure("GoodreadsId", "A volume with this ID was not found", newBook.ForeignBookId)
                                              });
            }
            catch (EditionUnavailableException ex)
            {
                // Preferred Edition (2026-09-24, M5 fix round 1): the series' bound edition is gone from
                // the catalogue -- a validation message, as AddAuthorService gives, never a server error.
                throw new ValidationException(new List<ValidationFailure>
                                              {
                                                  new NzbDroneValidationFailure("ForeignBookId", new ServerText("No {0} edition of this series in the catalogue", new ServerText(EditionLanguages.Name(ex.Language))))
                                                  {
                                                      AttemptedValue = ex.Language
                                                  }
                                              });
            }

            newBook.UseMetadataFrom(tuple.Item2);
            newBook.Added = DateTime.UtcNow;

            newBook.Editions = tuple.Item2.Editions.Value;
            newBook.Editions.Value.ForEach(x => x.Monitored = monitoredEditionIds.Contains(x.ForeignEditionId));

            var metadata = tuple.Item3.FirstOrDefault(x => x.ForeignAuthorId == tuple.Item1);
            newBook.AuthorMetadata = metadata;

            return newBook;
        }
    }
}
