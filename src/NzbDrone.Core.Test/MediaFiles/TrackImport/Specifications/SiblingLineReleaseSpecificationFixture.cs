using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.MetadataSource.Gcd;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    // Line safety (2026-09-28): the 09-28 pack (the main series' 13 volumes) must not import onto the
    // 6-volume spin-off entry; the same pack into the main entry, and a main entry's next volume, still do.
    [TestFixture]
    public class SiblingLineReleaseSpecificationFixture : CoreTest<SiblingLineReleaseSpecification>
    {
        // Verbatim: the 09-28 grab's title (History 8445-8450, and the qBittorrent download's name).
        private const string Pack = "Trapped in a Dating Sim - The World of Otome Games is Tough for Mobs (2018) vol.01-13 [Seven Seas] [EPUB]";

        private Author _author;
        private LocalEdition _edition;
        private DownloadClientItem _download;

        [SetUp]
        public void Setup()
        {
            var gcd = Mocker.GetMock<IGcdMetadataService>();
            gcd.SetupGet(s => s.Available).Returns(true);
            gcd.Setup(s => s.FindSeriesByTomeId(OtomeLines.SpinOffId)).Returns(OtomeLines.SpinOff());
            gcd.Setup(s => s.FindSeriesByTomeId(OtomeLines.MainId)).Returns(OtomeLines.MainLine());
            gcd.Setup(s => s.GetWorkLines(OtomeLines.WorkId)).Returns(OtomeLines.Work());

            GivenEntry(OtomeLines.SpinOffId);
            _download = new DownloadClientItem { Title = Pack };
        }

        private void GivenEntry(string tomeLineId)
        {
            _author = new Author
            {
                Id = 107,
                Metadata = new AuthorMetadata { ForeignAuthorId = "local-otome-spin-off~ln", Name = "Spin-off", TomeLineId = tomeLineId }
            };

            var book = new Book { Id = 1, VolumeNumber = 1, Author = _author };
            _edition = new LocalEdition(new List<LocalBook> { new LocalBook { Path = "/downloads/pack/v01.epub", Author = _author } })
            {
                Edition = new Edition { Book = book, MediaType = MediaType.Ebook },
                NewDownload = true
            };
        }

        [Test]
        public void the_09_28_pack_is_rejected_for_the_spin_off_entry()
        {
            var decision = Subject.IsSatisfiedBy(_edition, _download);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be("Release covers volumes 1-13; this series' line has 6 — it may belong to " + OtomeLines.MainName);
        }

        [Test]
        public void the_same_pack_into_the_main_entry_is_accepted()
        {
            GivenEntry(OtomeLines.MainId);

            Subject.IsSatisfiedBy(_edition, _download).Accepted.Should().BeTrue();
        }

        // A main line is never judged, even when a release runs past what the catalogue knows yet.
        [Test]
        public void an_ongoing_main_entry_getting_its_next_volumes_is_accepted()
        {
            GivenEntry(OtomeLines.MainId);
            _download.Title = "Trapped in a Dating Sim v01-14 [EPUB]";

            Subject.IsSatisfiedBy(_edition, _download).Accepted.Should().BeTrue();
        }

        // Ranges only: an ongoing spin-off's next single volume is not second-guessed.
        [Test]
        public void a_single_volume_past_the_spin_offs_count_is_accepted()
        {
            _download.Title = "Trapped in a Dating Sim - Otome Games Are Tough For Us, Too! v07 [EPUB]";

            Subject.IsSatisfiedBy(_edition, _download).Accepted.Should().BeTrue();
        }

        [Test]
        public void a_range_inside_the_spin_offs_volumes_is_accepted()
        {
            _download.Title = "Otome Games Are Tough For Us, Too! vol.01-06 [EPUB]";

            Subject.IsSatisfiedBy(_edition, _download).Accepted.Should().BeTrue();
        }

        // Only the line's catalogue count counts: volume rows the entry holds past it are what a mis-attached
        // sibling pack leaves behind, so they never widen the spin-off.
        [Test]
        public void volume_rows_past_the_lines_count_do_not_let_the_pack_in()
        {
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(107))
                  .Returns(Enumerable.Range(1, 13).Select(n => new Book { VolumeNumber = n }).ToList());

            Subject.IsSatisfiedBy(_edition, _download).Accepted.Should().BeFalse();
        }

        // Review fixes (I5): the catalogue's count is stale (the spin-off has 8 volumes out, the artifact says 6).
        // A pack naming the spin-off's own part (its name past the shared main-line prefix) is the spin-off's.
        [TestCase("Trapped in a Dating Sim - Otome Games Are Tough for Us, Too! vol.01-08 [Seven Seas] [EPUB]")]
        [TestCase("Trapped in a Dating Sim: Otome Games Are Tough For Us Too (2025) v01-08")]
        [TestCase("Otome Games Are Tough for Us, Too! vol.01-08 [EPUB]")]
        public void a_pack_naming_the_spin_off_past_a_stale_count_is_accepted(string title)
        {
            _download.Title = title;

            Subject.IsSatisfiedBy(_edition, _download).Accepted.Should().BeTrue();
        }

        // A part the main line's name also contains distinguishes nothing: a spin-off named "<main> Extra" whose
        // pack title is the main title is still the main series' pack.
        [Test]
        public void a_title_naming_only_the_shared_prefix_is_still_rejected()
        {
            var spinOff = OtomeLines.SpinOff();
            spinOff.Name = OtomeLines.MainName + " (Mobs)";
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId(OtomeLines.SpinOffId)).Returns(spinOff);

            Subject.IsSatisfiedBy(_edition, _download).Accepted.Should().BeFalse();
        }

        [Test]
        public void no_sibling_reaching_the_range_is_accepted()
        {
            _download.Title = "Trapped in a Dating Sim vol.01-20 [EPUB]";

            Subject.IsSatisfiedBy(_edition, _download).Accepted.Should().BeTrue();
        }

        [Test]
        public void an_entry_with_no_catalogue_line_is_accepted()
        {
            _author.Metadata.Value.TomeLineId = null;

            Subject.IsSatisfiedBy(_edition, _download).Accepted.Should().BeTrue();
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.FindSeriesByTomeId(It.IsAny<string>()), Times.Never());
        }

        // An artifact without tome_work_id / is_main has no siblings: nothing is ever rejected.
        [Test]
        public void an_older_artifact_is_accepted()
        {
            var old = OtomeLines.SpinOff();
            old.TomeWorkId = null;
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId(OtomeLines.SpinOffId)).Returns(old);

            Subject.IsSatisfiedBy(_edition, _download).Accepted.Should().BeTrue();
        }

        // A disk scan or Import Existing has no download; a download not flagged new is not judged either.
        [Test]
        public void no_download_or_an_existing_file_is_accepted()
        {
            Subject.IsSatisfiedBy(_edition, null).Accepted.Should().BeTrue();

            _edition.NewDownload = false;
            Subject.IsSatisfiedBy(_edition, _download).Accepted.Should().BeTrue();
        }

        [Test]
        public void without_a_catalogue_nothing_is_rejected()
        {
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(false);

            Subject.IsSatisfiedBy(_edition, _download).Accepted.Should().BeTrue();
        }
    }
}
