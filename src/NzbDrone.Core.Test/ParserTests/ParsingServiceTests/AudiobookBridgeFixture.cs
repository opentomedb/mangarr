using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    // Light-novel audio (2026-09-18, D4): the audiobook title bridge lives in the criteria parse
    // (ParseBookTitleWithSearchCriteria with the searched media type) and its result goes through
    // ParsingService.Map like any volume-tokened release -- the searched series attributed by name,
    // the volume mapped by number. An EPUB search and a manga author get what they got before.
    [TestFixture]
    public class AudiobookBridgeFixture : CoreTest<ParsingService>
    {
        private Author _sao;
        private Author _manga;
        private List<Book> _volumes;

        [SetUp]
        public void Setup()
        {
            _sao = new Author
            {
                Id = 1,
                CleanName = "swordartonline~ln",
                AuthorMetadataId = 1,
                Metadata = new AuthorMetadata { Name = "Sword Art Online", ForeignAuthorId = "local-sword-art-online~ln", Aliases = new List<string> { "SAO" } }
            };

            _manga = new Author
            {
                Id = 2,
                CleanName = "swordartonline",
                AuthorMetadataId = 2,
                Metadata = new AuthorMetadata { Name = "Sword Art Online", ForeignAuthorId = "local-sword-art-online", Aliases = new List<string>() }
            };

            _volumes = new List<Book>
            {
                Volume(20, "Moon Cradle", "Sword Art Online 20: Moon Cradle"),
                Volume(21, "Unital Ring I", "Sword Art Online 21: Unital Ring I"),
                Volume(22, "Unital Ring II", "Sword Art Online 22: Unital Ring II")
            };

            // The live shape: an entry's CleanName carries the library suffix, so GetAuthor resolves
            // the bridge's AuthorName by a per-library name lookup, not by the criteria shortcut.
            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.FindByName("Sword Art Online", LibraryType.LightNovel))
                .Returns(_sao);

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBooksByAuthor(_sao.Id))
                .Returns(_volumes);
        }

        private static Book Volume(double number, string subtitle, string audiobookTitle)
        {
            var book = new Book
            {
                Id = (int)number,
                Title = $"Sword Art Online Vol. {number}",
                ForeignBookId = $"local-sword-art-online~ln-v{number}",
                VolumeNumber = number,
                Subtitle = subtitle,
                AuthorMetadataId = 1
            };

            book.Editions = new List<Edition>
            {
                new Edition { BookId = book.Id, MediaType = MediaType.Ebook, Title = book.Title, Monitored = true },
                new Edition { BookId = book.Id, MediaType = MediaType.Audio, Title = book.Title, AudiobookTitle = audiobookTitle, Monitored = true }
            };

            return book;
        }

        private AuthorSearchCriteria Criteria(Author author, MediaType mediaType)
        {
            return new AuthorSearchCriteria { Author = author, Books = _volumes, MediaType = mediaType };
        }

        [TestCase("Unital Ring I (Sword Art Online 21) [M4B]")]
        [TestCase("Sword Art Online 21 - Unital Ring I by Reki Kawahara [ENG / M4B]")]
        public void should_bridge_an_audible_named_release_to_the_searched_volume(string title)
        {
            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria(title, _sao, _volumes, MediaType.Audio);

            parsed.Should().NotBeNull();
            parsed.AuthorName.Should().Be("Sword Art Online");
            parsed.VolumeNumber.Should().Be(21);
            parsed.ReleaseTitle.Should().Be(title);

            var remote = Subject.Map(parsed, Criteria(_sao, MediaType.Audio));

            remote.Author.Should().BeSameAs(_sao);
            remote.Books.Should().HaveCount(1);
            remote.Books.Single().VolumeNumber.Should().Be(21);
            remote.MediaType.Should().Be(MediaType.Audio);
        }

        [Test]
        public void should_bridge_the_next_volume_whose_subtitle_extends_the_first()
        {
            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria("Unital Ring II (Sword Art Online 22) [M4B]", _sao, _volumes, MediaType.Audio);

            parsed.Should().NotBeNull();
            parsed.VolumeNumber.Should().Be(22);

            Subject.Map(parsed, Criteria(_sao, MediaType.Audio)).Books.Single().VolumeNumber.Should().Be(22);
        }

        [Test]
        public void volume_tokened_release_still_takes_the_volume_bridge()
        {
            // The manga search bridge runs first and sets no ReleaseTitle; the audiobook bridge never sees it.
            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria("Sword Art Online Vol. 21 - Unital Ring I [M4B]", _sao, _volumes, MediaType.Audio);

            parsed.Should().NotBeNull();
            parsed.VolumeNumber.Should().Be(21);
            parsed.ReleaseTitle.Should().BeNull();

            Subject.Map(parsed, Criteria(_sao, MediaType.Audio)).Books.Single().VolumeNumber.Should().Be(21);
        }

        [TestCase("Unital Ring I (Sword Art Online 21) [M4B]")]
        [TestCase("Sword Art Online 21 - Unital Ring I by Reki Kawahara [ENG / M4B]")]
        public void manga_author_gets_the_result_it_got_before(string title)
        {
            // D7: the same call for a manga entry is byte-identical to the call without a media type.
            var before = Parser.Parser.ParseBookTitleWithSearchCriteria(title, _manga, _volumes);
            var withAudio = Parser.Parser.ParseBookTitleWithSearchCriteria(title, _manga, _volumes, MediaType.Audio);

            AssertUnchanged(before, withAudio);
        }

        // T2 (2026-09-20): an EPUB search of a light novel now has a bridge of its own
        // (EbookReleaseMatcher), so these titles no longer die as Unknown Author -- they name
        // volume 21 by the same subtitle key. The media-type invariant this case was written for
        // still holds: an EPUB search and an untyped one get the SAME result, and only an Audio
        // search takes the audiobook bridge. (The former "VolumeNumber is null" assertion was the
        // pre-T2 behaviour of the ebook path, not the invariant.)
        [TestCase("Unital Ring I (Sword Art Online 21) [EPUB]")]
        [TestCase("Sword Art Online 21 - Unital Ring I by Reki Kawahara [ENG / EPUB]")]
        public void ebook_search_and_untyped_search_agree(string title)
        {
            var before = Parser.Parser.ParseBookTitleWithSearchCriteria(title, _sao, _volumes);
            var withEbook = Parser.Parser.ParseBookTitleWithSearchCriteria(title, _sao, _volumes, MediaType.Ebook);

            withEbook.Should().BeEquivalentTo(before);
            withEbook.VolumeNumber.Should().Be(21);
            withEbook.ReleaseTitle.Should().Be(title);
        }

        [Test]
        public void should_not_bridge_when_two_searched_volumes_share_the_key()
        {
            _volumes[0].EditionOf(MediaType.Audio).AudiobookTitle = "Same Product";
            _volumes[1].EditionOf(MediaType.Audio).AudiobookTitle = "Same Product";

            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria("Same Product [M4B]", _sao, _volumes, MediaType.Audio);

            (parsed?.VolumeNumber).Should().BeNull();
            (parsed?.ReleaseTitle).Should().BeNull();
        }

        private static void AssertUnchanged(ParsedBookInfo before, ParsedBookInfo after)
        {
            if (before == null)
            {
                after.Should().BeNull();
                return;
            }

            after.Should().BeEquivalentTo(before);
            after.VolumeNumber.Should().BeNull();
            after.ReleaseTitle.Should().BeNull();
        }
    }
}
