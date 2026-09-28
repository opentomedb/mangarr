using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Test.BookTests.Calibre
{
    // Task 5 (D4): the volume page's formats row. CalibreFormats.Of takes the settings lookup and
    // the calibre call as delegates so the decision -- and its "never a 500" fallback -- is
    // testable without a calibre server or a database.
    [TestFixture]
    public class CalibreFormatsFixture
    {
        private static BookFile GivenFile(string path = "/books/Author/Title.epub", int calibreId = 5)
        {
            return new BookFile { Path = path, CalibreId = calibreId };
        }

        [Test]
        public void not_calibre_homed_falls_back_to_the_files_own_extension()
        {
            var result = CalibreFormats.Of(
                GivenFile(),
                file => null,
                (id, settings) => throw new InvalidOperationException("should not be called"));

            result.Formats.Should().Equal("EPUB");
            result.Error.Should().BeNull();
        }

        [Test]
        public void a_file_never_actually_added_to_calibre_falls_back_to_its_own_extension()
        {
            var result = CalibreFormats.Of(
                GivenFile(calibreId: 0),
                file => new CalibreSettings(),
                (id, settings) => throw new InvalidOperationException("should not be called"));

            result.Formats.Should().Equal("EPUB");
            result.Error.Should().BeNull();
        }

        [Test]
        public void settings_lookup_throwing_is_caught_as_calibre_not_answering()
        {
            var result = CalibreFormats.Of(
                GivenFile(),
                file => throw new Exception("root folder blew up"),
                (id, settings) => throw new InvalidOperationException("should not be called"));

            result.Formats.Should().BeEmpty();
            result.Error.Should().Be("Calibre did not answer");
        }

        [Test]
        public void calibre_call_throwing_is_caught_as_calibre_not_answering()
        {
            var result = CalibreFormats.Of(
                GivenFile(),
                file => new CalibreSettings(),
                (id, settings) => throw new CalibreException("Unable to connect to Calibre library: timed out"));

            result.Formats.Should().BeEmpty();
            result.Error.Should().Be("Calibre did not answer");
        }

        [Test]
        public void a_book_with_no_formats_falls_back_to_the_files_own_extension()
        {
            var result = CalibreFormats.Of(
                GivenFile(),
                file => new CalibreSettings(),
                (id, settings) => new CalibreBook { Formats = null });

            result.Formats.Should().Equal("EPUB");
            result.Error.Should().BeNull();
        }

        [Test]
        public void a_calibre_homed_book_lists_every_format_uppercased_with_its_own_extension_first()
        {
            var result = CalibreFormats.Of(
                GivenFile(),
                file => new CalibreSettings(),
                (id, settings) => new CalibreBook
                {
                    Formats = new Dictionary<string, CalibreBookFormat>
                    {
                        { "azw3", new CalibreBookFormat() },
                        { "epub", new CalibreBookFormat() }
                    }
                });

            result.Formats.Should().Equal("EPUB", "AZW3");
            result.Error.Should().BeNull();
        }

        // Final review C1 (2026-09-22): a light-novel calibre book is tracked by its EPUB, else by
        // the AZW3 fallback -- never by whichever format happens to be the row's own.
        [TestCase(new[] { "epub", "azw3" }, "epub")]
        [TestCase(new[] { "AZW3", "EPUB" }, "EPUB")]
        [TestCase(new[] { "azw3" }, "azw3")]
        [TestCase(new[] { "AZW3", "KEPUB" }, "AZW3")]
        [TestCase(new[] { "MOBI" }, null)]
        [TestCase(new string[0], null)]
        public void tracked_light_novel_format_is_epub_else_azw3(string[] formats, string expected)
        {
            CalibreFormats.TrackedLightNovelFormat(formats).Should().Be(expected);
        }

        [Test]
        public void tracked_light_novel_format_of_no_format_list_is_null()
        {
            CalibreFormats.TrackedLightNovelFormat(null).Should().BeNull();
        }

        // LN PDF (2026-09-22): a PDF-only calibre book (a light novel imported as Ebook PDF) is tracked
        // by its PDF -- judged by EPUB/AZW3 alone it would be forgotten on every scan and re-grabbed
        // (the C1 loop). EPUB, then AZW3, always win over it.
        [TestCase(new[] { "pdf" }, "pdf")]
        [TestCase(new[] { "PDF", "EPUB" }, "EPUB")]
        [TestCase(new[] { "pdf", "azw3" }, "azw3")]
        [TestCase(new[] { "PDF", "KEPUB" }, "PDF")]
        public void tracked_light_novel_format_falls_back_to_pdf_last(string[] formats, string expected)
        {
            CalibreFormats.TrackedLightNovelFormat(formats).Should().Be(expected);
        }

        // Final review M1 (2026-09-22): the path SetFields repoints a row to. Metadata embedding
        // rewrites the EPUB, so the converted AZW3 beside it can be the OLDER file; EPUB still wins
        // whenever calibre holds one. AZW3 is the answer only for an AZW3-only book.
        [Test]
        public void original_format_is_the_epub_even_when_the_converted_azw3_is_older()
        {
            var formats = new Dictionary<string, CalibreBookFormat>
            {
                { "epub", new CalibreBookFormat { Path = "/books/a/Title.epub", LastModified = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc) } },
                { "azw3", new CalibreBookFormat { Path = "/books/a/Title.azw3", LastModified = new DateTime(2026, 9, 22, 11, 0, 0, DateTimeKind.Utc) } }
            };

            CalibreProxy.GetOriginalFormat(formats).Should().Be("/books/a/Title.epub");
        }

        [Test]
        public void original_format_of_an_azw3_only_book_is_its_azw3()
        {
            var formats = new Dictionary<string, CalibreBookFormat>
            {
                { "azw3", new CalibreBookFormat { Path = "/books/a/Title.azw3", LastModified = new DateTime(2026, 9, 22, 11, 0, 0, DateTimeKind.Utc) } }
            };

            CalibreProxy.GetOriginalFormat(formats).Should().Be("/books/a/Title.azw3");
        }

        [Test]
        public void original_format_of_a_manga_book_is_still_its_oldest_archive()
        {
            var formats = new Dictionary<string, CalibreBookFormat>
            {
                { "pdf", new CalibreBookFormat { Path = "/books/a/Title.pdf", LastModified = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc) } },
                { "cbz", new CalibreBookFormat { Path = "/books/a/Title.cbz", LastModified = new DateTime(2026, 9, 22, 11, 0, 0, DateTimeKind.Utc) } }
            };

            CalibreProxy.GetOriginalFormat(formats).Should().Be("/books/a/Title.cbz");
        }

        // LN PDF (2026-09-22) P1: GetOriginalFormat ranks a PDF behind EPUB/AZW3 only when the caller
        // says lightNovel: true. The path SetFields repoints a light-novel row to: EPUB first, then
        // AZW3, then PDF -- never the PDF while calibre holds an EPUB or AZW3, whatever the mtimes say.
        [Test]
        public void original_format_is_the_epub_even_when_the_pdf_is_older()
        {
            var formats = new Dictionary<string, CalibreBookFormat>
            {
                { "epub", new CalibreBookFormat { Path = "/books/a/Title.epub", LastModified = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc) } },
                { "pdf", new CalibreBookFormat { Path = "/books/a/Title.pdf", LastModified = new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc) } }
            };

            CalibreProxy.GetOriginalFormat(formats, lightNovel: true).Should().Be("/books/a/Title.epub");
        }

        [Test]
        public void original_format_is_the_azw3_before_an_older_pdf()
        {
            var formats = new Dictionary<string, CalibreBookFormat>
            {
                { "azw3", new CalibreBookFormat { Path = "/books/a/Title.azw3", LastModified = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc) } },
                { "pdf", new CalibreBookFormat { Path = "/books/a/Title.pdf", LastModified = new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc) } }
            };

            CalibreProxy.GetOriginalFormat(formats, lightNovel: true).Should().Be("/books/a/Title.azw3");
        }

        [Test]
        public void original_format_of_a_pdf_only_book_is_its_pdf()
        {
            var formats = new Dictionary<string, CalibreBookFormat>
            {
                { "pdf", new CalibreBookFormat { Path = "/books/a/Title.pdf", LastModified = new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc) } }
            };

            CalibreProxy.GetOriginalFormat(formats, lightNovel: true).Should().Be("/books/a/Title.pdf");
        }

        // A manga book holds no EPUB/AZW3, so an OLDER PDF still wins over its CBZ, exactly as
        // before (the existing newer-PDF case is original_format_of_a_manga_book_is_still_its_oldest_archive).
        [Test]
        public void original_format_of_a_manga_book_is_its_older_pdf()
        {
            var formats = new Dictionary<string, CalibreBookFormat>
            {
                { "cbz", new CalibreBookFormat { Path = "/books/a/Title.cbz", LastModified = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc) } },
                { "pdf", new CalibreBookFormat { Path = "/books/a/Title.pdf", LastModified = new DateTime(2026, 9, 22, 11, 0, 0, DateTimeKind.Utc) } }
            };

            CalibreProxy.GetOriginalFormat(formats).Should().Be("/books/a/Title.pdf");
        }

        // Controller ruling P1 (2026-09-22): the lightNovel flag is opt-in -- GetAllBookFilePaths's
        // stock calibre roots and every manga call never pass it, so an older PDF still wins over a
        // newer AZW3 exactly as before this round, even though AZW3 itself always sorts last.
        [Test]
        public void original_format_of_a_manga_book_with_an_azw3_is_still_its_older_pdf()
        {
            var formats = new Dictionary<string, CalibreBookFormat>
            {
                { "azw3", new CalibreBookFormat { Path = "/books/a/Title.azw3", LastModified = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc) } },
                { "pdf", new CalibreBookFormat { Path = "/books/a/Title.pdf", LastModified = new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc) } }
            };

            CalibreProxy.GetOriginalFormat(formats, lightNovel: false).Should().Be("/books/a/Title.pdf");
        }
    }
}
