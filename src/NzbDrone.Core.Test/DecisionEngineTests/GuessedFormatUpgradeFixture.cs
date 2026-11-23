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
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.CustomFormats;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    // The pack re-grab (2026-11-22): a Nyaa torrent titled without a format was graded CBZ by the manga
    // default and grabbed as an upgrade over twelve CBR volumes; the pack held the same CBR files byte for
    // byte and every import failed. A release whose format is a guess is never an upgrade over a file on
    // disk. It still fills a missing volume, and it still replaces a chapter-built stand-in (that rule
    // runs first, as before: Nyaa titles rarely state a format).
    [TestFixture]
    public class GuessedFormatUpgradeFixture : CoreTest<UpgradeDiskSpecification>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(x => x.ParseCustomFormat(It.IsAny<BookFile>())).Returns(new List<CustomFormat>());
        }

        private static RemoteBook MangaVolume(Quality onDisk, Quality incoming, QualityDetectionSource source)
        {
            var author = Builder<Author>.CreateNew()
                                        .With(a => a.QualityProfile = EditionScopedFixtureData.Profile(Quality.CBZ, true, Quality.Unknown, Quality.PDF, Quality.CbzChapters, Quality.CBR, Quality.CBZ))
                                        .With(a => a.AudioQualityProfile = null)
                                        .Build();

            var volume = new Book { Id = 7, Title = "Sample Saga Vol. 7", VolumeNumber = 7 };
            var files = onDisk == null
                ? new List<BookFile>()
                : new List<BookFile> { new BookFile { Id = 3, EditionId = 71, Quality = new QualityModel(onDisk), DateAdded = DateTime.Now } };
            volume.WithEdition(MediaType.Archive, files);

            return new RemoteBook
            {
                Author = author,
                Books = new List<Book> { volume },
                MediaType = MediaType.Archive,
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(incoming) { QualityDetectionSource = source } },
                CustomFormats = new List<CustomFormat>()
            };
        }

        [Test]
        public void a_guessed_cbz_is_not_an_upgrade_over_the_cbr_on_disk()
        {
            var decision = Subject.IsSatisfiedBy(MangaVolume(Quality.CBR, Quality.CBZ, QualityDetectionSource.Guess), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("no format");
        }

        [Test]
        public void a_stated_cbz_upgrades_the_cbr_on_disk()
        {
            Subject.IsSatisfiedBy(MangaVolume(Quality.CBR, Quality.CBZ, QualityDetectionSource.Name), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_guessed_cbz_still_replaces_a_chapter_built_stand_in()
        {
            Subject.IsSatisfiedBy(MangaVolume(Quality.CbzChapters, Quality.CBZ, QualityDetectionSource.Guess), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_guessed_cbz_still_fills_a_missing_volume()
        {
            Subject.IsSatisfiedBy(MangaVolume(null, Quality.CBZ, QualityDetectionSource.Guess), null).Accepted.Should().BeTrue();
        }
    }
}
