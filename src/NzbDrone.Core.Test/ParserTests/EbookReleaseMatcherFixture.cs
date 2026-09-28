using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    // Light-novel ebook (2026-09-20, T2): usenet names an ebook "<Writer> - [<Series> NN] - <Title>
    // (epub)" / "<Writer>.-.<Series>-.<Subtitle>.NN.(epub)", so the series-name attribution refuses
    // it (its "author" is the real writer) and the volume bridges never fire (no v/Vol token). The
    // matcher names the ONE searched volume the release is: the writer is stripped, a bracket group
    // that names the series is unwrapped, and what is left must EQUAL one of that volume's keys --
    // never a containment, never a score.
    [TestFixture]
    public class EbookReleaseMatcherFixture : CoreTest
    {
        private Author _sao;
        private Author _saoNoWriter;
        private Author _saoManga;
        private Author _tbate;
        private Author _konosuba;

        private List<Book> _saoVolumes;
        private List<Book> _tbateVolumes;
        private List<Book> _konosubaVolumes;

        [SetUp]
        public void Setup()
        {
            _sao = LightNovel("Sword Art Online", "Reki Kawahara", "SAO");
            _saoNoWriter = LightNovel("Sword Art Online", null, "SAO");
            _saoManga = new Author
            {
                Metadata = new AuthorMetadata { Name = "Sword Art Online", Writer = "Reki Kawahara", ForeignAuthorId = "local-sword-art-online", Aliases = new List<string>() }
            };

            _tbate = LightNovel("The Beginning After the End", "Turtleme");
            _konosuba = LightNovel("KonoSuba: God's Blessing on this Wonderful World!", "Natsume Akatsuki", "Konosuba");

            // The live shape (2026-09-20): SAO 21-24 carry a subtitle, 25-28 none. 2.5 is the shape
            // of a side-story volume: normalised, "2.5" and "25" are the same string.
            _saoVolumes = new List<Book>
            {
                Volume("Sword Art Online", 2, "Aincrad"),
                Volume("Sword Art Online", 2.5, "Early and Late"),
                Volume("Sword Art Online", 21, "Unital Ring I"),
                Volume("Sword Art Online", 22, "Kiss and Fly"),
                Volume("Sword Art Online", 25, null)
            };

            // TBATE's stored subtitle is the junk "<Series>, No. N"; the real product name only
            // lives on the Audio edition.
            _tbateVolumes = new List<Book>
            {
                Volume("The Beginning After the End", 1, "The Beginning After The End, No. 1", "Early Years"),
                Volume("The Beginning After the End", 2, "The Beginning After The End, No. 2", "New Heights")
            };

            _konosubaVolumes = new List<Book>
            {
                Volume("KonoSuba", 7, "110-Million Bride"),
                Volume("KonoSuba", 15, "Cult Syndrome"),
                Volume("KonoSuba", 16, null)
            };
        }

        private static Author LightNovel(string name, string writer, params string[] aliases)
        {
            return new Author
            {
                Metadata = new AuthorMetadata
                {
                    Name = name,
                    Writer = writer,
                    Aliases = new List<string>(aliases),
                    ForeignAuthorId = "local-" + name.ToLowerInvariant().Replace(' ', '-') + "~ln"
                }
            };
        }

        private static Book Volume(string series, double number, string subtitle, string audiobookTitle = null)
        {
            var book = new Book
            {
                Id = (int)(number * 10),
                Title = $"{series} Vol. {number}",
                VolumeNumber = number,
                Subtitle = subtitle
            };

            book.Editions = new List<Edition>
            {
                new Edition { BookId = book.Id, MediaType = MediaType.Ebook, Title = book.Title, Monitored = true },
                new Edition { BookId = book.Id, MediaType = MediaType.Audio, Title = book.Title, AudiobookTitle = audiobookTitle, Monitored = true }
            };

            return book;
        }

        // TitleFold (2026-09-24): an accented series and a numeric-symbol series key like their plain
        // spellings, so the prefix rule reads the volume after the WHOLE name. Before the fold
        // "Ranma ½" keyed ranma, and "Ranma 1/2 3" read as ranma123 matched no volume.
        [TestCase("Rumiko Takahashi - Ranma 1/2 3 (epub)", 3)]
        [TestCase("Rumiko Takahashi - Ranma \u00bd 3 (epub)", 3)]
        [TestCase("Rumiko Takahashi - Ranma 1/2 12 (epub)", 12)]
        [TestCase("Rumiko Takahashi - Ranma 1/2 1 (epub)", 1)]
        public void a_numeric_symbol_series_reads_the_volume_after_the_whole_name(string title, int expected)
        {
            var ranma = LightNovel("Ranma \u00bd", "Rumiko Takahashi", "ranma");
            var volumes = new List<Book> { Volume("Ranma \u00bd", 1, null), Volume("Ranma \u00bd", 3, null), Volume("Ranma \u00bd", 12, null) };

            EbookReleaseMatcher.Match(title, volumes, ranma).VolumeNumber.Should().Be(expected);
        }

        // The writer strip compares folded forms on both sides (review, 2026-09-24): an accented
        // credit, an accented stored writer, or both, still strip, so rule 5 reads the volume.
        [TestCase("Sh\u014dgo Kinugasa", "Sh\u014dgo Kinugasa - Classroom of the Elite 5 (epub)")]
        [TestCase("Sh\u014dgo Kinugasa", "Shogo Kinugasa - Classroom of the Elite 5 (epub)")]
        [TestCase("Shogo Kinugasa", "Sh\u014dgo Kinugasa - Classroom of the Elite 5 (epub)")]
        public void an_accented_writer_credit_is_stripped_either_way(string writer, string title)
        {
            var cote = LightNovel("Classroom of the Elite", writer);
            var volumes = new List<Book> { Volume("Classroom of the Elite", 5, null), Volume("Classroom of the Elite", 6, null) };

            EbookReleaseMatcher.Match(title, volumes, cote).VolumeNumber.Should().Be(5);
        }

        [TestCase("Yuu Watase - Fushigi Yugi 3 (epub)")]
        [TestCase("Yuu Watase - Fushigi Y\u00fbgi 3 (epub)")]
        [TestCase("Yuu.Watase.-.Fushigi.Yugi-.Genbu.Kaiden.3.(epub)")]
        public void an_accented_series_matches_its_plain_spelling(string title)
        {
            var yugi = LightNovel("Fushigi Y\u00fbgi", "Yuu Watase");
            var volumes = new List<Book> { Volume("Fushigi Y\u00fbgi", 3, "Genbu Kaiden"), Volume("Fushigi Y\u00fbgi", 4, null) };

            EbookReleaseMatcher.Match(title, volumes, yugi).VolumeNumber.Should().Be(3);
        }

        [Test]
        public void should_match_the_dotted_writer_series_subtitle_volume_shape()
        {
            EbookReleaseMatcher.Match("Reki.Kawahara.-.Sword.Art.Online-.Unital.Ring.I.21.(epub)", _saoVolumes, _sao)
                .VolumeNumber.Should().Be(21);
        }

        [Test]
        public void should_match_a_bare_volume_with_no_stored_subtitle()
        {
            EbookReleaseMatcher.Match("Reki Kawahara - Sword Art Online 25 (retail) (epub)", _saoVolumes, _sao)
                .VolumeNumber.Should().Be(25);
        }

        // Rule 5: the writer was stripped and what follows starts with "<Series> <N>" for exactly
        // one searched volume, so the remainder does not have to be a subtitle we know -- SAO 25-28
        // have none stored.
        [Test]
        public void should_match_an_unknown_subtitle_by_the_writer_anchored_prefix()
        {
            EbookReleaseMatcher.Match("Reki Kawahara - Sword Art Online 25 - Unital Ring IV (epub)", _saoVolumes, _sao)
                .VolumeNumber.Should().Be(25);
        }

        // "Sword Art Online Progressive" is its own entry: the prefix rule takes the series name
        // plus the number, so a sibling that EXTENDS our name with more words can never match it.
        [Test]
        public void prefix_rule_must_not_take_a_sibling_series()
        {
            EbookReleaseMatcher.Match("Reki Kawahara - Sword Art Online Progressive 25 - Barcarolle of Froth (epub)", _saoVolumes, _sao)
                .Should().BeNull();
        }

        // Rule 5's second bare number: "Series 2 - Side Story 3" names a side story, not volume 2.
        [Test]
        public void prefix_rule_refuses_a_remainder_carrying_another_number()
        {
            EbookReleaseMatcher.Match("Reki Kawahara - Sword Art Online 2 - Side Story 3 (epub)", _saoVolumes, _sao)
                .Should().BeNull();
        }

        // The Audio edition's product name is the only real title volume 2 has: its stored subtitle
        // is the junk "<Series>, No. 2", which is skipped.
        [Test]
        public void should_match_a_bracketed_series_by_the_audiobook_title()
        {
            EbookReleaseMatcher.Match("TurtleMe - [The Beginning After the End 02] - New Heights (epub)", _tbateVolumes, _tbate)
                .VolumeNumber.Should().Be(2);
        }

        [Test]
        public void should_match_a_bracketed_series_and_a_zero_padded_volume()
        {
            EbookReleaseMatcher.Match("TurtleMe - [The Beginning After the End 02] (epub)", _tbateVolumes, _tbate)
                .VolumeNumber.Should().Be(2);
        }

        [Test]
        public void should_match_the_bracketed_dotted_konosuba_shape()
        {
            EbookReleaseMatcher.Match("Natsume.Akatsuki.-.[Konosuba-.Gods.Blessing.on.This.Wonderful.World.07].-.110-Million.Bride.(epub)", _konosubaVolumes, _konosuba)
                .VolumeNumber.Should().Be(7);
        }

        [Test]
        public void should_match_a_spelled_out_series_and_subtitle()
        {
            EbookReleaseMatcher.Match("Natsume Akatsuki - KonoSuba: God's Blessing on this Wonderful World! 15 - Cult Syndrome (epub)", _konosubaVolumes, _konosuba)
                .VolumeNumber.Should().Be(15);
        }

        // A different series that merely shares words: neither the writer, the bracketed series nor
        // the title is ours.
        [Test]
        public void should_not_match_a_stranger_series()
        {
            EbookReleaseMatcher.Match("Amy Plum - [After the End 02] - Until the Beginning (retail) (epub)", _tbateVolumes, _tbate)
                .Should().BeNull();
        }

        // A stranger whose book is literally named after our volume's audiobook title.
        [Test]
        public void should_not_match_a_stranger_sharing_a_product_name()
        {
            EbookReleaseMatcher.Match("Denise Hunter - New Heights 01 - Mending Place", _tbateVolumes, _tbate)
                .Should().BeNull();
        }

        // Rule 6: a range is a pack, the batch bridge's business.
        [TestCase("Sword Art Online v01-06 [Yen Press]")]
        [TestCase("Reki Kawahara - Sword Art Online 21-22 (epub)")]
        public void should_not_match_a_range(string title)
        {
            EbookReleaseMatcher.Match(title, _saoVolumes, _sao).Should().BeNull();
        }

        [Test]
        public void should_not_match_when_two_searched_volumes_share_a_key()
        {
            _saoVolumes.Single(b => b.VolumeNumber == 22).Subtitle = "Unital Ring I";

            EbookReleaseMatcher.Match("Reki Kawahara - Unital Ring I (epub)", _saoVolumes, _sao).Should().BeNull();
        }

        // Rule 2: no writer, no strip -- the exact keys still match, the prefix rule cannot.
        [Test]
        public void should_still_match_an_exact_key_without_a_writer()
        {
            EbookReleaseMatcher.Match("Sword Art Online 21 - Unital Ring I (epub)", _saoVolumes, _saoNoWriter)
                .VolumeNumber.Should().Be(21);
        }

        [Test]
        public void prefix_rule_needs_a_stripped_writer()
        {
            EbookReleaseMatcher.Match("Reki Kawahara - Sword Art Online 25 - Unital Ring IV (epub)", _saoVolumes, _saoNoWriter)
                .Should().BeNull();
        }

        // Rule 1: manga never enters (the parser gates on the library and Match checks it again).
        [Test]
        public void manga_entry_never_matches()
        {
            EbookReleaseMatcher.Match("Reki.Kawahara.-.Sword.Art.Online-.Unital.Ring.I.21.(epub)", _saoVolumes, _saoManga)
                .Should().BeNull();
        }

        [Test]
        public void should_not_match_an_unsearched_volume()
        {
            EbookReleaseMatcher.Match("Reki Kawahara - Sword Art Online 23 - Unital Ring II (epub)", _saoVolumes, _sao)
                .Should().BeNull();
        }

        // Fractional volumes (2026-09-20, review fix): normalising drops the dot, so "2.5" and "25"
        // are one string. A side story therefore keys nothing and satisfies no prefix -- otherwise
        // it would double-hit its integer twin (volume 25 would never bridge) or take it outright.
        // A release naming one bridges nothing either; side stories keep the volume-token path.
        [TestCase("Reki Kawahara - Sword Art Online 25 (epub)", 25)]
        [TestCase("Reki Kawahara - Sword Art Online 2 (epub)", 2)]
        public void a_side_story_volume_never_shadows_its_integer_twin(string title, int expected)
        {
            EbookReleaseMatcher.Match(title, _saoVolumes, _sao).VolumeNumber.Should().Be(expected);
        }

        [Test]
        public void a_release_naming_a_fractional_volume_never_bridges()
        {
            EbookReleaseMatcher.Match("Reki Kawahara - Sword Art Online 2.5 (epub)", _saoVolumes, _sao)
                .Should().BeNull();
        }

        // Final review M2 (2026-09-20): the fractional refusal used to read the raw release, where
        // a scene size tag or a repack marker carries a dot between digits that is not a volume at
        // all. It now reads each reading of the NAME, with the bracket tags off it.
        [TestCase("Reki Kawahara - Sword Art Online 21 [8.5 MB] (epub)", 21)]
        [TestCase("Reki Kawahara - Sword Art Online 25 (8.5 MB) (epub)", 25)]
        public void a_size_tag_is_not_a_fractional_volume(string title, int expected)
        {
            EbookReleaseMatcher.Match(title, _saoVolumes, _sao).VolumeNumber.Should().Be(expected);
        }

        // The scene-dotted spelling of "... 21 2024 Retail": "21.2024" is a volume and a year, not
        // a fraction, so the narrowed refusal lets it through -- but the release still bridges
        // nothing, because rule 5 refuses a remainder carrying a second number ("2024"). The two
        // spellings agree, which is the point: the dot changes nothing.
        [TestCase("Reki.Kawahara.-.Sword.Art.Online.21.2024.Retail.(epub)")]
        [TestCase("Reki Kawahara - Sword Art Online 21 2024 Retail (epub)")]
        public void a_year_after_the_volume_is_not_a_fractional_volume(string title)
        {
            EbookReleaseMatcher.Match(title, _saoVolumes, _sao).Should().BeNull();
        }

        // A release may name the series by a curated alias ("Konosuba"): every alias spells the same
        // keys, so it matches exactly -- with a writer credit in front of it or without one.
        [TestCase("Natsume Akatsuki - Konosuba 15 - Cult Syndrome (epub)")]
        [TestCase("Konosuba 15 - Cult Syndrome (epub)")]
        public void should_match_a_release_naming_an_alias(string title)
        {
            EbookReleaseMatcher.Match(title, _konosubaVolumes, _konosuba).VolumeNumber.Should().Be(15);
        }

        [Test]
        public void strip_writer_returns_what_follows_the_writer_and_its_separator()
        {
            EbookReleaseMatcher.StripWriter("Reki.Kawahara.-.Sword.Art.Online-.Unital.Ring.I.21.(epub)", _sao)
                .Should().StartWith("Sword Art Online");
        }

        [Test]
        public void strip_writer_accepts_the_last_first_spelling()
        {
            EbookReleaseMatcher.StripWriter("Kawahara, Reki - Sword Art Online 25 (epub)", _sao)
                .Should().StartWith("Sword Art Online");
        }

        [TestCase("Amy Plum - Until the Beginning (epub)")]
        [TestCase("Reki Kawahara Sword Art Online 21 (epub)")]
        public void strip_writer_leaves_a_title_it_does_not_head(string title)
        {
            EbookReleaseMatcher.StripWriter(title, _sao).Should().Be(title);
        }

        [Test]
        public void strip_writer_leaves_a_title_when_the_entry_has_no_writer()
        {
            EbookReleaseMatcher.StripWriter("Reki Kawahara - Sword Art Online 25 (epub)", _saoNoWriter)
                .Should().Be("Reki Kawahara - Sword Art Online 25 (epub)");
        }

        [Test]
        public void unwrap_series_brackets_keeps_the_series_group_and_drops_the_rest()
        {
            var unwrapped = EbookReleaseMatcher.UnwrapSeriesBrackets(
                "TurtleMe - [The Beginning After the End 02] - New Heights [Yen On]",
                new[] { "thebeginningaftertheend" });

            unwrapped.Should().Contain("The Beginning After the End 02");
            unwrapped.Should().NotContain("Yen On");
            unwrapped.Should().NotContain("[");
        }

        [Test]
        public void match_ignores_a_blank_title_and_a_null_author()
        {
            EbookReleaseMatcher.Match(" ", _saoVolumes, _sao).Should().BeNull();
            EbookReleaseMatcher.Match("Reki Kawahara - Sword Art Online 25 (epub)", _saoVolumes, null).Should().BeNull();
            EbookReleaseMatcher.Match("Reki Kawahara - Sword Art Online 25 (epub)", null, _sao).Should().BeNull();
        }
    }
}
