using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles
{
    // Light-novel storage (2026-09-22): "path as calibre/Audiobookshelf sees it" <-> "path as Mangarr
    // sees it". Container paths, so POSIX only (Mangarr runs in Docker).
    [TestFixture]
    public class LightNovelPathMapFixture : CoreTest
    {
        [TestCase(null, null)]
        [TestCase("", "")]
        [TestCase("/calibre/", "")]
        [TestCase("", "/srv/calibre/")]
        public void a_blank_pair_is_the_identity(string from, string to)
        {
            LightNovelPathMap.Map("/calibre/A/B.epub", from, to).Should().Be("/calibre/A/B.epub");
        }

        [TestCase("/calibre/", "/srv/calibre/")]
        [TestCase("/calibre", "/srv/calibre")]
        [TestCase("/calibre/", "/srv/calibre")]
        [TestCase("/calibre", "/srv/calibre/")]
        public void the_prefix_is_swapped_with_or_without_trailing_slashes(string from, string to)
        {
            LightNovelPathMap.Map("/calibre/Author/Book (12)/Book.epub", from, to).Should().Be("/srv/calibre/Author/Book (12)/Book.epub");
        }

        [Test]
        public void the_root_itself_maps_to_the_other_root()
        {
            LightNovelPathMap.Map("/audiobooks", "/audiobooks/", "/srv/audiobooks/").Should().Be("/srv/audiobooks");
        }

        [Test]
        public void a_path_outside_the_source_root_is_refused()
        {
            LightNovelPathMap.Map("/elsewhere/X.m4b", "/audiobooks/", "/srv/audiobooks/").Should().BeNull();
        }

        [Test]
        public void a_sibling_sharing_the_prefix_is_not_under_the_root()
        {
            LightNovelPathMap.Map("/audiobooks2/X.m4b", "/audiobooks/", "/srv/audiobooks/").Should().BeNull();
        }

        [Test]
        public void a_result_that_climbs_out_of_the_target_root_is_refused()
        {
            LightNovelPathMap.Map("/audiobooks/../../config/X.m4b", "/audiobooks/", "/srv/audiobooks/").Should().BeNull();
        }

        [Test]
        public void a_dot_dot_that_stays_inside_the_root_is_resolved()
        {
            LightNovelPathMap.Map("/audiobooks/A/../B/X.m4b", "/audiobooks/", "/srv/audiobooks/").Should().Be("/srv/audiobooks/B/X.m4b");
        }

        [TestCase(null)]
        [TestCase("")]
        public void a_blank_path_maps_to_null(string path)
        {
            LightNovelPathMap.Map(path, "/audiobooks/", "/srv/audiobooks/").Should().BeNull();
        }

        [TestCase("/audiobooks/", "/audiobooks", true)]
        [TestCase("/audiobooks", "/audiobooks/", true)]
        [TestCase("/audiobooks/Light Novels", "/audiobooks", true)]
        [TestCase("/audiobooks", "/audiobooks/Light Novels", false)]
        [TestCase("/audiobooks2", "/audiobooks", false)]
        [TestCase("", "/audiobooks", false)]
        public void is_under(string path, string root, bool expected)
        {
            LightNovelPathMap.IsUnder(path, root).Should().Be(expected);
        }

        [TestCase("/srv/audiobooks/", "/srv/audiobooks")]
        [TestCase("/srv/audiobooks", "/srv/audiobooks")]
        [TestCase("/", "/")]
        [TestCase("", null)]
        [TestCase(null, null)]
        public void without_slash(string path, string expected)
        {
            LightNovelPathMap.WithoutSlash(path).Should().Be(expected);
        }

        // the dev server's exact strings (the record is migration 055): the calibre pair returns calibre's path
        // unchanged and the Audiobookshelf pair swaps exactly the prefix, both ways -- one normalisation
        // difference would rewrite every calibre row and lose every ABS item match.
        [Test]
        public void towers_calibre_pair_returns_calibres_path_unchanged()
        {
            const string path = "/books/Kugane Maruyama/Overlord, Vol. 5 (101)/Overlord, Vol. 5 - Kugane Maruyama.epub";

            LightNovelPathMap.Map(path, light_novel_storage.CalibrePath, light_novel_storage.CalibrePath).Should().Be(path);
        }

        [Test]
        public void towers_audiobookshelf_pair_swaps_exactly_the_prefix_both_ways()
        {
            var abs = light_novel_storage.AudiobookshelfRemotePath + "Overlord - Vol. 5/Overlord - Vol 005.m4b";
            var local = light_novel_storage.AudiobookshelfLocalPath + "Overlord - Vol. 5/Overlord - Vol 005.m4b";

            LightNovelPathMap.Map(abs, light_novel_storage.AudiobookshelfRemotePath, light_novel_storage.AudiobookshelfLocalPath).Should().Be(local);
            LightNovelPathMap.Map(local, light_novel_storage.AudiobookshelfLocalPath, light_novel_storage.AudiobookshelfRemotePath).Should().Be(abs);
        }

        [Test]
        public void towers_audiobookshelf_root_is_the_old_mount()
        {
            var connection = new AudiobookshelfConnection
            {
                RemotePath = light_novel_storage.AudiobookshelfRemotePath,
                LocalPath = light_novel_storage.AudiobookshelfLocalPath
            };

            LightNovelStorage.AudiobookshelfRootOf(connection, null).Should().Be(light_novel_storage.AudiobookshelfLocalPath.TrimEnd('/'));
        }
    }
}
