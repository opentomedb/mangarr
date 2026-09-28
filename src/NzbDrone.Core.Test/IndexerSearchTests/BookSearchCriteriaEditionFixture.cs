using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    // Preferred Edition (2026-09-24, spec §3): an edition's volume token replaces v05 / v5 one for one;
    // the recall tier keeps its bare number and two alias slots -- slot 1 the best edition-language
    // alias, slot 2 the English anchor + vNN. Queries per volume never grow (private-tracker query budget).
    [TestFixture]
    public class BookSearchCriteriaEditionFixture : CoreTest
    {
        private static BookSearchCriteria Criteria(string name, string edition, string anchor, double volume, params string[] aliases)
        {
            var author = new Author { Name = name };
            author.Metadata.Value.EditionLanguage = edition;
            author.Metadata.Value.AnchorName = anchor;
            author.Metadata.Value.Aliases = new List<string>(aliases);

            return new BookSearchCriteria { Author = author, BookTitle = "x", VolumeNumber = volume };
        }

        [Test]
        public void french_tokens()
        {
            var c = Criteria("L'Attaque des Titans", "fr", "Attack on Titan", 5, "Shingeki no Kyojin", "L'Attaque des Titans");

            c.BookQuery.Should().Be("L'Attaque+des+Titans+T05");
            c.BookQueryAlt.Should().Be("L'Attaque+des+Titans+Tome+5");
            c.BookQueryBare.Should().Be("L'Attaque+des+Titans+05");
            c.AliasQueries.Should().Equal("Shingeki+no+Kyojin+T05", "Attack+on+Titan+v05");
        }

        [Test]
        public void german_tokens()
        {
            var c = Criteria("Angriff auf Titan", "de", "Attack on Titan", 12);

            c.BookQuery.Should().Be("Angriff+auf+Titan+Band+12");
            c.BookQueryAlt.Should().Be("Angriff+auf+Titan+Bd+12");
            c.AliasQueries.Should().Equal("Attack+on+Titan+v12");
        }

        [Test]
        public void japanese_tokens()
        {
            var c = Criteria("Shingeki no Kyojin", "ja", "Attack on Titan", 5, "進撃の巨人");

            c.BookQuery.Should().Be("Shingeki+no+Kyojin+第05巻");
            c.BookQueryAlt.Should().Be("Shingeki+no+Kyojin+第5巻");
            c.AliasQueries.Should().Equal("Attack+on+Titan+v05");
        }

        [Test]
        public void a_language_without_a_token_table_uses_v()
        {
            var c = Criteria("L'attacco dei giganti", "it", "Attack on Titan", 5);

            c.BookQuery.Should().Be("L'attacco+dei+giganti+v05");
        }

        [Test]
        public void an_english_series_is_unchanged()
        {
            var c = Criteria("Kaiju No. 8", null, null, 5, "Kaijuu 8-gou", "Guardianes de la Noche");

            c.BookQuery.Should().Be("Kaiju+No+8+v05");
            c.BookQueryAlt.Should().Be("Kaiju+No+8+v5");
            c.AliasQueries.Should().Equal("Kaijuu+8+gou+v05");
        }

        [TestCase("L'Attaque des Titans", "fr", true)]
        [TestCase("Les Carnets de l'apothicaire", "fr", true)]
        [TestCase("Die Tagebücher der Apothekerin", "de", true)]
        [TestCase("Die Tagebücher der Apothekerin", "fr", false)]
        [TestCase("L'Attaque des Titans", null, false)]
        [TestCase("進撃の巨人", "ja", false)]
        [TestCase("Kaijuu 8-gou", null, true)]
        public void searchable_alias_is_edition_aware(string alias, string edition, bool expected)
        {
            BookSearchCriteria.IsSearchableAlias(alias, edition).Should().Be(expected);
        }

        [Test]
        public void a_non_english_light_novel_audio_search_names_the_english_anchor()
        {
            var c = Criteria("Sword Art Online (FR)", "fr", "Sword Art Online", 2);
            c.MediaType = MediaType.Audio;
            c.Subtitle = "Aincrad";

            c.BookQuery.Should().Be("Sword+Art+Online+v02");
            c.SubtitleQueries.Should().Equal("Sword+Art+Online+2+Aincrad");
        }

        // Controller ruling S6 (2026-09-24): the anchor can lead the Aliases list itself (own-language
        // and AniList English/romaji rows both empty, BuildEditionAltTitles appends only the anchor).
        // Slot 1 must skip it -- the anchor already owns slot 2 -- and move on to the next distinct
        // alias, so the anchor appears exactly once in AliasQueries, not twice under two different
        // tokens.
        [Test]
        public void alias_slot_one_skips_the_anchor_when_it_leads_the_list()
        {
            var c = Criteria("L'Attaque des Titans", "fr", "Attack on Titan", 5, "Attack on Titan", "Shingeki no Kyojin");

            c.AliasQueries.Should().Equal("Shingeki+no+Kyojin+T05", "Attack+on+Titan+v05");
        }
    }
}
