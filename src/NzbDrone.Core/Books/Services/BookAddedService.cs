using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface IBookAddedService
    {
        void SearchForRecentlyAdded(int authorId);

        // Review fixes (2026-09-28, I2): the author's next refresh queues no search for the volumes it adds
        // (Switch Line). One-shot: that refresh consumes it. The volumes stay monitored; the scheduled
        // missing search picks them up.
        void SkipNextRefreshSearch(int authorId);
    }

    public class BookAddedService : IHandle<BookInfoRefreshedEvent>, IHandle<AuthorRefreshCompleteEvent>, IBookAddedService
    {
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IBookService _bookService;
        private readonly Logger _logger;
        private readonly ICached<List<int>> _addedBooksCache;
        private readonly HashSet<int> _skipNextSearch = new HashSet<int>();

        public BookAddedService(ICacheManager cacheManager,
                                   IManageCommandQueue commandQueueManager,
                                   IBookService bookService,
                                   Logger logger)
        {
            _commandQueueManager = commandQueueManager;
            _bookService = bookService;
            _logger = logger;
            _addedBooksCache = cacheManager.GetCache<List<int>>(GetType());
        }

        public void SearchForRecentlyAdded(int authorId)
        {
            var allBooks = _bookService.GetBooksByAuthor(authorId);
            var toSearch = allBooks.Where(x => x.AddOptions.SearchForNewBook).ToList();

            if (toSearch.Any())
            {
                toSearch.ForEach(x => x.AddOptions.SearchForNewBook = false);

                _bookService.SetAddOptions(toSearch);
            }

            var recentlyAddedIds = _addedBooksCache.Find(authorId.ToString());
            if (recentlyAddedIds != null)
            {
                toSearch.AddRange(allBooks.Where(x => recentlyAddedIds.Contains(x.Id)));
            }

            if (toSearch.Any())
            {
                _commandQueueManager.Push(new BookSearchCommand(toSearch.Select(e => e.Id).ToList()));
            }

            _addedBooksCache.Remove(authorId.ToString());
        }

        public void SkipNextRefreshSearch(int authorId)
        {
            lock (_skipNextSearch)
            {
                _skipNextSearch.Add(authorId);
            }
        }

        private bool TakeSkip(int authorId)
        {
            lock (_skipNextSearch)
            {
                return _skipNextSearch.Remove(authorId);
            }
        }

        public void Handle(BookInfoRefreshedEvent message)
        {
            if (TakeSkip(message.Author.Id))
            {
                _logger.Debug("Refresh after a line switch: not searching its new volumes");
                return;
            }

            if (message.Author.AddOptions == null)
            {
                if (!message.Author.Monitored)
                {
                    _logger.Debug("Author is not monitored");
                    return;
                }

                if (message.Added.Empty())
                {
                    _logger.Debug("No new books, skipping search");
                    return;
                }

                if (message.Added.None(a => a.ReleaseDate.HasValue))
                {
                    _logger.Debug("No new books have an release date");
                    return;
                }

                var previouslyReleased = message.Added.Where(a => a.ReleaseDate.HasValue && a.ReleaseDate.Value.Before(DateTime.UtcNow.AddDays(1)) && a.Monitored).ToList();

                if (previouslyReleased.Empty())
                {
                    _logger.Debug("Newly added books all release in the future");
                    return;
                }

                _addedBooksCache.Set(message.Author.Id.ToString(), previouslyReleased.Select(e => e.Id).ToList());
            }
        }

        // A refresh that added nothing publishes no BookInfoRefreshedEvent: the flag must not outlive it.
        public void Handle(AuthorRefreshCompleteEvent message)
        {
            TakeSkip(message.Author.Id);
        }
    }
}
