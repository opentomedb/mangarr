using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    [TestFixture]
    public class UpgradeSpecificationFixture : CoreTest<UpgradeSpecification>
    {
        /*
        private Author _author;
        private Book _book;
        private LocalTrack _localTrack;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateNew()
                                     .With(e => e.QualityProfile = new QualityProfile
                                     {
                                         Items = Qualities.QualityFixture.GetDefaultQualities(),
                                     }).Build();

            _book = Builder<Book>.CreateNew().Build();

            _localTrack = new LocalTrack
            {
                Path = @"C:\Test\Imagine Dragons\Imagine.Dragons.Song.1.mp3",
                Quality = new QualityModel(Quality.MP3, new Revision(version: 1)),
                Author = _author,
                Book = _book
            };
        }

        [Test]
        public void should_return_true_if_no_existing_trackFile()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.TrackFileId = 0)
                                                     .With(e => e.TrackFile = null)
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_no_existing_trackFile_for_multi_tracks()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(2)
                                                     .All()
                                                     .With(e => e.TrackFileId = 0)
                                                     .With(e => e.TrackFile = null)
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_upgrade_for_existing_trackFile()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                                                new TrackFile
                                                                                {
                                                                                    Quality = new QualityModel(Quality.MP3, new Revision(version: 1))
                                                                                }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_upgrade_for_existing_trackFile_for_multi_tracks()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(2)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                                                new TrackFile
                                                                                {
                                                                                    Quality = new QualityModel(Quality.MP3, new Revision(version: 1))
                                                                                }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_not_an_upgrade_for_existing_trackFile()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                                                new TrackFile
                                                                                {
                                                                                    Quality = new QualityModel(Quality.FLAC, new Revision(version: 1))
                                                                                }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_not_an_upgrade_for_existing_trackFile_for_multi_tracks()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(2)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                                                new TrackFile
                                                                                {
                                                                                    Quality = new QualityModel(Quality.FLAC, new Revision(version: 1))
                                                                                }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_not_an_upgrade_for_one_existing_trackFile_for_multi_track()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(2)
                                                     .TheFirst(1)
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                                                new TrackFile
                                                                                {
                                                                                    Quality = new QualityModel(Quality.MP3, new Revision(version: 1))
                                                                                }))
                                                     .TheNext(1)
                                                     .With(e => e.TrackFileId = 2)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                                                new TrackFile
                                                                                {
                                                                                    Quality = new QualityModel(Quality.FLAC, new Revision(version: 1))
                                                                                }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_not_a_revision_upgrade_and_prefers_propers()
        {
            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.DownloadPropersAndRepacks)
                  .Returns(ProperDownloadTypes.PreferAndUpgrade);

            _localTrack.Tracks = Builder<Track>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                         new TrackFile
                                                         {
                                                             Quality = new QualityModel(Quality.MP3, new Revision(version: 2))
                                                         }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_true_if_not_a_revision_upgrade_and_does_not_prefer_propers()
        {
            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.DownloadPropersAndRepacks)
                  .Returns(ProperDownloadTypes.DoNotPrefer);

            _localTrack.Tracks = Builder<Track>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                         new TrackFile
                                                         {
                                                             Quality = new QualityModel(Quality.MP3, new Revision(version: 2))
                                                         }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_when_comparing_to_a_lower_quality_proper()
        {
            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.DownloadPropersAndRepacks)
                  .Returns(ProperDownloadTypes.DoNotPrefer);

            _localTrack.Quality = new QualityModel(Quality.FLAC);

            _localTrack.Tracks = Builder<Track>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                         new TrackFile
                                                         {
                                                             Quality = new QualityModel(Quality.FLAC, new Revision(version: 2))
                                                         }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_track_file_is_null()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(2)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(null))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }
        */

        // Light novels (2026-09): a file is judged against the files of the edition it is imported
        // to, in the profile of that edition's media type. The profiles below rank M4B lowest in
        // the main profile (as migration 048 prepends the audio qualities, disallowed) and highest
        // in the audio profile, so the two profiles disagree on every audio comparison.
        private static QualityProfile ProfileOf(params Quality[] lowestFirst)
        {
            return new QualityProfile
            {
                Cutoff = lowestFirst.Last().Id,
                Items = lowestFirst.Select(q => new QualityProfileQualityItem { Quality = q, Allowed = true }).ToList()
            };
        }

        private static BookFile FileOf(int editionId, Quality quality)
        {
            return new BookFile { Id = editionId * 100, EditionId = editionId, Quality = new QualityModel(quality) };
        }

        private static LocalBook GivenLightNovelImport(MediaType targetType, Quality newQuality, BookFile epub, BookFile audio)
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata { ForeignAuthorId = "local-overlord~ln" },
                QualityProfile = ProfileOf(Quality.M4B, Quality.MP3, Quality.EPUB),
                AudioQualityProfile = ProfileOf(Quality.MP3, Quality.M4B)
            };

            var book = new Book { Id = 5, Author = author };
            book.WithEdition(MediaType.Ebook, epub == null ? new List<BookFile>() : new List<BookFile> { epub }, id: 51);
            book.WithEdition(MediaType.Audio, audio == null ? new List<BookFile>() : new List<BookFile> { audio }, id: 52);
            book.BookFiles = new[] { epub, audio }.Where(f => f != null).ToList();

            return new LocalBook
            {
                Path = targetType == MediaType.Audio
                    ? "/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005.m4b"
                    : "/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005.epub",
                Quality = new QualityModel(newQuality),
                Author = author,
                Book = book,
                Edition = book.EditionOf(targetType)
            };
        }

        [Test]
        public void should_judge_an_audio_file_against_the_audio_edition_not_the_epub()
        {
            // the volume has its EPUB; the first audiobook must not be "not an upgrade" of it
            var localBook = GivenLightNovelImport(MediaType.Audio, Quality.M4B, epub: FileOf(51, Quality.EPUB), audio: null);

            Subject.IsSatisfiedBy(localBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_rank_an_audio_file_in_the_audio_profile()
        {
            // M4B over MP3: an upgrade in the audio profile, a downgrade in the main one
            var localBook = GivenLightNovelImport(MediaType.Audio, Quality.M4B, epub: null, audio: FileOf(52, Quality.MP3));

            Subject.IsSatisfiedBy(localBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_an_audio_file_that_is_not_an_upgrade_in_the_audio_profile()
        {
            // MP3 over M4B: a downgrade in the audio profile (the main profile would call it an upgrade)
            var localBook = GivenLightNovelImport(MediaType.Audio, Quality.MP3, epub: null, audio: FileOf(52, Quality.M4B));

            Subject.IsSatisfiedBy(localBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void manga_volume_is_judged_against_its_one_edition_in_its_one_profile()
        {
            // Passes before and after: a manga volume's one Archive edition holds every file.
            var author = new Author { QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() } };
            var existing = FileOf(11, Quality.CBZ);
            var book = new Book { Id = 1, Author = author };
            book.WithEdition(MediaType.Archive, new List<BookFile> { existing }, id: 11);
            book.BookFiles = new List<BookFile> { existing };

            // CBR over CBZ is an upgrade in the default profile (CBZ ranks below CBR)
            var localBook = new LocalBook { Path = "/manga/One Piece/One Piece - Vol 001.cbr", Quality = new QualityModel(Quality.CBR), Author = author, Book = book, Edition = book.EditionOf(MediaType.Archive) };

            Subject.IsSatisfiedBy(localBook, null).Accepted.Should().BeTrue();

            // CBZ over CBR is not
            existing.Quality = new QualityModel(Quality.CBR);
            localBook.Quality = new QualityModel(Quality.CBZ);
            localBook.Path = "/manga/One Piece/One Piece - Vol 001.cbz";
            Subject.IsSatisfiedBy(localBook, null).Accepted.Should().BeFalse();
        }
    }
}
