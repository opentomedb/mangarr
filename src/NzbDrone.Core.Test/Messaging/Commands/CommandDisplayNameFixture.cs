using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Test.Messaging.Commands
{
    // UI pass (2026-09-24, SY-1): System -> Tasks and the queued-task list showed class names split
    // on camel case ("Refresh Author", "Missing Book Search", "Convert Pdf To Cbz"). One display map
    // gives them Mangarr wording; the API's TaskName/Name stay the class names so Run Now and every
    // client that matches on them keep working.
    [TestFixture]
    public class CommandDisplayNameFixture
    {
        [TestCase("RefreshAuthor", "Refresh Series")]
        [TestCase("MissingBookSearch", "Missing Volume Search")]
        [TestCase("CutoffUnmetBookSearch", "Cutoff Unmet Volume Search")]
        [TestCase("RssSync", "RSS Sync")]
        [TestCase("ConvertPdfToCbz", "Convert PDF to CBZ")]
        [TestCase("ConvertBookPdfToCbz", "Convert Volume PDF to CBZ")]
        [TestCase("WriteComicInfo", "Embed Metadata")]
        [TestCase("ReResolveMetadata", "Re-resolve Metadata")]
        [TestCase("ReResolveEdition", "Change Edition")]
        [TestCase("AuthorSearch", "Series Search")]
        [TestCase("BookSearch", "Volume Search")]
        [TestCase("BulkRefreshAuthor", "Bulk Refresh Series")]
        [TestCase("RefreshBook", "Refresh Volume")]
        [TestCase("DownloadedBooksScan", "Downloaded Volumes Scan")]
        [TestCase("ImportExistingLightNovels", "Import Existing Light Novels")]
        [TestCase("Backup", "Backup")]
        [TestCase("CheckHealth", "Check Health")]
        public void should_map_command_name_to_display_name(string name, string expected)
        {
            CommandDisplayName.For(name).Should().Be(expected);
        }

        [Test]
        public void should_accept_the_command_suffix()
        {
            CommandDisplayName.For("RefreshAuthorCommand").Should().Be("Refresh Series");
        }
    }
}
