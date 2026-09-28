using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    [TestFixture]
    public class GetAuthorFixture : CoreTest<ParsingService>
    {
        [Test]
        public void should_use_passed_in_title_when_it_cannot_be_parsed()
        {
            const string title = "30 Rock";

            Subject.GetAuthor(title);

            Mocker.GetMock<IAuthorService>()
                  .Verify(s => s.FindByName(title), Times.Once());
        }

        [Test]
        public void should_use_parsed_author_title()
        {
            const string title = "30 Rock - Get Some [FLAC]";

            Subject.GetAuthor(title);

            Mocker.GetMock<IAuthorService>()
                  .Verify(s => s.FindByName(Parser.Parser.ParseBookTitle(title).AuthorName), Times.Once());
        }

        [Test]
        public void should_attribute_manga_volume_pack_to_searched_series_despite_mangaka_in_title()
        {
            // Manga releases credit the mangaka ("by Junji Ito"), but Author=Series. When we
            // searched for the series and the release's own series matches, it must resolve to
            // the searched series instead of failing with "Unknown Author".
            var author = Builder<Author>.CreateNew()
                                        .With(a => a.CleanName = "gyo")
                                        .Build();

            var searchCriteria = new AuthorSearchCriteria { Author = author };

            var parsed = new ParsedBookInfo
            {
                ReleaseTitle = "Gyo Vol.1 - Vol.2 (2003-2004) (VIZ Media LLC) by Junji Ito [ENG / CBZ]",
                AuthorName = "Junji Ito",
                VolumeStart = 1,
                VolumeEnd = 2,
                Quality = new QualityModel()
            };

            Mocker.GetMock<IBookService>()
                  .Setup(s => s.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book>());

            var remoteBook = Subject.Map(parsed, searchCriteria);

            remoteBook.Author.Should().Be(author);
        }

        [Test]
        public void should_attribute_manga_volume_to_searched_series_despite_format_suffix()
        {
            // "(Light Novel)" / "(Digital)" / "(Omnibus)" in the release's series name must not
            // block attribution — the parenthetical is stripped before the clean-name compare.
            var author = Builder<Author>.CreateNew()
                                        .With(a => a.CleanName = "mushokutenseijoblessreincarnation")
                                        .Build();

            var searchCriteria = new AuthorSearchCriteria { Author = author };

            var parsed = new ParsedBookInfo
            {
                ReleaseTitle = "Mushoku Tensei: Jobless Reincarnation (Light Novel) Vol. 21 by Rifujin na Magonote [ENG / EPUB]",
                AuthorName = "Rifujin na Magonote",
                VolumeNumber = 21,
                Quality = new QualityModel()
            };

            Mocker.GetMock<IBookService>()
                  .Setup(s => s.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book> { new Book { Id = 21, VolumeNumber = 21, AuthorMetadataId = author.AuthorMetadataId } });

            var remoteBook = Subject.Map(parsed, searchCriteria);

            remoteBook.Author.Should().Be(author);
        }

        [Test]
        public void should_reject_sequel_release_whose_series_extends_the_searched_author()
        {
            // The generic ParseBookTitle mis-splits "Tokyo Ghoul - re v01" into AuthorName "Tokyo
            // Ghoul" (the searched author). But the release's own series is "Tokyo Ghoul - re" = the
            // searched author PLUS extra tokens, i.e. the DIFFERENT ":re" sequel. It must resolve to
            // no author (rejected), not silently grab the wrong series via the mis-parsed AuthorName.
            var author = Builder<Author>.CreateNew()
                                        .With(a => a.CleanName = "tokyoghoul")
                                        .Build();

            var searchCriteria = new AuthorSearchCriteria { Author = author };

            var parsed = new ParsedBookInfo
            {
                ReleaseTitle = "Tokyo Ghoul - re v01 (2017) (Digital) (danke-Empire)",
                AuthorName = "Tokyo Ghoul",
                VolumeNumber = 1,
                Quality = new QualityModel()
            };

            Mocker.GetMock<IBookService>()
                  .Setup(s => s.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book>());

            var remoteBook = Subject.Map(parsed, searchCriteria);

            remoteBook.Author.Should().BeNull();
        }

        // Light novels (2026-09, final review I4): RSS has no search criteria, so the entry comes
        // from the name alone -- and the manga and the light novel of one name are different entries
        // (CleanName "<clean>" / "<clean>~ln"). A release whose class is already ebook / audio tries
        // the light-novel entry first; a tokenless (archive-default) one tries the manga entry first,
        // the lookup it always was; the inexact fallback is untouched.

        private Author _manga;
        private Author _lightNovel;

        private void GivenBothLibrariesHave(string name)
        {
            _manga = Builder<Author>.CreateNew().With(a => a.Id = 1).With(a => a.CleanName = "overlord").Build();
            _lightNovel = Builder<Author>.CreateNew().With(a => a.Id = 2).With(a => a.CleanName = "overlord~ln").Build();

            Mocker.GetMock<IAuthorService>().Setup(s => s.FindByName(name)).Returns(_manga);
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindByName(name, LibraryType.Manga)).Returns(_manga);
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindByName(name, LibraryType.LightNovel)).Returns(_lightNovel);

            Mocker.GetMock<IBookService>()
                  .Setup(s => s.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book>());
        }

        // The RSS shape: ParseBookTitleFuzzy picks a candidate entry and ParseBookTitleWithSearchCriteria
        // hands back its Name as AuthorName (the same "Overlord" for both entries) and the real grade.
        private RemoteBook MapRss(string title)
        {
            var parsed = new ParsedBookInfo
            {
                ReleaseTitle = title,
                AuthorName = "Overlord",
                VolumeNumber = 5,
                Quality = QualityParser.ParseQuality(title)
            };

            return Subject.Map(parsed);
        }

        [Test]
        public void rss_light_novel_ebook_release_resolves_to_the_light_novel_entry_when_both_libraries_have_the_name()
        {
            GivenBothLibrariesHave("Overlord");

            MapRss("Overlord Vol. 5 (Light Novel) [Yen On]").Author.Should().BeSameAs(_lightNovel);
        }

        [Test]
        public void rss_audiobook_release_resolves_to_the_light_novel_entry_when_both_libraries_have_the_name()
        {
            GivenBothLibrariesHave("Overlord");

            MapRss("Overlord Vol. 5 Audiobook M4B").Author.Should().BeSameAs(_lightNovel);
        }

        [Test]
        public void rss_tokenless_release_resolves_to_the_manga_entry_when_both_libraries_have_the_name()
        {
            GivenBothLibrariesHave("Overlord");

            MapRss("Overlord v05 (Digital)").Author.Should().BeSameAs(_manga);

            Mocker.GetMock<IAuthorService>().Verify(s => s.FindByName(It.IsAny<string>(), LibraryType.LightNovel), Times.Never());
        }

        [Test]
        public void rss_light_novel_release_of_a_manga_only_name_still_resolves_to_the_manga_entry()
        {
            GivenBothLibrariesHave("Overlord");
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindByName("Overlord", LibraryType.LightNovel)).Returns((Author)null);

            MapRss("Overlord Vol. 5 (Light Novel) [Yen On]").Author.Should().BeSameAs(_manga);

            Mocker.GetMock<IAuthorService>().Verify(s => s.FindByNameInexact(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void rss_tokenless_release_of_a_light_novel_only_name_resolves_to_the_light_novel_entry_exactly()
        {
            GivenBothLibrariesHave("Overlord");
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindByName("Overlord")).Returns((Author)null);
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindByName("Overlord", LibraryType.Manga)).Returns((Author)null);

            MapRss("Overlord v05 (Digital)").Author.Should().BeSameAs(_lightNovel);

            Mocker.GetMock<IAuthorService>().Verify(s => s.FindByNameInexact(It.IsAny<string>()), Times.Never());
        }
    }
}
