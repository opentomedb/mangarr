using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public class AuthorScannedHandler : IHandle<AuthorScannedEvent>,
                                        IHandle<AuthorScanSkippedEvent>
    {
        // Post-add search timing (2026-09-18, B3c D3): an add's first scan event comes from the
        // quick refresh, before the volumes have release dates; the missing search only sees dated
        // volumes, so the post-add actions are held for the dated refresh's event. A held light
        // novel is no exception: its copy-in pushes the search the moment it finishes, and the
        // copy-in was seen to release ~6 s before the dated refresh landed (final review F1).
        // Bound after Author.Added, past which the options are consumed as today.
        public static readonly TimeSpan PostAddSearchWindow = TimeSpan.FromMinutes(10);

        private readonly IBookMonitoredService _bookMonitoredService;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IBookAddedService _bookAddedService;
        private readonly Logger _logger;

        public AuthorScannedHandler(IBookMonitoredService bookMonitoredService,
                                    IAuthorService authorService,
                                    IBookService bookService,
                                    IManageCommandQueue commandQueueManager,
                                    IBookAddedService bookAddedService,
                                    Logger logger)
        {
            _bookMonitoredService = bookMonitoredService;
            _authorService = authorService;
            _bookService = bookService;
            _commandQueueManager = commandQueueManager;
            _bookAddedService = bookAddedService;
            _logger = logger;
        }

        private bool PostAddSearchDeferred(Author author)
        {
            if (!author.AddOptions.SearchForMissingBooks ||
                author.Added <= DateTime.UtcNow - PostAddSearchWindow ||
                _bookService.GetBooksByAuthor(author.Id).Any(b => b.ReleaseDate.HasValue))
            {
                return false;
            }

            _logger.Debug("[{0}] post-add search deferred: no dated volumes yet", author.Name);
            return true;
        }

        private void HandleScanEvents(Author author)
        {
            // The deferral keeps AddOptions (and a light novel's hold) for the next scan event;
            // the recently-added sweep below still runs on every event.
            if (author.AddOptions != null && !PostAddSearchDeferred(author))
            {
                _logger.Info("[{0}] was recently added, performing post-add actions", author.Name);
                _bookMonitoredService.SetBookMonitoredStatus(author, author.AddOptions);

                // Copy-in hold (2026-09-16, D2): a held light novel imports what the maintainer already owns
                // first; the search he asked for on add runs from that command once the hold is clear.
                if (author.CopyInPending)
                {
                    _commandQueueManager.Push(new ImportExistingLightNovelsCommand(author.Id) { SearchAfter = author.AddOptions.SearchForMissingBooks });
                }
                else if (author.AddOptions.SearchForMissingBooks)
                {
                    _commandQueueManager.Push(new MissingBookSearchCommand(author.Id));
                }

                author.AddOptions = null;
                _authorService.RemoveAddOptions(author);
            }

            _bookAddedService.SearchForRecentlyAdded(author.Id);
        }

        public void Handle(AuthorScannedEvent message)
        {
            HandleScanEvents(message.Author);
        }

        public void Handle(AuthorScanSkippedEvent message)
        {
            HandleScanEvents(message.Author);
        }
    }
}
