using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    // Volumes built from chapter packs (2026-10-21): a chapter release ("Series c001-073
    // (Digital)") parses to its chapter range; any volume token, or a "Chapter N" that is part of
    // the series' own name, means it is not one.
    [TestFixture]
    public class ChapterReleaseParserFixture : CoreTest
    {
        private static readonly string[] NoAliases = new string[0];

        [TestCase("Marriagetoxin c001-073 (Digital) (empyrealarrow)", "Marriagetoxin", "en", 1, 73)]
        [TestCase("Marriagetoxin c074 (Digital) (empyrealarrow)", "Marriagetoxin", "en", 74, 74)]
        [TestCase("Marriagetoxin c073.5 (Digital) (empyrealarrow)", "Marriagetoxin", "en", 73.5, 73.5)]
        [TestCase("Series Ch. 1-73 (Digital)", "Series", "en", 1, 73)]
        [TestCase("Series Chapters 1-73 (Digital)", "Series", "en", 1, 73)]
        [TestCase("Série Chapitre 12 (Digital)", "Série", "fr", 12, 12)]
        [TestCase("Serie Kapitel 12 (Digital)", "Serie", "de", 12, 12)]
        [TestCase("シリーズ 第12話 (Digital)", "シリーズ", "ja", 12, 12)]
        [TestCase("시리즈 12화 (Digital)", "시리즈", "ko", 12, 12)]
        [TestCase("系列 第12话 (Digital)", "系列", "zh", 12, 12)]
        [TestCase("系列 第12回 (Digital)", "系列", "zh", 12, 12)]
        public void chapter_release_parses_its_range(string title, string series, string language, double start, double end)
        {
            var range = ChapterReleaseParser.ParseTitle(title, series, NoAliases, language);

            range.Should().NotBeNull();
            range.Start.Should().Be(start);
            range.End.Should().Be(end);
            range.IsDigital.Should().BeTrue();
            range.IsScanlation.Should().BeFalse();
        }

        [TestCase("Marriagetoxin c001-042 as v01-v05 + c043-058 (Digital-Compilation) (empyrealarrow)", "Marriagetoxin")]
        [TestCase("Series v05 (Digital)", "Series")]
        [TestCase("Re ZERO Starting Life in Another World Chapter 4 The Sanctuary and the Witch of Greed (Digital)", "Re:ZERO -Starting Life in Another World- Chapter 4: The Sanctuary and the Witch of Greed")]
        [TestCase("Series (C102) (Digital)", "Series")]
        public void not_a_chapter_release(string title, string series)
        {
            ChapterReleaseParser.ParseTitle(title, series, NoAliases, "en").Should().BeNull();
        }

        // A market's own volume token is never a chapter.
        [TestCase("시리즈 3권 (Digital)", "시리즈", "ko")]
        [TestCase("系列 第3集 (Digital)", "系列", "zh")]
        [TestCase("シリーズ 第3巻 (Digital)", "シリーズ", "ja")]
        public void edition_volume_token_is_not_a_chapter(string title, string series, string language)
        {
            ChapterReleaseParser.ParseTitle(title, series, NoAliases, language).Should().BeNull();
        }

        // The alias leads, but the rest is another arc's "Chapter 3", not this series' chapters.
        [Test]
        public void another_arc_behind_an_alias_is_not_a_chapter_release()
        {
            ChapterReleaseParser.ParseTitle("Series Chapter 3 Other Arc (Digital)", "Series Chapter 2 Arc", new[] { "Series" }, "en")
                .Should().BeNull();
        }

        [Test]
        public void another_arc_behind_an_alias_is_not_attributed_as_chapters()
        {
            var author = new Author { Metadata = new AuthorMetadata { Name = "Series Chapter 2 Arc", Aliases = new List<string> { "Series" } } };

            var result = Parser.Parser.ParseBookTitleWithSearchCriteria("Series Chapter 3 Other Arc (Digital)", author, new List<Book>());

            (result?.ChapterRange).Should().BeNull();
        }

        // A volume-tokened compilation stays with the volume bridge, exactly as before: its series text
        // before the first volume token ("Marriagetoxin c001-042 as") matches no series, so it is rejected
        // there and never reaches the chapter bridge.
        [Test]
        public void chapter_compilation_with_volume_range_never_takes_the_chapter_path()
        {
            var title = "Marriagetoxin c001-042 as v01-v05 + c043-058 (Digital-Compilation) (empyrealarrow)";
            var parsed = new NzbDrone.Core.Parser.Model.ParsedBookInfo();
            MangaVolumeParser.ParseVolume(title, parsed);

            parsed.VolumeStart.Should().Be(1);
            parsed.VolumeEnd.Should().Be(5);
            Parser.Parser.ParseBookTitleWithSearchCriteria(title, new Author { Name = "Marriagetoxin" }, new List<Book>()).Should().BeNull();
        }

        [Test]
        public void an_alias_with_its_own_chapter_number_is_stripped_too()
        {
            ChapterReleaseParser.ParseTitle("ReZERO Chapter 4 (Digital)", "Re:ZERO -Starting Life in Another World- Chapter 4: The Sanctuary and the Witch of Greed", new[] { "ReZERO Chapter 4" }, "en")
                .Should().BeNull();
        }

        [Test]
        public void scans_are_flagged_as_scanlation()
        {
            var range = ChapterReleaseParser.ParseTitle("Series c010 (Digital) (scans)", "Series", NoAliases, "en");

            range.Should().NotBeNull();
            range.IsDigital.Should().BeTrue();
            range.IsScanlation.Should().BeTrue();
        }

        [Test]
        public void a_web_release_is_not_digital()
        {
            var range = ChapterReleaseParser.ParseTitle("Series c010 (web)", "Series", NoAliases, "en");

            range.Should().NotBeNull();
            range.Start.Should().Be(10);
            range.IsDigital.Should().BeFalse();
        }

        [TestCase("Marriagetoxin c072 (Digital) (empyrealarrow).cbz", 72)]
        [TestCase("Marriagetoxin c073.5 (Digital) (empyrealarrow).cbz", 73.5)]
        [TestCase("Re ZERO Starting Life in Another World Chapter 4 The Sanctuary c010 (Digital).cbz", 10)]
        [TestCase("Series c010-010 (Digital).cbz", 10)]
        public void chapter_file_parses_its_number(string fileName, double expected)
        {
            ChapterReleaseParser.ParseFile(fileName, "en").Should().Be(expected);
        }

        [Test]
        public void edition_chapter_file_parses_its_number()
        {
            ChapterReleaseParser.ParseFile("Série Chapitre 12.cbz", "fr").Should().Be(12);
        }

        [TestCase("Series v09.cbz")]
        [TestCase("Series v09 c072.cbz")]
        [TestCase("Series.cbz")]
        [TestCase("Series c001-006 (Digital).cbz")]
        [TestCase("Series Chapters 1-6.cbz")]
        public void volume_tokenless_or_ranged_file_has_no_chapter(string fileName)
        {
            ChapterReleaseParser.ParseFile(fileName, "en").Should().BeNull();
        }

        // The batch bridge must not swallow a chapter release as a 1-9999 whole-series pack.
        [Test]
        public void chapter_release_searched_for_its_series_is_a_chapter_range_not_a_batch()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria("Marriagetoxin c001-073 (Digital) (empyrealarrow)", new Author { Name = "Marriagetoxin" }, new List<Book>());

            result.Should().NotBeNull();
            result.AuthorName.Should().Be("Marriagetoxin");
            result.ChapterRange.Should().NotBeNull();
            result.ChapterRange.Start.Should().Be(1);
            result.ChapterRange.End.Should().Be(73);
            result.VolumeStart.Should().BeNull();
            result.VolumeEnd.Should().BeNull();
            result.VolumeNumber.Should().BeNull();
            result.Quality.Quality.Should().Be(Quality.CbzChapters);
        }

        // The series' own "Chapter 4" is no chapter range: the tokenless arc batch stays a batch.
        [Test]
        public void a_series_named_chapter_n_still_bridges_as_a_batch()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria(
                "Re ZERO Starting Life in Another World Chapter 4 The Sanctuary and the Witch of Greed (Digital)",
                new Author { Name = "Re:ZERO -Starting Life in Another World- Chapter 4: The Sanctuary and the Witch of Greed" },
                new List<Book>());

            result.Should().NotBeNull();
            result.ChapterRange.Should().BeNull();
            result.VolumeStart.Should().Be(1);
        }

        [Test]
        public void chapter_release_of_another_series_is_not_attributed()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria("Tokyo Ghoul re c010 (Digital)", new Author { Name = "Tokyo Ghoul" }, new List<Book>());

            (result?.ChapterRange).Should().BeNull();
        }

        [Test]
        public void chapter_release_is_not_parsed_for_a_light_novel()
        {
            var author = new Author { Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord~ln" } };

            var result = Parser.Parser.ParseBookTitleWithSearchCriteria("Overlord c010 (Digital)", author, new List<Book>());

            (result?.ChapterRange).Should().BeNull();
        }

        [Test]
        public void volume_tokened_release_keeps_no_chapter_range()
        {
            var result = Parser.Parser.ParseBookTitleWithSearchCriteria("Marriagetoxin v05 (Digital) (empyrealarrow)", new Author { Name = "Marriagetoxin" }, new List<Book>());

            result.Should().NotBeNull();
            result.VolumeNumber.Should().Be(5);
            result.ChapterRange.Should().BeNull();
        }

        [Test]
        public void built_from_chapters_cbz_is_cbz_chapters_quality()
        {
            QualityParser.ParseQuality("Marriagetoxin v01 (Built from chapters 1-9).cbz").Quality.Should().Be(Quality.CbzChapters);
        }

        [Test]
        public void a_plain_cbz_stays_cbz()
        {
            QualityParser.ParseQuality("Marriagetoxin v01 (Digital) (empyrealarrow).cbz").Quality.Should().Be(Quality.CBZ);
        }
    }
}
