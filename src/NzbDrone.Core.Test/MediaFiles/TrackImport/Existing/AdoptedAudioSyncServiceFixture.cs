using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.MediaFiles;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Existing
{
    // One copy each (2026-09-20); one display title (2026-09-23, the maintainer): every Audiobooks-homed LN
    // audio row of the entry -- adopted and grabbed alike, Mangarr never writes into either kind --
    // has its series, sequence and title pushed to Audiobookshelf over its API. Title is
    // LightNovelTitles.Display(entry name, volume number, catalogue subtitle) -- the same string
    // CalibreProxy.SetFields sends calibre's Title; ABS's own subtitle field is always cleared (the
    // subtitle is folded into the title text). One library read per call; a patch only when ABS
    // disagrees on series, sequence, title or subtitle; never a file touched.
    [TestFixture]
    public class AdoptedAudioSyncServiceFixture : CoreTest<AdoptedAudioSyncService>
    {
        private Author _author;
        private List<AudiobookshelfItem> _items;

        [SetUp]
        public void Setup()
        {
            LightNovelStorageMock.Setup(Mocker.GetMock<ILightNovelStorage>());

            _author = new Author
            {
                Id = 3,
                Name = "Mushoku Tensei",
                Path = "/lightnovels/Mushoku Tensei",
                Metadata = new AuthorMetadata { Name = "Mushoku Tensei", ForeignAuthorId = "local-mushoku-tensei~ln" }
            };

            _items = new List<AudiobookshelfItem>();

            Mocker.GetMock<IAudiobookshelfClient>().Setup(s => s.GetLibraryItems()).Returns(() => _items);
            Mocker.GetMock<IAudiobookshelfClient>()
                .Setup(s => s.PatchMetadata(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(true);
        }

        // LightNovelTitles.Display(entry, N, subtitle). Most cases hold the item at exactly this
        // (and no ABS subtitle) so only the field under test moves.
        private static string DefaultTitle(string entryName, double volumeNumber, string subtitle = null) => LightNovelTitles.Display(entryName, volumeNumber, subtitle);

        private AudiobookshelfItem GivenItem(string id, string path, string seriesName, double volumeNumber = 1, string title = null, string subtitle = null, bool isFile = false)
        {
            var item = new AudiobookshelfItem { Id = id, Path = path, SeriesName = seriesName, Title = title ?? DefaultTitle(_author.Name, volumeNumber), Subtitle = subtitle, IsFile = isFile };
            _items.Add(item);

            return item;
        }

        // rows of one volume's audio edition, in Audiobookshelf's tree as Mangarr mounts it
        private List<BookFile> GivenRows(double volumeNumber, params string[] paths)
        {
            return GivenRows(_author, volumeNumber, null, paths);
        }

        private List<BookFile> GivenRows(Author author, double volumeNumber, string subtitle, params string[] paths)
        {
            var book = new Book { Id = (int)(volumeNumber * 10), VolumeNumber = volumeNumber, Title = DefaultTitle(author.Name, volumeNumber, subtitle), Subtitle = subtitle, Author = author };
            var edition = book.WithEdition(MediaType.Audio, id: (int)(volumeNumber * 100) + 2);

            var rows = paths.Select((p, i) => new BookFile
            {
                Id = (int)(volumeNumber * 1000) + i,
                Path = p,
                Part = i + 1,
                PartCount = paths.Length,
                EditionId = edition.Id,
                Edition = edition,
                Author = author,
                Home = FileHome.Audiobooks,
                Adopted = true
            }).ToList();

            edition.BookFiles = rows;

            return rows;
        }

        private void VerifyNoPatch()
        {
            Mocker.GetMock<IAudiobookshelfClient>()
                .Verify(v => v.PatchMetadata(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void an_item_already_on_the_shelf_is_left_alone()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", "Mushoku Tensei #1");

            var aligned = Subject.Sync(_author, GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b"), out var failure);

            aligned.Should().BeTrue();
            failure.Should().BeNull();
            VerifyNoPatch();
            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.GetLibraryItems(), Times.Once());
        }

        // A pack adopted onto its first volume sits on the shelf as "#3-4": ABS's record is right and
        // richer than ours, so a range whose first number is ours is no difference; a range that
        // starts elsewhere is.
        [Test]
        public void a_range_sequence_starting_at_our_volume_is_kept()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Pack 3-4", "Mushoku Tensei #3-4", volumeNumber: 3);

            var aligned = Subject.Sync(_author, GivenRows(3, "/srv/audiobooks/Mushoku Tensei/Pack 3-4/Pack.m4b"), out _);

            aligned.Should().BeTrue();
            VerifyNoPatch();
        }

        [Test]
        public void a_range_sequence_starting_elsewhere_is_patched_with_ours()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Pack 4-5", "Mushoku Tensei #4-5", volumeNumber: 3);

            Subject.Sync(_author, GivenRows(3, "/srv/audiobooks/Mushoku Tensei/Pack 4-5/Pack.m4b"), out _);

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-1", "Mushoku Tensei", "3", "Mushoku Tensei (Vol. 3)", null), Times.Once());
        }

        [Test]
        public void a_zero_padded_sequence_is_the_same_sequence()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", "Mushoku Tensei #01");

            Subject.Sync(_author, GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b"), out _);

            VerifyNoPatch();
        }

        [Test]
        public void the_series_name_is_compared_without_case()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", "mushoku tensei #1");

            Subject.Sync(_author, GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b"), out _);

            VerifyNoPatch();
        }

        // ABS joins several series with ", "; ours anywhere in the list counts.
        [Test]
        public void an_item_on_several_shelves_that_include_ours_is_left_alone()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", "Rifujin na Magonote #3, Mushoku Tensei #1");

            Subject.Sync(_author, GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b"), out _);

            VerifyNoPatch();
        }

        [Test]
        public void a_different_sequence_is_patched()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", "Mushoku Tensei #2");

            var aligned = Subject.Sync(_author, GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b"), out var failure);

            aligned.Should().BeTrue();
            failure.Should().BeNull();
            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-1", "Mushoku Tensei", "1", "Mushoku Tensei (Vol. 1)", null), Times.Once());
        }

        [Test]
        public void a_half_volume_keeps_its_fraction()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 3.5", "Mushoku Tensei #3", volumeNumber: 3.5);

            Subject.Sync(_author, GivenRows(3.5, "/srv/audiobooks/Mushoku Tensei/Vol 3.5/Vol 3.5.m4b"), out _);

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-1", "Mushoku Tensei", "3.5", "Mushoku Tensei (Vol. 3.5)", null), Times.Once());
        }

        [Test]
        public void an_item_with_no_series_is_patched()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", null);

            Subject.Sync(_author, GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b"), out _);

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-1", "Mushoku Tensei", "1", "Mushoku Tensei (Vol. 1)", null), Times.Once());
        }

        [Test]
        public void an_item_on_another_shelf_only_is_patched()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", "Rifujin na Magonote #3");

            Subject.Sync(_author, GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b"), out _);

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-1", "Mushoku Tensei", "1", "Mushoku Tensei (Vol. 1)", null), Times.Once());
        }

        // The Mushoku Tensei bug (brief, 2026-09-23): ABS carried the audio files' own Audible-style
        // title ("Mushoku Tensei: Jobless Reincarnation (Light Novel), Vol. 1") instead of the one
        // display title. The entry name itself carries a colon.
        [Test]
        public void the_title_sent_is_the_display_title_even_when_the_entry_name_has_a_colon()
        {
            var author = new Author { Id = 4, Name = "Mushoku Tensei: Jobless Reincarnation", Path = "/lightnovels/Mushoku Tensei", Metadata = new AuthorMetadata { Name = "Mushoku Tensei: Jobless Reincarnation" } };
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 14", "Mushoku Tensei: Jobless Reincarnation #14", title: "Mushoku Tensei: Jobless Reincarnation (Light Novel), Vol. 14", subtitle: "Light Novel");

            Subject.Sync(author, GivenRows(author, 14, null, "/srv/audiobooks/Mushoku Tensei/Vol 14/Vol 14 - 01.m4b"), out _);

            Mocker.GetMock<IAudiobookshelfClient>().Verify(
                v => v.PatchMetadata("item-1", "Mushoku Tensei: Jobless Reincarnation", "14", "Mushoku Tensei: Jobless Reincarnation (Vol. 14)", null), Times.Once());
        }

        // Classroom of the Elite style: the catalogue gives this volume a real subtitle, folded into
        // the title text -- ABS still has the pre-fold title, so the title itself is the difference.
        [Test]
        public void a_catalogue_subtitle_folds_into_the_patched_title()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", "Mushoku Tensei #1", title: "Mushoku Tensei (Vol. 1)");

            Subject.Sync(_author, GivenRows(_author, 1, "Year 2", "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b"), out _);

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-1", "Mushoku Tensei", "1", "Mushoku Tensei: Year 2 (Vol. 1)", null), Times.Once());
        }

        // A stray ABS subtitle (an old Audible-tag import, "Light Novel") is cleared even when the
        // series and the (already-folded) title already match.
        [Test]
        public void a_stray_abs_subtitle_is_cleared_even_when_series_and_title_already_match()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", "Mushoku Tensei #1", subtitle: "Light Novel");

            Subject.Sync(_author, GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b"), out _);

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-1", "Mushoku Tensei", "1", "Mushoku Tensei (Vol. 1)", null), Times.Once());
        }

        [Test]
        public void series_title_and_subtitle_all_already_matching_is_left_alone()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", "Mushoku Tensei #1", title: "Mushoku Tensei: Year 2 (Vol. 1)");

            var aligned = Subject.Sync(_author, GivenRows(_author, 1, "Year 2", "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b"), out var failure);

            aligned.Should().BeTrue();
            failure.Should().BeNull();
            VerifyNoPatch();
        }

        // A multi-part audiobook is one ABS item: one decision, one patch at most.
        [Test]
        public void the_parts_of_one_item_are_one_decision()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", "Mushoku Tensei #2");

            Subject.Sync(_author, GivenRows(1,
                "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.mp3",
                "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 02.mp3",
                "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 03.mp3"), out _);

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-1", "Mushoku Tensei", "1", "Mushoku Tensei (Vol. 1)", null), Times.Once());
            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.GetLibraryItems(), Times.Once());
        }

        // A single-file item's ABS path is the file itself; two of them can share a folder.
        [Test]
        public void a_single_file_item_is_found_by_its_file_path()
        {
            GivenItem("item-2", "/audiobooks/Mushoku Tensei/Vol 2.m4b", "Mushoku Tensei #1", volumeNumber: 2, isFile: true);

            Subject.Sync(_author, GivenRows(2, "/srv/audiobooks/Mushoku Tensei/Vol 2.m4b"), out _);

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-2", "Mushoku Tensei", "2", "Mushoku Tensei (Vol. 2)", null), Times.Once());
        }

        [Test]
        public void two_single_file_items_in_one_folder_are_two_decisions()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1.m4b", null, isFile: true);
            GivenItem("item-2", "/audiobooks/Mushoku Tensei/Vol 2.m4b", null, volumeNumber: 2, isFile: true);

            var rows = GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1.m4b").Concat(GivenRows(2, "/srv/audiobooks/Mushoku Tensei/Vol 2.m4b")).ToList();

            Subject.Sync(_author, rows, out _);

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-1", "Mushoku Tensei", "1", "Mushoku Tensei (Vol. 1)", null), Times.Once());
            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-2", "Mushoku Tensei", "2", "Mushoku Tensei (Vol. 2)", null), Times.Once());
            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.GetLibraryItems(), Times.Once());
        }

        [Test]
        public void an_unnumbered_volume_is_skipped()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Side Story", null, volumeNumber: 0);

            Subject.Sync(_author, GivenRows(0, "/srv/audiobooks/Mushoku Tensei/Side Story/Side Story.m4b"), out _);

            VerifyNoPatch();
        }

        [Test]
        public void a_row_audiobookshelf_does_not_know_is_skipped()
        {
            GivenItem("item-9", "/audiobooks/Something Else/Vol 1", null);

            Subject.Sync(_author, GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b"), out _);

            VerifyNoPatch();
        }

        [Test]
        public void a_row_outside_the_audiobooks_mount_is_skipped()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", null);

            Subject.Sync(_author, GivenRows(1, "/lightnovels/Mushoku Tensei/Mushoku Tensei - Vol. 1/Mushoku Tensei - Vol 001.m4b"), out _);

            VerifyNoPatch();
        }

        [Test]
        public void no_rows_reads_nothing()
        {
            var aligned = Subject.Sync(_author, new List<BookFile>(), out var failure);

            aligned.Should().BeTrue();
            failure.Should().BeNull();
            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.GetLibraryItems(), Times.Never());
            VerifyNoPatch();
        }

        // An ABS outage is a warning, never a failed refresh or retag -- and a false result with the
        // reason, so the adoption report can say so.
        [Test]
        public void a_library_read_failure_is_a_warning_and_a_false_result()
        {
            Mocker.GetMock<IAudiobookshelfClient>().Setup(s => s.GetLibraryItems()).Throws(new InvalidOperationException("401"));

            var aligned = Subject.Sync(_author, GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b"), out var failure);

            aligned.Should().BeFalse();
            failure.Should().Be("library read failed: 401");
            VerifyNoPatch();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_failed_patch_is_a_warning_the_next_item_still_syncs_and_the_result_is_false()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1.m4b", null, isFile: true);
            GivenItem("item-2", "/audiobooks/Mushoku Tensei/Vol 2.m4b", null, volumeNumber: 2, isFile: true);

            Mocker.GetMock<IAudiobookshelfClient>()
                .Setup(s => s.PatchMetadata("item-1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new InvalidOperationException("500"));

            var rows = GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1.m4b").Concat(GivenRows(2, "/srv/audiobooks/Mushoku Tensei/Vol 2.m4b")).ToList();

            var aligned = Subject.Sync(_author, rows, out var failure);

            aligned.Should().BeFalse();
            failure.Should().Be("1 patch(es) failed");
            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-2", "Mushoku Tensei", "2", "Mushoku Tensei (Vol. 2)", null), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void patches_that_went_through_are_a_true_result()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1.m4b", null, isFile: true);
            GivenItem("item-2", "/audiobooks/Mushoku Tensei/Vol 2.m4b", null, volumeNumber: 2, isFile: true);

            var rows = GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1.m4b").Concat(GivenRows(2, "/srv/audiobooks/Mushoku Tensei/Vol 2.m4b")).ToList();

            var aligned = Subject.Sync(_author, rows, out var failure);

            aligned.Should().BeTrue();
            failure.Should().BeNull();
            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata(It.IsAny<string>(), "Mushoku Tensei", It.IsAny<string>(), It.IsAny<string>(), null), Times.Exactly(2));
        }

        [Test]
        public void nothing_is_sent_to_audiobookshelf_while_audio_goes_to_the_entry_folder()
        {
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.AudioHome).Returns(LightNovelHome.Entry);
            var row = new BookFile { Id = 1, Path = LightNovelStorageMock.AbsLocal + "Mushoku Tensei - Vol. 1/Mushoku Tensei - Vol 001.m4b", Home = FileHome.Audiobooks, Adopted = true };

            Subject.Sync(_author, new List<BookFile> { row }, out var failure).Should().BeTrue();

            failure.Should().BeNull();
            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.GetLibraryItems(), Times.Never());
        }

        // Coverage (2026-09-23): a grabbed (never-adopted) row is synced the same as an adopted one --
        // Sync itself does not look at BookFile.Adopted at all; its callers decide which rows to pass.
        [Test]
        public void a_grabbed_row_is_synced_the_same_as_an_adopted_one()
        {
            GivenItem("item-1", "/audiobooks/Mushoku Tensei/Vol 1", "Mushoku Tensei #2");

            var rows = GivenRows(1, "/srv/audiobooks/Mushoku Tensei/Vol 1/Vol 1 - 01.m4b");
            rows.ForEach(r => r.Adopted = false);

            Subject.Sync(_author, rows, out _);

            Mocker.GetMock<IAudiobookshelfClient>().Verify(v => v.PatchMetadata("item-1", "Mushoku Tensei", "1", "Mushoku Tensei (Vol. 1)", null), Times.Once());
        }
    }
}
