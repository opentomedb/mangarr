using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    // D4 on the refresh path: the stored AniList id goes IN to the provider (with the entry's
    // de-slugged id for the alias retry) and the resolved id comes OUT on the metadata the
    // refresh upserts. The upsert replaces the row, so a resolve that found nothing must carry
    // the stored id forward itself — and the id is re-read at write time, because the store
    // can change while the resolve runs.
    [TestFixture]
    public class BookInfoProxyBindingFixture : CoreTest<BookInfoProxy>
    {
        private const string MangaId = "local-mushoku-tensei-jobless-reincarnation";

        // A stored entry with the presentation an earlier bound refresh wrote.
        private static Author Stored(int? anilistId)
        {
            return new Author
            {
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = MangaId,
                    Name = "Mushoku Tensei",
                    AniListId = anilistId,
                    Overview = "Stored overview.",
                    Status = AuthorStatusType.Ended,
                    TotalVolumes = 26,
                    Aliases = new List<string> { "Stored Alias" },
                    Ratings = new Ratings { Votes = 1000, Value = 4.2m },
                    Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover { CoverType = MediaCoverTypes.Poster, Url = "https://s4.anilist.co/stored.jpg" } }
                }
            };
        }

        private void GivenStored(int? anilistId)
        {
            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.FindById(MangaId))
                  .Returns(Stored(anilistId));
        }

        // The store changes while the provider is resolving: the refresh's first read (its
        // snapshot) sees one id, the write-time re-read sees what Fix Match or the rebind pass
        // wrote meanwhile.
        private void GivenStoredChangesDuringResolve(int? atStart, int? atWrite)
        {
            var reads = 0;

            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.FindById(MangaId))
                  .Returns(() => Stored(++reads == 1 ? atStart : atWrite));
        }

        private void GivenResolved(int? anilistId, string via, bool posterFellBackOnMiss = false)
        {
            GivenResolved(anilistId, via, "Rudeus starts over.", "https://s4.anilist.co/bx85564.jpg", posterFellBackOnMiss, displayFetchFailed: false);
        }

        private void GivenResolved(int? anilistId, string via, string overview, string coverUrl, bool posterFellBackOnMiss, bool displayFetchFailed)
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns(new MangaSeriesMetadata
                  {
                      DisplayName = "Mushoku Tensei: Jobless Reincarnation",
                      VolumeCount = 1,
                      OriginTotal = 24,
                      Overview = overview,
                      CoverUrl = coverUrl,
                      PosterSource = coverUrl == null ? "none" : "anilist",
                      PosterFellBackOnMiss = posterFellBackOnMiss,
                      DisplayFetchFailed = displayFetchFailed,
                      AltTitles = new List<string> { "Mushoku Tensei: Isekai Ittara Honki Dasu" },
                      AniListId = anilistId,
                      MatchedVia = via,
                      Volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 1 } }
                  });
        }

        [Test]
        public void a_refresh_passes_the_stored_id_and_the_de_slugged_name_to_the_provider()
        {
            GivenStored(85564);
            GivenResolved(85564, "id");

            Subject.GetAuthorInfo(MangaId);

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries("Mushoku Tensei", It.IsAny<int>(), It.IsAny<bool>(), LibraryType.Manga, 85564, "Mushoku Tensei Jobless Reincarnation"), Times.Once());
        }

        [Test]
        public void an_add_passes_no_id_and_the_de_slugged_name_twice()
        {
            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.FindById(It.IsAny<string>()))
                  .Returns((Author)null);
            GivenResolved(85564, "primary");

            Subject.GetAuthorInfo(MangaId);

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries("Mushoku Tensei Jobless Reincarnation", It.IsAny<int>(), It.IsAny<bool>(), LibraryType.Manga, null, "Mushoku Tensei Jobless Reincarnation"), Times.Once());
        }

        [Test]
        public void the_resolved_id_is_stored_on_the_metadata()
        {
            GivenStored(null);
            GivenResolved(85564, "alias");

            var author = Subject.GetAuthorInfo(MangaId);

            author.Metadata.Value.AniListId.Should().Be(85564);
        }

        [Test]
        public void a_resolve_that_found_nothing_carries_the_stored_id_forward()
        {
            GivenStored(85564);
            GivenResolved(null, null);

            var author = Subject.GetAuthorInfo(MangaId);

            author.Metadata.Value.AniListId.Should().Be(85564);
        }

        [Test]
        public void a_relaxed_hit_leaves_an_unbound_entry_unbound()
        {
            GivenStored(null);
            GivenResolved(null, "relaxed");

            var author = Subject.GetAuthorInfo(MangaId);

            author.Metadata.Value.AniListId.Should().BeNull();
        }

        // ---- the id is re-read at write time: a bind or unbind made mid-resolve wins ----

        [Test]
        public void a_bind_made_while_the_refresh_was_resolving_wins_over_its_snapshot()
        {
            GivenStoredChangesDuringResolve(atStart: 85564, atWrite: 30598);
            GivenResolved(85564, "id");

            var author = Subject.GetAuthorInfo(MangaId);

            author.Metadata.Value.AniListId.Should().Be(30598);
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries("Mushoku Tensei", It.IsAny<int>(), It.IsAny<bool>(), LibraryType.Manga, 85564, "Mushoku Tensei Jobless Reincarnation"), Times.Once());
        }

        [Test]
        public void an_unbind_made_while_the_refresh_was_resolving_wins_over_its_snapshot()
        {
            GivenStoredChangesDuringResolve(atStart: 85564, atWrite: null);
            GivenResolved(85564, "id");

            var author = Subject.GetAuthorInfo(MangaId);

            author.Metadata.Value.AniListId.Should().BeNull();
        }

        [Test]
        public void an_unchanged_store_keeps_the_resolved_id_over_the_stored_one()
        {
            GivenStoredChangesDuringResolve(atStart: 85564, atWrite: 85564);
            GivenResolved(30598, "alias");

            var author = Subject.GetAuthorInfo(MangaId);

            author.Metadata.Value.AniListId.Should().Be(30598);
        }

        // ---- a bound entry whose by-id fetch failed keeps its stored presentation ----

        [Test]
        public void a_bound_entry_whose_id_fetch_failed_keeps_its_stored_presentation()
        {
            GivenStored(85564);
            GivenResolved(null, null);

            var meta = Subject.GetAuthorInfo(MangaId).Metadata.Value;

            meta.AniListId.Should().Be(85564);
            meta.Overview.Should().Be("Stored overview.");
            meta.Images.Should().ContainSingle(i => i.Url == "https://s4.anilist.co/stored.jpg");
            meta.Aliases.Should().Equal("Stored Alias");
            meta.Status.Should().Be(AuthorStatusType.Ended);
            meta.TotalVolumes.Should().Be(26);
            meta.Ratings.Value.Should().Be(4.2m);
        }

        [Test]
        public void an_unbound_entry_whose_resolve_found_nothing_takes_the_provider_result()
        {
            GivenStored(null);
            GivenResolved(null, null);

            var meta = Subject.GetAuthorInfo(MangaId).Metadata.Value;

            meta.AniListId.Should().BeNull();
            meta.Overview.Should().Be("Rudeus starts over.");
            meta.Images.Should().ContainSingle(i => i.Url == "https://s4.anilist.co/bx85564.jpg");
            meta.Aliases.Should().Equal("Mushoku Tensei: Isekai Ittara Honki Dasu");
            meta.TotalVolumes.Should().Be(24);
            meta.Ratings.Value.Should().Be(0m);
        }

        [Test]
        public void an_unbind_made_while_a_failed_refresh_was_resolving_drops_the_stored_presentation_too()
        {
            GivenStoredChangesDuringResolve(atStart: 85564, atWrite: null);
            GivenResolved(null, null);

            var meta = Subject.GetAuthorInfo(MangaId).Metadata.Value;

            meta.AniListId.Should().BeNull();
            meta.Overview.Should().Be("Rudeus starts over.");
            meta.Images.Should().ContainSingle(i => i.Url == "https://s4.anilist.co/bx85564.jpg");
        }

        // ---- finding #2 (2026-09-16 review): the poster ratchets on a Google volume-1 miss ----

        [Test]
        public void a_poster_that_fell_back_only_because_google_missed_keeps_the_stored_one()
        {
            GivenStored(85564);
            GivenResolved(85564, "id", posterFellBackOnMiss: true);

            var meta = Subject.GetAuthorInfo(MangaId).Metadata.Value;

            meta.Images.Should().ContainSingle(i => i.Url == "https://s4.anilist.co/stored.jpg");

            // only the images: the rest of the presentation is this pass's (AniList resolved fine)
            meta.Overview.Should().Be("Rudeus starts over.");
            meta.Aliases.Should().Equal("Mushoku Tensei: Isekai Ittara Honki Dasu");
        }

        [Test]
        public void a_poster_that_fell_back_on_a_miss_with_nothing_stored_takes_the_provider_result()
        {
            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.FindById(It.IsAny<string>()))
                  .Returns((Author)null);
            GivenResolved(85564, "id", posterFellBackOnMiss: true);

            var meta = Subject.GetAuthorInfo(MangaId).Metadata.Value;

            meta.Images.Should().ContainSingle(i => i.Url == "https://s4.anilist.co/bx85564.jpg");
        }

        // ---- display fallback (2026-09-24): a display fetch that did not answer blanks nothing ----

        [Test]
        public void a_failed_display_fetch_keeps_the_stored_poster_and_overview()
        {
            GivenStored(null);
            GivenResolved(null, null, overview: string.Empty, coverUrl: null, posterFellBackOnMiss: false, displayFetchFailed: true);

            var meta = Subject.GetAuthorInfo(MangaId).Metadata.Value;

            meta.AniListId.Should().BeNull();
            meta.Images.Should().ContainSingle(i => i.Url == "https://s4.anilist.co/stored.jpg");
            meta.Overview.Should().Be("Stored overview.");
        }

        [Test]
        public void a_failed_display_fetch_does_not_override_what_this_pass_found()
        {
            // The fetch was needed for the overview only: this pass's own poster stands.
            GivenStored(null);
            GivenResolved(null, null, overview: string.Empty, coverUrl: "https://covers.openlibrary.org/b/id/1-L.jpg", posterFellBackOnMiss: false, displayFetchFailed: true);

            var meta = Subject.GetAuthorInfo(MangaId).Metadata.Value;

            meta.Images.Should().ContainSingle(i => i.Url == "https://covers.openlibrary.org/b/id/1-L.jpg");
            meta.Overview.Should().Be("Stored overview.");
        }

        [Test]
        public void without_a_failed_display_fetch_an_unbound_entry_with_no_poster_or_overview_takes_the_empty_result()
        {
            GivenStored(null);
            GivenResolved(null, null, overview: string.Empty, coverUrl: null, posterFellBackOnMiss: false, displayFetchFailed: false);

            var meta = Subject.GetAuthorInfo(MangaId).Metadata.Value;

            meta.Images.Should().BeEmpty();
            meta.Overview.Should().BeEmpty();
        }

        // ---- names never change on refresh (the stored name is curated) ----

        [Test]
        public void a_refresh_keeps_the_stored_name_when_the_anilist_title_differs_beyond_punctuation()
        {
            GivenStored(85564);
            GivenResolved(85564, "id");

            var author = Subject.GetAuthorInfo(MangaId);

            author.Name.Should().Be("Mushoku Tensei");
            author.Metadata.Value.SortName.Should().Be("mushoku tensei");
            author.CleanName.Should().Be("mushokutensei");
            author.Books.Value.First().Title.Should().Be("Mushoku Tensei Vol. 1");
        }

        [Test]
        public void a_refresh_still_takes_the_presentation_from_the_new_binding()
        {
            GivenStored(85564);
            GivenResolved(85564, "id");

            var meta = Subject.GetAuthorInfo(MangaId).Metadata.Value;

            meta.Overview.Should().Be("Rudeus starts over.");
            meta.Images.Should().ContainSingle(i => i.Url == "https://s4.anilist.co/bx85564.jpg");
            meta.Aliases.Should().Equal("Mushoku Tensei: Isekai Ittara Honki Dasu");
            meta.TotalVolumes.Should().Be(24);
        }

        [Test]
        public void an_add_names_a_new_entry_after_the_provider()
        {
            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.FindById(It.IsAny<string>()))
                  .Returns((Author)null);
            GivenResolved(85564, "primary");

            var author = Subject.GetAuthorInfo(MangaId);

            author.Name.Should().Be("Mushoku Tensei: Jobless Reincarnation");
            author.CleanName.Should().Be("mushokutenseijoblessreincarnation");
        }

        [Test]
        public void display_name_from_id_is_the_title_cased_slug()
        {
            BookInfoProxy.DisplayName("local-rezero-starting-life-in-another-world-chapter-4-the-sanctuary-and-the-witch-of-greed")
                .Should().Be("Rezero Starting Life In Another World Chapter 4 The Sanctuary And The Witch Of Greed");
            BookInfoProxy.DisplayName("local-sword-art-online~ln").Should().Be("Sword Art Online");
        }
    }
}
