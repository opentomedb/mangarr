using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.Qualities;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    // Volumes built from chapter packs (2026-10-21): a chapter release maps to the volumes it
    // completes on its own -- from the search bridge, and from RSS where the series is only known
    // once the author is resolved.
    [TestFixture]
    public class ChapterReleaseMappingFixture : CoreTest<ParsingService>
    {
        private const string Title = "Marriagetoxin c001-073 (Digital) (empyrealarrow)";

        private static readonly (int Volume, int First, int Last)[] Layout =
        {
            (1, 1, 6), (2, 7, 15), (3, 16, 24), (4, 25, 33), (5, 34, 42),
            (6, 43, 52), (7, 53, 61), (8, 62, 71), (9, 72, 81)
        };

        private Author _author;
        private List<Book> _books;
        private List<Edition> _editions;

        [SetUp]
        public void Setup()
        {
            _author = new Author
            {
                Id = 1,
                AuthorMetadataId = 1,
                Metadata = new AuthorMetadata { Name = "Marriagetoxin", ForeignAuthorId = "local-marriagetoxin" }
            };

            var books = Layout.Select(v => new Book { Id = v.Volume, VolumeNumber = v.Volume, Title = $"Marriagetoxin, Vol. {v.Volume}", AuthorMetadataId = 1, Monitored = true }).ToList();
            _books = books;
            var editions = _editions = Layout.Select(v => new Edition
            {
                Id = v.Volume,
                BookId = v.Volume,
                MediaType = MediaType.Archive,
                Monitored = true,
                Chapters = Enumerable.Range(v.First, v.Last - v.First + 1).Select(n => (double)n).ToList(),
                BookFiles = new List<BookFile>()
            }).ToList();

            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(_author.Id)).Returns(books);
            Mocker.GetMock<IBookService>().Setup(s => s.GetCandidates(It.IsAny<int>(), It.IsAny<string>())).Returns(new List<Book>());
            Mocker.GetMock<IEditionService>().Setup(s => s.GetEditionsByAuthor(_author.Id)).Returns(editions);
            Mocker.GetMock<IEditionService>().Setup(s => s.GetCandidates(It.IsAny<int>(), It.IsAny<string>())).Returns(new List<Edition>());
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindByName("Marriagetoxin", LibraryType.Manga)).Returns(_author);
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetReportCandidates(It.IsAny<string>())).Returns(new List<Author> { _author });

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(_author.Id)).Returns(new List<BookFile>());
            Mocker.GetMock<IHistoryService>().Setup(s => s.GetByAuthor(_author.Id, null)).Returns(new List<EntityHistory>());

            GivenFeature(true);
        }

        // Volumes 1..last owned as CBZ (the archive edition has a file).
        private void GivenOwned(int last)
        {
            var files = _editions.Where(e => e.Id <= last).Select(e => new BookFile { EditionId = e.Id, Quality = new QualityModel(Quality.CBZ) }).ToList();

            foreach (var edition in _editions)
            {
                edition.BookFiles = files.Where(f => f.EditionId == edition.Id).ToList();
            }

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(_author.Id)).Returns(files);
        }

        private void GivenFeature(bool on)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.BuildVolumesFromChapters).Returns(on);
        }

        // What DownloadDecisionMaker does with an RSS release: generic parse, else fuzzy, then Map.
        private RemoteBook MapRss(string title)
        {
            var parsed = Parser.Parser.ParseBookTitle(title) ?? Subject.ParseBookTitleFuzzy(title);

            parsed.Should().NotBeNull();

            return Subject.Map(parsed);
        }

        [Test]
        public void search_bridged_chapter_release_maps_to_the_volumes_it_completes()
        {
            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria(Title, _author, new List<Book>());

            var books = Subject.GetBooks(parsed, _author);

            books.Select(b => b.VolumeNumber).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        }

        [TestCase(Title)]
        [TestCase("Marriagetoxin - c001-073 (Digital) (empyrealarrow)")]
        public void rss_chapter_release_maps_to_the_volumes_it_completes(string title)
        {
            var remoteBook = MapRss(title);

            remoteBook.Author.Should().Be(_author);
            remoteBook.Books.Select(b => b.VolumeNumber).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
            remoteBook.ParsedBookInfo.ChapterRange.Start.Should().Be(1);
            remoteBook.ParsedBookInfo.ChapterRange.End.Should().Be(73);
            remoteBook.ParsedBookInfo.Quality.Quality.Should().Be(Quality.CbzChapters);
            remoteBook.MediaType.Should().Be(MediaType.Archive);
        }

        [Test]
        public void chapter_range_maps_nothing_when_the_feature_is_off()
        {
            GivenFeature(false);

            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria(Title, _author, new List<Book>());

            Subject.GetBooks(parsed, _author).Should().BeEmpty();
        }

        [Test]
        public void hyphenated_rss_chapter_release_gets_no_chapter_range_when_the_feature_is_off()
        {
            GivenFeature(false);

            var parsed = Parser.Parser.ParseBookTitle("Marriagetoxin - c001-073 (Digital) (empyrealarrow)");
            var remoteBook = Subject.Map(parsed);

            remoteBook.ParsedBookInfo.ChapterRange.Should().BeNull();
        }

        [Test]
        public void chapter_range_maps_nothing_for_a_light_novel()
        {
            _author.Metadata.Value.ForeignAuthorId = "local-marriagetoxin~ln";

            var parsed = new ParsedBookInfo
            {
                AuthorName = "Marriagetoxin",
                BookTitle = "Marriagetoxin",
                ReleaseTitle = Title,
                Quality = new QualityModel(Quality.CBZ)
            };

            Subject.GetBooks(parsed, _author);

            parsed.ChapterRange.Should().BeNull();
        }

        [Test]
        public void partly_owned_series_maps_to_the_missing_volume_and_passes_the_spec_chain()
        {
            GivenOwned(7);
            _author.QualityProfile = new NzbDrone.Core.Profiles.Qualities.QualityProfile
            {
                UpgradeAllowed = true,
                Cutoff = Quality.CBZ.Id,
                Items = QualityFixture.GetDefaultQualities()
            };
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(s => s.ParseCustomFormat(It.IsAny<BookFile>())).Returns(new List<CustomFormat>());

            var remoteBook = MapRss(Title);
            remoteBook.Release = new ReleaseInfo { Title = Title };
            remoteBook.CustomFormats = new List<CustomFormat>();

            remoteBook.Books.Select(b => b.VolumeNumber).Should().Equal(8);
            Mocker.Resolve<ChapterReleaseSpecification>().IsSatisfiedBy(remoteBook, null).Accepted.Should().BeTrue();
            Mocker.Resolve<NonVolumeContentSpecification>().IsSatisfiedBy(remoteBook, null).Accepted.Should().BeTrue();
            Mocker.Resolve<UpgradeDiskSpecification>().IsSatisfiedBy(remoteBook, null).Accepted.Should().BeTrue();
        }

        // Vol. 10 = chapters 82-90: a c001-090 pack searched for Vol. 10 with Vols. 1-9 unmonitored.
        private void GivenVolumeTen()
        {
            _books.Add(new Book { Id = 10, VolumeNumber = 10, Title = "Marriagetoxin, Vol. 10", AuthorMetadataId = 1, Monitored = true });
            _editions.Add(new Edition
            {
                Id = 10,
                BookId = 10,
                MediaType = MediaType.Archive,
                Monitored = true,
                Chapters = Enumerable.Range(82, 9).Select(n => (double)n).ToList(),
                BookFiles = new List<BookFile>()
            });
        }

        [Test]
        public void search_maps_a_pack_to_the_monitored_volume_only()
        {
            GivenVolumeTen();
            _books.Where(b => b.VolumeNumber < 10).ToList().ForEach(b => b.Monitored = false);

            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria("Marriagetoxin c001-090 (Digital) (empyrealarrow)", _author, new List<Book>());

            Subject.GetBooks(parsed, _author).Select(b => b.VolumeNumber).Should().Equal(10);
        }

        [Test]
        public void rss_pack_skips_an_unmonitored_volume_or_edition_and_maps_the_rest()
        {
            _books.Single(b => b.VolumeNumber == 3).Monitored = false;
            _editions.Single(e => e.BookId == 5).Monitored = false;

            var remoteBook = MapRss(Title);

            remoteBook.Books.Select(b => b.VolumeNumber).Should().Equal(1, 2, 4, 6, 7, 8);
        }

        // c001-081 promised Vol. 9, but its download lacked a chapter: Vols. 1-8 were built, Vol. 9 skipped.
        // The same release now maps to Vol. 9 alone and is rejected, never fetched again for it.
        [Test]
        public void a_partly_built_release_is_rejected_for_the_volume_it_skipped()
        {
            const string partial = "Marriagetoxin c001-081 (Digital) (empyrealarrow)";
            GivenOwned(8);
            Mocker.GetMock<IHistoryService>().Setup(s => s.GetByAuthor(_author.Id, null)).Returns(new List<EntityHistory>
            {
                new EntityHistory { AuthorId = 1, BookId = 1, EventType = EntityHistoryEventType.Grabbed, DownloadId = "abc123", SourceTitle = partial },
                new EntityHistory { AuthorId = 1, BookId = 1, EventType = EntityHistoryEventType.BookFileImported, DownloadId = "abc123" }
            });

            var remoteBook = MapRss(partial);
            remoteBook.Release = new ReleaseInfo { Title = partial };

            remoteBook.Books.Select(b => b.VolumeNumber).Should().Equal(9);
            var decision = Mocker.Resolve<ChapterReleaseSpecification>().IsSatisfiedBy(remoteBook, null);
            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Chapter release already built the volumes it completes");
        }

        [Test]
        public void fully_owned_series_maps_to_nothing_and_completes_no_volume()
        {
            GivenOwned(9);

            var remoteBook = MapRss(Title);
            remoteBook.Release = new ReleaseInfo { Title = Title };

            remoteBook.Books.Should().BeEmpty();
            var decision = Mocker.Resolve<ChapterReleaseSpecification>().IsSatisfiedBy(remoteBook, null);
            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Chapter release completes no volume");
        }

        [Test]
        public void another_series_chapter_release_is_not_mapped_to_a_candidate()
        {
            var parsed = new ParsedBookInfo { AuthorName = "Kaiju No 8 c001", BookTitle = "073", ReleaseTitle = "Kaiju No 8 c001-073 (Digital) (empyrealarrow)", Quality = new QualityModel(Quality.CBZ) };
            var remoteBook = Subject.Map(parsed);

            remoteBook.Author.Should().BeNull();
            parsed.ChapterRange.Should().BeNull();
        }

        [Test]
        public void a_title_with_no_chapter_token_never_looks_up_candidates()
        {
            var parsed = new ParsedBookInfo { AuthorName = "Unknown", BookTitle = "Unknown", ReleaseTitle = "Marriagetoxin (Digital) (empyrealarrow)", Quality = new QualityModel(Quality.CBZ) };

            Subject.Map(parsed).Author.Should().BeNull();

            Mocker.GetMock<IAuthorService>().Verify(s => s.GetReportCandidates(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_volume_release_never_looks_up_candidates()
        {
            var parsed = new ParsedBookInfo { AuthorName = "Unknown", BookTitle = "Unknown", ReleaseTitle = "Marriagetoxin v01 (Digital) (empyrealarrow)", VolumeNumber = 1, Quality = new QualityModel(Quality.CBZ) };

            Subject.Map(parsed).Author.Should().BeNull();

            parsed.ChapterRange.Should().BeNull();
            Mocker.GetMock<IAuthorService>().Verify(s => s.GetReportCandidates(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_series_with_the_feature_off_is_skipped_as_a_candidate()
        {
            _author.BuildFromChapters = false;

            var parsed = new ParsedBookInfo { AuthorName = "Unknown", BookTitle = "Unknown", ReleaseTitle = Title, Quality = new QualityModel(Quality.CBZ) };
            var remoteBook = Subject.Map(parsed);

            remoteBook.Author.Should().BeNull();
            parsed.ChapterRange.Should().BeNull();
        }
    }
}
