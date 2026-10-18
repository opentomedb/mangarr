using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extras.Metadata.ComicInfo;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.PdfConversion;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.PdfConversion
{
    // A converted PDF must KEEP its PDF quality: the CBZ container makes it readable now, but the
    // record staying below the CBZ cutoff is what lets a native scene CBZ upgrade-replace it later
    // — the converted file (and any reversed-order mistake inside it) is always temporary.
    [TestFixture]
    public class PdfConversionServiceFixture : CoreTest<PdfConversionService>
    {
        // LN PDF (2026-09-22): the conversion is a manga tool. A light novel's PDF is its ebook.
        private static Author LightNovelAuthor()
        {
            return new Author { Id = 9, Name = "Overlord", Metadata = new AuthorMetadata { ForeignAuthorId = "local-overlord~ln" } };
        }

        [SetUp]
        public void Setup()
        {
            var author = new Author { Id = 5, Name = "Test Series" };
            var book = new Book { Id = 11 };
            var file = new BookFile
            {
                Id = 21,
                Path = "/manga/Test Series/Test Series - Vol 001.pdf",
                Quality = new QualityModel(Quality.PDF)
            };

            Mocker.GetMock<IPdfToCbzConverter>()
                .Setup(x => x.IsAvailable())
                .Returns(true);

            Mocker.GetMock<IPdfToCbzConverter>()
                .Setup(x => x.Convert(file.Path, It.IsAny<string>()))
                .Returns(new PdfConvertResult { Status = PdfConvertStatus.Converted, PageCount = 100 });

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetAuthor(5))
                .Returns(author);

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthor(5))
                .Returns(new List<Book> { book });

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(11))
                .Returns(new List<BookFile> { file });

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.GetFileSize(It.IsAny<string>()))
                .Returns(12345);

            Mocker.GetMock<IAppFolderInfo>()
                .Setup(x => x.AppDataFolder)
                .Returns("/config");

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderWritable(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderExists(It.IsAny<string>()))
                .Returns(true);
        }

        // Beta readiness (2026-09-28, review M3): Write ComicInfo To = Never also covers the converted CBZ.
        [TestCase(WriteComicInfoType.NewDownloads, 1)]
        [TestCase(WriteComicInfoType.AllImports, 1)]
        [TestCase(WriteComicInfoType.Never, 0)]
        public void converted_cbz_gets_comicinfo_unless_the_mode_is_never(WriteComicInfoType mode, int writes)
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.WriteComicInfo).Returns(mode);

            Subject.Execute(new ConvertPdfToCbzCommand(5));

            Mocker.GetMock<IComicInfoWriter>()
                .Verify(w => w.WriteForFile(It.IsAny<Author>(), It.IsAny<Book>(), It.IsAny<BookFile>()), Times.Exactly(writes));
        }

        // Berserk Vol. 43 (2026-10-17): a PDF the converter refuses as not 1:1 was extracted again by every
        // nightly sweep (232 images out of a 761 MB file, every day). The sweep now leaves an unchanged
        // refused file alone; the volume's own Convert action still tries it, and a replaced file is tried again.
        private void GivenTheConverterRefusesTheFile()
        {
            Mocker.GetMock<IPdfToCbzConverter>()
                .Setup(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new PdfConvertResult { Status = PdfConvertStatus.Skipped, PageCount = 216, NotConvertible = true });
        }

        [Test]
        public void the_sweep_does_not_retry_a_pdf_the_converter_refused()
        {
            GivenTheConverterRefusesTheFile();

            Subject.Execute(new ConvertPdfToCbzCommand(5));
            Subject.Execute(new ConvertPdfToCbzCommand(5));

            Mocker.GetMock<IPdfToCbzConverter>().Verify(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void the_sweep_retries_a_refused_pdf_once_the_file_changes()
        {
            GivenTheConverterRefusesTheFile();

            Subject.Execute(new ConvertPdfToCbzCommand(5));
            Mocker.GetMock<IMediaFileService>().Object.GetFilesByBook(11)[0].Size = 999;
            Subject.Execute(new ConvertPdfToCbzCommand(5));

            Mocker.GetMock<IPdfToCbzConverter>().Verify(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
        }

        [Test]
        public void the_volume_convert_action_still_tries_a_refused_pdf()
        {
            GivenTheConverterRefusesTheFile();

            Mocker.GetMock<IBookService>().Setup(x => x.GetBook(11)).Returns(new Book { Id = 11, AuthorMetadataId = 3 });
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAuthorByMetadataId(3)).Returns(new Author { Id = 5, Name = "Test Series" });

            Subject.Execute(new ConvertPdfToCbzCommand(5));
            Subject.Execute(new ConvertBookPdfToCbzCommand(11));

            Mocker.GetMock<IPdfToCbzConverter>().Verify(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
        }

        [Test]
        public void should_keep_pdf_quality_so_a_native_cbz_can_still_upgrade()
        {
            Subject.Execute(new ConvertPdfToCbzCommand(5));

            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.Update(It.Is<BookFile>(f => f.Path.EndsWith(".cbz") &&
                                                           f.Quality.Quality == Quality.PDF)),
                        Times.Once());
        }

        // The toolbar button reports what it did: the counts the log line already carried now also
        // reach the client, because the executor completes the command with ResultMessage.
        [Test]
        public void should_report_converted_and_failed_counts_on_the_command()
        {
            var command = new ConvertPdfToCbzCommand(5);

            Subject.Execute(command);

            command.ResultMessage.Should().Be("Converted 1 PDF volume(s) to CBZ, 0 failed");
        }

        [Test]
        public void should_report_a_failed_conversion_in_the_counts()
        {
            Mocker.GetMock<IPdfToCbzConverter>()
                .Setup(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new PdfConvertResult { Status = PdfConvertStatus.Failed });

            var command = new ConvertPdfToCbzCommand(5);

            Subject.Execute(command);

            command.ResultMessage.Should().Be("Converted 0 PDF volume(s) to CBZ, 1 failed");
        }

        [Test]
        public void should_report_counts_for_a_single_volume_conversion()
        {
            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBook(11))
                .Returns(new Book { Id = 11, AuthorMetadataId = 7, Title = "Test Series Vol 1" });

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetAuthorByMetadataId(7))
                .Returns(new Author { Id = 5, Name = "Test Series" });

            var command = new ConvertBookPdfToCbzCommand(11);

            Subject.Execute(command);

            command.ResultMessage.Should().Be("Converted 1 PDF volume(s) to CBZ, 0 failed");
        }

        // The nightly sweep and the post-import per-author pushes run this same command with
        // client messages enabled. A run that converted nothing must say nothing, or an open tab
        // gets a green "Converted 0 PDF volume(s) to CBZ, 0 failed" every night.
        [Test]
        public void should_not_report_a_run_that_did_nothing()
        {
            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthor(5))
                .Returns(new List<Book>());

            var command = new ConvertPdfToCbzCommand(5);

            Subject.Execute(command);

            command.ResultMessage.Should().BeNull();
        }

        [Test]
        public void should_not_report_a_single_volume_run_that_did_nothing()
        {
            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBook(11))
                .Returns(new Book { Id = 11, AuthorMetadataId = 7, Title = "Test Series Vol 1" });

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetAuthorByMetadataId(7))
                .Returns(new Author { Id = 5, Name = "Test Series" });

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(11))
                .Returns(new List<BookFile>());

            var command = new ConvertBookPdfToCbzCommand(11);

            Subject.Execute(command);

            command.ResultMessage.Should().BeNull();
        }

        // Without poppler nothing happens at all — a green "Completed" toast would be a lie.
        [Test]
        public void should_report_when_poppler_is_missing()
        {
            Mocker.GetMock<IPdfToCbzConverter>()
                .Setup(x => x.IsAvailable())
                .Returns(false);

            var command = new ConvertPdfToCbzCommand(5);

            Subject.Execute(command);

            command.ResultMessage.Should().Be("PDF conversion unavailable: poppler (pdfimages) is not installed");
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_report_when_the_volume_cannot_be_found()
        {
            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBook(99))
                .Returns((Book)null);

            var command = new ConvertBookPdfToCbzCommand(99);

            Subject.Execute(command);

            command.ResultMessage.Should().Be("Volume not found");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_per_author_run_for_a_light_novel_converts_nothing()
        {
            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetAuthor(9))
                .Returns(LightNovelAuthor());

            var command = new ConvertPdfToCbzCommand(9);

            Subject.Execute(command);

            Mocker.GetMock<IBookService>().Verify(x => x.GetBooksByAuthor(9), Times.Never());
            Mocker.GetMock<IPdfToCbzConverter>().Verify(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            command.ResultMessage.Should().Be("PDF to CBZ conversion is for manga only");
        }

        [Test]
        public void the_all_authors_run_converts_manga_only()
        {
            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetAllAuthors())
                .Returns(new List<Author> { new Author { Id = 5, Name = "Test Series" }, LightNovelAuthor() });

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthor(9))
                .Returns(new List<Book> { new Book { Id = 12 } });

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(12))
                .Returns(new List<BookFile> { new BookFile { Id = 22, Path = "/lightnovels/Overlord/Overlord - Vol 001.pdf", Quality = new QualityModel(Quality.EbookPdf) } });

            var command = new ConvertPdfToCbzCommand();

            Subject.Execute(command);

            Mocker.GetMock<IBookService>().Verify(x => x.GetBooksByAuthor(9), Times.Never());
            Mocker.GetMock<IPdfToCbzConverter>().Verify(x => x.Convert("/lightnovels/Overlord/Overlord - Vol 001.pdf", It.IsAny<string>()), Times.Never());
            command.ResultMessage.Should().Be("Converted 1 PDF volume(s) to CBZ, 0 failed");
        }

        [Test]
        public void a_single_volume_run_for_a_light_novel_converts_nothing()
        {
            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBook(12))
                .Returns(new Book { Id = 12, AuthorMetadataId = 9, Title = "Overlord Vol 1" });

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetAuthorByMetadataId(9))
                .Returns(LightNovelAuthor());

            var command = new ConvertBookPdfToCbzCommand(12);

            Subject.Execute(command);

            Mocker.GetMock<IMediaFileService>().Verify(x => x.GetFilesByBook(12), Times.Never());
            Mocker.GetMock<IPdfToCbzConverter>().Verify(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            command.ResultMessage.Should().Be("PDF to CBZ conversion is for manga only");
        }

        [Test]
        public void should_move_the_original_into_the_configured_folder()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns("/data/pdf-originals");

            Subject.Execute(new ConvertPdfToCbzCommand(5));

            Mocker.GetMock<IDiskProvider>()
                .Verify(x => x.MoveFile("/manga/Test Series/Test Series - Vol 001.pdf",
                                        It.Is<string>(t => t.StartsWith("/data/pdf-originals") && t.EndsWith("Test Series - Vol 001.pdf")),
                                        false),
                        Times.Once());
        }

        [Test]
        public void should_default_to_pdf_originals_under_the_app_data_folder()
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(a => a.AppDataFolder).Returns("/config");
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns(string.Empty);

            Subject.Execute(new ConvertPdfToCbzCommand(5));

            Mocker.GetMock<IDiskProvider>()
                .Verify(x => x.MoveFile(It.IsAny<string>(), It.Is<string>(t => t.StartsWith("/config") && t.Contains("pdf-originals")), false), Times.Once());
        }

        // Review Focus 1: a folder that cannot be written must leave every PDF untouched.
        [Test]
        public void should_not_convert_anything_when_the_originals_folder_is_unusable()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns("/data/pdf-originals");
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FolderWritable("/data/pdf-originals")).Returns(false);

            var command = new ConvertPdfToCbzCommand(5);
            Subject.Execute(command);

            Mocker.GetMock<IPdfToCbzConverter>().Verify(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Update(It.IsAny<BookFile>()), Times.Never());
            command.ResultMessage.Should().Contain("PDF originals folder");
        }

        // Review Focus 2: an original already held under the same name is never overwritten.
        [Test]
        public void should_not_overwrite_an_original_that_is_already_held()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns("/data/pdf-originals");
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists(It.Is<string>(p => p.EndsWith("Test Series - Vol 001.pdf") && p.StartsWith("/data/pdf-originals"))))
                .Returns(true);

            Subject.Execute(new ConvertPdfToCbzCommand(5));

            Mocker.GetMock<IDiskProvider>()
                .Verify(x => x.MoveFile("/manga/Test Series/Test Series - Vol 001.pdf",
                                        It.Is<string>(t => t.StartsWith("/data/pdf-originals") && !t.EndsWith("Test Series - Vol 001.pdf") && t.EndsWith(".pdf")),
                                        false),
                        Times.Once());
        }

        private const string CustomRoot = "/data/pdf-originals";
        private const string CustomMarker = "/data/pdf-originals/.mangarr-pdf-originals";

        [Test]
        public void should_mark_an_empty_custom_folder_and_convert()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns(CustomRoot);

            Subject.Execute(new ConvertPdfToCbzCommand(5));

            Mocker.GetMock<IDiskProvider>().Verify(x => x.WriteAllText(CustomMarker, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IPdfToCbzConverter>().Verify(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        // A custom folder that already holds someone's files is never adopted: retention could delete them.
        [Test]
        public void should_refuse_a_non_empty_unmarked_custom_folder()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns(CustomRoot);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.GetFiles(CustomRoot, true)).Returns(new[] { CustomRoot + "/mine.pdf" });

            var command = new ConvertPdfToCbzCommand(5);
            Subject.Execute(command);

            Mocker.GetMock<IPdfToCbzConverter>().Verify(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(x => x.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            command.ResultMessage.Should().Contain("empty");
        }

        [Test]
        public void should_mark_the_default_folder_even_when_it_holds_files()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns(string.Empty);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.GetFiles(It.IsAny<string>(), true)).Returns(new[] { "/config/pdf-originals/Old/a.pdf" });

            Subject.Execute(new ConvertPdfToCbzCommand(5));

            Mocker.GetMock<IDiskProvider>().Verify(x => x.WriteAllText(It.Is<string>(p => p.StartsWith("/config") && p.EndsWith(".mangarr-pdf-originals")), It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IPdfToCbzConverter>().Verify(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_accept_a_marked_custom_folder_without_scanning_it()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns(CustomRoot);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FileExists(CustomMarker)).Returns(true);

            Subject.Execute(new ConvertPdfToCbzCommand(5));

            Mocker.GetMock<IDiskProvider>().Verify(x => x.GetFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(x => x.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IPdfToCbzConverter>().Verify(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        // A custom folder that is not there (an unmounted drive) must not be created: it would be a plain
        // directory inside the container and the originals would vanish on recreate.
        [Test]
        public void should_not_create_or_use_a_custom_folder_that_does_not_exist()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns(CustomRoot);
            Mocker.GetMock<IDiskProvider>().Setup(x => x.FolderExists(CustomRoot)).Returns(false);

            var command = new ConvertPdfToCbzCommand(5);
            Subject.Execute(command);

            Mocker.GetMock<IDiskProvider>().Verify(x => x.CreateFolder(CustomRoot), Times.Never());
            Mocker.GetMock<IPdfToCbzConverter>().Verify(x => x.Convert(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(x => x.Update(It.IsAny<BookFile>()), Times.Never());
            command.ResultMessage.Should().Contain("PDF originals folder");
        }

        // Retention counts from the day the original was set aside.
        [Test]
        public void should_reset_the_moved_originals_date()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns(CustomRoot);

            Subject.Execute(new ConvertPdfToCbzCommand(5));

            Mocker.GetMock<IDiskProvider>()
                .Verify(x => x.FileSetLastWriteTime(It.Is<string>(t => t.StartsWith(CustomRoot) && t.EndsWith("Test Series - Vol 001.pdf")),
                                                    It.Is<DateTime>(d => Math.Abs((DateTime.UtcNow - d).TotalSeconds) < 30)),
                        Times.Once());
        }

        [Test]
        public void should_add_a_counter_when_the_timestamped_name_is_also_held()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfOriginalsFolder).Returns(CustomRoot);
            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists(It.Is<string>(p => p.StartsWith(CustomRoot + "/") && p.EndsWith(".pdf") && !p.EndsWith(".1.pdf"))))
                .Returns(true);

            Subject.Execute(new ConvertPdfToCbzCommand(5));

            Mocker.GetMock<IDiskProvider>()
                .Verify(x => x.MoveFile("/manga/Test Series/Test Series - Vol 001.pdf", It.Is<string>(t => t.EndsWith(".1.pdf")), false), Times.Once());
        }
    }
}
