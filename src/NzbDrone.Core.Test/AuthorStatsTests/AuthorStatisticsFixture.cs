using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AuthorStatsTests
{
    [TestFixture]
    public class AuthorStatisticsFixture : DbTest<AuthorStatisticsRepository, Author>
    {
        private Author _author;
        private Book _book;
        private Edition _edition;
        private List<BookFile> _bookFiles;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateNew()
                .With(a => a.AuthorMetadataId = 10)
                .BuildNew();
            Db.Insert(_author);

            _book = Builder<Book>.CreateNew()
                .With(e => e.ReleaseDate = DateTime.Today.AddDays(-5))
                .With(e => e.AuthorMetadataId = 10)
                .BuildNew();
            Db.Insert(_book);

            _edition = Builder<Edition>.CreateNew()
                .With(e => e.BookId = _book.Id)
                .With(e => e.MediaType = MediaType.Archive)
                .With(e => e.Monitored = true)
                .BuildNew();
            Db.Insert(_edition);

            _bookFiles = Builder<BookFile>.CreateListOfSize(2)
                .All()
                .With(x => x.Id = 0)
                .With(e => e.Author = _author)
                .With(e => e.Edition = _edition)
                .With(e => e.EditionId = _edition.Id)
                .With(e => e.Quality = new QualityModel(Quality.MP3))
                .BuildList();
        }

        private void GivenBookFile()
        {
            Db.Insert(_bookFiles[0]);
        }

        private void GivenTwoBookFiles()
        {
            Db.InsertMany(_bookFiles);
        }

        [Test]
        public void should_get_stats_for_author()
        {
            var stats = Subject.AuthorStatistics();

            stats.Should().HaveCount(1);
        }

        [Test]
        public void should_not_include_unmonitored_book_in_book_count()
        {
            var stats = Subject.AuthorStatistics();

            stats.Should().HaveCount(1);
            stats.First().BookCount.Should().Be(0);
        }

        [Test]
        public void should_include_unmonitored_book_with_file_in_book_count()
        {
            GivenBookFile();

            var stats = Subject.AuthorStatistics();

            stats.Should().HaveCount(1);
            stats.First().BookCount.Should().Be(1);
        }

        [Test]
        public void should_have_size_on_disk_of_zero_when_no_book_file()
        {
            var stats = Subject.AuthorStatistics();

            stats.Should().HaveCount(1);
            stats.First().SizeOnDisk.Should().Be(0);
        }

        [Test]
        public void should_have_size_on_disk_when_book_file_exists()
        {
            GivenBookFile();

            var stats = Subject.AuthorStatistics();

            stats.Should().HaveCount(1);
            stats.First().SizeOnDisk.Should().Be(_bookFiles[0].Size);
        }

        [Test]
        public void should_count_book_with_two_files_as_one_book()
        {
            GivenTwoBookFiles();

            var stats = Subject.AuthorStatistics();

            Db.All<BookFile>().Should().HaveCount(2);
            stats.Should().HaveCount(1);

            var bookStats = stats.First();

            bookStats.TotalBookCount.Should().Be(1);
            bookStats.BookCount.Should().Be(1);
            bookStats.AvailableBookCount.Should().Be(1);
            bookStats.SizeOnDisk.Should().Be(_bookFiles.Sum(x => x.Size));
            bookStats.BookFileCount.Should().Be(2);
        }

        private Edition GivenAudioEdition(Book book, double? coveredBy = null)
        {
            var audio = Builder<Edition>.CreateNew()
                .With(e => e.BookId = book.Id)
                .With(e => e.ForeignEditionId = book.Id + "-audio-ed")
                .With(e => e.TitleSlug = book.Id + "-audio-ed")
                .With(e => e.MediaType = MediaType.Audio)
                .With(e => e.Monitored = true)
                .With(e => e.CoveredByVolume = coveredBy)
                .With(e => e.Asin = book.Id + "-asin")                          // released audio by default
                .With(e => e.AudioReleaseDate = DateTime.UtcNow.AddDays(-10))
                .BuildNew();
            Db.Insert(audio);
            return audio;
        }

        private Book GivenReleasedVolume(string id)
        {
            var released = Builder<Book>.CreateNew()
                .With(b => b.ReleaseDate = DateTime.Today.AddDays(-5))
                .With(b => b.AuthorMetadataId = 10)
                .With(b => b.Monitored = true)
                .With(b => b.ForeignBookId = id)
                .With(b => b.TitleSlug = id)
                .BuildNew();
            Db.Insert(released);
            return released;
        }

        private void GivenSeriesAudio(bool available)
        {
            _author.AudioAvailable = available;
            Db.Update(_author);
        }

        [Test]
        public void an_audio_edition_audible_lists_for_a_future_date_is_not_counted_while_the_series_has_audio()
        {
            GivenSeriesAudio(true);
            var volume = GivenReleasedVolume("preorder");
            var audio = GivenAudioEdition(volume);
            audio.AudioReleaseDate = DateTime.UtcNow.AddDays(40);
            Db.Update(audio);

            Subject.AuthorStatistics().Single(s => s.BookId == volume.Id).AudioBookCount.Should().Be(0);
        }

        [Test]
        public void an_audio_edition_audible_does_not_list_is_not_counted_while_the_series_has_audio()
        {
            GivenSeriesAudio(true);
            var volume = GivenReleasedVolume("unlisted");
            var audio = GivenAudioEdition(volume);
            audio.Asin = null;
            audio.AudioReleaseDate = null;
            Db.Update(audio);

            Subject.AuthorStatistics().Single(s => s.BookId == volume.Id).AudioBookCount.Should().Be(0);
        }

        [Test]
        public void a_pending_series_counts_its_unlisted_audio_editions_as_before()
        {
            GivenSeriesAudio(false);
            var volume = GivenReleasedVolume("pending");
            var audio = GivenAudioEdition(volume);
            audio.Asin = null;
            audio.AudioReleaseDate = null;
            Db.Update(audio);

            Subject.AuthorStatistics().Single(s => s.BookId == volume.Id).AudioBookCount.Should().Be(1);
        }

        [Test]
        public void an_unreleased_audio_edition_with_a_file_still_counts()
        {
            GivenSeriesAudio(true);
            var volume = GivenReleasedVolume("early");
            var audio = GivenAudioEdition(volume);
            audio.AudioReleaseDate = DateTime.UtcNow.AddDays(40);
            Db.Update(audio);
            GivenAudioFile(audio, "/x/early.m4b");

            var perBook = Subject.AuthorStatistics().Single(s => s.BookId == volume.Id);
            perBook.AudioBookCount.Should().Be(1);
            perBook.AudioBookFileCount.Should().Be(1);
        }

        private void GivenAudioFile(Edition edition, string path = "/lightnovels/X/X - Vol. 1/X - Vol 001.mp3")
        {
            Db.Insert(new BookFile
            {
                EditionId = edition.Id,
                Path = path,
                Quality = new QualityModel(Quality.MP3),
                Size = 100,
                DateAdded = DateTime.UtcNow
            });
        }

        [Test]
        public void audio_files_count_on_the_audio_side_not_the_primary_side()
        {
            var audio = GivenAudioEdition(_book);
            GivenAudioFile(audio);

            var stats = Subject.AuthorStatistics().Single();

            stats.BookFileCount.Should().Be(0);
            stats.AvailableBookCount.Should().Be(0);
            stats.AudioBookCount.Should().Be(1);
            stats.AudioBookFileCount.Should().Be(1);
            stats.BookCount.Should().Be(1);
            stats.SizeOnDisk.Should().Be(100);
        }

        [Test]
        public void a_three_part_audiobook_is_one_audio_book_file()
        {
            var audio = GivenAudioEdition(_book);
            GivenAudioFile(audio, "/x/part1.mp3");
            GivenAudioFile(audio, "/x/part2.mp3");
            GivenAudioFile(audio, "/x/part3.mp3");
            GivenBookFile();

            var stats = Subject.AuthorStatistics().Single();

            stats.BookFileCount.Should().Be(1);
            stats.AvailableBookCount.Should().Be(1);
            stats.AudioBookFileCount.Should().Be(1);
            stats.SizeOnDisk.Should().Be(300 + _bookFiles[0].Size);
        }

        [Test]
        public void a_monitored_audio_edition_without_a_file_counts_as_a_wanted_audio_book_once_released()
        {
            var released = Builder<Book>.CreateNew()
                .With(b => b.ReleaseDate = DateTime.Today.AddDays(-5))
                .With(b => b.AuthorMetadataId = 10)
                .With(b => b.Monitored = true)
                .With(b => b.ForeignBookId = "released")
                .With(b => b.TitleSlug = "released")
                .BuildNew();
            Db.Insert(released);
            GivenAudioEdition(released);

            // Subject is the repository: one BookStatistics row per (author, book)
            var perBook = Subject.AuthorStatistics().Single(s => s.BookId == released.Id);

            perBook.AudioBookCount.Should().Be(1);
            perBook.AudioBookFileCount.Should().Be(0);
            perBook.BookFileCount.Should().Be(0);
        }

        [Test]
        public void a_manga_volume_reports_no_audio()
        {
            GivenBookFile();

            var stats = Subject.AuthorStatistics().Single();

            stats.AudioBookCount.Should().Be(0);
            stats.AudioBookFileCount.Should().Be(0);
            stats.BookFileCount.Should().Be(1);
        }

        [Test]
        public void the_service_sums_the_audio_side_and_derives_audio_available()
        {
            var audio = GivenAudioEdition(_book);
            GivenAudioFile(audio);

            Mocker.SetConstant<IAuthorStatisticsRepository>(Subject);
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            var service = Mocker.Resolve<AuthorStatisticsService>();

            var stats = service.AuthorStatistics(_author.Id);

            stats.AudioBookCount.Should().Be(1);
            stats.AudioBookFileCount.Should().Be(1);
            stats.AudioAvailable.Should().BeTrue();
            stats.BookFileCount.Should().Be(0);
        }

        // Covered volumes (2026-09-17, D8): a covered Audio edition is satisfied without a file of
        // its own -- the audiobook is inside another volume's file.

        [Test]
        public void a_covered_audio_edition_without_a_file_counts_as_satisfied()
        {
            GivenAudioEdition(_book, coveredBy: 1);

            var stats = Subject.AuthorStatistics().Single();

            stats.AudioBookFileCount.Should().Be(1);
            stats.AudioBookCount.Should().Be(1);
            stats.BookFileCount.Should().Be(0);
            stats.AvailableBookCount.Should().Be(0);
            stats.SizeOnDisk.Should().Be(0);
        }

        [Test]
        public void an_uncovered_audio_edition_without_a_file_is_not_satisfied()
        {
            GivenAudioEdition(_book);

            var stats = Subject.AuthorStatistics().Single();

            stats.AudioBookFileCount.Should().Be(0);
            stats.AudioBookCount.Should().Be(0);
        }
    }
}
