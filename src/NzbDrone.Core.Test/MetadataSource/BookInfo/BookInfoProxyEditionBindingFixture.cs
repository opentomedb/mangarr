using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    // Preferred Edition (2026-09-24): which call BookInfoProxy makes and what it binds.
    [TestFixture]
    public class BookInfoProxyEditionBindingFixture : CoreTest<BookInfoProxy>
    {
        private static MangaSeriesMetadata English()
        {
            return new MangaSeriesMetadata
            {
                DisplayName = "Attack on Titan", IdentityName = "Attack on Titan", TomeLineId = "rl_en", VolumeCount = 1,
                Volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 1 } }
            };
        }

        private static MangaSeriesMetadata French()
        {
            return new MangaSeriesMetadata
            {
                DisplayName = "L'Attaque des Titans", IdentityName = "Attack on Titan", EditionLanguage = "fr", TomeLineId = "rl_fr", VolumeCount = 1,
                Volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 1, ReleaseDate = new System.DateTime(2019, 12, 31, 6, 0, 0, System.DateTimeKind.Utc), ReleaseDatePrecision = "year" } }
            };
        }

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.PreferredEditionLanguages).Returns("fr,en");
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById(It.IsAny<string>())).Returns((Author)null);

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns(English());
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()))
                  .Returns(French());
        }

        private static Author Existing(string name, string edition, string tomeLineId, string anchorName)
        {
            return new Author
            {
                Id = 7,
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = "local-attack-on-titan", Name = name, EditionLanguage = edition, TomeLineId = tomeLineId, AnchorName = anchorName
                }
            };
        }

        [Test]
        public void a_search_candidate_under_a_french_chain_keeps_an_english_identity()
        {
            var author = Subject.SearchForNewAuthor("attaque des titans", LibraryType.Manga).Single();

            author.ForeignAuthorId.Should().Be("local-attack-on-titan");
            author.Name.Should().Be("L'Attaque des Titans");
            author.Metadata.Value.EditionLanguage.Should().Be("fr");
            author.Metadata.Value.TomeLineId.Should().Be("rl_fr");
            author.Metadata.Value.AnchorName.Should().Be("Attack on Titan");
        }

        [Test]
        public void a_french_volume_is_titled_by_its_tome_and_its_edition_is_french()
        {
            var book = Subject.SearchForNewAuthor("attaque des titans", LibraryType.Manga).Single().Books.Value.Single();

            book.Title.Should().Be("L'Attaque des Titans Tome 1");
            book.ReleaseDatePrecision.Should().Be("year");
            book.Editions.Value.Single().Language.Should().Be("fra");
            book.Editions.Value.Single().Title.Should().Be("L'Attaque des Titans Tome 1");
        }

        [Test]
        public void an_existing_english_series_stays_on_the_six_argument_path_after_the_chain_changes()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(Existing("Attack on Titan", null, null, null));

            var author = Subject.GetAuthorInfo("local-attack-on-titan");

            author.Name.Should().Be("Attack on Titan");
            author.Metadata.Value.EditionLanguage.Should().BeNull();
            author.Metadata.Value.AnchorName.Should().BeNull();
            author.Metadata.Value.TomeLineId.Should().Be("rl_en");
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()), Times.Never());
        }

        // 2026-09-26 (migration 058): the bound line's collected flag follows the line the pass bound.
        [Test]
        public void a_refresh_binds_whether_the_line_is_collected()
        {
            var collected = French();
            collected.EditionCollected = true;
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()))
                  .Returns(collected);
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(Existing("L'Attaque des Titans", "fr", "rl_fr", "Attack on Titan"));

            Subject.GetAuthorInfo("local-attack-on-titan").Metadata.Value.EditionCollected.Should().BeTrue();

            collected.EditionCollected = false;
            Subject.GetAuthorInfo("local-attack-on-titan").Metadata.Value.EditionCollected.Should().BeFalse();
        }

        // A pass that binds no line keeps the stored flag with the stored line.
        [Test]
        public void a_pass_without_a_line_keeps_the_stored_collected_flag()
        {
            var unbound = French();
            unbound.TomeLineId = null;
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()))
                  .Returns(unbound);
            var existing = Existing("L'Attaque des Titans", "fr", "rl_fr", "Attack on Titan");
            existing.Metadata.Value.EditionCollected = true;
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(existing);

            var meta = Subject.GetAuthorInfo("local-attack-on-titan").Metadata.Value;

            meta.TomeLineId.Should().Be("rl_fr");
            meta.EditionCollected.Should().BeTrue();
        }

        [Test]
        public void a_bound_french_series_refreshes_by_its_anchor_name_and_bound_line()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(Existing("L'Attaque des Titans", "fr", "rl_fr", "Attack on Titan"));

            var author = Subject.GetAuthorInfo("local-attack-on-titan");

            author.Name.Should().Be("L'Attaque des Titans");
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries("Attack on Titan", It.IsAny<int>(), It.IsAny<bool>(), LibraryType.Manga, It.IsAny<int?>(), It.IsAny<string>(),
                      It.Is<EditionRequest>(r => r.Language == "fr" && r.TomeLineId == "rl_fr" && r.Chain == null)), Times.Once());
        }

        // Ruling S1: the stored name rides in the request, so the provider keys pins by it.
        [Test]
        public void a_bound_series_passes_its_stored_name_for_the_pins()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(Existing("L'Attaque des Titans", "fr", "rl_fr", "Attack on Titan"));

            Subject.GetAuthorInfo("local-attack-on-titan");

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(),
                      It.Is<EditionRequest>(r => r.StoredName == "L'Attaque des Titans")), Times.Once());
        }

        // Rulings S1 + A1/S3: a compatible re-resolve to French that kept the English name. The name is
        // the anchor, so AnchorName stays null, the query is the stored name, and the pins stay on it.
        [Test]
        public void a_french_series_that_kept_the_anchor_name_has_no_anchor_name()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(Existing("Attack on Titan", "fr", "rl_fr", null));

            var author = Subject.GetAuthorInfo("local-attack-on-titan");

            author.Name.Should().Be("Attack on Titan");
            author.Metadata.Value.EditionLanguage.Should().Be("fr");
            author.Metadata.Value.TomeLineId.Should().Be("rl_fr");
            author.Metadata.Value.AnchorName.Should().BeNull();
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries("Attack on Titan", It.IsAny<int>(), It.IsAny<bool>(), LibraryType.Manga, It.IsAny<int?>(), It.IsAny<string>(),
                      It.Is<EditionRequest>(r => r.Language == "fr" && r.StoredName == "Attack on Titan")), Times.Once());
        }

        // A1/S3: an edition line without a local name is named by the anchor -- no AnchorName either.
        [Test]
        public void a_new_french_series_named_by_the_anchor_has_no_anchor_name()
        {
            var french = French();
            french.DisplayName = "Attack on Titan";
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()))
                  .Returns(french);

            var author = Subject.SearchForNewAuthor("attack on titan", LibraryType.Manga).Single();

            author.Metadata.Value.EditionLanguage.Should().Be("fr");
            author.Metadata.Value.AnchorName.Should().BeNull();
        }

        [Test]
        public void a_skipped_audio_edition_is_minted_unmonitored()
        {
            var ln = French();
            ln.AudioSkipped = true;
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), LibraryType.LightNovel, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()))
                  .Returns(ln);

            var book = Subject.SearchForNewAuthor("sword art online", LibraryType.LightNovel).Single().Books.Value.Single();

            book.Editions.Value.Single(e => e.MediaType == MediaType.Audio).Monitored.Should().BeFalse();
            book.Editions.Value.Single(e => e.MediaType == MediaType.Ebook).Monitored.Should().BeTrue();
        }

        // M14 fix round 1 (2026-09-24, I1): the add mints the stored editions without volume details --
        // a skipped series' Audio editions are unmonitored from that first mint.
        [Test]
        public void an_added_skipped_novel_mints_its_audio_edition_unmonitored()
        {
            var ln = French();
            ln.AudioSkipped = true;
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), false, LibraryType.LightNovel, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()))
                  .Returns(ln);

            var book = Subject.GetAuthorInfo("local-sword-art-online~ln", false, false, "fr").Books.Value.Single();

            book.Editions.Value.Single(e => e.MediaType == MediaType.Audio).Monitored.Should().BeFalse();
            book.Editions.Value.Single(e => e.MediaType == MediaType.Ebook).Monitored.Should().BeTrue();
        }

        // Final fix round Minor 2 (2026-09-24): a French light novel's Audio edition is the English audiobook
        // (D8) and is stamped "eng"; its Ebook edition keeps the edition language.
        [Test]
        public void a_french_light_novels_audio_edition_is_english_and_its_ebook_is_french()
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), LibraryType.LightNovel, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()))
                  .Returns(French());

            var book = Subject.GetAuthorInfo("local-sword-art-online~ln", false, true, "fr").Books.Value.Single();

            book.Editions.Value.Single(e => e.MediaType == MediaType.Audio).Language.Should().Be("eng");
            book.Editions.Value.Single(e => e.MediaType == MediaType.Ebook).Language.Should().Be("fra");
        }

        // Preferred Edition (2026-09-24, M14): an English light novel mints both editions monitored, as today.
        [Test]
        public void an_english_light_novel_mints_both_editions_monitored()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.PreferredEditionLanguages).Returns("en");
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), LibraryType.LightNovel, It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns(English());

            var book = Subject.SearchForNewAuthor("sword art online", LibraryType.LightNovel).Single().Books.Value.Single();

            book.Editions.Value.Should().HaveCount(2);
            book.Editions.Value.Should().OnlyContain(e => e.Monitored);
        }

        [Test]
        public void a_light_novel_with_no_line_in_any_chain_language_is_refused_as_such()
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()))
                  .Returns(new MangaSeriesMetadata { DisplayName = "Les Carnets", NotInCatalogue = true });

            var ex = Assert.Throws<NotInCatalogueException>(() => Subject.GetAuthorInfo("local-les-carnets~ln"));

            ex.NoChainLine.Should().BeTrue();
            ex.Message.Should().Contain("no novel line in any chain language");
        }

        // M5 pre-review fix: search and identification are read-only -- a bound line the catalogue lost
        // answers from the English anchor with one Warn, and nothing escapes.
        private void GivenABoundFrenchSeriesWhoseLineVanished()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(Existing("L'Attaque des Titans", "fr", "rl_fr", "Attack on Titan"));
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()))
                  .Throws(new EditionUnavailableException("Attack on Titan", "fr", "rl_fr"));
        }

        private void VerifyAnsweredFromTheAnchor()
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries("Attack on Titan", It.IsAny<int>(), false, LibraryType.Manga, It.IsAny<int?>(), It.IsAny<string>()), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_search_for_a_series_whose_bound_line_vanished_answers_from_the_anchor()
        {
            GivenABoundFrenchSeriesWhoseLineVanished();

            var author = Subject.SearchForNewAuthor("attack on titan", LibraryType.Manga).Single();

            author.ForeignAuthorId.Should().Be("local-attack-on-titan");
            author.Books.Value.Should().HaveCount(1);

            // Fix round 1: the stored binding travels as one -- never the anchor's line ("rl_en") under
            // the stored language.
            author.Name.Should().Be("L'Attaque des Titans");
            author.Metadata.Value.EditionLanguage.Should().Be("fr");
            author.Metadata.Value.TomeLineId.Should().Be("rl_fr");
            author.Metadata.Value.AnchorName.Should().Be("Attack on Titan");
            VerifyAnsweredFromTheAnchor();
        }

        [Test]
        public void an_entity_search_for_a_series_whose_bound_line_vanished_answers_from_the_anchor()
        {
            GivenABoundFrenchSeriesWhoseLineVanished();

            Subject.SearchForNewEntity("attack on titan", LibraryType.Manga).Should().NotBeEmpty();

            VerifyAnsweredFromTheAnchor();
        }

        [Test]
        public void identification_of_a_series_whose_bound_line_vanished_answers_from_the_anchor()
        {
            GivenABoundFrenchSeriesWhoseLineVanished();

            var books = Subject.SearchForNewBook("Attack on Titan Vol. 1", null);

            books.Should().ContainSingle(b => b.VolumeNumber == 1);
            VerifyAnsweredFromTheAnchor();
        }

        // Fix round 1 (I1): AnchorName is create-only -- a refresh never rewrites it from this pass's
        // IdentityName, which drifts (relaxed add vs strict refresh, an AniList retitle).
        [Test]
        public void a_refresh_keeps_the_stored_anchor_name_when_the_identity_drifts()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(Existing("L'Attaque des Titans", "fr", "rl_fr", "Attack on Titan"));
            var drifted = French();
            drifted.IdentityName = "Attack on Titan: The Final Season";
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()))
                  .Returns(drifted);

            var author = Subject.GetAuthorInfo("local-attack-on-titan");

            author.Metadata.Value.AnchorName.Should().Be("Attack on Titan");
        }

        [Test]
        public void a_refresh_keeps_a_null_anchor_name_under_a_curated_name()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(Existing("Shingeki", "fr", "rl_fr", null));

            var author = Subject.GetAuthorInfo("local-attack-on-titan");

            author.Name.Should().Be("Shingeki");
            author.Metadata.Value.AnchorName.Should().BeNull();
        }

        [Test]
        public void a_re_resolve_written_mid_refresh_wins()
        {
            Mocker.GetMock<IAuthorService>().SetupSequence(s => s.FindById("local-attack-on-titan"))
                  .Returns(Existing("L'Attaque des Titans", "fr", "rl_fr", "Attack on Titan"))
                  .Returns(Existing("L'Attaque des Titans", "de", "rl_de", "Attack on Titan"));

            var author = Subject.GetAuthorInfo("local-attack-on-titan");

            author.Metadata.Value.EditionLanguage.Should().Be("de");
            author.Metadata.Value.TomeLineId.Should().Be("rl_de");
        }

        // Preferred Edition (2026-09-24, M13 regression): the pass that loses the race keeps the stored binding,
        // but its volumes stay on the line it fetched -- one pass never mixes the two. Change Edition queues
        // its own refresh, which re-mints them from the new line.
        [Test]
        public void a_re_resolve_written_mid_refresh_leaves_the_volumes_on_this_passs_line()
        {
            Mocker.GetMock<IAuthorService>().SetupSequence(s => s.FindById("local-attack-on-titan"))
                  .Returns(Existing("L'Attaque des Titans", "fr", "rl_fr", "Attack on Titan"))
                  .Returns(Existing("L'Attaque des Titans", "de", "rl_de", "Attack on Titan"));

            var author = Subject.GetAuthorInfo("local-attack-on-titan");
            var book = author.Books.Value.Single();

            author.Metadata.Value.EditionLanguage.Should().Be("de");
            book.Title.Should().Be("L'Attaque des Titans Tome 1");
            book.Editions.Value.Single().Language.Should().Be("fra");
        }

        [Test]
        public void an_english_series_re_resolved_mid_refresh_keeps_this_passs_english_volumes()
        {
            Mocker.GetMock<IAuthorService>().SetupSequence(s => s.FindById("local-attack-on-titan"))
                  .Returns(Existing("Attack on Titan", null, null, null))
                  .Returns(Existing("Attack on Titan", "fr", "rl_fr", null));

            var author = Subject.GetAuthorInfo("local-attack-on-titan");
            var book = author.Books.Value.Single();

            author.Metadata.Value.EditionLanguage.Should().Be("fr");
            author.Metadata.Value.TomeLineId.Should().Be("rl_fr");
            book.Title.Should().Be("Attack on Titan Vol. 1");
            book.Editions.Value.Single().Language.Should().Be("eng");
        }

        // M13 pre-review fix, ruling (c): the six-argument call runs inside a scope naming the stored name only
        // when the entry carries an AnchorName; the scope is read during the call.
        private string CaptureScopedPinName()
        {
            string captured = "(not called)";
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>()))
                  .Callback(() => captured = MangaSeriesMetadataProvider.ScopedEnglishPinName)
                  .Returns(English());

            Subject.GetAuthorInfo("local-attack-on-titan");

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()), Times.Never());
            MangaSeriesMetadataProvider.ScopedEnglishPinName.Should().BeNull();

            return captured;
        }

        [Test]
        public void a_series_localized_then_back_to_english_keeps_its_pins_under_its_stored_name()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(Existing("L'Attaque des Titans", null, "rl_en", "Attack on Titan"));

            CaptureScopedPinName().Should().Be("L'Attaque des Titans");
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries("Attack on Titan", It.IsAny<int>(), It.IsAny<bool>(), LibraryType.Manga, It.IsAny<int?>(), It.IsAny<string>()), Times.Once());
        }

        // A curated English name (the Mushoku case) with no AnchorName: no scope, today's display-name key.
        [Test]
        public void an_english_series_with_a_curated_name_and_no_anchor_opens_no_scope()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(Existing("Mushoku Tensei", null, null, null));

            CaptureScopedPinName().Should().BeNull();
        }

        [Test]
        public void an_explicit_edition_on_add_is_the_request()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.PreferredEditionLanguages).Returns("en");

            Subject.GetAuthorInfo("local-attack-on-titan", false, false, "fr");

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), false, LibraryType.Manga, It.IsAny<int?>(), It.IsAny<string>(),
                      It.Is<EditionRequest>(r => r.Language == "fr" && r.Chain == null)), Times.Once());
        }

        // Preferred Edition (2026-09-24, M6b): a disk-discovered extra volume of a French series is
        // resolved in French; an English series' extra keeps the three-argument call.
        private void GivenAnExtraVolumeOnDisk(Author existing)
        {
            existing.Path = "/series/aot";
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(existing);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists("/series/aot")).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFiles("/series/aot", true)).Returns(new[] { "/series/aot/Attack on Titan Vol 3.cbz" });
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.ResolveVolume(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<bool>()))
                  .Returns(new MangaVolumeMetadata { VolumeNumber = 3 });
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.ResolveVolume(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<string>()))
                  .Returns(new MangaVolumeMetadata { VolumeNumber = 3 });
        }

        [Test]
        public void a_french_series_extra_volume_is_resolved_in_french()
        {
            GivenAnExtraVolumeOnDisk(Existing("L'Attaque des Titans", "fr", "rl_fr", "Attack on Titan"));

            Subject.GetAuthorInfo("local-attack-on-titan");

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.ResolveVolume("L'Attaque des Titans", 3, It.IsAny<bool>(), "fr"), Times.Once());
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.ResolveVolume(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<bool>()), Times.Never());
        }

        // Preferred Edition (2026-09-24, M13 fix round 1): a compatible change to a SHORTER line (34 -> 32) with
        // files on volumes 33-34. The refresh the change queues mints 1-32 from the French line;
        // DiscoverExtraVolumes finds 33 and 34 on disk (their files keep Mangarr's "Vol." naming, which the
        // French parse still reads) and re-mints them under the same foreign book ids, so the books -- and
        // the files attached to them -- stay. (RefreshBookService.ShouldDelete would keep a book with a file
        // even without this.)
        [Test]
        public void a_change_to_a_shorter_line_keeps_the_volumes_that_have_files()
        {
            var existing = Existing("Attack on Titan", "fr", "rl_fr", null);
            existing.Path = "/series/aot";
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-attack-on-titan")).Returns(existing);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists("/series/aot")).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFiles("/series/aot", true))
                  .Returns(new[] { "/series/aot/Attack on Titan Vol. 33.cbz", "/series/aot/Attack on Titan Vol. 34.cbz" });
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.ResolveVolume(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<string>()))
                  .Returns((string n, double v, bool d, string l) => new MangaVolumeMetadata { VolumeNumber = v });
            var shorter = French();
            shorter.DisplayName = "Attack on Titan";
            shorter.VolumeCount = 32;
            shorter.Volumes = Enumerable.Range(1, 32).Select(v => new MangaVolumeMetadata { VolumeNumber = v }).ToList();
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()))
                  .Returns(shorter);

            var books = Subject.GetAuthorInfo("local-attack-on-titan").Books.Value;

            books.Should().HaveCount(34);
            books.Select(b => b.ForeignBookId).Should().Contain(new[] { "local-attack-on-titan-v33", "local-attack-on-titan-v34" });
            books.Single(b => b.ForeignBookId == "local-attack-on-titan-v33").Title.Should().Be("Attack on Titan Tome 33");
        }

        [Test]
        public void an_english_series_extra_volume_keeps_the_three_argument_call()
        {
            GivenAnExtraVolumeOnDisk(Existing("Attack on Titan", null, null, null));

            Subject.GetAuthorInfo("local-attack-on-titan");

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.ResolveVolume("Attack on Titan", 3, It.IsAny<bool>()), Times.Once());
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.ResolveVolume(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void an_explicit_english_edition_on_add_is_the_english_call()
        {
            Subject.GetAuthorInfo("local-attack-on-titan", false, false, "en");

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()), Times.Never());
        }

        // Preferred Edition (2026-09-24, M9 pre-review fix): a search term carrying a chain language's volume
        // token ("… Tome 5", a French book tag) slugs -- and so queries -- without it.
        [Test]
        public void a_search_term_with_a_french_tome_slugs_and_queries_without_it()
        {
            Subject.SearchForNewBook("L'Attaque des Titans Tome 5", null);

            Mocker.GetMock<IAuthorService>().Verify(s => s.FindById("local-l-attaque-des-titans"), Times.Once());
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries("L Attaque Des Titans", It.IsAny<int>(), It.IsAny<bool>(), LibraryType.Manga, It.IsAny<int?>(), "L Attaque Des Titans", It.IsAny<EditionRequest>()), Times.Once());
        }

        [Test]
        public void an_english_chain_slugs_a_tome_term_as_today()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.PreferredEditionLanguages).Returns("en");

            Subject.SearchForNewBook("L'Attaque des Titans Tome 5", null);

            Mocker.GetMock<IAuthorService>().Verify(s => s.FindById("local-l-attaque-des-titans-tome-5"), Times.Once());
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries("L Attaque Des Titans Tome 5", It.IsAny<int>(), It.IsAny<bool>(), LibraryType.Manga, It.IsAny<int?>(), "L Attaque Des Titans Tome 5"), Times.Once());
        }

        // A token needs its number, and today's "Vol." reading goes first: a series named with "Tome" or
        // "Band" keeps its name under a French + German chain.
        [TestCase("Tome of the Dead", "local-tome-of-the-dead")]
        [TestCase("Band of Brothers Vol. 3", "local-band-of-brothers")]
        public void a_series_named_with_tome_or_band_keeps_its_name(string term, string id)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.PreferredEditionLanguages).Returns("fr,de,en");

            Subject.SearchForNewBook(term, null);

            Mocker.GetMock<IAuthorService>().Verify(s => s.FindById(id), Times.Once());
        }

        // Preferred Edition (2026-09-24, M12): the Add form's Edition picker reads the languages the
        // candidate's work has a line in.
        [Test]
        public void a_search_candidate_carries_the_languages_its_work_has()
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>().Setup(s => s.EditionOptions("rl_fr", LibraryType.Manga))
                  .Returns(new List<EditionOption> { new EditionOption { Language = "fr", Name = "French", VolumeCount = 20 } });

            var author = Subject.SearchForNewAuthor("attaque des titans", LibraryType.Manga).Single();

            author.Metadata.Value.EditionOptions.Should().ContainSingle(o => o.Language == "fr");
        }

        // The Add page's own search (/search) serialises the author of each volume row too -- the same metadata.
        [Test]
        public void an_add_page_search_result_carries_the_languages_its_work_has()
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>().Setup(s => s.EditionOptions("rl_fr", LibraryType.Manga))
                  .Returns(new List<EditionOption> { new EditionOption { Language = "fr", Name = "French", VolumeCount = 20 } });

            var results = Subject.SearchForNewEntity("attaque des titans", LibraryType.Manga);

            results.OfType<Author>().Single().Metadata.Value.EditionOptions.Should().ContainSingle(o => o.Language == "fr");
            results.OfType<Book>().Single().Author.Value.Metadata.Value.EditionOptions.Should().ContainSingle(o => o.Language == "fr");
        }

        // English chain: a candidate bound to a catalogue line still lists its work's languages (a work with a
        // French line offers it), through the English six-argument resolve.
        [Test]
        public void an_english_chain_candidate_on_a_line_lists_its_works_languages()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.PreferredEditionLanguages).Returns("en");
            Mocker.GetMock<IMangaSeriesMetadataProvider>().Setup(s => s.EditionOptions("rl_en", LibraryType.Manga))
                  .Returns(new List<EditionOption> { new EditionOption { Language = "en", Name = "English", VolumeCount = 34 }, new EditionOption { Language = "fr", Name = "French", VolumeCount = 20 } });

            var author = Subject.SearchForNewAuthor("attack on titan", LibraryType.Manga).Single();

            author.Metadata.Value.EditionOptions.Should().HaveCount(2);
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()), Times.Never());
        }

        // The gate: a candidate with no catalogue line costs no options read at all.
        [Test]
        public void an_english_chain_candidate_with_no_line_asks_for_no_options()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.PreferredEditionLanguages).Returns("en");
            var unbound = English();
            unbound.TomeLineId = null;
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns(unbound);

            Subject.SearchForNewAuthor("attack on titan", LibraryType.Manga);
            Subject.SearchForNewEntity("attack on titan", LibraryType.Manga);

            Mocker.GetMock<IMangaSeriesMetadataProvider>().Verify(s => s.EditionOptions(It.IsAny<string>(), It.IsAny<LibraryType>()), Times.Never());
        }

        // Identification (manual import, queue refresh) and an add or refresh never read the options.
        [Test]
        public void identification_and_an_add_ask_for_no_options()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.PreferredEditionLanguages).Returns("en");

            Subject.SearchForNewBook("Attack on Titan Vol. 5", null);
            Subject.GetAuthorInfo("local-attack-on-titan");

            Mocker.GetMock<IMangaSeriesMetadataProvider>().Verify(s => s.EditionOptions(It.IsAny<string>(), It.IsAny<LibraryType>()), Times.Never());
        }
    }
}
