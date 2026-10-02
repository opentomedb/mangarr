using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation.Aggregators;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class EditionVolumeTokensFixture : CoreTest
    {
        [TestCase("L'Attaque des Titans - Tome 5 [FR]", "fr", 5)]
        [TestCase("L'Attaque des Titans Tome 05", "fr", 5)]
        [TestCase("Angriff auf Titan Band 05", "de", 5)]
        [TestCase("Angriff auf Titan Bd. 3", "de", 3)]
        [TestCase("進撃の巨人 5巻", "ja", 5)]
        [TestCase("進撃の巨人 第5巻", "ja", 5)]
        [TestCase("進撃の巨人 - 5巻", "ja", 5)]
        [TestCase("나 혼자만 레벨업 5권", "ko", 5)]
        [TestCase("나 혼자만 레벨업 제5권", "ko", 5)]
        [TestCase("나 혼자만 레벨업 - 5권", "ko", 5)]
        [TestCase("斗破苍穹 第5卷", "zh", 5)]
        [TestCase("斗破苍穹 5卷", "zh", 5)]
        [TestCase("斗破苍穹 第5册", "zh", 5)]
        [TestCase("霹靂神州 第5集", "zh-TW", 5)]
        public void single_volume_tokens_parse_for_their_edition(string text, string language, double expected)
        {
            MangaVolumeParser.ParseSingleVolume(text, language).Should().Be(expected);
        }

        [Test]
        public void ranges_become_packs()
        {
            var fr = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(EditionVolumeTokens.Rewrite("L'Attaque des Titans Tomes 1 à 5", "fr"), fr);
            fr.VolumeStart.Should().Be(1);
            fr.VolumeEnd.Should().Be(5);

            var de = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(EditionVolumeTokens.Rewrite("Angriff auf Titan Bände 1-3", "de"), de);
            de.VolumeStart.Should().Be(1);
            de.VolumeEnd.Should().Be(3);

            // Preferred Edition (2026-09-24, M9 fix round 1 I1): the end repeating the token is still the range.
            var frRepeated = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(EditionVolumeTokens.Rewrite("L'Attaque des Titans Tome 1 à Tome 5", "fr"), frRepeated);
            frRepeated.VolumeStart.Should().Be(1);
            frRepeated.VolumeEnd.Should().Be(5);

            var deRepeated = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(EditionVolumeTokens.Rewrite("Angriff auf Titan Band 1 bis Band 5", "de"), deRepeated);
            deRepeated.VolumeStart.Should().Be(1);
            deRepeated.VolumeEnd.Should().Be(5);
        }

        // KR/CN piece 2 (2026-10-02, M5): Korean and Chinese ranges are packs; whole-set words are no volume;
        // an English series never reads these tokens.
        [Test]
        public void korean_and_chinese_ranges_become_packs_and_whole_sets_are_no_volume()
        {
            var ko = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(EditionVolumeTokens.Rewrite("나 혼자만 레벨업 1-5권", "ko"), ko);
            ko.VolumeStart.Should().Be(1);
            ko.VolumeEnd.Should().Be(5);

            var koPrefixed = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(EditionVolumeTokens.Rewrite("나 혼자만 레벨업 제1~5권", "ko"), koPrefixed);
            koPrefixed.VolumeStart.Should().Be(1);
            koPrefixed.VolumeEnd.Should().Be(5);

            var zh = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(EditionVolumeTokens.Rewrite("斗破苍穹 第1-5卷", "zh"), zh);
            zh.VolumeStart.Should().Be(1);
            zh.VolumeEnd.Should().Be(5);

            MangaVolumeParser.ParseSingleVolume("나 혼자만 레벨업 전10권", "ko").Should().Be(0);
            MangaVolumeParser.ParseSingleVolume("斗破苍穹 全10卷", "zh").Should().Be(0);
            MangaVolumeParser.ParseSingleVolume("나 혼자만 레벨업 총10권", "ko").Should().Be(0);
            MangaVolumeParser.ParseSingleVolume("斗破苍穹 共10卷", "zh").Should().Be(0);
            MangaVolumeParser.ParseSingleVolume("斗破苍穹 全套10册", "zh").Should().Be(0);

            EditionVolumeTokens.Rewrite("Solo Leveling 5권", "en").Should().Be("Solo Leveling 5권");
            EditionVolumeTokens.Rewrite("Solo Leveling 5권", null).Should().Be("Solo Leveling 5권");
        }

        // Preferred Edition (2026-09-24, M9 fix round 1 I2): a Japanese pack is a range, never its last volume;
        // "全34巻" (the whole set) is no volume at all.
        [TestCase("進撃の巨人 1-5巻", 1, 5)]
        [TestCase("進撃の巨人 第01-34巻", 1, 34)]
        [TestCase("進撃の巨人 1～5巻", 1, 5)]
        public void a_japanese_pack_is_a_range(string title, int start, int end)
        {
            MangaVolumeParser.ParseSingleVolume(title, "ja").Should().Be(0);

            var parsed = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(EditionVolumeTokens.Rewrite(title, "ja"), parsed);
            parsed.VolumeNumber.Should().BeNull();
            parsed.VolumeStart.Should().Be(start);
            parsed.VolumeEnd.Should().Be(end);
        }

        [Test]
        public void a_japanese_whole_set_is_no_volume()
        {
            MangaVolumeParser.ParseSingleVolume("進撃の巨人 全34巻", "ja").Should().Be(0);

            var parsed = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(EditionVolumeTokens.Rewrite("進撃の巨人 全34巻", "ja"), parsed);
            parsed.VolumeNumber.Should().BeNull();
            parsed.VolumeStart.Should().BeNull();
        }

        [TestCase("/manga/進撃の巨人/進撃の巨人 1-5巻.cbz", 0)]
        [TestCase("/manga/進撃の巨人/進撃の巨人 第01-34巻.cbz", 0)]
        [TestCase("/manga/進撃の巨人/進撃の巨人 全34巻.cbz", 0)]
        [TestCase("/manga/進撃の巨人/進撃の巨人 5巻.cbz", 5)]
        public void the_import_volume_check_never_reads_a_japanese_pack_as_its_last_volume(string path, double expected)
        {
            var tracks = new List<LocalBook> { new LocalBook { Path = path, FileTrackInfo = new ParsedTrackInfo() } };

            DistanceCalculator.FileVolume(tracks, "ja", null).Should().Be(expected);
        }

        // Preferred Edition (2026-09-24, M9 fix round 1): '_' / '.' separators and the language's case.
        [Test]
        public void underscored_names_and_upper_case_languages_read()
        {
            MangaVolumeParser.ParseSingleVolume("L_Attaque_des_Titans_Tome_05", "fr").Should().Be(5);
            MangaVolumeParser.TryParseSeriesVolume("L_Attaque_des_Titans_Tome_05", out var series, out var volume, "fr").Should().BeTrue();
            series.Should().Be("L Attaque des Titans");
            volume.Should().Be(5);

            MangaVolumeParser.ParseSingleVolume("Angriff.auf.Titan.Band.05", "de").Should().Be(5);
            MangaVolumeParser.ParseSingleVolume("Angriff auf Titan Band 05", "DE").Should().Be(5);
            MangaVolumeParser.ParseSingleVolume("L'Attaque des Titans Tome 5", " Fr ").Should().Be(5);
        }

        [Test]
        public void t05_counts_only_right_after_an_accepted_series()
        {
            bool Accepted(string s) => s.CleanAuthorName() == "lattaquedestitans";

            MangaVolumeParser.ParseSingleVolume(EditionVolumeTokens.Rewrite("L'Attaque des Titans T05 [FR]", "fr", Accepted)).Should().Be(5);
            MangaVolumeParser.ParseSingleVolume(EditionVolumeTokens.Rewrite("Other Series T05", "fr", Accepted)).Should().Be(0);
            MangaVolumeParser.ParseSingleVolume(EditionVolumeTokens.Rewrite("L'Attaque des Titans T05", "fr")).Should().Be(0);
        }

        // Preferred Edition (2026-09-24): "T01 à T03" / "T01-T03" is a pack, not volume 1 -- rewriting only the
        // first T token left "Vol. 01 à T03", which the dash-range regex cannot read, so it parsed as volume 1.
        [TestCase("L'Attaque des Titans T01 à T03 [FR]")]
        [TestCase("L'Attaque des Titans T01-T03")]
        [TestCase("L.Attaque.des.Titans.T01-03.FRENCH.CBZ-GRP")]
        public void a_t_range_after_an_accepted_series_is_a_pack(string title)
        {
            bool Accepted(string s) => s.CleanAuthorName() == "lattaquedestitans";

            var parsed = new ParsedBookInfo();
            MangaVolumeParser.ParseVolume(EditionVolumeTokens.Rewrite(title, "fr", Accepted), parsed);

            parsed.VolumeStart.Should().Be(1);
            parsed.VolumeEnd.Should().Be(3);
        }

        [TestCase("Tome 5")]
        [TestCase("Band 2")]
        [TestCase("T05")]
        [TestCase("5巻")]
        public void rewrite_returns_its_input_for_a_null_or_english_edition(string text)
        {
            EditionVolumeTokens.Rewrite(text, null, _ => true).Should().BeSameAs(text);
            EditionVolumeTokens.Rewrite(text, "en", _ => true).Should().BeSameAs(text);
        }

        [Test]
        public void a_library_file_named_by_mangarr_parses_back()
        {
            MangaVolumeParser.TryParseSeriesVolume("L'Attaque des Titans - L'Attaque des Titans Tome 5", out var series, out var volume, "fr").Should().BeTrue();
            volume.Should().Be(5);
            series.Should().Be("L'Attaque des Titans - L'Attaque des Titans");
        }

        [TestCase("L'Attaque des Titans - Intégrale", true)]
        [TestCase("Angriff auf Titan Doppelband 3", true)]
        [TestCase("Fullmetal Alchemist Perfect Edition 2", true)]
        [TestCase("L'Attaque des Titans - Coffret T01 à T03", true)]
        [TestCase("Attack on Titan Box Set 1", true)]
        [TestCase("One Piece Massiv 3", true)]
        [TestCase("L'Attaque des Titans Tome 5", false)]
        public void collected_markers(string title, bool expected)
        {
            EditionVolumeTokens.IsCollectedEdition(title).Should().Be(expected);
        }

        [Test]
        public void the_import_volume_check_reads_an_edition_file()
        {
            var tracks = new List<LocalBook> { new LocalBook { Path = "/manga/L'Attaque des Titans/L'Attaque des Titans T05.cbz", FileTrackInfo = new ParsedTrackInfo() } };

            DistanceCalculator.FileVolume(tracks, "fr", s => s.CleanAuthorName() == "lattaquedestitans").Should().Be(5);
            DistanceCalculator.FileVolume(tracks).Should().Be(0);
        }

        // Preferred Edition (2026-09-24, M5 hand-off): under a non-English chain an unknown import's remote candidates come from
        // SearchForNewBook -> BuildFakeAuthorForTitle -> the chain's edition line, so the candidate is named in
        // the edition's language ("L'Attaque des Titans Tome 5", AuthorMetadata fr + AnchorName). The volume
        // check must read that candidate's own tokens -- never a false "volume" mismatch -- and still reject a
        // different volume. FileTrackInfo comes from the real filename augmenter, as it does for a bare .cbz.
        private static Edition EditionLineCandidate(double volume)
        {
            var meta = new AuthorMetadata
            {
                Name = "L'Attaque des Titans",
                EditionLanguage = "fr",
                AnchorName = "Attack on Titan",
                Aliases = new List<string> { "Shingeki no Kyojin" }
            };

            var book = new Book
            {
                Title = "L'Attaque des Titans Tome " + volume,
                VolumeNumber = volume,
                AuthorMetadata = meta,
                SeriesLinks = new List<SeriesBookLink>()
            };

            return new Edition { Title = book.Title, Book = book, Language = "fra", MediaType = MediaType.Archive, Format = "Paperback" };
        }

        private static List<LocalBook> Augmented(string path)
        {
            var tracks = new List<LocalBook> { new LocalBook { Path = path, FileTrackInfo = new ParsedTrackInfo() } };
            new AggregateFilenameInfo(LogManager.GetLogger("test")).Aggregate(new LocalEdition(tracks), false);

            return tracks;
        }

        [TestCase("/dl/L'Attaque des Titans Tome 5.cbz")]
        [TestCase("/dl/L'Attaque des Titans T05.cbz")]
        [TestCase("/dl/Attack on Titan T05.cbz")]
        [TestCase("/dl/Attack on Titan v05.cbz")]
        public void an_edition_line_candidate_reads_its_own_volume_tokens(string path)
        {
            var tracks = Augmented(path);

            // Preferred Edition (2026-09-24): "volume" is scored only when both sides name one: 0 = the same volume, 1 = a mismatch.
            DistanceCalculator.BookDistance(tracks, EditionLineCandidate(5)).Penalties["volume"].Should().Equal(0.0);
            DistanceCalculator.BookDistance(tracks, EditionLineCandidate(6)).Penalties["volume"].Should().Equal(1.0);
        }

        [Test]
        public void an_edition_line_candidate_matches_its_own_edition_file()
        {
            var own = DistanceCalculator.BookDistance(Augmented("/dl/L'Attaque des Titans T05.cbz"), EditionLineCandidate(5)).NormalizedDistance();
            var other = DistanceCalculator.BookDistance(Augmented("/dl/L'Attaque des Titans T05.cbz"), EditionLineCandidate(6)).NormalizedDistance();

            own.Should().BeLessThan(other);
            other.Should().BeGreaterThan(0.15, "a different volume must fail the 0.15 match threshold");
        }

        // Preferred Edition (2026-09-24, M9 pre-review fix): the author term also takes a non-English series'
        // anchor and aliases, so an English-named untracked file identifies against it (was 0.215 > 0.20).
        [TestCase("/dl/Attack on Titan v05.cbz")]
        [TestCase("/dl/Attack on Titan T05.cbz")]
        [TestCase("/dl/Shingeki no Kyojin v05.cbz")]
        public void an_english_named_file_identifies_against_an_edition_line_series(string path)
        {
            DistanceCalculator.BookDistance(Augmented(path), EditionLineCandidate(5)).NormalizedDistance().Should().BeLessThan(0.20);
        }

        // An English (or null-language) series computes exactly today's distance: its aliases never join the
        // author term. Values pinned from 6fa10e6, before the change.
        [TestCase(null, "/dl/Shingeki no Kyojin v05.cbz", 0.2530719907357766)]
        [TestCase(null, "/dl/Attack on Titan v05.cbz", 0.1597864081833973)]
        [TestCase("en", "/dl/Shingeki no Kyojin v05.cbz", 0.2530719907357766)]
        [TestCase("en", "/dl/Attack on Titan v05.cbz", 0.1597864081833973)]
        public void an_english_series_distance_is_unchanged(string language, string path, double expected)
        {
            var meta = new AuthorMetadata { Name = "Attack on Titan", EditionLanguage = language, Aliases = new List<string> { "Shingeki no Kyojin" } };
            var book = new Book { Title = "Attack on Titan Vol. 5", VolumeNumber = 5, AuthorMetadata = meta, SeriesLinks = new List<SeriesBookLink>() };
            var edition = new Edition { Title = book.Title, Book = book, Language = "eng", MediaType = MediaType.Archive, Format = "Paperback" };

            DistanceCalculator.BookDistance(Augmented(path), edition).NormalizedDistance().Should().Be(expected);
        }

        // Preferred Edition (2026-09-24, M9 pre-review fix): the author-scoped fallback reads a user's own
        // "… T05.cbz" for that edition's series; an English series parses as today.
        [TestCase("/manga/L'Attaque des Titans/L'Attaque des Titans T05.cbz", "fr", 5.0)]
        [TestCase("/manga/L'Attaque des Titans/Attack on Titan T05.cbz", "fr", 5.0)]
        [TestCase("/manga/L'Attaque des Titans/L'Attaque des Titans Tome 5.cbz", "fr", 5.0)]
        [TestCase("/manga/L'Attaque des Titans/L'Attaque des Titans T05.cbz", null, null)]
        [TestCase("/manga/Attack on Titan/Attack on Titan v05.cbz", null, 5.0)]
        public void the_scoped_fallback_reads_an_edition_series_own_files(string path, string language, double? expected)
        {
            var meta = new AuthorMetadata { Name = "L'Attaque des Titans", EditionLanguage = language, AnchorName = language == null ? null : "Attack on Titan" };
            var release = new LocalEdition(new List<LocalBook> { new LocalBook { Path = path, FileTrackInfo = new ParsedTrackInfo() } });

            IdentificationService.ParseScopedVolume(release, meta).Should().Be(expected);
        }

        // Follow-up round (KR/CN consumer, I1 residue): a fallback series (bound to its Japanese line, EditionFallback)
        // matches downloaded files like English -- the scoped parse, the volume check and the author term are the
        // English series' ones, byte for byte; the same binding as a Japanese edition keeps today's edition rules.
        private static AuthorMetadata JapaneseMeta(bool fallback)
        {
            return new AuthorMetadata { Name = "Attack on Titan", EditionLanguage = "ja", EditionFallback = fallback, AnchorName = "Shingeki no Kyojin Anchor", Aliases = new List<string> { "Shingeki no Kyojin" } };
        }

        [TestCase(true, null)]
        [TestCase(false, 5.0)]
        public void a_fallback_series_scoped_parse_reads_no_edition_token(bool fallback, double? expected)
        {
            var release = new LocalEdition(new List<LocalBook> { new LocalBook { Path = "/manga/Attack on Titan/Attack on Titan 5巻.cbz", FileTrackInfo = new ParsedTrackInfo() } });

            IdentificationService.ParseScopedVolume(release, JapaneseMeta(fallback)).Should().Be(expected);
        }

        [Test]
        public void a_fallback_series_file_volume_is_read_like_english()
        {
            var book = new Book { Title = "Attack on Titan Vol. 6", VolumeNumber = 6, AuthorMetadata = JapaneseMeta(true), SeriesLinks = new List<SeriesBookLink>() };
            var edition = new Edition { Title = book.Title, Book = book, Language = "eng", MediaType = MediaType.Archive, Format = "Paperback" };

            DistanceCalculator.BookDistance(Augmented("/dl/Attack on Titan 5巻.cbz"), edition).Penalties.Should().NotContainKey("volume");

            book.AuthorMetadata = JapaneseMeta(false);
            DistanceCalculator.BookDistance(Augmented("/dl/Attack on Titan 5巻.cbz"), edition).Penalties["volume"].Should().Equal(1.0);
        }

        // The English values pinned above (an_english_series_distance_is_unchanged): the aliases stay out of the
        // author term for a fallback series and join it for the edition series.
        [TestCase("/dl/Shingeki no Kyojin v05.cbz", 0.2530719907357766)]
        [TestCase("/dl/Attack on Titan v05.cbz", 0.1597864081833973)]
        public void a_fallback_series_distance_is_the_english_distance(string path, double english)
        {
            var book = new Book { Title = "Attack on Titan Vol. 5", VolumeNumber = 5, AuthorMetadata = JapaneseMeta(true), SeriesLinks = new List<SeriesBookLink>() };
            var edition = new Edition { Title = book.Title, Book = book, Language = "eng", MediaType = MediaType.Archive, Format = "Paperback" };

            DistanceCalculator.BookDistance(Augmented(path), edition).NormalizedDistance().Should().Be(english);
        }

        [Test]
        public void the_same_binding_as_an_edition_lets_its_aliases_into_the_author_term()
        {
            var book = new Book { Title = "Attack on Titan Vol. 5", VolumeNumber = 5, AuthorMetadata = JapaneseMeta(false), SeriesLinks = new List<SeriesBookLink>() };
            var edition = new Edition { Title = book.Title, Book = book, Language = "eng", MediaType = MediaType.Archive, Format = "Paperback" };

            DistanceCalculator.BookDistance(Augmented("/dl/Shingeki no Kyojin v05.cbz"), edition).NormalizedDistance().Should().BeLessThan(0.2530719907357766);
        }
    }
}
