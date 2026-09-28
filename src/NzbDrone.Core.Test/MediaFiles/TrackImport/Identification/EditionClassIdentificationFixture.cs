using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    // Light novels (2026-09): a file is matched to the edition of its own class. Both editions
    // here carry the same title and no Format, so their distances are IDENTICAL -- without the
    // class gate the first candidate (Audio) wins for every file (GetBestRelease keeps the first
    // zero-distance candidate and breaks).
    [TestFixture]
    public class EditionClassIdentificationFixture : CoreTest<IdentificationService>
    {
        private Book _volume;
        private Edition _ebook;
        private Edition _audio;

        [SetUp]
        public void Setup()
        {
            var metadata = new AuthorMetadata { Id = 1, Name = "Overlord", ForeignAuthorId = "local-overlord~ln" };
            var author = new Author { Id = 1, Name = "Overlord", AuthorMetadataId = 1, Metadata = metadata };

            _volume = new Book
            {
                Id = 5,
                Title = "Overlord Vol. 5",
                AuthorMetadataId = 1,
                AuthorMetadata = metadata,
                Author = author,
                SeriesLinks = new List<SeriesBookLink>()
            };

            _audio = _volume.WithEdition(MediaType.Audio, id: 52);
            _audio.Title = "Overlord Vol. 5";
            _audio.Language = "eng";

            _ebook = _volume.WithEdition(MediaType.Ebook, id: 51);
            _ebook.Title = "Overlord Vol. 5";
            _ebook.Language = "eng";

            GivenCandidates(_audio, _ebook);

            Mocker.GetMock<ICandidateService>()
                .Setup(s => s.GetRemoteCandidates(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>()))
                .Returns(new List<CandidateEdition>());

            // Grouping (SingleRelease = false) needs the real grouper -- every other test here uses
            // SingleRelease = true and never exercises it.
            Mocker.SetConstant<ITrackGroupingService>(Mocker.Resolve<TrackGroupingService>());
        }

        private void GivenCandidates(params Edition[] editions)
        {
            Mocker.GetMock<ICandidateService>()
                .Setup(s => s.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()))
                .Returns(editions.Select(e => new CandidateEdition(e)).ToList());
        }

        private LocalEdition Identify(string path)
        {
            var track = new LocalBook
            {
                Path = path,
                FileTrackInfo = new ParsedTrackInfo { Authors = new List<string> { "Overlord" }, BookTitle = "Overlord Vol. 5" }
            };

            var config = new ImportDecisionMakerConfig { SingleRelease = true, KeepAllEditions = true, IncludeExisting = false };

            return Subject.Identify(new List<LocalBook> { track }, new IdentificationOverrides(), config).Single();
        }

        [Test]
        public void epub_is_matched_to_the_ebook_edition()
        {
            Identify("/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005.epub").Edition.Should().BeSameAs(_ebook);
        }

        [Test]
        public void audio_is_matched_to_the_audio_edition()
        {
            Identify("/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005.m4b").Edition.Should().BeSameAs(_audio);
        }

        [Test]
        public void archive_with_no_archive_edition_is_not_matched_at_all()
        {
            Identify("/downloads/Overlord/Overlord - Vol 005.cbz").Edition.Should().BeNull();
        }

        [Test]
        public void manga_volume_with_its_one_archive_edition_matches_as_before()
        {
            var manga = new Book { Id = 6, Title = "Dandadan Vol. 1", AuthorMetadataId = 1, AuthorMetadata = _volume.AuthorMetadata.Value, Author = _volume.Author.Value, SeriesLinks = new List<SeriesBookLink>() };
            var archive = manga.WithEdition(MediaType.Archive, id: 61);
            archive.Title = "Dandadan Vol. 1";
            archive.Language = "eng";
            GivenCandidates(archive);

            Identify("/downloads/Dandadan/Dandadan - Vol 001.cbz").Edition.Should().BeSameAs(archive);
        }

        // LN PDF (2026-09-22): a light novel's PDF matches the Ebook edition (read against the
        // candidate's library); a manga PDF still matches its one Archive edition.
        [Test]
        public void light_novel_pdf_is_matched_to_the_ebook_edition()
        {
            Identify("/downloads/Overlord/Overlord - Vol 005.pdf").Edition.Should().BeSameAs(_ebook);
        }

        [Test]
        public void manga_pdf_is_matched_to_its_archive_edition()
        {
            var metadata = new AuthorMetadata { Id = 2, Name = "Dandadan", ForeignAuthorId = "local-dandadan" };
            var author = new Author { Id = 2, Name = "Dandadan", AuthorMetadataId = 2, Metadata = metadata };
            var manga = new Book { Id = 6, Title = "Dandadan Vol. 1", AuthorMetadataId = 2, AuthorMetadata = metadata, Author = author, SeriesLinks = new List<SeriesBookLink>() };
            var archive = manga.WithEdition(MediaType.Archive, id: 61);
            archive.Title = "Dandadan Vol. 1";
            archive.Language = "eng";
            GivenCandidates(archive);

            Identify("/downloads/Dandadan/Dandadan - Vol 001.pdf").Edition.Should().BeSameAs(archive);
        }

        // LN PDF fix round 1 (2026-09-22): the author-scoped fallback (idOverrides.Author set, a
        // poor text match -- e.g. a renamed series) also reads the file's class against the SCOPED
        // author's library, so a .pdf still finds its Ebook edition by volume ownership. The
        // fallback resets Distance to zero only when it actually re-attaches by volume; a wrong
        // class here (the library read from the wrong source) means no candidate matches and
        // Distance stays at the original poor-match value -- previously untested.
        [Test]
        public void a_light_novel_pdf_with_a_poor_text_match_still_attaches_by_scoped_volume_ownership()
        {
            _volume.VolumeNumber = 5;

            var idOverrides = new IdentificationOverrides { Author = _volume.Author.Value };

            var track = new LocalBook
            {
                Path = "/downloads/Overlord/Overlord - Vol 005.pdf",
                FileTrackInfo = new ParsedTrackInfo { Authors = new List<string> { "Somebody Else Entirely" }, BookTitle = "A Completely Different Title" }
            };

            var config = new ImportDecisionMakerConfig { SingleRelease = true, KeepAllEditions = true, IncludeExisting = false };

            var result = Subject.Identify(new List<LocalBook> { track }, idOverrides, config).Single();

            result.Edition.Should().BeSameAs(_ebook);
            result.Distance.NormalizedDistance().Should().Be(0, "the scoped fallback re-attached by volume and reset the distance");
        }

        // Mixed-folder rescan (2026-09-24): the TRACKED grab (DownloadedBooksImportService's
        // "Folder grab" shortcut) was fixed in build 488 by DominantExtension, which forces the
        // whole override onto the byte-dominant class. The residual gap is here:
        // ManualImportService.UpdateItems groups selected rows by (Book?.Id, Edition?.Id) and
        // re-identifies each group with SingleRelease = true, trusting a group to be one edition's
        // files. If the UI bulk-assigns a Book to several rows without also assigning each row's
        // own Edition, an EPUB row and its M4B/MP3 rows share the same (BookId, null) key --
        // GetLocalBookReleases's SingleRelease branch skipped grouping entirely, so GetBestRelease
        // resolved the whole mixed group at once from the FIRST file's class, mis-attaching or
        // refusing the rest.
        private List<LocalBook> GivenMixedVolumeFiles(string folder)
        {
            var epub = new LocalBook
            {
                Path = folder + "Overlord - Vol 005.epub",
                FileTrackInfo = new ParsedTrackInfo { Authors = new List<string> { "Overlord" }, BookTitle = "Overlord Vol. 5" }
            };
            var m4b = new LocalBook
            {
                Path = folder + "Overlord - Vol 005.m4b",
                FileTrackInfo = new ParsedTrackInfo { Authors = new List<string> { "Overlord" }, BookTitle = "Overlord Vol. 5" }
            };
            var mp3s = Enumerable.Range(1, 3).Select(i => new LocalBook
            {
                Path = folder + $"Overlord - Vol 005 - Part {i:00}.mp3",
                FileTrackInfo = new ParsedTrackInfo { Authors = new List<string> { "Overlord" }, BookTitle = "Overlord Vol. 5" }
            });

            return new List<LocalBook> { epub, m4b }.Concat(mp3s).ToList();
        }

        [Test]
        public void manual_import_bulk_book_assign_splits_a_mixed_epub_and_audio_group_by_class()
        {
            var tracks = GivenMixedVolumeFiles("/lightnovels/Overlord/Overlord - Vol. 5/");

            // Edition left unset on every row, as a bulk "assign this Book" UI action would --
            // every row's group key collapses to (BookId, null).
            var idOverrides = new IdentificationOverrides { Book = _volume };
            var config = new ImportDecisionMakerConfig { SingleRelease = true, KeepAllEditions = true, IncludeExisting = false };

            var releases = Subject.Identify(tracks, idOverrides, config);

            releases.Should().HaveCount(2);
            releases.Should().ContainSingle(r => r.LocalBooks.Count == 1 && r.Edition == _ebook);
            releases.Should().ContainSingle(r => r.LocalBooks.Count == 4 && r.Edition == _audio);
        }

        [Test]
        public void disk_rescan_of_a_mixed_folder_already_splits_epub_from_audio()
        {
            // The untracked disk-scan / initial manual-import-folder-scan path (SingleRelease =
            // false) is unaffected: TrackGroupingService isolates every ebook/archive file as its
            // own single-file release before folder grouping runs, so the group GetBestRelease
            // sees is already single-class. Documented here as a regression guard, in the same
            // config DiskScanService.Scan uses for an author-scoped rescan.
            var tracks = GivenMixedVolumeFiles("/lightnovels/Overlord/Overlord - Vol. 5/");

            var idOverrides = new IdentificationOverrides { Author = _volume.Author.Value };
            var config = new ImportDecisionMakerConfig { SingleRelease = false, KeepAllEditions = true, IncludeExisting = true };

            var releases = Subject.Identify(tracks, idOverrides, config);

            releases.Should().HaveCount(2);
            releases.Should().ContainSingle(r => r.LocalBooks.Count == 1 && r.Edition == _ebook);
            releases.Should().ContainSingle(r => r.LocalBooks.Count == 4 && r.Edition == _audio);
        }

        [Test]
        public void manga_volume_group_is_unaffected_by_the_class_split()
        {
            var manga = new Book { Id = 6, Title = "Dandadan Vol. 1", AuthorMetadataId = 1, AuthorMetadata = _volume.AuthorMetadata.Value, Author = _volume.Author.Value, SeriesLinks = new List<SeriesBookLink>() };
            var archive = manga.WithEdition(MediaType.Archive, id: 61);
            archive.Title = "Dandadan Vol. 1";
            archive.Language = "eng";
            GivenCandidates(archive);

            // A manga folder is single-class (Archive) even with several volumes grouped together,
            // e.g. a bulk "assign this series" manual-import action.
            var tracks = new List<LocalBook>
            {
                new LocalBook { Path = "/downloads/Dandadan/Dandadan - Vol 001.cbz", FileTrackInfo = new ParsedTrackInfo { Authors = new List<string> { "Dandadan" }, BookTitle = "Dandadan Vol. 1" } },
                new LocalBook { Path = "/downloads/Dandadan/Dandadan - Vol 002.cbz", FileTrackInfo = new ParsedTrackInfo { Authors = new List<string> { "Dandadan" }, BookTitle = "Dandadan Vol. 1" } }
            };

            var config = new ImportDecisionMakerConfig { SingleRelease = true, KeepAllEditions = true, IncludeExisting = false };

            var releases = Subject.Identify(tracks, new IdentificationOverrides(), config);

            releases.Should().HaveCount(1);
            releases[0].LocalBooks.Select(x => x.Path).Should().BeEquivalentTo(tracks.Select(x => x.Path), "a single-class group is untouched by the split");
            releases[0].Edition.Should().BeSameAs(archive);
        }

        [Test]
        public void pure_audio_multi_part_group_stays_one_release()
        {
            var tracks = Enumerable.Range(1, 12).Select(i => new LocalBook
            {
                Path = $"/lightnovels/Overlord/Overlord - Vol. 5/Overlord - Vol 005 - Part {i:00}.mp3",
                FileTrackInfo = new ParsedTrackInfo { Authors = new List<string> { "Overlord" }, BookTitle = "Overlord Vol. 5" }
            }).ToList();

            var idOverrides = new IdentificationOverrides { Book = _volume };
            var config = new ImportDecisionMakerConfig { SingleRelease = true, KeepAllEditions = true, IncludeExisting = false };

            var releases = Subject.Identify(tracks, idOverrides, config);

            releases.Should().HaveCount(1);
            releases[0].LocalBooks.Select(x => x.Path).Should().BeEquivalentTo(tracks.Select(x => x.Path), "a single-class group is untouched by the split");
            releases[0].Edition.Should().BeSameAs(_audio);
        }
    }
}
