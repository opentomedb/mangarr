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
    // Light novels (2026-09): "same size as an existing file" is judged within the edition the
    // file is imported to; the EPUB of a volume never blocks its audiobook.
    [TestFixture]
    public class SameFileSpecificationFixture : CoreTest<SameFileSpecification>
    {
        [Test]
        public void should_accept_an_audio_file_the_size_of_the_volumes_epub()
        {
            var epub = new BookFile { Id = 1, EditionId = 51, Size = 1000 };
            var book = new Book { Id = 5 };
            book.WithEdition(MediaType.Ebook, new List<BookFile> { epub }, id: 51);
            book.WithEdition(MediaType.Audio, id: 52);
            book.BookFiles = new List<BookFile> { epub };

            var localBook = new LocalBook { Path = "/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005.m4b", Size = 1000, Book = book, Edition = book.EditionOf(MediaType.Audio) };

            Subject.IsSatisfiedBy(localBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_file_the_size_of_a_file_of_the_same_edition()
        {
            var audio = new BookFile { Id = 2, EditionId = 52, Size = 1000 };
            var book = new Book { Id = 5 };
            book.WithEdition(MediaType.Ebook, id: 51);
            book.WithEdition(MediaType.Audio, new List<BookFile> { audio }, id: 52);
            book.BookFiles = new List<BookFile> { audio };

            var localBook = new LocalBook { Path = "/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005.m4b", Size = 1000, Book = book, Edition = book.EditionOf(MediaType.Audio) };

            Subject.IsSatisfiedBy(localBook, null).Accepted.Should().BeFalse();
        }
    }
}
