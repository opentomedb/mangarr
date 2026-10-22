using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class TrackedDownloadServiceFixture : CoreTest<TrackedDownloadService>
    {
        private void GivenDownloadHistory()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
                .Returns(new List<EntityHistory>()
                {
                    new EntityHistory()
                    {
                         DownloadId = "35238",
                         SourceTitle = "Audio Author - Audio Book [2018 - FLAC]",
                         AuthorId = 5,
                         BookId = 4,
                    }
                });
        }

        [Test]
        public void should_track_downloads_using_the_source_title_if_it_cannot_be_found_using_the_download_title()
        {
            GivenDownloadHistory();

            var remoteBook = new RemoteBook
            {
                Author = new Author() { Id = 5 },
                Books = new List<Book> { new Book { Id = 4 } },
                ParsedBookInfo = new ParsedBookInfo()
                {
                    BookTitle = "Audio Book",
                    AuthorName = "Audio Author"
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedBookInfo>(i => i.BookTitle == "Audio Book" && i.AuthorName == "Audio Author"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteBook);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "The torrent release folder",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.Should().NotBeNull();
            trackedDownload.RemoteBook.Should().NotBeNull();
            trackedDownload.RemoteBook.Author.Should().NotBeNull();
            trackedDownload.RemoteBook.Author.Id.Should().Be(5);
            trackedDownload.RemoteBook.Books.First().Id.Should().Be(4);
        }

        [Test]
        public void should_unmap_tracked_download_if_book_deleted()
        {
            GivenDownloadHistory();

            var remoteBook = new RemoteBook
            {
                Author = new Author() { Id = 5 },
                Books = new List<Book> { new Book { Id = 4 } },
                ParsedBookInfo = new ParsedBookInfo()
                {
                    BookTitle = "Audio Book",
                    AuthorName = "Audio Author"
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedBookInfo>(i => i.BookTitle == "Audio Book" && i.AuthorName == "Audio Author"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteBook);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "Audio Author - Audio Book [2018 - FLAC]",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            // get a tracked download in place
            var trackedDownload = Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            // simulate deletion - book no longer maps
            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.Is<ParsedBookInfo>(i => i.BookTitle == "Audio Book" && i.AuthorName == "Audio Author"), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns(default(RemoteBook));

            // handle deletion event
            Subject.Handle(new BookInfoRefreshedEvent(remoteBook.Author, new List<Book>(), new List<Book>(), remoteBook.Books));

            // verify download has null remote book
            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteBook.Should().BeNull();
        }

        [Test]
        public void should_not_throw_when_processing_deleted_episodes()
        {
            GivenDownloadHistory();

            var remoteEpisode = new RemoteBook
            {
                Author = new Author() { Id = 5 },
                Books = new List<Book> { new Book { Id = 4 } },
                ParsedBookInfo = new ParsedBookInfo()
                {
                    BookTitle = "TV Series"
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<int>(), It.IsAny<List<int>>()))
                  .Returns(default(RemoteBook));

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<EntityHistory>());

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "TV Series - S01E01",
                DownloadId = "12345",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Id = 1,
                    Type = "Blackhole",
                    Name = "Blackhole Client",
                    Protocol = DownloadProtocol.Torrent
                }
            };

            Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<int>(), It.IsAny<List<int>>()))
                  .Returns(default(RemoteBook));

            Subject.Handle(new BookInfoRefreshedEvent(remoteEpisode.Author, new List<Book>(), new List<Book>(), remoteEpisode.Books));

            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteBook.Should().BeNull();
        }

        [Test]
        public void should_not_throw_when_processing_deleted_series()
        {
            GivenDownloadHistory();

            var remoteEpisode = new RemoteBook
            {
                Author = new Author() { Id = 5 },
                Books = new List<Book> { new Book { Id = 4 } },
                ParsedBookInfo = new ParsedBookInfo()
                {
                    BookTitle = "TV Series",
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<int>(), It.IsAny<List<int>>()))
                  .Returns(default(RemoteBook));

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<EntityHistory>());

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "TV Series - S01E01",
                DownloadId = "12345",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Id = 1,
                    Type = "Blackhole",
                    Name = "Blackhole Client",
                    Protocol = DownloadProtocol.Torrent
                }
            };

            Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<int>(), It.IsAny<List<int>>()))
                  .Returns(default(RemoteBook));

            Subject.Handle(new AuthorDeletedEvent(remoteEpisode.Author, true, true));

            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteBook.Should().BeNull();
        }

        private static DownloadClientItem LightNovelItem(string title)
        {
            return new DownloadClientItem
            {
                Title = title,
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo { Protocol = DownloadProtocol.Torrent, Id = 1, Name = "client" }
            };
        }

        private static RemoteBook LightNovelRemoteBook(MediaType mediaType)
        {
            return new RemoteBook
            {
                Author = new Author { Id = 5 },
                Books = new List<Book> { new Book { Id = 4 } },
                ParsedBookInfo = new ParsedBookInfo { BookTitle = "Overlord Vol. 5", AuthorName = "Kugane Maruyama" },
                MediaType = mediaType
            };
        }

        private void GivenMapsTo(RemoteBook remoteBook)
        {
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(remoteBook);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                  .Returns(remoteBook);
        }

        [Test]
        public void should_type_the_queue_item_from_the_grab_history_row_not_the_client_title()
        {
            // A tokenless light-novel title maps Archive from the client's title (no author, no
            // regrade); the grab row carries the class the decision maker actually chose.
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId("35238"))
                .Returns(new List<EntityHistory>
                {
                    new EntityHistory
                    {
                        DownloadId = "35238",
                        SourceTitle = "Overlord Vol. 5",
                        AuthorId = 5,
                        BookId = 4,
                        EventType = EntityHistoryEventType.Grabbed,
                        Data = new Dictionary<string, string> { { EntityHistory.MEDIA_TYPE, "ebook" } }
                    }
                });

            GivenMapsTo(LightNovelRemoteBook(MediaType.Archive));

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var trackedDownload = Subject.TrackDownload(client, LightNovelItem("Kugane Maruyama - Overlord Vol. 5 [2019]"));

            trackedDownload.RemoteBook.Should().NotBeNull();
            trackedDownload.RemoteBook.MediaType.Should().Be(MediaType.Ebook);
        }

        [Test]
        public void should_keep_the_title_derived_media_type_when_there_is_no_history()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<EntityHistory>());

            GivenMapsTo(LightNovelRemoteBook(MediaType.Audio));

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var trackedDownload = Subject.TrackDownload(client, LightNovelItem("Kugane Maruyama - Overlord Vol. 5 [2019] [M4B]"));

            trackedDownload.RemoteBook.MediaType.Should().Be(MediaType.Audio);
        }

        // Light-novel audio (2026-09-18, D4): the history-path re-parse gets the grab row's media type,
        // so an Audible-named release the generic parser cannot read is bridged to the grabbed volume
        // and the queue item carries its RemoteBook. The parser is static, so the pin sits at the seam
        // the fixture sees: Map receives the bridge's ParsedBookInfo (the series by name, the volume by
        // number, ReleaseTitle set) for an audio grab row and never for an ebook one -- the bridge
        // stays Audio-only.
        //
        // The title is "<Series>: <Audible title>" (final review, 2026-09-20): T2's ebook bridge keys
        // "<Subtitle> (<Series> <N>)", the shape this case used before, so an ebook grab of THAT title
        // bridged too and the case stopped pinning anything. The series in front of the product name
        // is an audiobook key ("<Series> <Audible title>") and no ebook key: every ebook key either
        // ends in the volume number or is a stored name alone. A colon rather than " - " keeps the
        // generic parser off it (it reads "<Author> - <Book>", and then no bridge runs at all), and
        // rather than a parenthetical, whose dropped reading would be the bare series name -- which
        // the whole-series batch bridge claims before either volume bridge is reached.
        private static Author LightNovelSeries()
        {
            return new Author
            {
                Id = 1,
                CleanName = "swordartonline~ln",
                AuthorMetadataId = 1,
                Metadata = new AuthorMetadata { Name = "Sword Art Online", ForeignAuthorId = "local-sword-art-online~ln", Aliases = new List<string> { "SAO" } }
            };
        }

        private static Book LightNovelVolume(double number, string subtitle, string audiobookTitle)
        {
            var book = new Book
            {
                Id = (int)number,
                Title = $"Sword Art Online Vol. {number}",
                ForeignBookId = $"local-sword-art-online~ln-v{number}",
                VolumeNumber = number,
                Subtitle = subtitle,
                AuthorMetadataId = 1
            };

            book.Editions = new List<Edition>
            {
                new Edition { BookId = book.Id, MediaType = MediaType.Ebook, Title = book.Title, Monitored = true },
                new Edition { BookId = book.Id, MediaType = MediaType.Audio, Title = book.Title, AudiobookTitle = audiobookTitle, Monitored = true }
            };

            return book;
        }

        [TestCase("audio", true)]
        [TestCase("ebook", false)]
        public void should_bridge_an_audible_named_history_title_for_an_audio_grab_only(string grabbedMediaType, bool bridged)
        {
            const string title = "Sword Art Online: Sword Art Online 21: Unital Ring I [M4B]";
            var sao = LightNovelSeries();
            var vol21 = LightNovelVolume(21, "Unital Ring I", "Sword Art Online 21: Unital Ring I");

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId("35238"))
                .Returns(new List<EntityHistory>
                {
                    new EntityHistory
                    {
                        DownloadId = "35238",
                        SourceTitle = title,
                        AuthorId = sao.Id,
                        BookId = vol21.Id,
                        Author = sao,
                        Book = vol21,
                        EventType = EntityHistoryEventType.Grabbed,
                        Data = new Dictionary<string, string> { { EntityHistory.MEDIA_TYPE, grabbedMediaType } }
                    }
                });

            // Only the bridge parses this title to this shape; nothing else reaches Map.
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedBookInfo>(i => i.AuthorName == "Sword Art Online" && i.VolumeNumber == 21 && i.ReleaseTitle == title), sao.Id, It.IsAny<IEnumerable<int>>()))
                  .Returns(new RemoteBook { Author = sao, Books = new List<Book> { vol21 }, ParsedBookInfo = new ParsedBookInfo { AuthorName = "Sword Art Online", VolumeNumber = 21 } });

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var trackedDownload = Subject.TrackDownload(client, LightNovelItem(title));

            trackedDownload.Should().NotBeNull();

            if (bridged)
            {
                trackedDownload.RemoteBook.Should().NotBeNull();
                trackedDownload.RemoteBook.Books.Should().ContainSingle().Which.Should().BeSameAs(vol21);
                trackedDownload.RemoteBook.MediaType.Should().Be(MediaType.Audio);
            }
            else
            {
                trackedDownload.RemoteBook.Should().BeNull();
            }
        }

        // Grab-trusted mapping (2026-09-20): the client's title alone is ambiguous between
        // look-alike series -- "Sword Art Online Progressive - Canon of the Golden Rule v01 [2024]"
        // parses as Sword Art Online Progressive v1 -- so a grabbed download is mapped from its
        // own grab row. The first two cases are the proof as a pair: the SAME download title maps
        // to the look-alike when there is no grab, and to the grabbed series when there is one.
        private const string LookAlikeTitle = "Sword Art Online Progressive - Canon of the Golden Rule v01 [2024]";

        private static RemoteBook LookAlikeRemoteBook()
        {
            return new RemoteBook
            {
                Author = new Author { Id = 24 },
                Books = new List<Book> { new Book { Id = 100 } },
                ParsedBookInfo = new ParsedBookInfo { BookTitle = "Sword Art Online Progressive" }
            };
        }

        private static RemoteBook GrabbedRemoteBook()
        {
            return new RemoteBook
            {
                Author = new Author { Id = 92 },
                Books = new List<Book> { new Book { Id = 500 } },
                ParsedBookInfo = new ParsedBookInfo { BookTitle = "Canon of the Golden Rule" }
            };
        }

        private void GivenGrabbedHistory(string sourceTitle)
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId("35238"))
                .Returns(new List<EntityHistory>
                {
                    new EntityHistory
                    {
                        DownloadId = "35238",
                        SourceTitle = sourceTitle,
                        AuthorId = 92,
                        BookId = 500,
                        EventType = EntityHistoryEventType.Grabbed
                    }
                });
        }

        [Test]
        public void should_map_from_the_grab_history_before_the_download_title()
        {
            GivenGrabbedHistory(LookAlikeTitle);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(LookAlikeRemoteBook());

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), 92, It.IsAny<IEnumerable<int>>()))
                  .Returns(GrabbedRemoteBook());

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var trackedDownload = Subject.TrackDownload(client, LightNovelItem(LookAlikeTitle));

            trackedDownload.RemoteBook.Should().NotBeNull();
            trackedDownload.RemoteBook.Author.Id.Should().Be(92);
            trackedDownload.RemoteBook.Books.Select(b => b.Id).Should().BeEquivalentTo(new[] { 500 });

            Mocker.GetMock<IParsingService>()
                  .Verify(s => s.Map(It.IsAny<ParsedBookInfo>(), 92, It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 500 }))), Times.Once());

            Mocker.GetMock<IParsingService>()
                  .Verify(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()), Times.Never());
        }

        [Test]
        public void should_map_from_the_download_title_when_there_is_no_history()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<EntityHistory>());

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(LookAlikeRemoteBook());

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var trackedDownload = Subject.TrackDownload(client, LightNovelItem(LookAlikeTitle));

            trackedDownload.RemoteBook.Should().NotBeNull();
            trackedDownload.RemoteBook.Author.Id.Should().Be(24);
            trackedDownload.RemoteBook.Books.Select(b => b.Id).Should().BeEquivalentTo(new[] { 100 });

            Mocker.GetMock<IParsingService>()
                  .Verify(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()), Times.Once());
        }

        // Final review M1 (2026-09-20): an orphan grab row -- a grabbed book deleted while its
        // author remains, until the housekeeper runs -- makes the book lookup throw on the
        // repository's row-count check. The download must stay in the queue, mapped from its
        // title, not vanish from it.
        [Test]
        public void should_map_from_the_download_title_when_the_grab_row_no_longer_resolves()
        {
            GivenGrabbedHistory(LookAlikeTitle);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), 92, It.IsAny<IEnumerable<int>>()))
                  .Throws(new InvalidOperationException("Expected query to return 1 rows but returned 0"));

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(LookAlikeRemoteBook());

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var trackedDownload = Subject.TrackDownload(client, LightNovelItem(LookAlikeTitle));

            trackedDownload.Should().NotBeNull();
            trackedDownload.RemoteBook.Should().NotBeNull();
            trackedDownload.RemoteBook.Author.Id.Should().Be(24);
            trackedDownload.RemoteBook.Books.Select(b => b.Id).Should().BeEquivalentTo(new[] { 100 });

            Mocker.GetMock<IParsingService>()
                  .Verify(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<SearchCriteriaBase>()), Times.Once());
        }

        [Test]
        public void should_fall_back_to_the_source_title_when_the_download_title_does_not_parse()
        {
            // A hash-named torrent folder: ParseBookTitle rejects it, so the mapping is built from
            // the grab row's own source title. The row carries no lazy-loaded Author/Book, so the
            // Audible bridge cannot stand in for the source-title parse here.
            GivenGrabbedHistory(LookAlikeTitle);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedBookInfo>(i => i.ReleaseTitle == LookAlikeTitle), 92, It.IsAny<IEnumerable<int>>()))
                  .Returns(GrabbedRemoteBook());

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var trackedDownload = Subject.TrackDownload(client, LightNovelItem("3fa9bd2e7c1148d0a6b5e4f70921cc83"));

            trackedDownload.RemoteBook.Should().NotBeNull();
            trackedDownload.RemoteBook.Author.Id.Should().Be(92);
            trackedDownload.RemoteBook.Books.Select(b => b.Id).Should().BeEquivalentTo(new[] { 500 });
        }

        // Monthly packs: one torrent, one Grabbed row per volume across several series, each naming its file.
        private static EntityHistory PackGrab(int authorId, int bookId, string file, int index)
        {
            return new EntityHistory
            {
                DownloadId = "PACKHASH",
                SourceTitle = "Viz Manga Volumes Update 2026-10 (Digital)",
                AuthorId = authorId,
                BookId = bookId,
                EventType = EntityHistoryEventType.Grabbed,
                Data = new Dictionary<string, string>
                {
                    { EntityHistory.MEDIA_TYPE, "archive" },
                    { "PackFile", file },
                    { "PackFileIndex", index.ToString() }
                }
            };
        }

        private List<EntityHistory> GivenPackGrabs(params EntityHistory[] rows)
        {
            var history = rows.ToList();

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId("PACKHASH"))
                .Returns(() => history.ToList());

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns<ParsedBookInfo, int, IEnumerable<int>>((info, authorId, bookIds) => new RemoteBook
                {
                    ParsedBookInfo = info,
                    Author = new Author { Id = authorId },
                    Books = bookIds.Select(id => new Book { Id = id }).ToList()
                });

            return history;
        }

        private static DownloadClientItem PackItem()
        {
            return new DownloadClientItem
            {
                Title = "Viz Manga Volumes Update 2026-10 (Digital)",
                DownloadId = "PACKHASH",
                DownloadClientInfo = new DownloadClientItemClientInfo { Protocol = DownloadProtocol.Torrent, Id = 1, Name = "qBittorrent" }
            };
        }

        [Test]
        public void should_rebuild_the_pack_files_of_a_pack_download_from_its_grab_rows()
        {
            GivenPackGrabs(PackGrab(1, 11, "Kaiju No. 8 - Relax v02 (2026) (Digital) (Rillant).cbz", 4),
                           PackGrab(2, 21, "Jujutsu Kaisen v28 (2026) (Digital) (Rillant).cbz", 9),
                           PackGrab(1, 12, "Kaiju No. 8 v15 (2026) (Digital) (Rillant).cbz", 5));

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var trackedDownload = Subject.TrackDownload(client, PackItem());

            trackedDownload.RemoteBook.Books.Select(b => b.Id).Should().BeEquivalentTo(new[] { 11, 12, 21 });
            trackedDownload.RemoteBook.PackFiles.Should().BeEquivalentTo(new Dictionary<int, TorrentFileEntry>
            {
                { 11, new TorrentFileEntry(4, "Kaiju No. 8 - Relax v02 (2026) (Digital) (Rillant).cbz", 0) },
                { 12, new TorrentFileEntry(5, "Kaiju No. 8 v15 (2026) (Digital) (Rillant).cbz", 0) },
                { 21, new TorrentFileEntry(9, "Jujutsu Kaisen v28 (2026) (Digital) (Rillant).cbz", 0) }
            });
        }

        [Test]
        public void should_not_set_pack_files_for_a_normal_grab()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId("35238"))
                .Returns(new List<EntityHistory>
                {
                    new EntityHistory { DownloadId = "35238", SourceTitle = "Audio Author - Audio Book [2018 - FLAC]", AuthorId = 5, BookId = 4, EventType = EntityHistoryEventType.Grabbed }
                });

            GivenMapsTo(new RemoteBook { Author = new Author { Id = 5 }, Books = new List<Book> { new Book { Id = 4 } }, ParsedBookInfo = new ParsedBookInfo() });

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var trackedDownload = Subject.TrackDownload(client, LightNovelItem("Audio Author - Audio Book [2018 - FLAC]"));

            trackedDownload.RemoteBook.PackFiles.Should().BeNull();
        }

        [Test]
        public void should_track_an_imported_pack_again_when_another_of_its_volumes_is_grabbed()
        {
            var history = GivenPackGrabs(PackGrab(1, 11, "Kaiju No. 8 - Relax v02 (2026) (Digital) (Rillant).cbz", 4));

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem("PACKHASH"))
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadImported });

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            Subject.TrackDownload(client, PackItem()).State.Should().Be(TrackedDownloadState.Imported);

            // Another volume's file in the same, already-loaded torrent is selected (a later pack grab).
            history.Insert(0, PackGrab(2, 21, "Jujutsu Kaisen v28 (2026) (Digital) (Rillant).cbz", 9));

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem("PACKHASH"))
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadGrabbed });

            var grab = new RemoteBook
            {
                Books = new List<Book> { new Book { Id = 21 } },
                PackFiles = new Dictionary<int, TorrentFileEntry> { { 21, new TorrentFileEntry(9, "Jujutsu Kaisen v28 (2026) (Digital) (Rillant).cbz", 1000) } }
            };

            Subject.Handle(new BookGrabbedEvent(grab) { DownloadId = "PACKHASH" });

            var trackedDownload = Subject.TrackDownload(client, PackItem());

            trackedDownload.State.Should().Be(TrackedDownloadState.Downloading);
            trackedDownload.RemoteBook.PackFiles.Keys.Should().BeEquivalentTo(new[] { 11, 21 });
        }

        private void GivenPackImport(DownloadClientItem item)
        {
            var root = item.OutputPath.FullPath;
            Mocker.GetMock<IHistoryService>().Setup(s => s.MostRecentForDownloadId("PACKHASH")).Returns(new EntityHistory { DownloadId = "PACKHASH", EventType = EntityHistoryEventType.Grabbed });
            Mocker.GetMock<IProvideImportItemService>()
                .Setup(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                .Returns<DownloadClientItem, DownloadClientItem>((i, p) => i);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(root)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns<string, ImportMode, Author, DownloadClientItem, Book>((path, mode, author, i, book) => new List<ImportResult>
                {
                    new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = path, Author = author, Book = book }))
                });
        }

        private static DownloadClientItem CompletedPackItem()
        {
            var item = PackItem();
            item.Status = DownloadItemStatus.Completed;
            item.OutputPath = new OsPath(@"C:\Downloads\Viz Pack".AsOsAgnostic());
            return item;
        }

        [Test]
        public void a_pack_with_a_deleted_volume_still_tracks_and_imports_the_others()
        {
            const string kaijuFile = "Kaiju No. 8 - Relax v02 (2026) (Digital) (Rillant).cbz";
            const string jujutsuFile = "Jujutsu Kaisen v28 (2026) (Digital) (Rillant).cbz";
            GivenPackGrabs(PackGrab(2, 21, jujutsuFile, 9), PackGrab(1, 11, kaijuFile, 4));

            // Book 21 was deleted (its series remains): loading it with the others throws, as BasicRepository.Get does.
            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.IsAny<ParsedBookInfo>(), It.IsAny<int>(), It.Is<IEnumerable<int>>(ids => ids.Contains(21))))
                .Throws(new ApplicationException("Expected query to return 2 rows but returned 1"));
            Mocker.GetMock<IBookService>().Setup(s => s.GetBook(11)).Returns(new Book { Id = 11 });
            Mocker.GetMock<IBookService>().Setup(s => s.GetBook(21)).Throws(new ModelNotFoundException(typeof(Book), 21));

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var item = CompletedPackItem();
            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.RemoteBook.Should().NotBeNull();
            trackedDownload.RemoteBook.Books.Select(b => b.Id).Should().Equal(11);
            trackedDownload.RemoteBook.PackFiles.Keys.Should().Equal(11);

            GivenPackImport(item);
            var completed = Mocker.Resolve<CompletedDownloadService>();
            completed.Check(trackedDownload);
            completed.Import(trackedDownload);

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Verify(v => v.ProcessPath(Path.Combine(item.OutputPath.FullPath, kaijuFile), ImportMode.Copy, It.IsAny<Author>(), item, It.Is<Book>(b => b.Id == 11)), Times.Once());
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Verify(v => v.ProcessPath(Path.Combine(item.OutputPath.FullPath, jujutsuFile), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()), Times.Never());
            trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }

        [Test]
        public void a_pack_leaves_its_already_imported_volumes_out_of_the_mapping()
        {
            var history = GivenPackGrabs(PackGrab(2, 21, "Jujutsu Kaisen v28 (2026) (Digital) (Rillant).cbz", 9),
                                         PackGrab(1, 11, "Kaiju No. 8 - Relax v02 (2026) (Digital) (Rillant).cbz", 4));
            history.Add(new EntityHistory { DownloadId = "PACKHASH", BookId = 11, AuthorId = 1, EventType = EntityHistoryEventType.BookFileImported });

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var trackedDownload = Subject.TrackDownload(client, PackItem());

            trackedDownload.RemoteBook.Books.Select(b => b.Id).Should().Equal(21);
            trackedDownload.RemoteBook.PackFiles.Keys.Should().Equal(21);
        }

        [Test]
        public void a_pack_with_every_volume_imported_settles_without_a_folder_import()
        {
            const string kaijuFile = "Kaiju No. 8 - Relax v02 (2026) (Digital) (Rillant).cbz";
            var history = GivenPackGrabs(PackGrab(1, 11, kaijuFile, 4));
            history.Add(new EntityHistory { DownloadId = "PACKHASH", BookId = 11, AuthorId = 1, EventType = EntityHistoryEventType.BookFileImported });

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var item = CompletedPackItem();
            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.RemoteBook.Books.Should().BeEmpty();
            trackedDownload.RemoteBook.PackFiles.Should().BeEmpty();

            GivenPackImport(item);
            trackedDownload.State = TrackedDownloadState.ImportPending;
            Mocker.Resolve<CompletedDownloadService>().Import(trackedDownload);

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Verify(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()), Times.Never());
            trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }

        // A completed two-series pack waiting to import; the event under test then changes its mapping.
        private TrackedDownload GivenImportPendingPack(List<EntityHistory> history, DownloadClientItem item)
        {
            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem("PACKHASH"))
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadGrabbed });
            GivenPackImport(item);

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var trackedDownload = Subject.TrackDownload(client, item);
            Mocker.Resolve<CompletedDownloadService>().Check(trackedDownload);
            trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);

            return trackedDownload;
        }

        private void ImportRetrackedPack(DownloadClientItem item, string remainingFile, int remainingBookId)
        {
            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var trackedDownload = Subject.TrackDownload(client, item);
            var completed = Mocker.Resolve<CompletedDownloadService>();
            completed.Check(trackedDownload);
            completed.Import(trackedDownload);

            var root = item.OutputPath.FullPath;
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Verify(v => v.ProcessPath(Path.Combine(root, remainingFile), ImportMode.Copy, It.IsAny<Author>(), item, It.Is<Book>(b => b.Id == remainingBookId)), Times.Once());
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Verify(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()), Times.Once());
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Verify(v => v.ProcessPath(root, It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()), Times.Never());
        }

        [Test]
        public void a_pack_with_a_volume_removed_by_a_refresh_is_retracked_from_its_grab_rows()
        {
            const string kaijuFile = "Kaiju No. 8 - Relax v02 (2026) (Digital) (Rillant).cbz";
            const string jujutsuFile = "Jujutsu Kaisen v28 (2026) (Digital) (Rillant).cbz";
            var history = GivenPackGrabs(PackGrab(2, 21, jujutsuFile, 9), PackGrab(1, 11, kaijuFile, 4));
            var item = CompletedPackItem();
            GivenImportPendingPack(history, item);

            // The refresh removes Jujutsu's volume; its grab row goes with the book (history joins Books).
            history.RemoveAll(h => h.BookId == 21);
            Subject.Handle(new BookInfoRefreshedEvent(new Author { Id = 2 }, new List<Book>(), new List<Book>(), new List<Book> { new Book { Id = 21 } }));

            ImportRetrackedPack(item, kaijuFile, 11);
        }

        [Test]
        public void a_pack_whose_series_is_deleted_is_retracked_from_its_grab_rows()
        {
            const string kaijuFile = "Kaiju No. 8 - Relax v02 (2026) (Digital) (Rillant).cbz";
            const string jujutsuFile = "Jujutsu Kaisen v28 (2026) (Digital) (Rillant).cbz";
            var history = GivenPackGrabs(PackGrab(2, 21, jujutsuFile, 9), PackGrab(1, 11, kaijuFile, 4));
            var item = CompletedPackItem();
            var trackedDownload = GivenImportPendingPack(history, item);
            trackedDownload.RemoteBook.Author.Id.Should().Be(2);

            // Deleting the pack's mapped series deletes its history rows too.
            history.RemoveAll(h => h.AuthorId == 2);
            Subject.Handle(new AuthorDeletedEvent(new Author { Id = 2 }, false, false));

            ImportRetrackedPack(item, kaijuFile, 11);
        }

        [Test]
        public void a_volume_grabbed_later_from_an_imported_pack_is_tracked_again_and_imported()
        {
            // End to end over tracking and import: the pack imported Kaiju; a later pack grab selected Jujutsu.
            const string kaijuFile = "Kaiju No. 8 - Relax v02 (2026) (Digital) (Rillant).cbz";
            const string jujutsuFile = "Jujutsu Kaisen v28 (2026) (Digital) (Rillant).cbz";
            var history = GivenPackGrabs(PackGrab(1, 11, kaijuFile, 4));
            history.Insert(0, new EntityHistory { DownloadId = "PACKHASH", BookId = 11, AuthorId = 1, EventType = EntityHistoryEventType.BookFileImported });

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem("PACKHASH"))
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadImported });

            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var item = PackItem();
            item.Status = DownloadItemStatus.Completed;
            item.OutputPath = new OsPath(@"C:\Downloads\Viz Pack".AsOsAgnostic());

            Subject.TrackDownload(client, item).State.Should().Be(TrackedDownloadState.Imported);

            history.Insert(0, PackGrab(2, 21, jujutsuFile, 9));
            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem("PACKHASH"))
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadGrabbed });

            // As DownloadService publishes it after qBittorrent's already-loaded branch returns the hash.
            var grab = new RemoteBook
            {
                Books = new List<Book> { new Book { Id = 21 } },
                PackFiles = new Dictionary<int, TorrentFileEntry> { { 21, new TorrentFileEntry(9, jujutsuFile, 1000) } }
            };
            Subject.Handle(new BookGrabbedEvent(grab) { DownloadId = "PACKHASH" });

            var trackedDownload = Subject.TrackDownload(client, item);
            trackedDownload.State.Should().Be(TrackedDownloadState.Downloading);

            var root = item.OutputPath.FullPath;
            var jujutsuPath = Path.Combine(root, jujutsuFile);
            Mocker.GetMock<IHistoryService>().Setup(s => s.MostRecentForDownloadId("PACKHASH")).Returns(history[0]);
            Mocker.GetMock<IProvideImportItemService>()
                .Setup(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                .Returns<DownloadClientItem, DownloadClientItem>((i, p) => i);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(root)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()))
                .Returns<string, ImportMode, Author, DownloadClientItem, Book>((path, mode, author, i, book) => new List<ImportResult>
                {
                    new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = path, Author = author, Book = book }))
                });

            var completed = Mocker.Resolve<CompletedDownloadService>();
            completed.Check(trackedDownload);
            trackedDownload.State.Should().Be(TrackedDownloadState.ImportPending);
            completed.Import(trackedDownload);

            Mocker.GetMock<IDownloadedBooksImportService>()
                .Verify(v => v.ProcessPath(jujutsuPath, ImportMode.Copy, It.IsAny<Author>(), item, It.Is<Book>(b => b.Id == 21)), Times.Once());
            Mocker.GetMock<IDownloadedBooksImportService>()
                .Verify(v => v.ProcessPath(Path.Combine(root, kaijuFile), It.IsAny<ImportMode>(), It.IsAny<Author>(), It.IsAny<DownloadClientItem>(), It.IsAny<Book>()), Times.Never());
            trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }
    }
}
