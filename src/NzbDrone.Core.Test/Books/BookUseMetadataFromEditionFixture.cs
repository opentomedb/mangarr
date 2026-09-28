using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // Preferred Edition (2026-09-24, D6): the precision travels with the date it describes -- the same
    // ratchet as the date (a remote with no date keeps the stored date AND its precision).
    [TestFixture]
    public class BookUseMetadataFromEditionFixture : CoreTest
    {
        [Test]
        public void a_remote_date_brings_its_precision()
        {
            var stored = new Book { ReleaseDate = new DateTime(2019, 1, 5), ReleaseDatePrecision = null };
            stored.UseMetadataFrom(new Book { ReleaseDate = new DateTime(2020, 12, 31), ReleaseDatePrecision = "year" });

            stored.ReleaseDate.Should().Be(new DateTime(2020, 12, 31));
            stored.ReleaseDatePrecision.Should().Be("year");
        }

        [Test]
        public void a_remote_without_a_date_keeps_the_stored_precision()
        {
            var stored = new Book { ReleaseDate = new DateTime(2020, 12, 31), ReleaseDatePrecision = "year" };
            stored.UseMetadataFrom(new Book { ReleaseDate = null, ReleaseDatePrecision = null });

            stored.ReleaseDatePrecision.Should().Be("year");
        }

        [Test]
        public void author_metadata_ratchets_the_edition_binding()
        {
            var stored = new AuthorMetadata { EditionLanguage = "fr", TomeLineId = "rl_fr", AnchorName = "Attack on Titan" };
            stored.UseMetadataFrom(new AuthorMetadata());

            stored.EditionLanguage.Should().Be("fr");
            stored.TomeLineId.Should().Be("rl_fr");
            stored.AnchorName.Should().Be("Attack on Titan");
        }

        // 2026-09-26 (migration 058): the collected flag follows the line -- kept when the remote binds no
        // line, replaced when it does.
        [Test]
        public void author_metadata_ratchets_the_collected_flag_with_the_line()
        {
            var stored = new AuthorMetadata { TomeLineId = "rl_fr", EditionCollected = true };
            stored.UseMetadataFrom(new AuthorMetadata());
            stored.EditionCollected.Should().BeTrue();

            stored.UseMetadataFrom(new AuthorMetadata { TomeLineId = "rl_fr2", EditionCollected = false });
            stored.EditionCollected.Should().BeFalse();
        }
    }
}
