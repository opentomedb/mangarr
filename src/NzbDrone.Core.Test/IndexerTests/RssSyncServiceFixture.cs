using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MonthlyPacks;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests
{
    [TestFixture]
    public class RssSyncServiceFixture : CoreTest<RssSyncService>
    {
        private List<ReleaseInfo> _releases;
        private List<Book> _missing;

        [SetUp]
        public void Setup()
        {
            _releases = new List<ReleaseInfo> { new ReleaseInfo { Guid = "pack", Title = "Weekly Shonen Volumes Update 2026-10 (Digital)" } };
            _missing = new List<Book>
            {
                new Book { Id = 1, AuthorId = 1, Monitored = true },
                new Book { Id = 2, AuthorId = 1, Monitored = true }
            };

            Mocker.GetMock<IFetchAndParseRss>()
                .Setup(s => s.Fetch())
                .Returns(Task.FromResult(_releases));

            Mocker.GetMock<IPendingReleaseService>()
                .Setup(s => s.GetPending())
                .Returns(new List<ReleaseInfo>());

            Mocker.GetMock<IMakeDownloadDecision>()
                .Setup(s => s.GetRssDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<bool>()))
                .Returns(new List<DownloadDecision>());

            Mocker.GetMock<IProcessDownloadDecisions>()
                .Setup(s => s.ProcessDecisions(It.IsAny<List<DownloadDecision>>()))
                .Returns(Task.FromResult(new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision>(), new List<DownloadDecision>())));

            Mocker.GetMock<IConfigService>()
                .SetupGet(s => s.UseMonthlyVolumePacks)
                .Returns(true);

            Mocker.GetMock<IBookService>()
                .Setup(s => s.BooksWithoutFiles(It.IsAny<PagingSpec<Book>>(), It.IsAny<LibraryType?>(), It.IsAny<WantedMediaTypeFilter?>()))
                .Returns((PagingSpec<Book> spec, LibraryType? library, WantedMediaTypeFilter? mediaType) =>
                {
                    spec.Records = _missing;
                    spec.TotalRecords = _missing.Count;
                    return spec;
                });

            Mocker.GetMock<IMonthlyPackService>()
                .Setup(s => s.ProcessReleases(It.IsAny<IEnumerable<ReleaseInfo>>(), It.IsAny<IEnumerable<Book>>()))
                .Returns(new List<Book>());
        }

        [Test]
        public void the_fetched_releases_go_to_the_pack_service_once_with_the_missing_volumes()
        {
            Subject.Execute(new RssSyncCommand());

            Mocker.GetMock<IMonthlyPackService>()
                .Verify(
                    s => s.ProcessReleases(
                        It.Is<IEnumerable<ReleaseInfo>>(r => r.Single().Guid == "pack"),
                        It.Is<IEnumerable<Book>>(b => b.Select(x => x.Id).SequenceEqual(new[] { 1, 2 }))),
                    Times.Once());
        }

        [Test]
        public void a_volume_grabbed_as_a_single_release_is_not_offered_to_the_pack_service()
        {
            var grabbed = new DownloadDecision(new RemoteBook { Books = new List<Book> { _missing[0] }, Release = new ReleaseInfo { Guid = "single" } });
            Mocker.GetMock<IProcessDownloadDecisions>()
                .Setup(s => s.ProcessDecisions(It.IsAny<List<DownloadDecision>>()))
                .Returns(Task.FromResult(new ProcessedDecisions(new List<DownloadDecision> { grabbed }, new List<DownloadDecision>(), new List<DownloadDecision>())));

            Subject.Execute(new RssSyncCommand());

            Mocker.GetMock<IMonthlyPackService>()
                .Verify(s => s.ProcessReleases(It.IsAny<IEnumerable<ReleaseInfo>>(), It.Is<IEnumerable<Book>>(b => b.Select(x => x.Id).SequenceEqual(new[] { 2 }))), Times.Once());
        }

        [Test]
        public void a_volume_with_a_held_single_release_is_not_offered_to_the_pack_service()
        {
            var pending = new DownloadDecision(new RemoteBook { Books = new List<Book> { _missing[0] }, Release = new ReleaseInfo { Guid = "held" } });
            Mocker.GetMock<IProcessDownloadDecisions>()
                .Setup(s => s.ProcessDecisions(It.IsAny<List<DownloadDecision>>()))
                .Returns(Task.FromResult(new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision> { pending }, new List<DownloadDecision>())));

            Subject.Execute(new RssSyncCommand());

            Mocker.GetMock<IMonthlyPackService>()
                .Verify(s => s.ProcessReleases(It.IsAny<IEnumerable<ReleaseInfo>>(), It.Is<IEnumerable<Book>>(b => b.Select(x => x.Id).SequenceEqual(new[] { 2 }))), Times.Once());
        }

        [Test]
        public void no_missing_volumes_are_loaded_when_no_release_is_a_pack()
        {
            _releases[0].Title = "Some Series v01 (2026) (Digital) (Group)";

            Subject.Execute(new RssSyncCommand());

            Mocker.GetMock<IBookService>()
                .Verify(s => s.BooksWithoutFiles(It.IsAny<PagingSpec<Book>>(), It.IsAny<LibraryType?>(), It.IsAny<WantedMediaTypeFilter?>()), Times.Never());
            Mocker.GetMock<IMonthlyPackService>()
                .Verify(s => s.ProcessReleases(It.IsAny<IEnumerable<ReleaseInfo>>(), It.IsAny<IEnumerable<Book>>()), Times.Never());
        }

        [Test]
        public void only_manga_volumes_are_loaded()
        {
            Subject.Execute(new RssSyncCommand());

            Mocker.GetMock<IBookService>()
                .Verify(s => s.BooksWithoutFiles(It.IsAny<PagingSpec<Book>>(), LibraryType.Manga, It.IsAny<WantedMediaTypeFilter?>()), Times.Once());
        }

        [Test]
        public void nothing_is_queried_when_the_switch_is_off()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(s => s.UseMonthlyVolumePacks)
                .Returns(false);

            Subject.Execute(new RssSyncCommand());

            Mocker.GetMock<IBookService>()
                .Verify(s => s.BooksWithoutFiles(It.IsAny<PagingSpec<Book>>(), It.IsAny<LibraryType?>(), It.IsAny<WantedMediaTypeFilter?>()), Times.Never());
            Mocker.GetMock<IMonthlyPackService>()
                .Verify(s => s.ProcessReleases(It.IsAny<IEnumerable<ReleaseInfo>>(), It.IsAny<IEnumerable<Book>>()), Times.Never());
        }
    }
}
