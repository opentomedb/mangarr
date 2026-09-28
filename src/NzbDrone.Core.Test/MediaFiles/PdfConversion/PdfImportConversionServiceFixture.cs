using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.PdfConversion;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.PdfConversion
{
    // A grabbed PDF should convert to CBZ automatically once the import (move + rename) is done,
    // without waiting for the manual Convert PDFs button. Only download imports qualify — library
    // rescans re-importing existing files (e.g. PDFs restored from the holding folder) must not
    // trigger a surprise re-conversion.
    [TestFixture]
    public class PdfImportConversionServiceFixture : CoreTest<PdfImportConversionService>
    {
        private static TrackImportedEvent ImportEvent(string path, bool newDownload, string foreignAuthorId = "local-series")
        {
            var author = new Author { Id = 7, Metadata = new AuthorMetadata { ForeignAuthorId = foreignAuthorId } };
            var localBook = new LocalBook { Author = author };
            var bookFile = new BookFile { Path = path };

            return new TrackImportedEvent(localBook, bookFile, new List<BookFile>(), newDownload, null);
        }

        [Test]
        public void should_queue_conversion_for_the_author_when_a_pdf_download_is_imported()
        {
            Subject.Handle(ImportEvent("/manga/Series/Series - Vol 030.pdf", true));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<ConvertPdfToCbzCommand>(c => c.AuthorId == 7),
                                    It.IsAny<CommandPriority>(),
                                    It.IsAny<CommandTrigger>()),
                        Times.Once());
        }

        [Test]
        public void should_not_queue_conversion_for_non_pdf_imports()
        {
            Subject.Handle(ImportEvent("/manga/Series/Series - Vol 030.cbz", true));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<ConvertPdfToCbzCommand>(),
                                    It.IsAny<CommandPriority>(),
                                    It.IsAny<CommandTrigger>()),
                        Times.Never());
        }

        [Test]
        public void should_not_queue_conversion_for_rescan_imports()
        {
            Subject.Handle(ImportEvent("/manga/Series/Series - Vol 030.pdf", false));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<ConvertPdfToCbzCommand>(),
                                    It.IsAny<CommandPriority>(),
                                    It.IsAny<CommandTrigger>()),
                        Times.Never());
        }

        // LN PDF (2026-09-22): a light novel's PDF is its ebook (Ebook PDF) -- never turned into a
        // comic archive.
        [Test]
        public void should_not_queue_conversion_for_a_light_novel_pdf()
        {
            Subject.Handle(ImportEvent("/lightnovels/Overlord/Overlord - Vol 005.pdf", true, "local-overlord~ln"));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<ConvertPdfToCbzCommand>(),
                                    It.IsAny<CommandPriority>(),
                                    It.IsAny<CommandTrigger>()),
                        Times.Never());
        }
    }
}
