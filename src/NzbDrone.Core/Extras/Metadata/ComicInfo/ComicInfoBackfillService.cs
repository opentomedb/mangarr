using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Extras.Metadata.ComicInfo
{
    // Executes WriteComicInfoCommand: walks the library (or one series) and embeds/refreshes
    // ComicInfo.xml in every eligible file. Idempotent — a file whose ComicInfo already matches is
    // left untouched (WouldChange short-circuits), so re-running is cheap and safe.
    public class ComicInfoBackfillService : IExecute<WriteComicInfoCommand>
    {
        private readonly IComicInfoWriter _writer;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IMediaFileService _mediaFileService;
        private readonly Logger _logger;

        public ComicInfoBackfillService(IComicInfoWriter writer,
                                        IAuthorService authorService,
                                        IBookService bookService,
                                        IMediaFileService mediaFileService,
                                        Logger logger)
        {
            _writer = writer;
            _authorService = authorService;
            _bookService = bookService;
            _mediaFileService = mediaFileService;
            _logger = logger;
        }

        public void Execute(WriteComicInfoCommand message)
        {
            var authors = message.AuthorId.HasValue
                ? new List<Author> { _authorService.GetAuthor(message.AuthorId.Value) }
                : _authorService.GetAllAuthors();

            var written = 0;
            var unchanged = 0;
            var skipped = 0;

            foreach (var author in authors.Where(a => a != null))
            {
                foreach (var book in _bookService.GetBooksByAuthor(author.Id))
                {
                    foreach (var file in _mediaFileService.GetFilesByBook(book.Id))
                    {
                        var status = _writer.WriteForFile(author, book, file);

                        switch (status)
                        {
                            case ComicInfoEmbedStatus.Written: written++; break;
                            case ComicInfoEmbedStatus.Unchanged: unchanged++; break;
                            default: skipped++; break;
                        }
                    }
                }

                _logger.Debug("ComicInfo backfill for {0}: {1} written so far", author.Name, written);
            }

            _logger.Info("ComicInfo backfill complete: {0} written, {1} already current, {2} skipped", written, unchanged, skipped);
        }
    }
}
