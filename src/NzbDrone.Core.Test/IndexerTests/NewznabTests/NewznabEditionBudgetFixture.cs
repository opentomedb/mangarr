using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.NewznabTests
{
    // Preferred Edition (2026-09-24, private-tracker query budget, controller ruling S6 -- corrected): the invariant is
    // a CAP, not parity with an English series of the same shape. The main tier sends at most 2
    // queries; the recall tier sends at most the bare number plus 2 alias slots -- the most an
    // English series with aliases already sends. An edition search may reach that cap where an
    // English search of the same shape would not (the anchor slot is always on, and an edition token
    // never collapses at a double-digit volume the way English's v12 == v12 does); it must never
    // exceed it. Each test also pins the English shape's own counts, to prove that side is unchanged.
    [TestFixture]
    public class NewznabEditionBudgetFixture : CoreTest<NewznabRequestGenerator>
    {
        private const int MainTierCap = 2;
        private const int RecallTierCap = 3; // bare number + 2 alias slots

        [SetUp]
        public void SetUp()
        {
            Subject.Settings = new NewznabSettings { BaseUrl = "http://127.0.0.1:1234/", Categories = new[] { 7030 }, ApiKey = "abcd" };
            Mocker.GetMock<INewznabCapabilitiesProvider>().Setup(v => v.GetCapabilities(It.IsAny<NewznabSettings>())).Returns(new NewznabCapabilities());
        }

        private List<int> Shape(string name, string edition, string anchor, double volume, params string[] aliases)
        {
            var author = new Author { Name = name };
            author.Metadata.Value.EditionLanguage = edition;
            author.Metadata.Value.AnchorName = anchor;
            author.Metadata.Value.Aliases = new List<string>(aliases);

            var chain = Subject.GetSearchRequests(new BookSearchCriteria { Author = author, BookTitle = "x", VolumeNumber = volume });

            return Enumerable.Range(0, chain.Tiers).Select(t => chain.GetTier(t).Count()).ToList();
        }

        private static void NeverExceedsTheCap(List<int> shape)
        {
            shape.Should().HaveCount(2);
            shape[0].Should().BeLessOrEqualTo(MainTierCap, "the main tier");
            shape[1].Should().BeLessOrEqualTo(RecallTierCap, "the recall tier");
        }

        [Test]
        public void a_french_search_costs_what_an_english_one_costs()
        {
            var english = Shape("Attack on Titan", null, null, 5, "Shingeki no Kyojin", "AoT Tales");
            var french = Shape("L'Attaque des Titans", "fr", "Attack on Titan", 5, "Shingeki no Kyojin");

            french.Should().Equal(english);
        }

        [Test]
        public void a_zero_alias_edition_search_never_exceeds_the_cap()
        {
            var english = Shape("Attack on Titan", null, null, 5);
            english.Should().Equal(2, 1);

            var french = Shape("L'Attaque des Titans", "fr", "Attack on Titan", 5);
            NeverExceedsTheCap(french);
        }

        [Test]
        public void a_two_alias_edition_search_never_exceeds_the_cap()
        {
            var english = Shape("Attack on Titan", null, null, 5, "Shingeki no Kyojin", "AoT Encyclopedia");
            english.Should().Equal(2, 3);

            // Same alias pair as the french_tokens fixture: at HEAD, "L'Attaque des Titans" is rejected
            // outright by the English-only filter (recall tier 2); today it's accepted for French but
            // still loses slot 1 to "Shingeki no Kyojin" -- the growth to 3 comes from the always-on
            // anchor slot, and 3 is exactly the cap.
            var french = Shape("L'Attaque des Titans", "fr", "Attack on Titan", 5, "Shingeki no Kyojin", "L'Attaque des Titans");
            NeverExceedsTheCap(french);
        }

        [Test]
        public void a_double_digit_volume_edition_search_never_exceeds_the_cap()
        {
            var english = Shape("Attack on Titan", null, null, 12);
            english.Should().Equal(1, 1);

            var german = Shape("Angriff auf Titan", "de", "Attack on Titan", 12);
            NeverExceedsTheCap(german);
        }
    }
}
