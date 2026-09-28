using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class MediaFileRepositoryFixture : DbTest<MediaFileRepository, BookFile>
    {
        private Author _author;
        private Book _book;
        private Edition _edition;

        [SetUp]
        public void Setup()
        {
            var meta = Builder<AuthorMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .Build();
            Db.Insert(meta);

            _author = Builder<Author>.CreateNew()
                .With(a => a.AuthorMetadataId = meta.Id)
                .With(a => a.Id = 0)
                .Build();
            Db.Insert(_author);

            _book = Builder<Book>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.AuthorMetadataId = _author.AuthorMetadataId)
                .Build();
            Db.Insert(_book);

            _edition = Builder<Edition>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.BookId = _book.Id)
                .Build();
            Db.Insert(_edition);

            var files = Builder<BookFile>.CreateListOfSize(10)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Quality = new QualityModel(Quality.MP3))
                .TheFirst(5)
                .With(c => c.EditionId = _edition.Id)
                .TheRest()
                .With(c => c.EditionId = 0)
                .TheFirst(1)
                .With(c => c.Path = @"C:\Test\Path\Author\somefile1.flac".AsOsAgnostic())
                .TheNext(1)
                .With(c => c.Path = @"C:\Test\Path\Author\somefile2.flac".AsOsAgnostic())
                .BuildListOfNew();
            Db.InsertMany(files);
        }

        [Test]
        public void get_files_by_author()
        {
            VerifyData();
            var authorFiles = Subject.GetFilesByAuthor(_author.Id);
            VerifyEagerLoaded(authorFiles);

            authorFiles.Should().OnlyContain(c => c.Author.Value.Id == _author.Id);
        }

        [Test]
        public void get_unmapped_files()
        {
            VerifyData();
            var unmappedfiles = Subject.GetUnmappedFiles();
            VerifyUnmapped(unmappedfiles);

            unmappedfiles.Should().HaveCount(5);
        }

        [TestCase("C:\\Test\\Path")]
        [TestCase("C:\\Test\\Path\\")]
        public void get_files_by_base_path_should_cope_with_trailing_slash(string dir)
        {
            VerifyData();
            var firstReleaseFiles = Subject.GetFilesWithBasePath(dir.AsOsAgnostic());

            firstReleaseFiles.Should().HaveCount(2);
        }

        [TestCase("C:\\Test\\Path")]
        [TestCase("C:\\Test\\Path\\")]
        public void get_files_by_base_path_should_not_get_files_for_partial_path(string dir)
        {
            VerifyData();

            var files = Builder<BookFile>.CreateListOfSize(2)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Quality = new QualityModel(Quality.MP3))
                .TheFirst(1)
                .With(c => c.Path = @"C:\Test\Path2\Author\somefile1.flac".AsOsAgnostic())
                .TheNext(1)
                .With(c => c.Path = @"C:\Test\Path2\Author\somefile2.flac".AsOsAgnostic())
                .BuildListOfNew();
            Db.InsertMany(files);

            var firstReleaseFiles = Subject.GetFilesWithBasePath(dir.AsOsAgnostic());
            firstReleaseFiles.Should().HaveCount(2);
        }

        [Test]
        public void get_file_by_path()
        {
            VerifyData();
            var file = Subject.GetFileWithPath(@"C:\Test\Path\Author\somefile2.flac".AsOsAgnostic());

            file.Should().NotBeNull();
            file.Edition.IsLoaded.Should().BeTrue();
            file.Edition.Value.Should().NotBeNull();
            file.Author.IsLoaded.Should().BeTrue();
            file.Author.Value.Should().NotBeNull();
        }

        [Test]
        public void get_files_by_book()
        {
            VerifyData();
            var files = Subject.GetFilesByBook(_book.Id);
            VerifyEagerLoaded(files);

            files.Should().OnlyContain(c => c.EditionId == _book.Id);
        }

        // The manga index only offers Convert PDFs when there is something to convert, so the count
        // must match what the sweep actually touches: files reachable through an edition (the sweep
        // walks authors -> books -> GetFilesByBook, which joins Editions), never unmapped ones.
        [Test]
        public void get_pdf_file_count_counts_only_mapped_pdfs()
        {
            VerifyData();

            var files = Builder<BookFile>.CreateListOfSize(4)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Quality = new QualityModel(Quality.PDF))
                .With(c => c.EditionId = _edition.Id)
                .TheFirst(1)
                .With(c => c.Path = @"C:\Test\Path\Author\vol1.pdf".AsOsAgnostic())
                .TheNext(1)
                .With(c => c.Path = @"C:\Test\Path\Author\vol2.PDF".AsOsAgnostic())
                .TheNext(1)
                .With(c => c.Path = @"C:\Test\Path\Author\vol3.cbz".AsOsAgnostic())
                .TheNext(1)
                .With(c => c.EditionId = 0)
                .With(c => c.Path = @"C:\Test\Path\Author\unmapped.pdf".AsOsAgnostic())
                .BuildListOfNew();
            Db.InsertMany(files);

            Subject.GetPdfFileCount().Should().Be(2);
        }

        [Test]
        public void get_pdf_file_count_is_zero_without_pdfs()
        {
            VerifyData();

            Subject.GetPdfFileCount().Should().Be(0);
        }

        // LN PDF (2026-09-22): the index's Convert PDFs button counts what the sweep converts --
        // manga PDFs only. A light novel's PDF (its ebook) is not counted.
        [Test]
        public void get_pdf_file_count_counts_only_manga_pdfs()
        {
            VerifyData();

            var lnMeta = Builder<AuthorMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.ForeignAuthorId = "local-overlord~ln")
                .With(a => a.TitleSlug = "overlord-ln")
                .With(a => a.Name = "Overlord")
                .Build();
            Db.Insert(lnMeta);

            var lnAuthor = Builder<Author>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.AuthorMetadataId = lnMeta.Id)
                .With(a => a.CleanName = "overlord~ln")
                .Build();
            Db.Insert(lnAuthor);

            var lnBook = Builder<Book>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.AuthorMetadataId = lnMeta.Id)
                .With(a => a.ForeignBookId = "local-overlord~ln-v1")
                .With(a => a.TitleSlug = "overlord-ln-v1")
                .Build();
            Db.Insert(lnBook);

            var lnEdition = Builder<Edition>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.BookId = lnBook.Id)
                .With(a => a.ForeignEditionId = "local-overlord~ln-v1-ed")
                .With(a => a.TitleSlug = "overlord-ln-v1-ed")
                .Build();
            Db.Insert(lnEdition);

            var files = Builder<BookFile>.CreateListOfSize(2)
                .All()
                .With(c => c.Id = 0)
                .TheFirst(1)
                .With(c => c.EditionId = _edition.Id)
                .With(c => c.Quality = new QualityModel(Quality.PDF))
                .With(c => c.Path = @"C:\Test\Path\Author\vol1.pdf".AsOsAgnostic())
                .TheNext(1)
                .With(c => c.EditionId = lnEdition.Id)
                .With(c => c.Quality = new QualityModel(Quality.EbookPdf))
                .With(c => c.Path = @"C:\Test\Path\Overlord\vol1.pdf".AsOsAgnostic())
                .BuildListOfNew();
            Db.InsertMany(files);

            Subject.GetPdfFileCount().Should().Be(1);
        }

        private void VerifyData()
        {
            Db.All<Author>().Should().HaveCount(1);
            Db.All<Book>().Should().HaveCount(1);
            Db.All<BookFile>().Should().HaveCount(10);
        }

        private void VerifyEagerLoaded(List<BookFile> files)
        {
            foreach (var file in files)
            {
                file.Edition.IsLoaded.Should().BeTrue();
                file.Edition.Value.Should().NotBeNull();
                file.Author.IsLoaded.Should().BeTrue();
                file.Author.Value.Should().NotBeNull();
                file.Author.Value.Metadata.IsLoaded.Should().BeTrue();
                file.Author.Value.Metadata.Value.Should().NotBeNull();
            }
        }

        private void VerifyUnmapped(List<BookFile> files)
        {
            foreach (var file in files)
            {
                file.Edition.IsLoaded.Should().BeFalse();
                file.Edition.Value.Should().BeNull();
                file.Author.IsLoaded.Should().BeFalse();
                file.Author.Value.Should().BeNull();
            }
        }

        [Ignore("Doesn't make sense now we link to edition")]
        [Test]
        public void delete_files_by_book_should_work_if_join_fails()
        {
            Db.Delete(_book);
            Subject.DeleteFilesByBook(_book.Id);

            Db.All<BookFile>().Where(x => x.EditionId == _book.Id).Should().HaveCount(0);
        }
    }
}
