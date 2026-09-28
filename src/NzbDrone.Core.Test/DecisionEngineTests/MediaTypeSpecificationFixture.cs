using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class MediaTypeSpecificationFixture : CoreTest<MediaTypeSpecification>
    {
        private Book _mangaVolume;
        private Book _lightNovelVolume;
        private RemoteBook _remoteBook;

        [SetUp]
        public void Setup()
        {
            _mangaVolume = new Book { Id = 1, Title = "Dandadan Vol. 1" };
            _mangaVolume.WithEdition(MediaType.Archive);

            _lightNovelVolume = new Book { Id = 2, Title = "Overlord Vol. 1" };
            _lightNovelVolume.WithEdition(MediaType.Ebook);
            _lightNovelVolume.WithEdition(MediaType.Audio);

            _remoteBook = new RemoteBook
            {
                Author = Builder<Author>.CreateNew().Build(),
                Release = new ReleaseInfo { Title = "release" },
                Books = new List<Book> { _mangaVolume }
            };
        }

        [Test]
        public void archive_release_for_a_manga_volume_is_accepted()
        {
            _remoteBook.MediaType = MediaType.Archive;

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [TestCase(MediaType.Ebook)]
        [TestCase(MediaType.Audio)]
        public void ebook_or_audio_release_for_a_manga_volume_is_rejected(MediaType mediaType)
        {
            _remoteBook.MediaType = mediaType;

            var decision = Subject.IsSatisfiedBy(_remoteBook, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("No " + mediaType.ToString().ToLowerInvariant() + " edition");
        }

        [TestCase(MediaType.Ebook)]
        [TestCase(MediaType.Audio)]
        public void ebook_and_audio_releases_for_a_light_novel_volume_are_accepted(MediaType mediaType)
        {
            _remoteBook.Books = new List<Book> { _lightNovelVolume };
            _remoteBook.MediaType = mediaType;

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void archive_release_for_a_light_novel_volume_is_rejected()
        {
            _remoteBook.Books = new List<Book> { _lightNovelVolume };
            _remoteBook.MediaType = MediaType.Archive;

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }

        // D1 (2026-09-18), end to end: the manga-named release grades CBZ (volume token), the
        // light-novel re-grade leaves it alone because of the wording, the media type resolves
        // Archive, and this is the refusal that stops it — the volume has EPUB and Audio editions,
        // no archive one. At the 02c5030 base the re-grade turned it into EPUB and it was grabbed.
        // 2026-09-21: an explicitly typed light-novel search takes only its own class.
        [TestCase(MediaType.Audio, MediaType.Ebook, "Search asked for an audiobook, release is an ebook")]
        [TestCase(MediaType.Ebook, MediaType.Audio, "Search asked for an ebook, release is an audiobook")]
        public void a_typed_light_novel_search_rejects_the_other_class(MediaType searched, MediaType release, string reason)
        {
            var lightNovel = new Author { Id = 2, Name = "Overlord", AuthorMetadataId = 2, Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord~ln" } };
            _remoteBook.Author = lightNovel;
            _remoteBook.Books = new List<Book> { _lightNovelVolume };
            _remoteBook.MediaType = release;

            var decision = Subject.IsSatisfiedBy(_remoteBook, new BookSearchCriteria { Author = lightNovel, Books = new List<Book> { _lightNovelVolume }, MediaType = searched });

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(reason);
        }

        [Test]
        public void an_untyped_or_matching_search_accepts_either_light_novel_class()
        {
            _remoteBook.Books = new List<Book> { _lightNovelVolume };
            _remoteBook.MediaType = MediaType.Ebook;

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(_remoteBook, new BookSearchCriteria { MediaType = MediaType.Ebook }).Accepted.Should().BeTrue();
        }

        [Test]
        public void manga_named_release_resolved_to_archive_is_refused_for_a_light_novel_volume()
        {
            var title = "Yen.Press-The.Eminence.In.Shadow.Vol.06.Manga.2022.Hybrid.Comic.eBook-BitBook";
            var lightNovel = new Author { Id = 2, Name = "The Eminence in Shadow", AuthorMetadataId = 2, Metadata = new AuthorMetadata { Name = "The Eminence in Shadow", ForeignAuthorId = "local-the-eminence-in-shadow~ln" } };
            var ebookSearch = new BookSearchCriteria { Author = lightNovel, Books = new List<Book> { _lightNovelVolume }, MediaType = MediaType.Ebook };
            var parsed = new ParsedBookInfo { Quality = QualityParser.ParseQuality(title, null, new List<int> { 7020 }) };
            parsed.Quality.Quality.Should().Be(Quality.CBZ);

            ParsingService.RegradeForLibrary(parsed, title, lightNovel, ebookSearch, new List<int> { 7020 }).Should().BeFalse();
            parsed.Quality.Quality.Should().Be(Quality.CBZ);

            _remoteBook.Author = lightNovel;
            _remoteBook.Books = new List<Book> { _lightNovelVolume };
            _remoteBook.Release = new ReleaseInfo { Title = title };
            _remoteBook.ParsedBookInfo = parsed;
            _remoteBook.MediaType = ParsingService.ResolveMediaType(parsed, lightNovel, ebookSearch, title);
            _remoteBook.MediaType.Should().Be(MediaType.Archive);

            var decision = Subject.IsSatisfiedBy(_remoteBook, ebookSearch);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("No archive edition on this volume");
        }

        [Test]
        public void pack_is_rejected_when_any_volume_lacks_the_edition()
        {
            _remoteBook.Books = new List<Book> { _lightNovelVolume, _mangaVolume };
            _remoteBook.MediaType = MediaType.Ebook;

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }
    }
}
