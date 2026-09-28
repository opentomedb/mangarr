using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    // Manga search bridge: scene releases ("Series vN (Year) (Group)") have no author token and
    // can't fuzzy-match the edition title "Series Vol. N". When the release's own series matches
    // the searched author, ParseBookTitleWithSearchCriteria must attribute it and parse the volume.
    [TestFixture]
    public class ParseMangaSearchCriteriaFixture : CoreTest
    {
        private static Author Author(string name)
        {
            return new Author { Name = name };
        }

        private static Author AuthorWithAliases(string name, params string[] aliases)
        {
            return new Author { Metadata = new NzbDrone.Core.Books.AuthorMetadata { Name = name, Aliases = new List<string>(aliases) } };
        }

        private static Author LightNovel(string name)
        {
            return new Author { Metadata = new NzbDrone.Core.Books.AuthorMetadata { Name = name, ForeignAuthorId = "local-" + name.ToLowerInvariant().Replace(' ', '-') + "~ln" } };
        }

        // A tokenless batch of a LIGHT NOVEL grades by library: EPUB unless the title says
        // audiobook (Unknown Audio) or manga (CBZ, which the media-type spec refuses for a light
        // novel — D1). The manga default (CBZ) is untouched. Titles keep the bridge's shape
        // (series + trailing bracket groups only) -- anything else never bridges.
        [TestCase("Overlord [Yen On] [Stick]", "Overlord", "EPUB")]
        [TestCase("Overlord (Light Novel) [Stick]", "Overlord", "EPUB")]
        [TestCase("Overlord [Audiobook] [Troglodyte]", "Overlord", "Unknown Audio")]
        [TestCase("Overlord (Unabridged) [Troglodyte]", "Overlord", "Unknown Audio")]
        [TestCase("The Eminence in Shadow [Manga] [Digital]", "The Eminence in Shadow", "CBZ")]
        [TestCase("The Eminence in Shadow [Yen Press] [Stick]", "The Eminence in Shadow", "EPUB")]
        public void should_grade_tokenless_light_novel_batch_by_library(string title, string authorName, string expectedQuality)
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(title, LightNovel(authorName), new List<Book>());

            result.Should().NotBeNull();
            result.VolumeStart.Should().Be(1);
            result.Quality.Quality.Name.Should().Be(expectedQuality);
        }

        // 2026-09-21: a light-novel EPUB batch naming the series, an edition label and a bare range.
        [TestCase("[Synthworks] Rascal Does Not Dream English Light Novels 1-15", "Rascal Does Not Dream", 1, 15)]
        [TestCase("Rascal Does Not Dream Light Novels 1-15 [EPUB]", "Rascal Does Not Dream", 1, 15)]
        [TestCase("Overlord LN 1-16", "Overlord", 1, 16)]
        public void light_novel_epub_batch_with_a_bare_range_bridges_as_an_epub_pack(string title, string authorName, int start, int end)
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(title, LightNovel(authorName), new List<Book>(), MediaType.Ebook);

            result.Should().NotBeNull();
            result.AuthorName.Should().Be(authorName);
            result.VolumeStart.Should().Be(start);
            result.VolumeEnd.Should().Be(end);
            result.Quality.Quality.Should().Be(Quality.EPUB);
        }

        [Test]
        public void the_bare_range_bridge_never_fires_for_an_audio_search_or_a_manga_entry()
        {
            Parser.Parser.ParseBookTitleWithSearchCriteria("[Synthworks] Rascal Does Not Dream English Light Novels 1-15", LightNovel("Rascal Does Not Dream"), new List<Book>(), MediaType.Audio)
                .Should().BeNull();
            Parser.Parser.ParseBookTitleWithSearchCriteria("[Synthworks] Rascal Does Not Dream English Light Novels 1-15", Author("Rascal Does Not Dream"), new List<Book>())
                .Should().BeNull();
        }

        // The same audiobook-worded batch searched for a MANGA entry keeps the manga default.
        [Test]
        public void tokenless_manga_batch_with_audiobook_wording_still_grades_cbz()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria("Overlord (Unabridged) [Troglodyte]", Author("Overlord"), new List<Book>());

            result.Should().NotBeNull();
            result.Quality.Quality.Should().Be(Quality.CBZ);
        }

        [TestCase("Jujutsu Kaisen v25 (2025) (Digital) (LuCaZ)", "Jujutsu Kaisen", 25)]
        [TestCase("Jujutsu.Kaisen.v20.(2023).(Digital).(LuCaZ).(comic)", "Jujutsu Kaisen", 20)]
        [TestCase("Chainsaw Man v13 (2023) (Digital) (1r0n)", "Chainsaw Man", 13)]
        [TestCase("Demon Slayer Vol. 23 (2021) (Digital)", "Demon Slayer", 23)]
        [TestCase("Seven.Seas.Entertainment-Mushoku.Tensei.Jobless.Reincarnation.Vol.05.2017.Hybrid.Comic.eBook-BitBook", "Mushoku Tensei: Jobless Reincarnation", 5)]
        [TestCase("VIZ.Media-Jujutsu.Kaisen.Vol.20.2023.Hybrid.Comic.eBook-BitBook", "Jujutsu Kaisen", 20)]
        [TestCase("VIZ.Media.Jujutsu.Kaisen.Vol.30.2026.HYBRiD.MANGA.eBook-PNLS", "Jujutsu Kaisen", 30)]
        [TestCase("Seven.Seas.Entertainment.Mushoku.Tensei.Jobless.Reincarnation.Vol.05.2017.Hybrid.Comic.eBook-BitBook", "Mushoku Tensei: Jobless Reincarnation", 5)]
        [TestCase("Kodansha.Comics.Chainsaw.Man.Vol.13.2023.HYBRiD.MANGA.eBook-BKS", "Chainsaw Man", 13)]
        public void should_bridge_scene_volume_to_searched_author(string title, string authorName, int expectedVolume)
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(title, Author(authorName), new List<Book>());

            result.Should().NotBeNull();
            result.AuthorName.Should().Be(authorName);
            result.VolumeNumber.Should().Be(expectedVolume);
        }

        // A whole-series pack ("Series Volumes 1 to 13") must bridge too: attributed to the
        // searched author with VolumeStart/VolumeEnd set, so GetBooks fans it out to every volume.
        [TestCase("Dandadan Volumes 1 to 13 (2023) (Digital)", "Dandadan", 1, 13)]
        [TestCase("Chainsaw Man Vol. 1-11 [CBZ]", "Chainsaw Man", 1, 11)]
        [TestCase("Spy x Family v01-v12 Complete", "Spy x Family", 1, 12)]
        public void should_bridge_whole_series_pack_to_searched_author(string title, string authorName, int start, int end)
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(title, Author(authorName), new List<Book>());

            result.Should().NotBeNull();
            result.AuthorName.Should().Be(authorName);
            result.VolumeStart.Should().Be(start);
            result.VolumeEnd.Should().Be(end);
            result.VolumeNumber.Should().BeNull();
        }

        // The pack must survive the quality profile: a pack title carries no CBZ keyword, so the
        // quality parser must still default it to CBZ (Unknown would be rejected by the profile).
        [Test]
        public void should_default_whole_series_pack_to_cbz()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Dandadan Volumes 1 to 13 (2023) (Digital)",
                Author("Dandadan"),
                new List<Book>());

            result.Should().NotBeNull();
            result.Quality.Quality.Should().Be(Quality.CBZ);
        }

        [Test]
        public void should_bridge_when_searched_series_has_format_suffix_in_release()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Mushoku Tensei Jobless Reincarnation (Light Novel) v21 (2023)",
                Author("Mushoku Tensei Jobless Reincarnation"),
                new List<Book>());

            result.Should().NotBeNull();
            result.AuthorName.Should().Be("Mushoku Tensei Jobless Reincarnation");
            result.VolumeNumber.Should().Be(21);
        }

        [Test]
        public void should_not_bridge_when_leading_words_are_not_a_known_publisher()
        {
            // Only a known publisher prefix may be shaved from the front of the parsed series:
            // "Shin Tokyo Ghoul" would be a DIFFERENT series, not "Tokyo Ghoul" published by "Shin".
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Shin.Tokyo.Ghoul.Vol.03.2026.HYBRiD.MANGA.eBook-PNLS",
                Author("Tokyo Ghoul"),
                new List<Book>());

            result.Should().BeNull();
        }

        [Test]
        public void should_not_bridge_when_series_does_not_match_searched_author()
        {
            // A release for a different series must not be attributed to the searched author, even
            // when the searched series has real monitored books to fuzzy-match against.
            var books = new List<Book>
            {
                new Book
                {
                    Title = "Chainsaw Man Vol. 1",
                    Editions = new List<Edition> { new Edition { Title = "Chainsaw Man Vol. 1", Monitored = true } }
                }
            };

            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "One Piece v105 (2024) (Digital)",
                Author("Chainsaw Man"),
                books);

            result.Should().BeNull();
        }

        [Test]
        public void should_not_bridge_sequel_series_whose_name_extends_the_author()
        {
            // "Tokyo Ghoul:re" is a DIFFERENT series from "Tokyo Ghoul". The release's parsed series
            // ("Tokyo Ghoul - re") is the searched author plus extra tokens, so it must be rejected —
            // the fuzzy fallback would otherwise prefix-match "Tokyo Ghoul" inside it and grab the
            // wrong series. A monitored Vol. 1 is provided so a fuzzy fall-through could have matched.
            var books = new List<Book>
            {
                new Book
                {
                    Title = "Tokyo Ghoul Vol. 1",
                    Editions = new List<Edition> { new Edition { Title = "Tokyo Ghoul Vol. 1", Monitored = true } }
                }
            };

            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Tokyo Ghoul - re v01 (2017) (Digital) (danke-Empire)",
                Author("Tokyo Ghoul"),
                books);

            result.Should().BeNull();
        }

        // Token-first naming ("Vol 7 Dan da Dan", "Book 5 <series>") puts the series AFTER the
        // volume token, so the series must be read from the trailing text and matched exactly.
        [TestCase("Vol 7 Dan da Dan", "Dandadan", 7)]
        [TestCase("Book 5 Dandadan (2024) (Digital)", "Dandadan", 5)]
        [TestCase("vol. 12 Chainsaw Man", "Chainsaw Man", 12)]
        public void should_bridge_token_first_naming(string title, string authorName, int expectedVolume)
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(title, Author(authorName), new List<Book>());

            result.Should().NotBeNull();
            result.AuthorName.Should().Be(authorName);
            result.VolumeNumber.Should().Be(expectedVolume);
        }

        [Test]
        public void should_bridge_token_first_with_by_author_and_alias()
        {
            // "Book 5 Tensura by Fuse": series is the trailing "Tensura by Fuse" -> strip the credit
            // -> "Tensura", which resolves via the stored alias to the full series name.
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Book 5 Tensura by Fuse [ENG / CBZ]",
                AuthorWithAliases("That Time I Got Reincarnated as a Slime", "TenSura", "Tensei Shitara Slime Datta Ken"),
                new List<Book>());

            result.Should().NotBeNull();
            result.AuthorName.Should().Be("That Time I Got Reincarnated as a Slime");
            result.VolumeNumber.Should().Be(5);
        }

        [Test]
        public void should_bridge_abbreviation_alias_in_normal_order()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Tensura v10 (2024) (Digital)",
                AuthorWithAliases("That Time I Got Reincarnated as a Slime", "TenSura"),
                new List<Book>());

            result.Should().NotBeNull();
            result.VolumeNumber.Should().Be(10);
        }

        [Test]
        public void should_not_bridge_token_first_when_series_is_unrelated()
        {
            // Token-first must not become a loophole: an unrelated series after the token is rejected.
            var books = new List<Book>
            {
                new Book
                {
                    Title = "Dandadan Vol. 1",
                    Editions = new List<Edition> { new Edition { Title = "Dandadan Vol. 1", Monitored = true } }
                }
            };

            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Vol 3 One Piece (2024) (Digital)",
                Author("Dandadan"),
                books);

            result.Should().BeNull();
        }

        // Tokenless whole-series batches ("Series [Yen Press] [Stick]", no volume token anywhere)
        // are how a private tracker stocks most series. An exact normalized series match bridges them as a
        // whole-series pack (VolumeStart 1, open-ended end clamped by the library fan-out), so
        // pack decisioning can accept them when they fill missing volumes.
        [TestCase("That Time I Got Reincarnated as a Slime (Digital)", "That Time I Got Reincarnated as a Slime")]
        [TestCase("ReZERO -Starting Life in Another World- The Frozen Bond (2022-2023) (Digital)", "Re:ZERO -Starting Life in Another World-, The Frozen Bond")]
        [TestCase("Mushoku Tensei - Jobless Reincarnation (Digital)", "Mushoku Tensei: Jobless Reincarnation")]
        public void should_bridge_tokenless_series_batch_as_whole_series_pack(string title, string authorName)
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(title, Author(authorName), new List<Book>());

            result.Should().NotBeNull();
            result.AuthorName.Should().Be(authorName);
            result.VolumeStart.Should().Be(1);
            result.VolumeEnd.Should().BeGreaterOrEqualTo(999);
            result.Quality.Quality.Should().Be(Quality.CBZ);
        }

        [Test]
        public void should_not_bridge_tokenless_batch_for_unrelated_series()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "One Piece (Digital)",
                Author("Chainsaw Man"),
                new List<Book>());

            result.Should().BeNull();
        }

        [Test]
        public void should_not_bridge_tokenless_batch_for_sequel_series()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Tokyo Ghoul - re (Digital)",
                Author("Tokyo Ghoul"),
                new List<Book>());

            result.Should().BeNull();
        }

        // The light-novel guard must survive the batch bridge: a batch of the right series carrying
        // only a square-bracketed ebook group tag ([Stick], [LuCaZ]) still bridges, but stays
        // Unknown quality so the manga profile rejects it — the real grabbed LN batches carried no
        // format token at all.
        [TestCase("That Time I Got Reincarnated as a Slime [Yen Press] [Stick]", "That Time I Got Reincarnated as a Slime")]
        [TestCase("Mushoku Tensei - Jobless Reincarnation [Seven Seas] [LuCaZ]", "Mushoku Tensei: Jobless Reincarnation")]
        public void tokenless_batch_with_ebook_signal_should_stay_unknown_quality(string title, string authorName)
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(title, Author(authorName), new List<Book>());

            result.Should().NotBeNull();
            result.Quality.Quality.Should().Be(Quality.Unknown);
        }

        // An explicit EPUB token is not tokenless: the codec regex grades it EPUB before the bridge's
        // Unknown guard runs. The manga profile still rejects it (EPUB is not in its allowed list);
        // the Light Novel EPUB profile accepts it.
        [TestCase("That Time I Got Reincarnated as a Slime [EPUB]", "That Time I Got Reincarnated as a Slime")]
        public void explicit_epub_token_in_tokenless_batch_grades_epub(string title, string authorName)
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(title, Author(authorName), new List<Book>());

            result.Should().NotBeNull();
            result.Quality.Quality.Should().Be(Quality.EPUB);
        }

        [Test]
        public void should_bridge_pack_with_leading_scene_tag()
        {
            // A leading "[Manga]" scene tag must not poison the series name — "[Manga] Tokyo Ghoul
            // (v01-v05)" is a valid Tokyo Ghoul pack and must attribute + fan out to volumes 1-5.
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "[Manga] Tokyo Ghoul (v01-v05) (2015-2016) by Sui Ishida [ENG / CBZ] [VIP]",
                Author("Tokyo Ghoul"),
                new List<Book>());

            result.Should().NotBeNull();
            result.AuthorName.Should().Be("Tokyo Ghoul");
            result.VolumeStart.Should().Be(1);
            result.VolumeEnd.Should().Be(5);
        }
        // A series literally named in brackets ("[Oshi no Ko]") is stripped as if it were a scene
        // tag, which left no series to match: "[Oshi No Ko] v13 (2026) (Digital) (ShelfLife)" was
        // rejected as unparseable while the volume sat on the tracker. The bracket contents are
        // tried as a series candidate too (CJK brackets normalized first).
        [TestCase("[Oshi No Ko] v13 (2026) (Digital) (ShelfLife)", "[Oshi no Ko]", 13)]
        [TestCase("【OSHI NO KO】 v13 (2026) (Digital) (ShelfLife)", "[Oshi no Ko]", 13)]
        [TestCase("[Oshi No Ko] v13 (2026) (Digital) (ShelfLife)", "Oshi no Ko", 13)]
        public void should_bridge_series_named_in_brackets(string title, string authorName, int expectedVolume)
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(title, Author(authorName), new List<Book>());

            result.Should().NotBeNull();
            result.AuthorName.Should().Be(authorName);
            result.VolumeNumber.Should().Be(expectedVolume);
        }

        // A leading scene tag must still not be mistaken for the series of an unrelated release.
        [Test]
        public void should_not_bridge_bracket_tag_of_unrelated_series()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "[danke-Empire] One Piece v105 (2024) (Digital)",
                Author("Chainsaw Man"),
                new List<Book>());

            result.Should().BeNull();
        }

        // Nyaa dual-title headers ("Romaji / English", "Romaji | English") name the series twice;
        // either half must match the searched series, for single volumes and packs alike.
        [TestCase("Tensei Shitara Slime Datta Ken / That Time I Got Reincarnated as a Slime v26 (2025) (Digital)", "That Time I Got Reincarnated as a Slime", 26)]
        [TestCase("Kekkon suru tte, Hontou desu ka | 365 Days to the Wedding v11 (2026) (Digital) (1r0n)", "365 Days to the Wedding", 11)]
        [TestCase("That Time I Got Reincarnated as a Slime / Tensei Shitara Slime Datta Ken v26 (2025) (Digital)", "That Time I Got Reincarnated as a Slime", 26)]
        public void should_bridge_dual_title_single_volume(string title, string authorName, int expectedVolume)
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(title, Author(authorName), new List<Book>());

            result.Should().NotBeNull();
            result.AuthorName.Should().Be(authorName);
            result.VolumeNumber.Should().Be(expectedVolume);
        }

        [TestCase("Tensei Shitara Slime Datta Ken / That Time I Got Reincarnated as a Slime v01-28,125-137 (2017-2025) (Digital)", "That Time I Got Reincarnated as a Slime", 1, 28)]
        [TestCase("Kekkon suru tte, Hontou desu ka | 365 Days to the Wedding v01-10 (2023-2026) (Digital) (1r0n)", "365 Days to the Wedding", 1, 10)]
        public void should_bridge_dual_title_pack(string title, string authorName, int start, int end)
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(title, Author(authorName), new List<Book>());

            result.Should().NotBeNull();
            result.AuthorName.Should().Be(authorName);
            result.VolumeStart.Should().Be(start);
            result.VolumeEnd.Should().Be(end);
        }

        [Test]
        public void should_bridge_dual_title_tokenless_batch()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Tensei Shitara Slime Datta Ken / That Time I Got Reincarnated as a Slime (Digital) (nao + danke-Empire + Slikk)",
                Author("That Time I Got Reincarnated as a Slime"),
                new List<Book>());

            result.Should().NotBeNull();
            result.AuthorName.Should().Be("That Time I Got Reincarnated as a Slime");
            result.VolumeStart.Should().Be(1);
            result.VolumeEnd.Should().BeGreaterOrEqualTo(999);
        }

        // A dual title still cannot smuggle in a different series: neither half matches.
        [Test]
        public void should_not_bridge_dual_title_of_unrelated_series()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Tensura Nikki / The Slime Diaries v01 (2020) (Digital)",
                Author("That Time I Got Reincarnated as a Slime"),
                new List<Book>());

            result.Should().BeNull();
        }

        // Light-novel ebook bridge (2026-09-20, T2): usenet names an ebook after its WRITER
        // ("Reki.Kawahara.-.Sword.Art.Online-.Unital.Ring.I.21.(epub)"), so the series attribution
        // above refuses it and no volume token exists to bridge. EbookReleaseMatcher names the one
        // searched volume the release IS; it runs for a light novel on an EPUB or untyped search
        // only -- an audio search is the audiobook bridge's, and a manga entry never enters.
        private static Author LightNovelWriter(string name, string writer)
        {
            return new Author
            {
                Metadata = new NzbDrone.Core.Books.AuthorMetadata
                {
                    Name = name,
                    Writer = writer,
                    ForeignAuthorId = "local-" + name.ToLowerInvariant().Replace(' ', '-') + "~ln"
                }
            };
        }

        private static List<Book> SaoVolumes()
        {
            return new List<Book>
            {
                SaoVolume(21, "Unital Ring I"),
                SaoVolume(22, "Kiss and Fly")
            };
        }

        private static Book SaoVolume(double number, string subtitle)
        {
            var book = new Book
            {
                Id = (int)number,
                Title = $"Sword Art Online Vol. {number}",
                VolumeNumber = number,
                Subtitle = subtitle
            };

            book.Editions = new List<Edition>
            {
                new Edition { BookId = book.Id, MediaType = MediaType.Ebook, Title = book.Title, Monitored = true }
            };

            return book;
        }

        [Test]
        public void should_bridge_a_writer_prefixed_ebook_release_on_an_ebook_search()
        {
            const string title = "Reki.Kawahara.-.Sword.Art.Online-.Unital.Ring.I.21.(epub)";

            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(title, LightNovelWriter("Sword Art Online", "Reki Kawahara"), SaoVolumes(), MediaType.Ebook);

            result.Should().NotBeNull();
            result.AuthorName.Should().Be("Sword Art Online");
            result.BookTitle.Should().Be("Sword Art Online Vol. 21");
            result.VolumeNumber.Should().Be(21);
            result.ReleaseTitle.Should().Be(title);
            result.Quality.Quality.Should().Be(Quality.EPUB);
        }

        [Test]
        public void should_bridge_a_writer_prefixed_ebook_release_on_an_untyped_search()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "TurtleMe - [The Beginning After the End 02] - New Heights (epub)",
                LightNovelWriter("The Beginning After the End", "Turtleme"),
                new List<Book> { Tbate2() });

            result.Should().NotBeNull();
            result.VolumeNumber.Should().Be(2);
        }

        private static Book Tbate2()
        {
            var book = new Book
            {
                Id = 2,
                Title = "The Beginning After the End Vol. 2",
                VolumeNumber = 2,
                Subtitle = "The Beginning After The End, No. 2"
            };

            book.Editions = new List<Edition>
            {
                new Edition { BookId = book.Id, MediaType = MediaType.Ebook, Title = book.Title, Monitored = true },
                new Edition { BookId = book.Id, MediaType = MediaType.Audio, Title = book.Title, AudiobookTitle = "New Heights", Monitored = true }
            };

            return book;
        }

        [Test]
        public void should_not_bridge_a_writer_prefixed_ebook_release_on_an_audio_search()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Reki.Kawahara.-.Sword.Art.Online-.Unital.Ring.I.21.(epub)",
                LightNovelWriter("Sword Art Online", "Reki Kawahara"),
                SaoVolumes(),
                MediaType.Audio);

            (result?.VolumeNumber).Should().BeNull();
        }

        [Test]
        public void should_not_bridge_a_writer_prefixed_ebook_release_for_a_manga_entry()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Reki.Kawahara.-.Sword.Art.Online-.Unital.Ring.I.21.(epub)",
                Author("Sword Art Online"),
                SaoVolumes(),
                MediaType.Ebook);

            (result?.VolumeNumber).Should().BeNull();
        }

        // Audit regression (2026-09-20): a scanlation-tagged volume whose subtitle ends in a roman
        // numeral must still read as volume 21 -- the leading "[WtF-aNiMe]" tag is not the series
        // and the trailing "I" is part of the subtitle, never a volume.
        [Test]
        public void should_bridge_a_bracket_tagged_volume_with_a_roman_numeral_subtitle()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "[WtF-aNiMe]Sword.Art.Online.v21.-.Unital.Ring.I.[Yen.Press].[LuCaZ].epub-xpost",
                LightNovelWriter("Sword Art Online", "Reki Kawahara"),
                SaoVolumes(),
                MediaType.Ebook);

            result.Should().NotBeNull();
            result.AuthorName.Should().Be("Sword Art Online");
            result.VolumeNumber.Should().Be(21);
        }
    }
}
