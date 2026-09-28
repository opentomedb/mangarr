using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    [TestFixture]
    public class AltTitlesFixture : CoreTest
    {
        [Test]
        public void should_keep_romaji_and_latin_synonyms_and_drop_non_latin()
        {
            var ani = new AniListSeries
            {
                RomajiTitle = "Kaijuu 8-gou",
                EnglishTitle = "Kaiju No. 8",
                Synonyms = new List<string>
                {
                    "Monster #8",
                    "怪獣8号",
                    "Кайдзю номер восемь"
                }
            };

            var titles = MangaSeriesMetadataProvider.BuildAltTitles("Kaiju No. 8", ani);

            titles.Should().Equal("Kaijuu 8-gou", "Monster #8");
        }

        [Test]
        public void should_drop_variants_of_the_display_name()
        {
            var ani = new AniListSeries
            {
                RomajiTitle = "Boku no Hero Academia",
                Synonyms = new List<string> { "MY HERO ACADEMIA", "My  Hero-Academia" }
            };

            var titles = MangaSeriesMetadataProvider.BuildAltTitles("My Hero Academia", ani);

            titles.Should().Equal("Boku no Hero Academia");
        }

        // Review F3 (2026-09-24): TitleFold makes "Ranma 1/2" key like "Ranma ½", but the parser's
        // CleanAuthorName does not (ranma12 / ranma), so the spelled-out title stays a search and
        // parser key. An accent-only variant is a duplicate under both keys and is still dropped.
        [Test]
        public void a_title_the_parser_keys_differently_is_kept_even_when_the_fold_keys_it_alike()
        {
            var ranma = new AniListSeries { RomajiTitle = "Ranma \u00bd", EnglishTitle = "Ranma 1/2" };
            var yugi = new AniListSeries { RomajiTitle = "Fushigi Y\u016bgi", EnglishTitle = "Fushigi Yugi" };

            MangaSeriesMetadataProvider.BuildAltTitles("Ranma \u00bd", ranma).Should().Equal("Ranma 1/2");
            MangaSeriesMetadataProvider.BuildAltTitles("Fushigi Y\u00fbgi", yugi).Should().BeEmpty();
        }

        [Test]
        public void should_cap_at_four()
        {
            var ani = new AniListSeries
            {
                RomajiTitle = "Romaji Title",
                Synonyms = new List<string> { "Alt One", "Alt Two", "Alt Three", "Alt Four" }
            };

            var titles = MangaSeriesMetadataProvider.BuildAltTitles("Display Name", ani);

            titles.Should().HaveCount(4);
        }

        [Test]
        public void english_title_comes_first_when_it_differs_from_the_display_name()
        {
            var ani = new AniListSeries
            {
                RomajiTitle = "Re:Zero kara Hajimeru Isekai Seikatsu",
                EnglishTitle = "Re:ZERO -Starting Life in Another World-"
            };

            var titles = MangaSeriesMetadataProvider.BuildAltTitles("Re:Zero", ani);

            titles[0].Should().Be("Re:ZERO -Starting Life in Another World-");
            titles[1].Should().Be("Re:Zero kara Hajimeru Isekai Seikatsu");
        }

        [Test]
        public void english_title_that_is_the_display_name_is_skipped()
        {
            var ani = new AniListSeries
            {
                RomajiTitle = "Sword Art Online",
                EnglishTitle = "Sword Art Online",
                Synonyms = new List<string> { "S.A.O" }
            };

            var titles = MangaSeriesMetadataProvider.BuildAltTitles("Sword Art Online", ani);

            titles.Should().Equal("S.A.O");
        }

        [Test]
        public void should_return_empty_when_no_anilist_match()
        {
            MangaSeriesMetadataProvider.BuildAltTitles("Anything", null).Should().BeEmpty();
        }
    }
}
