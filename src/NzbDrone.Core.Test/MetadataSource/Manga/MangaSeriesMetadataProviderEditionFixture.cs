using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    // Preferred Edition (2026-09-24): the 7-argument GetSeries -- structure, name and binding from the
    // edition line; the anchor stays English for everything else.
    [TestFixture]
    public class MangaSeriesMetadataProviderEditionFixture : CoreTest<MangaSeriesMetadataProvider>
    {
        private static readonly GcdSeries Anchor = new GcdSeries
        {
            GcdSeriesId = 1001, Name = "Attack on Titan", Language = "en", VolumeCount = 34, Status = "completed",
            OrigSeriesId = 1003, TomeId = "rl_en", TomeWorkId = "w_aot", Medium = "manga", IsMain = true
        };

        private static readonly GcdSeries French = new GcdSeries
        {
            GcdSeriesId = 1004, Name = "Attack on Titan", Language = "fr", VolumeCount = 20, Status = "stalled",
            OrigSeriesId = 1003, TomeId = "rl_fr", TomeWorkId = "w_aot", Medium = "manga", IsMain = true,
            LocalName = "L'Attaque des Titans", YearBegan = 2013
        };

        [SetUp]
        public void Setup()
        {
            var gcd = Mocker.GetMock<IGcdMetadataService>();
            gcd.SetupGet(s => s.Available).Returns(true);
            gcd.Setup(s => s.FindSeriesByTitle("Attack on Titan", LibraryType.Manga)).Returns(Anchor);
            gcd.Setup(s => s.GetVolumes(It.IsAny<int>())).Returns(new List<GcdVolume>());

            // Ruling S5 (2026-09-24, D6): the edition path reads its volumes with the edition dates.
            gcd.Setup(s => s.GetVolumes(It.IsAny<int>(), It.IsAny<bool>())).Returns(new List<GcdVolume>());
            gcd.Setup(s => s.GetVolumes(1004, true)).Returns(new List<GcdVolume>
            {
                new GcdVolume { VolumeNumber = 1, ReleaseDate = "2013-06-05", Isbn13 = "9782811611699" }
            });
            gcd.Setup(s => s.GetAliases(It.IsAny<int>())).Returns(new List<string>());

            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.FindSeries(It.IsAny<string>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns(new AniListSeries { Id = 53390, EnglishTitle = "Attack on Titan", RomajiTitle = "Shingeki no Kyojin", Status = "FINISHED", Volumes = 34, MatchedVia = "primary" });

            Mocker.GetMock<IAudibleCatalogService>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                  .Returns(new List<AudibleProduct>());
        }

        private void GivenResolution(GcdSeries line, string language)
        {
            Mocker.GetMock<IEditionResolver>()
                  .Setup(s => s.Resolve(It.IsAny<GcdSeries>(), It.IsAny<EditionRequest>(), It.IsAny<LibraryType>(), It.IsAny<string>()))
                  .Returns(line == null ? null : new EditionResolution { Line = line, Language = language });
        }

        private MangaSeriesMetadata FrenchSeries(bool details = false)
        {
            return Subject.GetSeries("Attack on Titan", 0, details, LibraryType.Manga, null, "Attack On Titan", new EditionRequest { Chain = new[] { "fr", "en" } });
        }

        [Test]
        public void a_french_edition_takes_structure_and_name_from_the_edition_line()
        {
            GivenResolution(French, "fr");

            var s = FrenchSeries();

            s.VolumeCount.Should().Be(20);
            s.Status.Should().Be(AuthorStatusType.Stalled);
            s.JapaneseTotal.Should().Be(34);
            s.DisplayName.Should().Be("L'Attaque des Titans");
            s.IdentityName.Should().Be("Attack on Titan");
            s.EditionLanguage.Should().Be("fr");
            s.TomeLineId.Should().Be("rl_fr");
        }

        [Test]
        public void a_french_edition_takes_the_edition_isbns()
        {
            GivenResolution(French, "fr");

            // An ISBN-only answer (no record): not a Google miss, so no "did not answer" Warn.
            Mocker.GetMock<IGoogleBooksService>().Setup(s => s.LookupByIsbn(It.IsAny<string>())).Returns(new VolumeDetails());

            FrenchSeries(details: true).Volumes[0].Isbn13.Should().Be("9782811611699");
        }

        [Test]
        public void a_japanese_edition_is_named_by_the_romaji_title()
        {
            var japanese = new GcdSeries { GcdSeriesId = 1003, Name = "Attack on Titan", Language = "ja", VolumeCount = 34, TomeId = "rl_ja", TomeWorkId = "w_aot", Medium = "manga", LocalName = "進撃の巨人" };
            GivenResolution(japanese, "ja");

            var s = Subject.GetSeries("Attack on Titan", 0, false, LibraryType.Manga, null, "Attack On Titan", new EditionRequest { Language = "ja" });

            s.DisplayName.Should().Be("Shingeki no Kyojin");
            s.EditionLanguage.Should().Be("ja");
        }

        [Test]
        public void a_chosen_language_with_no_line_throws_edition_unavailable()
        {
            GivenResolution(null, null);

            Assert.Throws<EditionUnavailableException>(() =>
                Subject.GetSeries("Attack on Titan", 0, false, LibraryType.Manga, null, "Attack On Titan", new EditionRequest { Language = "fr", TomeLineId = "rl_fr" }));
        }

        // Fix round 1: no catalogue at all is no line of the bound language either -- never English
        // volumes for a French series, and the resolver is not asked.
        [Test]
        public void a_bound_language_without_a_catalogue_throws_edition_unavailable()
        {
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(false);

            var ex = Assert.Throws<EditionUnavailableException>(() =>
                Subject.GetSeries("Attack on Titan", 0, false, LibraryType.Manga, null, "Attack On Titan", new EditionRequest { Language = "fr", TomeLineId = "rl_fr" }));

            ex.Language.Should().Be("fr");
            ex.TomeLineId.Should().Be("rl_fr");
            Mocker.GetMock<IEditionResolver>().VerifyNoOtherCalls();
        }

        [Test]
        public void a_japanese_edition_without_a_romaji_title_is_named_by_the_anchor()
        {
            var japanese = new GcdSeries { GcdSeriesId = 1003, Language = "ja", LocalName = "進撃の巨人" };

            MangaSeriesMetadataProvider.EditionDisplayName(japanese, "ja", new AniListSeries { EnglishTitle = "Attack on Titan" }, "Attack on Titan", LibraryType.Manga)
                .Should().Be("Attack on Titan");
            MangaSeriesMetadataProvider.EditionDisplayName(japanese, "ja", null, "Attack on Titan", LibraryType.Manga)
                .Should().Be("Attack on Titan");
        }

        [Test]
        public void a_light_novel_edition_name_sheds_the_novel_qualifier()
        {
            var frenchNovel = new GcdSeries { GcdSeriesId = 7001, Language = "fr", LocalName = "Les Carnets de l'apothicaire (light novel)" };

            MangaSeriesMetadataProvider.EditionDisplayName(frenchNovel, "fr", null, "The Apothecary Diaries", LibraryType.LightNovel)
                .Should().Be("Les Carnets de l'apothicaire");
            MangaSeriesMetadataProvider.EditionDisplayName(frenchNovel, "fr", null, "The Apothecary Diaries", LibraryType.Manga)
                .Should().Be("Les Carnets de l'apothicaire (light novel)");
        }

        [Test]
        public void a_chain_that_finds_only_english_stays_english()
        {
            GivenResolution(Anchor, "en");

            var s = FrenchSeries();

            s.EditionLanguage.Should().BeNull();
            s.DisplayName.Should().Be("Attack on Titan");
            s.VolumeCount.Should().Be(34);
            s.TomeLineId.Should().Be("rl_en");
        }

        [Test]
        public void the_six_argument_call_never_asks_the_resolver()
        {
            var s = Subject.GetSeries("Attack on Titan", 0, false, LibraryType.Manga, null, "Attack On Titan");

            s.IdentityName.Should().Be("Attack on Titan");
            s.TomeLineId.Should().Be("rl_en");
            Mocker.GetMock<IEditionResolver>().VerifyNoOtherCalls();

            // Ruling S5 (2026-09-24, D6): the English path reads no edition dates and no build date.
            Mocker.GetMock<IGcdMetadataService>().Verify(g => g.GetVolumes(It.IsAny<int>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IGcdMetadataService>().Verify(g => g.ArtifactInfo(), Times.Never());
        }

        [Test]
        public void year_precision_volumes_keep_their_dates_through_the_batch_stamp_check()
        {
            GivenResolution(French, "fr");
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.ArtifactInfo()).Returns(new GcdArtifactInfo { Available = true, GeneratedAt = "2026-09-20T03:00:00+00:00" });
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetVolumes(1004, true)).Returns(new List<GcdVolume>
            {
                new GcdVolume { VolumeNumber = 1, ReleaseDateRaw = "2019", ReleaseDatePrecision = "year", ReleaseDateType = "published" },
                new GcdVolume { VolumeNumber = 2, ReleaseDateRaw = "2019", ReleaseDatePrecision = "year", ReleaseDateType = "published" },
                new GcdVolume { VolumeNumber = 3, ReleaseDateRaw = "2019", ReleaseDatePrecision = "year", ReleaseDateType = "published" },
                new GcdVolume { VolumeNumber = 4, ReleaseDate = "2015-01-05", ReleaseDateRaw = "2015-01-05", ReleaseDatePrecision = "day" }
            });
            Mocker.GetMock<IGoogleBooksService>().Setup(s => s.LookupByIsbn(It.IsAny<string>())).Returns(new VolumeDetails());

            var s = FrenchSeries(details: true);

            s.Volumes[0].ReleaseDatePrecision.Should().Be("year");
            s.Volumes[0].ReleaseDate.Should().Be(DateTime.SpecifyKind(new DateTime(2019, 12, 31), DateTimeKind.Local).ToUniversalTime());
            s.Volumes[2].ReleaseDate.Should().NotBeNull();
            s.Volumes[3].ReleaseDatePrecision.Should().BeNull();
        }

        // Ruling S1: a compatible re-resolve to French that kept the stored name ("Attack on Titan") keeps
        // the pins keyed by that name -- not by the French display name the edition line computes.
        [Test]
        public void pins_survive_a_compatible_re_resolve_without_a_rename()
        {
            GivenResolution(French, "fr");

            Subject.GetSeries("Attack on Titan", 0, false, LibraryType.Manga, null, "Attack On Titan",
                new EditionRequest { Language = "fr", TomeLineId = "rl_fr", StoredName = "Attack on Titan" });

            Mocker.GetMock<IMetadataOverridesService>()
                  .Verify(s => s.Apply("Attack on Titan", It.IsAny<List<MangaVolumeMetadata>>()), Times.Once());
            Mocker.GetMock<IMetadataOverridesService>()
                  .Verify(s => s.Apply("L'Attaque des Titans", It.IsAny<List<MangaVolumeMetadata>>()), Times.Never());
        }

        // M13 pre-review fix, ruling (c): an English entry that was localized and came back to English without
        // Rename (Name "L'Attaque des Titans", AnchorName "Attack on Titan") is resolved by the six-argument call
        // inside BookInfoProxy's scope, and its pins are read where MetadataPinController wrote them.
        [Test]
        public void an_english_resolve_in_a_stored_name_scope_pins_by_the_stored_name()
        {
            using (MangaSeriesMetadataProvider.PinsUnder("L'Attaque des Titans"))
            {
                Subject.GetSeries("Attack on Titan", 0, false, LibraryType.Manga, null, "Attack On Titan");
            }

            Mocker.GetMock<IMetadataOverridesService>()
                  .Verify(s => s.Apply("L'Attaque des Titans", It.IsAny<List<MangaVolumeMetadata>>()), Times.Once());
            Mocker.GetMock<IMetadataOverridesService>()
                  .Verify(s => s.Apply("Attack on Titan", It.IsAny<List<MangaVolumeMetadata>>()), Times.Never());
            Mocker.GetMock<IEditionResolver>()
                  .Verify(s => s.Resolve(It.IsAny<GcdSeries>(), It.IsAny<EditionRequest>(), It.IsAny<LibraryType>(), It.IsAny<string>()), Times.Never());
        }

        // No scope (every never-localized English series): today's key, the display name -- and a closed
        // scope leaves nothing behind for the next resolve.
        [Test]
        public void an_english_resolve_without_a_scope_pins_by_the_display_name()
        {
            using (MangaSeriesMetadataProvider.PinsUnder("L'Attaque des Titans"))
            {
            }

            Subject.GetSeries("Attack on Titan", 0, false, LibraryType.Manga, null, "Attack On Titan");

            MangaSeriesMetadataProvider.ScopedEnglishPinName.Should().BeNull();
            Mocker.GetMock<IMetadataOverridesService>()
                  .Verify(s => s.Apply("Attack on Titan", It.IsAny<List<MangaVolumeMetadata>>()), Times.Once());
            Mocker.GetMock<IMetadataOverridesService>()
                  .Verify(s => s.Apply("L'Attaque des Titans", It.IsAny<List<MangaVolumeMetadata>>()), Times.Never());
        }

        // The scope is English-only: an edition resolve keys by its own request, as before.
        [Test]
        public void an_edition_resolve_ignores_the_english_scope()
        {
            GivenResolution(French, "fr");

            using (MangaSeriesMetadataProvider.PinsUnder("Something Else"))
            {
                FrenchSeries();
            }

            Mocker.GetMock<IMetadataOverridesService>()
                  .Verify(s => s.Apply("L'Attaque des Titans", It.IsAny<List<MangaVolumeMetadata>>()), Times.Once());
            Mocker.GetMock<IMetadataOverridesService>()
                  .Verify(s => s.Apply("Something Else", It.IsAny<List<MangaVolumeMetadata>>()), Times.Never());
        }

        [Test]
        public void a_new_french_series_pins_by_its_french_name()
        {
            GivenResolution(French, "fr");

            FrenchSeries();

            Mocker.GetMock<IMetadataOverridesService>()
                  .Verify(s => s.Apply("L'Attaque des Titans", It.IsAny<List<MangaVolumeMetadata>>()), Times.Once());
        }

        [Test]
        public void a_light_novel_with_only_a_french_line_is_not_refused()
        {
            var frenchNovel = new GcdSeries { GcdSeriesId = 7001, Name = "Les Carnets de l'apothicaire", Language = "fr", VolumeCount = 5, TomeId = "rl_frln", TomeWorkId = "w_x", Medium = "light_novel", LocalName = "Les Carnets de l'apothicaire" };
            GivenResolution(frenchNovel, "fr");

            var s = Subject.GetSeries("Les Carnets de l'apothicaire", 0, false, LibraryType.LightNovel, null, "Les Carnets De L Apothicaire", new EditionRequest { Chain = new[] { "fr" } });

            s.NotInCatalogue.Should().BeFalse();
            s.VolumeCount.Should().Be(5);
        }

        // Preferred Edition (2026-09-24, M6b, spec §2.2 Covers). Ruling S5: the edition path reads its
        // volumes through GetVolumes(id, true), so the artifact cover is mocked there.
        [Test]
        public void a_french_edition_poster_prefers_the_artifact_cover_over_mangadex_locale_art()
        {
            GivenResolution(French, "fr");
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetVolumes(1004, true)).Returns(new List<GcdVolume>
            {
                new GcdVolume { VolumeNumber = 1, Isbn13 = "9782811611699", CoverUrl = "https://covers.openlibrary.org/b/isbn/9782811611699-L.jpg", CoverSource = "openlibrary" }
            });
            Mocker.GetMock<IMangaDexService>().Setup(s => s.GetLocaleCovers(53390, "Attack on Titan", "fr"))
                  .Returns(new MangaDexCovers { MangaId = "x", CoversByVolume = new Dictionary<int, string> { { 1, "https://uploads.mangadex.org/covers/x/fr1.jpg" }, { 2, "https://uploads.mangadex.org/covers/x/fr2.jpg" } } });
            Mocker.GetMock<IGoogleBooksService>().Setup(s => s.LookupByIsbn(It.IsAny<string>())).Returns(new VolumeDetails());

            var s = FrenchSeries(details: true);

            s.PosterSource.Should().Be("opentome");
            s.Volumes[1].CoverSource.Should().Be("mangadex-fr");
            Mocker.GetMock<IMangaDexService>().Verify(m => m.GetEnglishCovers(It.IsAny<int>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IGoogleBooksService>().Verify(g => g.LookupVolume(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void a_french_edition_without_an_artifact_cover_takes_mangadex_locale_art_for_the_poster()
        {
            GivenResolution(French, "fr");
            Mocker.GetMock<IMangaDexService>().Setup(s => s.GetLocaleCovers(53390, "Attack on Titan", "fr"))
                  .Returns(new MangaDexCovers { MangaId = "x", CoversByVolume = new Dictionary<int, string> { { 1, "https://uploads.mangadex.org/covers/x/fr1.jpg" } } });
            Mocker.GetMock<IGoogleBooksService>().Setup(s => s.LookupByIsbn(It.IsAny<string>())).Returns(new VolumeDetails());

            var s = FrenchSeries(details: true);

            s.PosterSource.Should().Be("mangadex-fr");
            s.CoverUrl.Should().EndWith("/fr1.jpg");
            s.Volumes[0].CoverSource.Should().Be("mangadex-fr");
        }

        // M6b (spec §2.2 Descriptions + Aliases): a French record's French blurb is taken; the edition
        // line's rows lead the alternate titles, read once for the record keys and the answer.
        [Test]
        public void a_french_edition_takes_french_blurbs_and_leads_its_titles_with_its_own_rows()
        {
            const string frBlurb = "Dans un monde ravagé par des titans mangeurs d'homme, l'humanité survit derrière de hauts murs.";
            GivenResolution(French, "fr");
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetAliasRows(1004)).Returns(new List<GcdAlias>
            {
                new GcdAlias { Alias = "Attack on Titan", Kind = "line" },
                new GcdAlias { Alias = "Les Titans Colossaux", Language = "fr", Kind = "alias" }
            });
            Mocker.GetMock<IGoogleBooksService>().Setup(s => s.LookupByIsbn("9782811611699"))
                  .Returns(new VolumeDetails { Title = "L'Attaque des Titans T01", Language = "fr", Description = frBlurb, Isbn13 = "9782811611699" });

            var s = FrenchSeries(details: true);

            s.Volumes[0].Overview.Should().Be(frBlurb);
            s.Volumes[0].OverviewSource.Should().Be("isbn");
            s.AltTitles.Should().Equal("Les Titans Colossaux", "Attack on Titan", "Shingeki no Kyojin");
            Mocker.GetMock<IGcdMetadataService>().Verify(g => g.GetAliasRows(1004), Times.Once());
        }

        // Preferred Edition (2026-09-24, M12): the Add form's Edition picker lists the languages of the work
        // the candidate's line belongs to; a candidate with no line has none.
        [Test]
        public void edition_options_come_from_the_line_the_series_resolved_to()
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_fr")).Returns(French);
            Mocker.GetMock<IEditionResolver>().Setup(r => r.Options(French, LibraryType.Manga))
                  .Returns(new List<EditionOption> { new EditionOption { Language = "en", Name = "English", VolumeCount = 34 }, new EditionOption { Language = "fr", Name = "French", VolumeCount = 20 } });

            Subject.EditionOptions("rl_fr", LibraryType.Manga).Should().HaveCount(2);
            Subject.EditionOptions(null, LibraryType.Manga).Should().BeEmpty();
        }

        // Without a catalogue there is no line to ask about -- and no read is made.
        [Test]
        public void edition_options_are_empty_without_a_catalogue()
        {
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(false);

            Subject.EditionOptions("rl_fr", LibraryType.Manga).Should().BeEmpty();
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.FindSeriesByTomeId(It.IsAny<string>()), Times.Never());
        }

        private static readonly GcdSeries EnglishNovel = new GcdSeries
        {
            GcdSeriesId = 5001, Name = "Sword Art Online", Language = "en", VolumeCount = 2, OrigSeriesId = 5003,
            TomeId = "rl_enln", TomeWorkId = "w_sao", Medium = "light_novel"
        };

        private static readonly GcdSeries FrenchNovel = new GcdSeries
        {
            GcdSeriesId = 5002, Name = "Sword Art Online", Language = "fr", VolumeCount = 2, OrigSeriesId = 5003,
            TomeId = "rl_frln", TomeWorkId = "w_sao", Medium = "light_novel", LocalName = "Sword Art Online"
        };

        private void GivenNovelEdition()
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTitle("Sword Art Online", LibraryType.LightNovel)).Returns(EnglishNovel);
            GivenResolution(FrenchNovel, "fr");
            Mocker.GetMock<IGoogleBooksService>().Setup(s => s.LookupByIsbn(It.IsAny<string>())).Returns(new VolumeDetails());
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.FindSeries(It.IsAny<string>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns(new AniListSeries { Id = 86302, EnglishTitle = "Sword Art Online", Format = "NOVEL", MatchedVia = "primary" });
        }

        private MangaSeriesMetadata FrenchNovelSeries()
        {
            return Subject.GetSeries("Sword Art Online", 0, true, LibraryType.LightNovel, null, "Sword Art Online", new EditionRequest { Language = "fr" });
        }

        [Test]
        public void a_one_to_one_french_novel_takes_english_audio_by_the_anchor_name()
        {
            GivenNovelEdition();

            var s = FrenchNovelSeries();

            s.AudioSkipped.Should().BeFalse();
            s.AudibleAnswered.Should().BeTrue();
            Mocker.GetMock<IAudibleCatalogService>().Verify(a => a.GetSeries("Sword Art Online", It.IsAny<IEnumerable<string>>()), Times.Once());
        }

        [Test]
        public void a_french_novel_in_collected_editions_skips_audible()
        {
            GivenNovelEdition();
            Mocker.GetMock<IEditionResolver>().Setup(r => r.IsCollected(FrenchNovel)).Returns(true);

            var s = FrenchNovelSeries();

            s.AudioSkipped.Should().BeTrue();
            s.AudibleAnswered.Should().BeFalse();
            Mocker.GetMock<IAudibleCatalogService>().Verify(a => a.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        // Ruling S4 (2026-09-24, M14): Audible is asked by the anchor's English name even when the edition's
        // own name differs; the display name stays the French one.
        [Test]
        public void a_one_to_one_french_novel_with_its_own_name_asks_audible_by_the_anchor_name()
        {
            GivenNovelEdition();
            var localized = new GcdSeries
            {
                GcdSeriesId = 5002, Name = "Sword Art Online", Language = "fr", VolumeCount = 2, OrigSeriesId = 5003,
                TomeId = "rl_frln", TomeWorkId = "w_sao", Medium = "light_novel", LocalName = "Sword Art Online - Le Roman"
            };
            GivenResolution(localized, "fr");

            var s = FrenchNovelSeries();

            s.DisplayName.Should().Be("Sword Art Online - Le Roman");
            s.AudioSkipped.Should().BeFalse();
            Mocker.GetMock<IAudibleCatalogService>().Verify(a => a.GetSeries("Sword Art Online", It.IsAny<IEnumerable<string>>()), Times.Once());
            Mocker.GetMock<IAudibleCatalogService>().Verify(a => a.GetSeries("Sword Art Online - Le Roman", It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        // D8: a French novel with no English line has no English audiobook to take.
        [Test]
        public void a_french_novel_with_no_english_line_skips_audible()
        {
            GivenNovelEdition();
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTitle("Sword Art Online", LibraryType.LightNovel)).Returns((GcdSeries)null);

            var s = FrenchNovelSeries();

            s.AudioSkipped.Should().BeTrue();
            s.AudibleAnswered.Should().BeFalse();
            Mocker.GetMock<IAudibleCatalogService>().Verify(a => a.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        // English is unchanged: the six-argument refresh asks Audible by the display name, is never
        // skipped, and never asks the resolver whether a line is collected.
        [Test]
        public void an_english_novel_asks_audible_by_its_name_and_never_skips()
        {
            GivenNovelEdition();

            var s = Subject.GetSeries("Sword Art Online", 0, true, LibraryType.LightNovel, null, "Sword Art Online");

            s.AudioSkipped.Should().BeFalse();
            s.AudibleAnswered.Should().BeTrue();
            s.EditionLanguage.Should().BeNull();
            Mocker.GetMock<IAudibleCatalogService>().Verify(a => a.GetSeries("Sword Art Online", It.IsAny<IEnumerable<string>>()), Times.Once());
            Mocker.GetMock<IEditionResolver>().Verify(r => r.IsCollected(It.IsAny<GcdSeries>()), Times.Never());
        }

        // M14 fix round 1 (2026-09-24, I1): the add and a new entry's first refresh run without volume
        // details -- the skip is decided there too, so the Audio editions they mint are unmonitored.
        private MangaSeriesMetadata FrenchNovelLookup()
        {
            return Subject.GetSeries("Sword Art Online", 0, false, LibraryType.LightNovel, null, "Sword Art Online", new EditionRequest { Language = "fr" });
        }

        [Test]
        public void a_collected_french_novel_is_skipped_without_volume_details()
        {
            GivenNovelEdition();
            Mocker.GetMock<IEditionResolver>().Setup(r => r.IsCollected(FrenchNovel)).Returns(true);

            FrenchNovelLookup().AudioSkipped.Should().BeTrue();
            Mocker.GetMock<IAudibleCatalogService>().Verify(a => a.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Test]
        public void a_french_novel_with_no_english_line_is_skipped_without_volume_details()
        {
            GivenNovelEdition();
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTitle("Sword Art Online", LibraryType.LightNovel)).Returns((GcdSeries)null);

            FrenchNovelLookup().AudioSkipped.Should().BeTrue();
        }

        [Test]
        public void a_one_to_one_french_novel_is_not_skipped_without_volume_details()
        {
            GivenNovelEdition();

            FrenchNovelLookup().AudioSkipped.Should().BeFalse();
            Mocker.GetMock<IAudibleCatalogService>().Verify(a => a.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Test]
        public void an_english_novel_without_volume_details_is_never_skipped()
        {
            GivenNovelEdition();

            var s = Subject.GetSeries("Sword Art Online", 0, false, LibraryType.LightNovel, null, "Sword Art Online");

            s.AudioSkipped.Should().BeFalse();
            Mocker.GetMock<IEditionResolver>().Verify(r => r.IsCollected(It.IsAny<GcdSeries>()), Times.Never());
            Mocker.GetMock<IAudibleCatalogService>().Verify(a => a.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        // M14 fix round 1 (I2): the origin-market line has no orig_series_id -- it IS the original the
        // English line points at (EditionResolver.IsCounterpart), so a Japanese edition takes English audio.
        private void GivenJapaneseNovel(int gcdSeriesId)
        {
            GivenNovelEdition();
            GivenResolution(new GcdSeries
            {
                GcdSeriesId = gcdSeriesId, Name = "Sword Art Online", Language = "ja", VolumeCount = 2, OrigSeriesId = null,
                TomeId = "rl_jaln", TomeWorkId = "w_sao", Medium = "light_novel", LocalName = "ソードアート・オンライン"
            }, "ja");
        }

        private MangaSeriesMetadata JapaneseNovelSeries()
        {
            return Subject.GetSeries("Sword Art Online", 0, true, LibraryType.LightNovel, null, "Sword Art Online", new EditionRequest { Language = "ja" });
        }

        [Test]
        public void the_origin_japanese_novel_takes_english_audio()
        {
            GivenJapaneseNovel(5003);

            var s = JapaneseNovelSeries();

            s.AudioSkipped.Should().BeFalse();
            s.AudibleAnswered.Should().BeTrue();
            Mocker.GetMock<IAudibleCatalogService>().Verify(a => a.GetSeries("Sword Art Online", It.IsAny<IEnumerable<string>>()), Times.Once());
        }

        [Test]
        public void a_japanese_novel_of_another_original_skips_audible()
        {
            GivenJapaneseNovel(5009);

            var s = JapaneseNovelSeries();

            s.AudioSkipped.Should().BeTrue();
            Mocker.GetMock<IAudibleCatalogService>().Verify(a => a.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Test]
        public void a_french_novel_against_a_collected_english_line_skips_audible()
        {
            GivenNovelEdition();
            Mocker.GetMock<IEditionResolver>().Setup(r => r.IsCollected(EnglishNovel)).Returns(true);

            var s = FrenchNovelSeries();

            s.AudioSkipped.Should().BeTrue();
            Mocker.GetMock<IAudibleCatalogService>().Verify(a => a.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Test]
        public void a_light_novel_with_no_line_in_any_chain_language_is_refused()
        {
            GivenResolution(null, null);

            var s = Subject.GetSeries("Les Carnets de l'apothicaire", 0, false, LibraryType.LightNovel, null, "Les Carnets De L Apothicaire", new EditionRequest { Chain = new[] { "fr" } });

            s.NotInCatalogue.Should().BeTrue();
        }
    }
}
