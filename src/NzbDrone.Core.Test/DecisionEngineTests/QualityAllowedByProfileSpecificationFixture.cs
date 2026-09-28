using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]

    public class QualityAllowedByProfileSpecificationFixture : CoreTest<QualityAllowedByProfileSpecification>
    {
        private RemoteBook _remoteBook;

        public static object[] AllowedTestCases =
        {
            new object[] { Quality.MP3 },
            new object[] { Quality.MP3 },
            new object[] { Quality.MP3 }
        };

        public static object[] DeniedTestCases =
        {
            new object[] { Quality.FLAC },
            new object[] { Quality.Unknown }
        };

        [SetUp]
        public void Setup()
        {
            var fakeAuthor = Builder<Author>.CreateNew()
                         .With(c => c.QualityProfile = new QualityProfile { Cutoff = Quality.MP3.Id })
                         .Build();

            _remoteBook = new RemoteBook
            {
                Author = fakeAuthor,
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.MP3, new Revision(version: 2)) },
            };
        }

        [Test]
        [TestCaseSource(nameof(AllowedTestCases))]
        public void should_allow_if_quality_is_defined_in_profile(Quality qualityType)
        {
            _remoteBook.ParsedBookInfo.Quality.Quality = qualityType;
            _remoteBook.Author.QualityProfile.Value.Items = Qualities.QualityFixture.GetDefaultQualities(Quality.MP3, Quality.MP3, Quality.MP3);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        [TestCaseSource(nameof(DeniedTestCases))]
        public void should_not_allow_if_quality_is_not_defined_in_profile(Quality qualityType)
        {
            _remoteBook.ParsedBookInfo.Quality.Quality = qualityType;
            _remoteBook.Author.QualityProfile.Value.Items = Qualities.QualityFixture.GetDefaultQualities(Quality.MP3, Quality.MP3, Quality.MP3);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void audio_release_should_use_the_audio_profile()
        {
            // EPUB profile allows nothing audio; the audio profile allows MP3.
            _remoteBook.Author.QualityProfile.Value.Items = Qualities.QualityFixture.GetDefaultQualities(Quality.CBZ);
            _remoteBook.Author.AudioQualityProfile = new QualityProfile { Cutoff = Quality.MP3.Id, Items = Qualities.QualityFixture.GetDefaultQualities(Quality.MP3) };
            _remoteBook.ParsedBookInfo.Quality.Quality = Quality.MP3;

            _remoteBook.MediaType = MediaType.Audio;
            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();

            _remoteBook.MediaType = MediaType.Ebook;
            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void audio_release_falls_back_to_the_main_profile_when_no_audio_profile_is_set()
        {
            _remoteBook.Author.QualityProfile.Value.Items = Qualities.QualityFixture.GetDefaultQualities(Quality.MP3);
            _remoteBook.Author.AudioQualityProfile = null;
            _remoteBook.ParsedBookInfo.Quality.Quality = Quality.MP3;
            _remoteBook.MediaType = MediaType.Audio;

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        // The "Manga" profile as QualityProfileService builds it on first start: every defined
        // quality present (each is its own group), only the archive formats allowed — so EPUB is
        // present and NOT allowed, rather than merely absent.
        private static List<QualityProfileQualityItem> MangaProfileItems()
        {
            var allowed = new[] { Quality.CBZ, Quality.CBR, Quality.ZIP, Quality.RAR, Quality.PDF };

            return Quality.DefaultQualityDefinitions
                          .OrderBy(d => d.Weight)
                          .Select(d => new QualityProfileQualityItem { Quality = d.Quality, Allowed = allowed.Contains(d.Quality) })
                          .ToList();
        }

        // D1 mirror case (2026-09-18): a "(Light Novel) epub" release found by a manga entry's search
        // grades EPUB, and the manga profile refuses it today — pinned so the light-novel rule's
        // counterpart never regresses.
        [Test]
        public void light_novel_epub_release_is_refused_by_the_manga_profile()
        {
            var title = "Overlord Vol 5 (Light Novel) epub";
            var manga = new Author { Id = 1, Name = "Overlord", AuthorMetadataId = 1, Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord" } };
            manga.QualityProfile = new QualityProfile { Name = "Manga", Cutoff = Quality.CBZ.Id, Items = MangaProfileItems() };

            _remoteBook.Author = manga;
            _remoteBook.ParsedBookInfo = new ParsedBookInfo { ReleaseTitle = title, Quality = QualityParser.ParseQuality(title) };
            _remoteBook.ParsedBookInfo.Quality.Quality.Should().Be(Quality.EPUB);
            _remoteBook.MediaType = ParsingService.ResolveMediaType(_remoteBook.ParsedBookInfo, manga, null, title);
            _remoteBook.MediaType.Should().Be(MediaType.Ebook);

            var decision = Subject.IsSatisfiedBy(_remoteBook, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("EPUB is not wanted in profile");
        }
    }
}
