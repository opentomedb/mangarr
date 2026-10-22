using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.QueueTests
{
    [TestFixture]
    public class QueueServiceFixture : CoreTest<QueueService>
    {
        private List<TrackedDownload> _trackedDownloads;

        [SetUp]
        public void SetUp()
        {
            var downloadClientInfo = Builder<DownloadClientItemClientInfo>.CreateNew().Build();

            var downloadItem = Builder<NzbDrone.Core.Download.DownloadClientItem>.CreateNew()
                .With(v => v.RemainingTime = TimeSpan.FromSeconds(10))
                .With(v => v.DownloadClientInfo = downloadClientInfo)
                .Build();

            var author = Builder<Author>.CreateNew()
                .Build();

            var books = Builder<Book>.CreateListOfSize(3)
                .All()
                .With(e => e.AuthorId = author.Id)
                .Build();

            var remoteBook = Builder<RemoteBook>.CreateNew()
                .With(r => r.Author = author)
                .With(r => r.Books = new List<Book>(books))
                .With(r => r.ParsedBookInfo = new ParsedBookInfo())
                .Build();

            _trackedDownloads = Builder<TrackedDownload>.CreateListOfSize(1)
                .All()
                .With(v => v.IsTrackable = true)
                .With(v => v.DownloadItem = downloadItem)
                .With(v => v.RemoteBook = remoteBook)
                .Build()
                .ToList();

            var historyItem = Builder<EntityHistory>.CreateNew()
                .Build();

            Mocker.GetMock<IHistoryService>()
                .Setup(c => c.Find(It.IsAny<string>(), EntityHistoryEventType.Grabbed)).Returns(
                    new List<EntityHistory> { historyItem });
        }

        [Test]
        public void queue_items_should_have_id()
        {
            Subject.Handle(new TrackedDownloadRefreshedEvent(_trackedDownloads));

            var queue = Subject.GetQueue();

            queue.Should().HaveCount(3);

            queue.All(v => v.Id > 0).Should().BeTrue();

            var distinct = queue.Select(v => v.Id).Distinct().ToArray();

            distinct.Should().HaveCount(3);
        }

        [Test]
        public void pack_queue_items_should_show_each_volumes_own_series()
        {
            var kaiju = new Author { Id = 1, Name = "Kaiju No. 8" };
            var jujutsu = new Author { Id = 2, Name = "Jujutsu Kaisen" };
            var kaijuVolume = new Book { Id = 11, Author = kaiju };
            var jujutsuVolume = new Book { Id = 21, Author = jujutsu };

            var remoteBook = _trackedDownloads[0].RemoteBook;
            remoteBook.Author = kaiju;
            remoteBook.Books = new List<Book> { kaijuVolume, jujutsuVolume };
            remoteBook.PackFiles = new Dictionary<int, TorrentFileEntry>
            {
                { 11, new TorrentFileEntry(4, "Kaiju No. 8 - Relax v02 (2026) (Digital) (Rillant).cbz", 0) },
                { 21, new TorrentFileEntry(9, "Jujutsu Kaisen v28 (2026) (Digital) (Rillant).cbz", 0) }
            };

            Subject.Handle(new TrackedDownloadRefreshedEvent(_trackedDownloads));

            var queue = Subject.GetQueue();

            queue.Single(q => q.Book.Id == 11).Author.Should().BeSameAs(kaiju);
            queue.Single(q => q.Book.Id == 21).Author.Should().BeSameAs(jujutsu);
        }
    }
}
