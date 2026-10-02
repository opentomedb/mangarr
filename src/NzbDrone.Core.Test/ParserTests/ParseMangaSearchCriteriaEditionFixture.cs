using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    // Preferred Edition (2026-09-24, spec §3 Parser): the search bridge reads a series' own edition's
    // volume tokens; a release named in romaji or English still resolves through the stored aliases.
    [TestFixture]
    public class ParseMangaSearchCriteriaEditionFixture : CoreTest
    {
        private static Author Series(string name, string edition, params string[] aliases)
        {
            return new Author { Metadata = new AuthorMetadata { Name = name, EditionLanguage = edition, Aliases = new List<string>(aliases) } };
        }

        private static readonly Author French = Series("L'Attaque des Titans", "fr", "Shingeki no Kyojin", "Attack on Titan");

        // Preferred Edition (2026-09-24): the volumes a French series holds, so the fuzzy author/book path below the bridges runs too.
        private static List<Book> FrenchBooks()
        {
            var books = new List<Book>();

            for (var i = 1; i <= 5; i++)
            {
                var title = "L'Attaque des Titans Tome " + i;
                books.Add(new Book { Title = title, VolumeNumber = i, Editions = new List<Edition> { new Edition { Title = title, Monitored = true } } });
            }

            return books;
        }

        [TestCase("L'Attaque des Titans - Tome 5 [FR]", 5)]
        [TestCase("L.Attaque.des.Titans.T05.FRENCH.CBZ-GRP", 5)]
        [TestCase("Shingeki no Kyojin T05 [FR]", 5)]
        [TestCase("Attack on Titan Tome 12 (Pika)", 12)]
        public void french_single_volumes(string title, double volume)
        {
            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria(title, French, new List<Book>());

            parsed.Should().NotBeNull();
            parsed.VolumeNumber.Should().Be(volume);
        }

        [TestCase("L'Attaque des Titans Tomes 1 à 5")]
        [TestCase("L'Attaque des Titans Tome 1 à Tome 5")]
        public void a_french_pack(string title)
        {
            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria(title, French, new List<Book>());

            parsed.VolumeStart.Should().Be(1);
            parsed.VolumeEnd.Should().Be(5);
        }

        [TestCase("L'Attaque des Titans T01 à T03 [FR]")]
        [TestCase("L'Attaque des Titans T01-T03")]
        public void a_french_t_range_is_a_pack(string title)
        {
            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria(title, French, new List<Book>());

            parsed.Should().NotBeNull();
            parsed.VolumeStart.Should().Be(1);
            parsed.VolumeEnd.Should().Be(3);
        }

        // Preferred Edition (2026-09-24, ruling S7): the English anchor is an accepted series name for an edition search, so an
        // English-named [FR] release parses for a French series even when no alias spells the anchor.
        [Test]
        public void an_english_named_release_resolves_through_the_anchor_name()
        {
            var french = Series("L'Attaque des Titans", "fr", "Shingeki no Kyojin");
            french.Metadata.Value.AnchorName = "Attack on Titan";

            var parsed = Parser.Parser.ParseBookTitleWithSearchCriteria("Attack on Titan T05 [FR]", french, new List<Book>());

            parsed.Should().NotBeNull();
            parsed.VolumeNumber.Should().Be(5);
        }

        [Test]
        public void a_collected_edition_without_a_range_is_no_single_volume()
        {
            // The series name is accepted and "Tome 1" parses -- only the collected marker rejects it.
            Parser.Parser.ParseBookTitleWithSearchCriteria("L'Attaque des Titans Tome 1 (Intégrale)", French, new List<Book>()).Should().BeNull();
        }

        // Preferred Edition (2026-09-24, ruling A9): a numbered collected release is rejected in v1 -- a ranged one too ("Coffret
        // T01 à T03" is one box, not three volumes of this edition), in either word order.
        // Fix round 1: every case reaches the collected reject (each parses without it -- checked by removing it).
        [TestCase("L'Attaque des Titans Tomes 1 à 3 (Coffret)")]
        [TestCase("L'Attaque des Titans T01 à T03 (Coffret)")]
        [TestCase("[Coffret] L'Attaque des Titans T01 à T03")]
        [TestCase("L'Attaque des Titans Tomes 1 à 3 (Intégrale)")]
        public void a_numbered_collected_release_is_rejected(string title)
        {
            Parser.Parser.ParseBookTitleWithSearchCriteria(title, French, new List<Book>()).Should().BeNull();
            Parser.Parser.ParseBookTitleWithSearchCriteria(title, French, FrenchBooks()).Should().BeNull();
        }

        // A marker between the series and the token makes the release's series "L'Attaque des Titans Coffret",
        // which no accepted key matches -- rejected by the series match, before the collected check.
        [TestCase("L'Attaque des Titans Coffret T01 à T03")]
        [TestCase("L'Attaque des Titans - Intégrale Tomes 1 à 3")]
        public void a_marker_inside_the_series_name_is_another_series(string title)
        {
            Parser.Parser.ParseBookTitleWithSearchCriteria(title, French, new List<Book>()).Should().BeNull();
            Parser.Parser.ParseBookTitleWithSearchCriteria(title, French, FrenchBooks()).Should().BeNull();
        }

        // Preferred Edition (2026-09-24, M9 fix round 1 ⚠2): a series bound to a collected line carries the marker
        // in its own names, so its releases are its volumes, not a rejected collected book.
        [Test]
        public void a_series_bound_to_a_collected_line_takes_its_own_releases()
        {
            var massiv = Series("One Piece Massiv", "de", "One Piece");
            Parser.Parser.ParseBookTitleWithSearchCriteria("One Piece Massiv Band 3", massiv, new List<Book>()).VolumeNumber.Should().Be(3);

            var perfect = Series("Fullmetal Alchemist", "fr", "Fullmetal Alchemist Perfect Edition");
            Parser.Parser.ParseBookTitleWithSearchCriteria("Fullmetal Alchemist Perfect Edition Tome 2", perfect, new List<Book>()).VolumeNumber.Should().Be(2);
        }

        // 2026-09-26: a series bound to an omnibus-only line whose names carry no marker -- the line itself
        // is collected (EditionCollected, set on refresh), so its marker-named releases are its volumes.
        [Test]
        public void a_series_whose_bound_line_is_collected_takes_marker_named_releases()
        {
            var omnibusOnly = Series("L'Attaque des Titans", "fr", "Attack on Titan");
            Parser.Parser.ParseBookTitleWithSearchCriteria("L'Attaque des Titans Tome 3 (Intégrale)", omnibusOnly, new List<Book>()).Should().BeNull();

            omnibusOnly.Metadata.Value.EditionCollected = true;
            Parser.Parser.ParseBookTitleWithSearchCriteria("L'Attaque des Titans Tome 3 (Intégrale)", omnibusOnly, new List<Book>()).VolumeNumber.Should().Be(3);
        }

        [Test]
        public void another_series_with_the_token_is_still_rejected()
        {
            Parser.Parser.ParseBookTitleWithSearchCriteria("Kaiju No. 8 T05", French, new List<Book>()).Should().BeNull();
        }

        [Test]
        public void german_and_japanese()
        {
            var german = Series("Angriff auf Titan", "de", "Attack on Titan");
            Parser.Parser.ParseBookTitleWithSearchCriteria("Angriff auf Titan Band 05", german, new List<Book>()).VolumeNumber.Should().Be(5);

            var pack = Parser.Parser.ParseBookTitleWithSearchCriteria("Angriff auf Titan Bände 1-3", german, new List<Book>());
            pack.VolumeStart.Should().Be(1);
            pack.VolumeEnd.Should().Be(3);

            var repeated = Parser.Parser.ParseBookTitleWithSearchCriteria("Angriff auf Titan Band 1 bis Band 5", german, new List<Book>());
            repeated.VolumeStart.Should().Be(1);
            repeated.VolumeEnd.Should().Be(5);

            var japanese = Series("Shingeki no Kyojin", "ja", "進撃の巨人", "Attack on Titan");
            Parser.Parser.ParseBookTitleWithSearchCriteria("進撃の巨人 5巻", japanese, new List<Book>()).VolumeNumber.Should().Be(5);

            var japanesePack = Parser.Parser.ParseBookTitleWithSearchCriteria("進撃の巨人 1-5巻", japanese, new List<Book>());
            japanesePack.VolumeNumber.Should().BeNull();
            japanesePack.VolumeStart.Should().Be(1);
            japanesePack.VolumeEnd.Should().Be(5);
        }

        // Follow-up round (KR/CN consumer, I1 residue): a fallback series parses releases like English -- a numbered
        // "Box Set" is read as its volume (as an unbound English entry reads it) instead of the edition's A9 reject,
        // and "5巻" is not read; the same binding as a Japanese edition keeps both edition rules.
        private static Author JapaneseSeries(bool fallback)
        {
            var series = Series("Stand Up Start", "ja", "Sutando Appu Sutato");
            series.Metadata.Value.EditionFallback = fallback;

            return series;
        }

        [Test]
        public void a_fallback_series_reads_a_numbered_box_set_like_english()
        {
            Parser.Parser.ParseBookTitleWithSearchCriteria("Stand Up Start Vol. 2 (Box Set)", JapaneseSeries(true), new List<Book>()).VolumeNumber.Should().Be(2);
            Parser.Parser.ParseBookTitleWithSearchCriteria("Stand Up Start Vol. 2 (Box Set)", Series("Stand Up Start", null), new List<Book>()).VolumeNumber.Should().Be(2);
        }

        [Test]
        public void the_same_binding_as_an_edition_rejects_a_numbered_box_set()
        {
            Parser.Parser.ParseBookTitleWithSearchCriteria("Stand Up Start Vol. 2 (Box Set)", JapaneseSeries(false), new List<Book>()).Should().BeNull();
        }

        [Test]
        public void a_fallback_series_does_not_read_the_editions_volume_token()
        {
            var fallback = Parser.Parser.ParseBookTitleWithSearchCriteria("Stand Up Start 5巻", JapaneseSeries(true), new List<Book>());
            var english = Parser.Parser.ParseBookTitleWithSearchCriteria("Stand Up Start 5巻", Series("Stand Up Start", null), new List<Book>());

            (fallback?.VolumeNumber).Should().Be(english?.VolumeNumber);
            (fallback?.VolumeNumber).Should().NotBe(5);
            Parser.Parser.ParseBookTitleWithSearchCriteria("Stand Up Start 5巻", JapaneseSeries(false), new List<Book>()).VolumeNumber.Should().Be(5);
        }
    }
}
