using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    public class BookSearchDefinitionFixture : CoreTest<BookSearchCriteria>
    {
        [TestCase("Mötley Crüe", "Motley+Crue")]
        [TestCase("방탄소년단", "방탄소년단")]
        public void should_replace_some_special_characters_author(string author, string expected)
        {
            Subject.Author = new Author { Name = author };
            Subject.AuthorQuery.Should().Be(expected);
        }

        [TestCase("…and Justice for All", "and+Justice+for+All")]
        [TestCase("American III: Solitary Man", "American+III")]
        [TestCase("Sad Clowns & Hillbillies", "Sad+Clowns+Hillbillies")]
        [TestCase("¿Quién sabe?", "Quien+sabe")]
        [TestCase("Seal the Deal & Let’s Boogie", "Seal+the+Deal+Let’s+Boogie")]
        [TestCase("Section.80", "Section+80")]
        public void should_replace_some_special_characters(string book, string expected)
        {
            Subject.Author = new Author { Name = "Author" };
            Subject.BookTitle = book;
            Subject.BookQuery.Should().Be(expected);
        }

        [TestCase("+", "+")]
        public void should_not_replace_some_special_characters_if_result_empty_string(string book, string expected)
        {
            Subject.Author = new Author { Name = "Author" };
            Subject.BookTitle = book;
            Subject.BookQuery.Should().Be(expected);
        }

        // Manga fork: when VolumeNumber > 0 the query is "<Series>+v<NN>" (the Torznab form).
        [TestCase("Chainsaw Man", 13, "Chainsaw+Man+v13")]
        [TestCase("Chainsaw Man", 1, "Chainsaw+Man+v01")]
        [TestCase("Spy x Family", 5, "Spy+x+Family+v05")]
        [TestCase("One Piece", 103, "One+Piece+v103")]
        public void should_build_manga_volume_query_when_volume_set(string series, int volume, string expected)
        {
            Subject.Author = new Author { Name = series };
            Subject.BookTitle = $"{series} Vol. {volume}";
            Subject.VolumeNumber = volume;
            Subject.BookQuery.Should().Be(expected);
        }

        [Test]
        public void should_keep_legacy_book_query_when_volume_is_zero()
        {
            // VolumeNumber == 0 (non-manga) must be byte-identical to the original behavior.
            Subject.Author = new Author { Name = "Author" };
            Subject.BookTitle = "Some Book Title";
            Subject.VolumeNumber = 0;
            Subject.BookQuery.Should().Be("Some+Book+Title");
        }

        // The fielded "title=" search must NOT carry the volume for manga (the indexer's book title
        // doesn't contain it) — search by series and let the q= tier carry the volume.
        [TestCase("Chainsaw Man", 13, "Chainsaw+Man")]
        [TestCase("Spy x Family", 5, "Spy+x+Family")]
        public void fielded_title_query_is_series_only_for_manga(string series, int volume, string expected)
        {
            Subject.Author = new Author { Name = series };
            Subject.BookTitle = $"{series} Vol. {volume}";
            Subject.VolumeNumber = volume;
            Subject.FieldedTitleQuery.Should().Be(expected);
        }

        [Test]
        public void fielded_title_query_is_book_title_for_non_manga()
        {
            Subject.Author = new Author { Name = "Author" };
            Subject.BookTitle = "Some Book Title";
            Subject.VolumeNumber = 0;
            Subject.FieldedTitleQuery.Should().Be("Some+Book+Title");
        }

        [Test]
        public void book_query_alt_adds_unpadded_variant_for_single_digit_volume()
        {
            Subject.Author = new Author { Name = "Spy x Family" };
            Subject.BookTitle = "Spy x Family Vol. 5";
            Subject.VolumeNumber = 5;
            Subject.BookQuery.Should().Be("Spy+x+Family+v05");
            Subject.BookQueryAlt.Should().Be("Spy+x+Family+v5");
        }

        [Test]
        public void book_query_alt_is_null_when_it_would_duplicate_or_non_manga()
        {
            Subject.Author = new Author { Name = "One Piece" };
            Subject.BookTitle = "One Piece Vol. 103";
            Subject.VolumeNumber = 103;
            Subject.BookQueryAlt.Should().BeNull();

            Subject.VolumeNumber = 0;
            Subject.BookQueryAlt.Should().BeNull();
        }

        [Test]
        public void fractional_volume_query()
        {
            Subject.Author = new Author { Name = "Berserk" };
            Subject.BookTitle = "Berserk Vol. 3.5";
            Subject.VolumeNumber = 3.5;
            Subject.BookQuery.Should().Be("Berserk+v3.5");
            Subject.BookQueryAlt.Should().BeNull();
        }
    }
}
