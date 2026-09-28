using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class MediaFileExtensionsFixture
    {
        [Test]
        public void mp4_is_an_audio_extension_graded_m4b()
        {
            // SAB sniffs some M4B posts as .mp4 (same container); they must import on the audio
            // side at the same grade as a .m4b.
            MediaFileExtensions.GetQualityForExtension(".mp4").Should().Be(Quality.M4B);
            MediaFileExtensions.AudioExtensions.Should().Contain(".mp4");
            MediaFileExtensions.AudiobookExtensions.Should().Contain(".mp4");
            MediaFileExtensions.AllExtensions.Should().Contain(".mp4");
        }

        [Test]
        public void mp4_is_not_a_text_or_ebook_extension()
        {
            MediaFileExtensions.TextExtensions.Should().NotContain(".mp4");
            MediaFileExtensions.EbookExtensions.Should().NotContain(".mp4");
        }

        [Test]
        public void azw3_is_an_importable_ebook_extension_graded_azw3()
        {
            // AZW3 is a light-novel fallback quality (id 7): unlike mobi/azw/kepub, a .azw3 file
            // is imported.
            MediaFileExtensions.GetQualityForExtension(".azw3").Should().Be(Quality.AZW3);
            MediaFileExtensions.TextExtensions.Should().Contain(".azw3");
            MediaFileExtensions.EbookExtensions.Should().Contain(".azw3");
        }
    }
}
