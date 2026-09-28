using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    // Increment 4a: pack fan-out. A release parsed to a volume range (VolumeStart/VolumeEnd)
    // must map to EVERY matching Volume the series has, not 0-or-1. Mirrors the Discography
    // multi-book behaviour. Single-volume and non-manga releases must be unaffected.
    [TestFixture]
    public class GetBooksVolumeFanoutFixture : CoreTest<ParsingService>
    {
        private Author _author;
        private List<Book> _volumes;

        [SetUp]
        public void Setup()
        {
            _author = new Author
            {
                Id = 1,
                Name = "Dandadan",
                AuthorMetadataId = 1
            };

            // A 13-volume series: VolumeNumber 1..13.
            _volumes = Enumerable.Range(1, 13)
                .Select(n => new Book
                {
                    Id = n,
                    Title = $"Dandadan Vol. {n}",
                    VolumeNumber = n,
                    AuthorMetadataId = 1
                })
                .ToList();

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBooksByAuthor(_author.Id))
                .Returns(_volumes);
        }

        private ParsedBookInfo PackInfo(int? start, int? end)
        {
            return new ParsedBookInfo
            {
                BookTitle = "Dandadan",
                AuthorName = "Dandadan",
                Quality = new QualityModel(Quality.CBZ),
                VolumeStart = start,
                VolumeEnd = end
            };
        }

        [Test]
        public void should_fan_out_full_range_to_all_volumes()
        {
            var result = Subject.GetBooks(PackInfo(1, 13), _author, null);

            result.Should().HaveCount(13);
            result.Select(b => b.VolumeNumber).Should().BeEquivalentTo(Enumerable.Range(1, 13));
        }

        [Test]
        public void should_fan_out_sub_range_to_only_those_volumes()
        {
            var result = Subject.GetBooks(PackInfo(3, 7), _author, null);

            result.Should().HaveCount(5);
            result.Select(b => b.VolumeNumber).Should().BeEquivalentTo(new[] { 3, 4, 5, 6, 7 });
        }

        [Test]
        public void should_clamp_range_to_existing_volumes()
        {
            // Pack claims 1-20 but the series only has 13 volumes.
            var result = Subject.GetBooks(PackInfo(1, 20), _author, null);

            result.Should().HaveCount(13);
            result.Max(b => b.VolumeNumber).Should().Be(13);
        }

        [Test]
        public void should_return_empty_when_range_matches_no_volumes()
        {
            var result = Subject.GetBooks(PackInfo(50, 60), _author, null);

            result.Should().BeEmpty();
        }

        [Test]
        public void should_not_fan_out_single_volume_release()
        {
            // A single volume (VolumeNumber set, no range) must NOT hit the pack branch.
            // It resolves to exactly one book.
            var single = new ParsedBookInfo
            {
                BookTitle = "Dandadan Vol. 5",
                AuthorName = "Dandadan",
                Quality = new QualityModel(Quality.CBZ),
                VolumeNumber = 5
            };

            Mocker.GetMock<IBookService>()
                .Setup(s => s.FindByTitle(_author.AuthorMetadataId, "Dandadan Vol. 5"))
                .Returns(_volumes.Single(b => b.VolumeNumber == 5));

            var result = Subject.GetBooks(single, _author, null);

            result.Should().HaveCount(1);
            result.Single().VolumeNumber.Should().Be(5);
        }

        [Test]
        public void should_map_single_volume_by_number_not_title()
        {
            // "Jujutsu Kaisen v25" parses to VolumeNumber 25 with a BookTitle that does NOT
            // textually match the edition title "... Vol. 25"; volume-number mapping must still
            // resolve it. No FindByTitle mock is set up on purpose, proving the mapping is by number.
            var single = new ParsedBookInfo
            {
                BookTitle = "Jujutsu Kaisen v25",
                AuthorName = "Dandadan",
                Quality = new QualityModel(Quality.CBZ),
                VolumeNumber = 9
            };

            var result = Subject.GetBooks(single, _author, null);

            result.Should().HaveCount(1);
            result.Single().VolumeNumber.Should().Be(9);
        }

        [Test]
        public void should_fall_through_when_single_volume_not_in_library()
        {
            // Volume 99 isn't in the 13-volume series; volume-number mapping must not invent a
            // match — it falls through to (here unmocked) title resolution and returns empty.
            var single = new ParsedBookInfo
            {
                BookTitle = "Dandadan Vol. 99",
                AuthorName = "Dandadan",
                Quality = new QualityModel(Quality.CBZ),
                VolumeNumber = 99
            };

            var result = Subject.GetBooks(single, _author, null);

            result.Should().BeEmpty();
        }
    }
}
