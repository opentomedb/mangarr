using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // Covered volumes (2026-09-17, D4a): a spanning audiobook import marks the volumes it covers;
    // the mark — whoever wrote it — is cleared when the covering volume's last audio file goes
    // away or its Audio edition is unmonitored.
    [TestFixture]
    public class CoveredVolumeServiceFixture : CoreTest<CoveredVolumeService>
    {
        private const int Sao = 10;
        private const int Overlord = 20;
        private const int Berserk = 30;

        private Author _sao;
        private Author _overlord;
        private Author _berserk;
        private Dictionary<int, Book> _volumes;
        private MemoryTarget _log;

        [SetUp]
        public void Setup()
        {
            _sao = AuthorNamed(Sao, "Sword Art Online", "local-sword-art-online~ln");
            _overlord = AuthorNamed(Overlord, "Overlord", "local-overlord~ln");
            _berserk = AuthorNamed(Berserk, "Berserk", "local-berserk");
            _volumes = Enumerable.Range(1, 4).ToDictionary(n => n, n => Volume(_sao, n));

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByEdition(It.IsAny<int>()))
                  .Returns(new List<BookFile>());

            Mocker.GetMock<IEditionRepository>()
                  .Setup(s => s.GetCoveredBy(It.IsAny<int>(), It.IsAny<double>()))
                  .Returns(new List<Edition>());

            _log = new MemoryTarget("covered") { Layout = "${level}|${message}" };
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

        // The library is read off the foreign id (LibraryTypes.Parse): "~ln" = light novel.
        private static Author AuthorNamed(int metadataId, string name, string foreignAuthorId)
        {
            return new Author
            {
                Id = metadataId,
                AuthorMetadataId = metadataId,
                Metadata = new AuthorMetadata { Id = metadataId, Name = name, ForeignAuthorId = foreignAuthorId }
            };
        }

        // A volume with a fileless, unmarked Ebook and Audio edition, resolvable through the
        // repositories the way the service loads them.
        private Book Volume(Author author, int number)
        {
            var book = Builder<Book>.CreateNew()
                .With(b => b.Id = author.AuthorMetadataId + number)
                .With(b => b.AuthorMetadataId = author.AuthorMetadataId)
                .With(b => b.VolumeNumber = number)
                .With(b => b.Title = $"{author.Name} Vol. {number}")
                .With(b => b.Author = author)
                .Build();

            book.Editions = new List<Edition> { EditionOf(book, MediaType.Ebook), EditionOf(book, MediaType.Audio) };

            Mocker.GetMock<IBookRepository>().Setup(s => s.Find(book.Id)).Returns(book);
            Mocker.GetMock<IBookRepository>().Setup(s => s.Get(book.Id)).Returns(book);

            return book;
        }

        private Edition EditionOf(Book book, MediaType mediaType)
        {
            var edition = Builder<Edition>.CreateNew()
                .With(e => e.Id = (book.Id * 10) + (int)mediaType)
                .With(e => e.BookId = book.Id)
                .With(e => e.MediaType = mediaType)
                .With(e => e.Monitored = true)
                .With(e => e.CoveredByVolume = null)
                .With(e => e.CoveredSource = null)
                .With(e => e.Book = book)
                .Build();

            Mocker.GetMock<IEditionRepository>().Setup(s => s.Find(edition.Id)).Returns(edition);
            Mocker.GetMock<IEditionRepository>().Setup(s => s.Get(edition.Id)).Returns(edition);

            return edition;
        }

        private static Edition Audio(Book book)
        {
            return book.Editions.Value.Single(e => e.MediaType == MediaType.Audio);
        }

        private static Edition Ebook(Book book)
        {
            return book.Editions.Value.Single(e => e.MediaType == MediaType.Ebook);
        }

        private static void Marked(Edition edition, double by, string source)
        {
            edition.CoveredByVolume = by;
            edition.CoveredSource = source;
        }

        private void GivenCoveredBy(int authorMetadataId, double covering, params Edition[] editions)
        {
            Mocker.GetMock<IEditionRepository>()
                  .Setup(s => s.GetCoveredBy(authorMetadataId, covering))
                  .Returns(editions.ToList());
        }

        private void GivenAudioFile(Edition edition)
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByEdition(edition.Id))
                  .Returns(new List<BookFile> { new BookFile { Id = edition.Id * 100, EditionId = edition.Id } });
        }

        // A real event carries the file's quality; the handler gates on its class (M4B = audio, EPUB = ebook).
        private static BookFileDeletedEvent Deleted(Edition edition, DeleteMediaFileReason reason = DeleteMediaFileReason.Manual)
        {
            var quality = edition.MediaType == MediaType.Audio ? Quality.M4B : Quality.EPUB;
            return new BookFileDeletedEvent(new BookFile { Id = edition.Id * 100, EditionId = edition.Id, Quality = new QualityModel(quality) }, reason);
        }

        // The real chain: EditionService.SetMonitored -> the subject -> the repository.
        private EditionService RealEditionService()
        {
            Mocker.SetConstant<ICoveredVolumeService>(Subject);
            return Mocker.Resolve<EditionService>();
        }

        // params Expression<Func<Edition, object>>[] -- match the array (Moq compares expression trees by
        // reference); the two column names are asserted through the selectors' text.
        private void VerifyCoveredWrite(Edition edition, Times times)
        {
            Mocker.GetMock<IEditionRepository>()
                  .Verify(v => v.SetFields(edition, It.Is<Expression<Func<Edition, object>>[]>(p => p.Length == 2 && p[0].ToString().Contains("CoveredByVolume") && p[1].ToString().Contains("CoveredSource"))), times);
        }

        private void VerifyBookEdited(Book book, Times times)
        {
            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.Is<BookEditedEvent>(e => e.Book == book)), times);
        }

        [Test]
        public void marks_every_uncovered_fileless_audio_edition_of_the_span()
        {
            var covered = Audio(_volumes[2]);

            var result = Subject.MarkCoveredByImport(_volumes[1], new[] { _volumes[2] }, "Aincrad");

            result.Should().Equal(covered);
            covered.CoveredByVolume.Should().Be(1);
            covered.CoveredSource.Should().Be(CoveredSources.Import);
            VerifyCoveredWrite(covered, Times.Once());
            VerifyBookEdited(_volumes[2], Times.Once());
            _log.Logs.Should().Contain("Info|Import: Sword Art Online Vol. 2 audio covered by Vol. 1 (Aincrad)");
        }

        [Test]
        public void a_volume_with_an_audio_file_is_not_marked()
        {
            var withFile = Audio(_volumes[3]);
            GivenAudioFile(withFile);

            var result = Subject.MarkCoveredByImport(_volumes[1], new[] { _volumes[2], _volumes[3] }, "Aincrad");

            result.Should().Equal(Audio(_volumes[2]));
            withFile.CoveredByVolume.Should().BeNull();
            withFile.CoveredSource.Should().BeNull();
            VerifyCoveredWrite(withFile, Times.Never());
        }

        [TestCase(CoveredSources.Audible)]
        [TestCase(CoveredSources.Import)]
        [TestCase(CoveredSources.Manual)]
        public void a_volume_already_marked_keeps_its_mark(string source)
        {
            var marked = Audio(_volumes[2]);
            Marked(marked, 3, source);

            var result = Subject.MarkCoveredByImport(_volumes[1], new[] { _volumes[2] }, "Aincrad");

            result.Should().BeEmpty();
            marked.CoveredByVolume.Should().Be(3);
            marked.CoveredSource.Should().Be(source);
            VerifyCoveredWrite(marked, Times.Never());
        }

        [Test]
        public void ebook_editions_are_never_marked()
        {
            var ebook = Ebook(_volumes[2]);

            Subject.MarkCoveredByImport(_volumes[1], new[] { _volumes[2] }, "Aincrad");

            ebook.CoveredByVolume.Should().BeNull();
            ebook.CoveredSource.Should().BeNull();
            Mocker.GetMock<IEditionRepository>().Verify(v => v.SetFields(It.Is<Edition>(e => e.MediaType != MediaType.Audio), It.IsAny<Expression<Func<Edition, object>>[]>()), Times.Never());
        }

        [Test]
        public void deleted_covering_file_clears_import_and_audible_marks()
        {
            var byImport = Audio(_volumes[2]);
            var byAudible = Audio(_volumes[3]);
            Marked(byImport, 1, CoveredSources.Import);
            Marked(byAudible, 1, CoveredSources.Audible);
            GivenCoveredBy(Sao, 1, byImport, byAudible);

            Subject.Handle(Deleted(Audio(_volumes[1])));

            byImport.CoveredByVolume.Should().BeNull();
            byImport.CoveredSource.Should().BeNull();
            byAudible.CoveredByVolume.Should().BeNull();
            byAudible.CoveredSource.Should().BeNull();
            VerifyCoveredWrite(byImport, Times.Once());
            VerifyCoveredWrite(byAudible, Times.Once());
            VerifyBookEdited(_volumes[2], Times.Once());
            VerifyBookEdited(_volumes[3], Times.Once());
            _log.Logs.Should().Contain("Info|Covered mark cleared on Sword Art Online Vol. 2 (covering Vol. 1 file deleted)");
            _log.Logs.Should().Contain("Info|Covered mark cleared on Sword Art Online Vol. 3 (covering Vol. 1 file deleted)");
        }

        [Test]
        public void a_deleted_file_with_files_remaining_clears_nothing()
        {
            var covering = Audio(_volumes[1]);
            var covered = Audio(_volumes[2]);
            Marked(covered, 1, CoveredSources.Import);
            GivenCoveredBy(Sao, 1, covered);
            GivenAudioFile(covering);

            Subject.Handle(Deleted(covering));

            covered.CoveredByVolume.Should().Be(1);
            covered.CoveredSource.Should().Be(CoveredSources.Import);
            Mocker.GetMock<IEditionRepository>().Verify(v => v.GetCoveredBy(It.IsAny<int>(), It.IsAny<double>()), Times.Never());
            VerifyCoveredWrite(covered, Times.Never());
        }

        [Test]
        public void a_deleted_ebook_file_clears_nothing()
        {
            var covered = Audio(_volumes[2]);
            Marked(covered, 1, CoveredSources.Import);
            GivenCoveredBy(Sao, 1, covered);

            Subject.Handle(Deleted(Ebook(_volumes[1])));

            covered.CoveredByVolume.Should().Be(1);
            Mocker.GetMock<IEditionRepository>().Verify(v => v.GetCoveredBy(It.IsAny<int>(), It.IsAny<double>()), Times.Never());
            VerifyCoveredWrite(covered, Times.Never());
        }

        // D7 (M2): a manga file — every CBZ of a rescan — returns on its quality class alone, before
        // any lookup; a manga book delete never reads the marks table. Manga cost unchanged.
        [Test]
        public void a_deleted_manga_file_is_never_looked_up()
        {
            var cbz = new BookFile { Id = 99900, EditionId = 999, Quality = new QualityModel(Quality.CBZ) };

            Subject.Handle(new BookFileDeletedEvent(cbz, DeleteMediaFileReason.Manual));

            Mocker.GetMock<IEditionRepository>().Verify(v => v.Find(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.GetFilesByEdition(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IEditionRepository>().Verify(v => v.GetCoveredBy(It.IsAny<int>(), It.IsAny<double>()), Times.Never());
        }

        [Test]
        public void deleting_a_manga_book_never_looks_up_marks()
        {
            var berserkVol1 = Volume(_berserk, 1);

            Subject.Handle(new BookDeletedEvent(berserkVol1, true, false));

            Mocker.GetMock<IEditionRepository>().Verify(v => v.GetCoveredBy(It.IsAny<int>(), It.IsAny<double>()), Times.Never());
        }

        // The gate reads only what the event's copy already carries — loading a lazy to decide
        // would be the query the gate is there to save. Neither loaded (a repository-loaded book
        // published by DeleteMany: lazies prepared, not loaded) = unknown, and the clear runs.
        [Test]
        public void deleting_a_book_whose_library_is_not_loaded_still_clears()
        {
            var covered = Audio(_volumes[2]);
            Marked(covered, 1, CoveredSources.Import);
            GivenCoveredBy(Sao, 1, covered);
            _volumes[1].Author = new LazyLoaded<Author>();
            _volumes[1].AuthorMetadata = new LazyLoaded<AuthorMetadata>();

            Subject.Handle(new BookDeletedEvent(_volumes[1], true, false));

            covered.CoveredByVolume.Should().BeNull();
            VerifyCoveredWrite(covered, Times.Once());
        }

        [Test]
        public void unmonitoring_the_covering_audio_edition_clears()
        {
            var covering = Audio(_volumes[1]);
            var covered = Audio(_volumes[2]);
            Marked(covered, 1, CoveredSources.Import);
            GivenCoveredBy(Sao, 1, covered);

            var editionService = RealEditionService();

            editionService.SetMonitored(covering.Id, true);
            Mocker.GetMock<IEditionRepository>().Verify(v => v.GetCoveredBy(It.IsAny<int>(), It.IsAny<double>()), Times.Never());

            editionService.SetMonitored(covering.Id, false);

            covered.CoveredByVolume.Should().BeNull();
            covered.CoveredSource.Should().BeNull();
            VerifyCoveredWrite(covered, Times.Once());
            VerifyBookEdited(_volumes[2], Times.Once());
            _log.Logs.Should().Contain("Info|Covered mark cleared on Sword Art Online Vol. 2 (covering Vol. 1 unmonitored)");
        }

        [Test]
        public void another_authors_marks_are_untouched()
        {
            var overlordVol1 = Volume(_overlord, 1);
            var overlordVol2 = Volume(_overlord, 2);
            var overlordCovered = Audio(overlordVol2);
            var saoCovered = Audio(_volumes[2]);
            Marked(overlordCovered, 1, CoveredSources.Audible);
            Marked(saoCovered, 1, CoveredSources.Import);
            GivenCoveredBy(Overlord, 1, overlordCovered);
            GivenCoveredBy(Sao, 1, saoCovered);

            // SAO Vol 1's audio file goes away: only SAO's marks are looked up and cleared.
            Subject.Handle(Deleted(Audio(_volumes[1])));

            saoCovered.CoveredByVolume.Should().BeNull();
            overlordCovered.CoveredByVolume.Should().Be(1);
            overlordCovered.CoveredSource.Should().Be(CoveredSources.Audible);
            Mocker.GetMock<IEditionRepository>().Verify(v => v.GetCoveredBy(Overlord, It.IsAny<double>()), Times.Never());
            VerifyCoveredWrite(overlordCovered, Times.Never());

            // Nor does a SAO import mark a volume of another series.
            Subject.MarkCoveredByImport(_volumes[1], new[] { overlordVol1 }, "Aincrad").Should().BeEmpty();
            Audio(overlordVol1).CoveredByVolume.Should().BeNull();
        }

        // An upgrade deletes the old file before the replacement lands, so the marks clear here too
        // (the sibling BookFileDeletedEvent handlers early-return on Upgrade; this one must not — a
        // narrower replacement would otherwise leave stale marks nothing clears).
        [Test]
        public void an_upgrade_delete_clears_too()
        {
            var covered = Audio(_volumes[2]);
            Marked(covered, 1, CoveredSources.Import);
            GivenCoveredBy(Sao, 1, covered);

            Subject.Handle(Deleted(Audio(_volumes[1]), DeleteMediaFileReason.Upgrade));

            covered.CoveredByVolume.Should().BeNull();
            covered.CoveredSource.Should().BeNull();
            VerifyCoveredWrite(covered, Times.Once());
            _log.Logs.Should().Contain("Info|Covered mark cleared on Sword Art Online Vol. 2 (covering Vol. 1 file deleted)");
        }

        [Test]
        public void deleting_the_covering_book_clears()
        {
            var byImport = Audio(_volumes[2]);
            var byAudible = Audio(_volumes[3]);
            Marked(byImport, 1, CoveredSources.Import);
            Marked(byAudible, 1, CoveredSources.Audible);
            GivenCoveredBy(Sao, 1, byImport, byAudible);

            Subject.Handle(new BookDeletedEvent(_volumes[1], true, false));

            byImport.CoveredByVolume.Should().BeNull();
            byAudible.CoveredByVolume.Should().BeNull();
            VerifyCoveredWrite(byImport, Times.Once());
            VerifyCoveredWrite(byAudible, Times.Once());
            VerifyBookEdited(_volumes[2], Times.Once());
            VerifyBookEdited(_volumes[3], Times.Once());
            _log.Logs.Should().Contain("Info|Covered mark cleared on Sword Art Online Vol. 2 (covering Vol. 1 book deleted)");
            _log.Logs.Should().Contain("Info|Covered mark cleared on Sword Art Online Vol. 3 (covering Vol. 1 book deleted)");
        }

        [Test]
        public void a_book_without_a_loaded_editions_lazy_is_looked_up()
        {
            var covered = Audio(_volumes[2]);
            var ebook = Ebook(_volumes[2]);
            _volumes[2].Editions = null;
            Mocker.GetMock<IEditionRepository>()
                  .Setup(s => s.FindByBook(It.Is<IEnumerable<int>>(ids => ids.Single() == _volumes[2].Id)))
                  .Returns(new List<Edition> { ebook, covered });

            var result = Subject.MarkCoveredByImport(_volumes[1], new[] { _volumes[2] }, "Aincrad");

            result.Should().Equal(covered);
            covered.CoveredByVolume.Should().Be(1);
            covered.CoveredSource.Should().Be(CoveredSources.Import);
            ebook.CoveredByVolume.Should().BeNull();
        }

        [Test]
        public void a_deleted_file_whose_edition_row_is_gone_uses_the_events_copy()
        {
            var covering = Audio(_volumes[1]);
            var covered = Audio(_volumes[2]);
            Marked(covered, 1, CoveredSources.Import);
            GivenCoveredBy(Sao, 1, covered);
            Mocker.GetMock<IEditionRepository>().Setup(s => s.Find(covering.Id)).Returns((Edition)null);

            var file = new BookFile { Id = covering.Id * 100, EditionId = covering.Id, Edition = covering, Quality = new QualityModel(Quality.M4B) };
            Subject.Handle(new BookFileDeletedEvent(file, DeleteMediaFileReason.Manual));

            covered.CoveredByVolume.Should().BeNull();
            VerifyCoveredWrite(covered, Times.Once());
        }

        [Test]
        public void a_deleted_file_with_no_resolvable_edition_clears_nothing()
        {
            var covering = Audio(_volumes[1]);
            var covered = Audio(_volumes[2]);
            Marked(covered, 1, CoveredSources.Import);
            GivenCoveredBy(Sao, 1, covered);
            Mocker.GetMock<IEditionRepository>().Setup(s => s.Find(covering.Id)).Returns((Edition)null);

            Subject.Handle(Deleted(covering));

            covered.CoveredByVolume.Should().Be(1);
            Mocker.GetMock<IEditionRepository>().Verify(v => v.GetCoveredBy(It.IsAny<int>(), It.IsAny<double>()), Times.Never());
            VerifyCoveredWrite(covered, Times.Never());
        }

        [Test]
        public void the_carrier_itself_is_never_marked()
        {
            var carrier = Audio(_volumes[1]);

            var result = Subject.MarkCoveredByImport(_volumes[1], new[] { _volumes[1], _volumes[2] }, "Aincrad");

            result.Should().Equal(Audio(_volumes[2]));
            carrier.CoveredByVolume.Should().BeNull();
            VerifyCoveredWrite(carrier, Times.Never());
        }

        // An Audio edition that gains its own file drops its own mark (2026-09-17 ruling): a single
        // imported by hand for a covered volume takes over from the pack.
        private static BookFileAddedEvent Added(Edition edition, Quality quality)
        {
            return new BookFileAddedEvent(new BookFile { Id = edition.Id * 100 + 1, EditionId = edition.Id, Quality = new QualityModel(quality) });
        }

        [Test]
        public void an_audio_edition_that_gains_its_own_file_drops_its_mark()
        {
            var covered = Audio(_volumes[2]);
            Marked(covered, 1, CoveredSources.Import);

            Subject.Handle(Added(covered, Quality.M4B));

            covered.CoveredByVolume.Should().BeNull();
            covered.CoveredSource.Should().BeNull();
            VerifyCoveredWrite(covered, Times.Once());
            VerifyBookEdited(_volumes[2], Times.Once());
            _log.Logs.Should().Contain("Info|Covered mark cleared on Sword Art Online Vol. 2 (own audio file imported)");
        }

        [Test]
        public void an_added_ebook_file_clears_nothing()
        {
            var covered = Audio(_volumes[2]);
            Marked(covered, 1, CoveredSources.Import);

            Subject.Handle(Added(Ebook(_volumes[2]), Quality.EPUB));

            covered.CoveredByVolume.Should().Be(1);
            VerifyCoveredWrite(covered, Times.Never());
            Mocker.GetMock<IEditionRepository>().Verify(v => v.Find(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void an_added_audio_file_on_an_uncovered_edition_clears_nothing()
        {
            var uncovered = Audio(_volumes[3]);

            Subject.Handle(Added(uncovered, Quality.MP3));

            uncovered.CoveredByVolume.Should().BeNull();
            VerifyCoveredWrite(uncovered, Times.Never());
            VerifyBookEdited(_volumes[3], Times.Never());
        }

        [Test]
        public void unmonitoring_an_ebook_edition_never_clears()
        {
            var covered = Audio(_volumes[2]);
            Marked(covered, 1, CoveredSources.Import);
            GivenCoveredBy(Sao, 1, covered);

            RealEditionService().SetMonitored(Ebook(_volumes[1]).Id, false);

            covered.CoveredByVolume.Should().Be(1);
            covered.CoveredSource.Should().Be(CoveredSources.Import);
            Mocker.GetMock<IEditionRepository>().Verify(v => v.GetCoveredBy(It.IsAny<int>(), It.IsAny<double>()), Times.Never());
            VerifyCoveredWrite(covered, Times.Never());
        }
    }
}
