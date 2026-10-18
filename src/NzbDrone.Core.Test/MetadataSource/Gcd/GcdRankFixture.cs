using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.Gcd;

namespace NzbDrone.Core.Test.MetadataSource.Gcd
{
    [TestFixture]
    public class GcdRankFixture
    {
        private static GcdSeries Line(int id, string name, string medium, int volumes, string language = "en")
        {
            return new GcdSeries
            {
                GcdSeriesId = id,
                Name = name,
                Language = language,
                Medium = medium,
                VolumeCount = volumes,
                DatedCount = volumes,
                IsMain = true
            };
        }

        private static readonly List<GcdSeries> SwordArtOnline = new List<GcdSeries>
        {
            Line(1, "Sword Art Online", "manga", 28),
            Line(2, "Sword Art Online", "light_novel", 28)
        };

        [Test]
        public void manga_prefers_the_manga_line_of_a_shared_name()
        {
            GcdMetadataService.Rank(SwordArtOnline, "sword art online", LibraryType.Manga).GcdSeriesId.Should().Be(1);
        }

        [Test]
        public void light_novel_picks_the_novel_line_of_a_shared_name()
        {
            GcdMetadataService.Rank(SwordArtOnline, "sword art online", LibraryType.LightNovel).GcdSeriesId.Should().Be(2);
        }

        [Test]
        public void light_novel_never_falls_through_to_a_manga_line()
        {
            var onlyManga = new List<GcdSeries> { Line(1, "One Piece", "manga", 113) };

            GcdMetadataService.Rank(onlyManga, "one piece", LibraryType.LightNovel).Should().BeNull();
        }

        [Test]
        public void light_novel_ignores_non_english_lines()
        {
            // Mushoku Tensei today: a JA novel line only -> catalogue miss, not a Japanese binding.
            var onlyJapanese = new List<GcdSeries> { Line(4, "Mushoku Tensei", "light_novel", 26, "ja") };

            GcdMetadataService.Rank(onlyJapanese, "mushoku tensei", LibraryType.LightNovel).Should().BeNull();
        }

        [Test]
        public void light_novel_accepts_plain_novel_lines_too()
        {
            var novel = new List<GcdSeries> { Line(3, "Some Novel", "novel", 5) };

            GcdMetadataService.Rank(novel, "some novel", LibraryType.LightNovel).GcdSeriesId.Should().Be(3);
        }

        [Test]
        public void manga_keeps_its_novel_fallback_when_no_manga_line_exists()
        {
            // Unchanged behaviour: live manga entries bound to a novel line today must not re-bind.
            var onlyNovel = new List<GcdSeries> { Line(2, "Overlord", "light_novel", 16) };

            GcdMetadataService.Rank(onlyNovel, "overlord", LibraryType.Manga).GcdSeriesId.Should().Be(2);
        }

        // The Water Magician (OpenTome round C, 2026-10-02): the light novel's line is named the bare title and
        // the manga's line "(Part 1)", which carries the bare title only as an alias. A manga entry must still
        // get the manga line -- the library's own class outranks an exact name in the other class.
        private static readonly List<GcdSeries> WaterMagician = new List<GcdSeries>
        {
            Line(5, "The Water Magician (Part 1)", "manga", 4),
            Line(6, "The Water Magician", "light_novel", 7)
        };

        [Test]
        public void manga_prefers_a_manga_line_over_an_exact_novel_name()
        {
            GcdMetadataService.Rank(WaterMagician, "the water magician", LibraryType.Manga).GcdSeriesId.Should().Be(5);
        }

        [Test]
        public void light_novel_still_takes_the_exact_novel_name()
        {
            GcdMetadataService.Rank(WaterMagician, "the water magician", LibraryType.LightNovel).GcdSeriesId.Should().Be(6);
        }

        [Test]
        public void preferred_edition_ranking_also_puts_the_library_class_first()
        {
            var french = new List<GcdSeries>
            {
                Line(7, "Le Magicien de l'eau (Partie 1)", "manga", 4, "fr"),
                Line(8, "Le Magicien de l'eau", "light_novel", 7, "fr")
            };

            GcdMetadataService.RankInLanguages(french, "le magicien de l eau", LibraryType.Manga, new List<string> { "fr" }).GcdSeriesId.Should().Be(7);
        }

        [Test]
        public void default_library_is_manga()
        {
            GcdMetadataService.Rank(SwordArtOnline, "sword art online").GcdSeriesId.Should().Be(1);
        }
    }
}
