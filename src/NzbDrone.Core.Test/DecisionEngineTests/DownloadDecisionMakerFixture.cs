using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class DownloadDecisionMakerFixture : CoreTest<DownloadDecisionMaker>
    {
        private List<ReleaseInfo> _reports;
        private RemoteBook _remoteBook;

        private Mock<IDecisionEngineSpecification> _pass1;
        private Mock<IDecisionEngineSpecification> _pass2;
        private Mock<IDecisionEngineSpecification> _pass3;

        private Mock<IDecisionEngineSpecification> _fail1;
        private Mock<IDecisionEngineSpecification> _fail2;
        private Mock<IDecisionEngineSpecification> _fail3;

        private Mock<IDecisionEngineSpecification> _failDelayed1;

        [SetUp]
        public void Setup()
        {
            _pass1 = new Mock<IDecisionEngineSpecification>();
            _pass2 = new Mock<IDecisionEngineSpecification>();
            _pass3 = new Mock<IDecisionEngineSpecification>();

            _fail1 = new Mock<IDecisionEngineSpecification>();
            _fail2 = new Mock<IDecisionEngineSpecification>();
            _fail3 = new Mock<IDecisionEngineSpecification>();

            _failDelayed1 = new Mock<IDecisionEngineSpecification>();

            _pass1.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Accept);
            _pass2.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Accept);
            _pass3.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Accept);

            _fail1.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Reject("fail1"));
            _fail2.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Reject("fail2"));
            _fail3.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Reject("fail3"));

            _failDelayed1.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null)).Returns(Decision.Reject("failDelayed1"));
            _failDelayed1.SetupGet(c => c.Priority).Returns(SpecificationPriority.Disk);

            _reports = new List<ReleaseInfo> { new ReleaseInfo { Title = "Coldplay-A Head Full Of Dreams-CD-FLAC-2015-PERFECT" } };
            _remoteBook = new RemoteBook
            {
                Author = new Author(),
                Books = new List<Book> { new Book() },
                ParsedBookInfo = Builder<ParsedBookInfo>.CreateNew().With(x => x.Quality = new QualityModel(Quality.FLAC)).Build()
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(_remoteBook);
        }

        private void GivenSpecifications(params Mock<IDecisionEngineSpecification>[] mocks)
        {
            Mocker.SetConstant<IEnumerable<IDecisionEngineSpecification>>(mocks.Select(c => c.Object));
        }

        [Test]
        public void should_call_all_specifications()
        {
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            Subject.GetRssDecision(_reports).ToList();

            _fail1.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
            _fail2.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
            _fail3.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
            _pass1.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
            _pass2.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
            _pass3.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
        }

        [Test]
        public void should_call_delayed_specifications_if_non_delayed_passed()
        {
            GivenSpecifications(_pass1, _failDelayed1);

            Subject.GetRssDecision(_reports).ToList();
            _failDelayed1.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Once());
        }

        [Test]
        public void should_not_call_delayed_specifications_if_non_delayed_failed()
        {
            GivenSpecifications(_fail1, _failDelayed1);

            Subject.GetRssDecision(_reports).ToList();

            _failDelayed1.Verify(c => c.IsSatisfiedBy(_remoteBook, null), Times.Never());
        }

        // Light novels (2026-09): the decision maker re-grades a light-novel author's defaulted
        // CBZ (EPUB, or Unknown Audio on an audio category) and re-stamps the media type before
        // any specification runs; the same title for a manga author is untouched.
        [TestCase("local-overlord~ln", null, "EPUB", MediaType.Ebook)]
        [TestCase("local-overlord~ln", 3030, "Unknown Audio", MediaType.Audio)]
        [TestCase("local-overlord", null, "CBZ", MediaType.Archive)]
        [TestCase("local-overlord", 3030, "CBZ", MediaType.Archive)]
        public void should_regrade_a_light_novel_authors_defaulted_cbz_before_the_specs_run(string foreignAuthorId, int? category, string expectedQuality, MediaType expectedMediaType)
        {
            GivenSpecifications(_pass1);
            _pass1.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), It.IsAny<SearchCriteriaBase>())).Returns(Decision.Accept);

            var author = new Author { Id = 1, Name = "Overlord", CleanName = "overlord", Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = foreignAuthorId } };
            var report = new ReleaseInfo { Title = "Overlord Vol. 5 [Yen Press]", Categories = category.HasValue ? new List<int> { category.Value } : new List<int>() };

            _remoteBook.Author = author;
            _remoteBook.ParsedBookInfo.Quality = QualityParser.ParseQuality(report.Title);
            _remoteBook.ParsedBookInfo.Quality.Quality.Should().Be(Quality.CBZ);

            var decision = Subject.GetSearchDecision(new List<ReleaseInfo> { report }, new AuthorSearchCriteria { Author = author, Books = new List<Book>() }).Single();

            decision.RemoteBook.ParsedBookInfo.Quality.Quality.Name.Should().Be(expectedQuality);
            decision.RemoteBook.MediaType.Should().Be(expectedMediaType);
        }

        // Light-novel audio (2026-09-18, D4): the criteria parse gets the SEARCHED
        // media type, not null or Ebook -- the audiobook title bridge inside it only runs for Audio.
        // The parser is static, so the pin sits at the seam the fixture sees: Map receives the
        // bridge's ParsedBookInfo (ReleaseTitle set, the volume number) for an Audio search and never
        // for an Ebook search of the same title.
        // T2 (2026-09-20): an EPUB search now has a bridge of its own, so the title has to be one
        // only the AUDIO bridge can name -- "<Series> - <Audible title>", which is an audiobook key
        // (the Audible product beside its series) and no ebook key (those all carry the volume
        // number, or are the product name alone).
        [TestCase(MediaType.Audio, true)]
        [TestCase(MediaType.Ebook, false)]
        public void should_pass_the_criteria_media_type_to_the_criteria_parse(MediaType mediaType, bool bridged)
        {
            GivenSpecifications(_pass1);
            _pass1.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), It.IsAny<SearchCriteriaBase>())).Returns(Decision.Accept);

            var author = new Author { Id = 1, Name = "Sword Art Online", CleanName = "swordartonline~ln", Metadata = new AuthorMetadata { Name = "Sword Art Online", ForeignAuthorId = "local-sword-art-online~ln", Aliases = new List<string>() } };
            var vol21 = new Book { Id = 21, Title = "Sword Art Online Vol. 21", VolumeNumber = 21, Subtitle = "Unital Ring I", AuthorMetadataId = 1 };
            vol21.Editions = new List<Edition>
            {
                new Edition { BookId = 21, MediaType = MediaType.Ebook, Title = vol21.Title, Monitored = true },
                new Edition { BookId = 21, MediaType = MediaType.Audio, Title = vol21.Title, AudiobookTitle = "Unital Ring I", Monitored = true }
            };

            var title = "Sword Art Online - Unital Ring I [M4B]";
            var criteria = new BookSearchCriteria { Author = author, Books = new List<Book> { vol21 }, MediaType = mediaType };

            _remoteBook.Author = author;
            _remoteBook.Books = new List<Book> { vol21 };

            // Whatever the plain parse makes of the title maps to no book, so the decision maker
            // re-parses with the criteria either way; only the bridge's result maps to the volume.
            Mocker.GetMock<IParsingService>()
                  .Setup(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns<ParsedBookInfo, SearchCriteriaBase>((parsed, c) => parsed.VolumeNumber == 21
                      ? _remoteBook
                      : new RemoteBook { Author = author, Books = new List<Book>(), ParsedBookInfo = new ParsedBookInfo { AuthorName = author.Name, Quality = new QualityModel(Quality.M4B) } });

            Subject.GetSearchDecision(new List<ReleaseInfo> { new ReleaseInfo { Title = title } }, criteria).ToList();

            Mocker.GetMock<IParsingService>()
                  .Verify(c => c.Map(It.Is<ParsedBookInfo>(p => p.ReleaseTitle == title && p.VolumeNumber == 21 && p.AuthorName == "Sword Art Online"), criteria), bridged ? Times.Once() : Times.Never());
        }

        [Test]
        public void should_return_rejected_if_single_specs_fail()
        {
            GivenSpecifications(_fail1);

            var result = Subject.GetRssDecision(_reports);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_one_of_specs_fail()
        {
            GivenSpecifications(_pass1, _fail1, _pass2, _pass3);

            var result = Subject.GetRssDecision(_reports);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_pass_if_all_specs_pass()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            var result = Subject.GetRssDecision(_reports);

            result.Single().Approved.Should().BeTrue();
        }

        [Test]
        public void should_have_same_number_of_rejections_as_specs_that_failed()
        {
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            var result = Subject.GetRssDecision(_reports);
            result.Single().Rejections.Should().HaveCount(3);
        }

        [Test]
        public void should_not_attempt_to_map_book_if_not_parsable()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            _reports[0].Title = "Not parsable";

            Subject.GetRssDecision(_reports).ToList();

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()), Times.Never());

            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
        }

        [Test]
        public void should_not_attempt_to_map_book_if_author_title_is_blank()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            _reports[0].Title = "2013 - Night Visions";

            var results = Subject.GetRssDecision(_reports).ToList();

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()), Times.Never());

            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());

            results.Should().BeEmpty();
        }

        [Test]
        public void should_return_rejected_result_for_unparsable_search()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            _reports[0].Title = "1937 - Snow White and the Seven Dwarves";

            var author = new Author { Name = "Some Author" };
            var books = new List<Book>
            {
                new Book
                {
                    Title = "Some Book",
                    Editions = new List<Edition>
                    {
                        new Edition { Title = "Some Edition Title" }
                    }
                }
            };

            Subject.GetSearchDecision(_reports, new BookSearchCriteria { Author = author, Books = books }).ToList();

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()), Times.Never());

            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
        }

        [Test]
        public void should_not_attempt_to_make_decision_if_author_is_unknown()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            _remoteBook.Author = null;

            Subject.GetRssDecision(_reports);

            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteBook>(), null), Times.Never());
        }

        [Test]
        public void broken_report_shouldnt_blowup_the_process()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IParsingService>().Setup(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                     .Throws<TestException>();

            _reports = new List<ReleaseInfo>
                {
                    new ReleaseInfo { Title = "Coldplay-A Head Full Of Dreams-CD-FLAC-2015-PERFECT" },
                    new ReleaseInfo { Title = "Coldplay-A Head Full Of Dreams-CD-FLAC-2015-PERFECT" },
                    new ReleaseInfo { Title = "Coldplay-A Head Full Of Dreams-CD-FLAC-2015-PERFECT" }
                };

            Subject.GetRssDecision(_reports);

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()), Times.Exactly(_reports.Count));

            ExceptionVerification.ExpectedErrors(3);
        }

        [Test]
        public void should_return_unknown_author_rejection_if_author_is_unknown()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            _remoteBook.Author = null;

            var result = Subject.GetRssDecision(_reports);

            result.Should().HaveCount(1);
        }

        [Test]
        public void should_only_include_reports_for_requested_books()
        {
            var author = Builder<Author>.CreateNew().Build();

            var books = Builder<Book>.CreateListOfSize(2)
                .All()
                .With(v => v.AuthorId, author.Id)
                .With(v => v.Author, new LazyLoaded<Author>(author))
                .BuildList();

            var criteria = new AuthorSearchCriteria { Books = books.Take(1).ToList() };

            var reports = books.Select(v =>
                new ReleaseInfo()
                {
                    Title = string.Format("{0}-{1}[FLAC][2017][DRONE]", author.Name, v.Title)
                }).ToList();

            Mocker.GetMock<IParsingService>()
                .Setup(v => v.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                .Returns<ParsedBookInfo, SearchCriteriaBase>((p, c) =>
                    new RemoteBook
                    {
                        DownloadAllowed = true,
                        ParsedBookInfo = p,
                        Author = author,
                        Books = books.Where(v => v.Title == p.BookTitle).ToList()
                    });

            Mocker.SetConstant<IEnumerable<IDecisionEngineSpecification>>(new List<IDecisionEngineSpecification>
            {
                Mocker.Resolve<NzbDrone.Core.DecisionEngine.Specifications.Search.BookRequestedSpecification>()
            });

            var decisions = Subject.GetSearchDecision(reports, criteria);

            var approvedDecisions = decisions.Where(v => v.Approved).ToList();

            approvedDecisions.Count.Should().Be(1);
        }

        [Test]
        public void should_not_allow_download_if_author_is_unknown()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            _remoteBook.Author = null;

            var result = Subject.GetRssDecision(_reports);

            result.Should().HaveCount(1);

            result.First().RemoteBook.DownloadAllowed.Should().BeFalse();
        }

        [Test]
        public void should_not_allow_download_if_no_books_found()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            _remoteBook.Books = new List<Book>();

            var result = Subject.GetRssDecision(_reports);

            result.Should().HaveCount(1);

            result.First().RemoteBook.DownloadAllowed.Should().BeFalse();
        }

        [Test]
        public void should_return_a_decision_when_exception_is_caught()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IParsingService>().Setup(c => c.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                     .Throws<TestException>();

            _reports = new List<ReleaseInfo>
                {
                    new ReleaseInfo { Title = "Alien Ant Farm - TruAnt (FLAC) DRONE" },
                };

            Subject.GetRssDecision(_reports).Should().HaveCount(1);

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
