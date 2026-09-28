using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    // Covered volumes (2026-09-17, D4): an Audio release for a volume whose audiobook is inside
    // another volume's file is rejected unless the release carries the covering volume too (then
    // it is an upgrade of the carrier); the EPUB side and manga are untouched.
    [TestFixture]
    public class CoveredVolumeSpecificationFixture : CoreTest<CoveredVolumeSpecification>
    {
        private RemoteBook _remoteBook;
        private Book _volume;
        private Edition _audio;

        [SetUp]
        public void Setup()
        {
            _volume = new Book { Id = 5, Title = "Overlord Vol. 5", VolumeNumber = 5 };
            _volume.WithEdition(MediaType.Ebook);
            _audio = _volume.WithEdition(MediaType.Audio);

            _remoteBook = new RemoteBook
            {
                Author = new Author { Id = 3, Name = "Overlord" },
                Release = new ReleaseInfo { Title = "Overlord Vol. 5 [M4B]" },
                MediaType = MediaType.Audio,
                Books = new List<Book> { _volume }
            };
        }

        [Test]
        public void rejects_an_audio_release_for_a_covered_volume()
        {
            _audio.CoveredByVolume = 1;

            var decision = Subject.IsSatisfiedBy(_remoteBook, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Audiobook covered by Vol. 1");
        }

        private Book GivenVolumeSix(double coveredBy)
        {
            var six = new Book { Id = 6, Title = "Overlord Vol. 6", VolumeNumber = 6 };
            six.WithEdition(MediaType.Ebook);
            six.WithEdition(MediaType.Audio).CoveredByVolume = coveredBy;

            return six;
        }

        [Test]
        public void accepts_a_pack_that_carries_the_covering_volume()
        {
            // Vol. 5-6 when 6 is covered by 5: an upgrade of the carrier, for the upgrade specs
            _remoteBook.Books.Add(GivenVolumeSix(coveredBy: 5));
            _remoteBook.Release.Title = "Overlord Vol. 5-6 [M4B]";

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void rejects_a_covered_volume_released_without_its_carrier()
        {
            _remoteBook.Books = new List<Book> { GivenVolumeSix(coveredBy: 5) };
            _remoteBook.Release.Title = "Overlord Vol. 6 [M4B]";

            var decision = Subject.IsSatisfiedBy(_remoteBook, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Audiobook covered by Vol. 5");
        }

        [Test]
        public void rejects_a_pack_whose_covered_volume_has_its_carrier_elsewhere()
        {
            // Vol. 5-6 when 6 is covered by 3: the carrier is not in the release
            _remoteBook.Books.Add(GivenVolumeSix(coveredBy: 3));
            _remoteBook.Release.Title = "Overlord Vol. 5-6 [M4B]";

            var decision = Subject.IsSatisfiedBy(_remoteBook, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Audiobook covered by Vol. 3");
        }

        [Test]
        public void accepts_an_ebook_release_for_the_same_volume()
        {
            _audio.CoveredByVolume = 1;
            _remoteBook.MediaType = MediaType.Ebook;
            _remoteBook.Release.Title = "Overlord Vol. 5 [EPUB]";

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void accepts_an_audio_release_when_no_volume_is_covered()
        {
            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void accepts_a_manga_release()
        {
            var manga = new Book { Id = 7, Title = "Berserk Vol. 7" };
            manga.WithEdition(MediaType.Archive);

            _remoteBook.MediaType = MediaType.Archive;
            _remoteBook.Release.Title = "Berserk Vol. 7 [CBZ]";
            _remoteBook.Books = new List<Book> { manga };

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void accepts_an_audio_release_for_a_volume_without_editions()
        {
            _remoteBook.Books = new List<Book> { new Book { Id = 8 } };

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }
    }
}
