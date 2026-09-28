using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // D1 as table tests. Candidates are what AniList's page carries after AniListService maps it;
    // the ranker is pure, so no HTTP here.
    [TestFixture]
    public class AniListRankerFixture : CoreTest
    {
        private const string Manga = "MANGA";
        private const string OneShot = "ONE_SHOT";
        private const string Finished = "FINISHED";
        private const string Releasing = "RELEASING";

        private static AniListCandidate C(int id, string romaji, string english, string format, string status, int? volumes, int popularity, params string[] synonyms)
        {
            return new AniListCandidate
            {
                Id = id,
                TitleRomaji = romaji,
                TitleEnglish = english,
                Format = format,
                Status = status,
                Volumes = volumes,
                Popularity = popularity,
                Synonyms = synonyms.ToList()
            };
        }

        [Test]
        public void a_one_shot_is_rejected_even_with_primary_title_equality()
        {
            // Black Clover: the 2014 Jump NEXT!! pilot (ONE_SHOT, romaji "Black Clover") is rank 0.
            var page = new List<AniListCandidate>
            {
                C(114652, "Black Clover", "Black Clover", OneShot, Finished, 1, 3982),
                C(86123, "Black Clover", "Black Clover", Manga, Releasing, null, 107312)
            };

            var result = AniListRanker.Pick(page, "Black Clover", 37);

            result.Pick.Id.Should().Be(86123);
            result.Via.Should().Be("primary");
            result.Rejections.Should().ContainSingle(r => r == "114652 \"Black Clover\": ONE_SHOT");
        }

        [Test]
        public void primary_title_equality_beats_a_more_popular_synonym_hit()
        {
            // Fairy Tail's shape with the volume rule out of play (no catalogue count): the
            // anthology carries "Fairy Tail" only as a synonym and must lose to the serial.
            var page = new List<AniListCandidate>
            {
                C(128087, "Pantsu Agerune", null, Manga, Finished, 1, 999999, "Fairy Tail", "Kenkyuu tte Muzukashii"),
                C(30598, "FAIRY TAIL", "Fairy Tail", Manga, Finished, 63, 57000)
            };

            var result = AniListRanker.Pick(page, "Fairy Tail", null);

            result.Pick.Id.Should().Be(30598);
            result.Via.Should().Be("primary");
        }

        [Test]
        public void a_synonym_hit_is_taken_when_nothing_has_primary_equality()
        {
            var page = new List<AniListCandidate>
            {
                C(1, "Hayaku Shitai Futari", null, Manga, Releasing, null, 6000, "Let's Do It Already!")
            };

            var result = AniListRanker.Pick(page, "Let's Do It Already!", 9);

            result.Pick.Id.Should().Be(1);
            result.Via.Should().Be("synonym");
        }

        // R1 (OpenTome's live run, 2026-09-15): a candidate with PRIMARY-title equality that the
        // volume rule rejected says "this is the work, but the count disagrees" — a synonym-only
        // carrier on the same page is a chapter title wearing the name and must never win instead.
        [Test]
        public void a_synonym_carrier_never_wins_when_a_primary_title_candidate_was_rejected()
        {
            // Doll: the 6-volume line; "DOLL" (1 volume) is rejected on volumes, and the 4-volume
            // "Onegai, Sore wo Yamenaide" carries "Doll" as a synonym.
            var page = new List<AniListCandidate>
            {
                C(31566, "DOLL", null, Manga, Finished, 1, 4000),
                C(128084, "Onegai, Sore wo Yamenaide", null, Manga, Finished, 4, 900, "Doll")
            };

            var result = AniListRanker.Pick(page, "Doll", 6);

            result.Pick.Should().BeNull();
            result.Via.Should().BeNull();
            result.Rejections.Should().Equal(
                "31566 \"DOLL\": 1 vols vs catalogue 6",
                "128084 \"Onegai, Sore wo Yamenaide\": synonym only (a primary-title candidate is on the page)");
        }

        [Test]
        public void a_synonym_carrier_never_wins_beside_a_same_named_one_shot()
        {
            // The same rule read conservatively: a ONE_SHOT wearing the exact name is that serial's
            // pilot, so the serial is on AniList and a synonym-only carrier is not it. Null is
            // recoverable (alias retry, Fix Match); a wrong bind is pinned by every future add.
            var page = new List<AniListCandidate>
            {
                C(1, "Foo", "Foo", OneShot, Finished, 1, 4000),
                C(2, "Bar Anthology", null, Manga, Finished, 3, 900, "Foo")
            };

            var result = AniListRanker.Pick(page, "Foo", null);

            result.Pick.Should().BeNull();
            result.Rejections.Should().Equal(
                "1 \"Foo\": ONE_SHOT",
                "2 \"Bar Anthology\": synonym only (a primary-title candidate is on the page)");
        }

        [Test]
        public void a_tiny_line_binds_the_full_serial_over_a_one_volume_synonym_carrier()
        {
            // Pupa: the 1-volume line; the 5-volume serial has primary equality (R2 lets it through),
            // the 1-volume synonym carrier is outranked.
            var page = new List<AniListCandidate>
            {
                C(75613, "Pupa", "Pupa", Manga, Finished, 5, 12000),
                C(191157, "Sanagi", null, Manga, Finished, 1, 300, "Pupa")
            };

            var result = AniListRanker.Pick(page, "Pupa", 1);

            result.Pick.Id.Should().Be(75613);
            result.Via.Should().Be("primary");
        }

        [Test]
        public void ties_break_on_popularity_then_anilist_rank()
        {
            var page = new List<AniListCandidate>
            {
                C(1, "Berserk", "Berserk", Manga, Finished, null, 100),
                C(2, "Berserk", "Berserk", Manga, Finished, null, 244979),
                C(3, "Berserk", "Berserk", Manga, Finished, null, 244979)
            };

            var result = AniListRanker.Pick(page, "Berserk", null);

            result.Pick.Id.Should().Be(2);
        }

        [Test]
        public void a_candidate_far_smaller_than_the_catalogue_line_is_rejected()
        {
            // Pantsu Agerune: 1 volume against OpenTome's 63-volume Fairy Tail line.
            var page = new List<AniListCandidate>
            {
                C(128087, "Fairy Tail", "Fairy Tail", Manga, Finished, 1, 39)
            };

            var result = AniListRanker.Pick(page, "Fairy Tail", 63);

            result.Pick.Should().BeNull();
            result.Rejections.Should().Equal("128087 \"Fairy Tail\": 1 vols vs catalogue 63");
        }

        // D9: OpenTome counts English collected editions, AniList counts Japanese tankoubon, so a
        // larger AniList count is the normal case for a 2-in-1 line and must never reject.
        [TestCase(29, 15)]   // Vinland Saga: 15 deluxe 2-in-1 books vs 29 JP volumes
        [TestCase(9, 5)]     // Erased: 5 EN 2-in-1 books vs 9 JP
        [TestCase(34, 34)]   // Attack on Titan: equal
        [TestCase(16, 14)]   // [Oshi no Ko]
        [TestCase(14, 15)]   // Solo Leveling
        [TestCase(12, 3)]    // exactly the 4x ceiling
        public void a_collected_edition_line_keeps_the_larger_japanese_count(int anilistVolumes, int catalogueVolumes)
        {
            AniListRanker.VolumesAgree(anilistVolumes, Finished, catalogueVolumes).Should().BeTrue();
        }

        [TestCase(13, 3)]    // past the 4x omnibus-plausibility ceiling
        [TestCase(1, 63)]    // a 1-volume anthology against a 63-volume line
        [TestCase(11, 20)]   // FINISHED, 11 < 20 - max(3, 8)
        public void a_count_outside_the_window_is_rejected(int anilistVolumes, int catalogueVolumes)
        {
            AniListRanker.VolumesAgree(anilistVolumes, Finished, catalogueVolumes).Should().BeFalse();
        }

        // R2 (OpenTome's live run, 2026-09-15): a line of 1-2 volumes is a one-book release or a
        // run cut short, and the full Japanese serial is its normal binding (Pupa: 5 JP volumes
        // against a 1-volume line) — no 4x ceiling there; the smaller-side rule still applies.
        [TestCase(5, 1)]     // Pupa
        [TestCase(63, 1)]    // a 63-volume serial against a 1-volume line
        [TestCase(9, 2)]
        public void a_tiny_line_has_no_upper_ceiling(int anilistVolumes, int catalogueVolumes)
        {
            AniListRanker.VolumesAgree(anilistVolumes, Finished, catalogueVolumes).Should().BeTrue();
        }

        // R3 (OpenTome's live run, 2026-09-15): the lifted ceiling is for the line's OWN name; an
        // alias retry always keeps it. "Attack on Titan: Harsh Mistress of the City" (2 volumes)
        // bound to 53390 "Attack on Titan" (34) through the bare franchise alias "Shingeki no Kyojin".
        [Test]
        public void an_alias_retry_keeps_the_ceiling_for_a_tiny_line()
        {
            var page = new List<AniListCandidate>
            {
                C(53390, "Shingeki no Kyojin", "Attack on Titan", Manga, Finished, 34, 226876)
            };

            var byAlias = AniListRanker.Pick(page, "Shingeki no Kyojin", 2, ownName: false);

            byAlias.Pick.Should().BeNull();
            byAlias.Rejections.Should().Equal("53390 \"Attack on Titan\": 34 vols vs catalogue 2");

            var byName = AniListRanker.Pick(page, "Shingeki no Kyojin", 2);

            byName.Pick.Id.Should().Be(53390);
            byName.Via.Should().Be("primary");
        }

        [TestCase(34, 2, false)]  // Harsh Mistress of the City vs the main serial
        [TestCase(9, 2, false)]   // 4x of 2 is 8
        [TestCase(13, 3, false)]  // a line of 3 never had the exemption
        public void an_alias_term_rejects_past_the_ceiling(int anilistVolumes, int catalogueVolumes, bool ownName)
        {
            AniListRanker.VolumesAgree(anilistVolumes, Finished, catalogueVolumes, ownName).Should().BeFalse();
        }

        [TestCase(8, 2, false)]   // exactly 4x
        [TestCase(1, 2, false)]   // the smaller side is unchanged
        [TestCase(34, 2, true)]   // the same count against the line's own name
        public void an_alias_term_keeps_a_count_within_the_ceiling(int anilistVolumes, int catalogueVolumes, bool ownName)
        {
            AniListRanker.VolumesAgree(anilistVolumes, Finished, catalogueVolumes, ownName).Should().BeTrue();
        }

        [Test]
        public void a_releasing_candidate_gets_double_tolerance()
        {
            // 11 vs 20: FINISHED tolerance is max(3, 8) = 8 -> 11 < 12 rejected; RELEASING doubles it to 16.
            AniListRanker.VolumesAgree(11, Finished, 20).Should().BeFalse();
            AniListRanker.VolumesAgree(11, Releasing, 20).Should().BeTrue();
        }

        [TestCase(null, 63)]
        [TestCase(0, 63)]
        [TestCase(63, null)]
        [TestCase(63, 0)]
        [TestCase(null, null)]
        public void an_unknown_count_on_either_side_skips_the_volume_rule(int? anilistVolumes, int? catalogueVolumes)
        {
            AniListRanker.VolumesAgree(anilistVolumes, Releasing, catalogueVolumes).Should().BeTrue();
        }

        [Test]
        public void no_equality_returns_null_with_every_reason()
        {
            var page = new List<AniListCandidate>
            {
                C(1, "Mushoku Tensei: Isekai Ittara Honki Dasu", "Mushoku Tensei: Jobless Reincarnation", Manga, Releasing, null, 60000),
                C(2, "Mushoku Tensei: Roxy datte Honki desu", "Mushoku Tensei: Roxy Gets Serious", Manga, Finished, 11, 4007),
                C(3, "Mushoku Tensei", null, OneShot, Finished, 1, 10)
            };

            var result = AniListRanker.Pick(page, "Mushoku Tensei", 24);

            result.Pick.Should().BeNull();
            result.Via.Should().BeNull();
            result.Rejections.Should().Equal(
                "1 \"Mushoku Tensei: Jobless Reincarnation\": no title equality",
                "2 \"Mushoku Tensei: Roxy Gets Serious\": 11 vols vs catalogue 24",
                "3 \"Mushoku Tensei\": ONE_SHOT");
        }

        [Test]
        public void an_empty_or_null_page_returns_null()
        {
            AniListRanker.Pick(new List<AniListCandidate>(), "x", null).Pick.Should().BeNull();
            AniListRanker.Pick(null, "x", null).Pick.Should().BeNull();
        }

        // ---- OpenTome 2026-09-24: the fallback tiers R4-R7 (export/test_resolve_anilist.py) ----

        private static readonly AniListCandidate Serial = C(2, null, "X", Manga, Finished, 20, 5);
        private static readonly AniListCandidate Carrier = C(3, null, "Y", Manga, Finished, 21, 99, "X");

        private static AniListCandidate With(AniListCandidate c, int? id = null, int? volumes = -1, string english = null, string format = null, int? popularity = null, string[] synonyms = null)
        {
            return new AniListCandidate
            {
                Id = id ?? c.Id,
                TitleRomaji = c.TitleRomaji,
                TitleEnglish = english ?? c.TitleEnglish,
                TitleNative = c.TitleNative,
                Format = format ?? c.Format,
                Status = c.Status,
                Volumes = volumes == -1 ? c.Volumes : volumes,
                Popularity = popularity ?? c.Popularity,
                Synonyms = (synonyms ?? c.Synonyms.ToArray()).ToList()
            };
        }

        // "<id> via <tier>", or "none via none" when nothing binds.
        private static string Picked(IReadOnlyList<AniListCandidate> page, string term, int? count, bool ownName = true)
        {
            var result = AniListRanker.Pick(page, term, count, ownName);

            return $"{result.Pick?.Id.ToString() ?? "none"} via {result.Via ?? "none"}";
        }

        // Recorded AniList pages (opentome export/fixtures/anilist/, read 2026-09-24). Native titles
        // are left out: all are Japanese / Korean except 31566's "DOLL" (equal to "Doll", but its
        // romaji "DOLL" already is, so the page reads the same) and 30298's "DOLL: IC in a Doll"
        // (the same as its romaji). Weed is the whole page.
        private static List<AniListCandidate> WeedPage() => new List<AniListCandidate>
        {
            C(34010, "Ginga Densetsu WEED", "Ginga Legend Weed", Manga, Finished, 60, 630, "Silver Fang Legend Weed", "은아전설 위드", "WEED", "銀牙伝説ウィード"),
            C(36414, "Boku no Inu: Boku no Weed", null, Manga, Finished, 1, 77, "My Dog, My Weed", "Minun Weedini"),
            C(48607, "Ginga Densetsu Weed Gaiden", null, Manga, Finished, 1, 124),
            C(45785, "Ginga Densetsu Weed: Orion", null, Manga, Finished, 30, 215, "Ginga Densetsu Wiido: Orion", "은아전설 오리온", "銀牙伝説WEEDオリオン")
        };

        // Left out of the recorded page: 94803, 190818 "The Worst Generation", 63501, 205136 "The
        // Worst Infatuation", 95356 — none key-equals "worst" exactly or by article, none has 3
        // volumes; the outcome (147044 via primary) is the same with them.
        private static List<AniListCandidate> WorstPage() => new List<AniListCandidate>
        {
            C(31741, "Worst", "Worst", Manga, Finished, 33, 5174),
            C(147044, "Worst", null, Manga, Finished, 4, 55),
            C(95897, "Saigo no Worst", null, Manga, Finished, null, 272),
            C(178339, "Makjang Angnyeo", "The Worst Villainess", Manga, Finished, null, 616, "The Worst Villain"),
            C(46330, "Worst Gaiden", null, Manga, Finished, 1, 461)
        };

        // Left out of the recorded page: 117138 "Paradise Doll" and 117152 (1 volume each, both
        // with a "Doll" synonym — rejected on volumes against 6 like the others) and 119094 (1
        // volume, no equal title); the outcome (nothing binds) is the same with them.
        private static List<AniListCandidate> DollPage() => new List<AniListCandidate>
        {
            C(31566, "DOLL", null, Manga, Finished, 1, 290, "ドール", "すべての恋人に愛が降る"),
            C(62821, "Maboroshi Koi Kitan", null, Manga, Finished, 1, 73, "Maboroshi Koikitan", "Swan Ninja Art Anecdotes", " Edo Love Dance", "DOLL"),
            C(43796, "DoLL", null, Manga, Finished, 2, 368),
            C(60765, "Change!!", null, Manga, Finished, 1, 164, "Doll", "Loyal Dog Maid", "JC ISM", "No"),
            C(128084, "Onegai, Sore wo Yamenaide", null, Manga, Finished, 4, 156, "Onegai, Sore o Yamenai de", "Please, I Just Want It More", "Please, Don't Stop That", "Doll"),
            C(200561, "Doll Man Doll", "Doll Man Doll", OneShot, Finished, null, 79),
            C(30298, "DOLL: IC in a Doll", "DOLL: IC in a Doll", Manga, Finished, 6, 786, "Doru", "Dōru")
        };

        private const string Novel = "NOVEL";

        // Every candidate of the recorded page; the CJK / French / Thai / Portuguese synonyms are
        // trimmed — none changes which entries contain "ascendanceofabookworm" at 3 volumes (only
        // 87383), so the outcome is the same with them.
        private static List<AniListCandidate> BookwormPage() => new List<AniListCandidate>
        {
            C(110800, "Honzuki no Gekokujou: Shisho ni Naru Tame ni wa Shudan wo Erandeiraremasen Dai 2-bu - Shinden no Miko Minarai", "Ascendance of a Bookworm: Part 2", Novel, Finished, 4, 2916, "Honzuki no Gekokujou Part 2", "Ascendance of a Bookworm ~I'll do anything to become a librarian~ Part 2 Apprentice Shrine Maiden"),
            C(110801, "Honzuki no Gekokujou: Shisho ni Naru Tame ni wa Shudan wo Erandeiraremasen Dai 3-bu - Ryoushu no Youjo", "Ascendance of a Bookworm: Part 3", Novel, Finished, 5, 2633, "Honzuki no Gekokujou Part 3"),
            C(110802, "Honzuki no Gekokujou: Shisho ni Naru Tame ni wa Shudan wo Erandeiraremasen Dai 4-bu - Kizoku-in no Jishou Tosho Iin", "Ascendance of a Bookworm: Part 4", Novel, Finished, 9, 2334, "Honzuki no Gekokujou Part 4"),
            C(87383, "Honzuki no Gekokujou: Shisho ni Naru Tame ni wa Shudan wo Erandeiraremasen Dai 1-bu - Heishi no Musume", "Ascendance of a Bookworm: Part 1", Novel, Finished, 3, 7774, "Honzuki no Gekokujou Part 1", "Ascendance of a Bookworm Part 1"),
            C(113869, "Honzuki no Gekokujou: Shisho ni Naru Tame ni wa Shudan wo Erandeiraremasen Dai 5-bu - Megami no Keshin", "Ascendance of a Bookworm: Part 5", Novel, Finished, 12, 2338, "Honzuki no Gekokujou Part 5"),
            C(113868, "Honzuki no Gekokujou: Shisho ni Naru Tame ni wa Shudan wo Erandeiraremasen - Tanpenshuu", "Ascendance of a Bookworm: Short Story Collection", Novel, Releasing, null, 734),
            C(110803, "Honzuki no Gekokujou: Shisho ni Naru Tame ni wa Shudan wo Erandeiraremasen - Kizoku-in Gaiden 1-nensei", "Ascendance of a Bookworm: Royal Academy Stories - First Year", Novel, Finished, 1, 728),
            C(173934, "Honzuki no Gekokujou: Hannelore no Kizokuin Gonensei", "Ascendance of a Bookworm: Hannelore’s Fifth Year at the Royal Academy", Novel, Releasing, null, 587)
        };

        // R4 (Weed): an exact title match rejected SOLELY by the 4x ceiling binds when nothing
        // passed the ceiling — a short English run of the full Japanese serial.
        [Test]
        public void r4_an_exact_primary_rejected_only_by_the_ceiling_binds_when_nothing_else_did()
        {
            var result = AniListRanker.Pick(new[] { With(Serial, volumes: 60) }, "X", 3);

            result.Pick.Id.Should().Be(2);
            result.Via.Should().Be("ceiling");
            result.Rejections.Should().Equal("2 \"X\": 60 vols vs catalogue 3");
        }

        [TestCase(63, 5)]    // larger than 4x the line: only R4 then binds it
        [TestCase(13, 3)]    // R2 stops at 3 volumes
        public void r4_binds_what_the_ceiling_alone_rejected(int anilistVolumes, int catalogueVolumes)
        {
            Picked(new[] { With(Serial, volumes: anilistVolumes) }, "X", catalogueVolumes).Should().Be("2 via ceiling");
        }

        [Test]
        public void r4_an_exact_synonym_rejected_only_by_the_ceiling_binds_too()
        {
            Picked(new[] { With(Carrier, volumes: 60) }, "X", 3).Should().Be("3 via ceiling");
        }

        [Test]
        public void r4_never_fires_on_an_alias_term()
        {
            Picked(new[] { With(Serial, volumes: 60) }, "X", 3, ownName: false).Should().Be("none via none");
        }

        [Test]
        public void r4_never_replaces_a_candidate_that_passes_the_ceiling()
        {
            // Worst-shaped: the 4-volume entry passes, the 33-volume one only fails the ceiling.
            Picked(new[] { With(Serial, id: 31741, volumes: 33), With(Serial, id: 147044, volumes: 4) }, "X", 3).Should().Be("147044 via primary");
        }

        [Test]
        public void r4_never_outranks_a_ceiling_passing_synonym()
        {
            Picked(new[] { With(Carrier, id: 8, volumes: 60, popularity: 999), With(Carrier, id: 9, volumes: 3) }, "X", 3).Should().Be("9 via synonym");
        }

        [Test]
        public void r4_never_outranks_a_ceiling_passing_synonym_carrier_even_one_r1_rejected()
        {
            var result = AniListRanker.Pick(new[] { With(Serial, volumes: 60), With(Carrier, id: 9, volumes: 3) }, "X", 3);

            result.Pick.Should().BeNull();
            result.Rejections.Should().Contain("9 \"Y\": synonym only (a primary-title candidate is on the page)");
        }

        [Test]
        public void r4_never_binds_a_one_shot()
        {
            Picked(new[] { With(Serial, format: OneShot, volumes: 60) }, "X", 3).Should().Be("none via none");
        }

        [Test]
        public void r4_keeps_r1_an_oversized_synonym_carrier_never_wins_beside_a_primary_title_candidate()
        {
            Picked(new[] { With(Serial, volumes: 1), With(Carrier, volumes: 60) }, "X", 6).Should().Be("none via none");
        }

        [Test]
        public void r4_needs_exact_equality()
        {
            Picked(new[] { With(Serial, english: "X Gaiden", volumes: 60) }, "X", 3).Should().Be("none via none");
        }

        [Test]
        public void r4_never_binds_beside_an_article_equal_candidate_that_passes_the_ceiling()
        {
            var page = new[]
            {
                C(34010, null, "Weed", Manga, Finished, 60, 5),
                C(81, null, "The Weed", Manga, Finished, 3, 5)
            };

            Picked(page, "Weed", 3).Should().Be("none via none");
        }

        [Test]
        public void r4_without_a_catalogue_count_has_no_ceiling_to_fall_back_from()
        {
            // An uncatalogued series: the 60-volume entry passes the (skipped) volume rule and
            // binds as a plain primary — R4 is never what binds it.
            Picked(new[] { With(Serial, volumes: 60) }, "X", null).Should().Be("2 via primary");
        }

        // R4 and R5 read the catalogue count as the term's: when the term is not the hinted line's
        // own name (an alias or arc title the line was found by) neither fires; ownName and R2 are
        // untouched.
        [Test]
        public void r4_and_r5_never_fire_when_the_term_is_not_the_lines_own_name()
        {
            AniListRanker.Pick(new[] { With(Serial, volumes: 60) }, "X", 3, termIsLineName: false).Pick.Should().BeNull();
            AniListRanker.Pick(new[] { NightShift }, "Night Shift", 3, termIsLineName: false).Pick.Should().BeNull();
            AniListRanker.Pick(new[] { TheNightShift }, "Night Shift", 20, termIsLineName: false).Via.Should().Be("article");
            AniListRanker.Pick(new[] { With(Serial, volumes: 34) }, "X", 2, termIsLineName: false).Via.Should().Be("primary");
        }

        [Test]
        public void weed_page_binds_34010_through_the_ceiling_tier()
        {
            var result = AniListRanker.Pick(WeedPage(), "Weed", 3);

            result.Pick.Id.Should().Be(34010);
            result.Via.Should().Be("ceiling");
            result.Rejections.Should().Contain("45785 \"Ginga Densetsu Weed: Orion\": 30 vols vs catalogue 3");
        }

        [Test]
        public void weed_page_as_an_alias_term_binds_nothing()
        {
            Picked(WeedPage(), "Weed", 3, ownName: false).Should().Be("none via none");
        }

        [Test]
        public void worst_page_keeps_147044_because_it_passes_the_ceiling()
        {
            Picked(WorstPage(), "Worst", 3).Should().Be("147044 via primary");
        }

        // R5: no equality anywhere on the page -> ONE candidate whose title key contains the
        // term's key (or sits inside it, shorter side >= 4) AND whose volumes equal the line's.
        private static readonly AniListCandidate NightShift = C(50, null, "Night Shift: Tale of a Vampire", Manga, Finished, 3, 5);

        [Test]
        public void r5_the_term_inside_a_longer_title_with_the_exact_count_binds()
        {
            Picked(new[] { NightShift }, "Night Shift", 3).Should().Be("50 via substring");
        }

        [Test]
        public void r5_a_shorter_title_inside_the_term_binds()
        {
            Picked(new[] { With(NightShift, english: "Night Shift") }, "Night Shift (Part 2: The Dawn)", 3).Should().Be("50 via substring");
        }

        [Test]
        public void r5_reads_synonyms_too()
        {
            Picked(new[] { With(NightShift, english: "Yakin", synonyms: new[] { "Night Shift: Tale" }) }, "Night Shift", 3).Should().Be("50 via substring");
        }

        [Test]
        public void r5_never_fires_on_an_alias_term()
        {
            Picked(new[] { NightShift }, "Night Shift", 3, ownName: false).Should().Be("none via none");
        }

        [Test]
        public void r5_needs_a_unique_candidate()
        {
            Picked(new[] { NightShift, With(NightShift, id: 51, english: "Night Shift Zero") }, "Night Shift", 3).Should().Be("none via none");
        }

        [TestCase(4)]
        [TestCase(2)]
        [TestCase(null)]
        public void r5_needs_the_exact_count(int? anilistVolumes)
        {
            Picked(new[] { With(NightShift, volumes: anilistVolumes) }, "Night Shift", 3).Should().Be("none via none");
        }

        [Test]
        public void r5_never_fires_without_a_catalogue_count()
        {
            // A series not in OpenTome has no count to be exact against.
            Picked(new[] { NightShift }, "Night Shift", null).Should().Be("none via none");
        }

        [Test]
        public void r5_never_binds_a_one_shot()
        {
            Picked(new[] { With(NightShift, format: OneShot) }, "Night Shift", 3).Should().Be("none via none");
        }

        [Test]
        public void r5_the_shorter_side_must_be_at_least_four_characters()
        {
            Picked(new[] { With(NightShift, english: "Houseki") }, "Hou", 3).Should().Be("none via none");
        }

        [Test]
        public void r5_does_not_fire_beside_an_equal_title_rejected_on_volumes()
        {
            // Doll-shaped: the equal title is the work with a disputed count.
            Picked(new[] { With(Serial, id: 31566, volumes: 1), With(NightShift, volumes: 6, english: "X: IC in a X") }, "X", 6).Should().Be("none via none");
        }

        [Test]
        public void r5_does_not_fire_beside_a_same_named_one_shot()
        {
            Picked(new[] { C(1, null, "Night Shift", OneShot, Finished, 1, 9), NightShift }, "Night Shift", 3).Should().Be("none via none");
        }

        [Test]
        public void r5_never_outranks_equality()
        {
            Picked(new[] { With(NightShift, english: "Night Shift"), With(NightShift, id: 51) }, "Night Shift", 3).Should().Be("50 via primary");
        }

        [TestCase(3, 87383)]    // Part 1
        [TestCase(6, null)]     // a count no Part has
        public void bookworm_page_binds_the_part_with_the_exact_count(int catalogueVolumes, int? id)
        {
            Picked(BookwormPage(), "Ascendance of a Bookworm", catalogueVolumes).Should().Be(id.HasValue ? $"{id} via substring" : "none via none");
        }

        [Test]
        public void bookworm_part_2_binds_the_shorter_part_2_entry()
        {
            var page = BookwormPage().Where(c => c.Id == 110800).ToList();

            Picked(page, "Ascendance of a Bookworm (Part 2: Apprentice Shrine Maiden)", 4).Should().Be("110800 via substring");
        }

        // R7: equality after dropping one leading the / a / an from both sides, a tier below
        // exact equality ("Hollow Regalia" is AniList's "The Hollow Regalia").
        private static readonly AniListCandidate TheNightShift = C(70, null, "The Night Shift", Manga, Finished, 20, 5);

        [Test]
        public void r7_binds_through_a_leading_article()
        {
            Picked(new[] { TheNightShift }, "Night Shift", 20).Should().Be("70 via article");
        }

        [Test]
        public void r7_drops_the_article_on_the_terms_side_too()
        {
            Picked(new[] { With(TheNightShift, english: "Night Shift") }, "A Night Shift", 20).Should().Be("70 via article");
        }

        [Test]
        public void r7_never_outranks_exact_equality()
        {
            Picked(new[] { TheNightShift, C(71, null, "Night Shift", Manga, Finished, 20, 5) }, "Night Shift", 20).Should().Be("71 via primary");
        }

        [Test]
        public void r7_only_a_whole_leading_word_is_an_article()
        {
            Picked(new[] { With(TheNightShift, english: "Theater Night") }, "ater Night", 19).Should().Be("none via none");
        }

        [Test]
        public void r7_keeps_the_volume_rule_and_one_shots_out()
        {
            Picked(new[] { With(TheNightShift, volumes: 1) }, "Night Shift", 20).Should().Be("none via none");
            Picked(new[] { With(TheNightShift, format: OneShot) }, "Night Shift", 20).Should().Be("none via none");
        }

        [Test]
        public void r7_keeps_r1_an_article_equal_synonym_carrier_never_wins_beside_a_rejected_article_equal_primary()
        {
            Picked(new[] { With(TheNightShift, volumes: 1), With(Carrier, id: 72, synonyms: new[] { "The Night Shift" }) }, "Night Shift", 20).Should().Be("none via none");
        }

        [Test]
        public void r7_reads_synonyms()
        {
            Picked(new[] { With(Carrier, id: 72, synonyms: new[] { "The Night Shift" }) }, "Night Shift", 20).Should().Be("72 via article");
        }

        [Test]
        public void r7_runs_on_an_alias_term_too()
        {
            Picked(new[] { TheNightShift }, "Night Shift", 20, ownName: false).Should().Be("70 via article");
        }

        // R7 respects R1 ACROSS tiers: an exact primary-title candidate on the page — even one
        // rejected on volumes, or a same-named ONE_SHOT — is the work with a disputed count.
        [Test]
        public void r7_never_binds_beside_an_exact_primary_rejected_on_volumes()
        {
            var page = new[]
            {
                C(31566, null, "Doll", Manga, Finished, 1, 5),
                C(80, null, "The Doll", Manga, Finished, 6, 5)
            };

            Picked(page, "Doll", 6).Should().Be("none via none");
        }

        [Test]
        public void r7_never_binds_beside_a_same_named_one_shot()
        {
            var page = new[]
            {
                C(1, null, "Doll", OneShot, Finished, 1, 9),
                C(80, null, "The Doll", Manga, Finished, 6, 5)
            };

            Picked(page, "Doll", 6).Should().Be("none via none");
        }

        [Test]
        public void hollow_regalia_binds_the_hollow_regalia_through_the_article_tier()
        {
            var page = new[]
            {
                C(133016, "Utsuro Naru Regalia", "The Hollow Regalia", Novel, Releasing, null, 270, "Corpse Reviewer", "Ryuu to Aoku Fukai Umi no Ma de", " All Hell Breaks Loose")
            };

            Picked(page, "Hollow Regalia", 6).Should().Be("133016 via article");
        }

        [Test]
        public void doll_page_stays_unresolved_under_every_tier()
        {
            // "DOLL" (1) and "DoLL" (2) are rejected on volumes, the synonym carriers by R1, and
            // the right 30298 "DOLL: IC in a Doll" (6) never key-equals "doll": R5 needs no
            // equality on the page, R7 none either.
            var result = AniListRanker.Pick(DollPage(), "Doll", 6);

            result.Pick.Should().BeNull();
            result.Rejections.Should().Contain("31566 \"DOLL\": 1 vols vs catalogue 6");
            result.Rejections.Should().Contain("128084 \"Onegai, Sore wo Yamenaide\": synonym only (a primary-title candidate is on the page)");
        }

        // R6: the name without a trailing edition-qualifier parenthetical.
        [TestCase("Inuyasha (VizBig edition)", "Inuyasha")]
        [TestCase("Yo-kai Watch (Noriyuki Konishi version)", "Yo-kai Watch")]
        [TestCase("Tomie (Original release)", "Tomie")]
        [TestCase("Arata: The Legend (Tankōbon edition)", "Arata: The Legend")]
        [TestCase("Foo (2-in-1)", "Foo")]
        [TestCase("Foo (Second printing)", "Foo")]
        [TestCase("Foo (Shinsōban)", "Foo")]
        [TestCase("Foo (English-language volume list)", "Foo")]
        [TestCase("Ranma ½ (2014 English release (2-in-1 Edition)", "Ranma 1/2")] // ForSearch's numeric fold (OpenTome's test says the same)
        [TestCase("Marmalade Boy (Collector's edition)", "Marmalade Boy")]
        [TestCase("Sailor Moon (Shinsōban short stories)", "Sailor Moon")]
        public void r6_strips_an_edition_qualifier(string name, string stripped)
        {
            AniListRanker.EditionStripped(name).Should().Be(stripped);
        }

        [TestCase("Re:Zero (Truth of Zero)")]
        [TestCase("The Wallflower (Chapter and volume list)")]
        [TestCase("Foo (Manga series (2010 edition))")]
        [TestCase("Restaurant to Another World (First series)")]
        [TestCase("Weed")]
        [TestCase("(Deluxe edition)")]
        [TestCase("Amazing Agent Luna (\"Amazing Agent Jennifer\" Volume list)")]
        [TestCase("Amazing Agent Luna (“Amazing Agent Jennifer” Volume list)")]
        [TestCase("")]
        [TestCase(null)]
        public void r6_never_strips_an_arc_a_chapter_list_a_nested_series_a_quoted_title_or_a_plain_subtitle(string name)
        {
            AniListRanker.EditionStripped(name).Should().BeNull();
        }

        [Test]
        public void article_key_drops_one_leading_article_only()
        {
            AniListRanker.ArticleKey("The Hollow Regalia").Should().Be("hollowregalia");
            AniListRanker.ArticleKey("A Night Shift").Should().Be("nightshift");
            AniListRanker.ArticleKey("Theater Night").Should().Be("theaternight");
            AniListRanker.ArticleKey("The The Band").Should().Be("theband");
            AniListRanker.ArticleKey(null).Should().Be(string.Empty);
        }
    }
}
