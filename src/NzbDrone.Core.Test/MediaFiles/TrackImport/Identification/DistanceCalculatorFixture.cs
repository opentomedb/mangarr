using System.Collections.Generic;
using NzbDrone.Core.Books;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class DistanceCalculatorFixture : TestBase
    {
        private static LocalBook File(string path, string bookTitle = null)
        {
            return new LocalBook { Path = path, FileTrackInfo = new ParsedTrackInfo { BookTitle = bookTitle } };
        }

        [TestCase("/dl/pack/Classroom of the Elite - Volume 11.5 [TLG].epub", 11.5)]
        [TestCase("/dl/pack/Classroom of the Elite - Volume 11 [Confused].epub", 11)]
        [TestCase("/abs/Classroom of the Elite - Vol. 10/Classroom of the Elite - Vol 010.m4b", 10)]
        [TestCase("/books/Some Novel.epub", 0)]
        public void file_volume_comes_from_the_file_name(string path, double expected)
        {
            DistanceCalculator.FileVolume(new List<LocalBook> { File(path) }).Should().Be(expected);
        }

        private static Edition VolumeEdition(double volume)
        {
            var book = new Book
            {
                Title = "Classroom of the Elite Vol. " + volume,
                VolumeNumber = volume,
                AuthorMetadata = new AuthorMetadata { Name = "Classroom of the Elite" },
                SeriesLinks = new List<SeriesBookLink>()
            };

            return new Edition { Title = book.Title, Book = book, MediaType = MediaType.Ebook };
        }

        [Test]
        public void a_side_volume_file_is_far_from_the_whole_volume_and_near_its_own()
        {
            var side = new List<LocalBook> { File("/dl/pack/Classroom of the Elite - Volume 11.5 [TLG].epub", "Classroom of the Elite - Volume 11.5") };
            side[0].FileTrackInfo.Authors = new List<string> { "Classroom of the Elite" };

            var toEleven = DistanceCalculator.BookDistance(side, VolumeEdition(11)).NormalizedDistance();
            var toElevenHalf = DistanceCalculator.BookDistance(side, VolumeEdition(11.5)).NormalizedDistance();

            toEleven.Should().BeGreaterThan(0.15, "a different volume number must fail the 0.15 match threshold");
            toElevenHalf.Should().BeLessThan(toEleven);
        }

        [Test]
        public void a_file_without_a_volume_token_is_not_penalised()
        {
            var plain = new List<LocalBook> { File("/books/Some Novel.epub", "Some Novel") };
            plain[0].FileTrackInfo.Authors = new List<string> { "Classroom of the Elite" };

            var d = DistanceCalculator.BookDistance(plain, VolumeEdition(3));

            d.Penalties.Should().NotContainKey("volume");
        }

        [Test]
        public void an_audio_part_number_is_not_read_as_a_volume()
        {
            var part = new List<LocalBook> { File("/dl/[PZG].Classroom.of.the.Elite,.Vol..02/Classroom.of.the.Elite,.Vol..3.02.mp4", "Classroom of the Elite, Vol. 02") };
            part[0].FileTrackInfo.Authors = new List<string> { "Classroom of the Elite" };

            var d = DistanceCalculator.BookDistance(part, VolumeEdition(2));

            d.Penalties.Should().NotContainKey("volume");
        }

        [Test]
        public void file_volume_falls_back_to_the_title_tag()
        {
            DistanceCalculator.FileVolume(new List<LocalBook> { File("/dl/abc123.epub", "Classroom of the Elite Vol. 7") }).Should().Be(7);
        }

        [Test]
        public void should_reverse_single_reversed_author()
        {
            var input = new List<string> { "Last, First" };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().Contain("First Last");
        }

        [Test]
        public void should_reverse_two_reversed_author()
        {
            var input = new List<string>
            {
                "Last, First",
                "Last2, First2"
            };

            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().HaveCount(4);
            authors.Should().Contain("First Last");
            authors.Should().Contain("First2 Last2");
            authors.Should().Contain("Last, First");
            authors.Should().Contain("Last2, First2");
        }

        [Test]
        public void should_not_reverse_single_author()
        {
            var input = new List<string> { "First Last" };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().HaveCount(1);
            authors.Should().Contain("First Last");
        }

        [TestCase("First1 Last1, First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1; First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1 & First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1 / First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1 and First2 Last2", "First1 Last1", "First2 Last2")]
        public void should_split_concatenated_author(string inputString, string first, string second)
        {
            var input = new List<string> { inputString };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().Contain(inputString);
            authors.Should().Contain(first);
            authors.Should().Contain(second);
            authors.Should().HaveCount(3);
        }

        [Test]
        public void should_split_concatenated_with_trailing_and()
        {
            var inputString = "First Last, First2 Last2 & First3 Last3";
            var input = new List<string> { inputString };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().Contain(inputString);
            authors.Should().Contain("First Last");
            authors.Should().Contain("First2 Last2");
            authors.Should().Contain("First3 Last3");
            authors.Should().HaveCount(4);
        }

        [Test]
        public void should_not_split_if_multiple_input()
        {
            var input = new List<string>
            {
                "First Last",
                "Second Third, Fourth Fifth"
            };

            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().HaveCount(2);
            authors.Should().Contain("First Last");
            authors.Should().Contain("Second Third, Fourth Fifth");
        }
    }
}
