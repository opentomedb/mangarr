using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    // Preferred Edition (2026-09-24, M9 fix round 1 ⚠1): the grab path parses a release generically first
    // (DownloadDecisionMaker: Parser.ParseBookTitle, then Map). "L'Attaque des Titans - Tome 5 [FR]" parses as
    // author "L'Attaque des Titans", book "Tome 5", no volume -- so Map must read the edition's own token for a
    // series of that edition, and reject a numbered collected release, before any title lookup.
    [TestFixture]
    public class EditionVolumeMapFixture : CoreTest<ParsingService>
    {
        private static Author Series(int id, string name, string edition, string anchorName, string volumeLabel)
        {
            var author = new Author
            {
                Id = id,
                AuthorMetadataId = id,
                CleanName = name.CleanAuthorName(),
                Metadata = new AuthorMetadata { Id = id, Name = name, EditionLanguage = edition, AnchorName = anchorName, ForeignAuthorId = "local-attack-on-titan" }
            };

            author.Books = Enumerable.Range(1, 5)
                .Select(n => new Book { Id = (id * 100) + n, Title = $"{name} {volumeLabel} {n}", CleanTitle = $"{name} {volumeLabel} {n}".CleanAuthorName(), VolumeNumber = n, AuthorMetadataId = id })
                .ToList();

            return author;
        }

        private Author _french;
        private Author _english;

        [SetUp]
        public void Setup()
        {
            _french = Series(1, "L'Attaque des Titans", "fr", "Attack on Titan", "Tome");
            _english = Series(2, "Attack on Titan", null, null, "Vol.");

            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(1)).Returns(_french.Books.Value);
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(2)).Returns(_english.Books.Value);
        }

        private RemoteBookResult Map(string title, Author author)
        {
            var parsed = Parser.Parser.ParseBookTitle(title);
            parsed.Should().NotBeNull();

            var criteria = new BookSearchCriteria { Author = author, Books = author.Books.Value };
            var remote = Subject.Map(parsed, criteria);

            return new RemoteBookResult { Parsed = parsed, Books = remote.Books, Author = remote.Author };
        }

        private class RemoteBookResult
        {
            public Parser.Model.ParsedBookInfo Parsed { get; set; }
            public List<Book> Books { get; set; }
            public Author Author { get; set; }
        }

        [TestCase("L'Attaque des Titans - Tome 5 [FR]")]
        [TestCase("L'Attaque des Titans - T05 [FR]")]
        public void a_french_single_volume_maps_to_that_volume(string title)
        {
            var result = Map(title, _french);

            result.Author.Should().BeSameAs(_french);
            result.Books.Select(b => b.VolumeNumber).Should().Equal(5);
        }

        [Test]
        public void a_french_pack_maps_to_its_volumes()
        {
            var result = Map("L'Attaque des Titans - Tome 1 à Tome 3 [FR]", _french);

            result.Books.Select(b => b.VolumeNumber).Should().Equal(1, 2, 3);
        }

        [TestCase("L'Attaque des Titans - Tome 1 à 3 (Coffret)")]
        [TestCase("L'Attaque des Titans - Tome 4 (Intégrale)")]
        [TestCase("L'Attaque des Titans - Vol. 2 (Intégrale)")]
        public void a_numbered_collected_release_maps_to_no_volume(string title)
        {
            // Polish (2026-09-24): the production hazard -- the title lookups below the volume mapping would
            // answer with SOME volume ("Tome 4" inexact-matches a stored title). The reject must stop the release
            // before they run, so that book is never used.
            var hazard = _french.Books.Value[2];
            Mocker.GetMock<IBookService>().Setup(s => s.FindByTitle(It.IsAny<int>(), It.IsAny<string>())).Returns(hazard);
            Mocker.GetMock<IBookService>().Setup(s => s.FindByTitleInexact(It.IsAny<int>(), It.IsAny<string>())).Returns(hazard);
            Mocker.GetMock<IEditionService>().Setup(s => s.FindByTitle(It.IsAny<int>(), It.IsAny<string>())).Returns(new Edition { Book = hazard });
            Mocker.GetMock<IEditionService>().Setup(s => s.FindByTitleInexact(It.IsAny<int>(), It.IsAny<string>())).Returns(new Edition { Book = hazard });

            var books = Map(title, _french).Books;

            books.Should().NotContain(hazard);
            books.Should().BeEmpty();
            Mocker.GetMock<IBookService>().Verify(s => s.FindByTitleInexact(It.IsAny<int>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_series_bound_to_a_collected_line_maps_its_own_release()
        {
            _french.Metadata.Value.Aliases = new List<string> { "L'Attaque des Titans Intégrale" };

            Map("L'Attaque des Titans - Tome 4 (Intégrale)", _french).Books.Select(b => b.VolumeNumber).Should().Equal(4);
        }

        [Test]
        public void a_series_whose_bound_line_is_collected_maps_its_own_release()
        {
            _french.Metadata.Value.EditionCollected = true;

            Map("L'Attaque des Titans - Tome 4 (Intégrale)", _french).Books.Select(b => b.VolumeNumber).Should().Equal(4);
        }

        // An English series is never re-read: the generic parse maps exactly as before (no volume, the title
        // lookups run with the book "Tome 5" and find nothing).
        [Test]
        public void an_english_series_maps_the_same_release_as_today()
        {
            var result = Map("Attack on Titan - Tome 5 [FR]", _english);

            result.Author.Should().BeSameAs(_english);
            result.Parsed.VolumeNumber.Should().BeNull();
            result.Parsed.VolumeStart.Should().BeNull();
            result.Books.Should().BeEmpty();
            Mocker.GetMock<IBookService>().Verify(s => s.FindByTitle(2, result.Parsed.BookTitle), Times.Once());
            Mocker.GetMock<IBookService>().Verify(s => s.GetBooksByAuthor(2), Times.Never());
        }
    }
}
