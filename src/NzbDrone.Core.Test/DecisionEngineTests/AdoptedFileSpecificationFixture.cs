using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    // One copy each (2026-09-20): a release for an edition whose file Mangarr merely adopted is
    // never grabbed -- the original is managed outside Mangarr. Only the edition of the release's
    // media type counts: an adopted audiobook does not block an EPUB grab.
    [TestFixture]
    public class AdoptedFileSpecificationFixture : CoreTest<AdoptedFileSpecification>
    {
        private RemoteBook _remoteBook;
        private Edition _ebook;
        private Edition _audio;

        [SetUp]
        public void Setup()
        {
            _ebook = new Edition { Id = 11, MediaType = MediaType.Ebook, BookFiles = new List<BookFile>() };
            _audio = new Edition { Id = 12, MediaType = MediaType.Audio, BookFiles = new List<BookFile>() };

            _remoteBook = new RemoteBook
            {
                Author = new Author { Id = 3, Name = "KonoSuba" },
                Release = new ReleaseInfo { Title = "KonoSuba Vol. 1 [EPUB]" },
                MediaType = MediaType.Ebook,
                Books = new List<Book>
                {
                    new Book { Id = 5, Editions = new List<Edition> { _ebook, _audio } }
                }
            };
        }

        [Test]
        public void accepts_when_the_edition_has_no_adopted_file()
        {
            _ebook.BookFiles.Value.Add(new BookFile { Id = 1, EditionId = 11, Adopted = false });

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void rejects_when_the_edition_has_an_adopted_file()
        {
            _ebook.BookFiles.Value.Add(new BookFile { Id = 1, EditionId = 11, Adopted = true });

            var decision = Subject.IsSatisfiedBy(_remoteBook, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Adopted original — manage it outside Mangarr");
        }

        [Test]
        public void accepts_when_only_the_other_media_types_edition_is_adopted()
        {
            _audio.BookFiles.Value.Add(new BookFile { Id = 2, EditionId = 12, Adopted = true });

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void accepts_when_the_book_has_no_edition_of_that_media_type()
        {
            _remoteBook.MediaType = MediaType.Archive;
            _ebook.BookFiles.Value.Add(new BookFile { Id = 1, EditionId = 11, Adopted = true });

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }
    }
}
