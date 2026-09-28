using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.PreferredEdition
{
    // Preferred Edition (2026-09-24): English series' search queries, parses and display strings.
    // The adversarial titles ("Tome 5", "Band 2", "T05", "5巻") are what the edition tokens will
    // recognise for a French/German/Japanese series -- for an English one they must read as today.
    [TestFixture]
    public class EnEditionPinningFixture : CoreTest
    {
        private static Author Series(string name, params string[] aliases)
        {
            var author = new Author { Name = name };
            author.Metadata.Value.Aliases = new List<string>(aliases);
            return author;
        }

        private static object Queries(BookSearchCriteria c)
        {
            return new
            {
                c.Author.Name,
                c.VolumeNumber,
                c.MediaType,
                c.BookQuery,
                c.BookQueryAlt,
                c.BookQueryBare,
                c.FieldedTitleQuery,
                c.AliasQueries,
                c.SubtitleQueries
            };
        }

        [Test]
        public void search_criteria_are_pinned()
        {
            var criteria = new List<BookSearchCriteria>
            {
                new BookSearchCriteria { Author = Series("Kaiju No. 8", "Kaijuu 8-gou", "Guardianes de la Noche", "Monster #8"), BookTitle = "Kaiju No. 8 Vol. 5", VolumeNumber = 5 },
                new BookSearchCriteria { Author = Series("The Apothecary Diaries", "Kusuriya no Hitorigoto"), BookTitle = "The Apothecary Diaries Vol. 12", VolumeNumber = 12 },
                new BookSearchCriteria { Author = Series("Re:ZERO -Starting Life in Another World-, Chapter 1: A Day in the Capital"), BookTitle = "x", VolumeNumber = 2 },
                new BookSearchCriteria { Author = Series("Kaiju No. 8"), BookTitle = "Kaiju No. 8 Vol. 3.5", VolumeNumber = 3.5 },
                new BookSearchCriteria { Author = Series("L'Attaque des Titans", "Shingeki no Kyojin"), BookTitle = "x", VolumeNumber = 5 },
                new BookSearchCriteria
                {
                    Author = Series("Sword Art Online", "SAO"),
                    BookTitle = "Sword Art Online Vol. 2",
                    VolumeNumber = 2,
                    MediaType = MediaType.Audio,
                    Subtitle = "Aincrad",
                    AudiobookTitle = "Sword Art Online 2: Aincrad (light novel)"
                }
            };

            EnGolden.Pin("search-criteria", criteria.Select(Queries).ToList());
        }

        private static readonly string[] ReleaseTitles =
        {
            "Attack on Titan v05 (2013) (Digital) (danke-Empire)",
            "Attack on Titan Vol. 1-34 (Kodansha) [Stick]",
            "Attack.on.Titan.Vol.12.2014.Hybrid.eBook-GROUP",
            "Shingeki no Kyojin v12",
            "Attack on Titan Tome 5",
            "Attack on Titan Tomes 1 à 5",
            "Attack on Titan Band 2",
            "Attack on Titan Bd. 2",
            "Attack on Titan T05",
            "Attack on Titan 第5巻",
            "Attack on Titan 5巻",
            "Attack on Titan - Intégrale",
            "Attack on Titan v05 [FR]",
            "Attack on Titan (Before the Fall) v01",
            "Brass Band 2 v01"
        };

        [Test]
        public void search_bridge_parses_are_pinned()
        {
            var author = new Author { Metadata = new AuthorMetadata { Name = "Attack on Titan", Aliases = new List<string> { "Shingeki no Kyojin" } } };

            var parsed = ReleaseTitles.Select(t =>
            {
                var p = Parser.Parser.ParseBookTitleWithSearchCriteria(t, author, new List<Book>());
                return new { Title = t, p?.AuthorName, p?.BookTitle, p?.VolumeNumber, p?.VolumeStart, p?.VolumeEnd };
            }).ToList();

            EnGolden.Pin("parser", parsed);
        }

        [Test]
        public void volume_parser_is_pinned()
        {
            var names = new[]
            {
                "Attack on Titan Vol. 5", "Attack on Titan - Vol 005", "Attack on Titan Tome 5", "L'Attaque des Titans T05",
                "Brass Band 2", "Angriff der Titanen Bd. 3", "Kaiju No. 8 v03.5", "進撃の巨人 第5巻", "進撃の巨人 5巻",
                "Chainsaw Man - Vol. 13 (2023)"
            };

            var parsed = names.Select(n =>
            {
                var ok = MangaVolumeParser.TryParseSeriesVolume(n, out var series, out var volume);
                return new { Name = n, Parsed = ok, Series = series, Volume = volume, Single = MangaVolumeParser.ParseSingleVolume(n), Token = MangaVolumeParser.HasVolumeToken(n) };
            }).ToList();

            EnGolden.Pin("volume-parser", parsed);
        }

        [Test]
        public void light_novel_titles_and_subtitle_filters_are_pinned()
        {
            var titles = new
            {
                Display = new[]
                {
                    LightNovelTitles.Display("Sword Art Online", 1, "Aincrad"),
                    LightNovelTitles.Display("Rascal Does Not Dream", 2, "Rascal Does Not Dream of Petite Devil Kohai"),
                    LightNovelTitles.Display("Overlord", 3.5, null),
                    LightNovelTitles.Display("Tokyo Ghoul", 1, "Tokyo Ghoul: Days"),
                    LightNovelTitles.Display("Overlord", 4, "Tome 4")
                },
                Sort = new[] { LightNovelTitles.SortTitle("Sword Art Online", 11.5), LightNovelTitles.SortTitle("Overlord", 3) },
                Junk = new[] { "Tome 3", "Band 2", "Vol. 1", "Aincrad", "Light Novel, Vol. 5", "第3巻", "Roman" }
                    .Select(c => new { Candidate = c, Junk = Subtitles.IsJunk(c, "Overlord") }).ToList(),
                FullTitle = Subtitles.IsFullTitle("Rascal Does Not Dream of Petite Devil Kohai", "Rascal Does Not Dream")
            };

            EnGolden.Pin("titles", titles);
        }

        [Test]
        public void description_gate_is_pinned()
        {
            const string english = "Kafka Hibino dreams of joining the Defense Force, but a strange creature changes everything.";
            const string french = "Kafka Hibino rêve d'intégrer les Forces de défense, mais une étrange créature change tout.";
            const string japanese = "日比野カフカは防衛隊への入隊を夢見ていたが、謎の生物がすべてを変えてしまう。そして物語が始まる。";

            var cases = new[] { (english, "en"), (english, (string)null), (french, "fr"), (japanese, "ja"), ("Notebook for manga fans with 120 lined pages inside.", "en") }
                .Select(c =>
                {
                    var ok = GoogleBooksService.IsAcceptableDescription(c.Item1, c.Item2, out var reason);
                    return new { Language = c.Item2, Ok = ok, Reason = reason };
                }).ToList();

            EnGolden.Pin("descriptions", cases);
        }
    }
}
