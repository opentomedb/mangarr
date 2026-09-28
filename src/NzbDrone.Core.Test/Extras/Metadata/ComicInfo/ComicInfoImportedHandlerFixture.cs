using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Extras.Metadata.ComicInfo;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Extras.Metadata.ComicInfo
{
    // Beta readiness (2026-09-28, F2): Write ComicInfo To. Each import path is the BookImportedEvent it
    // publishes (ImportApprovedBooks.Import(decisions, replaceExisting, downloadClientItem)):
    //   disk scan      -- DiskScanService: Import(decisions, false)            -> newDownload false, no item
    //   Library Import -- adds the series, then the same disk scan              -> newDownload false, no item
    //   Manual Import  -- ManualImportService: ReplaceExistingFiles as newDownload; the tracked download's
    //                     item only when the row carries a DownloadId           -> no item / item
    //   grab           -- CompletedDownloadService: Import(decisions, true, item) -> newDownload true, item
    [TestFixture]
    public class ComicInfoImportedHandlerFixture : CoreTest<ComicInfoImportedHandler>
    {
        private BookImportedEvent Event(bool newDownload, string downloadId)
        {
            var item = downloadId == null ? null : new DownloadClientItem { DownloadId = downloadId };

            return new BookImportedEvent(new Author(), new Book { Title = "Chainsaw Man Vol. 1" },
                new List<BookFile> { new BookFile { Path = "/manga/Chainsaw Man/Chainsaw Man - Vol 001.cbz" } },
                new List<BookFile>(), newDownload, item);
        }

        private static IEnumerable<TestCaseData> Paths()
        {
            yield return new TestCaseData(false, null, false).SetName("disk scan");
            yield return new TestCaseData(false, null, false).SetName("library import (its disk scan)");
            yield return new TestCaseData(true, null, false).SetName("manual import of loose files, replace existing ticked");
            yield return new TestCaseData(false, null, false).SetName("manual import of loose files");
            yield return new TestCaseData(true, "SABnzbd_nzo_abc123", true).SetName("manual import of a queue item");
            yield return new TestCaseData(true, "SABnzbd_nzo_def456", true).SetName("grab");
        }

        private void GivenMode(WriteComicInfoType mode)
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.WriteComicInfo).Returns(mode);
        }

        private void VerifyWrites(int times)
        {
            Mocker.GetMock<IComicInfoWriter>()
                  .Verify(w => w.WriteForFile(It.IsAny<Author>(), It.IsAny<Book>(), It.IsAny<BookFile>()), Times.Exactly(times));
        }

        [TestCaseSource(nameof(Paths))]
        public void new_downloads_writes_only_a_download_client_item(bool newDownload, string downloadId, bool writes)
        {
            GivenMode(WriteComicInfoType.NewDownloads);

            Subject.Handle(Event(newDownload, downloadId));

            VerifyWrites(writes ? 1 : 0);
        }

        [TestCaseSource(nameof(Paths))]
        public void all_imports_writes_every_path(bool newDownload, string downloadId, bool writes)
        {
            GivenMode(WriteComicInfoType.AllImports);

            Subject.Handle(Event(newDownload, downloadId));

            VerifyWrites(1);
        }

        [TestCaseSource(nameof(Paths))]
        public void never_writes_no_path(bool newDownload, string downloadId, bool writes)
        {
            GivenMode(WriteComicInfoType.Never);

            Subject.Handle(Event(newDownload, downloadId));

            VerifyWrites(0);
        }
    }
}
