using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles
{
    public class RenameTrackFileServiceFixture : CoreTest<RenameBookFileService>
    {
        private Author _author;
        private List<BookFile> _trackFiles;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateNew()
                                     .Build();

            _trackFiles = Builder<BookFile>.CreateListOfSize(2)
                                                .All()
                                                .With(e => e.Author = _author)
                                                .With(e => e.CalibreId = 0)
                                                .With(e => e.Home = FileHome.Entry)
                                                .With(e => e.Adopted = false)
                                                .Build()
                                                .ToList();

            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.GetAuthor(_author.Id))
                  .Returns(_author);

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByAuthor(_author.Id))
                .Returns(_trackFiles);
        }

        private void GivenNoTrackFiles()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.Get(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<BookFile>());
        }

        private void GivenTrackFiles()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.Get(It.IsAny<IEnumerable<int>>()))
                  .Returns(_trackFiles);
        }

        private void GivenMovedFiles()
        {
            Mocker.GetMock<IMoveBookFiles>()
                  .Setup(s => s.MoveBookFile(It.IsAny<BookFile>(), _author));
        }

        [Test]
        public void should_not_publish_event_if_no_files_to_rename()
        {
            GivenNoTrackFiles();

            Subject.Execute(new RenameFilesCommand(_author.Id, new List<int> { 1 }));

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<AuthorRenamedEvent>()), Times.Never());
        }

        [Test]
        public void should_not_publish_event_if_no_files_are_renamed()
        {
            GivenTrackFiles();

            Mocker.GetMock<IMoveBookFiles>()
                  .Setup(s => s.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<Author>()))
                  .Throws(new SameFilenameException("Same file name", "Filename"));

            Subject.Execute(new RenameFilesCommand(_author.Id, new List<int> { 1 }));

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<AuthorRenamedEvent>()), Times.Never());
        }

        [Test]
        public void should_publish_event_if_files_are_renamed()
        {
            GivenTrackFiles();
            GivenMovedFiles();

            Subject.Execute(new RenameFilesCommand(_author.Id, new List<int> { 1 }));

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<AuthorRenamedEvent>()), Times.Once());
        }

        [Test]
        public void should_update_moved_files()
        {
            GivenTrackFiles();
            GivenMovedFiles();

            Subject.Execute(new RenameFilesCommand(_author.Id, new List<int> { 1 }));

            Mocker.GetMock<IMediaFileService>()
                  .Verify(v => v.Update(It.IsAny<BookFile>()), Times.Exactly(2));
        }

        [Test]
        public void should_get_trackfiles_by_ids_only()
        {
            GivenTrackFiles();
            GivenMovedFiles();

            var files = new List<int> { 1 };

            Subject.Execute(new RenameFilesCommand(_author.Id, files));

            Mocker.GetMock<IMediaFileService>()
                  .Verify(v => v.Get(files), Times.Once());
        }

        // One copy each (2026-09-20): a light novel's off-entry rows (calibre, Audiobookshelf's
        // tree) are never renamed; only Entry rows are previewed or moved.
        private List<BookFile> GivenOffEntryFiles()
        {
            var edition = Builder<Edition>.CreateNew().Build();

            var entry = _trackFiles[0];
            entry.Path = "/lightnovels/Sword Art Online/Sword Art Online - Vol. 1/Sword Art Online - Vol 001.epub";
            entry.Edition = edition;
            entry.EditionId = edition.Id;

            var calibre = new BookFile
            {
                Id = 20,
                Author = _author,
                Edition = edition,
                EditionId = edition.Id,
                Home = FileHome.Calibre,
                CalibreId = 12,
                Path = "/books/Reki Kawahara/Sword Art Online 2 (12)/Sword Art Online 2 - Reki Kawahara.epub"
            };

            var audio = new BookFile
            {
                Id = 21,
                Author = _author,
                Edition = edition,
                EditionId = edition.Id,
                Home = FileHome.Audiobooks,
                Path = "/srv/audiobooks/Sword Art Online/Sword Art Online - Vol. 3/Sword Art Online - Vol 003.m4b"
            };

            var files = new List<BookFile> { entry, calibre, audio };

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByAuthor(_author.Id))
                  .Returns(files);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.Get(It.IsAny<IEnumerable<int>>()))
                  .Returns(files);

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildBookFileName(It.IsAny<Author>(), It.IsAny<Edition>(), It.IsAny<BookFile>(), null, null))
                  .Returns("Renamed");

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildBookFilePath(It.IsAny<Author>(), It.IsAny<Edition>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FileHome>()))
                  .Returns<Author, Edition, string, string, FileHome>((a, e, name, ext, home) => "/lightnovels/Sword Art Online/" + name + ext);

            return files;
        }

        [Test]
        public void previews_list_only_entry_files()
        {
            var files = GivenOffEntryFiles();

            var previews = Subject.GetRenamePreviews(_author.Id);

            previews.Should().HaveCount(1);
            previews[0].BookFileId.Should().Be(files[0].Id);
            previews[0].ExistingPath.Should().Be(files[0].Path);
        }

        [Test]
        public void rename_moves_only_entry_files()
        {
            var files = GivenOffEntryFiles();
            GivenMovedFiles();

            Subject.Execute(new RenameFilesCommand(_author.Id, files.Select(f => f.Id).ToList()));

            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(files[0], _author), Times.Once());
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<Author>()), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(files[0]), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Update(It.IsAny<BookFile>()), Times.Once());
        }

        [Test]
        public void rename_author_moves_only_entry_files()
        {
            var files = GivenOffEntryFiles();
            GivenMovedFiles();

            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.GetAuthors(It.IsAny<List<int>>()))
                  .Returns(new List<Author> { _author });

            Subject.Execute(new RenameAuthorCommand { AuthorIds = new List<int> { _author.Id } });

            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(files[0], _author), Times.Once());
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<Author>()), Times.Once());
        }
    }
}
