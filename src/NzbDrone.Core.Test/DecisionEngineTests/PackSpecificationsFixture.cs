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
    // A manga pack fans out to every volume in its range (a tokenless title maps to the whole
    // series). Grab decisions follow upstream Sonarr/Readarr semantics: the pack is rejected as
    // soon as ANY volume it maps to already has a file the release would not upgrade. Accepting
    // a pack because one mapped volume was still missing (the 2026-08 "any book wanted" rule)
    // re-grabbed the same packs forever once they turned out not to hold the missing volumes,
    // and two such packs then replaced each other's library files on every import pass.
    public abstract class PackSpecificationFixtureBase<TSpec> : CoreTest<TSpec>
        where TSpec : class
    {
        protected Author FakeAuthor;

        [SetUp]
        public void BaseSetup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            CustomFormatsTestHelpers.GivenCustomFormats();

            // Test quality order is Unknown < CBZ < CBR < ZIP; cutoff ZIP so a CBZ file is
            // upgradable and a ZIP file is satisfied.
            FakeAuthor = Builder<Author>.CreateNew()
                .With(c => c.QualityProfile = new QualityProfile
                {
                    UpgradeAllowed = true,
                    Cutoff = Quality.ZIP.Id,
                    Items = Qualities.QualityFixture.GetDefaultQualities(),
                    FormatItems = CustomFormatsTestHelpers.GetSampleFormatItems("None"),
                    MinFormatScore = 0,
                })
                .Build();

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<BookFile>()))
                .Returns(new List<CustomFormat>());
        }

        // Light novels (2026-09): the specs read the files of the release's edition (Archive for
        // a manga release), so the file sits on the volume's one Archive edition.
        protected static Book BookWithFile(Quality quality)
        {
            var book = new Book();
            book.WithEdition(MediaType.Archive, new List<BookFile> { new BookFile { Quality = new QualityModel(quality) } });

            return book;
        }

        protected static Book BookWithoutFiles()
        {
            var book = new Book();
            book.WithEdition(MediaType.Archive);

            return book;
        }

        protected RemoteBook ZipReleaseFor(params Book[] books)
        {
            return new RemoteBook
            {
                Author = FakeAuthor,
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.ZIP) },
                Books = new List<Book>(books),
                CustomFormats = new List<CustomFormat>()
            };
        }
    }

    [TestFixture]
    public class CutoffSpecificationPackFixture : PackSpecificationFixtureBase<CutoffSpecification>
    {
        [Test]
        public void pack_should_be_accepted_when_no_mapped_book_has_a_file()
        {
            var remote = ZipReleaseFor(BookWithoutFiles(), BookWithoutFiles(), BookWithoutFiles());

            Subject.IsSatisfiedBy(remote, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void pack_should_be_accepted_when_owned_files_are_below_cutoff()
        {
            var remote = ZipReleaseFor(BookWithFile(Quality.CBZ), BookWithFile(Quality.CBZ), BookWithoutFiles());

            Subject.IsSatisfiedBy(remote, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void pack_should_be_rejected_when_any_mapped_book_already_meets_cutoff()
        {
            var remote = ZipReleaseFor(BookWithFile(Quality.ZIP), BookWithoutFiles(), BookWithoutFiles());

            Subject.IsSatisfiedBy(remote, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void single_book_at_cutoff_should_be_rejected()
        {
            var remote = ZipReleaseFor(BookWithFile(Quality.ZIP));

            Subject.IsSatisfiedBy(remote, null).Accepted.Should().BeFalse();
        }
    }

    [TestFixture]
    public class UpgradeDiskSpecificationPackFixture : PackSpecificationFixtureBase<UpgradeDiskSpecification>
    {
        [Test]
        public void pack_should_be_accepted_when_no_mapped_book_has_a_file()
        {
            var remote = ZipReleaseFor(BookWithoutFiles(), BookWithoutFiles(), BookWithoutFiles());

            Subject.IsSatisfiedBy(remote, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void pack_should_be_accepted_when_owned_files_are_upgradable()
        {
            var remote = ZipReleaseFor(BookWithFile(Quality.CBZ), BookWithFile(Quality.CBZ), BookWithoutFiles());

            Subject.IsSatisfiedBy(remote, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void pack_should_be_rejected_when_any_mapped_book_has_an_equal_file()
        {
            var remote = ZipReleaseFor(BookWithFile(Quality.ZIP), BookWithoutFiles(), BookWithoutFiles());

            Subject.IsSatisfiedBy(remote, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void single_book_with_equal_file_should_be_rejected()
        {
            var remote = ZipReleaseFor(BookWithFile(Quality.ZIP));

            Subject.IsSatisfiedBy(remote, null).Accepted.Should().BeFalse();
        }
    }
}
