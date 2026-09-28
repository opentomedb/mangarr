using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

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
    }
}
