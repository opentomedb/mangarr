using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // Copy-in hold (2026-09-16, D2): after the add-refresh's scan, a held light novel queues the
    // copy-in (carrying the user's search-on-add intent) instead of the missing search.
    // Post-add search timing (2026-09-18, B3c D3): the add's first scan event arrives before the
    // volumes have release dates (BooksWithoutFiles needs one), so the post-add actions — the
    // missing search, or a held light novel's copy-in that pushes it — are deferred to the dated
    // refresh's event, bounded to PostAddSearchWindow after Author.Added.
    [TestFixture]
    public class AuthorScannedHandlerFixture : CoreTest<AuthorScannedHandler>
    {
        private Author _author;
        private MemoryTarget _log;

        [SetUp]
        public void Setup()
        {
            _author = new Author
            {
                Id = 3,
                Name = "Overlord",
                Added = DateTime.UtcNow,
                AddOptions = new AddAuthorOptions { SearchForMissingBooks = true }
            };

            GivenBooks(dated: false);

            _log = new MemoryTarget("author-scanned") { Layout = "${level}|${message}" };
            LogManager.Configuration.AddTarget(_log.Name, _log);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, _log));
            LogManager.ReconfigExistingLoggers();
        }

        [TearDown]
        public void ReleaseLog()
        {
            foreach (var rule in LogManager.Configuration.LoggingRules.Where(r => r.Targets.Contains(_log)).ToList())
            {
                LogManager.Configuration.LoggingRules.Remove(rule);
            }

            LogManager.Configuration.RemoveTarget(_log.Name);
            LogManager.ReconfigExistingLoggers();
        }

        private void GivenBooks(bool dated)
        {
            var books = new List<Book>
            {
                new Book { Id = 30, Title = "Vol. 1", ReleaseDate = dated ? new DateTime(2012, 7, 30) : (DateTime?)null },
                new Book { Id = 31, Title = "Vol. 2", ReleaseDate = dated ? new DateTime(2012, 11, 30) : (DateTime?)null }
            };

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBooksByAuthor(3))
                .Returns(books);
        }

        private void VerifyMissingSearch(Times times)
        {
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.Is<MissingBookSearchCommand>(c => c.AuthorId == 3), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), times);
        }

        private void VerifyCopyIn(Times times)
        {
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.Is<ImportExistingLightNovelsCommand>(c => c.AuthorId == 3 && c.SearchAfter), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), times);
        }

        private void VerifyAddOptionsConsumed(Times times)
        {
            Mocker.GetMock<IBookMonitoredService>()
                .Verify(v => v.SetBookMonitoredStatus(_author, It.IsAny<AddAuthorOptions>()), times);
            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.RemoveAddOptions(_author), times);
        }

        [Test]
        public void held_light_novel_queues_the_copy_in_with_the_search_flag()
        {
            _author.CopyInPending = true;
            GivenBooks(dated: true);

            Subject.Handle(new AuthorScannedEvent(_author));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.Is<ImportExistingLightNovelsCommand>(c => c.AuthorId == 3 && c.SearchAfter), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
            VerifyMissingSearch(Times.Never());
        }

        // No search asked for: nothing to wait for, the copy-in is queued on the first event.
        [Test]
        public void held_light_novel_without_search_on_add_queues_the_copy_in_without_it()
        {
            _author.CopyInPending = true;
            _author.AddOptions.SearchForMissingBooks = false;

            Subject.Handle(new AuthorScannedEvent(_author));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.Is<ImportExistingLightNovelsCommand>(c => c.AuthorId == 3 && !c.SearchAfter), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
            VerifyAddOptionsConsumed(Times.Once());
            Mocker.GetMock<IBookService>()
                .Verify(v => v.GetBooksByAuthor(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void unheld_entry_with_dated_volumes_queues_the_missing_search_as_before()
        {
            GivenBooks(dated: true);

            Subject.Handle(new AuthorScannedEvent(_author));

            VerifyMissingSearch(Times.Once());
            VerifyAddOptionsConsumed(Times.Once());
            _author.AddOptions.Should().BeNull();
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(v => v.Push(It.IsAny<ImportExistingLightNovelsCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        // (a) the quick refresh's scan: volumes exist but carry no release date yet, so the
        // search would find nothing — hold the add options for the dated refresh's event.
        [Test]
        public void recent_add_with_undated_volumes_defers_and_keeps_the_add_options()
        {
            Subject.Handle(new AuthorScannedEvent(_author));

            VerifyMissingSearch(Times.Never());
            VerifyAddOptionsConsumed(Times.Never());
            _author.AddOptions.Should().NotBeNull();
            _log.Logs.Should().Contain("Debug|[Overlord] post-add search deferred: no dated volumes yet");
        }

        // The deferral skips only the post-add actions; the recently-added sweep still runs.
        [Test]
        public void deferral_still_runs_the_recently_added_search()
        {
            Subject.Handle(new AuthorScannedEvent(_author));

            Mocker.GetMock<IBookAddedService>()
                .Verify(v => v.SearchForRecentlyAdded(3), Times.Once());
        }

        // (b) the dated refresh's scan consumes the held options: one search, monitoring set once.
        [Test]
        public void second_event_with_dated_volumes_consumes_the_add_options_once()
        {
            Subject.Handle(new AuthorScannedEvent(_author));

            VerifyMissingSearch(Times.Never());
            VerifyAddOptionsConsumed(Times.Never());

            GivenBooks(dated: true);

            Subject.Handle(new AuthorScannedEvent(_author));

            VerifyMissingSearch(Times.Once());
            VerifyAddOptionsConsumed(Times.Once());
            _author.AddOptions.Should().BeNull();
        }

        // (c) the window is a bound, not a wait: an add older than PostAddSearchWindow with no
        // dated volume is consumed as today.
        [Test]
        public void expired_window_with_undated_volumes_consumes_as_today()
        {
            _author.Added = DateTime.UtcNow.AddMinutes(-11);

            Subject.Handle(new AuthorScannedEvent(_author));

            VerifyMissingSearch(Times.Once());
            VerifyAddOptionsConsumed(Times.Once());
            _author.AddOptions.Should().BeNull();
            _log.Logs.Should().NotContain(l => l.Contains("post-add search deferred"));
        }

        // (d) nothing to defer without a search: monitoring is applied on the first event.
        [Test]
        public void no_search_on_add_with_undated_volumes_consumes_immediately()
        {
            _author.AddOptions.SearchForMissingBooks = false;

            Subject.Handle(new AuthorScannedEvent(_author));

            VerifyMissingSearch(Times.Never());
            VerifyAddOptionsConsumed(Times.Once());
            _author.AddOptions.Should().BeNull();
            Mocker.GetMock<IBookService>()
                .Verify(v => v.GetBooksByAuthor(It.IsAny<int>()), Times.Never());
        }

        // (e) a held light novel defers too (final review F1): its copy-in pushes the search the
        // moment it finishes, which was before the dated refresh landed. The hold stays meanwhile.
        [Test]
        public void held_light_novel_with_undated_volumes_is_deferred()
        {
            _author.CopyInPending = true;

            Subject.Handle(new AuthorScannedEvent(_author));

            VerifyCopyIn(Times.Never());
            VerifyMissingSearch(Times.Never());
            VerifyAddOptionsConsumed(Times.Never());
            _author.AddOptions.Should().NotBeNull();
            _author.CopyInPending.Should().BeTrue();
            _log.Logs.Should().Contain("Debug|[Overlord] post-add search deferred: no dated volumes yet");
        }

        [Test]
        public void held_light_novel_dated_event_queues_the_copy_in_once_with_the_search_flag()
        {
            _author.CopyInPending = true;

            Subject.Handle(new AuthorScannedEvent(_author));

            VerifyCopyIn(Times.Never());

            GivenBooks(dated: true);

            Subject.Handle(new AuthorScannedEvent(_author));

            VerifyCopyIn(Times.Once());
            VerifyMissingSearch(Times.Never());
            VerifyAddOptionsConsumed(Times.Once());
            _author.AddOptions.Should().BeNull();
        }

        [Test]
        public void held_light_novel_past_the_window_with_undated_volumes_queues_the_copy_in_as_today()
        {
            _author.CopyInPending = true;
            _author.Added = DateTime.UtcNow.AddMinutes(-11);

            Subject.Handle(new AuthorScannedEvent(_author));

            VerifyCopyIn(Times.Once());
            VerifyAddOptionsConsumed(Times.Once());
            _author.AddOptions.Should().BeNull();
            _log.Logs.Should().NotContain(l => l.Contains("post-add search deferred"));
        }
    }
}
