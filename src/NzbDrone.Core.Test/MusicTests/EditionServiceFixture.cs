using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class EditionServiceFixture : CoreTest<EditionService>
    {
        private Book _volume;
        private Edition _audio;

        [SetUp]
        public void Setup()
        {
            _volume = new Book { Id = 3 };
            _audio = new Edition { Id = 7, BookId = 3, MediaType = MediaType.Audio, Monitored = true, Book = _volume };

            Mocker.GetMock<IEditionRepository>()
                  .Setup(s => s.Get(7))
                  .Returns(_audio);
        }

        [Test]
        public void monitoring_goes_through_the_repository_collapse()
        {
            _audio.Monitored = false;

            var result = Subject.SetMonitored(7, true);

            result.Monitored.Should().BeTrue();
            Mocker.GetMock<IEditionRepository>().Verify(v => v.SetMonitored(_audio), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.Is<BookEditedEvent>(e => e.Book == _volume)), Times.Once());
        }

        [Test]
        public void unmonitoring_only_flips_that_edition()
        {
            var result = Subject.SetMonitored(7, false);

            result.Monitored.Should().BeFalse();
            Mocker.GetMock<IEditionRepository>().Verify(v => v.SetMonitored(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IEditionRepository>().Verify(v => v.SetFields(It.Is<Edition>(e => e.Id == 7 && !e.Monitored), It.IsAny<Expression<Func<Edition, object>>[]>()), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.Is<BookEditedEvent>(e => e.Book == _volume)), Times.Once());
        }

        [Test]
        public void get_all_editions_includes_unmonitored_editions()
        {
            var ebook = new Edition { Id = 6, BookId = 3, MediaType = MediaType.Ebook, Monitored = true };
            var unmonitoredAudio = new Edition { Id = 7, BookId = 3, MediaType = MediaType.Audio, Monitored = false };

            Mocker.GetMock<IEditionRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<Edition> { ebook, unmonitoredAudio });

            var result = Subject.GetAllEditions();

            result.Should().Equal(ebook, unmonitoredAudio);
            Mocker.GetMock<IEditionRepository>().Verify(v => v.GetAllMonitoredEditions(), Times.Never());
        }
    }
}
