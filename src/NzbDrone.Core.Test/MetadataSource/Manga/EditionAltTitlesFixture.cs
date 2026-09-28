using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    // Preferred Edition (2026-09-24, spec §2.2 Aliases): a non-English series' alternate titles -- the
    // edition line's own-language rows first (official, then romanized, then the rest), then AniList,
    // then the English anchor; cap 6; native script allowed for a Japanese edition only; raw
    // list-article names ("Liste des chapitres de ...") never.
    [TestFixture]
    public class EditionAltTitlesFixture : CoreTest
    {
        private static readonly List<GcdAlias> FrenchRows = new List<GcdAlias>
        {
            new GcdAlias { Alias = "Attack on Titan", Kind = "line" },
            new GcdAlias { Alias = "Liste des chapitres de L'Attaque des Titans", Language = "fr", Kind = "official" },
            new GcdAlias { Alias = "L'Attaque des Titans", Language = "fr", Kind = "official" },
            new GcdAlias { Alias = "l attaque des titans", Language = "fr", Kind = "official" },
            new GcdAlias { Alias = "Shingeki no Kyojin", Language = "en", Kind = "alias" },
            new GcdAlias { Alias = "進撃の巨人", Language = "ja", Kind = "official" }
        };

        private static readonly AniListSeries Ani = new AniListSeries
        {
            EnglishTitle = "Attack on Titan", RomajiTitle = "Shingeki no Kyojin", NativeTitle = "進撃の巨人",
            Synonyms = new List<string> { "AoT", "L'Attaque des Titans", "Ataque a los Titanes", "Angriff auf Titan" }
        };

        [Test]
        public void french_rows_first_then_anilist_then_the_anchor()
        {
            var titles = MangaSeriesMetadataProvider.BuildEditionAltTitles("L'Attaque des Titans", "Attack on Titan", FrenchRows, "fr", Ani);

            titles.Should().Equal("Attack on Titan", "Shingeki no Kyojin", "Ataque a los Titanes", "Angriff auf Titan");
        }

        [Test]
        public void a_japanese_edition_keeps_the_native_title()
        {
            var titles = MangaSeriesMetadataProvider.BuildEditionAltTitles("Shingeki no Kyojin", "Attack on Titan", FrenchRows, "ja", Ani);

            titles.Should().Contain("進撃の巨人");
            titles.Should().Contain("Attack on Titan");
        }

        [Test]
        public void at_most_six()
        {
            var many = new AniListSeries { Synonyms = new List<string> { "One A", "Two B", "Three C", "Four D", "Five E", "Six F", "Seven G" } };

            MangaSeriesMetadataProvider.BuildEditionAltTitles("Local", "Anchor Name", new List<GcdAlias>(), "fr", many).Should().HaveCount(6);
        }

        // M6b fix round 1 (2026-09-24, I1): the cap never pushes out the English anchor -- the parser
        // accepts Name + Aliases, so an English-named "[FR]" release needs it. AniList (and the edition's
        // rows) stop one short while the anchor is still new.
        [Test]
        public void the_cap_keeps_a_slot_for_the_anchor()
        {
            var many = new AniListSeries { Synonyms = new List<string> { "One A", "Two B", "Three C", "Four D", "Five E", "Six F", "Seven G" } };

            var titles = MangaSeriesMetadataProvider.BuildEditionAltTitles("Local", "Anchor Name", new List<GcdAlias>(), "fr", many);

            titles.Should().Contain("Anchor Name");
            titles.Count.Should().BeLessOrEqualTo(6);
            titles.Should().Equal("One A", "Two B", "Three C", "Four D", "Five E", "Anchor Name");
        }

        [Test]
        public void own_language_rows_also_leave_the_anchor_its_slot()
        {
            var rows = new List<GcdAlias>();
            for (var i = 1; i <= 7; i++)
            {
                rows.Add(new GcdAlias { Alias = "Titre Numero " + i, Language = "fr", Kind = "official" });
            }

            var titles = MangaSeriesMetadataProvider.BuildEditionAltTitles("Local", "Anchor Name", rows, "fr", null);

            titles.Should().HaveCount(6);
            titles.Last().Should().Be("Anchor Name");
        }

        // An anchor AniList already supplied needs no reserved slot: the full cap goes to titles.
        [Test]
        public void an_anchor_already_seen_reserves_nothing()
        {
            var many = new AniListSeries { EnglishTitle = "Anchor Name", Synonyms = new List<string> { "One A", "Two B", "Three C", "Four D", "Five E", "Six F" } };

            MangaSeriesMetadataProvider.BuildEditionAltTitles("Local", "Anchor Name", new List<GcdAlias>(), "fr", many)
                .Should().Equal("Anchor Name", "One A", "Two B", "Three C", "Four D", "Five E");
        }

        // Controller ruling (2026-09-24): the catalogue tags a same-spelled title with the alphabetically
        // first of its languages, so a French title can arrive tagged "de". Language orders the rows,
        // it never excludes one: the other-language rows follow the anchor, inside the cap.
        [Test]
        public void a_row_tagged_under_another_language_is_still_a_title_after_the_anchor()
        {
            var rows = new List<GcdAlias>
            {
                new GcdAlias { Alias = "Les Titans", Language = "de", Kind = "official" },
                new GcdAlias { Alias = "Titans Attaque", Language = "fr", Kind = "alias" },
                new GcdAlias { Alias = "Titans Officiel", Language = "fr", Kind = "official" }
            };

            var titles = MangaSeriesMetadataProvider.BuildEditionAltTitles("L'Attaque des Titans", "Attack on Titan", rows, "fr", null);

            titles.Should().Equal("Titans Officiel", "Titans Attaque", "Attack on Titan", "Les Titans");
        }

        [Test]
        public void an_older_artifact_without_rows_still_has_anilist_and_the_anchor()
        {
            var titles = MangaSeriesMetadataProvider.BuildEditionAltTitles("L'Attaque des Titans", "Attack on Titan", null, "fr", null);

            titles.Should().Equal("Attack on Titan");
        }

        // Ruling S2 (2026-09-24): the shared dedupe -- a title is a duplicate only when BOTH keys (the
        // TitleMatcher key and the parser's CleanAuthorName key) were already seen.
        [Test]
        public void a_parser_distinct_spelling_is_kept_as_build_alt_titles_keeps_it()
        {
            var ani = new AniListSeries { Synonyms = new List<string> { "Ranma ½" } };

            MangaSeriesMetadataProvider.BuildEditionAltTitles("Ranma 1/2", "Ranma 1/2", null, "fr", ani)
                .Should().Equal(MangaSeriesMetadataProvider.BuildAltTitles("Ranma 1/2", ani));
        }
    }
}
