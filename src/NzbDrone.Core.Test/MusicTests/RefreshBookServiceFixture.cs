using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshBookServiceFixture
    {
        private static Edition Edition(int id, MediaType mediaType, bool monitored, int votes = 0)
        {
            return new Edition { Id = id, MediaType = mediaType, Monitored = monitored, Ratings = new Ratings { Value = 4, Votes = votes } };
        }

        [Test]
        public void a_light_novel_volume_keeps_one_monitored_edition_per_media_type()
        {
            var future = new List<Edition> { Edition(1, MediaType.Ebook, true), Edition(2, MediaType.Audio, true) };

            RefreshBookService.CollapseMonitoredPerMediaType(future, e => 0).Should().BeEmpty();

            future.Should().OnlyContain(e => e.Monitored);
        }

        [Test]
        public void two_monitored_editions_of_one_media_type_collapse_to_the_one_with_files()
        {
            var future = new List<Edition> { Edition(1, MediaType.Ebook, true), Edition(2, MediaType.Audio, true), Edition(3, MediaType.Audio, true) };

            var changed = RefreshBookService.CollapseMonitoredPerMediaType(future, e => e.Id == 3 ? 4 : 0);

            changed.Should().BeEquivalentTo(new[] { future[1] });
            future[0].Monitored.Should().BeTrue();
            future[1].Monitored.Should().BeFalse();
            future[2].Monitored.Should().BeTrue();
        }

        [Test]
        public void popularity_breaks_the_tie()
        {
            var future = new List<Edition> { Edition(2, MediaType.Audio, true, votes: 10), Edition(3, MediaType.Audio, true, votes: 100) };

            RefreshBookService.CollapseMonitoredPerMediaType(future, e => 0);

            future[0].Monitored.Should().BeFalse();
            future[1].Monitored.Should().BeTrue();
        }

        [Test]
        public void an_unmonitored_light_novel_edition_stays_unmonitored()
        {
            // the per-edition toggle (PUT /edition/{id}) must survive the next refresh
            var future = new List<Edition> { Edition(1, MediaType.Ebook, true), Edition(2, MediaType.Audio, false) };

            RefreshBookService.CollapseMonitoredPerMediaType(future, e => 0).Should().BeEmpty();

            future[1].Monitored.Should().BeFalse();
        }

        [Test]
        public void a_single_edition_manga_volume_is_always_monitored()
        {
            var future = new List<Edition> { Edition(1, MediaType.Archive, false) };

            var changed = RefreshBookService.CollapseMonitoredPerMediaType(future, e => 0);

            changed.Should().BeEquivalentTo(new[] { future[0] });
            future[0].Monitored.Should().BeTrue();
        }

        [Test]
        public void a_monitored_manga_volume_is_untouched()
        {
            var future = new List<Edition> { Edition(1, MediaType.Archive, true) };

            RefreshBookService.CollapseMonitoredPerMediaType(future, e => 0).Should().BeEmpty();
        }

        // Final review I3: a newly minted edition is seeded from the series -- a class no existing
        // edition of the series monitors is inserted unmonitored, so the add-time "Audiobook"
        // unticked survives every catalogue volume minted later.

        [Test]
        public void a_new_volume_of_a_series_with_no_monitored_audio_gets_its_audio_edition_unmonitored()
        {
            var added = new List<Edition> { Edition(0, MediaType.Ebook, true), Edition(0, MediaType.Audio, true) };
            var existing = new List<Edition> { Edition(1, MediaType.Ebook, true), Edition(2, MediaType.Audio, false), Edition(3, MediaType.Ebook, true), Edition(4, MediaType.Audio, false) };

            var changed = RefreshBookService.SeedMonitoredFromSeries(added, existing);

            changed.Should().BeEquivalentTo(new[] { added[1] });
            added[0].Monitored.Should().BeTrue();
            added[1].Monitored.Should().BeFalse();
        }

        [Test]
        public void a_new_volume_of_a_series_with_a_monitored_audio_somewhere_keeps_its_audio_edition_monitored()
        {
            var added = new List<Edition> { Edition(0, MediaType.Ebook, true), Edition(0, MediaType.Audio, true) };
            var existing = new List<Edition> { Edition(1, MediaType.Ebook, true), Edition(2, MediaType.Audio, false), Edition(3, MediaType.Ebook, true), Edition(4, MediaType.Audio, true) };

            RefreshBookService.SeedMonitoredFromSeries(added, existing).Should().BeEmpty();

            added.Should().OnlyContain(e => e.Monitored);
        }

        [Test]
        public void the_first_volumes_of_a_series_keep_the_minted_flags()
        {
            // the first refresh after add: nothing exists yet to seed from, and the add-time choice
            // is applied after that scan (AuthorScannedHandler)
            var added = new List<Edition> { Edition(0, MediaType.Ebook, true), Edition(0, MediaType.Audio, true) };

            RefreshBookService.SeedMonitoredFromSeries(added, new List<Edition>()).Should().BeEmpty();

            added.Should().OnlyContain(e => e.Monitored);
        }

        // LN PDF (2026-09-22): a merged file whose edition is gone is typed by its extension against
        // the TARGET's library -- a light novel's PDF goes to the Ebook edition even when that
        // edition is unmonitored (the display-edition fallback would pick the Audio one).
        [Test]
        public void a_merged_light_novel_pdf_without_an_edition_goes_to_the_ebook_edition()
        {
            var target = new Book { Id = 9, AuthorMetadata = new AuthorMetadata { ForeignAuthorId = "local-overlord~ln" } };
            var ebook = target.WithEdition(MediaType.Ebook, id: 91, monitored: false);
            target.WithEdition(MediaType.Audio, id: 92);

            RefreshBookService.MergeTargetEdition(new BookFile { Path = "/lightnovels/Overlord/Overlord - Vol 009.pdf" }, target).Should().BeSameAs(ebook);
        }

        [Test]
        public void a_merged_manga_pdf_without_an_edition_goes_to_the_archive_edition()
        {
            var target = new Book { Id = 9, AuthorMetadata = new AuthorMetadata { ForeignAuthorId = "local-dandadan" } };
            var archive = target.WithEdition(MediaType.Archive, id: 91);

            RefreshBookService.MergeTargetEdition(new BookFile { Path = "/manga/Dandadan/Dandadan - Vol 009.pdf" }, target).Should().BeSameAs(archive);
        }

        [Test]
        public void a_merged_file_with_an_edition_keeps_that_edition_s_media_type()
        {
            var target = new Book { Id = 9, AuthorMetadata = new AuthorMetadata { ForeignAuthorId = "local-overlord~ln" } };
            target.WithEdition(MediaType.Ebook, id: 91);
            var audio = target.WithEdition(MediaType.Audio, id: 92);

            var file = new BookFile { Path = "/lightnovels/Overlord/Overlord - Vol 009.m4b", Edition = new Edition { MediaType = MediaType.Audio } };

            RefreshBookService.MergeTargetEdition(file, target).Should().BeSameAs(audio);
        }
    }
}
