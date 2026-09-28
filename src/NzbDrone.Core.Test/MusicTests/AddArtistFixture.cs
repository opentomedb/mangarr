using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AddAuthorFixture : CoreTest<AddAuthorService>
    {
        private Author _fakeAuthor;

        [SetUp]
        public void Setup()
        {
            _fakeAuthor = Builder<Author>
                .CreateNew()
                .With(s => s.Path = null)
                .Build();
            _fakeAuthor.Books = new List<Book>();

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()))
                .Returns<Author, bool>((author, _) => author);
        }

        private void GivenValidAuthor(string readarrId)
        {
            Mocker.GetMock<IProvideAuthorInfo>()
                .Setup(s => s.GetAuthorInfo(readarrId, false, It.IsAny<bool>()))
                .Returns(_fakeAuthor);
        }

        private void GivenValidPath()
        {
            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null))
                  .Returns<Author, NamingConfig>((c, n) => c.Name);

            Mocker.GetMock<IAddAuthorValidator>()
                  .Setup(s => s.Validate(It.IsAny<Author>()))
                  .Returns(new ValidationResult());
        }

        [Test]
        public void should_be_able_to_add_a_author_without_passing_in_name()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                RootFolderPath = @"C:\Test\Music"
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            var author = Subject.AddAuthor(newAuthor);

            author.Name.Should().Be(_fakeAuthor.Name);
        }

        [Test]
        public void should_have_proper_path()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                RootFolderPath = @"C:\Test\Music"
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            var author = Subject.AddAuthor(newAuthor);

            author.Path.Should().Be(Path.Combine(newAuthor.RootFolderPath, _fakeAuthor.Name));
        }

        [Test]
        public void should_throw_if_author_validation_fails()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1"
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);

            Mocker.GetMock<IAddAuthorValidator>()
                  .Setup(s => s.Validate(It.IsAny<Author>()))
                  .Returns(new ValidationResult(new List<ValidationFailure>
                                                {
                                                    new ValidationFailure("Path", "Test validation failure")
                                                }));

            Assert.Throws<ValidationException>(() => Subject.AddAuthor(newAuthor));
        }

        [Test]
        public void should_throw_if_author_cannot_be_found()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1"
            };

            Mocker.GetMock<IProvideAuthorInfo>()
                  .Setup(s => s.GetAuthorInfo(newAuthor.ForeignAuthorId, false, It.IsAny<bool>()))
                  .Throws(new AuthorNotFoundException(newAuthor.ForeignAuthorId));

            Mocker.GetMock<IAddAuthorValidator>()
                  .Setup(s => s.Validate(It.IsAny<Author>()))
                  .Returns(new ValidationResult(new List<ValidationFailure>
                                                {
                                                    new ValidationFailure("Path", "Test validation failure")
                                                }));

            Assert.Throws<ValidationException>(() => Subject.AddAuthor(newAuthor));

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_throw_if_series_already_exists_by_name()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                RootFolderPath = @"C:\Test\Music"
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            var existing = Builder<Author>.CreateNew().Build();
            existing.Metadata = Builder<AuthorMetadata>.CreateNew()
                .With(m => m.Name = "Existing Series")
                .With(m => m.ForeignAuthorId = "local-existing")
                .Build();

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.FindByName(It.IsAny<string>(), It.IsAny<LibraryType>()))
                .Returns(existing);

            Assert.Throws<ValidationException>(() => Subject.AddAuthor(newAuthor));

            Mocker.GetMock<IAuthorService>()
                .Verify(s => s.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_disambiguate_if_author_folder_exists()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1",
            };

            _fakeAuthor.Metadata = Builder<AuthorMetadata>.CreateNew().With(x => x.Disambiguation = "Disambiguation").Build();

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path))
                .Returns(true);

            var author = Subject.AddAuthor(newAuthor);
            author.Path.Should().Be(newAuthor.Path + " (Disambiguation)");
        }

        [Test]
        public void should_disambiguate_with_numbers_if_author_folder_still_exists()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1",
            };

            _fakeAuthor.Metadata = Builder<AuthorMetadata>.CreateNew().With(x => x.Disambiguation = "Disambiguation").Build();

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (Disambiguation)"))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (Disambiguation) (1)"))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (Disambiguation) (2)"))
                .Returns(true);

            var author = Subject.AddAuthor(newAuthor);
            author.Path.Should().Be(newAuthor.Path + " (Disambiguation) (3)");
        }

        [Test]
        public void should_disambiguate_with_numbers_if_author_folder_exists_and_no_disambiguation()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                Path = @"C:\Test\Music\Name1",
            };

            _fakeAuthor.Metadata = Builder<AuthorMetadata>.CreateNew().With(x => x.Disambiguation = string.Empty).Build();

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (1)"))
                .Returns(true);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.AuthorPathExists(newAuthor.Path + " (2)"))
                .Returns(true);

            var author = Subject.AddAuthor(newAuthor);
            author.Path.Should().Be(newAuthor.Path + " (3)");
        }

        [Test]
        public void should_refuse_a_light_novel_the_catalogue_does_not_have()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "local-mushoku-tensei~ln",
                RootFolderPath = @"C:\Test\LightNovels"
            };

            Mocker.GetMock<IProvideAuthorInfo>()
                  .Setup(s => s.GetAuthorInfo(newAuthor.ForeignAuthorId, false, It.IsAny<bool>()))
                  .Throws(new NotInCatalogueException("Mushoku Tensei"));

            var ex = Assert.Throws<ValidationException>(() => Subject.AddAuthor(newAuthor));

            ex.Errors.Should().Contain(e => e.ErrorMessage == "Not in the catalogue as a light novel yet");
            Mocker.GetMock<IAuthorService>()
                .Verify(s => s.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_allow_a_light_novel_next_to_the_manga_of_the_same_name()
        {
            _fakeAuthor.Metadata = Builder<AuthorMetadata>.CreateNew()
                .With(m => m.Name = "Sword Art Online")
                .With(m => m.ForeignAuthorId = "local-sword-art-online~ln")
                .Build();

            var manga = Builder<Author>.CreateNew().Build();
            manga.Metadata = Builder<AuthorMetadata>.CreateNew()
                .With(m => m.Name = "Sword Art Online")
                .With(m => m.ForeignAuthorId = "local-sword-art-online")
                .Build();

            var newAuthor = new Author
            {
                ForeignAuthorId = "local-sword-art-online~ln",
                RootFolderPath = @"C:\Test\LightNovels"
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            Mocker.GetMock<IAuthorService>().Setup(s => s.FindByName("Sword Art Online", LibraryType.Manga)).Returns(manga);
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindByName("Sword Art Online", LibraryType.LightNovel)).Returns((Author)null);

            var added = Subject.AddAuthor(newAuthor);

            added.CleanName.Should().Be("swordartonline~ln");
            Mocker.GetMock<IAuthorService>()
                .Verify(s => s.FindByName(It.IsAny<string>(), LibraryType.Manga), Times.Never());
            Mocker.GetMock<IAuthorService>()
                .Verify(s => s.AddAuthor(It.Is<Author>(a => a.ForeignAuthorId == "local-sword-art-online~ln"), It.IsAny<bool>()), Times.Once());
        }
    
        [Test]
        public void an_added_series_carries_its_root_folders_default_tags()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                RootFolderPath = @"C:\Test\Music".AsOsAgnostic(),
                Tags = new HashSet<int> { 7 }
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.GetBestRootFolder(It.IsAny<string>()))
                  .Returns(new RootFolder { Path = newAuthor.RootFolderPath, DefaultTags = new HashSet<int> { 1 } });

            var author = Subject.AddAuthor(newAuthor);

            author.Tags.Should().BeEquivalentTo(new[] { 1, 7 });
        }

        [Test]
        public void a_root_folder_without_default_tags_leaves_the_tags_alone()
        {
            var newAuthor = new Author
            {
                ForeignAuthorId = "ce09ea31-3d4a-4487-a797-e315175457a0",
                RootFolderPath = @"C:\Test\Music".AsOsAgnostic(),
                Tags = new HashSet<int> { 7 }
            };

            GivenValidAuthor(newAuthor.ForeignAuthorId);
            GivenValidPath();

            var author = Subject.AddAuthor(newAuthor);

            author.Tags.Should().BeEquivalentTo(new[] { 7 });
        }
    }
}
