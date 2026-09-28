using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    // Light novels (2026-09): a file imports to the edition of its own class, and never into
    // the wrong library (D9).
    [TestFixture]
    public class MediaTypeMatchesEditionSpecificationFixture : CoreTest<MediaTypeMatchesEditionSpecification>
    {
        private static LocalBook Local(string path, string foreignAuthorId, MediaType? editionType = null)
        {
            var local = new LocalBook
            {
                Path = path,
                Author = new Author { Metadata = new AuthorMetadata { Name = "X", ForeignAuthorId = foreignAuthorId } }
            };

            if (editionType.HasValue)
            {
                local.Edition = new Edition { MediaType = editionType.Value };
            }

            return local;
        }

        [TestCase("/downloads/x/Series - Vol 001.cbz", "local-x", MediaType.Archive)]
        [TestCase("/downloads/x/Series - Vol 001.pdf", "local-x", MediaType.Archive)]
        [TestCase("/downloads/x/Series - Vol 001.epub", "local-x~ln", MediaType.Ebook)]
        [TestCase("/downloads/x/Series - Vol 001.m4b", "local-x~ln", MediaType.Audio)]
        [TestCase("/downloads/x/Series - Vol 001 - 03.mp3", "local-x~ln", MediaType.Audio)]
        [TestCase("/downloads/x/Series - Vol 001.pdf", "local-x~ln", MediaType.Ebook)]
        public void should_accept_a_file_on_the_edition_of_its_class(string path, string id, MediaType editionType)
        {
            Subject.IsSatisfiedBy(Local(path, id, editionType), null).Accepted.Should().BeTrue();
        }

        [TestCase("/downloads/x/Series - Vol 001.epub", "local-x", MediaType.Archive)]
        [TestCase("/downloads/x/Series - Vol 001.m4b", "local-x", MediaType.Archive)]
        [TestCase("/downloads/x/Series - Vol 001.cbz", "local-x~ln", MediaType.Ebook)]
        [TestCase("/downloads/x/Series - Vol 001.epub", "local-x~ln", MediaType.Audio)]
        [TestCase("/downloads/x/Series - Vol 001.mp3", "local-x~ln", MediaType.Ebook)]
        [TestCase("/downloads/x/Series - Vol 001.pdf", "local-x~ln", MediaType.Audio)]
        [TestCase("/downloads/x/Series - Vol 001.pdf", "local-x", MediaType.Ebook)]
        public void should_reject_a_file_of_another_class(string path, string id, MediaType editionType)
        {
            Subject.IsSatisfiedBy(Local(path, id, editionType), null).Accepted.Should().BeFalse();
        }

        // Server messages (2026-09-26): each class pair is now its own sentence; the reasons are the English the
        // old "{0} file in a manga entry" / "{0} file matched to the {1} edition" templates rendered.
        [TestCase("/downloads/x/Series - Vol 001.epub", "local-x", null, "ebook file in a manga entry")]
        [TestCase("/downloads/x/Series - Vol 001.m4b", "local-x", null, "audio file in a manga entry")]
        [TestCase("/downloads/x/Series - Vol 001.cbz", "local-x~ln", null, "Archive file in a light-novel entry")]
        [TestCase("/downloads/x/Series - Vol 001.cbz", "local-x", MediaType.Ebook, "archive file matched to the ebook edition")]
        [TestCase("/downloads/x/Series - Vol 001.cbz", "local-x", MediaType.Audio, "archive file matched to the audio edition")]
        [TestCase("/downloads/x/Series - Vol 001.epub", "local-x~ln", MediaType.Archive, "ebook file matched to the archive edition")]
        [TestCase("/downloads/x/Series - Vol 001.epub", "local-x~ln", MediaType.Audio, "ebook file matched to the audio edition")]
        [TestCase("/downloads/x/Series - Vol 001.m4b", "local-x~ln", MediaType.Archive, "audio file matched to the archive edition")]
        [TestCase("/downloads/x/Series - Vol 001.m4b", "local-x~ln", MediaType.Ebook, "audio file matched to the ebook edition")]
        public void should_give_the_old_english_reason(string path, string id, MediaType? editionType, string reason)
        {
            Subject.IsSatisfiedBy(Local(path, id, editionType), null).Reason.Should().Be(reason);
        }

        // LN PDF (2026-09-22): a light novel's PDF is its ebook, not "an archive in a light-novel
        // entry"; a manga PDF is the manga archive as before.
        [Test]
        public void a_pdf_is_typed_by_the_entry_before_an_edition_is_known()
        {
            Subject.IsSatisfiedBy(Local("/downloads/x/Series - Vol 001.pdf", "local-x~ln"), null).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(Local("/downloads/x/Series - Vol 001.pdf", "local-x"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_by_library_before_an_edition_is_known()
        {
            Subject.IsSatisfiedBy(Local("/downloads/x/Series - Vol 001.epub", "local-x"), null).Accepted.Should().BeFalse();
            Subject.IsSatisfiedBy(Local("/downloads/x/Series - Vol 001.cbz", "local-x~ln"), null).Accepted.Should().BeFalse();
            Subject.IsSatisfiedBy(Local("/downloads/x/Series - Vol 001.cbz", "local-x"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_nothing_is_known_yet()
        {
            Subject.IsSatisfiedBy(new LocalBook { Path = "/downloads/x/y.epub" }, null).Accepted.Should().BeTrue();
        }
    }
}
