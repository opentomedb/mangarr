using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    // AliasQueries feeds the recall search tier. AniList synonyms mix useful romaji /
    // abbreviation aliases with European licensed titles ("Guardianes de la Noche",
    // "In dieser Welt mach ich alles anders") that never match releases on English/romaji-named
    // indexers and only burn rate-limited API calls — those must be filtered out BEFORE the
    // two-alias cap so good aliases behind them still get searched.
    [TestFixture]
    public class BookSearchCriteriaFixture : CoreTest
    {
        private static BookSearchCriteria CriteriaWithAliases(params string[] aliases)
        {
            var author = new Author { Name = "Test Series" };
            author.Metadata.Value.Aliases = new List<string>(aliases);

            return new BookSearchCriteria
            {
                Author = author,
                BookTitle = "Test Series Vol. 5",
                VolumeNumber = 5
            };
        }

        [Test]
        public void alias_queries_should_skip_foreign_language_aliases()
        {
            var criteria = CriteriaWithAliases(
                "Mushoku Tensei: In dieser Welt mach ich alles anders",
                "Mushoku Tensei: Isekai Ittara Honki Dasu");

            criteria.AliasQueries.Should().HaveCount(1);
            criteria.AliasQueries[0].Should().Contain("Isekai");
        }

        [Test]
        public void alias_queries_should_skip_non_ascii_aliases()
        {
            var criteria = CriteriaWithAliases(
                "Miecz zabójcy demonów",
                "Kimetsu no Yaiba");

            criteria.AliasQueries.Should().HaveCount(1);
            criteria.AliasQueries[0].Should().Contain("Kimetsu");
        }

        [Test]
        public void alias_queries_should_backfill_after_filtering()
        {
            // The junk alias is FIRST: filtering must happen before the two-alias cap so the
            // two good aliases behind it are both searched.
            var criteria = CriteriaWithAliases(
                "Guardianes de la Noche",
                "Kimetsu no Yaiba",
                "KnY");

            criteria.AliasQueries.Should().HaveCount(2);
            criteria.AliasQueries[0].Should().Contain("Kimetsu");
            criteria.AliasQueries[1].Should().Contain("KnY");
        }

        [Test]
        public void alias_queries_should_keep_at_most_two_good_aliases()
        {
            var criteria = CriteriaWithAliases("Kaijuu 8-gou", "Monster #8", "8Kaijuu");

            criteria.AliasQueries.Should().HaveCount(2);
        }

        // Light-novel audio (2026-09-18, D4): the subtitle tier -- "<Series> <N>: <Subtitle>" and the
        // Audible title when it says something else -- exists for an Audio volume search only.
        // GetQueryTitle joins with '+' and drops the colon, exactly as it does for BookQuery.

        private static BookSearchCriteria VolumeCriteria(MediaType mediaType, string series, double volume, string subtitle, string audiobookTitle, params string[] aliases)
        {
            var author = new Author { Name = series };
            author.Metadata.Value.Aliases = new List<string>(aliases);

            return new BookSearchCriteria
            {
                Author = author,
                BookTitle = $"{series} Vol. {volume}",
                VolumeNumber = volume,
                MediaType = mediaType,
                Subtitle = subtitle,
                AudiobookTitle = audiobookTitle
            };
        }

        [Test]
        public void subtitle_queries_are_series_number_subtitle_then_audiobook_title()
        {
            // SAO 21: the Audible title equals the bare form ("Sword Art Online 21") -> not repeated.
            var criteria = VolumeCriteria(MediaType.Audio, "Sword Art Online", 21, "Unital Ring I", "Sword Art Online 21");

            criteria.SubtitleQueries.Should().Equal(SearchCriteriaBase.GetQueryTitle("Sword Art Online 21: Unital Ring I"));
            criteria.SubtitleQueries[0].Should().Be("Sword+Art+Online+21+Unital+Ring+I");
            criteria.SubtitleQueries[0].Should().NotBe(criteria.BookQueryBare);
        }

        [Test]
        public void audiobook_title_that_differs_is_a_second_query()
        {
            // TBATE Vol 1: no subtitle, Audible calls the product "Early Years".
            var criteria = VolumeCriteria(MediaType.Audio, "The Beginning After the End", 1, null, "Early Years");

            criteria.SubtitleQueries.Should().Equal("Early+Years");
        }

        [Test]
        public void audiobook_title_equal_to_the_subtitle_query_is_not_repeated()
        {
            // SAO 1: Audible's "Sword Art Online 1: Aincrad" IS the subtitle query once cleaned.
            var criteria = VolumeCriteria(MediaType.Audio, "Sword Art Online", 1, "Aincrad", "Sword Art Online 1: Aincrad");

            criteria.SubtitleQueries.Should().Equal("Sword+Art+Online+1+Aincrad");
        }

        [Test]
        public void a_subtitle_that_names_the_series_is_not_a_query()
        {
            var criteria = VolumeCriteria(MediaType.Audio, "Sword Art Online", 1, "Aincrad", null, "Aincrad");

            criteria.SubtitleQueries.Should().BeEmpty();
        }

        // Full-title subtitles (2026-09-24): Rascal vol 2's subtitle IS the volume's whole title.
        // Before: ["Rascal+Does+Not+Dream+2+Rascal+Does+Not+Dream+of+Petite+Devil+Kohai",
        // "Rascal+Does+Not+Dream+of+Petite+Devil+Kohai"] -- two queries, the first naming no release.
        // After: the full title once.
        [Test]
        public void a_full_title_subtitle_is_the_query_itself_and_the_same_audible_title_is_not_repeated()
        {
            var criteria = VolumeCriteria(MediaType.Audio, "Rascal Does Not Dream", 2, "Rascal Does Not Dream of Petite Devil Kohai", "Rascal Does Not Dream of Petite Devil Kohai");

            criteria.SubtitleQueries.Should().Equal("Rascal+Does+Not+Dream+of+Petite+Devil+Kohai");
            criteria.SubtitleQueries.Should().NotContain("Rascal+Does+Not+Dream+2+Rascal+Does+Not+Dream+of+Petite+Devil+Kohai");
            criteria.SubtitleQueries.Should().OnlyHaveUniqueItems();
        }

        // Audible's vol 16 product is "Rascal Does Not Dream of a Beach Queen + (light novel)": with
        // the edition tag cut it is the full-title query, so it is not a second query.
        [Test]
        public void an_audible_title_that_is_the_full_title_plus_an_edition_tag_is_not_a_second_query()
        {
            var criteria = VolumeCriteria(MediaType.Audio, "Rascal Does Not Dream", 16, "Rascal Does Not Dream of a Beach Queen +", "Rascal Does Not Dream of a Beach Queen + (light novel)");

            criteria.SubtitleQueries.Should().Equal("Rascal+Does+Not+Dream+of+a+Beach+Queen");
        }

        [Test]
        public void manga_and_epub_have_no_subtitle_queries()
        {
            VolumeCriteria(MediaType.Archive, "Sword Art Online", 21, "Unital Ring I", "Sword Art Online 21").SubtitleQueries.Should().BeEmpty();
            VolumeCriteria(MediaType.Ebook, "Sword Art Online", 21, "Unital Ring I", "Early Years").SubtitleQueries.Should().BeEmpty();
        }
    }
}
