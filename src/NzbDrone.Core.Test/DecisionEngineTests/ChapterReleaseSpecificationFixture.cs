using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.History;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    // Volumes built from chapter packs (2026-10-21): a chapter release is grabbed only for a manga
    // series with the feature on, official (Digital, no scan marker), and completing a volume.
    [TestFixture]
    public class ChapterReleaseSpecificationFixture : CoreTest<ChapterReleaseSpecification>
    {
        private const string Title = "Marriagetoxin c001-073 (Digital) (empyrealarrow)";

        private RemoteBook _remoteBook;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.BuildVolumesFromChapters).Returns(true);
            GivenHistory();

            _remoteBook = new RemoteBook
            {
                Release = new ReleaseInfo { Title = Title },
                MediaType = MediaType.Archive,
                Author = new Author { Id = 3, Metadata = new AuthorMetadata { Name = "Marriagetoxin", ForeignAuthorId = "local-marriagetoxin" } },
                Books = new List<Book> { new Book { Id = 1, VolumeNumber = 1 } },
                ParsedBookInfo = new ParsedBookInfo
                {
                    ChapterRange = new ChapterRange { Start = 1, End = 73, IsDigital = true, IsScanlation = false }
                }
            };
        }

        private void ShouldReject(string reason)
        {
            var decision = Subject.IsSatisfiedBy(_remoteBook, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(reason);
        }

        [Test]
        public void non_chapter_release_is_accepted()
        {
            _remoteBook.ParsedBookInfo.ChapterRange = null;
            Mocker.GetMock<IConfigService>().SetupGet(c => c.BuildVolumesFromChapters).Returns(false);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void chapter_release_is_rejected_when_turned_off_globally()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.BuildVolumesFromChapters).Returns(false);

            ShouldReject("Building volumes from chapters is turned off");
        }

        [Test]
        public void chapter_release_is_rejected_when_turned_off_for_the_series()
        {
            _remoteBook.Author.BuildFromChapters = false;

            ShouldReject("Building volumes from chapters is turned off");
        }

        [Test]
        public void chapter_release_is_rejected_for_a_light_novel()
        {
            _remoteBook.Author.Metadata.Value.ForeignAuthorId = "local-marriagetoxin~ln";

            ShouldReject("Chapter releases build manga volumes only");
        }

        [Test]
        public void chapter_release_without_digital_is_rejected()
        {
            _remoteBook.ParsedBookInfo.ChapterRange.IsDigital = false;

            ShouldReject("Chapter release is not marked (Digital)");
        }

        [Test]
        public void scanlation_is_rejected_even_when_digital()
        {
            _remoteBook.ParsedBookInfo.ChapterRange.IsScanlation = true;

            ShouldReject("Chapter release looks like a scanlation");
        }

        [Test]
        public void chapter_release_completing_no_volume_is_rejected()
        {
            _remoteBook.Books = new List<Book>();

            ShouldReject("Chapter release completes no volume");
        }

        private void GivenHistory(params EntityHistory[] history)
        {
            Mocker.GetMock<IHistoryService>().Setup(h => h.GetByAuthor(3, null)).Returns(history.ToList());
        }

        private static EntityHistory History(EntityHistoryEventType type, string downloadId, string sourceTitle = null)
        {
            return new EntityHistory { AuthorId = 3, BookId = 1, EventType = type, DownloadId = downloadId, SourceTitle = sourceTitle };
        }

        // A pack that built some volumes and skipped others cannot complete the skipped ones on a second
        // grab (same files), so it is never fetched again for them; the seeding original is left alone.
        [Test]
        public void a_release_that_already_built_volumes_here_is_rejected()
        {
            GivenHistory(
                History(EntityHistoryEventType.Grabbed, "abc123", Title.ToUpperInvariant()),
                History(EntityHistoryEventType.BookFileImported, "abc123", "Marriagetoxin Vol. 08 (Built from chapters 62–71)"));

            ShouldReject("Chapter release already built the volumes it completes");
        }

        [Test]
        public void a_grabbed_release_that_never_imported_is_accepted_again()
        {
            GivenHistory(
                History(EntityHistoryEventType.Grabbed, "abc123", Title),
                History(EntityHistoryEventType.DownloadFailed, "abc123", Title));

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void another_release_that_built_volumes_does_not_reject_this_one()
        {
            GivenHistory(
                History(EntityHistoryEventType.Grabbed, "abc123", "Marriagetoxin c001-040 (Digital) (empyrealarrow)"),
                History(EntityHistoryEventType.BookFileImported, "abc123"));

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void official_chapter_release_completing_a_volume_is_accepted()
        {
            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }
    }
}
