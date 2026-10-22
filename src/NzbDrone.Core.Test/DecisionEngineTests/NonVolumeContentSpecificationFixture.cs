using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class NonVolumeContentSpecificationFixture : CoreTest<NonVolumeContentSpecification>
    {
        private const string Audiobook = "[PZG].Sword.Art.Online,.Vol..01. Audiobook .[Yen.Audio]";
        private const string Codec = "Sword Art Online Vol 1 M4B 64kbps";
        private const string GroupTag = "Sword Art Online Vol 1 [Troglodyte]";

        private static RemoteBook GivenRelease(string title, MediaType mediaType)
        {
            return new RemoteBook
            {
                Release = new ReleaseInfo { Title = title },
                MediaType = mediaType
            };
        }

        // Light novels (2026-09, incident fix C): the audio leg wants exactly these releases.
        [TestCase(Audiobook)]
        [TestCase(Codec)]
        [TestCase(GroupTag)]
        public void audiobook_marked_release_is_accepted_on_the_audio_leg(string title)
        {
            Subject.IsSatisfiedBy(GivenRelease(title, MediaType.Audio), null).Accepted.Should().BeTrue();
        }

        [TestCase(Audiobook, MediaType.Ebook)]
        [TestCase(Codec, MediaType.Ebook)]
        [TestCase(GroupTag, MediaType.Ebook)]
        [TestCase(Audiobook, MediaType.Archive)]
        [TestCase(Codec, MediaType.Archive)]
        [TestCase(GroupTag, MediaType.Archive)]
        public void audiobook_marked_release_is_rejected_on_the_ebook_and_archive_legs(string title, MediaType mediaType)
        {
            var decision = Subject.IsSatisfiedBy(GivenRelease(title, mediaType), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Audiobook release");
        }

        [TestCase(MediaType.Archive)]
        [TestCase(MediaType.Ebook)]
        [TestCase(MediaType.Audio)]
        public void chapter_release_with_no_volume_token_is_rejected_on_every_leg(MediaType mediaType)
        {
            var decision = Subject.IsSatisfiedBy(GivenRelease("Sword Art Online c001-c118 (Digital) (v2)", mediaType), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Chapter release with no volume archives");
        }

        [TestCase(MediaType.Archive)]
        [TestCase(MediaType.Ebook)]
        [TestCase(MediaType.Audio)]
        public void volume_release_is_accepted_on_every_leg(MediaType mediaType)
        {
            Subject.IsSatisfiedBy(GivenRelease("Sword Art Online Vol. 1 (Digital)", mediaType), null).Accepted.Should().BeTrue();
        }

        // Volumes built from chapter packs (2026-10-21): a release with a parsed chapter range is the
        // chapter path's business when the feature is on; otherwise the old rejection still fires.
        private static RemoteBook GivenChapterRelease()
        {
            var remoteBook = GivenRelease("Marriagetoxin c001-073 (Digital) (empyrealarrow)", MediaType.Archive);
            remoteBook.Author = new Author { Metadata = new AuthorMetadata { Name = "Marriagetoxin", ForeignAuthorId = "local-marriagetoxin" } };
            remoteBook.ParsedBookInfo = new ParsedBookInfo { ChapterRange = new ChapterRange { Start = 1, End = 73, IsDigital = true } };

            return remoteBook;
        }

        [Test]
        public void parsed_chapter_release_is_left_to_the_chapter_path_when_the_feature_is_on()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.BuildVolumesFromChapters).Returns(true);

            Subject.IsSatisfiedBy(GivenChapterRelease(), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void parsed_chapter_release_is_rejected_when_the_feature_is_off()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.BuildVolumesFromChapters).Returns(false);

            var decision = Subject.IsSatisfiedBy(GivenChapterRelease(), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Chapter release with no volume archives");
        }

        [Test]
        public void chapter_titled_release_without_a_parsed_range_is_rejected_when_the_feature_is_on()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.BuildVolumesFromChapters).Returns(true);

            var remoteBook = GivenChapterRelease();
            remoteBook.ParsedBookInfo.ChapterRange = null;

            Subject.IsSatisfiedBy(remoteBook, null).Reason.Should().Be("Chapter release with no volume archives");
        }
    }
}
