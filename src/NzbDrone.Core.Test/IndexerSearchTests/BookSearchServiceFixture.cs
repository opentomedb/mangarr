using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    // The scheduled missing search groups wanted volumes by series: a series with several
    // missing volumes gets ONE series (author) search instead of one fan-out per volume; a
    // series with a single missing volume keeps the precise per-volume search.
    [TestFixture]
    public class BookSearchServiceFixture : CoreTest
    {
        // BookSearchService is internal, so resolve it here instead of via CoreTest<T>.
        private BookSearchService Subject => Mocker.Resolve<BookSearchService>();
        private List<Book> _books;

        [SetUp]
        public void Setup()
        {
            _books = new List<Book>
            {
                new Book { Id = 1, AuthorId = 1, Monitored = true, Author = new Author { Id = 1, Monitored = true } },
                new Book { Id = 2, AuthorId = 1, Monitored = true, Author = new Author { Id = 1, Monitored = true } },
                new Book { Id = 3, AuthorId = 1, Monitored = true, Author = new Author { Id = 1, Monitored = true } },
                new Book { Id = 4, AuthorId = 2, Monitored = true, Author = new Author { Id = 2, Monitored = true } }
            };

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.GetAllAuthors())
                .Returns(new List<Author>());

            Mocker.GetMock<IBookService>()
                .Setup(s => s.BooksWithoutFiles(It.IsAny<PagingSpec<Book>>(), It.IsAny<LibraryType?>(), It.IsAny<WantedMediaTypeFilter?>()))
                .Returns((PagingSpec<Book> spec, LibraryType? library, WantedMediaTypeFilter? mediaType) =>
                {
                    spec.Records = _books;
                    spec.TotalRecords = _books.Count;
                    return spec;
                });

            Mocker.GetMock<IQueueService>()
                .Setup(s => s.GetQueue())
                .Returns(new List<NzbDrone.Core.Queue.Queue>());

            Mocker.GetMock<ISearchForReleases>()
                .Setup(s => s.AuthorSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(Task.FromResult(new List<DownloadDecision>()));

            Mocker.GetMock<ISearchForReleases>()
                .Setup(s => s.BookSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(Task.FromResult(new List<DownloadDecision>()));

            Mocker.GetMock<IProcessDownloadDecisions>()
                .Setup(s => s.ProcessDecisions(It.IsAny<List<DownloadDecision>>()))
                .Returns(Task.FromResult(new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision>(), new List<DownloadDecision>())));
        }

        [Test]
        public void should_search_once_per_series_with_several_missing_volumes()
        {
            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(1, MediaType.Archive, true, true, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(It.IsIn(1, 2, 3), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_keep_per_volume_search_for_a_single_missing_volume()
        {
            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(4, MediaType.Archive, false, true, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(2, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_process_decisions_once_per_search()
        {
            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<IProcessDownloadDecisions>()
                .Verify(v => v.ProcessDecisions(It.IsAny<List<DownloadDecision>>()), Times.Exactly(2));
        }

        // Light novels (2026-09): the scheduled unit is (volume, media type); pending audio is
        // best effort (D5). The editions and files of a light-novel series come from one
        // IEditionService / IMediaFileService call per series, never from the per-row lazy loads.

        private Author _lightNovel;

        private List<Book> GivenLightNovelSeries(bool audioAvailable, DateTime? lastAudioSearch = null, bool epubOnDisk = false)
        {
            _lightNovel = new Author
            {
                Id = 3,
                Name = "Overlord",
                Monitored = true,
                AudioAvailable = audioAvailable,
                LastAudioSearch = lastAudioSearch,
                Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord~ln" }
            };

            var volumes = new List<Book>
            {
                new Book { Id = 31, AuthorId = 3, Monitored = true, Author = _lightNovel },
                new Book { Id = 32, AuthorId = 3, Monitored = true, Author = _lightNovel }
            };

            foreach (var volume in volumes)
            {
                var ebook = volume.WithEdition(MediaType.Ebook);
                var audio = volume.WithEdition(MediaType.Audio);
                audio.Asin = "B0" + volume.Id;                                  // released audio by default
                audio.AudioReleaseDate = DateTime.UtcNow.AddDays(-30);

                if (epubOnDisk)
                {
                    ebook.BookFiles = new List<BookFile> { new BookFile { Id = volume.Id, EditionId = ebook.Id, Quality = new QualityModel(Quality.EPUB) } };
                }
            }

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.GetAllAuthors())
                .Returns(new List<Author> { _lightNovel });

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.GetAuthor(3))
                .Returns(_lightNovel);

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBooksByAuthor(3))
                .Returns(volumes);

            // the per-series lookups the service reads instead of Book.Editions / Edition.BookFiles
            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionsByAuthor(3))
                .Returns(() => volumes.SelectMany(v => v.Editions.Value).ToList());

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByAuthor(3))
                .Returns(() => volumes.SelectMany(v => v.Editions.Value).SelectMany(e => e.BookFiles.Value).ToList());

            return volumes;
        }

        [Test]
        public void manga_series_are_scheduled_without_edition_or_file_lookups()
        {
            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(1, MediaType.Archive, true, false, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(4, MediaType.Archive, false, false, false, true), Times.Once());

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.GetEditionsByAuthor(It.IsAny<int>()), Times.Never());

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.GetFilesByAuthor(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_search_each_wanted_media_type_of_a_light_novel_series()
        {
            _books.AddRange(GivenLightNovelSeries(audioAvailable: true));

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Ebook, true, false, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, true, false, false, true), Times.Once());

            // the manga series are untouched: one Archive search each, as before
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(1, MediaType.Archive, true, false, false, true), Times.Once());

            // one editions query and one files query for the light-novel series, not one per volume
            Mocker.GetMock<IEditionService>()
                .Verify(v => v.GetEditionsByAuthor(3), Times.Once());

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.GetFilesByAuthor(3), Times.Once());
        }

        [Test]
        public void pending_audio_is_left_out_of_the_scheduled_run_and_stamped_by_the_weekly_probe()
        {
            // Every EPUB is on disk, so BooksWithoutFiles (Part A) returns none of these volumes;
            // the pending audio editions are reached only through the probe, which is due.
            GivenLightNovelSeries(audioAvailable: false, lastAudioSearch: null, epubOnDisk: true);

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, true, false, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Ebook, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateLastAudioSearch(It.Is<List<Author>>(a => a.Single().Id == 3 && a.Single().LastAudioSearch.HasValue)), Times.Once());
        }

        [Test]
        public void pending_audio_probed_this_week_is_not_probed_again()
        {
            GivenLightNovelSeries(audioAvailable: false, lastAudioSearch: DateTime.UtcNow.AddDays(-1), epubOnDisk: true);

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateLastAudioSearch(It.IsAny<List<Author>>()), Times.Never());
        }

        [Test]
        public void pending_audio_is_skipped_by_the_normal_list_even_when_the_volume_is_missing()
        {
            // The EPUB is missing too, so the volumes come back from BooksWithoutFiles: their EPUB
            // editions are searched, their pending audio editions are not (the probe ran yesterday).
            _books.AddRange(GivenLightNovelSeries(audioAvailable: false, lastAudioSearch: DateTime.UtcNow.AddDays(-1)));

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Ebook, true, false, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void manual_run_probes_pending_audio_regardless_of_the_stamp()
        {
            GivenLightNovelSeries(audioAvailable: false, lastAudioSearch: DateTime.UtcNow.AddHours(-1), epubOnDisk: true);

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, true, true, false, true), Times.Once());
        }

        [Test]
        public void probe_leaves_out_an_unreleased_volume()
        {
            // Volume 32 is not out yet: BooksWithoutFiles never lists it and the probe must not
            // search it either, so the probe's one wanted volume takes the per-volume search.
            var volumes = GivenLightNovelSeries(audioAvailable: false, lastAudioSearch: null, epubOnDisk: true);
            volumes[1].ReleaseDate = DateTime.UtcNow.AddDays(30);

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(31, MediaType.Audio, false, false, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(32, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void queued_media_type_is_skipped_but_the_other_is_searched()
        {
            var volumes = GivenLightNovelSeries(audioAvailable: true);
            _books.AddRange(volumes);

            // the EPUB of volume 31 is downloading; its audiobook and both editions of 32 are not
            Mocker.GetMock<IQueueService>()
                .Setup(s => s.GetQueue())
                .Returns(new List<NzbDrone.Core.Queue.Queue>
                {
                    new NzbDrone.Core.Queue.Queue
                    {
                        Book = volumes[0],
                        RemoteBook = new RemoteBook { MediaType = MediaType.Ebook }
                    }
                });

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            // one EPUB volume left -> per-volume search; two audio volumes -> series search
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(32, MediaType.Ebook, false, false, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, true, false, false, true), Times.Once());
        }

        // A light-novel series whose EPUBs meet their cutoff and whose audio editions hold MP3s
        // below the M4B cutoff: the cutoff-unmet run wants the Audio edition of both volumes.
        private List<Book> GivenCutoffUnmetAudio()
        {
            var volumes = GivenLightNovelSeries(audioAvailable: true, epubOnDisk: true);

            _lightNovel.QualityProfile = new QualityProfile
            {
                Cutoff = Quality.EPUB.Id,
                Items = new[] { Quality.Unknown, Quality.EPUB }.Select(q => new QualityProfileQualityItem { Quality = q, Allowed = true }).ToList()
            };

            _lightNovel.AudioQualityProfile = new QualityProfile
            {
                Cutoff = Quality.M4B.Id,
                Items = new[] { Quality.UnknownAudio, Quality.MP3, Quality.FLAC, Quality.M4B }.Select(q => new QualityProfileQualityItem { Quality = q, Allowed = true }).ToList()
            };

            foreach (var volume in volumes)
            {
                var audio = volume.EditionOf(MediaType.Audio);
                audio.BookFiles = new List<BookFile> { new BookFile { Id = 100 + volume.Id, EditionId = audio.Id, Quality = new QualityModel(Quality.MP3) } };
            }

            Mocker.GetMock<IBookCutoffService>()
                .Setup(s => s.BooksWhereCutoffUnmet(It.IsAny<PagingSpec<Book>>(), It.IsAny<LibraryType?>(), It.IsAny<WantedMediaTypeFilter?>()))
                .Returns((PagingSpec<Book> spec, LibraryType? library, WantedMediaTypeFilter? mediaType) =>
                {
                    spec.Records = volumes;
                    spec.TotalRecords = volumes.Count;
                    return spec;
                });

            return volumes;
        }

        [Test]
        public void cutoff_search_asks_only_for_the_edition_below_its_own_cutoff()
        {
            GivenCutoffUnmetAudio();

            Subject.Execute(new CutoffUnmetBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, true, false, false, false), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Ebook, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        // Final review I1 (2026-09-20): Book.LastSearchTime is one stamp per volume, but the
        // missing search wants the FILELESS edition and the cutoff search the below-cutoff one.
        // A scheduled cutoff run that stamped would burn the missing search's 24h back-off for an
        // edition it never searched -- the volume's audiobook would never be searched again.

        [Test]
        public void scheduled_cutoff_search_does_not_stamp_the_last_search_time()
        {
            GivenCutoffUnmetAudio();

            Subject.Execute(new CutoffUnmetBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), true), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), true), Times.Never());
        }

        [Test]
        public void manual_cutoff_search_stamps_as_every_other_search_does()
        {
            GivenCutoffUnmetAudio();

            Subject.Execute(new CutoffUnmetBookSearchCommand { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, true, true, false, true), Times.Once());
        }

        [Test]
        public void the_missing_search_stamps_as_before()
        {
            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(1, MediaType.Archive, true, false, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(4, MediaType.Archive, false, false, false, true), Times.Once());
        }

        // Final review I1: BookSearchCommand.MediaType routes to the typed overload; null (every
        // caller before, every manga search) is the untyped, every-monitored-class search as before.

        [Test]
        public void typed_book_search_command_searches_that_class_only()
        {
            Subject.Execute(new BookSearchCommand(new List<int> { 4 }, MediaType.Audio) { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(4, MediaType.Audio, false, false, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void untyped_book_search_command_is_the_every_class_search_as_before()
        {
            Mocker.GetMock<ISearchForReleases>()
                .Setup(s => s.BookSearch(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(Task.FromResult(new List<DownloadDecision>()));

            Subject.Execute(new BookSearchCommand(new List<int> { 4 }) { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(4, false, true, false), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        // Copy-in hold (2026-09-16, D4b): nothing of a held entry is scheduled; the other series
        // are searched as before.

        [Test]
        public void pending_author_volumes_are_not_scheduled()
        {
            _books[3].Author.Value.CopyInPending = true;

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(4, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(2, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(1, MediaType.Archive, true, true, false, true), Times.Once());
        }

        [Test]
        public void pending_author_is_not_probed_for_audio_and_not_stamped()
        {
            // As pending_audio_is_left_out_of_the_scheduled_run_and_stamped_by_the_weekly_probe, but
            // the entry is held: the probe is due and must neither search nor burn the weekly stamp.
            GivenLightNovelSeries(audioAvailable: false, lastAudioSearch: null, epubOnDisk: true);
            _lightNovel.CopyInPending = true;

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(It.IsIn(31, 32), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateLastAudioSearch(It.IsAny<List<Author>>()), Times.Never());
        }

        // Covered volumes (2026-09-17, D4): a covered Audio edition is not wanted by the missing
        // or the cutoff search; the volume's EPUB edition and the other volumes' audio still are.

        [Test]
        public void covered_audio_edition_is_not_wanted_by_the_missing_search()
        {
            var volumes = GivenLightNovelSeries(audioAvailable: true);
            _books.AddRange(volumes);

            // volume 32's audiobook is inside volume 31's file; no file of its own
            volumes[1].EditionOf(MediaType.Audio).CoveredByVolume = 1;

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            // both EPUBs wanted -> series search; one audio volume left -> per-volume search
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Ebook, true, false, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(31, MediaType.Audio, false, false, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(32, MediaType.Audio, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void covered_audio_edition_is_not_wanted_by_the_cutoff_search()
        {
            // As cutoff_search_asks_only_for_the_edition_below_its_own_cutoff: both audio editions
            // hold MP3s below the M4B cutoff, but volume 32's audio is covered.
            var volumes = GivenCutoffUnmetAudio();

            volumes[1].EditionOf(MediaType.Audio).CoveredByVolume = 1;

            Subject.Execute(new CutoffUnmetBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(31, MediaType.Audio, false, false, false, false), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(32, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        // Wanted edition filter (2026-09-21): the Wanted pages launch Search All with the media
        // type of the selected filter preset, so an EPUB-filtered page searches EPUBs only.

        [Test]
        public void missing_search_scoped_to_a_media_type_searches_that_edition_only()
        {
            _books.AddRange(GivenLightNovelSeries(audioAvailable: true));

            Subject.Execute(new MissingBookSearchCommand { MediaType = MediaType.Ebook, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Ebook, true, true, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            // the scope is the EDITION, not the library: the manga series are wanted Archive
            // editions and an EPUB-scoped run leaves them alone too
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(1, MediaType.Archive, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(4, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void missing_search_scoped_to_ebooks_does_not_burn_the_audio_probe_stamp()
        {
            // The weekly probe is itself an audio search AND it stamps Author.LastAudioSearch, so
            // an EPUB-scoped run that let it execute would burn a week of probes for audiobooks
            // it never searched.
            GivenLightNovelSeries(audioAvailable: false, lastAudioSearch: null, epubOnDisk: true);

            Subject.Execute(new MissingBookSearchCommand { MediaType = MediaType.Ebook, Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateLastAudioSearch(It.IsAny<List<Author>>()), Times.Never());
        }

        [Test]
        public void missing_search_scoped_to_audio_still_probes_pending_audio()
        {
            GivenLightNovelSeries(audioAvailable: false, lastAudioSearch: null, epubOnDisk: true);

            Subject.Execute(new MissingBookSearchCommand { MediaType = MediaType.Audio, Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, true, false, false, true), Times.Once());

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateLastAudioSearch(It.Is<List<Author>>(a => a.Single().Id == 3)), Times.Once());
        }

        [Test]
        public void cutoff_search_scoped_to_ebooks_leaves_the_audio_edition_alone()
        {
            // Both volumes hold MP3s below the M4B cutoff and both EPUBs meet theirs, so an
            // EPUB-scoped cutoff run has nothing to search.
            GivenCutoffUnmetAudio();

            Subject.Execute(new CutoffUnmetBookSearchCommand { MediaType = MediaType.Ebook, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void cutoff_search_scoped_to_audio_searches_it_as_before()
        {
            GivenCutoffUnmetAudio();

            Subject.Execute(new CutoffUnmetBookSearchCommand { MediaType = MediaType.Audio, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, true, true, false, true), Times.Once());
        }

        [Test]
        public void probe_leaves_out_a_covered_audio_edition()
        {
            // As probe_leaves_out_an_unreleased_volume: the probe is due and every EPUB is on disk;
            // volume 32's audio is covered, so the probe's one wanted volume is 31.
            var volumes = GivenLightNovelSeries(audioAvailable: false, lastAudioSearch: null, epubOnDisk: true);
            volumes[1].EditionOf(MediaType.Audio).CoveredByVolume = 1;

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(31, MediaType.Audio, false, false, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(32, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateLastAudioSearch(It.Is<List<Author>>(a => a.Single().Id == 3)), Times.Once());
        }

        // Search All scope (2026-09-21): the Wanted pages launch Search All with the library of
        // the selected filter preset too, so a manga page never sweeps the light novels and a
        // light-novel page never sweeps the manga.

        [Test]
        public void missing_search_scoped_to_light_novels_leaves_the_manga_alone()
        {
            _books.AddRange(GivenLightNovelSeries(audioAvailable: true));

            Subject.Execute(new MissingBookSearchCommand { Library = LibraryType.LightNovel, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Ebook, true, true, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, true, true, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(1, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(4, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void missing_search_scoped_to_manga_leaves_the_light_novel_alone()
        {
            _books.AddRange(GivenLightNovelSeries(audioAvailable: true));

            Subject.Execute(new MissingBookSearchCommand { Library = LibraryType.Manga, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(1, MediaType.Archive, true, true, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(4, MediaType.Archive, false, true, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(It.IsIn(31, 32), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void missing_search_scoped_to_manga_does_not_burn_the_audio_probe_stamp()
        {
            // As missing_search_scoped_to_ebooks_does_not_burn_the_audio_probe_stamp: the probe is
            // a light-novel audio search that stamps Author.LastAudioSearch, so a manga-scoped run
            // must not make it -- filtering its results away would burn a week of probes.
            GivenLightNovelSeries(audioAvailable: false, lastAudioSearch: null, epubOnDisk: true);

            Subject.Execute(new MissingBookSearchCommand { Library = LibraryType.Manga, Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateLastAudioSearch(It.IsAny<List<Author>>()), Times.Never());
        }

        [Test]
        public void missing_search_skips_audio_audible_lists_for_a_future_date()
        {
            // SAO 23 (2026-09-21): a pre-order is nothing to find. Volume 32's audiobook is dated
            // later; only volume 31's is searched, so the run is a single volume search, not a series one.
            var volumes = GivenLightNovelSeries(audioAvailable: true, epubOnDisk: true);
            _books.AddRange(volumes);
            volumes[1].Editions.Value.Single(e => e.MediaType == MediaType.Audio).AudioReleaseDate = DateTime.UtcNow.AddDays(40);

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(31, MediaType.Audio, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Once());
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(32, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void missing_search_skips_audio_audible_does_not_list_yet()
        {
            var volumes = GivenLightNovelSeries(audioAvailable: true, epubOnDisk: true);
            _books.AddRange(volumes);
            var unlisted = volumes[1].Editions.Value.Single(e => e.MediaType == MediaType.Audio);
            unlisted.Asin = null;
            unlisted.AudioReleaseDate = null;

            Subject.Execute(new MissingBookSearchCommand { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(32, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void missing_search_scoped_to_a_library_and_an_edition_searches_that_one_pair()
        {
            _books.AddRange(GivenLightNovelSeries(audioAvailable: true));

            Subject.Execute(new MissingBookSearchCommand { Library = LibraryType.LightNovel, MediaType = MediaType.Audio, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, true, true, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Ebook, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(1, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void cutoff_search_scoped_to_manga_leaves_the_light_novel_alone()
        {
            // Every volume below its cutoff here is a light novel's, so a manga-scoped run has
            // nothing to search.
            GivenCutoffUnmetAudio();

            Subject.Execute(new CutoffUnmetBookSearchCommand { Library = LibraryType.Manga, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void cutoff_search_scoped_to_light_novels_searches_it_as_before()
        {
            GivenCutoffUnmetAudio();

            Subject.Execute(new CutoffUnmetBookSearchCommand { Library = LibraryType.LightNovel, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, true, true, false, true), Times.Once());
        }

        // The Both preset (2026-09-21): that page lists a volume only when its EPUB AND its
        // audiobook are both wanted, so Search All must search those volumes and nothing else --
        // the library alone swept every wanted light-novel edition. Both implies light novels, so
        // the manga series drop out of the group without a library test.

        [Test]
        public void missing_search_both_editions_only_searches_volumes_wanting_both()
        {
            var volumes = GivenLightNovelSeries(audioAvailable: true);
            _books.AddRange(volumes);

            // volume 31 wants its EPUB and its audiobook; volume 32's EPUB is already on disk, so
            // it wants audio only and the Both page does not list it
            var ebook = volumes[1].EditionOf(MediaType.Ebook);
            ebook.BookFiles = new List<BookFile> { new BookFile { Id = 132, EditionId = ebook.Id, Quality = new QualityModel(Quality.EPUB) } };

            Subject.Execute(new MissingBookSearchCommand { BothEditions = true, Trigger = CommandTrigger.Manual });

            // one volume per media type -> the precise per-volume search
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(31, MediaType.Ebook, false, true, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(31, MediaType.Audio, false, true, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(32, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            // the manga series have one Archive edition and never an Audio one
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(4, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void cutoff_search_both_editions_only_searches_volumes_wanting_both()
        {
            var volumes = GivenCutoffUnmetAudio();

            // both audiobooks hold MP3s below the M4B cutoff; volume 31's EPUB is below its cutoff
            // too, so it is the only volume the Both page lists
            volumes[0].EditionOf(MediaType.Ebook).BookFiles.Value.Single().Quality = new QualityModel(Quality.Unknown);

            Subject.Execute(new CutoffUnmetBookSearchCommand { BothEditions = true, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(31, MediaType.Ebook, false, true, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(31, MediaType.Audio, false, true, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(32, It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }
    
        // 2026-09-22: a book search command naming several volumes of ONE light novel runs one
        // series search per media type, not one volume search each.
        private void GivenBooksById(List<Book> books)
        {
            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBooks(It.IsAny<IEnumerable<int>>()))
                .Returns<IEnumerable<int>>(ids => books.Where(b => ids.Contains(b.Id)).ToList());
            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBook(It.IsAny<int>()))
                .Returns<int>(id => books.First(b => b.Id == id));
        }

        [Test]
        public void a_typed_multi_volume_light_novel_command_is_one_series_search()
        {
            var volumes = GivenLightNovelSeries(audioAvailable: true);
            GivenBooksById(volumes);

            Subject.Execute(new BookSearchCommand(new List<int> { 31, 32 }, MediaType.Ebook) { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Ebook, false, true, false, true), Times.Once());
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void an_untyped_multi_volume_light_novel_command_is_one_series_search_per_class()
        {
            var volumes = GivenLightNovelSeries(audioAvailable: true);
            GivenBooksById(volumes);

            Subject.Execute(new BookSearchCommand(new List<int> { 31, 32 }) { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Ebook, false, true, false, true), Times.Once());
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(3, MediaType.Audio, false, true, false, true), Times.Once());
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void a_single_light_novel_volume_keeps_the_volume_search()
        {
            var volumes = GivenLightNovelSeries(audioAvailable: true);
            GivenBooksById(volumes);

            Subject.Execute(new BookSearchCommand(new List<int> { 31 }, MediaType.Ebook) { Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.BookSearch(31, MediaType.Ebook, false, true, false, true), Times.Once());
            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }
    }
}
