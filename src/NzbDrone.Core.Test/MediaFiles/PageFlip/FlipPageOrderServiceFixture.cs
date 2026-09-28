using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.PageFlip;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.PageFlip
{
    [TestFixture]
    public class FlipPageOrderServiceFixture : CoreTest<FlipPageOrderService>
    {
        private BookFile GivenFile(string path)
        {
            var file = new BookFile { Id = 21, Path = path };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.Get(21))
                .Returns(file);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.GetFileSize(path))
                .Returns(4321);

            return file;
        }

        [Test]
        public void should_flip_cbz_and_update_size()
        {
            var file = GivenFile("/manga/Series/Series - Vol 012.cbz");

            var command = new FlipPageOrderCommand(21);

            Subject.Execute(command);

            command.ResultMessage.Should().Be("Page order flipped for Series - Vol 012");

            Mocker.GetMock<ICbzPageFlipper>()
                .Verify(x => x.Flip(file.Path), Times.Once());

            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.Update(It.Is<BookFile>(f => f.Id == 21 && f.Size == 4321)), Times.Once());
        }

        // The button reports rather than showing a green "Completed" for a file it refused to touch.
        [Test]
        public void should_not_flip_non_archive_files()
        {
            GivenFile("/manga/Series/Series - Vol 012.pdf");

            var command = new FlipPageOrderCommand(21);

            Subject.Execute(command);

            Mocker.GetMock<ICbzPageFlipper>()
                .Verify(x => x.Flip(It.IsAny<string>()), Times.Never());

            command.ResultMessage.Should().Be("Series - Vol 012 is not a CBZ archive; page order not flipped");

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
