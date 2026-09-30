using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    [TestFixture]
    public class BookInfoProxyEditionsFixture : CoreTest<BookInfoProxy>
    {
        private const string Series = "Mushoku Tensei: Jobless Reincarnation";
        private const string MangaId = "local-mushoku-tensei-jobless-reincarnation";
        private const string LightNovelId = "local-mushoku-tensei-jobless-reincarnation~ln";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.FindById(It.IsAny<string>()))
                  .Returns((Author)null);

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns<string, int, bool, LibraryType, int?, string>((name, cap, details, library, anilistId, idName) => new MangaSeriesMetadata
                  {
                      DisplayName = Series,
                      VolumeCount = 2,
                      JapaneseTotal = 26,
                      VolumeCoverUrl = "https://uploads.mangadex.org/covers/bd6d0982/v1.jpg",
                      Volumes = new List<MangaVolumeMetadata>
                      {
                          new MangaVolumeMetadata { VolumeNumber = 1, Isbn13 = "9781626924154", PageCount = 320, Overview = "Rudeus Greyrat is reborn in a world of swords and sorcery.", CoverUrl = "https://covers.openlibrary.org/b/id/7382174-L.jpg" },
                          new MangaVolumeMetadata { VolumeNumber = 2 }
                      }
                  });
        }

        private void GivenNotInCatalogue()
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), LibraryType.LightNovel, It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns<string, int, bool, LibraryType, int?, string>((name, cap, details, library, anilistId, idName) => new MangaSeriesMetadata { DisplayName = name, NotInCatalogue = true });
        }

        // Beta polish (2026-09-28): a refresh stores the same CleanName the add path, the daily housekeeper
        // and FindByName use (interior articles and accents dropped, "~ln" for a light novel), so a
        // refresh no longer flips an entry to a form the exact name lookup misses.
        private void GivenDisplayName(string displayName)
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns(new MangaSeriesMetadata { DisplayName = displayName, VolumeCount = 1, Volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 1 } } });
        }

        [TestCase("local-trapped-in-a-dating-sim", "Trapped in a Dating Sim: The World of Otome Games Is Tough for Mobs", "trappedindatingsimworldotomegamesistoughformobs")]
        [TestCase("local-trapped-in-a-dating-sim~ln", "Trapped in a Dating Sim: The World of Otome Games Is Tough for Mobs", "trappedindatingsimworldotomegamesistoughformobs~ln")]
        [TestCase("local-re-zero~ln", "Re:ZERO -Starting Life in Another World-, Chapter 1: A Day in the Capital", "rezerostartinglifeinanotherworldchapter1dayincapital~ln")]
        [TestCase("local-cafe", "Café Terrace and Its Goddesses", "cafeterraceitsgoddesses")]
        [TestCase("local-punct", "!!!", "series")]
        public void a_refresh_stores_the_housekeeping_clean_name(string foreignAuthorId, string displayName, string expected)
        {
            GivenDisplayName(displayName);

            var author = Subject.GetAuthorInfo(foreignAuthorId);

            author.CleanName.Should().Be(expected);

            if (expected != "series")
            {
                author.CleanName.Should().Be(LibraryTypes.CleanNameFor(author.Name.CleanAuthorName(), author.Library));
            }
        }

        [Test]
        public void static_id_helper_carries_the_library()
        {
            BookInfoProxy.ForeignAuthorIdFor("Mushoku Tensei").Should().Be("local-mushoku-tensei");
            BookInfoProxy.ForeignAuthorIdFor("Mushoku Tensei", LibraryType.Manga).Should().Be("local-mushoku-tensei");
            BookInfoProxy.ForeignAuthorIdFor("Mushoku Tensei", LibraryType.LightNovel).Should().Be("local-mushoku-tensei~ln");
        }

        [Test]
        public void a_manga_volume_is_one_archive_edition_byte_for_byte()
        {
            var author = Subject.GetAuthorInfo(MangaId);

            author.ForeignAuthorId.Should().Be(MangaId);
            author.Library.Should().Be(LibraryType.Manga);
            author.CleanName.Should().Be("mushokutenseijoblessreincarnation");

            var book = author.Books.Value.First();
            book.ForeignBookId.Should().Be(MangaId + "-v1");

            var edition = book.Editions.Value.Single();
            edition.ForeignEditionId.Should().Be(MangaId + "-v1-ed");
            edition.TitleSlug.Should().Be(MangaId + "-v1-ed");
            edition.MediaType.Should().Be(MediaType.Archive);
            edition.Format.Should().Be("Paperback");
            edition.IsEbook.Should().BeFalse();
            edition.Monitored.Should().BeTrue();
            edition.ManualAdd.Should().BeTrue();
            edition.Language.Should().Be("eng");
            edition.Isbn13.Should().Be("9781626924154");
            edition.PageCount.Should().Be(320);
        }

        [Test]
        public void a_light_novel_volume_is_an_ebook_edition_and_an_audio_edition()
        {
            var author = Subject.GetAuthorInfo(LightNovelId);

            author.ForeignAuthorId.Should().Be(LightNovelId);
            author.Metadata.Value.TitleSlug.Should().Be(LightNovelId);
            author.Library.Should().Be(LibraryType.LightNovel);
            author.CleanName.Should().Be("mushokutenseijoblessreincarnation~ln");

            var books = author.Books.Value;
            books.Select(b => b.ForeignBookId).Should().Equal(LightNovelId + "-v1", LightNovelId + "-v2");
            books.Select(b => b.TitleSlug).Should().Equal(books.Select(b => b.ForeignBookId));

            var editions = books.First().Editions.Value;
            editions.Select(e => e.ForeignEditionId).Should().Equal(LightNovelId + "-v1-ed", LightNovelId + "-v1-audio-ed");
            editions.Select(e => e.MediaType).Should().Equal(MediaType.Ebook, MediaType.Audio);
            editions.Select(e => e.Format).Should().Equal("ebook", "Audiobook");
            editions.Select(e => e.IsEbook).Should().Equal(true, false);
            editions.Should().OnlyContain(e => e.Monitored && e.ManualAdd);
            editions.Should().OnlyContain(e => e.Book.Value == books.First());
        }

        // ABS titles match calibre (2026-09-23): CalibreProxy.SetFields sends calibre `edition.Title`
        // of the Ebook edition; the Audio edition (and Book itself) are minted from the same local
        // "title" variable in BuildFakeAuthor, so all three are always "<entry> Vol. N" -- including
        // when the entry name itself carries a colon, the shape the ABS-title bug (2026-09-23) hit.
        [Test]
        public void the_book_and_both_editions_share_the_calibre_title_even_with_a_colon_in_the_name()
        {
            var book = Subject.GetAuthorInfo(LightNovelId).Books.Value.First();
            var ebook = book.Editions.Value.Single(e => e.MediaType == MediaType.Ebook);
            var audio = book.Editions.Value.Single(e => e.MediaType == MediaType.Audio);

            book.Title.Should().Be("Mushoku Tensei: Jobless Reincarnation Vol. 1");
            ebook.Title.Should().Be(book.Title);
            audio.Title.Should().Be(book.Title);
        }

        [Test]
        public void both_light_novel_editions_carry_the_same_volume_facts()
        {
            var book = Subject.GetAuthorInfo(LightNovelId).Books.Value.First();
            var ebook = book.Editions.Value.Single(e => e.MediaType == MediaType.Ebook);
            var audio = book.Editions.Value.Single(e => e.MediaType == MediaType.Audio);

            audio.Title.Should().Be(ebook.Title);
            audio.Isbn13.Should().Be(ebook.Isbn13);
            audio.PageCount.Should().Be(ebook.PageCount);
            audio.ReleaseDate.Should().Be(ebook.ReleaseDate);
            audio.Overview.Should().Be(ebook.Overview);
        }

        [Test]
        public void the_provider_is_asked_in_the_entry_library_with_the_base_name()
        {
            Subject.GetAuthorInfo(LightNovelId);

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries("Mushoku Tensei Jobless Reincarnation", It.IsAny<int>(), It.IsAny<bool>(), LibraryType.LightNovel, null, "Mushoku Tensei Jobless Reincarnation"), Times.AtLeastOnce());
        }

        [Test]
        public void a_title_search_mints_the_requested_library()
        {
            Subject.SearchForNewAuthor("Mushoku Tensei Jobless Reincarnation").Single().ForeignAuthorId.Should().Be(MangaId);
            Subject.SearchForNewAuthor("Mushoku Tensei Jobless Reincarnation", LibraryType.LightNovel).Single().ForeignAuthorId.Should().Be(LightNovelId);

            var entities = Subject.SearchForNewEntity("Mushoku Tensei Jobless Reincarnation", LibraryType.LightNovel);
            entities.OfType<Author>().Single().ForeignAuthorId.Should().Be(LightNovelId);
            entities.OfType<Book>().Should().HaveCount(2);
        }

        [Test]
        public void a_book_id_resolves_back_to_its_light_novel_series()
        {
            var result = Subject.GetBookInfo(LightNovelId + "-v2");

            result.Item1.Should().Be(LightNovelId);
            result.Item2.ForeignBookId.Should().Be(LightNovelId + "-v2");
            result.Item2.Editions.Value.Should().HaveCount(2);
        }

        [Test]
        public void a_catalogue_miss_yields_an_empty_search_and_a_refused_resolve()
        {
            GivenNotInCatalogue();

            Subject.SearchForNewEntity("Mushoku Tensei", LibraryType.LightNovel).Should().BeEmpty();
            Subject.SearchForNewAuthor("Mushoku Tensei", LibraryType.LightNovel).Should().BeEmpty();
            Assert.Throws<NotInCatalogueException>(() => Subject.GetAuthorInfo("local-mushoku-tensei~ln"));

            // the manga library is untouched by a light-novel miss (the shared mock still resolves the
            // manga call to the series title, so the canonical-id rewrite yields the full slug)
            Subject.SearchForNewAuthor("Mushoku Tensei").Single().ForeignAuthorId.Should().Be(MangaId);
        }

        [Test]
        public void a_volume_without_a_description_has_an_empty_overview_not_a_placeholder()
        {
            var books = Subject.GetAuthorInfo(MangaId).Books.Value;

            books.First().Editions.Value.Single().Overview.Should().Be("Rudeus Greyrat is reborn in a world of swords and sorcery.");
            books.Last().Editions.Value.Single().Overview.Should().BeEmpty();
        }

        [Test]
        public void both_light_novel_editions_carry_the_volume_cover_and_the_series_poster_is_the_last_resort()
        {
            var books = Subject.GetAuthorInfo(LightNovelId).Books.Value;

            var withOwnCover = books.First().Editions.Value;
            withOwnCover.Select(e => e.Images.Single().Url).Should().OnlyContain(u => u == "https://covers.openlibrary.org/b/id/7382174-L.jpg");

            var withoutOwnCover = books.Last().Editions.Value;
            withoutOwnCover.Select(e => e.Images.Single().Url).Should().OnlyContain(u => u == "https://uploads.mangadex.org/covers/bd6d0982/v1.jpg");
            withoutOwnCover.Select(e => e.MediaType).Should().Equal(MediaType.Ebook, MediaType.Audio);
        }

        // Finding #2 of the 2026-09-16 review: a volume Google did not answer for gets NO image
        // rather than the poster, so Edition.UseMetadataFrom's ratchet keeps the cover a previous
        // pass fetched instead of swapping it for the poster for a day.
        [Test]
        public void a_volume_google_missed_is_minted_without_an_image_not_with_the_poster()
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns<string, int, bool, LibraryType, int?, string>((name, cap, details, library, anilistId, idName) => new MangaSeriesMetadata
                  {
                      DisplayName = Series,
                      VolumeCount = 2,
                      VolumeCoverUrl = "https://uploads.mangadex.org/covers/bd6d0982/v1.jpg",
                      Volumes = new List<MangaVolumeMetadata>
                      {
                          new MangaVolumeMetadata { VolumeNumber = 1, GoogleMissed = true },
                          new MangaVolumeMetadata { VolumeNumber = 2 }
                      }
                  });

            var books = Subject.GetAuthorInfo(LightNovelId).Books.Value;

            books.First().Editions.Value.Should().OnlyContain(e => !e.Images.Any());
            books.Last().Editions.Value.Select(e => e.Images.Single().Url).Should().OnlyContain(u => u == "https://uploads.mangadex.org/covers/bd6d0982/v1.jpg");
        }

        [Test]
        public void a_volume_google_missed_still_carries_its_own_cover_when_another_source_had_one()
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns<string, int, bool, LibraryType, int?, string>((name, cap, details, library, anilistId, idName) => new MangaSeriesMetadata
                  {
                      DisplayName = Series,
                      VolumeCount = 1,
                      VolumeCoverUrl = "https://uploads.mangadex.org/covers/bd6d0982/v1.jpg",
                      Volumes = new List<MangaVolumeMetadata>
                      {
                          new MangaVolumeMetadata { VolumeNumber = 1, GoogleMissed = true, CoverUrl = "https://covers.openlibrary.org/b/id/7382174-L.jpg" }
                      }
                  });

            var edition = Subject.GetAuthorInfo(MangaId).Books.Value.Single().Editions.Value.Single();

            edition.Images.Single().Url.Should().Be("https://covers.openlibrary.org/b/id/7382174-L.jpg");
        }

        // B3b (2026-09-18): the image-less mint above is the one case Edition.UseDbFieldsFrom carries
        // the stored images onto the remote (so the row compares equal instead of being rewritten
        // every quota-out pass) — flagged CoverMissed on both editions of the volume.
        [Test]
        public void a_volume_google_missed_without_a_cover_is_flagged_cover_missed_on_both_editions()
        {
            GivenVolume(new MangaVolumeMetadata { VolumeNumber = 1, GoogleMissed = true });

            var editions = Subject.GetAuthorInfo(LightNovelId).Books.Value.Single().Editions.Value;

            editions.Should().HaveCount(2);
            editions.Should().OnlyContain(e => !e.Images.Any());
            editions.Should().OnlyContain(e => e.CoverMissed);
        }

        [Test]
        public void a_volume_google_missed_with_a_cover_from_another_source_is_not_flagged_cover_missed()
        {
            GivenVolume(new MangaVolumeMetadata { VolumeNumber = 1, GoogleMissed = true, CoverUrl = "https://covers.openlibrary.org/b/id/7382174-L.jpg" });

            var edition = Subject.GetAuthorInfo(MangaId).Books.Value.Single().Editions.Value.Single();

            edition.Images.Should().ContainSingle();
            edition.CoverMissed.Should().BeFalse();
        }

        [Test]
        public void a_volume_that_did_not_miss_google_is_not_flagged_cover_missed()
        {
            GivenVolume(new MangaVolumeMetadata { VolumeNumber = 1 });

            var edition = Subject.GetAuthorInfo(MangaId).Books.Value.Single().Editions.Value.Single();

            edition.Images.Single().Url.Should().Be("https://uploads.mangadex.org/covers/bd6d0982/v1.jpg");
            edition.CoverMissed.Should().BeFalse();
        }

        // D5 (2026-09-17): a volume whose Google ISBN record was rejected (its title does not name
        // the series) is NOT a miss — the poster is minted so the ratchet replaces the cover that
        // record gave — and both editions carry the flag that clears the blurb it gave.
        [Test]
        public void a_volume_whose_record_was_rejected_gets_the_poster_and_the_overview_clearing_flag()
        {
            GivenVolume(new MangaVolumeMetadata { VolumeNumber = 1, GoogleRejected = true });

            var editions = Subject.GetAuthorInfo(LightNovelId).Books.Value.Single().Editions.Value;

            editions.Should().HaveCount(2);
            editions.Should().OnlyContain(e => e.Images.Single().Url == "https://uploads.mangadex.org/covers/bd6d0982/v1.jpg");
            editions.Should().OnlyContain(e => e.OverviewRejected);
            editions.Should().OnlyContain(e => e.Overview == string.Empty);
        }

        [Test]
        public void a_rejected_record_does_not_flag_a_blurb_another_source_gave()
        {
            GivenVolume(new MangaVolumeMetadata { VolumeNumber = 1, GoogleRejected = true, Overview = "Natsu and Lucy set out for the Tower of Heaven.", OverviewSource = "title" });

            var edition = Subject.GetAuthorInfo(MangaId).Books.Value.Single().Editions.Value.Single();

            edition.OverviewRejected.Should().BeFalse();
            edition.Overview.Should().Be("Natsu and Lucy set out for the Tower of Heaven.");
        }

        // The ISBN record was rejected and the title search then 429'd: a pass that could not ask
        // Google keeps the cover (the miss), so it keeps the blurb too; the next answering pass decides.
        [Test]
        public void a_rejected_volume_that_also_missed_google_keeps_its_blurb_this_pass()
        {
            GivenVolume(new MangaVolumeMetadata { VolumeNumber = 1, GoogleRejected = true, GoogleMissed = true, Overview = null });

            var editions = Subject.GetAuthorInfo(LightNovelId).Books.Value.Single().Editions.Value;

            editions.Should().HaveCount(2);
            editions.Should().OnlyContain(e => !e.OverviewRejected);
            editions.Should().OnlyContain(e => !e.Images.Any());
        }

        // Audiobook identity B1 (2026-09-17, D1): the provider's Audible identity and derived subtitle
        // are minted onto the Audio edition and the Book; the Ebook edition carries none of it.
        [Test]
        public void the_audio_edition_carries_the_audiobook_identity_and_the_ebook_edition_does_not()
        {
            GivenVolume(new MangaVolumeMetadata
            {
                VolumeNumber = 1,
                Subtitle = "Aincrad",
                Audio = new AudiobookIdentity { Asin = "1975337182", Title = "Sword Art Online 1: Aincrad", Subtitle = null, RuntimeMinutes = 483, ReleaseDate = new DateTime(2021, 8, 10) }
            });

            var book = Subject.GetAuthorInfo(LightNovelId).Books.Value.Single();
            book.Subtitle.Should().Be("Aincrad");

            var audio = book.Editions.Value.Single(e => e.MediaType == MediaType.Audio);
            audio.Asin.Should().Be("1975337182");
            audio.AudiobookTitle.Should().Be("Sword Art Online 1: Aincrad");
            audio.AudiobookSubtitle.Should().BeNull();
            audio.RuntimeMinutes.Should().Be(483);
            audio.AudioReleaseDate.Should().Be(new DateTime(2021, 8, 10));
            audio.CoveredByVolume.Should().BeNull();

            var ebook = book.Editions.Value.Single(e => e.MediaType == MediaType.Ebook);
            ebook.Asin.Should().BeNull();
            ebook.AudiobookTitle.Should().BeNull();
            ebook.AudiobookSubtitle.Should().BeNull();
            ebook.RuntimeMinutes.Should().BeNull();
            ebook.AudioReleaseDate.Should().BeNull();
            ebook.CoveredByVolume.Should().BeNull();
        }

        [Test]
        public void a_covered_volume_names_the_pack_carrier_on_its_audio_edition_only()
        {
            GivenVolume(new MangaVolumeMetadata { VolumeNumber = 2, CoveredByVolume = 1 });

            var editions = Subject.GetAuthorInfo(LightNovelId).Books.Value.Single().Editions.Value;

            editions.Single(e => e.MediaType == MediaType.Audio).CoveredByVolume.Should().Be(1);
            editions.Single(e => e.MediaType == MediaType.Audio).Asin.Should().BeNull();
            editions.Single(e => e.MediaType == MediaType.Ebook).CoveredByVolume.Should().BeNull();
        }

        [Test]
        public void a_volume_without_an_identity_mints_null_identity_fields_and_no_subtitle()
        {
            var books = Subject.GetAuthorInfo(LightNovelId).Books.Value;

            books.Should().OnlyContain(b => b.Subtitle == null);
            books.SelectMany(b => b.Editions.Value).Should().OnlyContain(e =>
                e.Asin == null && e.AudiobookTitle == null && e.AudiobookSubtitle == null && e.RuntimeMinutes == null && e.AudioReleaseDate == null && e.CoveredByVolume == null);
        }

        [Test]
        public void a_manga_volume_carries_no_audiobook_identity()
        {
            var book = Subject.GetAuthorInfo(MangaId).Books.Value.First();

            book.Subtitle.Should().BeNull();
            var edition = book.Editions.Value.Single();
            edition.MediaType.Should().Be(MediaType.Archive);
            edition.Asin.Should().BeNull();
            edition.AudiobookTitle.Should().BeNull();
            edition.CoveredByVolume.Should().BeNull();
        }

        // B2 (2026-09-17, D4a): the Audio edition names who wrote its covered mark ("audible" when the
        // volume has one) and whether this pass asserts it — Audible answered, so the mark or its
        // absence replaces an "audible" mark the ratchet holds. The Ebook edition and a manga's
        // Archive edition carry neither (D7).
        [Test]
        public void a_covered_volume_on_a_pass_audible_answered_is_an_asserted_audible_mark()
        {
            GivenVolume(new MangaVolumeMetadata { VolumeNumber = 2, CoveredByVolume = 1 }, audibleAnswered: true);

            var editions = Subject.GetAuthorInfo(LightNovelId).Books.Value.Single().Editions.Value;

            var audio = editions.Single(e => e.MediaType == MediaType.Audio);
            audio.CoveredByVolume.Should().Be(1);
            audio.CoveredSource.Should().Be(CoveredSources.Audible);
            audio.CoveredAsserted.Should().BeTrue();

            var ebook = editions.Single(e => e.MediaType == MediaType.Ebook);
            ebook.CoveredByVolume.Should().BeNull();
            ebook.CoveredSource.Should().BeNull();
            ebook.CoveredAsserted.Should().BeFalse();
        }

        [Test]
        public void an_uncovered_volume_on_a_pass_audible_answered_asserts_no_mark()
        {
            GivenVolume(new MangaVolumeMetadata { VolumeNumber = 1 }, audibleAnswered: true);

            var audio = Subject.GetAuthorInfo(LightNovelId).Books.Value.Single().Editions.Value.Single(e => e.MediaType == MediaType.Audio);

            audio.CoveredByVolume.Should().BeNull();
            audio.CoveredSource.Should().BeNull();
            audio.CoveredAsserted.Should().BeTrue();
        }

        [Test]
        public void a_pass_audible_did_not_answer_mints_the_mark_unasserted()
        {
            GivenVolume(new MangaVolumeMetadata { VolumeNumber = 2, CoveredByVolume = 1 }, audibleAnswered: false);

            var audio = Subject.GetAuthorInfo(LightNovelId).Books.Value.Single().Editions.Value.Single(e => e.MediaType == MediaType.Audio);

            audio.CoveredByVolume.Should().Be(1);
            audio.CoveredSource.Should().Be(CoveredSources.Audible);
            audio.CoveredAsserted.Should().BeFalse();
        }

        [Test]
        public void a_manga_edition_carries_no_covered_mark_even_when_the_series_says_audible_answered()
        {
            GivenVolume(new MangaVolumeMetadata { VolumeNumber = 2, CoveredByVolume = 1 }, audibleAnswered: true);

            var edition = Subject.GetAuthorInfo(MangaId).Books.Value.Single().Editions.Value.Single();

            edition.MediaType.Should().Be(MediaType.Archive);
            edition.CoveredByVolume.Should().BeNull();
            edition.CoveredSource.Should().BeNull();
            edition.CoveredAsserted.Should().BeFalse();
        }

        private void GivenVolume(MangaVolumeMetadata volume, bool audibleAnswered = false)
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns<string, int, bool, LibraryType, int?, string>((name, cap, details, library, anilistId, idName) => new MangaSeriesMetadata
                  {
                      DisplayName = Series,
                      VolumeCount = 1,
                      VolumeCoverUrl = "https://uploads.mangadex.org/covers/bd6d0982/v1.jpg",
                      AudibleAnswered = audibleAnswered,
                      Volumes = new List<MangaVolumeMetadata> { volume }
                  });
        }
    }
}
