using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    // One copy each (2026-09-20): a file is never imported over an edition whose file Mangarr
    // merely adopted -- the original is managed outside Mangarr.
    [TestFixture]
    public class AdoptedEditionSpecificationFixture : CoreTest<AdoptedEditionSpecification>
    {
        private LocalBook _localBook;

        [SetUp]
        public void Setup()
        {
            _localBook = new LocalBook
            {
                Path = "/downloads/x/KonoSuba - Vol 001.epub",
                Author = new Author { Metadata = new AuthorMetadata { Name = "KonoSuba", ForeignAuthorId = "local-konosuba~ln" } },
                Edition = new Edition { Id = 11, MediaType = MediaType.Ebook, BookFiles = new List<BookFile>() }
            };
        }

        [Test]
        public void accepts_when_the_edition_has_no_files()
        {
            Subject.IsSatisfiedBy(_localBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void accepts_when_the_editions_files_are_not_adopted()
        {
            _localBook.Edition.BookFiles.Value.Add(new BookFile { Id = 1, EditionId = 11, Adopted = false });

            Subject.IsSatisfiedBy(_localBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void rejects_when_the_edition_has_an_adopted_file()
        {
            _localBook.Edition.BookFiles.Value.Add(new BookFile { Id = 1, EditionId = 11, Adopted = true });

            var decision = Subject.IsSatisfiedBy(_localBook, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Adopted original — manage it outside Mangarr");
        }

        [Test]
        public void accepts_when_no_edition_is_known_yet()
        {
            _localBook.Edition = null;

            Subject.IsSatisfiedBy(_localBook, null).Accepted.Should().BeTrue();
        }
    }
}
