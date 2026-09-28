using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Test.BookTests
{
    // One display title (2026-09-23, the maintainer): the pure formatting the maintainer chose, shared by calibre's
    // Title/sort and Audiobookshelf's title.
    [TestFixture]
    public class LightNovelTitlesFixture
    {
        [TestCase("Mushoku Tensei: Jobless Reincarnation", 14, null, "Mushoku Tensei: Jobless Reincarnation (Vol. 14)")]
        [TestCase("Mushoku Tensei: Jobless Reincarnation", 14, "", "Mushoku Tensei: Jobless Reincarnation (Vol. 14)")]
        [TestCase("Mushoku Tensei: Jobless Reincarnation", 14, "   ", "Mushoku Tensei: Jobless Reincarnation (Vol. 14)")]
        [TestCase("Sword Art Online", 1, "Aincrad", "Sword Art Online: Aincrad (Vol. 1)")]
        [TestCase("Classroom of the Elite", 11.5, null, "Classroom of the Elite (Vol. 11.5)")]
        [TestCase("Sword Art Online", 1, "  Aincrad  ", "Sword Art Online: Aincrad (Vol. 1)")]
        [TestCase("Mushoku Tensei: Jobless Reincarnation", 14, "Boy Meets Girl, Compulsively", "Mushoku Tensei: Jobless Reincarnation: Boy Meets Girl, Compulsively (Vol. 14)")]
        // Defence in depth (fix round 3, 2026-09-24): a Book.Subtitle stored before Subtitles.IsJunk
        // existed must not reach the display title even if a refresh hasn't cleared it yet -- the
        // three live-finding examples that reached calibre/ABS as junk before this fix.
        [TestCase("Mushoku Tensei: Jobless Reincarnation", 1, "Jobless Reincarnation (Light Novel), Vol. 1", "Mushoku Tensei: Jobless Reincarnation (Vol. 1)")]
        [TestCase("Classroom of the Elite", 5, "Light Novel, Vol. 5", "Classroom of the Elite (Vol. 5)")]
        [TestCase("Classroom of the Elite", 10, "Light Novel (Classroom of the Elite, Book 26)", "Classroom of the Elite (Vol. 10)")]
        // Full-title subtitles (2026-09-24): a subtitle that is the volume's whole title, opening with
        // the series name, renders as that title -- never "<series>: <series> of ...".
        [TestCase("Rascal Does Not Dream", 1, "Rascal Does Not Dream of Bunny Girl Senpai", "Rascal Does Not Dream of Bunny Girl Senpai (Vol. 1)")]
        [TestCase("Rascal Does Not Dream", 2, "Rascal Does Not Dream of Petite Devil Kohai", "Rascal Does Not Dream of Petite Devil Kohai (Vol. 2)")]
        [TestCase("Rascal Does Not Dream", 15, "Rascal Does Not Dream of a Dear Friend", "Rascal Does Not Dream of a Dear Friend (Vol. 15)")]
        [TestCase("Rascal Does Not Dream", 16, "Rascal Does Not Dream of a Beach Queen +", "Rascal Does Not Dream of a Beach Queen + (Vol. 16)")]
        [TestCase("Fullmetal Alchemist", 1, "Fullmetal Alchemist: The Land of Sand", "Fullmetal Alchemist: The Land of Sand (Vol. 1)")]
        [TestCase("Rascal Does Not Dream", 2, "Rascal Does Not Dream 2", "Rascal Does Not Dream (Vol. 2)")]
        // "<series>: X" subtitles (2026-09-24): Derive stores "Days"; a stored "Tokyo Ghoul: Days" shows the same.
        [TestCase("Tokyo Ghoul", 1, "Days", "Tokyo Ghoul: Days (Vol. 1)")]
        [TestCase("Tokyo Ghoul", 1, "Tokyo Ghoul: Days", "Tokyo Ghoul: Days (Vol. 1)")]
        [TestCase("Magical Girl Raising Project", 13, "Black", "Magical Girl Raising Project: Black (Vol. 13)")]
        public void display_formats_series_subtitle_and_volume(string series, double volumeNumber, string subtitle, string expected)
        {
            LightNovelTitles.Display(series, volumeNumber, subtitle).Should().Be(expected);
        }

        [TestCase("Sword Art Online", 1, "Sword Art Online 0001")]
        [TestCase("Rascal Does Not Dream", 2, "Rascal Does Not Dream 0002")]
        [TestCase("Classroom of the Elite", 11.5, "Classroom of the Elite 0011.5")]
        [TestCase("Overlord", 100, "Overlord 0100")]
        [TestCase("Overlord", 10000, "Overlord 10000")]
        public void sort_title_pads_the_integer_part_to_four_digits_and_keeps_any_fraction(string series, double volumeNumber, string expected)
        {
            LightNovelTitles.SortTitle(series, volumeNumber).Should().Be(expected);
        }

        [Test]
        public void series_of_is_the_entrys_own_name()
        {
            var author = new Author { Name = "Mushoku Tensei: Jobless Reincarnation" };

            LightNovelTitles.SeriesOf(author).Should().Be("Mushoku Tensei: Jobless Reincarnation");
        }

        // UI pass (2026-09-24, V1): the volume page shows the same display title calibre and
        // Audiobookshelf carry -- for a light-novel volume only; a manga volume keeps its own title.
        [Test]
        public void display_of_a_light_novel_volume_is_the_display_title()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata { Name = "Sword Art Online", ForeignAuthorId = "local-sword-art-online~ln" }
            };

            LightNovelTitles.DisplayOf(author, new Book { VolumeNumber = 1, Subtitle = "Aincrad" }).Should().Be("Sword Art Online: Aincrad (Vol. 1)");
        }

        // Full-title subtitles (2026-09-24): the API's displaySubtitle -- the subtitle the display
        // title shows, for the series page row ("Vol. 2: <this>").
        [Test]
        public void display_subtitle_of_a_light_novel_volume_is_the_shown_subtitle()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata { Name = "Rascal Does Not Dream", ForeignAuthorId = "local-rascal-does-not-dream~ln" }
            };

            LightNovelTitles.DisplaySubtitleOf(author, new Book { VolumeNumber = 2, Subtitle = " Rascal Does Not Dream of Petite Devil Kohai " })
                .Should().Be("Rascal Does Not Dream of Petite Devil Kohai");
        }

        // Review fix (2026-09-24): BookResource.DisplaySubtitle carries this value verbatim. A light-
        // novel volume with no (or a junk) subtitle is "" -- not null, which the API drops and the
        // frontend store's merge would read as "unchanged", leaving a cleared subtitle on screen.
        [Test]
        public void display_subtitle_of_a_light_novel_volume_without_a_subtitle_is_empty_not_null()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata { Name = "Rascal Does Not Dream", ForeignAuthorId = "local-rascal-does-not-dream~ln" }
            };

            LightNovelTitles.DisplaySubtitleOf(author, new Book { VolumeNumber = 1, Subtitle = "Light Novel, Vol. 1" }).Should().Be(string.Empty);
            LightNovelTitles.DisplaySubtitleOf(author, new Book { VolumeNumber = 1 }).Should().Be(string.Empty);
            LightNovelTitles.DisplaySubtitleOf(author, new Book { VolumeNumber = 1, Subtitle = "   " }).Should().Be(string.Empty);
        }

        [Test]
        public void display_subtitle_of_a_stored_series_colon_subtitle_is_the_remainder()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata { Name = "Tokyo Ghoul", ForeignAuthorId = "local-tokyo-ghoul~ln" }
            };

            LightNovelTitles.DisplaySubtitleOf(author, new Book { VolumeNumber = 1, Subtitle = "Tokyo Ghoul: Days" }).Should().Be("Days");
        }

        [Test]
        public void display_subtitle_of_a_manga_volume_or_without_an_author_is_null()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata { Name = "Berserk", ForeignAuthorId = "local-berserk" }
            };

            LightNovelTitles.DisplaySubtitleOf(author, new Book { VolumeNumber = 1, Subtitle = "The Black Swordsman" }).Should().BeNull();
            LightNovelTitles.DisplaySubtitleOf(null, new Book { VolumeNumber = 1, Subtitle = "Aincrad" }).Should().BeNull();
        }

        [Test]
        public void display_of_a_manga_volume_is_null()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata { Name = "Berserk", ForeignAuthorId = "local-berserk" }
            };

            LightNovelTitles.DisplayOf(author, new Book { VolumeNumber = 1 }).Should().BeNull();
        }

        [Test]
        public void display_of_without_an_author_is_null()
        {
            LightNovelTitles.DisplayOf(null, new Book { VolumeNumber = 1 }).Should().BeNull();
        }
    }
}
