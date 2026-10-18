using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.PdfConversion;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.PdfConversion
{
    [TestFixture]
    public class PdfOriginalsCleanupServiceFixture : CoreTest<PdfOriginalsCleanupService>
    {
        private const string Root = "/data/pdf-originals";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns(Root);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsCleanupDays).Returns(30);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(Root)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(Root + "/.mangarr-pdf-originals")).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFiles(Root, true)).Returns(new[]
            {
                Root + "/Series/Old.pdf",
                Root + "/Series/Fresh.pdf",
                Root + "/Series/Old Replaced Volume.cbz",
                Root + "/Series/cover.jpg"
            });
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(It.IsAny<string>())).Returns(DateTime.UtcNow.AddDays(-90));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(Root + "/Series/Fresh.pdf")).Returns(DateTime.UtcNow.AddDays(-2));
        }

        // Review Focus 3 and 5: only expired .pdf files go; a young PDF, a CBZ and an image in the same folder stay.
        [Test]
        public void should_delete_only_expired_pdfs()
        {
            Subject.Execute(new CleanUpPdfOriginalsCommand());

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(Root + "/Series/Old.pdf"), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.RemoveEmptySubfolders(Root), Times.Once());
        }

        [Test]
        public void should_do_nothing_when_retention_is_zero()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsCleanupDays).Returns(0);

            Subject.Execute(new CleanUpPdfOriginalsCommand());

            Mocker.GetMock<IDiskProvider>().Verify(d => d.GetFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_do_nothing_when_the_folder_does_not_exist()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(Root)).Returns(false);

            Subject.Execute(new CleanUpPdfOriginalsCommand());

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        // Review Focus 4: only the CURRENT folder is listed; an older folder is never walked.
        [Test]
        public void should_only_list_the_current_originals_folder()
        {
            Subject.Execute(new CleanUpPdfOriginalsCommand());

            Mocker.GetMock<IDiskProvider>().Verify(d => d.GetFiles(It.Is<string>(p => p != Root), It.IsAny<bool>()), Times.Never());
        }

        // A custom folder Mangarr does not own (no marker) is never listed or cleaned.
        [Test]
        public void should_do_nothing_in_a_custom_folder_without_the_marker()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(Root + "/.mangarr-pdf-originals")).Returns(false);

            Subject.Execute(new CleanUpPdfOriginalsCommand());

            Mocker.GetMock<IDiskProvider>().Verify(d => d.GetFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_clean_the_default_folder_without_a_marker()
        {
            const string DefaultRoot = "/config/pdf-originals";
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns(string.Empty);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(a => a.AppDataFolder).Returns("/config");
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(DefaultRoot)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFiles(DefaultRoot, true)).Returns(new[] { DefaultRoot + "/S/Old.pdf" });

            Subject.Execute(new CleanUpPdfOriginalsCommand());

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(DefaultRoot + "/S/Old.pdf"), Times.Once());
        }

        [Test]
        public void should_keep_going_when_one_file_fails()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFiles(Root, true)).Returns(new[]
            {
                Root + "/Series/Bad.pdf",
                Root + "/Series/Old.pdf"
            });
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(Root + "/Series/Bad.pdf")).Throws(new System.IO.IOException("boom"));

            Subject.Execute(new CleanUpPdfOriginalsCommand());

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(Root + "/Series/Old.pdf"), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.RemoveEmptySubfolders(Root), Times.Once());
        }
    }
}
