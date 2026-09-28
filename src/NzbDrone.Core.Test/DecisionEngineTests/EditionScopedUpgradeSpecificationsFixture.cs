using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.CustomFormats;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    // Light novels (2026-09): the cutoff / upgrade-on-disk / upgrade-allowed trio compare the
    // incoming release against the files of the edition of ITS media type, in the profile of
    // that media type. One volume: an EPUB edition holding an EPUB (at the EPUB cutoff) and an
    // Audio edition holding nothing; and the mirror, a six-volume pack (see LightNovelPack).
    public static class EditionScopedFixtureData
    {
        public static QualityProfile Profile(Quality cutoff, bool upgradeAllowed, params Quality[] allowed)
        {
            return new QualityProfile
            {
                UpgradeAllowed = upgradeAllowed,
                Cutoff = cutoff.Id,
                MinFormatScore = 0,
                FormatItems = CustomFormatsTestHelpers.GetSampleFormatItems("None"),
                Items = allowed.Select(q => new QualityProfileQualityItem { Quality = q, Allowed = true }).ToList()
            };
        }

        public static RemoteBook RemoteBook(MediaType mediaType, Quality quality)
        {
            var author = Builder<Author>.CreateNew()
                                        .With(a => a.QualityProfile = Profile(Quality.EPUB, false, Quality.Unknown, Quality.EPUB))
                                        .With(a => a.AudioQualityProfile = Profile(Quality.M4B, true, Quality.UnknownAudio, Quality.MP3, Quality.FLAC, Quality.M4B))
                                        .Build();

            var volume = new Book { Id = 5, Title = "Overlord Vol. 5" };
            volume.WithEdition(MediaType.Ebook, new List<BookFile> { new BookFile { Id = 1, EditionId = 52, Quality = new QualityModel(Quality.EPUB), DateAdded = DateTime.UtcNow } });
            volume.WithEdition(MediaType.Audio);

            return new RemoteBook
            {
                Author = author,
                Books = new List<Book> { volume },
                MediaType = mediaType,
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(quality) },
                CustomFormats = new List<CustomFormat>()
            };
        }

        // The mirror shape (B3c D2, 2026-09-18): a multi-volume pack over six light-novel volumes
        // whose Audio editions each hold an M4B and whose EPUB editions hold nothing. The ebook
        // profile lists M4B above its EPUB cutoff exactly as the live "Light Novel EPUB" profile
        // does, so an M4B that leaked into the ebook comparison WOULD read as "cutoff met".
        public static RemoteBook LightNovelPack(MediaType mediaType, Quality quality)
        {
            var author = Builder<Author>.CreateNew()
                                        .With(a => a.QualityProfile = Profile(Quality.EPUB, true, Quality.Unknown, Quality.EPUB, Quality.UnknownAudio, Quality.MP3, Quality.FLAC, Quality.M4B))
                                        .With(a => a.AudioQualityProfile = Profile(Quality.M4B, true, Quality.UnknownAudio, Quality.MP3, Quality.FLAC, Quality.M4B))
                                        .Build();

            var volumes = new List<Book>();

            for (var i = 1; i <= 6; i++)
            {
                var volume = new Book { Id = i, Title = $"The Eminence in Shadow Vol. {i}", VolumeNumber = i };
                volume.WithEdition(MediaType.Ebook);
                volume.WithEdition(MediaType.Audio, new List<BookFile> { new BookFile { Id = 100 + i, EditionId = (i * 10) + (int)MediaType.Audio + 1, Quality = new QualityModel(Quality.M4B), DateAdded = DateTime.UtcNow } });
                volumes.Add(volume);
            }

            return new RemoteBook
            {
                Author = author,
                Books = volumes,
                MediaType = mediaType,
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(quality) },
                CustomFormats = new List<CustomFormat>()
            };
        }

        // A manga volume: one Archive edition holding a CBZ at the manga profile's CBZ cutoff.
        public static RemoteBook MangaVolume(Quality quality)
        {
            var author = Builder<Author>.CreateNew()
                                        .With(a => a.QualityProfile = Profile(Quality.CBZ, true, Quality.Unknown, Quality.PDF, Quality.CBR, Quality.CBZ))
                                        .With(a => a.AudioQualityProfile = null)
                                        .Build();

            var volume = new Book { Id = 7, Title = "Your Lie in April Vol. 7", VolumeNumber = 7 };
            volume.WithEdition(MediaType.Archive, new List<BookFile> { new BookFile { Id = 3, EditionId = 71, Quality = new QualityModel(Quality.CBZ), DateAdded = DateTime.UtcNow } });

            return new RemoteBook
            {
                Author = author,
                Books = new List<Book> { volume },
                MediaType = MediaType.Archive,
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(quality) },
                CustomFormats = new List<CustomFormat>()
            };
        }
    }

    [TestFixture]
    public class CutoffSpecificationEditionScopeFixture : CoreTest<CutoffSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(x => x.ParseCustomFormat(It.IsAny<BookFile>())).Returns(new List<CustomFormat>());
        }

        [Test]
        public void audio_release_is_not_blocked_by_the_epub_on_disk()
        {
            Subject.IsSatisfiedBy(EditionScopedFixtureData.RemoteBook(MediaType.Audio, Quality.M4B), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void epub_release_is_blocked_by_the_epub_that_meets_its_cutoff()
        {
            Subject.IsSatisfiedBy(EditionScopedFixtureData.RemoteBook(MediaType.Ebook, Quality.EPUB), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void epub_pack_is_not_blocked_by_the_audiobooks_on_its_volumes()
        {
            Subject.IsSatisfiedBy(EditionScopedFixtureData.LightNovelPack(MediaType.Ebook, Quality.EPUB), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void audio_pack_is_blocked_by_the_audiobooks_that_meet_their_cutoff()
        {
            Subject.IsSatisfiedBy(EditionScopedFixtureData.LightNovelPack(MediaType.Audio, Quality.M4B), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void manga_volume_at_cutoff_is_still_rejected()
        {
            Subject.IsSatisfiedBy(EditionScopedFixtureData.MangaVolume(Quality.CBZ), null).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class UpgradeDiskSpecificationEditionScopeFixture : CoreTest<UpgradeDiskSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(x => x.ParseCustomFormat(It.IsAny<BookFile>())).Returns(new List<CustomFormat>());
        }

        [Test]
        public void audio_release_is_not_a_downgrade_of_the_epub_on_disk()
        {
            Subject.IsSatisfiedBy(EditionScopedFixtureData.RemoteBook(MediaType.Audio, Quality.MP3), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void epub_release_is_equal_to_the_epub_on_disk_and_rejected()
        {
            Subject.IsSatisfiedBy(EditionScopedFixtureData.RemoteBook(MediaType.Ebook, Quality.EPUB), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void epub_pack_is_not_a_downgrade_of_the_audiobooks_on_its_volumes()
        {
            Subject.IsSatisfiedBy(EditionScopedFixtureData.LightNovelPack(MediaType.Ebook, Quality.EPUB), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void audio_pack_equal_to_the_audiobooks_on_disk_is_rejected()
        {
            Subject.IsSatisfiedBy(EditionScopedFixtureData.LightNovelPack(MediaType.Audio, Quality.M4B), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void manga_volume_with_an_equal_file_is_still_rejected()
        {
            Subject.IsSatisfiedBy(EditionScopedFixtureData.MangaVolume(Quality.CBZ), null).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class UpgradeAllowedSpecificationEditionScopeFixture : CoreTest<UpgradeAllowedSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(x => x.ParseCustomFormat(It.IsAny<BookFile>(), It.IsAny<Author>())).Returns(new List<CustomFormat>());
        }

        [Test]
        public void audio_release_is_judged_by_the_audio_profile_with_no_audio_files()
        {
            Subject.IsSatisfiedBy(EditionScopedFixtureData.RemoteBook(MediaType.Audio, Quality.M4B), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void epub_release_that_is_no_upgrade_is_still_accepted_here()
        {
            // Equal quality is not an "upgrade", so this spec has nothing to forbid; UpgradeDisk rejects it.
            Subject.IsSatisfiedBy(EditionScopedFixtureData.RemoteBook(MediaType.Ebook, Quality.EPUB), null).Accepted.Should().BeTrue();
        }
    }
}
