using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.BookRepositoryTests
{
    [TestFixture]
    public class BookRepositoryWantedFixture : DbTest<BookRepository, Book>
    {
        private const int EpubProfile = 2;
        private const int AudioProfile = 3;
        private const int MangaProfile = 4;

        private Author _author;
        private Book _volume;
        private Edition _ebook;
        private Edition _audio;
        private Book _mangaVolume;
        private Book _lnEbookOnly;
        private Book _lnAudioOnly;

        [SetUp]
        public void Setup()
        {
            var meta = Builder<AuthorMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.ForeignAuthorId = "local-x~ln")
                .With(a => a.TitleSlug = "local-x~ln")
                .With(a => a.SortName = "zeta")
                .Build();
            Db.Insert(meta);

            _author = Builder<Author>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.AuthorMetadataId = meta.Id)
                .With(a => a.Monitored = true)
                .With(a => a.QualityProfileId = EpubProfile)
                .With(a => a.AudioQualityProfileId = AudioProfile)
                .With(a => a.AudioAvailable = false)
                .Build();
            Db.Insert(_author);

            _volume = Builder<Book>.CreateNew()
                .With(b => b.Id = 0)
                .With(b => b.AuthorMetadataId = meta.Id)
                .With(b => b.Monitored = true)
                .With(b => b.ReleaseDate = DateTime.UtcNow.AddDays(-30))
                .Build();
            Db.Insert(_volume);

            _ebook = InsertEdition(_volume, "local-x~ln-v1-ed", MediaType.Ebook);
            _audio = InsertEdition(_volume, "local-x~ln-v1-audio-ed", MediaType.Audio);
        }

        private Edition InsertEdition(Book book, string foreignId, MediaType mediaType)
        {
            var edition = Builder<Edition>.CreateNew()
                .With(e => e.Id = 0)
                .With(e => e.BookId = book.Id)
                .With(e => e.ForeignEditionId = foreignId)
                .With(e => e.TitleSlug = foreignId)
                .With(e => e.MediaType = mediaType)
                .With(e => e.Monitored = true)
                .With(e => e.CoveredByVolume = null)   // NBuilder fills the nullable; an unmarked row is NULL
                .With(e => e.CoveredSource = null)
                .With(e => e.Asin = mediaType == MediaType.Audio ? foreignId + "-asin" : null)                 // released audio by default
                .With(e => e.AudioReleaseDate = mediaType == MediaType.Audio ? DateTime.UtcNow.AddDays(-30) : null)
                .Build();
            Db.Insert(edition);
            return edition;
        }

        [Test]
        public void an_audio_edition_audible_lists_for_a_future_date_is_not_wanted()
        {
            InsertFile(_ebook, Quality.EPUB);
            GivenAudioAvailable();
            _audio.AudioReleaseDate = DateTime.UtcNow.AddDays(40);
            Db.Update(_audio);

            var result = Subject.BooksWithoutFiles(Page());

            result.TotalRecords.Should().Be(0);
        }

        [Test]
        public void an_audio_edition_audible_does_not_list_is_not_wanted()
        {
            InsertFile(_ebook, Quality.EPUB);
            GivenAudioAvailable();
            _audio.Asin = null;
            _audio.AudioReleaseDate = null;
            Db.Update(_audio);

            var result = Subject.BooksWithoutFiles(Page());

            result.TotalRecords.Should().Be(0);
        }

        [Test]
        public void a_pending_series_audio_edition_without_an_asin_is_still_pending_not_unreleased()
        {
            // AudioAvailable off: the pending rule owns it (not listed, probed weekly), unchanged.
            InsertFile(_ebook, Quality.EPUB);
            _audio.Asin = null;
            _audio.AudioReleaseDate = null;
            Db.Update(_audio);

            Subject.BooksWithoutFiles(Page()).TotalRecords.Should().Be(0);
        }

        private void InsertFile(Edition edition, Quality quality)
        {
            Db.Insert(new BookFile
            {
                EditionId = edition.Id,
                Path = $"/lightnovels/X/X - Vol. 1/{edition.ForeignEditionId}.{quality.Name.ToLowerInvariant()}",
                Quality = new QualityModel(quality),
                Size = 100,
                DateAdded = DateTime.UtcNow
            });
        }

        private void GivenAudioAvailable()
        {
            _author.AudioAvailable = true;
            Db.Update(_author);
        }

        private void GivenCovered(Edition edition, string source)
        {
            edition.CoveredByVolume = 1;
            edition.CoveredSource = source;
            Db.Update(edition);
        }

        private static PagingSpec<Book> Page()
        {
            return new PagingSpec<Book> { Page = 1, PageSize = 10, SortDirection = SortDirection.Ascending };
        }

        [Test]
        public void a_volume_missing_its_epub_is_wanted_once()
        {
            var result = Subject.BooksWithoutFiles(Page());

            result.Records.Select(b => b.Id).Should().Equal(_volume.Id);
            result.TotalRecords.Should().Be(1);
        }

        [Test]
        public void a_pending_audio_edition_does_not_make_a_volume_wanted()
        {
            InsertFile(_ebook, Quality.EPUB);

            var result = Subject.BooksWithoutFiles(Page());

            result.Records.Should().BeEmpty();
            result.TotalRecords.Should().Be(0);
        }

        [Test]
        public void an_audio_edition_of_an_available_series_is_wanted()
        {
            InsertFile(_ebook, Quality.EPUB);
            GivenAudioAvailable();

            var result = Subject.BooksWithoutFiles(Page());

            result.Records.Select(b => b.Id).Should().Equal(_volume.Id);
            result.TotalRecords.Should().Be(1);
        }

        [Test]
        public void an_unmonitored_audio_edition_is_not_wanted()
        {
            InsertFile(_ebook, Quality.EPUB);
            GivenAudioAvailable();
            _audio.Monitored = false;
            Db.Update(_audio);

            Subject.BooksWithoutFiles(Page()).Records.Should().BeEmpty();
        }

        // Covered volumes (2026-09-17, D8): an Audio edition inside another volume's file is satisfied,
        // not missing -- the Wanted pages agree with the statistics and the details page. The volume's
        // Ebook edition is judged on its own.
        [Test]
        public void a_covered_audio_edition_is_not_wanted()
        {
            InsertFile(_ebook, Quality.EPUB);
            GivenAudioAvailable();
            GivenCovered(_audio, CoveredSources.Import);

            var result = Subject.BooksWithoutFiles(Page());

            result.Records.Should().BeEmpty();
            result.TotalRecords.Should().Be(0);
        }

        [Test]
        public void a_covered_volume_missing_its_epub_is_still_wanted()
        {
            GivenAudioAvailable();
            GivenCovered(_audio, CoveredSources.Import);

            var result = Subject.BooksWithoutFiles(Page());

            result.Records.Select(b => b.Id).Should().Equal(_volume.Id);
            result.TotalRecords.Should().Be(1);
        }

        // A refresh takes an "audible" mark whatever is on disk (Edition.UseMetadataFrom), so a
        // covered Audio edition can hold a below-cutoff single of its own; covered = satisfied there too.
        [Test]
        public void a_covered_audio_edition_is_not_cutoff_unmet()
        {
            InsertFile(_audio, Quality.MP3);
            GivenCovered(_audio, CoveredSources.Audible);

            var audioBelowCutoff = new List<QualitiesBelowCutoff> { new QualitiesBelowCutoff(AudioProfile, new[] { Quality.MP3.Id }) };
            var result = Subject.BooksWhereCutoffUnmet(Page(), audioBelowCutoff);

            result.Records.Should().BeEmpty();
            result.TotalRecords.Should().Be(0);
        }

        [Test]
        public void a_manga_volume_is_wanted_exactly_as_before()
        {
            var manga = Builder<Book>.CreateNew()
                .With(b => b.Id = 0)
                .With(b => b.AuthorMetadataId = _volume.AuthorMetadataId)
                .With(b => b.ForeignBookId = "local-m-v1")
                .With(b => b.TitleSlug = "local-m-v1")
                .With(b => b.Monitored = true)
                .With(b => b.ReleaseDate = DateTime.UtcNow.AddDays(-30))
                .Build();
            Db.Insert(manga);
            InsertEdition(manga, "local-m-v1-ed", MediaType.Archive);
            InsertFile(_ebook, Quality.EPUB);

            var result = Subject.BooksWithoutFiles(Page());

            result.Records.Select(b => b.Id).Should().Equal(manga.Id);
            result.TotalRecords.Should().Be(1);
        }

        [Test]
        public void cutoff_unmet_judges_an_audio_file_against_the_audio_profile()
        {
            InsertFile(_ebook, Quality.EPUB);
            InsertFile(_audio, Quality.MP3);

            var audioBelowCutoff = new List<QualitiesBelowCutoff> { new QualitiesBelowCutoff(AudioProfile, new[] { Quality.UnknownAudio.Id, Quality.MP3.Id, Quality.FLAC.Id }) };
            var result = Subject.BooksWhereCutoffUnmet(Page(), audioBelowCutoff);

            result.Records.Select(b => b.Id).Should().Equal(_volume.Id);
            result.TotalRecords.Should().Be(1);
        }

        [Test]
        public void cutoff_unmet_never_judges_an_audio_file_by_the_epub_profile()
        {
            InsertFile(_audio, Quality.MP3);

            // the EPUB profile lists MP3 below its cutoff, but MP3 files belong to the audio side
            var epubBelowCutoff = new List<QualitiesBelowCutoff> { new QualitiesBelowCutoff(EpubProfile, new[] { Quality.MP3.Id }) };

            Subject.BooksWhereCutoffUnmet(Page(), epubBelowCutoff).Records.Should().BeEmpty();
        }

        [Test]
        public void cutoff_unmet_falls_back_to_the_main_profile_when_no_audio_profile_is_set()
        {
            _author.AudioQualityProfileId = null;
            Db.Update(_author);
            InsertFile(_audio, Quality.MP3);

            var mainBelowCutoff = new List<QualitiesBelowCutoff> { new QualitiesBelowCutoff(EpubProfile, new[] { Quality.MP3.Id }) };

            Subject.BooksWhereCutoffUnmet(Page(), mainBelowCutoff).Records.Select(b => b.Id).Should().Equal(_volume.Id);
        }

        [Test]
        public void cutoff_unmet_falls_back_to_the_main_profile_when_the_audio_profile_is_zero()
        {
            // 0 is "unset" everywhere else (Author.QualityProfileIdFor, the HasOne mapping, the validator)
            _author.AudioQualityProfileId = 0;
            Db.Update(_author);
            InsertFile(_audio, Quality.MP3);

            var mainBelowCutoff = new List<QualitiesBelowCutoff> { new QualitiesBelowCutoff(EpubProfile, new[] { Quality.MP3.Id }) };

            Subject.BooksWhereCutoffUnmet(Page(), mainBelowCutoff).Records.Select(b => b.Id).Should().Equal(_volume.Id);
        }

        [Test]
        public void a_volume_below_cutoff_on_both_sides_is_listed_once()
        {
            InsertFile(_ebook, Quality.Unknown);
            InsertFile(_audio, Quality.MP3);

            var below = new List<QualitiesBelowCutoff>
            {
                new QualitiesBelowCutoff(EpubProfile, new[] { Quality.Unknown.Id }),
                new QualitiesBelowCutoff(AudioProfile, new[] { Quality.MP3.Id })
            };
            var result = Subject.BooksWhereCutoffUnmet(Page(), below);

            result.Records.Should().HaveCount(1);
            result.TotalRecords.Should().Be(1);
        }

        [Test]
        public void sorting_by_author_name_keeps_one_row_per_volume()
        {
            // The Wanted pages sort by "authorMetadata.sortName" (frontend/src/Store/Actions/wantedActions.js:31),
            // so the paged ORDER BY names a column outside "Books".* -- the grouped query must accept it.
            var otherMeta = Builder<AuthorMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.ForeignAuthorId = "local-y~ln")
                .With(a => a.TitleSlug = "local-y~ln")
                .With(a => a.SortName = "alpha")
                .Build();
            Db.Insert(otherMeta);

            var otherAuthor = Builder<Author>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.AuthorMetadataId = otherMeta.Id)
                .With(a => a.CleanName = "y~ln")
                .With(a => a.Monitored = true)
                .With(a => a.QualityProfileId = EpubProfile)
                .With(a => a.AudioQualityProfileId = AudioProfile)
                .With(a => a.AudioAvailable = true)
                .Build();
            Db.Insert(otherAuthor);

            var otherVolume = Builder<Book>.CreateNew()
                .With(b => b.Id = 0)
                .With(b => b.AuthorMetadataId = otherMeta.Id)
                .With(b => b.ForeignBookId = "local-y~ln-v1")
                .With(b => b.TitleSlug = "local-y~ln-v1")
                .With(b => b.Monitored = true)
                .With(b => b.ReleaseDate = DateTime.UtcNow.AddDays(-30))
                .Build();
            Db.Insert(otherVolume);
            InsertEdition(otherVolume, "local-y~ln-v1-ed", MediaType.Ebook);
            InsertEdition(otherVolume, "local-y~ln-v1-audio-ed", MediaType.Audio);
            GivenAudioAvailable();

            var page = Page();
            page.SortKey = "authorMetadata.sortName";
            var result = Subject.BooksWithoutFiles(page);

            result.Records.Select(b => b.Id).Should().Equal(otherVolume.Id, _volume.Id);
            result.TotalRecords.Should().Be(2);
        }

        // Wanted filter presets (2026-09-21). A series of its own per shape: the library a volume
        // belongs to is its series', and AuthorMetadata.ForeignAuthorId is where the "~ln" suffix
        // lives -- Authors.ForeignAuthorId is a proxy onto it, not a column.
        private Book InsertSeriesWithVolume(string foreignAuthorId, string sortName, int qualityProfileId, bool audioAvailable)
        {
            var meta = Builder<AuthorMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.ForeignAuthorId = foreignAuthorId)
                .With(a => a.TitleSlug = foreignAuthorId)
                .With(a => a.SortName = sortName)
                .Build();
            Db.Insert(meta);

            var author = Builder<Author>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.AuthorMetadataId = meta.Id)
                .With(a => a.CleanName = foreignAuthorId)
                .With(a => a.Monitored = true)
                .With(a => a.QualityProfileId = qualityProfileId)
                .With(a => a.AudioQualityProfileId = AudioProfile)
                .With(a => a.AudioAvailable = audioAvailable)
                .Build();
            Db.Insert(author);

            var volume = Builder<Book>.CreateNew()
                .With(b => b.Id = 0)
                .With(b => b.AuthorMetadataId = meta.Id)
                .With(b => b.ForeignBookId = foreignAuthorId + "-v1")
                .With(b => b.TitleSlug = foreignAuthorId + "-v1")
                .With(b => b.Monitored = true)
                .With(b => b.ReleaseDate = DateTime.UtcNow.AddDays(-30))
                .Build();
            Db.Insert(volume);

            return volume;
        }

        // The four shapes a preset has to tell apart: a manga volume, a light novel wanting both
        // editions (_volume), one wanting only its EPUB, one wanting only its audiobook.
        private void GivenTheFourWantedShapes()
        {
            GivenAudioAvailable();

            _mangaVolume = InsertSeriesWithVolume("local-m", "manga", MangaProfile, false);
            InsertEdition(_mangaVolume, "local-m-v1-ed", MediaType.Archive);

            _lnEbookOnly = InsertSeriesWithVolume("local-e~ln", "epub only", EpubProfile, true);
            InsertEdition(_lnEbookOnly, "local-e~ln-v1-ed", MediaType.Ebook);
            InsertFile(InsertEdition(_lnEbookOnly, "local-e~ln-v1-audio-ed", MediaType.Audio), Quality.M4B);

            _lnAudioOnly = InsertSeriesWithVolume("local-a~ln", "audio only", EpubProfile, true);
            InsertFile(InsertEdition(_lnAudioOnly, "local-a~ln-v1-ed", MediaType.Ebook), Quality.EPUB);
            InsertEdition(_lnAudioOnly, "local-a~ln-v1-audio-ed", MediaType.Audio);
        }

        // The same four shapes with every edition on disk, each file below the cutoff of the
        // profile its own media type routes to -- or above it, for the edition that must not match.
        private void GivenTheFourCutoffShapes()
        {
            InsertFile(_ebook, Quality.Unknown);
            InsertFile(_audio, Quality.MP3);

            _mangaVolume = InsertSeriesWithVolume("local-m", "manga", MangaProfile, false);
            InsertFile(InsertEdition(_mangaVolume, "local-m-v1-ed", MediaType.Archive), Quality.Unknown);

            _lnEbookOnly = InsertSeriesWithVolume("local-e~ln", "epub only", EpubProfile, true);
            InsertFile(InsertEdition(_lnEbookOnly, "local-e~ln-v1-ed", MediaType.Ebook), Quality.Unknown);
            InsertFile(InsertEdition(_lnEbookOnly, "local-e~ln-v1-audio-ed", MediaType.Audio), Quality.FLAC);

            _lnAudioOnly = InsertSeriesWithVolume("local-a~ln", "audio only", EpubProfile, true);
            InsertFile(InsertEdition(_lnAudioOnly, "local-a~ln-v1-ed", MediaType.Ebook), Quality.EPUB);
            InsertFile(InsertEdition(_lnAudioOnly, "local-a~ln-v1-audio-ed", MediaType.Audio), Quality.MP3);
        }

        private static List<QualitiesBelowCutoff> BelowCutoffEverywhere()
        {
            return new List<QualitiesBelowCutoff>
            {
                new QualitiesBelowCutoff(EpubProfile, new[] { Quality.Unknown.Id }),
                new QualitiesBelowCutoff(AudioProfile, new[] { Quality.MP3.Id }),
                new QualitiesBelowCutoff(MangaProfile, new[] { Quality.Unknown.Id })
            };
        }

        [Test]
        public void the_manga_filter_lists_manga_volumes_only()
        {
            GivenTheFourWantedShapes();

            var result = Subject.BooksWithoutFiles(Page(), LibraryType.Manga, null);

            result.Records.Select(b => b.Id).Should().BeEquivalentTo(new[] { _mangaVolume.Id });
            result.TotalRecords.Should().Be(1);
        }

        [Test]
        public void the_light_novel_epub_filter_lists_volumes_wanting_an_epub()
        {
            GivenTheFourWantedShapes();

            var result = Subject.BooksWithoutFiles(Page(), LibraryType.LightNovel, WantedMediaTypeFilter.Ebook);

            result.Records.Select(b => b.Id).Should().BeEquivalentTo(new[] { _volume.Id, _lnEbookOnly.Id });
            result.TotalRecords.Should().Be(2);
        }

        [Test]
        public void the_light_novel_audio_filter_lists_volumes_wanting_an_audiobook()
        {
            GivenTheFourWantedShapes();

            var result = Subject.BooksWithoutFiles(Page(), LibraryType.LightNovel, WantedMediaTypeFilter.Audio);

            result.Records.Select(b => b.Id).Should().BeEquivalentTo(new[] { _volume.Id, _lnAudioOnly.Id });
            result.TotalRecords.Should().Be(2);
        }

        [Test]
        public void the_both_missing_filter_lists_only_volumes_wanting_each_edition()
        {
            GivenTheFourWantedShapes();

            var result = Subject.BooksWithoutFiles(Page(), LibraryType.LightNovel, WantedMediaTypeFilter.Both);

            result.Records.Select(b => b.Id).Should().BeEquivalentTo(new[] { _volume.Id });
            result.TotalRecords.Should().Be(1);
        }

        [Test]
        public void cutoff_unmet_takes_the_manga_filter()
        {
            GivenTheFourCutoffShapes();

            var result = Subject.BooksWhereCutoffUnmet(Page(), BelowCutoffEverywhere(), LibraryType.Manga, null);

            result.Records.Select(b => b.Id).Should().BeEquivalentTo(new[] { _mangaVolume.Id });
            result.TotalRecords.Should().Be(1);
        }

        [Test]
        public void cutoff_unmet_takes_the_light_novel_epub_filter()
        {
            GivenTheFourCutoffShapes();

            var result = Subject.BooksWhereCutoffUnmet(Page(), BelowCutoffEverywhere(), LibraryType.LightNovel, WantedMediaTypeFilter.Ebook);

            result.Records.Select(b => b.Id).Should().BeEquivalentTo(new[] { _volume.Id, _lnEbookOnly.Id });
            result.TotalRecords.Should().Be(2);
        }

        [Test]
        public void cutoff_unmet_takes_the_light_novel_audio_filter()
        {
            GivenTheFourCutoffShapes();

            var result = Subject.BooksWhereCutoffUnmet(Page(), BelowCutoffEverywhere(), LibraryType.LightNovel, WantedMediaTypeFilter.Audio);

            result.Records.Select(b => b.Id).Should().BeEquivalentTo(new[] { _volume.Id, _lnAudioOnly.Id });
            result.TotalRecords.Should().Be(2);
        }

        [Test]
        public void cutoff_unmet_takes_the_both_filter()
        {
            GivenTheFourCutoffShapes();

            var result = Subject.BooksWhereCutoffUnmet(Page(), BelowCutoffEverywhere(), LibraryType.LightNovel, WantedMediaTypeFilter.Both);

            result.Records.Select(b => b.Id).Should().BeEquivalentTo(new[] { _volume.Id });
            result.TotalRecords.Should().Be(1);
        }

        // Routed from the Task 2 review (2026-09-21): the combinations the presets never send on
        // their own, plus the count the Both filter has to get right on a second page.

        [Test]
        public void a_media_type_filter_without_a_library_still_never_lists_manga()
        {
            GivenTheFourWantedShapes();

            var result = Subject.BooksWithoutFiles(Page(), null, WantedMediaTypeFilter.Ebook);

            // a manga volume has one Archive edition, so an EPUB filter drops it whether or not
            // the library is named
            result.Records.Select(b => b.Id).Should().BeEquivalentTo(new[] { _volume.Id, _lnEbookOnly.Id });
            result.TotalRecords.Should().Be(2);
        }

        [Test]
        public void the_both_filter_on_manga_lists_nothing()
        {
            GivenTheFourWantedShapes();

            var result = Subject.BooksWithoutFiles(Page(), LibraryType.Manga, WantedMediaTypeFilter.Both);

            result.Records.Should().BeEmpty();
            result.TotalRecords.Should().Be(0);
        }

        [Test]
        public void the_both_filter_counts_every_match_when_the_page_holds_one()
        {
            GivenTheFourWantedShapes();

            // a second light novel wanting both editions, so the first page cannot hold them all
            var alsoBoth = InsertSeriesWithVolume("local-b~ln", "both missing", EpubProfile, true);
            InsertEdition(alsoBoth, "local-b~ln-v1-ed", MediaType.Ebook);
            InsertEdition(alsoBoth, "local-b~ln-v1-audio-ed", MediaType.Audio);

            var page = Page();
            page.PageSize = 1;

            var result = Subject.BooksWithoutFiles(page, LibraryType.LightNovel, WantedMediaTypeFilter.Both);

            // the HAVING reaches the count template as well as the paged select, so the total is
            // the matches and not every wanted volume
            result.Records.Should().HaveCount(1);
            result.TotalRecords.Should().Be(2);
        }
    }
}
