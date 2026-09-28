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
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.BookTests
{
    // D6: the rebind pass computes every series' D1-D4 pick with the stored id cleared, logs
    // old -> new, and writes + refreshes only the ones that moved. The four authors below are
    // the audit's shapes: a wrong binding, a right one, an unbound one, and one the resolver
    // cannot place this time (kept as is).
    [TestFixture]
    public class ReResolveMetadataServiceFixture : CoreTest<ReResolveMetadataService>
    {
        private Author _fairyTail;      // 128087 (wrong) -> 30598
        private Author _attackOnTitan;  // 53390 -> 53390 (unchanged)
        private Author _letsDoIt;       // null -> 120768
        private Author _frieren;        // 118586 -> unresolved this pass (kept)
        private MemoryTarget _log;

        [SetUp]
        public void Setup()
        {
            _fairyTail = AuthorWith(1, "local-fairy-tail", "Fairy Tail", 128087);
            _attackOnTitan = AuthorWith(2, "local-attack-on-titan", "Attack on Titan", 53390);
            _letsDoIt = AuthorWith(3, "local-let-s-do-it-already", "Let’s Do It Already!", null);
            _frieren = AuthorWith(4, "local-frieren-beyond-journey-s-end", "Frieren: Beyond Journey’s End", 118586);

            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.GetAllAuthors())
                  .Returns(new List<Author> { _frieren, _fairyTail, _letsDoIt, _attackOnTitan });

            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.GetAuthor(1))
                  .Returns(_fairyTail);

            GivenPick("Fairy Tail", 30598, "primary");
            GivenPick("Attack on Titan", 53390, "primary");
            GivenPick("Let’s Do It Already!", 120768, "primary");
            GivenPick("Frieren: Beyond Journey’s End", null, null);

            _log = new MemoryTarget("rebind") { Layout = "${level}|${message}" };
            LogManager.Configuration.AddTarget(_log.Name, _log);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Info, _log));
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

        private static Author AuthorWith(int id, string foreignId, string name, int? anilistId)
        {
            return new Author
            {
                Id = id,
                Metadata = new AuthorMetadata { ForeignAuthorId = foreignId, Name = name, AniListId = anilistId }
            };
        }

        private void GivenPick(string name, int? id, string via)
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(p => p.ResolveAniList(name, It.IsAny<LibraryType>(), It.IsAny<string>()))
                  .Returns(id.HasValue ? new AniListSeries { Id = id, MatchedVia = via } : null);
        }

        private void VerifyWritten(string foreignId, int anilistId, Times times)
        {
            Mocker.GetMock<IAuthorMetadataService>()
                  .Verify(s => s.Upsert(It.Is<AuthorMetadata>(m => m.ForeignAuthorId == foreignId && m.AniListId == anilistId)), times);
        }

        private void VerifyRefreshed(int authorId, Times times)
        {
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(It.Is<RefreshAuthorCommand>(c => c.AuthorId == authorId), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), times);
        }

        [Test]
        public void rebind_writes_and_refreshes_only_the_entries_whose_pick_moved()
        {
            Subject.Execute(new ReResolveMetadataCommand { Rebind = true });

            VerifyWritten("local-fairy-tail", 30598, Times.Once());
            VerifyWritten("local-let-s-do-it-already", 120768, Times.Once());
            Mocker.GetMock<IAuthorMetadataService>()
                  .Verify(s => s.Upsert(It.IsAny<AuthorMetadata>()), Times.Exactly(2));

            VerifyRefreshed(1, Times.Once());
            VerifyRefreshed(3, Times.Once());
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(It.IsAny<RefreshAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Exactly(2));

            Mocker.GetMock<IMangaSeriesMetadataProvider>().Verify(p => p.ForgetLookup("Fairy Tail"), Times.Once());
            Mocker.GetMock<IMangaSeriesMetadataProvider>().Verify(p => p.ForgetLookup("Let’s Do It Already!"), Times.Once());
            Mocker.GetMock<IMangaSeriesMetadataProvider>().Verify(p => p.ForgetLookup("Attack on Titan"), Times.Never());

            _attackOnTitan.Metadata.Value.AniListId.Should().Be(53390);
            _frieren.Metadata.Value.AniListId.Should().Be(118586);
        }

        [Test]
        public void rebind_passes_the_de_slugged_id_and_the_library_to_the_resolver()
        {
            var lightNovel = AuthorWith(9, "local-sword-art-online~ln", "Sword Art Online", null);
            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.GetAllAuthors())
                  .Returns(new List<Author> { lightNovel });
            GivenPick("Sword Art Online", 51479, "primary");

            Subject.Execute(new ReResolveMetadataCommand { Rebind = true });

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(p => p.ResolveAniList("Sword Art Online", LibraryType.LightNovel, "Sword Art Online"), Times.Once());
            VerifyWritten("local-sword-art-online~ln", 51479, Times.Once());
        }

        // Final fix round Minor 4 (2026-09-24): a series named in its edition's language is asked about by its
        // English anchor name (and its cached lookup forgotten under it); an English series by its name.
        [Test]
        public void rebind_asks_a_localized_series_by_its_anchor_name()
        {
            var french = AuthorWith(9, "local-attack-on-titan", "L'Attaque des Titans", 1);
            french.Metadata.Value.EditionLanguage = "fr";
            french.Metadata.Value.AnchorName = "Attack on Titan";
            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.GetAllAuthors())
                  .Returns(new List<Author> { french, _fairyTail });

            Subject.Execute(new ReResolveMetadataCommand { Rebind = true });

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(p => p.ResolveAniList("Attack on Titan", LibraryType.Manga, "Attack On Titan"), Times.Once());
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(p => p.ResolveAniList("L'Attaque des Titans", It.IsAny<LibraryType>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMangaSeriesMetadataProvider>().Verify(p => p.ForgetLookup("Attack on Titan"), Times.Once());
            VerifyWritten("local-attack-on-titan", 53390, Times.Once());

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(p => p.ResolveAniList("Fairy Tail", LibraryType.Manga, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IMangaSeriesMetadataProvider>().Verify(p => p.ForgetLookup("Fairy Tail"), Times.Once());
        }

        [Test]
        public void rebind_for_one_author_touches_only_that_author()
        {
            Subject.Execute(new ReResolveMetadataCommand { Rebind = true, AuthorId = 1 });

            Mocker.GetMock<IAuthorService>().Verify(s => s.GetAllAuthors(), Times.Never());
            VerifyWritten("local-fairy-tail", 30598, Times.Once());
            Mocker.GetMock<IAuthorMetadataService>()
                  .Verify(s => s.Upsert(It.IsAny<AuthorMetadata>()), Times.Once());
            VerifyRefreshed(1, Times.Once());
        }

        [Test]
        public void rebind_logs_every_decision_and_a_summary_at_info()
        {
            Subject.Execute(new ReResolveMetadataCommand { Rebind = true });

            _log.Logs.Should().Contain("Info|Rebind Fairy Tail: 128087 -> 30598 via primary");
            _log.Logs.Should().Contain("Info|Rebind Attack on Titan: 53390 unchanged via primary");
            _log.Logs.Should().Contain("Info|Rebind Let’s Do It Already!: null -> 120768 via primary");
            _log.Logs.Should().Contain("Info|Rebind Frieren: Beyond Journey’s End: 118586 -> unresolved, keeping 118586");
            _log.Logs.Should().Contain("Info|Rebind pass: 2 changed, 1 unchanged, 1 unresolved, 0 failed of 4");
        }

        [Test]
        public void rebind_isolates_a_throwing_author_and_keeps_going()
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(p => p.ResolveAniList("Frieren: Beyond Journey’s End", It.IsAny<LibraryType>(), It.IsAny<string>()))
                  .Throws(new InvalidOperationException("database is locked"));

            Subject.Execute(new ReResolveMetadataCommand { Rebind = true });

            // Frieren is third in name order; Let's Do It Already! after it is still processed.
            VerifyWritten("local-fairy-tail", 30598, Times.Once());
            VerifyWritten("local-let-s-do-it-already", 120768, Times.Once());
            Mocker.GetMock<IAuthorMetadataService>()
                  .Verify(s => s.Upsert(It.IsAny<AuthorMetadata>()), Times.Exactly(2));
            _frieren.Metadata.Value.AniListId.Should().Be(118586);

            _log.Logs.Should().Contain("Warn|Rebind Frieren: Beyond Journey’s End: failed — database is locked");
            _log.Logs.Should().Contain("Info|Rebind pass: 2 changed, 1 unchanged, 0 unresolved, 1 failed of 4");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void rebind_leaves_the_cached_metadata_alone_when_the_write_fails_so_a_retry_writes_it()
        {
            var cached = _fairyTail.Metadata.Value;
            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.GetAllAuthors())
                  .Returns(new List<Author> { _fairyTail });
            Mocker.GetMock<IAuthorMetadataService>()
                  .Setup(s => s.Upsert(It.IsAny<AuthorMetadata>()))
                  .Throws(new InvalidOperationException("database is locked"));

            Subject.Execute(new ReResolveMetadataCommand { Rebind = true });

            // The write was attempted with the new id on a copy, never on the cached instance.
            Mocker.GetMock<IAuthorMetadataService>()
                  .Verify(s => s.Upsert(It.Is<AuthorMetadata>(m => !ReferenceEquals(m, cached) && m.ForeignAuthorId == "local-fairy-tail" && m.AniListId == 30598)), Times.Once());
            cached.AniListId.Should().Be(128087);
            VerifyRefreshed(1, Times.Never());
            Mocker.GetMock<IMangaSeriesMetadataProvider>().Verify(p => p.ForgetLookup("Fairy Tail"), Times.Never());
            _log.Logs.Should().Contain("Warn|Rebind Fairy Tail: failed — database is locked");
            _log.Logs.Should().Contain("Info|Rebind pass: 0 changed, 0 unchanged, 0 unresolved, 1 failed of 1");

            // Same cached list, working repository: the retry still sees a mover and writes it.
            Mocker.GetMock<IAuthorMetadataService>()
                  .Setup(s => s.Upsert(It.IsAny<AuthorMetadata>()))
                  .Returns(true);

            Subject.Execute(new ReResolveMetadataCommand { Rebind = true });

            VerifyWritten("local-fairy-tail", 30598, Times.Exactly(2));
            cached.AniListId.Should().Be(30598);
            VerifyRefreshed(1, Times.Once());
            Mocker.GetMock<IMangaSeriesMetadataProvider>().Verify(p => p.ForgetLookup("Fairy Tail"), Times.Once());
            _log.Logs.Should().Contain("Info|Rebind pass: 1 changed, 0 unchanged, 0 unresolved, 0 failed of 1");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void rebind_runs_in_name_order()
        {
            Subject.Execute(new ReResolveMetadataCommand { Rebind = true });

            _log.Logs.Where(l => l.StartsWith("Info|Rebind ") && !l.StartsWith("Info|Rebind pass"))
                .Select(l => l.Substring("Info|Rebind ".Length).Split(':')[0])
                .Should().Equal("Attack on Titan", "Fairy Tail", "Frieren", "Let’s Do It Already!");
        }

        [Test]
        public void rebind_does_not_clear_dates_or_pages()
        {
            Subject.Execute(new ReResolveMetadataCommand { Rebind = true });

            Mocker.GetMock<IBookService>().Verify(s => s.UpdateMany(It.IsAny<List<Book>>()), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(s => s.UpdateMany(It.IsAny<List<Edition>>()), Times.Never());
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.ClearCache(), Times.Never());
        }

        [Test]
        public void the_existing_re_resolve_path_is_unchanged()
        {
            var book = new Book { Id = 5, ReleaseDate = new DateTime(2016, 7, 26) };
            var edition = new Edition { Id = 6, BookId = 5, ReleaseDate = new DateTime(2016, 7, 26), PageCount = 192 };
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(1)).Returns(new List<Book> { book });
            Mocker.GetMock<IEditionService>().Setup(s => s.GetEditionsByBook(It.IsAny<IEnumerable<int>>())).Returns(new List<Edition> { edition });

            Subject.Execute(new ReResolveMetadataCommand(1));

            book.ReleaseDate.Should().BeNull();
            edition.ReleaseDate.Should().BeNull();
            edition.PageCount.Should().Be(0);
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.ClearCache(), Times.Once());
            VerifyRefreshed(1, Times.Once());
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(p => p.ResolveAniList(It.IsAny<string>(), It.IsAny<LibraryType>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.IsAny<AuthorMetadata>()), Times.Never());
        }

        // Named to sort after the test above: ExceptionVerification misses the first test of a
        // process (NzbDroneLogger's static ctor replaces the NLog config InitLogging just built),
        // so an ExpectedWarns test must not be the fixture's first — see the task report.
        [Test]
        public void the_existing_re_resolve_path_without_an_author_id_warns_and_does_nothing()
        {
            Subject.Execute(new ReResolveMetadataCommand());

            _log.Logs.Should().Contain("Warn|ReResolveMetadata needs an author id unless rebind is set; nothing to do");
            Mocker.GetMock<IBookService>().Verify(s => s.GetBooksByAuthor(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IBookService>().Verify(s => s.UpdateMany(It.IsAny<List<Book>>()), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(s => s.UpdateMany(It.IsAny<List<Edition>>()), Times.Never());
            Mocker.GetMock<IGoogleBooksService>().Verify(s => s.ClearCache(), Times.Never());
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(q => q.Push(It.IsAny<RefreshAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(p => p.ResolveAniList(It.IsAny<string>(), It.IsAny<LibraryType>(), It.IsAny<string>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
