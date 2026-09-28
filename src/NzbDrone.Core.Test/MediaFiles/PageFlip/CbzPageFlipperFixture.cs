using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.PageFlip;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.PageFlip
{
    // Flipping a CBZ is a pure reorder of its page entries: pages come back in reverse order under
    // clean zero-padded names, non-image entries (ComicInfo.xml) ride along untouched, and flipping
    // twice restores the original page sequence byte-for-byte — which is what makes the UI action
    // its own undo.
    [TestFixture]
    public class CbzPageFlipperFixture : CoreTest<CbzPageFlipper>
    {
        private string _cbzPath;

        [SetUp]
        public void Setup()
        {
            _cbzPath = Path.Combine(TempFolder, "volume.cbz");

            using var stream = File.Create(_cbzPath);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

            AddEntry(zip, "0001.jpg", "cover");
            AddEntry(zip, "0002.jpg", "middle");
            AddEntry(zip, "0003.png", "back");
            AddEntry(zip, "ComicInfo.xml", "<ComicInfo/>");
        }

        private static void AddEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using var output = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            output.Write(bytes, 0, bytes.Length);
        }

        private static string[] PageContents(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries
                .Where(e => e.FullName != "ComicInfo.xml")
                .OrderBy(e => e.FullName, System.StringComparer.Ordinal)
                .Select(e =>
                {
                    using var reader = new StreamReader(e.Open());
                    return reader.ReadToEnd();
                })
                .ToArray();
        }

        [Test]
        public void should_reverse_page_order_and_keep_other_entries()
        {
            Subject.Flip(_cbzPath);

            PageContents(_cbzPath).Should().Equal("back", "middle", "cover");

            using var zip = ZipFile.OpenRead(_cbzPath);
            zip.Entries.Select(e => e.FullName).Should().Contain("ComicInfo.xml");
            zip.Entries.First(e => e.FullName == "0001.png").Should().NotBeNull();
        }

        [Test]
        public void flipping_twice_restores_the_original_order()
        {
            Subject.Flip(_cbzPath);
            Subject.Flip(_cbzPath);

            PageContents(_cbzPath).Should().Equal("cover", "middle", "back");
        }

        [Test]
        public void get_page_returns_first_and_last_image()
        {
            Encoding.UTF8.GetString(Subject.GetPage(_cbzPath, last: false, out var firstExt)).Should().Be("cover");
            firstExt.Should().Be(".jpg");

            Encoding.UTF8.GetString(Subject.GetPage(_cbzPath, last: true, out var lastExt)).Should().Be("back");
            lastExt.Should().Be(".png");
        }
    }
}
