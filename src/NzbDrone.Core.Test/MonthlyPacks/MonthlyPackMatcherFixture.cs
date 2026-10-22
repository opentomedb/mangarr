using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.MonthlyPacks;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MonthlyPacks
{
    [TestFixture]
    public class MonthlyPackMatcherFixture : CoreTest<MonthlyPackMatcher>
    {
        private static int _id;

        private static Book Volume(string series, double volume, string language = "en", params string[] aliases)
        {
            var id = ++_id;
            var author = new Author
            {
                Id = id,
                Metadata = new AuthorMetadata { Name = series, EditionLanguage = language, Aliases = aliases.ToList() }
            };

            return new Book { Id = id, VolumeNumber = volume, Title = $"{series} Vol. {volume}", Author = author };
        }

        private List<PackFileMatch> Run(List<Book> eligible, params string[] paths)
        {
            var files = paths.Select((p, i) => new TorrentFileEntry(i, p, 1000)).ToList();
            return Subject.Match(files, eligible);
        }

        private List<Book> Eligible() => new List<Book>
        {
            Volume("Kaiju No. 8", 16),
            Volume("Kaiju No. 8: Relax", 2),
            Volume("Akira Failing in Love", 4)
        };

        [Test]
        public void should_match_the_longer_series_name()
        {
            var eligible = Eligible();
            var result = Run(eligible, "Kaiju No. 8 - Relax v02 (2026) (Digital) (Rillant).cbz");

            result.Should().ContainSingle().Which.Book.Should().BeSameAs(eligible[1]);
        }

        [Test]
        public void should_match_the_shorter_series_name()
        {
            var eligible = Eligible();
            var result = Run(eligible, "Kaiju No. 8 v16 (2026) (Digital) (Rillant).cbz");

            result.Should().ContainSingle().Which.Book.Should().BeSameAs(eligible[0]);
        }

        [Test]
        public void should_match_a_file_inside_a_subfolder()
        {
            var eligible = Eligible();
            var result = Run(eligible, "Pack/Akira Failing in Love v04 (2026) (Digital) (Rillant).cbz");

            result.Should().ContainSingle().Which.Book.Should().BeSameAs(eligible[2]);
            result[0].Author.Should().BeSameAs(eligible[2].Author.Value);
            result[0].File.Index.Should().Be(0);
        }

        [Test]
        public void should_not_match_a_series_nobody_follows()
        {
            Run(Eligible(), "Some Other Series v01 (2026) (Digital) (Rillant).cbz").Should().BeEmpty();
        }

        [Test]
        public void should_not_match_a_volume_range()
        {
            Run(Eligible(), "Akira Failing in Love v01-03 (2026) (Digital).cbz").Should().BeEmpty();
        }

        [Test]
        public void should_not_match_a_chapter_file()
        {
            Run(Eligible(), "Akira Failing in Love c021 (2026) (Digital).cbz").Should().BeEmpty();
        }

        [Test]
        public void should_not_match_a_volume_that_is_not_eligible()
        {
            Run(Eligible(), "Akira Failing in Love v05 (2026) (Digital).cbz").Should().BeEmpty();
        }

        [Test]
        public void should_not_match_when_two_followed_series_fit_equally()
        {
            var eligible = new List<Book> { Volume("Dandadan", 5), Volume("Dan Da Dan Remake", 5, "en", "Dandadan") };

            Run(eligible, "Dandadan v05 (2026) (Digital).cbz").Should().BeEmpty();
        }

        [Test]
        public void should_match_through_an_alias()
        {
            var eligible = new List<Book> { Volume("Boku no Hero", 3, "en", "My Hero Pack") };

            Run(eligible, "My Hero Pack v03 (2026) (Digital).cbz").Should().ContainSingle();
        }

        [TestCase("Akira Failing in Love v04 (2026).txt")]
        [TestCase("Akira Failing in Love v04 (2026).nfo")]
        [TestCase("Akira Failing in Love v04 (2026).jpg")]
        public void should_ignore_non_comic_files(string path)
        {
            Run(Eligible(), path).Should().BeEmpty();
        }

        [TestCase("Naruto (3-in-1 Edition) v12 (2026) (Digital) (Rillant).cbz")]
        [TestCase("Naruto (Omnibus) v12 (2026) (Digital) (Rillant).cbz")]
        [TestCase("Naruto (Deluxe) v12 (2026) (Digital) (Rillant).cbz")]
        [TestCase("Naruto (Box Set) v12 (2026) (Digital) (Rillant).cbz")]
        [TestCase("Naruto (Collector's Edition) v12 (2026) (Digital) (Rillant).cbz")]
        [TestCase("Naruto (3 in 1) v12 (2026) (Digital) (Rillant).cbz")]
        public void should_not_match_another_edition_through_its_stripped_name(string path)
        {
            var eligible = new List<Book> { Volume("Naruto", 12) };

            Run(eligible, path).Should().BeEmpty();
        }

        [Test]
        public void should_match_the_plain_volume_beside_another_edition()
        {
            var eligible = new List<Book> { Volume("Naruto", 12) };

            Run(eligible, "Naruto v12 (2026) (Digital) (Rillant).cbz").Should().ContainSingle().Which.Book.Should().BeSameAs(eligible[0]);
        }

        [Test]
        public void should_match_an_edition_series_by_its_full_name()
        {
            var eligible = new List<Book> { Volume("Naruto (3-in-1 Edition)", 4) };

            Run(eligible, "Naruto (3-in-1 Edition) v04 (2026) (Digital) (Rillant).cbz").Should().ContainSingle();
        }

        [Test]
        public void should_not_match_when_two_wanted_volumes_of_the_series_share_the_number()
        {
            var first = Volume("Kaiju No. 8", 16);
            var second = new Book { Id = ++_id, VolumeNumber = 16, Title = "Kaiju No. 8 Vol. 16 (Special)", Author = first.Author };

            Run(new List<Book> { first, second }, "Kaiju No. 8 v16 (2026) (Digital) (Rillant).cbz").Should().BeEmpty();
        }

        [Test]
        public void should_not_match_a_french_edition_file_without_a_french_marker()
        {
            var eligible = new List<Book> { Volume("Attack on Titan", 5, "fr") };

            Run(eligible, "Attack on Titan v05 (2026) (Digital).cbz").Should().BeEmpty();
        }
    }
}
