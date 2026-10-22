using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // Chapter-built volumes (2026-10): a built volume stays wanted until a real one replaces it,
    // even on a profile that forbids upgrades (where the cutoff is the lowest allowed quality).
    [TestFixture]
    public class BookCutoffServiceFixture : CoreTest<BookCutoffService>
    {
        private List<QualitiesBelowCutoff> _captured;

        private void GivenProfile(bool upgradeAllowed, bool chaptersAllowed = true, bool chaptersAboveCutoff = false)
        {
            var profile = new QualityProfile
            {
                Id = 3,
                UpgradeAllowed = upgradeAllowed,
                Cutoff = Quality.CBZ.Id,
                Items = new List<QualityProfileQualityItem>
                {
                    new QualityProfileQualityItem { Quality = Quality.CbzChapters, Allowed = chaptersAllowed },
                    new QualityProfileQualityItem { Quality = Quality.PDF, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.CBR, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.CBZ, Allowed = true }
                }
            };

            if (chaptersAboveCutoff)
            {
                // CbzChapters ranked above the CBZ cutoff: the cutoff slice alone never holds it.
                var chapters = profile.Items[0];
                profile.Items.RemoveAt(0);
                profile.Items.Add(chapters);
            }

            Mocker.GetMock<IQualityProfileService>().Setup(s => s.All()).Returns(new List<QualityProfile> { profile });

            Mocker.GetMock<IBookRepository>()
                  .Setup(r => r.BooksWhereCutoffUnmet(It.IsAny<PagingSpec<Book>>(), It.IsAny<List<QualitiesBelowCutoff>>(), It.IsAny<LibraryType?>(), It.IsAny<WantedMediaTypeFilter?>()))
                  .Callback<PagingSpec<Book>, List<QualitiesBelowCutoff>, LibraryType?, WantedMediaTypeFilter?>((p, q, l, m) => _captured = q)
                  .Returns<PagingSpec<Book>, List<QualitiesBelowCutoff>, LibraryType?, WantedMediaTypeFilter?>((p, q, l, m) => p);
        }

        [Test]
        public void should_keep_chapter_built_wanted_when_upgrades_are_not_allowed()
        {
            GivenProfile(false);

            Subject.BooksWhereCutoffUnmet(new PagingSpec<Book>());

            _captured.Single().QualityIds.Should().BeEquivalentTo(new[] { Quality.CbzChapters.Id });
        }

        [Test]
        public void should_keep_chapter_built_wanted_when_the_quality_is_unticked_and_ranked_above_the_cutoff()
        {
            GivenProfile(true, chaptersAllowed: false, chaptersAboveCutoff: true);

            Subject.BooksWhereCutoffUnmet(new PagingSpec<Book>());

            _captured.Single().QualityIds.Should().BeEquivalentTo(new[] { Quality.PDF.Id, Quality.CBR.Id, Quality.CbzChapters.Id });
        }

        [Test]
        public void should_keep_chapter_built_wanted_when_upgrades_are_allowed()
        {
            GivenProfile(true);

            Subject.BooksWhereCutoffUnmet(new PagingSpec<Book>());

            _captured.Single().QualityIds.Should().BeEquivalentTo(new[] { Quality.CbzChapters.Id, Quality.PDF.Id, Quality.CBR.Id });
        }
    }
}
