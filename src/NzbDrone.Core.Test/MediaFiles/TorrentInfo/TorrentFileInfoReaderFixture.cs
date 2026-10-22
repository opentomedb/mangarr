using System.IO;
using System.Linq;
using FluentAssertions;
using MonoTorrent;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TorrentInfo
{
    [TestFixture]
    public class TorrentFileInfoReaderFixture : CoreTest<TorrentFileInfoReader>
    {
        private string _dir;

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "mangarr-torrent-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_dir, true);
        }

        private static void WriteFile(string path, int length)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[length]);
        }

        private static byte[] Build(string source)
        {
            var creator = new TorrentCreator();
            return creator.CreateAsync(new TorrentFileSource(source)).GetAwaiter().GetResult().Encode();
        }

        [Test]
        public void should_list_files_of_a_multi_file_torrent_in_order_without_the_name_folder()
        {
            var root = Path.Combine(_dir, "Pack");
            WriteFile(Path.Combine(root, "a v01 (Digital).cbz"), 1000);
            WriteFile(Path.Combine(root, "sub", "b v02.cbz"), 2500);

            var result = Subject.GetFilesFromTorrentFile(Build(root));

            result.Name.Should().Be("Pack");
            result.Files.Select(f => f.Index).Should().Equal(0, 1);
            result.Files.Select(f => f.Path).Should().Equal("a v01 (Digital).cbz", "sub/b v02.cbz");
            result.Files.Select(f => f.Length).Should().Equal(1000, 2500);
        }

        [Test]
        public void should_list_the_file_name_for_a_single_file_torrent()
        {
            var file = Path.Combine(_dir, "single v01.cbz");
            WriteFile(file, 700);

            var result = Subject.GetFilesFromTorrentFile(Build(file));

            result.Files.Should().ContainSingle();
            result.Files[0].Index.Should().Be(0);
            result.Files[0].Path.Should().Be("single v01.cbz");
            result.Files[0].Length.Should().Be(700);
        }
    }
}
