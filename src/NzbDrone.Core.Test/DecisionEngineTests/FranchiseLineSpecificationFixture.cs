using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class FranchiseLineSpecificationFixture : CoreTest<FranchiseLineSpecification>
    {
        private const string Progressive = "Sword Art Online - Progressive - Volume 02 [Yen Press][OCR Mamue]";
        private const string Reason = "Release names a different line of the franchise: ";

        private static Author GivenAuthor(string name, LibraryType library, params string[] aliases)
        {
            var author = new Author { Id = 1, CleanName = LibraryTypes.CleanNameFor(name.CleanAuthorName(), library) };
            author.Metadata.Value.Name = name;
            author.Metadata.Value.ForeignAuthorId = LibraryTypes.WithType("local-" + name.CleanAuthorName(), library);
            author.Metadata.Value.Aliases = new List<string>(aliases);

            return author;
        }

        private static RemoteBook GivenRelease(Author author, string title)
        {
            return new RemoteBook
            {
                Author = author,
                Release = new ReleaseInfo { Title = title },
                ParsedBookInfo = new ParsedBookInfo { ReleaseTitle = title },
                Books = new List<Book> { new Book { Id = 2, VolumeNumber = 2 } }
            };
        }

        // 2026-09-21: "Series", "Light Novels" and the like are edition words, not lines of the franchise.
        [TestCase("Rascal Does Not Dream Series v01-16 [Audiobook] [Yen Audio] [Stick & Ush]")]
        [TestCase("Rascal Does Not Dream Light Novels v01-15 [EPUB]")]
        [TestCase("Rascal Does Not Dream English Light Novels Vol. 1-15")]
        [TestCase("Rascal Does Not Dream Complete Collection v01-16")]
        public void generic_edition_words_are_not_a_different_line(string title)
        {
            Subject.IsSatisfiedBy(GivenRelease(GivenAuthor("Rascal Does Not Dream", LibraryType.LightNovel), title), null)
                .Accepted.Should().BeTrue();
        }

        [Test]
        public void progressive_release_is_rejected_for_the_main_line_light_novel()
        {
            var decision = Subject.IsSatisfiedBy(GivenRelease(GivenAuthor("Sword Art Online", LibraryType.LightNovel), Progressive), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(Reason + "'Progressive'");
        }

        [Test]
        public void progressive_release_is_accepted_for_the_progressive_light_novel()
        {
            Subject.IsSatisfiedBy(GivenRelease(GivenAuthor("Sword Art Online Progressive", LibraryType.LightNovel), Progressive), null)
                   .Accepted.Should().BeTrue();
        }

        [Test]
        public void multi_word_sub_series_segment_is_rejected_and_named()
        {
            var decision = Subject.IsSatisfiedBy(
                GivenRelease(GivenAuthor("Sword Art Online", LibraryType.LightNovel), "Sword Art Online - Alternative Gun Gale Online - Volume 01"),
                null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(Reason + "'Alternative Gun Gale Online'");
        }

        [TestCase("Sword Art Online Vol. 5")]
        [TestCase("Sword Art Online - Volume 17 - Alicization Awakening [Yen On]")]
        [TestCase("Sword Art Online v05 (Yen Press)")]
        [TestCase("Sword Art Online 05 - Phantom Bullet")]
        [TestCase("Sword Art Online (Light Novel) Vol. 5 by Reki Kawahara [ENG / EPUB]")]
        [TestCase("Seven.Seas.Entertainment-Sword.Art.Online.Vol.05.2019.Hybrid.eBook-BitBook")]
        [TestCase("Yen.On.Sword.Art.Online.Vol.05.2019.Hybrid.eBook-BitBook")]
        public void main_line_forms_are_accepted(string title)
        {
            Subject.IsSatisfiedBy(GivenRelease(GivenAuthor("Sword Art Online", LibraryType.LightNovel), title), null)
                   .Accepted.Should().BeTrue();
        }

        // Scene ebook naming leads with the publisher, hyphen- or dot-joined; the search bridge
        // shaves a known publisher / retries after each hyphen, and so does this spec.
        [TestCase("Seven.Seas.Entertainment-Sword.Art.Online.Progressive.Vol.02.2019.Hybrid.eBook-BitBook")]
        [TestCase("Yen.On.Sword.Art.Online.Progressive.Vol.02.2019.Hybrid.eBook-BitBook")]
        public void publisher_prefixed_sub_series_is_still_rejected(string title)
        {
            var decision = Subject.IsSatisfiedBy(GivenRelease(GivenAuthor("Sword Art Online", LibraryType.LightNovel), title), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(Reason + "'Progressive'");
        }

        [Test]
        public void dual_title_whose_half_is_the_series_is_accepted()
        {
            var author = GivenAuthor("That Time I Got Reincarnated as a Slime", LibraryType.LightNovel);

            Subject.IsSatisfiedBy(GivenRelease(author, "That Time I Got Reincarnated as a Slime / Tensei Shitara Slime Datta Ken v05 (Light Novel)"), null)
                   .Accepted.Should().BeTrue();
        }

        [Test]
        public void alias_counts_as_the_series_name()
        {
            var author = GivenAuthor("That Time I Got Reincarnated as a Slime", LibraryType.LightNovel, "Tensura");

            Subject.IsSatisfiedBy(GivenRelease(author, "Tensura v05"), null).Accepted.Should().BeTrue();

            var decision = Subject.IsSatisfiedBy(GivenRelease(author, "Tensura - Trinity in Tempest v01"), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(Reason + "'Trinity in Tempest'");
        }

        // Manga never enters this spec: the manga guard lives in ParsingService.GetAuthor
        // (should_reject_sequel_release_whose_series_extends_the_searched_author) and is unchanged.
        [TestCase(Progressive)]
        [TestCase("Sword Art Online v05 (Digital)")]
        public void manga_author_is_never_judged_here(string title)
        {
            Subject.IsSatisfiedBy(GivenRelease(GivenAuthor("Sword Art Online", LibraryType.Manga), title), null)
                   .Accepted.Should().BeTrue();
        }
    }
}
