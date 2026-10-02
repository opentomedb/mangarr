using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class EditionPreviewServiceFixture : CoreTest<EditionPreviewService>
    {
        private static readonly GcdSeries English = new GcdSeries { GcdSeriesId = 1001, Name = "Attack on Titan", Language = "en", VolumeCount = 34, TomeId = "rl_en" };
        private static readonly GcdSeries French = new GcdSeries { GcdSeriesId = 1004, Name = "Attack on Titan", Language = "fr", VolumeCount = 34, TomeId = "rl_fr", LocalName = "L'Attaque des Titans" };

        private Author _author;

        [SetUp]
        public void Setup()
        {
            _author = new Author { Id = 7, Metadata = new AuthorMetadata { Name = "Attack on Titan", TomeLineId = "rl_en" } };

            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTitle("Attack on Titan", LibraryType.Manga)).Returns(English);
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_en")).Returns(English);
            Mocker.GetMock<IEditionResolver>()
                  .Setup(r => r.Resolve(English, It.Is<EditionRequest>(q => q.Language == "fr"), LibraryType.Manga, "Attack on Titan"))
                  .Returns(new EditionResolution { Line = French, Language = "fr" });
            GivenFiles(3);
        }

        private void GivenFiles(int count)
        {
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(7)).Returns(Enumerable.Range(0, count).Select(i => new BookFile()).ToList());
        }

        private void GivenCollected(GcdSeries line)
        {
            Mocker.GetMock<IEditionResolver>().Setup(r => r.IsCollected(line)).Returns(true);
        }

        [Test]
        public void same_numbering_with_files_is_allowed_and_names_the_local_title()
        {
            var p = Subject.Preview(_author, "fr");

            p.BlockedReason.Should().BeNull();
            p.Compatible.Should().BeTrue();
            p.FromLanguage.Should().Be("en");
            p.ToLanguage.Should().Be("fr");
            p.FromVolumes.Should().Be(34);
            p.ToVolumes.Should().Be(34);
            p.FilesAffected.Should().Be(3);
            p.ToTomeLineId.Should().Be("rl_fr");
            p.NewName.Should().Be("L'Attaque des Titans");
        }

        // KR/CN piece 2 (2026-10-02, M5): a preview to Korean names by AniList's romaji, like Japanese.
        [Test]
        public void a_preview_to_korean_names_by_the_romaji_title()
        {
            var korean = new GcdSeries { GcdSeriesId = 1007, Name = "Attack on Titan", Language = "ko", VolumeCount = 34, TomeId = "rl_ko", LocalName = "진격의 거인" };
            Mocker.GetMock<IEditionResolver>()
                  .Setup(r => r.Resolve(English, It.Is<EditionRequest>(q => q.Language == "ko"), LibraryType.Manga, "Attack on Titan"))
                  .Returns(new EditionResolution { Line = korean, Language = "ko" });
            _author.Metadata.Value.AniListId = 53390;
            Mocker.GetMock<IAniListService>().Setup(s => s.GetById(53390)).Returns(new AniListSeries { RomajiTitle = "Shingeki no Kyojin" });

            var p = Subject.Preview(_author, "ko");

            p.BlockedReason.Should().BeNull();
            p.NewName.Should().Be("Shingeki no Kyojin");
        }

        [Test]
        public void different_numbering_with_files_is_blocked()
        {
            GivenCollected(French);

            var p = Subject.Preview(_author, "fr");

            p.Compatible.Should().BeFalse();
            p.BlockedReason.Should().Be("Volume numbering differs between the two editions and 3 file(s) are attached");
        }

        // Line safety review fixes (M2): a light novel is collected by its omnibus flag or composition only --
        // its single volumes run past the manga page-count tell (EditionResolver.IsCollected's median >= 320).
        private void GivenLightNovel()
        {
            _author.Metadata.Value.ForeignAuthorId = "local-attack-on-titan~ln";
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTitle("Attack on Titan", LibraryType.LightNovel)).Returns(English);
            Mocker.GetMock<IEditionResolver>()
                  .Setup(r => r.Resolve(English, It.Is<EditionRequest>(q => q.Language == "fr"), LibraryType.LightNovel, "Attack on Titan"))
                  .Returns(new EditionResolution { Line = French, Language = "fr" });
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetVolumes(It.IsAny<int>()))
                  .Returns(Enumerable.Range(1, 34).Select(n => new GcdVolume { VolumeNumber = n, PageCount = 420 }).ToList());
        }

        [Test]
        public void a_light_novels_page_counts_do_not_block_the_change()
        {
            GivenLightNovel();
            GivenCollected(French);

            var p = Subject.Preview(_author, "fr");

            p.Compatible.Should().BeTrue();
            p.BlockedReason.Should().BeNull();
        }

        [Test]
        public void a_light_novel_omnibus_line_with_files_is_blocked()
        {
            GivenLightNovel();
            var omnibus = new GcdSeries { GcdSeriesId = 1005, Name = "Attack on Titan", Language = "fr", VolumeCount = 17, TomeId = "rl_fr_omni", IsOmnibus = true };
            Mocker.GetMock<IEditionResolver>()
                  .Setup(r => r.Resolve(English, It.Is<EditionRequest>(q => q.Language == "fr"), LibraryType.LightNovel, "Attack on Titan"))
                  .Returns(new EditionResolution { Line = omnibus, Language = "fr" });

            var p = Subject.Preview(_author, "fr");

            p.Compatible.Should().BeFalse();
            p.BlockedReason.Should().Be("Volume numbering differs between the two editions and 3 file(s) are attached");
        }

        [Test]
        public void different_numbering_without_files_is_allowed()
        {
            GivenCollected(French);
            GivenFiles(0);

            Subject.Preview(_author, "fr").BlockedReason.Should().BeNull();
        }

        [Test]
        public void a_language_the_catalogue_lacks_is_blocked()
        {
            var p = Subject.Preview(_author, "de");

            p.BlockedReason.Should().Be("No German edition of this series in the catalogue");

            // i18n leftovers (2026-09-28): reuses ServerValidationNoLanguageEditionInCatalogue (the same key
            // AddBookService/AddAuthorService build), with the nested language name for the API boundary.
            p.BlockedReasonText.Should().NotBeNull();
            p.BlockedReasonText.English.Should().Be(p.BlockedReason);
        }

        [Test]
        public void the_current_edition_is_blocked_as_a_no_op()
        {
            var p = Subject.Preview(_author, "en");

            p.BlockedReason.Should().Be("Already the English edition");

            // i18n leftovers (2026-09-28): the new ServerValidationAlreadyLanguageEdition carrier.
            p.BlockedReasonText.Should().NotBeNull();
            p.BlockedReasonText.Template.Should().Be("Already the {0} edition");
            p.BlockedReasonText.English.Should().Be(p.BlockedReason);
        }

        // Final fix round I2 (2026-09-24): a series bound through its arc title (the provider's arc-subtitle
        // lookup) has no line under its stored name; the bound line is the anchor, so the French arc line
        // is found instead of "No French edition".
        [Test]
        public void a_series_bound_through_its_arc_title_previews_its_french_line()
        {
            var arc = new GcdSeries { GcdSeriesId = 1101, Name = "Attack on Titan: Before the Fall", Language = "en", VolumeCount = 17, TomeId = "rl_btf_en" };
            var frenchArc = new GcdSeries { GcdSeriesId = 1104, Name = "Attack on Titan: Before the Fall", Language = "fr", VolumeCount = 17, TomeId = "rl_btf_fr" };
            _author.Metadata.Value.Name = "Before the Fall";
            _author.Metadata.Value.TomeLineId = "rl_btf_en";
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_btf_en")).Returns(arc);
            Mocker.GetMock<IEditionResolver>()
                  .Setup(r => r.Resolve(arc, It.Is<EditionRequest>(q => q.Language == "fr"), LibraryType.Manga, "Before the Fall"))
                  .Returns(new EditionResolution { Line = frenchArc, Language = "fr" });

            var p = Subject.Preview(_author, "fr");

            p.BlockedReason.Should().BeNull();
            p.ToLanguage.Should().Be("fr");
            p.ToVolumes.Should().Be(17);
            p.FromVolumes.Should().Be(17);
            p.ToTomeLineId.Should().Be("rl_btf_fr");
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.FindSeriesByTitle(It.IsAny<string>(), It.IsAny<LibraryType>()), Times.Never());
        }

        // Final fix round I2: an English series whose bound line is not the one its name ranks to is still
        // the English edition -- no spurious en -> en change offered.
        [Test]
        public void an_english_series_bound_to_another_line_than_its_name_is_already_english()
        {
            var other = new GcdSeries { GcdSeriesId = 1002, Name = "Attack on Titan", Language = "en", VolumeCount = 12, TomeId = "rl_en_other" };
            _author.Metadata.Value.TomeLineId = "rl_en_other";
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_en_other")).Returns(other);

            var p = Subject.Preview(_author, "en");

            p.BlockedReason.Should().Be("Already the English edition");
            p.RenameOnly.Should().BeFalse();
        }

        // The same with the name lookup still answering a different line (the anchor would once have been it).
        [Test]
        public void an_english_series_with_a_stale_binding_is_already_english_whatever_the_line_ids()
        {
            _author.Metadata.Value.TomeLineId = "rl_gone";

            var p = Subject.Preview(_author, "en");

            p.BlockedReason.Should().Be("Already the English edition");
            p.RenameOnly.Should().BeFalse();
        }

        // Final fix round I2: an English series the catalogue has no line of (no binding, no name match) is
        // "Already the English edition", not "No English edition".
        [Test]
        public void an_english_series_with_no_catalogue_line_is_already_english()
        {
            _author.Metadata.Value.Name = "Some Webcomic";
            _author.Metadata.Value.TomeLineId = null;

            var p = Subject.Preview(_author, "en");

            p.BlockedReason.Should().Be("Already the English edition");
            p.RenameOnly.Should().BeFalse();
        }

        // Ruling S3: the name comes from the add path's own rule (EditionDisplayName); a line with no local
        // title falls back to the anchor there, which is no new name at all.
        [Test]
        public void a_line_without_a_local_title_offers_no_new_name()
        {
            var untitled = new GcdSeries { GcdSeriesId = 1005, Name = "Attack on Titan", Language = "fr", VolumeCount = 34, TomeId = "rl_fr" };
            Mocker.GetMock<IEditionResolver>()
                  .Setup(r => r.Resolve(English, It.Is<EditionRequest>(q => q.Language == "fr"), LibraryType.Manga, "Attack on Titan"))
                  .Returns(new EditionResolution { Line = untitled, Language = "fr" });

            var p = Subject.Preview(_author, "fr");

            p.BlockedReason.Should().BeNull();
            p.NewName.Should().BeNull();
        }

        // Back to English: the anchor is the name to return to (the rename works in both directions).
        [Test]
        public void back_to_english_offers_the_anchor_name()
        {
            _author.Metadata.Value.Name = "L'Attaque des Titans";
            _author.Metadata.Value.AnchorName = "Attack on Titan";
            _author.Metadata.Value.EditionLanguage = "fr";
            _author.Metadata.Value.TomeLineId = "rl_fr";
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_fr")).Returns(French);

            var p = Subject.Preview(_author, "en");

            p.BlockedReason.Should().BeNull();
            p.FromLanguage.Should().Be("fr");
            p.ToTomeLineId.Should().Be("rl_en");
            p.NewName.Should().Be("Attack on Titan");
        }

        // i18n leftovers (2026-09-28): the from=fr/to=fr no-op (EditionPreviewService.cs:243, the same-name
        // branch of the from==to check) builds the same "Already the {0} edition" carrier as the English
        // no-op above, with the nested language name being the non-English one this time. The French
        // sentence this produces at the API boundary is proven end to end in ServerMessageSurfacesFixture.
        [Test]
        public void already_on_the_requested_non_english_edition_is_blocked_with_its_carrier()
        {
            _author.Metadata.Value.Name = "L'Attaque des Titans";
            _author.Metadata.Value.AnchorName = "Attack on Titan";
            _author.Metadata.Value.EditionLanguage = "fr";
            _author.Metadata.Value.TomeLineId = "rl_fr";
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_fr")).Returns(French);

            var p = Subject.Preview(_author, "fr");

            p.BlockedReason.Should().Be("Already the French edition");
            p.BlockedReasonText.Should().NotBeNull();
            p.BlockedReasonText.Template.Should().Be("Already the {0} edition");
            p.BlockedReasonText.English.Should().Be(p.BlockedReason);
        }

        // Only a Japanese target asks AniList (for the romaji name): a bulk French preview costs no call.
        [Test]
        public void a_non_japanese_preview_never_asks_anilist()
        {
            _author.Metadata.Value.AniListId = 53390;

            Subject.Preview(_author, "fr");

            Mocker.GetMock<IAniListService>().Verify(s => s.GetById(It.IsAny<int>()), Times.Never());
        }

        // Fix round 1 (I1): shown before anything else, single or bulk.
        [Test]
        public void a_series_a_running_refresh_covers_is_blocked()
        {
            Mocker.GetMock<IManageCommandQueue>().Setup(q => q.GetStarted())
                  .Returns(new List<CommandModel> { new CommandModel { Body = new RefreshAuthorCommand(7), Status = CommandStatus.Started } });

            Subject.Preview(_author, "fr").BlockedReason.Should().Be("A refresh is running for this series; try again when it finishes");
        }

        // Fix round 1 (I2): a taken destination blocks the rename (and says why), not the edition change.
        [Test]
        public void a_rename_onto_an_existing_folder_is_blocked_but_the_change_is_not()
        {
            _author.Path = "/manga/Attack on Titan";
            Mocker.GetMock<IBuildFileNames>().Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null)).Returns((Author a, NamingConfig c) => a.Name);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists("/manga/L'Attaque des Titans")).Returns(true);

            var p = Subject.Preview(_author, "fr");

            p.BlockedReason.Should().BeNull();
            p.NewName.Should().Be("L'Attaque des Titans");
            p.RenameBlockedReason.Should().Be("A folder already exists at /manga/L'Attaque des Titans");
        }

        [Test]
        public void a_rename_onto_a_pin_key_that_holds_rows_is_blocked()
        {
            Mocker.GetMock<IMetadataOverridesService>().Setup(s => s.Get("L'Attaque des Titans"))
                  .Returns(new Dictionary<string, VolumeOverride> { { "*", new VolumeOverride { Author = "Hajime Isayama" } } });

            Subject.Preview(_author, "fr").RenameBlockedReason.Should().Be("Metadata pins already exist under \"L'Attaque des Titans\"");
        }

        [Test]
        public void a_rename_onto_another_series_name_is_blocked()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindByName("L'Attaque des Titans", LibraryType.Manga))
                  .Returns(new Author { Id = 9, Metadata = new AuthorMetadata { Name = "L'Attaque des Titans" } });

            Subject.Preview(_author, "fr").RenameBlockedReason.Should().Be("Another series is already named \"L'Attaque des Titans\"");
        }

        // Fix round 1 (minor): "back to English without Rename" left the French name on an English series;
        // the same edition is then not a no-op -- a rename restores the English name.
        [Test]
        public void the_same_edition_under_another_name_is_a_rename_only_change()
        {
            _author.Metadata.Value.Name = "L'Attaque des Titans";
            _author.Metadata.Value.AnchorName = "Attack on Titan";

            var p = Subject.Preview(_author, "en");

            p.BlockedReason.Should().BeNull();
            p.RenameOnly.Should().BeTrue();
            p.NewName.Should().Be("Attack on Titan");
        }

        // Polish: a rename-only change stays on its line, so a collected edition with files attached does not
        // block it -- no numbering changes.
        [Test]
        public void a_rename_only_change_on_a_collected_line_with_files_is_not_blocked()
        {
            _author.Metadata.Value.Name = "L'Attaque des Titans";
            _author.Metadata.Value.AnchorName = "Attack on Titan";
            GivenCollected(English);

            var p = Subject.Preview(_author, "en");

            p.RenameOnly.Should().BeTrue();
            p.Compatible.Should().BeTrue();
            p.BlockedReason.Should().BeNull();
            p.FilesAffected.Should().Be(3);
        }

        // Final fix wave I3: a fallback series (bound to its Japanese line, no English line by its name) takes the
        // target from its bound line's work -- the real EditionResolver ranks it, so the spin-off's lines lose.
        private void GivenJapaneseFallbackSeries()
        {
            var jaMain = new GcdSeries { GcdSeriesId = 8001, Name = "Stand Up Start", LocalName = "スタンドUPスタート", Language = "ja", VolumeCount = 7, IsMain = true, Medium = "manga", TomeId = "rl_ja_sus", TomeWorkId = "w_sus" };
            var jaSpin = new GcdSeries { GcdSeriesId = 8003, Name = "Stand Up Start Side", Language = "ja", VolumeCount = 2, IsMain = false, Medium = "manga", TomeId = "rl_ja_side", TomeWorkId = "w_sus" };
            var en = new GcdSeries { GcdSeriesId = 8002, Name = "Stand Up Start", Language = "en", VolumeCount = 3, IsMain = true, OrigSeriesId = 8001, Medium = "manga", TomeId = "rl_en_sus", TomeWorkId = "w_sus" };
            var fr = new GcdSeries { GcdSeriesId = 8004, Name = "Stand Up Start", LocalName = "Stand Up Start FR", Language = "fr", VolumeCount = 5, IsMain = true, OrigSeriesId = 8001, Medium = "manga", TomeId = "rl_fr_sus", TomeWorkId = "w_sus" };
            var frSpin = new GcdSeries { GcdSeriesId = 8005, Name = "Stand Up Start Side", Language = "fr", VolumeCount = 9, IsMain = true, OrigSeriesId = 8003, Medium = "manga", TomeId = "rl_fr_side", TomeWorkId = "w_sus" };

            var gcd = Mocker.GetMock<IGcdMetadataService>();
            gcd.Setup(s => s.FindSeriesByTomeId("rl_ja_sus")).Returns(jaMain);
            gcd.Setup(s => s.GetWorkLines("w_sus")).Returns(new List<GcdSeries> { jaMain, jaSpin, en, fr, frSpin });
            gcd.Setup(s => s.GetVolumes(It.IsAny<int>())).Returns(new List<GcdVolume>());
            Mocker.SetConstant<IEditionResolver>(Mocker.Resolve<EditionResolver>());

            _author = new Author { Id = 7, Metadata = new AuthorMetadata { Name = "Stand Up Start", EditionLanguage = "ja", TomeLineId = "rl_ja_sus", EditionFallback = true } };
        }

        [Test]
        public void a_fallback_series_previews_a_move_to_english()
        {
            GivenJapaneseFallbackSeries();

            var p = Subject.Preview(_author, "en");

            p.BlockedReason.Should().BeNull();
            p.FromLanguage.Should().Be("ja");
            p.ToTomeLineId.Should().Be("rl_en_sus");
        }

        [Test]
        public void a_fallback_series_previews_a_move_to_french()
        {
            GivenJapaneseFallbackSeries();

            var p = Subject.Preview(_author, "fr");

            p.BlockedReason.Should().BeNull();
            p.ToTomeLineId.Should().Be("rl_fr_sus");
        }

        [Test]
        public void a_fallback_series_on_its_own_language_is_already_that_edition()
        {
            GivenJapaneseFallbackSeries();

            Subject.Preview(_author, "ja").BlockedReason.Should().Be("Already the Japanese edition");
        }

        // Follow-up round (KR/CN consumer): a first Change Edition (ja -> ko) cleared the fallback flag, but the series
        // still has no English line by its name -- the target still comes from its bound line's work.
        private void GivenAKoreanSeriesWhoseFallbackFlagWasCleared()
        {
            GivenJapaneseFallbackSeries();

            var ko = new GcdSeries { GcdSeriesId = 8006, Name = "Stand Up Start", Language = "ko", VolumeCount = 7, IsMain = true, OrigSeriesId = 8001, Medium = "manga", TomeId = "rl_ko_sus", TomeWorkId = "w_sus" };
            var gcd = Mocker.GetMock<IGcdMetadataService>();
            var work = gcd.Object.GetWorkLines("w_sus").Concat(new[] { ko }).ToList();
            gcd.Setup(s => s.FindSeriesByTomeId("rl_ko_sus")).Returns(ko);
            gcd.Setup(s => s.GetWorkLines("w_sus")).Returns(work);

            _author.Metadata.Value.EditionLanguage = "ko";
            _author.Metadata.Value.TomeLineId = "rl_ko_sus";
            _author.Metadata.Value.EditionFallback = false;
        }

        [Test]
        public void a_series_whose_fallback_flag_was_cleared_previews_a_move_to_english()
        {
            GivenAKoreanSeriesWhoseFallbackFlagWasCleared();

            var p = Subject.Preview(_author, "en");

            p.BlockedReason.Should().BeNull();
            p.FromLanguage.Should().Be("ko");
            p.ToTomeLineId.Should().Be("rl_en_sus");
        }

        [Test]
        public void a_series_whose_fallback_flag_was_cleared_previews_a_move_to_french()
        {
            GivenAKoreanSeriesWhoseFallbackFlagWasCleared();

            var p = Subject.Preview(_author, "fr");

            p.BlockedReason.Should().BeNull();
            p.ToTomeLineId.Should().Be("rl_fr_sus");
        }

        // A fallback (or anchorless) series moved to English is offered the English line's name, which also becomes
        // its anchor -- an English series is refreshed by that name (BookInfoProxy), so the old name would lose the line.
        [Test]
        public void a_fallback_series_moved_to_english_is_offered_the_english_lines_name()
        {
            GivenJapaneseFallbackSeries();
            _author.Metadata.Value.Name = "Sutando Appu Sutato";

            var p = Subject.Preview(_author, "en");

            p.ToTomeLineId.Should().Be("rl_en_sus");
            p.NewName.Should().Be("Stand Up Start");
            p.ToAnchorName.Should().Be("Stand Up Start");
        }

        [Test]
        public void a_series_whose_fallback_flag_was_cleared_is_offered_the_english_lines_name()
        {
            GivenAKoreanSeriesWhoseFallbackFlagWasCleared();
            _author.Metadata.Value.Name = "Sutando Appu Sutato";

            var p = Subject.Preview(_author, "en");

            p.NewName.Should().Be("Stand Up Start");
            p.ToAnchorName.Should().Be("Stand Up Start");
        }

        // An anchored series keeps today's rule (back_to_english_offers_the_anchor_name): no anchor override.
        [Test]
        public void an_anchored_series_moved_to_english_carries_no_anchor_override()
        {
            _author.Metadata.Value.Name = "L'Attaque des Titans";
            _author.Metadata.Value.AnchorName = "Attack on Titan";
            _author.Metadata.Value.EditionLanguage = "fr";
            _author.Metadata.Value.TomeLineId = "rl_fr";
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_fr")).Returns(French);

            var p = Subject.Preview(_author, "en");

            p.NewName.Should().Be("Attack on Titan");
            p.ToAnchorName.Should().BeNull();
        }
    }
}
