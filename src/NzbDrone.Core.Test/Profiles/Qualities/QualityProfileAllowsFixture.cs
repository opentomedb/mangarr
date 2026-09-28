using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Qualities
{
    // LN PDF final review (2026-09-22): QualityProfile.Allows, the lookup CompletedDownloadService
    // and EbookPdfAllowedSpecification use to gate an Ebook PDF import.
    [TestFixture]
    public class QualityProfileAllowsFixture : CoreTest
    {
        [TestCase(true)]
        [TestCase(false)]
        public void allows_a_flat_quality_by_its_own_allowed_flag(bool allowed)
        {
            var profile = new QualityProfile
            {
                Items = new List<QualityProfileQualityItem> { new QualityProfileQualityItem { Quality = Quality.EbookPdf, Allowed = allowed } }
            };

            profile.Allows(Quality.EbookPdf).Should().Be(allowed);
        }

        // id 8 can sit inside a group -- Allows reads the GROUP's own Allowed flag (GetIndex's
        // default, respectGroupOrder = false), not the nested item's own, same as
        // QualityAllowedByProfileSpecification's read of a release. The nested item's own Allowed is
        // deliberately the opposite of the group's, so a bug reading the wrong flag would show up.
        [TestCase(true)]
        [TestCase(false)]
        public void allows_reads_the_enclosing_group_s_allowed_flag_for_a_grouped_quality(bool groupAllowed)
        {
            var profile = new QualityProfile
            {
                Items = new List<QualityProfileQualityItem>
                {
                    new QualityProfileQualityItem
                    {
                        Name = "Ebook Group",
                        Allowed = groupAllowed,
                        Items = new List<QualityProfileQualityItem>
                        {
                            new QualityProfileQualityItem { Quality = Quality.EbookPdf, Allowed = !groupAllowed },
                            new QualityProfileQualityItem { Quality = Quality.AZW3, Allowed = !groupAllowed }
                        }
                    }
                }
            };

            profile.Allows(Quality.EbookPdf).Should().Be(groupAllowed);
        }
    }
}
