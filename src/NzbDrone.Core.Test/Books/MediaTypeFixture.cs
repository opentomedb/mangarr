using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class MediaTypeFixture
    {
        [Test]
        public void quality_maps_to_a_media_type()
        {
            MediaTypes.OfQuality(Quality.CBZ).Should().Be(MediaType.Archive);
            MediaTypes.OfQuality(Quality.PDF).Should().Be(MediaType.Archive);
            MediaTypes.OfQuality(Quality.Unknown).Should().Be(MediaType.Archive);
            MediaTypes.OfQuality(null).Should().Be(MediaType.Archive);
            MediaTypes.OfQuality(Quality.EPUB).Should().Be(MediaType.Ebook);
            MediaTypes.OfQuality(Quality.AZW3).Should().Be(MediaType.Ebook);
            MediaTypes.OfQuality(Quality.MP3).Should().Be(MediaType.Audio);
            MediaTypes.OfQuality(Quality.FLAC).Should().Be(MediaType.Audio);
            MediaTypes.OfQuality(Quality.M4B).Should().Be(MediaType.Audio);
            MediaTypes.OfQuality(Quality.UnknownAudio).Should().Be(MediaType.Audio);
        }

        [TestCase(".cbz", MediaType.Archive)]
        [TestCase(".pdf", MediaType.Archive)]
        [TestCase(".epub", MediaType.Ebook)]
        [TestCase(".mobi", MediaType.Ebook)]
        [TestCase(".KEPUB", MediaType.Ebook)]
        [TestCase(".m4b", MediaType.Audio)]
        [TestCase(".MP3", MediaType.Audio)]
        [TestCase(".opus", MediaType.Audio)]
        [TestCase("", MediaType.Archive)]
        [TestCase(null, MediaType.Archive)]
        public void extension_maps_to_a_media_type(string extension, MediaType expected)
        {
            MediaTypes.OfExtension(extension).Should().Be(expected);
        }

        [Test]
        public void edition_format_and_id_suffix_per_media_type()
        {
            MediaTypes.EditionFormat(MediaType.Archive).Should().Be("Paperback");
            MediaTypes.EditionFormat(MediaType.Ebook).Should().Be("ebook");
            MediaTypes.EditionFormat(MediaType.Audio).Should().Be("Audiobook");

            MediaTypes.EditionIdSuffix(MediaType.Archive).Should().Be("-ed");
            MediaTypes.EditionIdSuffix(MediaType.Ebook).Should().Be("-ed");
            MediaTypes.EditionIdSuffix(MediaType.Audio).Should().Be("-audio-ed");
        }

        [TestCase(3000, true)]
        [TestCase(3030, true)]
        [TestCase(3999, true)]
        [TestCase(2999, false)]
        [TestCase(4000, false)]
        [TestCase(7020, false)]
        public void audio_categories_are_the_3000_family(int category, bool expected)
        {
            MediaTypes.IsAudioCategory(category).Should().Be(expected);
        }

        // LN PDF (2026-09-22): Ebook PDF (id 8) is a light novel's PDF -- the ebook class. The manga
        // PDF (id 1) stays an archive.
        [Test]
        public void ebook_pdf_is_an_ebook_quality()
        {
            MediaTypes.OfQuality(Quality.EbookPdf).Should().Be(MediaType.Ebook);
            MediaTypes.OfQuality(Quality.PDF).Should().Be(MediaType.Archive);
        }

        // A .pdf is typed by the entry's library where the caller knows it; every other extension,
        // and a manga or unknown-library .pdf, is OfExtension exactly.
        [TestCase("/lightnovels/X/X - Vol 001.pdf", LibraryType.LightNovel, MediaType.Ebook)]
        [TestCase("/lightnovels/X/X - Vol 001.PDF", LibraryType.LightNovel, MediaType.Ebook)]
        [TestCase("/manga/X/X - Vol 001.pdf", LibraryType.Manga, MediaType.Archive)]
        [TestCase("/downloads/X - Vol 001.pdf", null, MediaType.Archive)]
        [TestCase("/lightnovels/X/X - Vol 001.cbz", LibraryType.LightNovel, MediaType.Archive)]
        [TestCase("/lightnovels/X/X - Vol 001.epub", LibraryType.LightNovel, MediaType.Ebook)]
        [TestCase("/lightnovels/X/X - Vol 001.m4b", LibraryType.LightNovel, MediaType.Audio)]
        [TestCase("/manga/X/X - Vol 001.epub", LibraryType.Manga, MediaType.Ebook)]
        [TestCase(".pdf", LibraryType.LightNovel, MediaType.Ebook)]
        [TestCase(null, LibraryType.LightNovel, MediaType.Archive)]
        public void of_file_types_a_pdf_by_the_library(string path, LibraryType? library, MediaType expected)
        {
            MediaTypes.OfFile(path, library).Should().Be(expected);
        }

        // LN PDF (2026-09-22): once the file's entry is known, a light novel's PDF becomes Ebook PDF
        // and keeps its revision; anything else is left exactly as it was.
        [Test]
        public void a_light_novel_pdf_is_regraded_ebook_pdf_and_keeps_its_revision()
        {
            var quality = new QualityModel(Quality.PDF, new Revision(version: 2));

            MediaTypes.RegradePdfForLibrary(quality, "/lightnovels/X/X - Vol 001.pdf", LibraryType.LightNovel);

            quality.Quality.Should().Be(Quality.EbookPdf);
            quality.Revision.Version.Should().Be(2);
        }

        [TestCase("/manga/X/X - Vol 001.pdf", LibraryType.Manga)]
        [TestCase("/downloads/X - Vol 001.pdf", null)]
        public void a_pdf_outside_a_light_novel_keeps_the_manga_pdf(string path, LibraryType? library)
        {
            var quality = new QualityModel(Quality.PDF);

            MediaTypes.RegradePdfForLibrary(quality, path, library);

            quality.Quality.Should().Be(Quality.PDF);
        }

        [Test]
        public void only_the_manga_pdf_quality_is_regraded()
        {
            var epub = new QualityModel(Quality.EPUB);
            MediaTypes.RegradePdfForLibrary(epub, "/lightnovels/X/X - Vol 001.pdf", LibraryType.LightNovel);
            epub.Quality.Should().Be(Quality.EPUB);

            var cbz = new QualityModel(Quality.CBZ);
            MediaTypes.RegradePdfForLibrary(cbz, "/lightnovels/X/X - Vol 001.cbz", LibraryType.LightNovel);
            cbz.Quality.Should().Be(Quality.CBZ);

            MediaTypes.RegradePdfForLibrary(null, "/lightnovels/X/X - Vol 001.pdf", LibraryType.LightNovel);
        }

        private static Book LightNovelVolume(bool ebookMonitored, bool audioMonitored)
        {
            var ebook = new Edition { Id = 1, ForeignEditionId = "local-x~ln-v1-ed", MediaType = MediaType.Ebook, Monitored = ebookMonitored, Title = "X Vol. 1" };
            var audio = new Edition { Id = 2, ForeignEditionId = "local-x~ln-v1-audio-ed", MediaType = MediaType.Audio, Monitored = audioMonitored, Title = "X Vol. 1" };

            return new Book { Editions = new List<Edition> { ebook, audio } };
        }

        [Test]
        public void edition_of_returns_the_edition_of_that_media_type()
        {
            var book = LightNovelVolume(true, true);

            book.EditionOf(MediaType.Ebook).Id.Should().Be(1);
            book.EditionOf(MediaType.Audio).Id.Should().Be(2);
            book.EditionOf(MediaType.Archive).Should().BeNull();
        }

        [Test]
        public void primary_edition_prefers_the_monitored_ebook_or_archive_edition()
        {
            LightNovelVolume(true, true).PrimaryEdition().Id.Should().Be(1);
            LightNovelVolume(false, true).PrimaryEdition().Id.Should().Be(2);
            LightNovelVolume(false, false).PrimaryEdition().Id.Should().Be(1);
        }

        [Test]
        public void primary_edition_of_a_manga_volume_is_its_only_edition()
        {
            var edition = new Edition { Id = 7, MediaType = MediaType.Archive, Monitored = true };
            var book = new Book { Editions = new List<Edition> { edition } };

            book.PrimaryEdition().Should().BeSameAs(edition);
        }

        [Test]
        public void primary_edition_is_null_safe()
        {
            new Book { Editions = new List<Edition>() }.PrimaryEdition().Should().BeNull();
            ((Book)null).PrimaryEdition().Should().BeNull();
        }
    }
}
