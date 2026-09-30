using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;
using Readarr.Api.V1.PreferredEdition;

namespace NzbDrone.Core.Test.Api.PreferredEdition
{
    // Preferred Edition (2026-09-24, ruling S8): GET api/v1/edition/markets feeds the Settings -> UI list
    // editor and its "series use another edition" note. Reached through Readarr.Core.Test's project
    // reference to Readarr.Api.V1 (see MediaManagementConfigControllerFixture). M13 adds author/{id}
    // and preview cases here.
    [TestFixture]
    public class PreferredEditionControllerFixture : CoreTest<PreferredEditionController>
    {
        private void GivenMarkets(Dictionary<string, int> markets)
        {
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.Markets())
                  .Returns(markets);
        }

        private void GivenAuthors(params Author[] authors)
        {
            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.GetAllAuthors())
                  .Returns(authors.ToList());
        }

        private static Author Series(string foreignAuthorId, string editionLanguage)
        {
            return new Author
            {
                Metadata = new AuthorMetadata { ForeignAuthorId = foreignAuthorId, EditionLanguage = editionLanguage }
            };
        }

        [SetUp]
        public void Setup()
        {
            GivenMarkets(new Dictionary<string, int>());
            GivenAuthors();

            // i18n leftovers (2026-09-28): Preview() now localizes BlockedReason through EditionPreviewResourceMapper
            // at the API boundary; the localizer's own French/German/Japanese behaviour is proven in
            // ServerMessageSurfacesFixture, so here it just echoes the English through unchanged, like an
            // English UI (or a miss) would, keeping this fixture's existing English assertions unchanged.
            Mocker.GetMock<IServerMessageLocalizer>()
                  .Setup(m => m.Localize(It.IsAny<string>(), It.IsAny<ServerText>()))
                  .Returns((string english, ServerText text) => english);
        }

        // An older artifact (or a catalogue with no English line) still offers English, the default.
        [Test]
        public void english_is_listed_first_with_zero_lines_when_the_catalogue_has_no_english_line()
        {
            GivenMarkets(new Dictionary<string, int> { { "fr", 1493 } });

            var languages = Subject.GetMarkets().Languages;

            languages.Select(l => l.Language).Should().Equal("en", "fr");
            languages[0].Name.Should().Be("English");
            languages[0].Lines.Should().Be(0);
        }

        [Test]
        public void no_catalogue_lists_english_only()
        {
            var languages = Subject.GetMarkets().Languages;

            languages.Should().ContainSingle();
            languages[0].Language.Should().Be("en");
        }

        [Test]
        public void english_from_the_catalogue_is_listed_once_with_its_line_count()
        {
            GivenMarkets(new Dictionary<string, int> { { "en", 3030 }, { "ja", 6978 } });

            var languages = Subject.GetMarkets().Languages;

            languages.Where(l => l.Language == "en").Should().ContainSingle()
                     .Which.Lines.Should().Be(3030);
        }

        // English first, then the rest by display name (not by code, not by line count).
        [Test]
        public void languages_are_english_first_then_alphabetical_by_name_with_their_line_counts()
        {
            GivenMarkets(new Dictionary<string, int> { { "ja", 6978 }, { "de", 1459 }, { "en", 3030 }, { "fr", 1493 }, { "ko", 66 } });

            var languages = Subject.GetMarkets().Languages;

            languages.Select(l => l.Name).Should().Equal("English", "French", "German", "Japanese", "Korean");
            languages.Select(l => l.Language).Should().Equal("en", "fr", "de", "ja", "ko");
            languages.Select(l => l.Lines).Should().Equal(3030, 1493, 1459, 6978, 66);
        }

        // Null, blank and "en" are all English (every existing series reads null); only a stored
        // non-English edition counts, split by the library the foreign id encodes.
        [Test]
        public void other_edition_series_counts_only_non_english_series_split_by_library()
        {
            GivenAuthors(
                Series("local-one-piece", null),
                Series("local-berserk", ""),
                Series("local-naruto", "en"),
                Series("local-overlord~ln", "en"),
                Series("local-attack-on-titan", "fr"),
                Series("local-vinland-saga", "de"),
                Series("local-sword-art-online~ln", "ja"));

            var result = Subject.GetMarkets();

            result.OtherEditionSeries.Should().Be(3);
            result.OtherEditionManga.Should().Be(2);
            result.OtherEditionLightNovels.Should().Be(1);
        }

        [Test]
        public void an_all_english_library_has_no_other_edition_series()
        {
            GivenAuthors(Series("local-one-piece", null), Series("local-overlord~ln", null));

            var result = Subject.GetMarkets();

            result.OtherEditionSeries.Should().Be(0);
            result.OtherEditionManga.Should().Be(0);
            result.OtherEditionLightNovels.Should().Be(0);
        }

        // M13: Edit Series' Change Edition reads the series' edition and the languages its work has a line in.
        private void GivenAuthor(int id, string foreignAuthorId, string editionLanguage, string tomeLineId)
        {
            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.GetAuthor(id))
                  .Returns(new Author { Id = id, Metadata = new AuthorMetadata { ForeignAuthorId = foreignAuthorId, EditionLanguage = editionLanguage, TomeLineId = tomeLineId } });
        }

        [Test]
        public void author_editions_are_the_current_edition_and_the_works_lines()
        {
            GivenAuthor(7, "local-attack-on-titan", "fr", "rl_fr");
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.EditionOptions("rl_fr", LibraryType.Manga))
                  .Returns(new List<EditionOption>
                  {
                      new EditionOption { Language = "en", Name = "English", VolumeCount = 34 },
                      new EditionOption { Language = "fr", Name = "French", VolumeCount = 34 }
                  });

            var result = Subject.GetAuthorEditions(7);

            result.Current.Should().Be("fr");
            result.Options.Select(o => o.Language).Should().Equal("en", "fr");
        }

        // A series no refresh has bound to a line yet: English, and nothing to choose from (never null).
        [Test]
        public void an_unbound_series_is_english_with_no_options()
        {
            GivenAuthor(7, "local-attack-on-titan", null, null);

            var result = Subject.GetAuthorEditions(7);

            result.Current.Should().Be("en");
            result.Options.Should().NotBeNull().And.BeEmpty();
        }

        [Test]
        public void preview_answers_one_row_per_series_in_order()
        {
            GivenAuthor(1, "local-attack-on-titan", null, "rl_en");
            GivenAuthor(2, "local-vinland-saga", null, "rl_en2");
            Mocker.GetMock<IEditionPreviewService>()
                  .Setup(s => s.Preview(It.IsAny<Author>(), "fr"))
                  .Returns((Author a, string l) => new EditionPreview { AuthorId = a.Id, ToLanguage = l });

            var result = Subject.Preview(new PreferredEditionController.EditionPreviewRequestResource { AuthorIds = new List<int> { 2, 1 }, Language = "fr" });

            result.Select(p => p.AuthorId).Should().Equal(2, 1);
            result.Should().OnlyContain(p => p.ToLanguage == "fr");
        }

        // Fix round 1: a series deleted since the list loaded is left out, not a failed POST.
        [Test]
        public void preview_skips_a_series_that_no_longer_exists()
        {
            GivenAuthor(1, "local-attack-on-titan", null, "rl_en");
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(2)).Throws(new ModelNotFoundException(typeof(Author), 2));
            Mocker.GetMock<IEditionPreviewService>()
                  .Setup(s => s.Preview(It.IsAny<Author>(), "fr"))
                  .Returns((Author a, string l) => new EditionPreview { AuthorId = a.Id, ToLanguage = l });

            var result = Subject.Preview(new PreferredEditionController.EditionPreviewRequestResource { AuthorIds = new List<int> { 2, 1 }, Language = "fr" });

            result.Select(p => p.AuthorId).Should().Equal(1);
        }

        // Polish: the series list never renames, so its rename-only rows show blocked, not eligible.
        [Test]
        public void a_rename_only_row_is_blocked_in_a_bulk_preview_but_not_for_one_series()
        {
            GivenAuthor(1, "local-attack-on-titan", null, "rl_en");
            GivenAuthor(2, "local-vinland-saga", null, "rl_en2");
            Mocker.GetMock<IEditionPreviewService>()
                  .Setup(s => s.Preview(It.IsAny<Author>(), "en"))
                  .Returns((Author a, string l) => new EditionPreview { AuthorId = a.Id, ToLanguage = l, RenameOnly = a.Id == 1, NewName = a.Id == 1 ? "Attack on Titan" : null });

            var bulk = Subject.Preview(new PreferredEditionController.EditionPreviewRequestResource { AuthorIds = new List<int> { 1, 2 }, Language = "en" });
            var single = Subject.Preview(new PreferredEditionController.EditionPreviewRequestResource { AuthorIds = new List<int> { 1 }, Language = "en" });

            bulk.Single(p => p.AuthorId == 1).BlockedReason.Should().Be("Already the English edition (rename from Edit Series)");
            bulk.Single(p => p.AuthorId == 2).BlockedReason.Should().BeNull();
            single.Single().BlockedReason.Should().BeNull();
        }

        // Line safety (2026-09-28): Switch Line's list localizes each blocked reason from its template.
        [Test]
        public void author_lines_localize_each_blocked_reason()
        {
            GivenAuthor(1, "local-otome~ln", null, "rl_b816b634db1a");
            var reason = new ServerText("{0} is already bound to this line", "Mobseka");
            Mocker.GetMock<ILineSwitchService>().Setup(s => s.Choices(It.IsAny<Author>())).Returns(new LineSwitchChoices
            {
                Options = new List<LineSwitchOption> { new LineSwitchOption { TomeLineId = "rl_224c4428cda7", BlockedReason = reason.English, BlockedReasonText = reason } }
            });
            Mocker.GetMock<IServerMessageLocalizer>().Setup(m => m.Localize(reason.English, reason)).Returns("Mobseka est déjà liée à cette ligne");

            Subject.GetAuthorLines(1).Options.Single().BlockedReason.Should().Be("Mobseka est déjà liée à cette ligne");
        }

        // The prompt's yes is refused (400) with the service's reason; accepted (202) otherwise.
        [Test]
        public void author_line_sync_is_refused_with_the_services_reason()
        {
            GivenAuthor(1, "local-otome~ln", null, "rl_224c4428cda7");
            Mocker.GetMock<ILineSwitchService>().Setup(s => s.SyncExternal(It.IsAny<Author>(), It.IsAny<List<int>>()))
                  .Returns(new ServerText("A refresh is running for this series; try again when it finishes"));

            Assert.Throws<Readarr.Http.REST.BadRequestException>(() => Subject.SyncAuthorLine(1, new PreferredEditionController.LineSyncRequestResource { BookFileIds = new List<int> { 5 } }));

            Mocker.GetMock<ILineSwitchService>().Setup(s => s.SyncExternal(It.IsAny<Author>(), It.IsAny<List<int>>())).Returns((ServerText)null);

            Subject.SyncAuthorLine(1, new PreferredEditionController.LineSyncRequestResource { BookFileIds = new List<int> { 5 } })
                   .Should().BeOfType<Microsoft.AspNetCore.Mvc.AcceptedResult>();
        }

        [Test]
        public void preview_of_nothing_is_empty()
        {
            Subject.Preview(null).Should().BeEmpty();
            Subject.Preview(new PreferredEditionController.EditionPreviewRequestResource { Language = "fr" }).Should().BeEmpty();
        }
    }
}
