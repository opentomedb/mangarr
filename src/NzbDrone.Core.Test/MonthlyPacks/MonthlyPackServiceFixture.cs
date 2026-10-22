using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.MonthlyPacks;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MonthlyPacks
{
    [TestFixture]
    public class MonthlyPackServiceFixture : CoreTest<MonthlyPackService>
    {
        private const string PackTitle = "Viz Media Volumes Update 2026-10 (Digital) (Rillant)";

        private int _nextId;
        private List<Edition> _editions;
        private List<BookFile> _files;
        private List<Queue.Queue> _queue;
        private List<EntityHistory> _history;
        private List<ReleaseInfo> _releases;
        private Dictionary<string, PackFileList> _cache;
        private Dictionary<string, byte[]> _torrents;
        private List<HttpRequest> _requests;
        private List<RemoteBook> _grabs;
        private Mock<IIndexer> _nyaa;
        private IndexerDefinition _nyaaDefinition;
        private IndexerDefinition _mamDefinition;
        private Mock<IIndexer> _mam;

        [SetUp]
        public void Setup()
        {
            _nextId = 0;
            _editions = new List<Edition>();
            _files = new List<BookFile>();
            _queue = new List<Queue.Queue>();
            _history = new List<EntityHistory>();
            _releases = new List<ReleaseInfo>();
            _cache = new Dictionary<string, PackFileList>();
            _torrents = new Dictionary<string, byte[]>();
            _requests = new List<HttpRequest>();
            _grabs = new List<RemoteBook>();

            Mocker.SetConstant<IMonthlyPackMatcher>(Mocker.Resolve<MonthlyPackMatcher>());

            Mocker.GetMock<IConfigService>().SetupGet(c => c.UseMonthlyVolumePacks).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.MaximumSize).Returns(0);

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionsByBook(It.IsAny<IEnumerable<int>>()))
                .Returns<IEnumerable<int>>(ids => _editions.Where(e => ids.Contains(e.BookId)).ToList());
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByBooks(It.IsAny<IEnumerable<int>>()))
                .Returns(() => _files);
            Mocker.GetMock<IQueueService>().Setup(s => s.GetQueue()).Returns(() => _queue);
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.GetByBook(It.IsAny<int>(), It.IsAny<EntityHistoryEventType?>()))
                .Returns<int, EntityHistoryEventType?>((id, type) => _history.Where(h => h.BookId == id && (type == null || h.EventType == type)).ToList());
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.Find(It.IsAny<string>(), It.IsAny<EntityHistoryEventType>()))
                .Returns<string, EntityHistoryEventType>((downloadId, type) => _history.Where(h => h.DownloadId == downloadId && h.EventType == type).ToList());

            var client = new Mock<IDownloadClient>();
            client.As<ISelectTorrentFiles>().SetupGet(c => c.CanSelectFiles).Returns(true);
            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(p => p.GetDownloadClient(DownloadProtocol.Torrent, It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                .Returns(client.Object);

            _nyaaDefinition = new IndexerDefinition { Id = 1, Name = "Nyaa.si (Prowlarr)", Settings = new TorznabSettings { BaseUrl = "http://prowlarr:9696/5/" } };
            _mamDefinition = new IndexerDefinition { Id = 2, Name = "MyAnonamouse (Prowlarr)", Settings = new TorznabSettings { BaseUrl = "http://prowlarr:9696/61/" } };
            _nyaa = Indexer(_nyaaDefinition);
            _mam = Indexer(_mamDefinition);
            _nyaa.Setup(i => i.FetchPacks(It.IsAny<PackSearchCriteria>())).Returns(() => Task.FromResult<IList<ReleaseInfo>>(_releases));

            Mocker.GetMock<IIndexerFactory>().Setup(f => f.AutomaticSearchEnabled(It.IsAny<bool>())).Returns(new List<IIndexer> { _nyaa.Object, _mam.Object });
            Mocker.GetMock<IIndexerFactory>().Setup(f => f.Find(1)).Returns(_nyaaDefinition);
            Mocker.GetMock<IIndexerFactory>().Setup(f => f.Find(2)).Returns(_mamDefinition);
            Mocker.GetMock<IIndexerFactory>().Setup(f => f.GetInstance(_nyaaDefinition)).Returns(_nyaa.Object);
            Mocker.GetMock<IIndexerFactory>().Setup(f => f.GetInstance(_mamDefinition)).Returns(_mam.Object);

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.GetAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r =>
                {
                    _requests.Add(r);
                    return Task.FromResult(new HttpResponse(r, new HttpHeader(), _torrents[r.Url.FullUri]));
                });

            Mocker.GetMock<IPackFileListRepository>()
                .Setup(r => r.FindByHash(It.IsAny<string>()))
                .Returns<string>(h => _cache.GetValueOrDefault(h));
            Mocker.GetMock<IPackFileListRepository>()
                .Setup(r => r.Insert(It.IsAny<PackFileList>()))
                .Returns<PackFileList>(p =>
                {
                    _cache[p.InfoHash] = p;
                    return p;
                });

            Mocker.GetMock<IDownloadService>()
                .Setup(s => s.DownloadReport(It.IsAny<RemoteBook>(), It.IsAny<int?>()))
                .Callback<RemoteBook, int?>((r, _) => _grabs.Add(r))
                .Returns(Task.CompletedTask);
        }

        private Mock<IIndexer> Indexer(IndexerDefinition definition)
        {
            var indexer = new Mock<IIndexer>();
            indexer.SetupGet(i => i.Definition).Returns(definition);
            indexer.SetupGet(i => i.Protocol).Returns(DownloadProtocol.Torrent);
            indexer.Setup(i => i.GetDownloadRequest(It.IsAny<string>())).Returns<string>(url => new HttpRequest(url));
            indexer.Setup(i => i.FetchPacks(It.IsAny<PackSearchCriteria>())).Returns(Task.FromResult<IList<ReleaseInfo>>(new List<ReleaseInfo>()));
            return indexer;
        }

        private Book Volume(string series, double volume, int releasedDaysAgo = 30, bool lightNovel = false)
        {
            var id = ++_nextId;
            var slug = "local-" + series.ToLowerInvariant().Replace(' ', '-').Replace(".", "").Replace(":", "");
            var author = new Author
            {
                Id = id,
                Metadata = new AuthorMetadata
                {
                    Name = series,
                    EditionLanguage = "en",
                    ForeignAuthorId = lightNovel ? slug + LibraryTypes.LightNovelSuffix : slug
                }
            };

            _editions.Add(new Edition { Id = 1000 + id, BookId = id, MediaType = MediaType.Archive, Monitored = true });

            return new Book
            {
                Id = id,
                AuthorId = id,
                Title = $"{series} Vol. {volume}",
                VolumeNumber = volume,
                Monitored = true,
                ReleaseDate = DateTime.UtcNow.AddDays(-releasedDaysAgo),
                Author = author
            };
        }

        private TorrentInfo Pack(string title, int seeders, int publishedDaysAgo, params string[] files)
        {
            var n = _torrents.Count + 1;
            var url = $"http://prowlarr:9696/5/download?id={n}";
            var bytes = Encoding.UTF8.GetBytes(url);
            var hash = $"ABCDEF{n:D4}";
            var entries = files.Select((f, i) => new TorrentFileEntry(i, f, 100 * 1024 * 1024)).ToList();

            _torrents[url] = bytes;
            Mocker.GetMock<ITorrentFileInfoReader>().Setup(r => r.GetHashFromTorrentFile(bytes)).Returns(hash);
            Mocker.GetMock<ITorrentFileInfoReader>().Setup(r => r.GetFilesFromTorrentFile(bytes)).Returns(("Pack " + n, entries));

            var release = new TorrentInfo
            {
                Title = title,
                Guid = "guid-" + n,
                DownloadUrl = url,
                DownloadProtocol = DownloadProtocol.Torrent,
                IndexerId = 1,
                Indexer = "Nyaa.si (Prowlarr)",
                PublishDate = DateTime.UtcNow.AddDays(-publishedDaysAgo),
                Seeders = seeders,
                InfoHash = hash.ToLowerInvariant(),
                Size = 6L * 1024 * 1024 * 1024
            };

            _releases.Add(release);
            return release;
        }

        private TorrentInfo Pack(params string[] files)
        {
            return Pack(PackTitle, 10, 5, files);
        }

        private static string File(Book book)
        {
            return $"{book.Author.Value.Metadata.Value.Name} v{book.VolumeNumber:00} (2026) (Digital) (Rillant).cbz";
        }

        private List<Book> Grabbed()
        {
            return _grabs.SelectMany(g => g.Books).ToList();
        }

        [Test]
        public void should_not_query_or_grab_when_switched_off()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.UseMonthlyVolumePacks).Returns(false);
            var book = Volume("Kaiju No. 8", 16);
            Pack(File(book));

            Subject.Search(new[] { book }).Should().BeEmpty();

            _nyaa.Verify(i => i.FetchPacks(It.IsAny<PackSearchCriteria>()), Times.Never());
            _grabs.Should().BeEmpty();
        }

        [Test]
        public void should_wait_three_days_after_release()
        {
            var twoDays = Volume("Kaiju No. 8", 16, releasedDaysAgo: 2);
            var threeDays = Volume("Akira Failing in Love", 4, releasedDaysAgo: 3);
            Pack(File(twoDays), File(threeDays));

            Subject.Search(new[] { twoDays, threeDays }).Should().BeEquivalentTo(new[] { threeDays });
        }

        [Test]
        public void should_skip_an_owned_volume_but_replace_a_chapter_built_one()
        {
            var owned = Volume("Kaiju No. 8", 16);
            var built = Volume("Akira Failing in Love", 4);
            _files.Add(new BookFile { EditionId = 1000 + owned.Id, Quality = new QualityModel(Quality.CBZ) });
            _files.Add(new BookFile { EditionId = 1000 + built.Id, Quality = new QualityModel(Quality.CbzChapters) });
            Pack(File(owned), File(built));

            Subject.Search(new[] { owned, built }).Should().BeEquivalentTo(new[] { built });
        }

        [Test]
        public void should_skip_a_queued_volume()
        {
            var queued = Volume("Kaiju No. 8", 16);
            var free = Volume("Akira Failing in Love", 4);
            _queue.Add(new Queue.Queue { Book = queued });
            Pack(File(queued), File(free));

            Subject.Search(new[] { queued, free }).Should().BeEquivalentTo(new[] { free });
        }

        [Test]
        public void should_skip_a_light_novel()
        {
            var novel = Volume("Kaiju No. 8", 16, lightNovel: true);
            Pack(File(novel));

            Subject.Search(new[] { novel }).Should().BeEmpty();
            _grabs.Should().BeEmpty();
        }

        [Test]
        public void should_skip_an_unmonitored_volume_or_archive_edition()
        {
            var unmonitored = Volume("Kaiju No. 8", 16);
            unmonitored.Monitored = false;
            var editionOff = Volume("Akira Failing in Love", 4);
            _editions.Single(e => e.BookId == editionOff.Id).Monitored = false;
            Pack(File(unmonitored), File(editionOff));

            Subject.Search(new[] { unmonitored, editionOff }).Should().BeEmpty();
        }

        [Test]
        public void should_skip_a_volume_already_grabbed_from_the_same_pack()
        {
            var again = Volume("Kaiju No. 8", 16);
            var fresh = Volume("Akira Failing in Love", 4);
            var pack = Pack(File(again), File(fresh));
            _history.Add(new EntityHistory { BookId = again.Id, EventType = EntityHistoryEventType.Grabbed, DownloadId = pack.InfoHash });

            Subject.Search(new[] { again, fresh }).Should().BeEquivalentTo(new[] { fresh });
        }

        [Test]
        public void should_mark_a_pack_mangarr_grabbed_before()
        {
            var again = Volume("Kaiju No. 8", 16);
            var fresh = Volume("Akira Failing in Love", 4);
            var pack = Pack(File(again), File(fresh));
            _history.Add(new EntityHistory { BookId = again.Id, EventType = EntityHistoryEventType.Grabbed, DownloadId = "ABCDEF0001" });

            Subject.Search(new[] { again, fresh });

            _grabs.Should().ContainSingle().Which.PackPreviouslyGrabbed.Should().BeTrue();
        }

        [Test]
        public void should_not_mark_a_pack_mangarr_never_grabbed()
        {
            var book = Volume("Kaiju No. 8", 16);
            Pack(File(book));
            _history.Add(new EntityHistory { BookId = 99, EventType = EntityHistoryEventType.Grabbed, DownloadId = "SOMEOTHERHASH" });

            Subject.Search(new[] { book });

            _grabs.Should().ContainSingle().Which.PackPreviouslyGrabbed.Should().BeFalse();
        }

        [Test]
        public void should_skip_a_pack_below_the_indexers_minimum_seeders()
        {
            ((TorznabSettings)_nyaaDefinition.Settings).MinimumSeeders = 5;
            var book = Volume("Kaiju No. 8", 16);
            Pack(PackTitle, 4, 5, File(book));

            Subject.Search(new[] { book }).Should().BeEmpty();
            _requests.Should().BeEmpty();
        }

        [Test]
        public void should_take_a_pack_at_the_indexers_minimum_seeders()
        {
            ((TorznabSettings)_nyaaDefinition.Settings).MinimumSeeders = 5;
            var book = Volume("Kaiju No. 8", 16);
            Pack(PackTitle, 5, 5, File(book));

            Subject.Search(new[] { book }).Should().BeEquivalentTo(new[] { book });
        }

        [Test]
        public void should_skip_a_blocklisted_pack()
        {
            var book = Volume("Kaiju No. 8", 16);
            var release = Pack(File(book));
            Mocker.GetMock<IBlocklistService>()
                .Setup(s => s.Blocklisted(book.AuthorId, release))
                .Returns(true);

            Subject.Search(new[] { book }).Should().BeEmpty();
            _requests.Should().BeEmpty();
            _grabs.Should().BeEmpty();
        }

        [Test]
        public void should_take_a_volume_grabbed_before_from_another_pack()
        {
            var book = Volume("Kaiju No. 8", 16);
            Pack(File(book));
            _history.Add(new EntityHistory { BookId = book.Id, EventType = EntityHistoryEventType.Grabbed, DownloadId = "SOMEOTHERHASH" });

            Subject.Search(new[] { book }).Should().BeEquivalentTo(new[] { book });
        }

        [TestCase("Viz Media Volumes Update 2026-10 (Digital) (Rillant)", true)]
        [TestCase("Square Enix volumes  update October 2026 (digital)", true)]
        [TestCase("Viz Media Volumes Update 2026-10 (Rillant)", false)]
        [TestCase("Viz Media Volume Updates 2026-10 (Digital)", false)]
        [TestCase("Viz Media Volumes Update 2026-10 (Digital) (Scans)", false)]
        [TestCase("Volumes Update 2026-10 (Digital) Scanlation", false)]
        [TestCase("Weekly Viz, ShonenJump and MangaPlus Chapter Updates (Digital)", false)]
        public void should_apply_the_pack_title_rule(string title, bool taken)
        {
            var book = Volume("Kaiju No. 8", 16);
            Pack(title, 10, 5, File(book));

            Subject.Search(new[] { book }).Should().HaveCount(taken ? 1 : 0);
            _requests.Should().HaveCount(taken ? 1 : 0);
        }

        [Test]
        public void should_skip_a_usenet_copy()
        {
            var book = Volume("Kaiju No. 8", 16);
            Pack(File(book)).DownloadProtocol = DownloadProtocol.Usenet;

            Subject.Search(new[] { book }).Should().BeEmpty();
            _requests.Should().BeEmpty();
        }

        [Test]
        public void should_never_query_or_fetch_from_myanonamouse()
        {
            var book = Volume("Kaiju No. 8", 16);
            var release = Pack(File(book));
            release.IndexerId = 2;
            release.Indexer = "MyAnonamouse (Prowlarr)";

            Subject.Search(new[] { book }).Should().BeEmpty();
            Subject.ProcessReleases(new[] { release }, new[] { book }).Should().BeEmpty();

            _mam.Verify(i => i.FetchPacks(It.IsAny<PackSearchCriteria>()), Times.Never());
            _mam.Verify(i => i.GetDownloadRequest(It.IsAny<string>()), Times.Never());
            _requests.Should().BeEmpty();
        }

        [Test]
        public void should_recognise_myanonamouse_by_base_url()
        {
            _nyaaDefinition.Name = "Books";
            ((TorznabSettings)_nyaaDefinition.Settings).BaseUrl = "https://www.MyAnonamouse.net";
            var book = Volume("Kaiju No. 8", 16);
            Pack(File(book));

            Subject.Search(new[] { book }).Should().BeEmpty();
            _nyaa.Verify(i => i.FetchPacks(It.IsAny<PackSearchCriteria>()), Times.Never());
        }

        [Test]
        public void should_skip_a_pack_published_over_a_year_ago()
        {
            var book = Volume("Kaiju No. 8", 16);
            Pack(PackTitle, 10, 400, File(book));

            Subject.Search(new[] { book }).Should().BeEmpty();
            _requests.Should().BeEmpty();
        }

        [Test]
        public void should_fetch_an_uncached_torrent_once_through_the_indexer_and_store_it()
        {
            var book = Volume("Kaiju No. 8", 16);
            var release = Pack(File(book));

            Subject.Search(new[] { book });

            _requests.Should().ContainSingle();
            _requests[0].Headers.Accept.Should().Be("application/x-bittorrent");
            _requests[0].AllowAutoRedirect.Should().BeFalse();
            _nyaa.Verify(i => i.GetDownloadRequest(release.DownloadUrl), Times.Once());

            var stored = _cache.Values.Should().ContainSingle().Subject;
            stored.InfoHash.Should().Be("ABCDEF0001");
            stored.Title.Should().Be(PackTitle);
            stored.Files.Should().ContainSingle();
            stored.Torrent.Should().Equal(_torrents[release.DownloadUrl]);
        }

        [Test]
        public void should_not_fetch_a_cached_pack()
        {
            var book = Volume("Kaiju No. 8", 16);
            var release = Pack(File(book));
            _cache["ABCDEF0001"] = new PackFileList
            {
                InfoHash = "ABCDEF0001",
                Title = PackTitle,
                Files = new List<TorrentFileEntry> { new TorrentFileEntry(0, File(book), 1000) },
                Torrent = new byte[] { 1, 2, 3 }
            };

            Subject.Search(new[] { book }).Should().BeEquivalentTo(new[] { book });

            _requests.Should().BeEmpty();
            _grabs.Single().PackTorrent.Should().Equal(1, 2, 3);
            _grabs.Single().Release.Should().BeSameAs(release);
        }

        [Test]
        public void should_fetch_a_release_without_a_hash_and_cache_it_by_the_torrent_hash()
        {
            var book = Volume("Kaiju No. 8", 16);
            Pack(File(book)).InfoHash = null;

            Subject.Search(new[] { book }).Should().BeEquivalentTo(new[] { book });

            _requests.Should().ContainSingle();
            _cache.Keys.Should().Equal("ABCDEF0001");
        }

        [Test]
        public void should_reuse_the_stored_list_when_the_fetched_torrent_is_already_cached()
        {
            var book = Volume("Kaiju No. 8", 16);
            Pack(File(book)).InfoHash = "1111";
            _cache["ABCDEF0001"] = new PackFileList
            {
                InfoHash = "ABCDEF0001",
                Title = PackTitle,
                Files = new List<TorrentFileEntry> { new TorrentFileEntry(0, File(book), 1000) },
                Torrent = new byte[] { 1, 2, 3 }
            };

            Subject.Search(new[] { book }).Should().BeEquivalentTo(new[] { book });

            Mocker.GetMock<IPackFileListRepository>().Verify(r => r.Insert(It.IsAny<PackFileList>()), Times.Never());
            _grabs.Single().PackTorrent.Should().Equal(1, 2, 3);
        }

        [Test]
        public void should_return_nothing_when_the_grab_is_refused()
        {
            Mocker.GetMock<IDownloadService>()
                .Setup(s => s.DownloadReport(It.IsAny<RemoteBook>(), It.IsAny<int?>()))
                .ThrowsAsync(new DownloadClientException("Transmission cannot download selected files of a torrent, refusing to add the whole pack"));
            var book = Volume("Kaiju No. 8", 16);
            Pack(File(book));

            Subject.Search(new[] { book }).Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_skip_a_magnet_only_pack()
        {
            var book = Volume("Kaiju No. 8", 16);
            Pack(File(book)).DownloadUrl = "magnet:?xt=urn:btih:abcdef0001";

            Subject.Search(new[] { book }).Should().BeEmpty();
            _requests.Should().BeEmpty();
        }

        [Test]
        public void should_fetch_at_most_ten_uncached_packs_per_run()
        {
            var book = Volume("Kaiju No. 8", 16);

            for (var i = 0; i < 11; i++)
            {
                Pack(PackTitle, 10, 20 - i, "Other Series v01 (2026) (Digital) (Rillant).cbz");
            }

            Subject.Search(new[] { book }).Should().BeEmpty();

            _requests.Should().HaveCount(10);

            // Newest first: the oldest one is the one left for the next run.
            _requests.Select(r => r.Url.FullUri).Should().NotContain(_releases[0].DownloadUrl);
        }

        [Test]
        public void should_grab_a_volume_once_from_the_pack_with_more_seeders()
        {
            var book = Volume("Kaiju No. 8", 16);
            Pack(PackTitle, 5, 2, File(book));
            var best = Pack(PackTitle, 20, 9, File(book));

            Subject.Search(new[] { book });

            _grabs.Should().ContainSingle().Which.Release.Should().BeSameAs(best);
        }

        [Test]
        public void should_prefer_the_newer_pack_on_equal_seeders()
        {
            var book = Volume("Kaiju No. 8", 16);
            Pack(PackTitle, 10, 9, File(book));
            var newer = Pack(PackTitle, 10, 2, File(book));

            Subject.Search(new[] { book });

            _grabs.Should().ContainSingle().Which.Release.Should().BeSameAs(newer);
        }

        [Test]
        public void should_grab_one_pack_once_for_all_its_matched_volumes()
        {
            var kaiju = Volume("Kaiju No. 8", 16);
            var akira = Volume("Akira Failing in Love", 4);
            var release = Pack("Unrelated v01 (2026) (Digital) (Rillant).cbz", File(kaiju), "readme.txt", File(akira));

            Subject.Search(new[] { kaiju, akira }).Should().BeEquivalentTo(new[] { kaiju, akira });

            var grab = _grabs.Should().ContainSingle().Subject;
            grab.Release.Should().BeSameAs(release);
            grab.Books.Should().BeEquivalentTo(new[] { kaiju, akira });
            grab.Author.Should().BeSameAs(kaiju.Author.Value);
            grab.MediaType.Should().Be(MediaType.Archive);
            grab.PackTorrent.Should().Equal(_torrents[release.DownloadUrl]);
            grab.PackFiles.Should().HaveCount(2);
            grab.PackFiles[kaiju.Id].Should().Be(new TorrentFileEntry(1, File(kaiju), 100 * 1024 * 1024));
            grab.PackFiles[akira.Id].Index.Should().Be(3);
            grab.ParsedBookInfo.Should().NotBeNull();

            Mocker.GetMock<IDownloadService>().Verify(s => s.DownloadReport(It.IsAny<RemoteBook>(), null), Times.Once());
        }

        [Test]
        public void should_drop_a_matched_file_over_the_maximum_size()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.MaximumSize).Returns(50);
            var book = Volume("Kaiju No. 8", 16);
            Pack(File(book));

            Subject.Search(new[] { book }).Should().BeEmpty();
            _grabs.Should().BeEmpty();
        }

        [Test]
        public void should_grab_nothing_without_a_client_that_can_select_files()
        {
            var client = new Mock<IDownloadClient>();
            client.As<ISelectTorrentFiles>().SetupGet(c => c.CanSelectFiles).Returns(false);
            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(p => p.GetDownloadClient(DownloadProtocol.Torrent, It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                .Returns(client.Object);
            var book = Volume("Kaiju No. 8", 16);
            Pack(File(book));

            Subject.Search(new[] { book }).Should().BeEmpty();

            _nyaa.Verify(i => i.FetchPacks(It.IsAny<PackSearchCriteria>()), Times.Never());
            _grabs.Should().BeEmpty();
        }

        [Test]
        public void should_grab_nothing_when_nothing_matches()
        {
            var book = Volume("Kaiju No. 8", 16);
            Pack("Other Series v01 (2026) (Digital) (Rillant).cbz");

            Subject.Search(new[] { book }).Should().BeEmpty();
            _grabs.Should().BeEmpty();
        }

        [Test]
        public void should_skip_a_volume_with_two_files_in_one_pack()
        {
            var book = Volume("Kaiju No. 8", 16);
            Pack(File(book), "Extra/" + File(book));

            Subject.Search(new[] { book }).Should().BeEmpty();
            _grabs.Should().BeEmpty();
        }

        [Test]
        public void should_process_feed_releases_without_a_query()
        {
            var book = Volume("Kaiju No. 8", 16);
            var release = Pack(File(book));
            var notAPack = new TorrentInfo { Title = "Kaiju No. 8 v16 (2026) (Digital) (Rillant)", DownloadProtocol = DownloadProtocol.Torrent, IndexerId = 1, PublishDate = DateTime.UtcNow };

            Subject.ProcessReleases(new ReleaseInfo[] { notAPack, release }, new[] { book }).Should().BeEquivalentTo(new[] { book });

            _nyaa.Verify(i => i.FetchPacks(It.IsAny<PackSearchCriteria>()), Times.Never());
            _grabs.Single().Release.Should().BeSameAs(release);
            _grabs.Single().ReleaseSource.Should().Be(ReleaseSourceType.Rss);
        }
    }
}
