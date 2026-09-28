using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // Preferred Edition (2026-09-24, spec §5 / D4): a non-English light novel's one display title carries
    // its edition's volume label; its subtitle filter knows the edition's words. English is unchanged
    // (EnEditionPinning's en-titles golden).
    [TestFixture]
    public class LightNovelTitlesEditionFixture : CoreTest
    {
        [Test]
        public void a_french_volume_is_a_tome()
        {
            LightNovelTitles.Display("Sword Art Online", 1, "Aincrad", "fr").Should().Be("Sword Art Online: Aincrad (Tome 1)");
            LightNovelTitles.Display("Overlord", 3.5, null, "de").Should().Be("Overlord (Band 3.5)");
            LightNovelTitles.Display("Overlord", 2, null, "ja").Should().Be("Overlord (第2巻)");
            LightNovelTitles.Display("Overlord", 2, null).Should().Be("Overlord (Vol. 2)");
        }

        [TestCase("Tome 3", "fr", true)]
        [TestCase("T. 3", "fr", true)]
        [TestCase("Roman", "fr", true)]
        [TestCase("Band 2", "de", true)]
        [TestCase("Bd. 2", "de", true)]
        [TestCase("第3巻", "ja", true)]
        [TestCase("Aincrad", "fr", false)]
        public void edition_labels_are_junk_for_their_edition(string candidate, string edition, bool junk)
        {
            Subtitles.IsJunk(candidate, "Overlord", editionLanguage: edition).Should().Be(junk);
        }

        // M14 fix round 1 (2026-09-24): the wider edition vocabulary -- the capital "T" abbreviation only,
        // kanji numerals, the part labels, "Teil N", and an article with an edition word -- while a real
        // French / German / Japanese subtitle that merely contains such a word is kept.
        [TestCase("T05", "fr", true)]
        [TestCase("t 3", "fr", false)]
        [TestCase("Le roman", "fr", true)]
        [TestCase("L'intégrale", "fr", true)]
        [TestCase("Le Roman de Renart", "fr", false)]
        [TestCase("Tome d'or", "fr", false)]
        [TestCase("Teil 2", "de", true)]
        [TestCase("Der Roman", "de", true)]
        [TestCase("Der Band der Freundschaft", "de", false)]
        [TestCase("Die Rückkehr des Königs", "de", false)]
        [TestCase("第三巻", "ja", true)]
        [TestCase("上巻", "ja", true)]
        [TestCase("下巻", "ja", true)]
        [TestCase("ライトノベル", "ja", true)]
        [TestCase("アリシゼーション・ビギニング", "ja", false)]
        // M14 polish (2026-09-24): a non-breaking space (French typography) and an ideographic space separate words.
        [TestCase("Le\u00A0roman", "fr", true)]
        [TestCase("小説\u3000文庫", "ja", true)]
        public void the_edition_vocabulary_rejects_labels_and_keeps_real_subtitles(string candidate, string edition, bool junk)
        {
            Subtitles.IsJunk(candidate, "Overlord", editionLanguage: edition).Should().Be(junk);
        }

        // English never reads that vocabulary: each of these stays a subtitle, as it always was.
        [TestCase("T05")]
        [TestCase("Le roman")]
        [TestCase("Teil 2")]
        [TestCase("Der Roman")]
        [TestCase("第三巻")]
        [TestCase("上巻")]
        [TestCase("ライトノベル")]
        [TestCase("Le\u00A0roman")]
        [TestCase("小説\u3000文庫")]
        public void english_ignores_the_edition_vocabulary(string candidate)
        {
            Subtitles.IsJunk(candidate, "Overlord").Should().BeFalse();
            Subtitles.IsJunk(candidate, "Overlord", editionLanguage: "en").Should().BeFalse();
        }

        [Test]
        public void the_sort_title_is_unchanged()
        {
            LightNovelTitles.SortTitle("Sword Art Online", 11.5).Should().Be("Sword Art Online 0011.5");
        }

        // Preferred Edition (2026-09-24, M14): the edition's words stop counting towards a full title only
        // for that edition -- "Roman" / "Tome" are no volume words to an English series.
        [Test]
        public void edition_words_do_not_make_a_full_title_for_their_edition()
        {
            Subtitles.IsFullTitle("Rascal Does Not Dream Roman Tome", "Rascal Does Not Dream", "fr").Should().BeFalse();
            Subtitles.IsFullTitle("Rascal Does Not Dream Band Ausgabe", "Rascal Does Not Dream", "de").Should().BeFalse();
            Subtitles.IsFullTitle("Rascal Does Not Dream of Petite Devil Kohai", "Rascal Does Not Dream", "fr").Should().BeTrue();
        }

        [Test]
        public void a_french_catalogue_tome_title_derives_no_subtitle()
        {
            Subtitles.Derive(null, "Tome 3", null, "Overlord", null, 3, trustedArtifactCandidate: true, editionLanguage: "fr").Should().BeNull();
        }

        [Test]
        public void a_french_entry_shows_tome_labels_in_display_of()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord~ln", EditionLanguage = "fr" }
            };
            var book = new Book { VolumeNumber = 4, Subtitle = "Tome 4" };

            LightNovelTitles.DisplayOf(author, book).Should().Be("Overlord (Tome 4)");
            LightNovelTitles.DisplaySubtitleOf(author, book).Should().Be(string.Empty);
        }

        // Preferred Edition (2026-09-24, M14): today's English outputs, pinned. These strings are what
        // calibre (title + sort) and Audiobookshelf hold for the live English library -- every value
        // below was taken from HEAD 2de94cc before M14 touched the code, and must never move.
        [TestCase("Rascal Does Not Dream", 1, "Rascal Does Not Dream of Bunny Girl Senpai", "Rascal Does Not Dream of Bunny Girl Senpai (Vol. 1)")]
        [TestCase("Rascal Does Not Dream", 2, "Rascal Does Not Dream of Petite Devil Kohai", "Rascal Does Not Dream of Petite Devil Kohai (Vol. 2)")]
        [TestCase("Rascal Does Not Dream", 16, "Rascal Does Not Dream of a Beach Queen +", "Rascal Does Not Dream of a Beach Queen + (Vol. 16)")]
        [TestCase("Rascal Does Not Dream", 2, "Rascal Does Not Dream 2", "Rascal Does Not Dream (Vol. 2)")]
        [TestCase("Sword Art Online", 1, "Aincrad", "Sword Art Online: Aincrad (Vol. 1)")]
        [TestCase("Overlord", 3.5, null, "Overlord (Vol. 3.5)")]
        [TestCase("Tokyo Ghoul", 1, "Tokyo Ghoul: Days", "Tokyo Ghoul: Days (Vol. 1)")]
        [TestCase("Mushoku Tensei: Jobless Reincarnation", 1, "Jobless Reincarnation (Light Novel), Vol. 1", "Mushoku Tensei: Jobless Reincarnation (Vol. 1)")]
        [TestCase("Overlord", 4, "Tome 4", "Overlord: Tome 4 (Vol. 4)")]
        [TestCase("Overlord", 2, "Band 2", "Overlord: Band 2 (Vol. 2)")]
        [TestCase("Overlord", 3, "Roman", "Overlord: Roman (Vol. 3)")]
        [TestCase("Overlord", 5, "第5巻", "Overlord: 第5巻 (Vol. 5)")]
        public void english_display_titles_are_pinned(string series, double volumeNumber, string subtitle, string expected)
        {
            LightNovelTitles.Display(series, volumeNumber, subtitle).Should().Be(expected);
            LightNovelTitles.Display(series, volumeNumber, subtitle, null).Should().Be(expected);
            LightNovelTitles.Display(series, volumeNumber, subtitle, "en").Should().Be(expected);
            LightNovelTitles.Display(series, volumeNumber, subtitle, " EN ").Should().Be(expected);
        }

        [TestCase("Tome 3", false)]
        [TestCase("Band 2", false)]
        [TestCase("Bd. 2", false)]
        [TestCase("Roman", false)]
        [TestCase("第3巻", false)]
        [TestCase("Aincrad", false)]
        [TestCase("Vol. 1", true)]
        [TestCase("Light Novel, Vol. 5", true)]
        public void english_junk_filter_is_pinned(string candidate, bool junk)
        {
            Subtitles.IsJunk(candidate, "Overlord").Should().Be(junk);
            Subtitles.IsJunk(candidate, "Overlord", editionLanguage: null).Should().Be(junk);
            Subtitles.IsJunk(candidate, "Overlord", editionLanguage: "en").Should().Be(junk);
        }

        [TestCase("Rascal Does Not Dream of Petite Devil Kohai", true)]
        [TestCase("Rascal Does Not Dream of a Beach Queen +", true)]
        [TestCase("Rascal Does Not Dream Novel Series", false)]
        [TestCase("Rascal Does Not Dream Roman Tome", true)]
        [TestCase("Rascal Does Not Dream Band Ausgabe", true)]
        public void english_full_title_test_is_pinned(string candidate, bool full)
        {
            Subtitles.IsFullTitle(candidate, "Rascal Does Not Dream").Should().Be(full);
            Subtitles.IsFullTitle(candidate, "Rascal Does Not Dream", null).Should().Be(full);
            Subtitles.IsFullTitle(candidate, "Rascal Does Not Dream", "en").Should().Be(full);
        }

        [Test]
        public void english_subtitle_derivation_is_pinned()
        {
            Subtitles.Derive(null, "Rascal Does Not Dream of Petite Devil Kohai", null, "Rascal Does Not Dream", null, 2, trustedArtifactCandidate: true)
                .Should().Be("Rascal Does Not Dream of Petite Devil Kohai");
            Subtitles.Derive(null, "Rascal Does Not Dream of Petite Devil Kohai", null, "Rascal Does Not Dream", null, 2)
                .Should().BeNull();
            Subtitles.Derive("Sword Art Online 1: Aincrad", null, null, "Sword Art Online", null, 1)
                .Should().Be("Aincrad");
            Subtitles.Derive(null, "Tome 3", null, "Overlord", null, 3, trustedArtifactCandidate: true)
                .Should().Be("Tome 3");
            Subtitles.Derive(null, "Tome 3", null, "Overlord", null, 3, trustedArtifactCandidate: true, editionLanguage: "en")
                .Should().Be("Tome 3");
            Subtitles.Derive(null, "Rascal Does Not Dream of Petite Devil Kohai", null, "Rascal Does Not Dream", null, 2, trustedArtifactCandidate: true, editionLanguage: "en")
                .Should().Be("Rascal Does Not Dream of Petite Devil Kohai");
        }

        [Test]
        public void english_display_of_and_display_subtitle_of_are_pinned()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata { Name = "Rascal Does Not Dream", ForeignAuthorId = "local-rascal-does-not-dream~ln" }
            };
            var book = new Book { VolumeNumber = 2, Subtitle = "Rascal Does Not Dream of Petite Devil Kohai" };

            LightNovelTitles.DisplayOf(author, book).Should().Be("Rascal Does Not Dream of Petite Devil Kohai (Vol. 2)");
            LightNovelTitles.DisplaySubtitleOf(author, book).Should().Be("Rascal Does Not Dream of Petite Devil Kohai");

            author.Metadata.Value.EditionLanguage = "en";

            LightNovelTitles.DisplayOf(author, book).Should().Be("Rascal Does Not Dream of Petite Devil Kohai (Vol. 2)");
            LightNovelTitles.DisplaySubtitleOf(author, book).Should().Be("Rascal Does Not Dream of Petite Devil Kohai");
        }
    }
}
