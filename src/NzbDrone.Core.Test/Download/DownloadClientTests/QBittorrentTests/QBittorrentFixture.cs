using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.QBittorrentTests
{
    [TestFixture]
    public class QBittorrentFixture : DownloadClientFixtureBase<QBittorrent>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new DownloadClientDefinition();
            Subject.Definition.Settings = new QBittorrentSettings
            {
                Host = "127.0.0.1",
                Port = 2222,
                Username = "admin",
                Password = "pass",
                MusicCategory = "music"
            };

            Mocker.GetMock<ITorrentFileInfoReader>()
                  .Setup(s => s.GetHashFromTorrentFile(It.IsAny<byte[]>()))
                  .Returns("CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951");

            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), new byte[0]));

            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.GetConfig(It.IsAny<QBittorrentSettings>()))
                  .Returns(new QBittorrentPreferences { DhtEnabled = true });

            Mocker.GetMock<IQBittorrentProxySelector>()
                  .Setup(s => s.GetProxy(It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()))
                  .Returns(Mocker.GetMock<IQBittorrentProxy>().Object);
        }

        protected void GivenRedirectToMagnet()
        {
            var httpHeader = new HttpHeader();
            httpHeader["Location"] = "magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp";

            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Get(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, httpHeader, new byte[0], System.Net.HttpStatusCode.SeeOther));
        }

        protected void GivenRedirectToTorrent()
        {
            var httpHeader = new HttpHeader();
            httpHeader["Location"] = "http://test.readarr.com/not-a-real-torrent.torrent";

            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Get(It.Is<HttpRequest>(h => h.Url.FullUri == _downloadUrl)))
                  .Returns<HttpRequest>(r => new HttpResponse(r, httpHeader, new byte[0], System.Net.HttpStatusCode.Found));
        }

        protected void GivenFailedDownload()
        {
            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(s => s.AddTorrentFromUrl(It.IsAny<string>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()))
                .Throws<InvalidOperationException>();

            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(s => s.AddTorrentFromFile(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()))
                .Throws<InvalidOperationException>();
        }

        protected void GivenSuccessfulDownload()
        {
            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(s => s.AddTorrentFromFile(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()))
                .Callback(() =>
                {
                    var torrent = new QBittorrentTorrent
                    {
                        Hash = "CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951",
                        Name = _title,
                        Size = 1000,
                        Progress = 1.0,
                        Eta = 8640000,
                        State = "queuedUP",
                        Label = "",
                        SavePath = ""
                    };
                    GivenTorrents(new List<QBittorrentTorrent> { torrent });
                });
        }

        protected void GivenHighPriority()
        {
            Subject.Definition.Settings.As<QBittorrentSettings>().OlderTvPriority = (int)QBittorrentPriority.First;
            Subject.Definition.Settings.As<QBittorrentSettings>().RecentTvPriority = (int)QBittorrentPriority.First;
        }

        protected void GivenGlobalSeedLimits(float maxRatio, int maxSeedingTime = -1, int maxInactiveSeedingTime = -1, QBittorrentMaxRatioAction maxRatioAction = QBittorrentMaxRatioAction.Pause)
        {
            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.GetConfig(It.IsAny<QBittorrentSettings>()))
                  .Returns(new QBittorrentPreferences
                  {
                      MaxRatioAction = maxRatioAction,
                      MaxRatio = maxRatio,
                      MaxRatioEnabled = maxRatio >= 0,
                      MaxSeedingTime = maxSeedingTime,
                      MaxSeedingTimeEnabled = maxSeedingTime >= 0,
                      MaxInactiveSeedingTime = maxInactiveSeedingTime,
                      MaxInactiveSeedingTimeEnabled = maxInactiveSeedingTime >= 0
                  });
        }

        protected virtual void GivenTorrents(List<QBittorrentTorrent> torrents)
        {
            if (torrents == null)
            {
                torrents = new List<QBittorrentTorrent>();
            }

            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(s => s.GetTorrents(It.IsAny<QBittorrentSettings>()))
                .Returns(torrents);

            foreach (var torrent in torrents)
            {
                Mocker.GetMock<IQBittorrentProxy>()
                    .Setup(s => s.GetTorrentProperties(torrent.Hash.ToLower(), It.IsAny<QBittorrentSettings>()))
                    .Returns(new QBittorrentTorrentProperties { SavePath = torrent.SavePath });

                Mocker.GetMock<IQBittorrentProxy>()
                    .Setup(s => s.GetTorrentFiles(torrent.Hash.ToLower(), It.IsAny<QBittorrentSettings>()))
                    .Returns(new List<QBittorrentTorrentFile> { new QBittorrentTorrentFile { Name = torrent.Name } });
            }

            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(s => s.IsTorrentLoaded(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()))
                .Returns<string, QBittorrentSettings>((hash, s) => torrents.Any(v => v.Hash.ToLower() == hash));
        }

        private void GivenTorrentFiles(string hash, List<QBittorrentTorrentFile> files)
        {
            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(s => s.GetTorrentFiles(hash.ToLower(), It.IsAny<QBittorrentSettings>()))
                .Returns(files);
        }

        [Test]
        public void error_item_should_have_required_properties()
        {
            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = _title,
                Size = 1000,
                Progress = 0.7,
                Eta = 8640000,
                State = "error",
                Label = "",
                SavePath = ""
            };
            GivenTorrents(new List<QBittorrentTorrent> { torrent });

            var item = Subject.GetItems().Single();
            VerifyWarning(item);
        }

        [TestCase("pausedDL")]
        [TestCase("stoppedDL")]
        public void paused_item_should_have_required_properties(string state)
        {
            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = _title,
                Size = 1000,
                Progress = 0.7,
                Eta = 8640000,
                State = state,
                Label = "",
                SavePath = ""
            };
            GivenTorrents(new List<QBittorrentTorrent> { torrent });

            var item = Subject.GetItems().Single();
            VerifyPaused(item);
            item.RemainingTime.Should().NotHaveValue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        [TestCase("queuedUP")]
        [TestCase("uploading")]
        [TestCase("stalledUP")]
        [TestCase("forcedUP")]
        public void completed_item_should_have_required_properties(string state)
        {
            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = _title,
                Size = 1000,
                Progress = 1.0,
                Eta = 8640000,
                State = state,
                Label = "",
                SavePath = ""
            };
            GivenTorrents(new List<QBittorrentTorrent> { torrent });

            var item = Subject.GetItems().Single();
            VerifyCompleted(item);
            item.RemainingTime.Should().Be(TimeSpan.Zero);
        }

        [TestCase("queuedDL")]
        [TestCase("checkingDL")]
        [TestCase("checkingUP")]
        [TestCase("metaDL")]
        [TestCase("checkingResumeData")]
        public void queued_item_should_have_required_properties(string state)
        {
            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = _title,
                Size = 1000,
                Progress = 0.7,
                Eta = 8640000,
                State = state,
                Label = "",
                SavePath = ""
            };
            GivenTorrents(new List<QBittorrentTorrent> { torrent });

            var item = Subject.GetItems().Single();
            VerifyQueued(item);
            item.RemainingTime.Should().NotHaveValue();
        }

        [Test]
        public void downloading_item_should_have_required_properties()
        {
            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = _title,
                Size = 1000,
                Progress = 0.7,
                Eta = 60,
                State = "downloading",
                Label = "",
                SavePath = ""
            };
            GivenTorrents(new List<QBittorrentTorrent> { torrent });

            var item = Subject.GetItems().Single();
            VerifyDownloading(item);
            item.RemainingTime.Should().NotBe(TimeSpan.Zero);
        }

        [Test]
        public void stalledDL_item_should_have_required_properties()
        {
            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = _title,
                Size = 1000,
                Progress = 0.7,
                Eta = 8640000,
                State = "stalledDL",
                Label = "",
                SavePath = ""
            };
            GivenTorrents(new List<QBittorrentTorrent> { torrent });

            var item = Subject.GetItems().Single();
            VerifyWarning(item);
            item.RemainingTime.Should().NotHaveValue();
        }

        [Test]
        public void single_file_torrent_outputpath_should_have_sanitised_name()
        {
            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = @"Droned.S01E01.Test\'s.1080p.WEB-DL-DRONE.mkv",
                Size = 1000,
                Progress = 0.7,
                Eta = 8640000,
                State = "stalledDL",
                Label = "",
                SavePath = @"C:\Torrents".AsOsAgnostic()
            };

            var file = new QBittorrentTorrentFile
            {
                Name = "Droned.S01E01.Tests.1080p.WEB-DL-DRONE.mkv"
            };

            GivenTorrents(new List<QBittorrentTorrent> { torrent });
            GivenTorrentFiles(torrent.Hash, new List<QBittorrentTorrentFile> { file });

            var item = new DownloadClientItem
            {
                DownloadId = torrent.Hash
            };

            var result = Subject.GetImportItem(item, null);

            result.OutputPath.FullPath.Should().Be(Path.Combine(torrent.SavePath, file.Name));
        }

        [Test]
        public void single_file_torrent_with_folder_should_only_have_first_subfolder()
        {
            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = @"Droned.S01E01.Test\'s.1080p.WEB-DL-DRONE",
                Size = 1000,
                Progress = 0.7,
                Eta = 8640000,
                State = "stalledDL",
                Label = "",
                SavePath = @"C:\Torrents".AsOsAgnostic()
            };

            var file = new QBittorrentTorrentFile
            {
                Name = "Folder/Droned.S01E01.Tests.1080p.WEB-DL-DRONE.mkv"
            };

            GivenTorrents(new List<QBittorrentTorrent> { torrent });
            GivenTorrentFiles(torrent.Hash, new List<QBittorrentTorrentFile> { file });

            var item = new DownloadClientItem
            {
                DownloadId = torrent.Hash
            };

            var result = Subject.GetImportItem(item, null);

            result.OutputPath.FullPath.Should().Be(Path.Combine(torrent.SavePath, "Folder"));
        }

        [Test]
        public void multi_file_torrent_outputpath_should_have_sanitised_name()
        {
            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = @"Droned.S01.\1/2",
                Size = 1000,
                Progress = 0.7,
                Eta = 8640000,
                State = "stalledDL",
                Label = "",
                SavePath = @"C:\Torrents".AsOsAgnostic()
            };

            var files = new List<QBittorrentTorrentFile>
            {
                new QBittorrentTorrentFile
                {
                    Name = @"Droned.S01.12\E01.mkv".AsOsAgnostic()
                },
                new QBittorrentTorrentFile
                {
                    Name = @"Droned.S01.12\E02.mkv".AsOsAgnostic()
                }
            };

            GivenTorrents(new List<QBittorrentTorrent> { torrent });
            GivenTorrentFiles(torrent.Hash, files);

            var item = new DownloadClientItem
            {
                DownloadId = torrent.Hash
            };

            var result = Subject.GetImportItem(item, null);

            result.OutputPath.FullPath.Should().Be(Path.Combine(torrent.SavePath, "Droned.S01.12"));
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void api_261_should_use_content_path(string state)
        {
            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = @"Droned.S01.\1/2",
                Size = 1000,
                Progress = 0.7,
                Eta = 8640000,
                State = state,
                Label = "",
                SavePath = @"C:\Torrents".AsOsAgnostic(),
                ContentPath = @"C:\Torrents\Droned.S01.12".AsOsAgnostic()
            };

            GivenTorrents(new List<QBittorrentTorrent> { torrent });

            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(v => v.GetApiVersion(It.IsAny<QBittorrentSettings>()))
                .Returns(new Version(2, 6, 1));

            var item = new DownloadClientItem
            {
                DownloadId = torrent.Hash,
                OutputPath = new OsPath(torrent.ContentPath)
            };

            var result = Subject.GetImportItem(item, null);

            result.OutputPath.FullPath.Should().Be(torrent.ContentPath);
        }

        [Test]
        public void missingFiles_item_should_have_required_properties()
        {
            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = _title,
                Size = 1000,
                Progress = 0.7,
                Eta = 8640000,
                State = "missingFiles",
                Label = "",
                SavePath = ""
            };
            GivenTorrents(new List<QBittorrentTorrent> { torrent });

            var item = Subject.GetItems().Single();
            VerifyWarning(item);
            item.RemainingTime.Should().NotHaveValue();
        }

        [Test]
        public async Task Download_should_return_unique_id()
        {
            GivenSuccessfulDownload();

            var remoteBook = CreateRemoteBook();

            var id = await Subject.Download(remoteBook, CreateIndexer());

            id.Should().NotBeNullOrEmpty();
        }

        [TestCase("magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp", "CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951")]
        public async Task Download_should_get_hash_from_magnet_url(string magnetUrl, string expectedHash)
        {
            GivenSuccessfulDownload();

            var remoteBook = CreateRemoteBook();
            remoteBook.Release.DownloadUrl = magnetUrl;

            var id = await Subject.Download(remoteBook, CreateIndexer());

            id.Should().Be(expectedHash);
        }

        [Test]
        public void Download_should_refuse_magnet_if_no_trackers_provided_and_dht_is_disabled()
        {
            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.GetConfig(It.IsAny<QBittorrentSettings>()))
                  .Returns(new QBittorrentPreferences() { DhtEnabled = false });

            var remoteBook = CreateRemoteBook();
            remoteBook.Release.DownloadUrl = "magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR";

            Assert.ThrowsAsync<ReleaseDownloadException>(async () => await Subject.Download(remoteBook, CreateIndexer()));
        }

        [Test]
        public void Download_should_accept_magnet_if_trackers_provided_and_dht_is_disabled()
        {
            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.GetConfig(It.IsAny<QBittorrentSettings>()))
                  .Returns(new QBittorrentPreferences { DhtEnabled = false });

            var remoteBook = CreateRemoteBook();
            remoteBook.Release.DownloadUrl = "magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp://abc";

            Assert.DoesNotThrowAsync(async () => await Subject.Download(remoteBook, CreateIndexer()));

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(s => s.AddTorrentFromUrl(It.IsAny<string>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()), Times.Once());
        }

        [Test]
        public async Task Download_should_set_top_priority()
        {
            GivenHighPriority();
            GivenSuccessfulDownload();

            var remoteBook = CreateRemoteBook();

            var id = await Subject.Download(remoteBook, CreateIndexer());

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.MoveTorrentToTopInQueue(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Once());
        }

        [Test]
        public async Task Download_should_not_fail_if_top_priority_not_available()
        {
            GivenHighPriority();
            GivenSuccessfulDownload();

            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(v => v.MoveTorrentToTopInQueue(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()))
                  .Throws(new HttpException(new HttpResponse(new HttpRequest("http://me.local/"), new HttpHeader(), new byte[0], System.Net.HttpStatusCode.Forbidden)));

            var remoteBook = CreateRemoteBook();

            var id = await Subject.Download(remoteBook, CreateIndexer());

            id.Should().NotBeNullOrEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_return_status_with_outputdirs()
        {
            var config = new QBittorrentPreferences
            {
                SavePath = @"C:\Downloads\Finished\QBittorrent".AsOsAgnostic()
            };

            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(v => v.GetConfig(It.IsAny<QBittorrentSettings>()))
                .Returns(config);

            var result = Subject.GetStatus();

            result.IsLocalhost.Should().BeTrue();
            result.OutputRootFolders.Should().NotBeNull();
            result.OutputRootFolders.First().Should().Be(@"C:\Downloads\Finished\QBittorrent".AsOsAgnostic());
        }

        [Test]
        public void should_correct_category_output_path()
        {
            var config = new QBittorrentPreferences
            {
                SavePath = @"C:\Downloads\Finished\QBittorrent".AsOsAgnostic()
            };

            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(v => v.GetConfig(It.IsAny<QBittorrentSettings>()))
                .Returns(config);

            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(v => v.GetApiVersion(It.IsAny<QBittorrentSettings>()))
                .Returns(new Version(2, 0));

            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(s => s.GetLabels(It.IsAny<QBittorrentSettings>()))
                .Returns(new Dictionary<string, QBittorrentLabel>
                    { { "music", new QBittorrentLabel { Name = "music", SavePath = "//server/store/downloads" } } });

            var result = Subject.GetStatus();

            result.IsLocalhost.Should().BeTrue();
            result.OutputRootFolders.Should().NotBeNull();
            result.OutputRootFolders.First().Should().Be(@"\\server\store\downloads");
        }

        [Test]
        public async Task Download_should_handle_http_redirect_to_magnet()
        {
            GivenRedirectToMagnet();
            GivenSuccessfulDownload();

            var remoteBook = CreateRemoteBook();

            var id = await Subject.Download(remoteBook, CreateIndexer());

            id.Should().NotBeNullOrEmpty();
        }

        [Test]
        public async Task Download_should_handle_http_redirect_to_torrent()
        {
            GivenRedirectToTorrent();
            GivenSuccessfulDownload();

            var remoteBook = CreateRemoteBook();

            var id = await Subject.Download(remoteBook, CreateIndexer());

            id.Should().NotBeNullOrEmpty();
        }

        [Test]
        public void should_not_be_removable_and_should_not_allow_move_files_if_max_ratio_not_reached()
        {
            GivenGlobalSeedLimits(1.0f);

            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = _title,
                Size = 1000,
                Progress = 1.0,
                Eta = 8640000,
                State = "uploading",
                Label = "",
                SavePath = "",
                Ratio = 0.5f
            };
            GivenTorrents(new List<QBittorrentTorrent> { torrent });

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeFalse();
            item.CanMoveFiles.Should().BeFalse();
        }

        protected virtual QBittorrentTorrent GivenCompletedTorrent(
            string state = "pausedUP",
            float ratio = 0.1f,
            float ratioLimit = -2,
            int seedingTime = 1,
            int seedingTimeLimit = -2,
            int inactiveSeedingTimeLimit = -2,
            long lastActivity = -1)
        {
            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = _title,
                Size = 1000,
                Progress = 1.0,
                Eta = 8640000,
                State = state,
                Label = "",
                SavePath = "",
                Ratio = ratio,
                RatioLimit = ratioLimit,
                SeedingTimeLimit = seedingTimeLimit,
                InactiveSeedingTimeLimit = inactiveSeedingTimeLimit,
                LastActivity = lastActivity == -1 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : lastActivity
            };

            GivenTorrents(new List<QBittorrentTorrent>() { torrent });

            Mocker.GetMock<IQBittorrentProxy>()
                .Setup(s => s.GetTorrentProperties("HASH", It.IsAny<QBittorrentSettings>()))
                .Returns(new QBittorrentTorrentProperties
                {
                    Hash = "HASH",
                    SeedingTime = seedingTime * 60
                });

            return torrent;
        }

        [Test]
        public void should_not_be_removable_and_should_not_allow_move_files_if_max_ratio_reached_and_not_paused()
        {
            GivenGlobalSeedLimits(1.0f);
            GivenCompletedTorrent("uploading", ratio: 1.0f);
            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeFalse();
            item.CanMoveFiles.Should().BeFalse();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_not_be_removable_and_should_not_allow_move_files_if_max_ratio_is_not_set(string state)
        {
            GivenGlobalSeedLimits(-1);
            GivenCompletedTorrent(state, ratio: 1.0f);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeFalse();
            item.CanMoveFiles.Should().BeFalse();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_be_removable_and_should_allow_move_files_if_max_ratio_reached_and_paused(string state)
        {
            GivenGlobalSeedLimits(1.0f);
            GivenCompletedTorrent(state, ratio: 1.0f);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_be_removable_and_should_allow_move_files_if_max_ratio_reached_after_rounding_and_paused(string state)
        {
            GivenGlobalSeedLimits(1.0f);
            GivenCompletedTorrent(state, ratio: 1.1006066990976857f);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_be_removable_and_should_allow_move_files_if_just_under_max_ratio_reached_after_rounding_and_paused(string state)
        {
            GivenGlobalSeedLimits(1.0f);
            GivenCompletedTorrent(state, ratio: 0.9999f);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_be_removable_and_should_allow_move_files_if_overridden_max_ratio_reached_and_paused(string state)
        {
            GivenGlobalSeedLimits(2.0f);
            GivenCompletedTorrent(state, ratio: 1.0f, ratioLimit: 0.8f);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_be_removable_and_should_allow_move_files_if_overridden_max_ratio_reached_after_rounding_and_paused(string state)
        {
            GivenGlobalSeedLimits(2.0f);
            GivenCompletedTorrent(state, ratio: 1.1006066990976857f, ratioLimit: 1.1f);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_be_removable_and_should_allow_move_files_if_just_under_overridden_max_ratio_reached_after_rounding_and_paused(string state)
        {
            GivenGlobalSeedLimits(2.0f);
            GivenCompletedTorrent(state, ratio: 0.9999f, ratioLimit: 1.0f);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_not_be_removable_if_overridden_max_ratio_not_reached_and_paused(string state)
        {
            GivenGlobalSeedLimits(0.2f);
            GivenCompletedTorrent(state, ratio: 0.5f, ratioLimit: 0.8f);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeFalse();
            item.CanMoveFiles.Should().BeFalse();
        }

        [Test]
        public void should_not_be_removable_and_should_not_allow_move_files_if_max_seedingtime_reached_and_not_paused()
        {
            GivenGlobalSeedLimits(-1, 20);
            GivenCompletedTorrent("uploading", ratio: 2.0f, seedingTime: 30);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeFalse();
            item.CanMoveFiles.Should().BeFalse();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_be_removable_and_should_allow_move_files_if_max_seedingtime_reached_and_paused(string state)
        {
            GivenGlobalSeedLimits(-1, 20);
            GivenCompletedTorrent(state, ratio: 2.0f, seedingTime: 20);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_be_removable_and_should_allow_move_files_if_overridden_max_seedingtime_reached_and_paused(string state)
        {
            GivenGlobalSeedLimits(-1, 40);
            GivenCompletedTorrent(state, ratio: 2.0f, seedingTime: 20, seedingTimeLimit: 10);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_not_be_removable_if_overridden_max_seedingtime_not_reached_and_paused(string state)
        {
            GivenGlobalSeedLimits(-1, 20);
            GivenCompletedTorrent(state, ratio: 2.0f, seedingTime: 30, seedingTimeLimit: 40);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeFalse();
            item.CanMoveFiles.Should().BeFalse();
        }

        [Test]
        public void should_not_be_removable_and_should_not_allow_move_files_if_max_inactive_seedingtime_reached_and_not_paused()
        {
            GivenGlobalSeedLimits(-1, maxInactiveSeedingTime: 20);
            GivenCompletedTorrent("uploading", ratio: 2.0f, lastActivity: DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(25)).ToUnixTimeSeconds());

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeFalse();
            item.CanMoveFiles.Should().BeFalse();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_be_removable_and_should_allow_move_files_if_max_inactive_seedingtime_reached_and_paused(string state)
        {
            GivenGlobalSeedLimits(-1, maxInactiveSeedingTime: 20);
            GivenCompletedTorrent(state, ratio: 2.0f, lastActivity: DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(25)).ToUnixTimeSeconds());

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_be_removable_and_should_allow_move_files_if_overridden_max_inactive_seedingtime_reached_and_paused(string state)
        {
            GivenGlobalSeedLimits(-1, maxInactiveSeedingTime: 40);
            GivenCompletedTorrent(state, ratio: 2.0f, seedingTime: 20, inactiveSeedingTimeLimit: 10, lastActivity: DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(15)).ToUnixTimeSeconds());

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_not_be_removable_if_overridden_max_inactive_seedingtime_not_reached_and_paused(string state)
        {
            GivenGlobalSeedLimits(-1, maxInactiveSeedingTime: 20);
            GivenCompletedTorrent(state, ratio: 2.0f, seedingTime: 30, inactiveSeedingTimeLimit: 40, lastActivity: DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(30)).ToUnixTimeSeconds());

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeFalse();
            item.CanMoveFiles.Should().BeFalse();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_be_removable_and_should_allow_move_files_if_max_seedingtime_reached_but_ratio_not_and_paused(string state)
        {
            GivenGlobalSeedLimits(2.0f, 20);
            GivenCompletedTorrent(state, ratio: 1.0f, seedingTime: 30);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_be_removable_and_should_allow_move_files_if_max_inactive_seedingtime_reached_but_ratio_not_and_paused(string state)
        {
            GivenGlobalSeedLimits(2.0f, maxInactiveSeedingTime: 20);
            GivenCompletedTorrent(state, ratio: 1.0f, lastActivity: DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(25)).ToUnixTimeSeconds());

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeTrue();
            item.CanMoveFiles.Should().BeTrue();
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_not_fetch_details_twice(string state)
        {
            GivenGlobalSeedLimits(-1, 30);
            GivenCompletedTorrent(state, ratio: 2.0f, seedingTime: 20);

            var item = Subject.GetItems().Single();
            item.CanBeRemoved.Should().BeFalse();
            item.CanMoveFiles.Should().BeFalse();

            var item2 = Subject.GetItems().Single();

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(p => p.GetTorrentProperties(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Once());
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_get_category_from_the_category_if_set(string state)
        {
            const string category = "music-readarr";
            GivenGlobalSeedLimits(1.0f);

            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = _title,
                Size = 1000,
                Progress = 1.0,
                Eta = 8640000,
                State = state,
                Category = category,
                SavePath = "",
                Ratio = 1.0f
            };

            GivenTorrents(new List<QBittorrentTorrent> { torrent });

            var item = Subject.GetItems().Single();
            item.Category.Should().Be(category);
        }

        [TestCase("pausedUP")]
        [TestCase("stoppedUP")]
        public void should_get_category_from_the_label_if_the_category_is_not_available(string state)
        {
            const string category = "music-readarr";
            GivenGlobalSeedLimits(1.0f);

            var torrent = new QBittorrentTorrent
            {
                Hash = "HASH",
                Name = _title,
                Size = 1000,
                Progress = 1.0,
                Eta = 8640000,
                State = state,
                Label = category,
                SavePath = "",
                Ratio = 1.0f
            };

            GivenTorrents(new List<QBittorrentTorrent> { torrent });

            var item = Subject.GetItems().Single();
            item.Category.Should().Be(category);
        }

        [Test]
        public void should_handle_eta_biginteger()
        {
            var json = "{ \"eta\": 18446744073709335000 }";
            var torrent = Newtonsoft.Json.JsonConvert.DeserializeObject<QBittorrentTorrent>(json);
            torrent.Eta.ToString().Should().Be("18446744073709335000");
        }

        private const string PackHash = "CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951";
        private static readonly byte[] PackTorrentBytes = { 1, 2, 3 };

        private List<string> _packCalls;
        private bool _packListed;
        private int _wantedClientIndex;

        // The torrent has 64 files (one a BEP47 pad file when padAt >= 0); qBittorrent lists them
        // without the pad file. The wanted volume is torrent file 24.
        private RemoteBook GivenPack(bool alreadyLoaded = false, int padAt = -1)
        {
            var files = Enumerable.Range(0, 64)
                                  .Select(i => new TorrentFileEntry(i, i == padAt ? ".pad/0" : $"Series {i}/Series {i} v01 (Digital).cbz", 1000))
                                  .ToList();
            var clientFiles = files.Where(f => f.Index != padAt)
                                   .Select(f => new QBittorrentTorrentFile { Name = "Monthly Pack/" + f.Path })
                                   .ToList();
            _wantedClientIndex = files.Where(f => f.Index != padAt).ToList().IndexOf(files[24]);

            Mocker.GetMock<ITorrentFileInfoReader>()
                  .Setup(s => s.GetFilesFromTorrentFile(It.IsAny<byte[]>()))
                  .Returns(("Monthly Pack", files));

            Mocker.GetMock<IQBittorrentProxySelector>()
                  .Setup(s => s.GetApiVersion(It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()))
                  .Returns(new Version(2, 9, 3));

            var loaded = alreadyLoaded;
            _packCalls = new List<string>();
            _packListed = true;

            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.IsTorrentLoaded(PackHash.ToLower(), It.IsAny<QBittorrentSettings>()))
                  .Returns(() => loaded);

            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.GetTorrentFiles(PackHash.ToLower(), It.IsAny<QBittorrentSettings>()))
                  .Returns(clientFiles);

            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.AddTorrentFromFile(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()))
                  .Callback(() => loaded = true);

            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.SetFilePriority(It.IsAny<string>(), It.IsAny<IEnumerable<int>>(), It.IsAny<int>(), It.IsAny<QBittorrentSettings>()))
                  .Callback<string, IEnumerable<int>, int, QBittorrentSettings>((h, i, p, st) => _packCalls.Add("prio" + p));
            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.StartTorrent(It.IsAny<string>(), It.IsAny<QBittorrentSettings>()))
                  .Callback(() => _packCalls.Add("start"));
            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.SetForceStart(It.IsAny<string>(), true, It.IsAny<QBittorrentSettings>()))
                  .Callback(() => _packCalls.Add("force"));
            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.SetTorrentLabel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()))
                  .Callback<string, string, QBittorrentSettings>((h, l, st) => _packCalls.Add("label:" + l));

            // GetItems lists only Mangarr's category; the pack is listed there unless a test moves it out.
            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.GetTorrents(It.IsAny<QBittorrentSettings>()))
                  .Returns(() => _packListed ? new List<QBittorrentTorrent> { new QBittorrentTorrent { Hash = PackHash.ToLower(), Category = "music" } } : new List<QBittorrentTorrent>());

            var remoteBook = CreateRemoteBook();
            remoteBook.PackTorrent = PackTorrentBytes;
            remoteBook.PackFiles = new Dictionary<int, TorrentFileEntry> { { 646, files[24] } };
            remoteBook.PackPreviouslyGrabbed = alreadyLoaded;

            return remoteBook;
        }

        private bool IsAllExceptWanted(IEnumerable<int> indexes, int total = 64)
        {
            return indexes.OrderBy(i => i).SequenceEqual(Enumerable.Range(0, total).Where(i => i != _wantedClientIndex));
        }

        [TestCase(QBittorrentState.Start)]
        [TestCase(QBittorrentState.Stop)]
        [TestCase(QBittorrentState.ForceStart)]
        public async Task pack_should_be_added_stopped_with_unmatched_files_skipped(QBittorrentState initialState)
        {
            Subject.Definition.Settings.As<QBittorrentSettings>().InitialState = (int)initialState;
            var remoteBook = GivenPack();

            var id = await Subject.Download(remoteBook, CreateIndexer());

            id.Should().Be(PackHash);

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.AddTorrentFromFile(It.IsAny<string>(), PackTorrentBytes, It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>(), true), Times.Once());
            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.AddTorrentFromFile(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.SetFilePriority(PackHash.ToLower(), It.Is<IEnumerable<int>>(i => IsAllExceptWanted(i, 64)), 0, It.IsAny<QBittorrentSettings>()), Times.Once());

            // the deselect always comes before anything starts the torrent
            _packCalls.Should().Equal(initialState switch
            {
                QBittorrentState.Start => new[] { "prio0", "start" },
                QBittorrentState.ForceStart => new[] { "prio0", "force" },
                _ => new[] { "prio0" }
            });
        }

        [Test]
        public async Task pack_should_map_files_by_qbittorrent_indexes_when_torrent_has_pad_files()
        {
            var remoteBook = GivenPack(padAt: 10);

            await Subject.Download(remoteBook, CreateIndexer());

            _wantedClientIndex.Should().Be(23);
            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.SetFilePriority(PackHash.ToLower(), It.Is<IEnumerable<int>>(i => IsAllExceptWanted(i, 63)), 0, It.IsAny<QBittorrentSettings>()), Times.Once());
        }

        [Test]
        public async Task pack_should_not_send_empty_priority_call_when_every_file_is_wanted()
        {
            var remoteBook = GivenPack();
            remoteBook.PackFiles = Enumerable.Range(0, 64).ToDictionary(i => i, i => new TorrentFileEntry(i, $"Series {i}/Series {i} v01 (Digital).cbz", 1000));

            await Subject.Download(remoteBook, CreateIndexer());

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.SetFilePriority(It.IsAny<string>(), It.IsAny<IEnumerable<int>>(), It.IsAny<int>(), It.IsAny<QBittorrentSettings>()), Times.Never());
            _packCalls.Should().Equal("start");
        }

        [Test]
        public void pack_that_never_appears_should_fail_without_a_delete()
        {
            var remoteBook = GivenPack();
            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.IsTorrentLoaded(PackHash.ToLower(), It.IsAny<QBittorrentSettings>()))
                  .Returns(false);

            Assert.ThrowsAsync<DownloadClientException>(async () => await Subject.Download(remoteBook, CreateIndexer()));

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.RemoveTorrent(PackHash.ToLower(), false, It.IsAny<QBittorrentSettings>()), Times.Never());
            _packCalls.Should().BeEmpty();
        }

        [Test]
        public void pack_listed_late_should_still_be_removed_after_the_wait_times_out()
        {
            var remoteBook = GivenPack();
            var calls = 0;

            // not listed for the first 5 s of polling, then it appears
            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.IsTorrentLoaded(PackHash.ToLower(), It.IsAny<QBittorrentSettings>()))
                  .Returns(() => ++calls > 52);

            Assert.ThrowsAsync<DownloadClientException>(async () => await Subject.Download(remoteBook, CreateIndexer()));

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.RemoveTorrent(PackHash.ToLower(), false, It.IsAny<QBittorrentSettings>()), Times.Once());
            _packCalls.Should().BeEmpty();
        }

        [Test]
        public void pack_should_be_removed_again_when_file_selection_fails()
        {
            var remoteBook = GivenPack();
            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(s => s.SetFilePriority(It.IsAny<string>(), It.IsAny<IEnumerable<int>>(), 0, It.IsAny<QBittorrentSettings>()))
                  .Throws(new DownloadClientException("boom"));

            Assert.ThrowsAsync<DownloadClientException>(async () => await Subject.Download(remoteBook, CreateIndexer()));

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.RemoveTorrent(PackHash.ToLower(), false, It.IsAny<QBittorrentSettings>()), Times.Once());
            _packCalls.Should().BeEmpty();
        }

        [Test]
        public void pack_should_be_removed_again_when_a_wanted_file_is_not_listed_by_the_client()
        {
            var remoteBook = GivenPack();
            remoteBook.PackFiles[646] = new TorrentFileEntry(24, "Missing/Missing v01 (Digital).cbz", 1000);

            Assert.ThrowsAsync<DownloadClientException>(async () => await Subject.Download(remoteBook, CreateIndexer()));

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.RemoveTorrent(PackHash.ToLower(), false, It.IsAny<QBittorrentSettings>()), Times.Once());
            _packCalls.Should().BeEmpty();
        }

        [Test]
        public void pack_should_not_be_sent_to_a_client_that_cannot_select_files()
        {
            var remoteBook = GivenPack();
            Mocker.GetMock<IQBittorrentProxySelector>()
                  .Setup(s => s.GetApiVersion(It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()))
                  .Returns(new Version(1, 5));

            Assert.ThrowsAsync<DownloadClientException>(async () => await Subject.Download(remoteBook, CreateIndexer()));

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.AddTorrentFromFile(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public async Task pack_should_use_cached_torrent_bytes_instead_of_fetching()
        {
            var remoteBook = GivenPack();

            await Subject.Download(remoteBook, CreateIndexer());

            Mocker.GetMock<IHttpClient>()
                  .Verify(v => v.GetAsync(It.IsAny<HttpRequest>()), Times.Never());
        }

        [TestCase(QBittorrentState.Start, "start")]
        [TestCase(QBittorrentState.ForceStart, "force")]
        [TestCase(QBittorrentState.Stop, null)]
        public async Task pack_already_in_client_should_select_matched_file_and_resume(QBittorrentState initialState, string expectedStart)
        {
            Subject.Definition.Settings.As<QBittorrentSettings>().InitialState = (int)initialState;
            var remoteBook = GivenPack(alreadyLoaded: true, padAt: 10);

            var id = await Subject.Download(remoteBook, CreateIndexer());

            id.Should().Be(PackHash);

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.AddTorrentFromFile(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.SetFilePriority(PackHash.ToLower(), It.Is<IEnumerable<int>>(i => i.SequenceEqual(new[] { _wantedClientIndex })), 1, It.IsAny<QBittorrentSettings>()), Times.Once());
            _packCalls.Should().Equal(expectedStart == null ? new[] { "prio1" } : new[] { "prio1", expectedStart });
        }

        [Test]
        public void loaded_pack_mangarr_never_grabbed_should_be_rejected_untouched()
        {
            Subject.Definition.Settings.As<QBittorrentSettings>().InitialState = (int)QBittorrentState.Start;
            var remoteBook = GivenPack(alreadyLoaded: true);
            remoteBook.PackPreviouslyGrabbed = false;
            _packListed = false;

            Assert.ThrowsAsync<DownloadClientRejectedReleaseException>(async () => await Subject.Download(remoteBook, CreateIndexer()));

            _packCalls.Should().BeEmpty();
            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.AddTorrentFromFile(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TorrentSeedConfiguration>(), It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.RemoveTorrent(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public async Task imported_pack_in_the_post_import_category_should_return_to_the_category_before_it_starts()
        {
            Subject.Definition.Settings.As<QBittorrentSettings>().InitialState = (int)QBittorrentState.Start;
            Subject.Definition.Settings.As<QBittorrentSettings>().MusicImportedCategory = "music-imported";
            var remoteBook = GivenPack(alreadyLoaded: true);
            _packListed = false;

            await Subject.Download(remoteBook, CreateIndexer());

            _packCalls.Should().Equal("prio1", "label:music", "start");
            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.SetTorrentLabel(PackHash.ToLower(), "music", It.IsAny<QBittorrentSettings>()), Times.Once());
        }

        [Test]
        public async Task loaded_pack_already_in_the_category_should_keep_its_label()
        {
            Subject.Definition.Settings.As<QBittorrentSettings>().InitialState = (int)QBittorrentState.Start;
            var remoteBook = GivenPack(alreadyLoaded: true);

            await Subject.Download(remoteBook, CreateIndexer());

            _packCalls.Should().Equal("prio1", "start");
            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.SetTorrentLabel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public async Task loaded_pack_should_not_be_relabelled_without_a_category()
        {
            Subject.Definition.Settings.As<QBittorrentSettings>().InitialState = (int)QBittorrentState.Start;
            Subject.Definition.Settings.As<QBittorrentSettings>().MusicCategory = null;
            var remoteBook = GivenPack(alreadyLoaded: true);
            _packListed = false;

            await Subject.Download(remoteBook, CreateIndexer());

            _packCalls.Should().Equal("prio1", "start");
            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.SetTorrentLabel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public async Task normal_grab_should_not_touch_file_priorities()
        {
            GivenSuccessfulDownload();

            await Subject.Download(CreateRemoteBook(), CreateIndexer());

            Mocker.GetMock<IQBittorrentProxy>()
                  .Verify(v => v.SetFilePriority(It.IsAny<string>(), It.IsAny<IEnumerable<int>>(), It.IsAny<int>(), It.IsAny<QBittorrentSettings>()), Times.Never());
        }

        [Test]
        public void can_select_files_only_on_api_v2()
        {
            Mocker.GetMock<IQBittorrentProxySelector>()
                  .Setup(s => s.GetApiVersion(It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()))
                  .Returns(new Version(1, 5));
            Subject.CanSelectFiles.Should().BeFalse();

            Mocker.GetMock<IQBittorrentProxySelector>()
                  .Setup(s => s.GetApiVersion(It.IsAny<QBittorrentSettings>(), It.IsAny<bool>()))
                  .Returns(new Version(2, 9, 3));
            Subject.CanSelectFiles.Should().BeTrue();
        }

        [Test]
        public void Test_should_force_api_version_check()
        {
            // Set TestConnection up to fail quick
            Mocker.GetMock<IQBittorrentProxy>()
                  .Setup(v => v.GetApiVersion(It.IsAny<QBittorrentSettings>()))
                  .Returns(new Version(1, 0));

            Subject.Test();

            Mocker.GetMock<IQBittorrentProxySelector>()
                  .Verify(v => v.GetProxy(It.IsAny<QBittorrentSettings>(), true), Times.Once());
        }
    }
}
