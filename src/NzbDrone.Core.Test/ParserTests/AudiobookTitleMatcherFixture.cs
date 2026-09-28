using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    // Light-novel audio (2026-09-18, D4): an audio release named the way Audible names the product
    // maps to the searched volume whose audiobook title or subtitle it IS -- exact normalised
    // equality against a finite key set, no containment, no fuzzy widening; a subtitle that names
    // the series is never a key; a bare numeric range is a pack; manga never enters.
    [TestFixture]
    public class AudiobookTitleMatcherFixture : CoreTest
    {
        private static Author LightNovel(string name, params string[] aliases)
        {
            return new Author
            {
                Id = 1,
                CleanName = name.ToLowerInvariant(),
                AuthorMetadataId = 1,
                Metadata = new AuthorMetadata { Name = name, ForeignAuthorId = $"local-{name.ToLowerInvariant().Replace(' ', '-')}~ln", Aliases = new List<string>(aliases) }
            };
        }

        private static Author Manga(string name)
        {
            return new Author
            {
                Id = 2,
                CleanName = name.ToLowerInvariant(),
                AuthorMetadataId = 2,
                Metadata = new AuthorMetadata { Name = name, ForeignAuthorId = $"local-{name.ToLowerInvariant().Replace(' ', '-')}", Aliases = new List<string>() }
            };
        }

        private static Book Volume(double number, string subtitle, string audiobookTitle)
        {
            var book = new Book
            {
                Id = (int)number,
                Title = $"Vol. {number}",
                ForeignBookId = $"local-x-v{number}",
                VolumeNumber = number,
                Subtitle = subtitle,
                AuthorMetadataId = 1
            };

            book.Editions = new List<Edition>
            {
                new Edition { BookId = book.Id, MediaType = MediaType.Ebook, Title = book.Title },
                new Edition { BookId = book.Id, MediaType = MediaType.Audio, Title = book.Title, AudiobookTitle = audiobookTitle }
            };

            return book;
        }

        private static Book Sao21() => Volume(21, "Unital Ring I", "Sword Art Online 21: Unital Ring I");
        private static Book Sao22() => Volume(22, "Unital Ring II", "Sword Art Online 22: Unital Ring II");

        // Normalise: "by <author>" to the end, bracket tags, parentheticals, a trailing audiobook /
        // unabridged / light novel label, a vol/volume/book token in front of a number, punctuation.
        [TestCase("Sword Art Online 21 - Unital Ring I by Reki Kawahara [ENG / M4B]", "swordartonline21unitalringi")]
        [TestCase("Early Years - The Beginning After the End Book 1 (Unabridged)", "earlyyearsthebeginningaftertheend1")]
        [TestCase("Sword Art Online, Vol. 21: Unital Ring I (Light Novel)", "swordartonline21unitalringi")]
        [TestCase("Overlord Volume 1 - The Undead King Unabridged Audiobook", "overlord1theundeadking")]
        [TestCase("Konosuba Vol. 3 Audiobook", "konosuba3")]
        [TestCase("Unital Ring I (Sword Art Online 21) [M4B]", "unitalringi")]
        [TestCase("by Reki Kawahara", "byrekikawahara")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void should_normalise_release_title(string title, string expected)
        {
            AudiobookTitleMatcher.Normalise(title).Should().Be(expected);
        }

        [Test]
        public void should_match_series_number_subtitle_by_author_with_tags()
        {
            AudiobookTitleMatcher.Matches("Sword Art Online 21 - Unital Ring I by Reki Kawahara [ENG / M4B]", Sao21(), LightNovel("Sword Art Online")).Should().BeTrue();
        }

        [Test]
        public void should_match_audible_title_beside_the_series()
        {
            // The Audible product name ("Early Years") next to the series and a Book token.
            var tbate1 = Volume(1, "Early Years", "Early Years");

            AudiobookTitleMatcher.Matches("Early Years - The Beginning After the End Book 1 (Unabridged)", tbate1, LightNovel("The Beginning After the End")).Should().BeTrue();
        }

        [Test]
        public void should_not_match_when_the_subtitle_is_null_and_the_release_names_an_alias()
        {
            // Vol 1's derived subtitle is null ("Aincrad" names the series -- it is an alias).
            var sao1 = Volume(1, null, "Sword Art Online 1: Aincrad");

            AudiobookTitleMatcher.Matches("Sword Art Online - Aincrad [M4B]", sao1, LightNovel("Sword Art Online", "Aincrad")).Should().BeFalse();
        }

        [Test]
        public void should_not_match_a_volume_token_release_by_the_bridge()
        {
            // "Konosuba Vol. 3" is the volume-token path's business; nothing here names Vol 3's
            // audiobook title or subtitle, and there is no bare "<Series> <N>" key.
            var konosuba3 = Volume(3, "You're Being Summoned, Darkness", "Konosuba: God's Blessing on This Wonderful World!, Vol. 3: You're Being Summoned, Darkness");

            AudiobookTitleMatcher.Matches("Konosuba Vol. 3 Audiobook", konosuba3, LightNovel("Konosuba")).Should().BeFalse();
        }

        [Test]
        public void should_never_match_for_a_manga_author()
        {
            var book = Volume(1, "Early Years", "Early Years");

            AudiobookTitleMatcher.Matches("Early Years", book, Manga("The Beginning After the End")).Should().BeFalse();
        }

        [Test]
        public void should_match_the_subtitle_alone()
        {
            AudiobookTitleMatcher.Matches("Unital Ring I [M4B]", Sao21(), LightNovel("Sword Art Online")).Should().BeTrue();
        }

        [Test]
        public void should_match_the_subtitle_with_the_series_in_parentheses()
        {
            AudiobookTitleMatcher.Matches("Unital Ring I (Sword Art Online 21) [M4B]", Sao21(), LightNovel("Sword Art Online")).Should().BeTrue();
        }

        [Test]
        public void should_match_the_subtitle_in_parentheses_after_the_series()
        {
            // The parenthetical is the subtitle: the parentheses-unwrapped form is the key.
            AudiobookTitleMatcher.Matches("Sword Art Online 21 (Unital Ring I) [M4B]", Sao21(), LightNovel("Sword Art Online")).Should().BeTrue();
        }

        [Test]
        public void should_match_series_number_subtitle_without_an_audiobook_title()
        {
            var sao21 = Volume(21, "Unital Ring I", null);

            AudiobookTitleMatcher.Matches("Sword Art Online 21: Unital Ring I", sao21, LightNovel("Sword Art Online")).Should().BeTrue();
        }

        [Test]
        public void should_tolerate_a_volume_token_in_front_of_the_number()
        {
            AudiobookTitleMatcher.Matches("Sword Art Online Vol. 21 - Unital Ring I", Sao21(), LightNovel("Sword Art Online")).Should().BeTrue();
            AudiobookTitleMatcher.Matches("Sword Art Online Volume 21 - Unital Ring I", Sao21(), LightNovel("Sword Art Online")).Should().BeTrue();
            AudiobookTitleMatcher.Matches("Sword Art Online Book 21 - Unital Ring I", Sao21(), LightNovel("Sword Art Online")).Should().BeTrue();
        }

        [Test]
        public void should_not_match_a_release_that_names_only_the_series_and_number()
        {
            // "<Series> <N>" is a key only when Audible named the product that way ("<Series>, Vol. N");
            // SAO's products carry the subtitle, so the bare shape names nothing here.
            AudiobookTitleMatcher.Matches("Sword Art Online 21 [M4B]", Sao21(), LightNovel("Sword Art Online")).Should().BeFalse();
        }

        [Test]
        public void should_match_a_series_vol_n_product_name_to_that_volume_only()
        {
            // Audible names Re:ZERO "<Series>, Vol. N": the tokenless "<Series> 12" is Vol 12's key and
            // no other volume's -- never ambiguous with Vol 1.
            var author = LightNovel("Re:ZERO -Starting Life in Another World-");
            var volumes = new[] { 1, 2, 12 }.Select(n => Volume(n, null, $"Re:ZERO -Starting Life in Another World-, Vol. {n}")).ToList();

            var hit = AudiobookTitleMatcher.Match("Re:ZERO -Starting Life in Another World- 12 [M4B]", volumes, author);

            hit.Should().NotBeNull();
            hit.VolumeNumber.Should().Be(12);
        }

        [Test]
        public void should_not_match_a_release_carrying_a_bare_numeric_range()
        {
            // "1-2" is a pack; without this "…World- 1-2" would normalise to Vol 12's "<Series>12" key.
            var author = LightNovel("Re:ZERO -Starting Life in Another World-");
            var vol12 = Volume(12, null, "Re:ZERO -Starting Life in Another World-, Vol. 12");

            AudiobookTitleMatcher.Matches("Re:ZERO -Starting Life in Another World- 1-2 [M4B]", vol12, author).Should().BeFalse();
            AudiobookTitleMatcher.Matches("Re:ZERO -Starting Life in Another World- 1 – 2 [M4B]", vol12, author).Should().BeFalse();

            // Any Unicode dash is a range dash (U+2015 horizontal bar here): Normalize strips them all.
            AudiobookTitleMatcher.Matches("Re:ZERO -Starting Life in Another World- 1―2 [M4B]", vol12, author).Should().BeFalse();

            // The parenthesised spelling is a pack too: the kept-text reading would otherwise
            // normalise "(1-2)" onto Vol 12's "<Series>12" key.
            AudiobookTitleMatcher.Matches("Re:ZERO -Starting Life in Another World- (1-2) [M4B]", vol12, author).Should().BeFalse();
        }

        [Test]
        public void should_judge_the_bare_range_on_the_title_without_its_tags_and_parentheticals()
        {
            // A date, an ISBN or a technical range inside a tag is not a pack; the bare range in the
            // name itself still is.
            AudiobookTitleMatcher.Matches("Unital Ring I (Sword Art Online 21) [2024-09-18 M4B]", Sao21(), LightNovel("Sword Art Online")).Should().BeTrue();
            AudiobookTitleMatcher.Matches("Unital Ring I (Sword Art Online 21) (978-1-9753-2138-9) [M4B]", Sao21(), LightNovel("Sword Art Online")).Should().BeTrue();

            var author = LightNovel("Re:ZERO -Starting Life in Another World-");
            var vol12 = Volume(12, null, "Re:ZERO -Starting Life in Another World-, Vol. 12");

            AudiobookTitleMatcher.Matches("Re:ZERO -Starting Life in Another World- 1-2 [M4B]", vol12, author).Should().BeFalse();
        }

        [Test]
        public void should_match_the_product_name_beside_the_series_in_either_order()
        {
            var tbate1 = Volume(1, "Early Years", "Early Years");
            var author = LightNovel("The Beginning After the End");

            AudiobookTitleMatcher.Matches("The Beginning After the End: Early Years [M4B]", tbate1, author).Should().BeTrue();
            AudiobookTitleMatcher.Matches("The Beginning After the End 1 - Early Years [M4B]", tbate1, author).Should().BeTrue();
            AudiobookTitleMatcher.Matches("Early Years (The Beginning After the End Book 1) [M4B]", tbate1, author).Should().BeTrue();
        }

        [Test]
        public void match_should_return_null_when_none_or_several_searched_volumes_are_named()
        {
            var author = LightNovel("Sword Art Online");
            var twins = new List<Book> { Volume(20, null, "Same Product"), Volume(21, null, "Same Product") };

            var sao = new List<Book> { Sao21(), Sao22() };

            AudiobookTitleMatcher.Match("Same Product [M4B]", twins, author).Should().BeNull();
            AudiobookTitleMatcher.Match("Nothing Here [M4B]", sao, author).Should().BeNull();
            AudiobookTitleMatcher.Match("Unital Ring I [M4B]", sao, author).Should().BeSameAs(sao[0]);
        }

        [Test]
        public void should_not_let_a_subtitle_that_names_the_series_be_a_key()
        {
            // A stored subtitle that is an alias of the series is never a key.
            var sao1 = Volume(1, "Aincrad", null);

            AudiobookTitleMatcher.Matches("Aincrad [M4B]", sao1, LightNovel("Sword Art Online", "Aincrad")).Should().BeFalse();
        }

        [Test]
        public void should_not_widen_a_prefix_subtitle_onto_the_next_volume()
        {
            // "Unital Ring I" is a prefix of "Unital Ring II": Vol 22's release is Vol 22's only.
            AudiobookTitleMatcher.Matches("Unital Ring II (Sword Art Online 22) [M4B]", Sao21(), LightNovel("Sword Art Online")).Should().BeFalse();
            AudiobookTitleMatcher.Matches("Unital Ring II (Sword Art Online 22) [M4B]", Sao22(), LightNovel("Sword Art Online")).Should().BeTrue();
        }

        [Test]
        public void should_never_key_an_audiobook_title_that_is_the_series_name()
        {
            // A product named exactly the series (or an alias) is no key: the bare "<Series>" release
            // is a whole-series batch, not Vol 1.
            var vol1 = Volume(1, null, "Sword Art Online");
            var author = LightNovel("Sword Art Online", "SAO");

            AudiobookTitleMatcher.Matches("Sword Art Online [M4B]", vol1, author).Should().BeFalse();
            AudiobookTitleMatcher.Matches("Sword Art Online - Something Else [M4B]", vol1, author).Should().BeFalse();
            AudiobookTitleMatcher.Matches("SAO [M4B]", Volume(1, null, "SAO"), author).Should().BeFalse();
        }

        [Test]
        public void should_not_match_a_different_volume_that_extends_the_product_name()
        {
            // Vol 2's release while Vol 2 has no keys yet: "Early Years II ... Book 2" and
            // "Early Years 2 ... 2" carry Vol 1's product name and the series but ARE neither.
            var tbate1 = Volume(1, "Early Years", "Early Years");
            var author = LightNovel("The Beginning After the End");

            AudiobookTitleMatcher.Matches("Early Years II - The Beginning After the End Book 2 (Unabridged)", tbate1, author).Should().BeFalse();
            AudiobookTitleMatcher.Matches("Early Years 2 - The Beginning After the End 2 (Unabridged)", tbate1, author).Should().BeFalse();

            // A parenthetical year is dropped in one reading, so Vol 1's own release still matches.
            AudiobookTitleMatcher.Matches("Early Years - The Beginning After the End Book 1 (2023)", tbate1, author).Should().BeTrue();
        }

        [Test]
        public void should_not_match_when_nothing_is_stored()
        {
            var bare = Volume(5, null, null);

            AudiobookTitleMatcher.Matches("Some Series 5 - Anything", bare, LightNovel("Some Series")).Should().BeFalse();
            AudiobookTitleMatcher.Matches(null, Sao21(), LightNovel("Sword Art Online")).Should().BeFalse();
        }
    }
}
