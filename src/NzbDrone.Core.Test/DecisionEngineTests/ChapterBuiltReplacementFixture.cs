using System;
using System.Collections.Generic;
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
    // Chapter-built volumes (2026-10): a real volume always replaces a CbzChapters stand-in, even
    // on a profile that forbids upgrades and whose cutoff the stand-in already meets. The real
    // UpgradeDiskSpecification is resolved here because UpgradeDiskSpecificationFixture is [Ignore]d.
    public static class ChapterBuiltFixtureData
    {
        public static RemoteBook MangaVolume(Quality onDisk, Quality incoming, bool chaptersRankedAboveCbz = false)
        {
            var profile = new QualityProfile
            {
                UpgradeAllowed = false,
                Cutoff = Quality.CBZ.Id,
                MinFormatScore = 0,
                FormatItems = CustomFormatsTestHelpers.GetSampleFormatItems("None"),
                Items = new List<QualityProfileQualityItem>
                {
                    new QualityProfileQualityItem { Quality = Quality.CbzChapters, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.PDF, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.CBR, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.CBZ, Allowed = true }
                }
            };

            if (chaptersRankedAboveCbz)
            {
                // A profile whose order alone would call CBZ over CbzChapters a downgrade.
                profile.Items.Reverse();
            }

            var author = Builder<Author>.CreateNew().With(a => a.QualityProfile = profile).Build();

            var volume = new Book { Id = 7, Title = "Your Lie in April Vol. 7", VolumeNumber = 7 };
            volume.WithEdition(MediaType.Archive, new List<BookFile> { new BookFile { Id = 3, EditionId = 71, Quality = new QualityModel(onDisk), DateAdded = DateTime.UtcNow } });

            return new RemoteBook
            {
                Author = author,
                Books = new List<Book> { volume },
                MediaType = MediaType.Archive,
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(incoming) },
                CustomFormats = new List<CustomFormat>()
            };
        }
    }

    [TestFixture]
    public class ReplacesChapterBuiltFixture
    {
        [Test]
        public void chapter_built_is_replaced_by_a_real_volume()
        {
            UpgradableSpecification.ReplacesChapterBuilt(new QualityModel(Quality.CbzChapters), new QualityModel(Quality.CBZ)).Should().BeTrue();
        }

        [Test]
        public void chapter_built_is_not_replaced_by_chapter_built()
        {
            UpgradableSpecification.ReplacesChapterBuilt(new QualityModel(Quality.CbzChapters), new QualityModel(Quality.CbzChapters)).Should().BeFalse();
        }

        [Test]
        public void chapter_built_is_not_replaced_by_an_unknown_quality()
        {
            UpgradableSpecification.ReplacesChapterBuilt(new QualityModel(Quality.CbzChapters), new QualityModel(Quality.Unknown)).Should().BeFalse();
        }

        [Test]
        public void a_real_volume_is_not_a_chapter_built_replacement()
        {
            UpgradableSpecification.ReplacesChapterBuilt(new QualityModel(Quality.CBZ), new QualityModel(Quality.CBZ)).Should().BeFalse();
        }
    }

    [TestFixture]
    public class ChapterBuiltUpgradeDiskFixture : CoreTest<UpgradeDiskSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(x => x.ParseCustomFormat(It.IsAny<BookFile>())).Returns(new List<CustomFormat>());
        }

        [Test]
        public void real_volume_replaces_the_chapter_built_file()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzChapters, Quality.CBZ), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void real_volume_replaces_the_chapter_built_file_on_a_profile_that_ranks_chapters_above_cbz()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzChapters, Quality.CBZ, chaptersRankedAboveCbz: true), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void chapter_built_over_chapter_built_is_rejected()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzChapters, Quality.CbzChapters), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void cbz_over_cbz_is_rejected_as_before()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CBZ, Quality.CBZ), null).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class ChapterBuiltUpgradeAllowedFixture : CoreTest<UpgradeAllowedSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(x => x.ParseCustomFormat(It.IsAny<BookFile>(), It.IsAny<Author>())).Returns(new List<CustomFormat>());
        }

        [Test]
        public void real_volume_replaces_the_chapter_built_file_although_upgrades_are_off()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzChapters, Quality.CBZ), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void cbz_over_cbz_stays_accepted_here_as_before()
        {
            // Equal quality is no upgrade, so this spec has nothing to forbid (UpgradeDisk rejects it).
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CBZ, Quality.CBZ), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_real_upgrade_of_a_real_volume_is_still_forbidden()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.PDF, Quality.CBZ), null).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class ChapterBuiltCutoffFixture : CoreTest<CutoffSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(x => x.ParseCustomFormat(It.IsAny<BookFile>())).Returns(new List<CustomFormat>());
        }

        [Test]
        public void real_volume_replaces_the_chapter_built_file_although_it_is_the_cutoff()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzChapters, Quality.CBZ), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void chapter_built_over_chapter_built_is_rejected()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CbzChapters, Quality.CbzChapters), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void cbz_over_cbz_is_rejected_as_before()
        {
            Subject.IsSatisfiedBy(ChapterBuiltFixtureData.MangaVolume(Quality.CBZ, Quality.CBZ), null).Accepted.Should().BeFalse();
        }
    }
}
