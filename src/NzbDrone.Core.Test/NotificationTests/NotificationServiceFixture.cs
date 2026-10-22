using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests
{
    // Volumes built from chapter packs (2026-10-21): the import notification says a volume was built
    // from chapters, and an upgrade over a built copy says the official volume replaced it.
    [TestFixture]
    public class NotificationServiceFixture : CoreTest<NotificationService>
    {
        private Author _author;
        private Book _book;
        private Edition _edition;
        private Mock<INotification> _notification;
        private BookDownloadMessage _sent;

        [SetUp]
        public void Setup()
        {
            _author = new Author { Metadata = new AuthorMetadata { Name = "Marriagetoxin", ForeignAuthorId = "local-marriagetoxin" }, Tags = new HashSet<int>() };
            _edition = new Edition { Id = 7, MediaType = MediaType.Archive, Chapters = new List<double> { 72, 73, 74, 75, 76, 77, 78, 79, 80, 81 } };
            _book = new Book { Id = 3, Title = "Marriagetoxin Vol. 9", VolumeNumber = 9, Editions = new List<Edition> { _edition } };

            _notification = new Mock<INotification>();
            _notification.SetupGet(n => n.Definition).Returns(new NotificationDefinition { Id = 1, Name = "Discord", OnUpgrade = true, Tags = new HashSet<int>() });
            _notification.Setup(n => n.OnReleaseImport(It.IsAny<BookDownloadMessage>()))
                .Callback<BookDownloadMessage>(m => _sent = m);

            Mocker.GetMock<INotificationFactory>()
                .Setup(f => f.OnReleaseImportEnabled(It.IsAny<bool>()))
                .Returns(new List<INotification> { _notification.Object });
        }

        private BookFile File(Quality quality)
        {
            return new BookFile { EditionId = _edition.Id, Edition = _edition, Quality = new QualityModel(quality), Path = "/manga/Marriagetoxin/Marriagetoxin Vol. 09.cbz" };
        }

        private void Import(List<BookFile> imported, List<BookFile> old)
        {
            Subject.Handle(new BookImportedEvent(_author, _book, imported, old, true, null));
        }

        [Test]
        public void a_chapter_built_import_says_which_chapters_built_it()
        {
            Import(new List<BookFile> { File(Quality.CbzChapters) }, new List<BookFile>());

            _sent.Message.Should().Be("Marriagetoxin Vol. 9 built from chapters 72–81");
        }

        [Test]
        public void a_trailing_half_chapter_ends_the_range()
        {
            _edition.Chapters = new List<double> { 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 81.5 };

            Import(new List<BookFile> { File(Quality.CbzChapters) }, new List<BookFile>());

            _sent.Message.Should().Be("Marriagetoxin Vol. 9 built from chapters 72–81.5");
        }

        [Test]
        public void an_official_volume_over_a_chapter_built_copy_says_it_replaced_it()
        {
            Import(new List<BookFile> { File(Quality.CBZ) }, new List<BookFile> { File(Quality.CbzChapters) });

            _sent.Message.Should().Be("Marriagetoxin Vol. 9: the official volume replaced the chapter-built copy");
        }

        [Test]
        public void a_normal_import_message_is_unchanged()
        {
            Import(new List<BookFile> { File(Quality.CBZ) }, new List<BookFile>());

            _sent.Message.Should().Be("Marriagetoxin - Marriagetoxin Vol. 9 (1 Files Imported)");
        }

        [Test]
        public void a_normal_upgrade_message_is_unchanged()
        {
            Import(new List<BookFile> { File(Quality.CBZ) }, new List<BookFile> { File(Quality.PDF) });

            _sent.Message.Should().Be("Marriagetoxin - Marriagetoxin Vol. 9 (1 Files Imported)");
        }

        [Test]
        public void a_chapter_built_file_with_no_chapter_list_keeps_the_normal_message()
        {
            _edition.Chapters = null;

            Import(new List<BookFile> { File(Quality.CbzChapters) }, new List<BookFile>());

            _sent.Message.Should().Be("Marriagetoxin - Marriagetoxin Vol. 9 (1 Files Imported)");
        }
    }
}
