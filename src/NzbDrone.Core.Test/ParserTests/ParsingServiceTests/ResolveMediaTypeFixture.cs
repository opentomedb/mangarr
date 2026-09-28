using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    // Light novels (2026-09): a release's quality class decides which edition it targets. The
    // decision carries that as RemoteBook.MediaType, set once by ParsingService.Map.
    [TestFixture]
    public class ResolveMediaTypeFixture : CoreTest<ParsingService>
    {
        private static Author Manga() => new Author { Id = 1, Name = "Dandadan", CleanName = "dandadan", AuthorMetadataId = 1, Metadata = new AuthorMetadata { Name = "Dandadan", ForeignAuthorId = "local-dandadan" } };
        private static Author LightNovel() => new Author { Id = 2, Name = "Overlord", CleanName = "overlord", AuthorMetadataId = 2, Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord~ln" } };

        private static ParsedBookInfo Parsed(Quality quality)
        {
            return new ParsedBookInfo { Quality = new QualityModel(quality) };
        }

        [TestCase("CBZ", MediaType.Archive)]
        [TestCase("PDF", MediaType.Archive)]
        [TestCase("EPUB", MediaType.Ebook)]
        [TestCase("Ebook PDF", MediaType.Ebook)]
        [TestCase("M4B", MediaType.Audio)]
        [TestCase("MP3", MediaType.Audio)]
        public void should_resolve_media_type_from_the_quality_class(string qualityName, MediaType expected)
        {
            var quality = Quality.All.Find(q => q.Name == qualityName);

            ParsingService.ResolveMediaType(Parsed(quality), Manga(), null).Should().Be(expected);
            ParsingService.ResolveMediaType(Parsed(quality), LightNovel(), null).Should().Be(expected);
        }

        [Test]
        public void unknown_quality_is_archive_for_a_manga_author()
        {
            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), Manga(), null).Should().Be(MediaType.Archive);
            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), Manga(), new AuthorSearchCriteria { MediaType = MediaType.Audio }).Should().Be(MediaType.Archive);
        }

        [Test]
        public void unknown_quality_follows_the_search_for_a_light_novel_author()
        {
            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), LightNovel(), new AuthorSearchCriteria { MediaType = MediaType.Audio }).Should().Be(MediaType.Audio);
            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), LightNovel(), new BookSearchCriteria { MediaType = MediaType.Ebook }).Should().Be(MediaType.Ebook);
        }

        [Test]
        public void unknown_quality_without_a_search_is_archive_even_for_a_light_novel_author()
        {
            // RSS has no search criteria; Unknown stays Archive and the profile/media-type specs reject it.
            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), LightNovel(), null).Should().Be(MediaType.Archive);
        }

        // D1 (2026-09-18): inside the light-novel search branch, manga wording in the release name
        // ("Yen.Press-The.Eminence.In.Shadow.Vol.06.Manga...Comic.eBook") resolves to Archive, which
        // the media-type spec rejects for a light novel, instead of following the search.
        private const string MangaNamedRelease = "Yen.Press-The.Eminence.In.Shadow.Vol.06.Manga.2022.Hybrid.Comic.eBook-BitBook";

        [Test]
        public void manga_wording_in_an_unknown_release_is_archive_for_a_light_novel_search()
        {
            var ebookSearch = new BookSearchCriteria { MediaType = MediaType.Ebook };
            var audioSearch = new AuthorSearchCriteria { MediaType = MediaType.Audio };

            // The decision maker threads the indexer title in.
            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), LightNovel(), ebookSearch, MangaNamedRelease).Should().Be(MediaType.Archive);
            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), LightNovel(), audioSearch, MangaNamedRelease).Should().Be(MediaType.Archive);

            // Without a threaded title the parsed release title is read.
            var parsed = Parsed(Quality.Unknown);
            parsed.ReleaseTitle = MangaNamedRelease;
            ParsingService.ResolveMediaType(parsed, LightNovel(), ebookSearch).Should().Be(MediaType.Archive);
        }

        [Test]
        public void unknown_release_without_manga_wording_still_follows_a_light_novel_search()
        {
            var title = "The Eminence in Shadow v01-06 [Yen Press] [Stick]";

            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), LightNovel(), new BookSearchCriteria { MediaType = MediaType.Ebook }, title).Should().Be(MediaType.Ebook);
            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), LightNovel(), new AuthorSearchCriteria { MediaType = MediaType.Audio }, title).Should().Be(MediaType.Audio);
        }

        [Test]
        public void manga_wording_changes_nothing_for_a_manga_author()
        {
            var expected = MediaTypes.OfQuality(Quality.Unknown);

            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), Manga(), null, MangaNamedRelease).Should().Be(expected);
            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), Manga(), new BookSearchCriteria { MediaType = MediaType.Ebook }, MangaNamedRelease).Should().Be(expected);
            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), Manga(), new AuthorSearchCriteria { MediaType = MediaType.Audio }, "Dandadan Vol. 5 (Light Novel)").Should().Be(expected);
        }

        [Test]
        public void map_should_stamp_the_media_type_on_the_remote_book()
        {
            var author = LightNovel();
            var criteria = new AuthorSearchCriteria { Author = author, Books = new List<Book>(), MediaType = MediaType.Audio };

            Mocker.GetMock<IBookService>()
                  .Setup(s => s.GetBooksByAuthor(author.Id))
                  .Returns(new List<Book>());

            var parsed = new ParsedBookInfo
            {
                ReleaseTitle = "Overlord Vol. 5 (Light Novel) [M4B]",
                AuthorName = "Overlord",
                BookTitle = "Overlord Vol. 5",
                VolumeNumber = 5,
                Quality = new QualityModel(Quality.M4B)
            };

            Subject.Map(parsed, criteria).MediaType.Should().Be(MediaType.Audio);

            parsed.Quality = new QualityModel(Quality.Unknown);
            Subject.Map(parsed, criteria).MediaType.Should().Be(MediaType.Audio);

            parsed.Quality = new QualityModel(Quality.EPUB);
            Subject.Map(parsed, criteria).MediaType.Should().Be(MediaType.Ebook);
        }

        // A volume-tokened title with no format token grades CBZ by the manga default (the quality
        // parser has no author). For a light-novel author the decision re-grades it by library so
        // the EPUB / audio profile and the media-type spec see the right class; a manga author and
        // an explicit archive token are left exactly as graded.
        private static ParsedBookInfo ParsedFromTitle(string title)
        {
            var parsed = new ParsedBookInfo { ReleaseTitle = title, Quality = QualityParser.ParseQuality(title) };
            parsed.Quality.Quality.Should().Be(Quality.CBZ, "the manga volume-token default is the input under test");

            return parsed;
        }

        // 2026-09-21: "[Synthworks] Rascal Does Not Dream English Light Novels 1-15" (Nyaa, cats 7000)
        // was grabbed for a light-novel EPUB search as ARCHIVE, downloaded 15 EPUBs and was failed at
        // import ("only ebook files, which no monitored edition ... can hold"). The decision maker's
        // steps, in order, on that exact title.
        [Test]
        public void light_novel_epub_batch_with_plural_wording_types_as_ebook_for_an_epub_search()
        {
            var title = "[Synthworks] Rascal Does Not Dream English Light Novels 1-15";
            var categories = new List<int> { 7000, 117084 };
            var search = new BookSearchCriteria { MediaType = MediaType.Ebook };
            var author = new Author { Id = 3, Name = "Rascal Does Not Dream", CleanName = "rascaldoesnotdream", AuthorMetadataId = 3, Metadata = new AuthorMetadata { Name = "Rascal Does Not Dream", ForeignAuthorId = "local-rascal-does-not-dream~ln" } };

            var parsed = new ParsedBookInfo { Quality = QualityParser.ParseQuality(title) };
            var afterParse = parsed.Quality.Quality;

            if (parsed.Quality.Quality == Quality.Unknown)
            {
                parsed.Quality = QualityParser.ParseQuality(title, null, categories);
            }

            var afterCategories = parsed.Quality.Quality;
            var type = ParsingService.ResolveMediaType(parsed, author, search, title);

            if (ParsingService.RegradeForLibrary(parsed, title, author, search, categories))
            {
                type = ParsingService.ResolveMediaType(parsed, author, search, title);
            }

            type.Should().Be(MediaType.Ebook, "parse={0} categories={1} final={2}", afterParse, afterCategories, parsed.Quality.Quality);
        }

        [Test]
        public void volume_title_without_a_format_token_is_regraded_epub_for_a_light_novel_author()
        {
            var parsed = ParsedFromTitle("Overlord Vol. 5 [Yen Press]");

            ParsingService.RegradeForLibrary(parsed, "Overlord Vol. 5 [Yen Press]", LightNovel(), null, null).Should().BeTrue();
            parsed.Quality.Quality.Should().Be(Quality.EPUB);
            ParsingService.ResolveMediaType(parsed, LightNovel(), null).Should().Be(MediaType.Ebook);

            // An EPUB search agrees.
            parsed = ParsedFromTitle("Overlord Vol. 5 [Yen Press]");
            ParsingService.RegradeForLibrary(parsed, "Overlord Vol. 5 [Yen Press]", LightNovel(), new BookSearchCriteria { MediaType = MediaType.Ebook }, null).Should().BeTrue();
            parsed.Quality.Quality.Should().Be(Quality.EPUB);
        }

        [Test]
        public void volume_title_without_a_format_token_keeps_cbz_for_a_manga_author()
        {
            var parsed = ParsedFromTitle("Overlord Vol. 5 [Yen Press]");

            ParsingService.RegradeForLibrary(parsed, "Overlord Vol. 5 [Yen Press]", Manga(), null, null).Should().BeFalse();
            ParsingService.RegradeForLibrary(parsed, "Overlord Vol. 5 [Yen Press]", Manga(), new AuthorSearchCriteria { MediaType = MediaType.Audio }, new List<int> { 3030 }).Should().BeFalse();
            parsed.Quality.Quality.Should().Be(Quality.CBZ);
            ParsingService.ResolveMediaType(parsed, Manga(), null).Should().Be(MediaType.Archive);
        }

        [Test]
        public void volume_title_without_a_format_token_follows_an_audio_search_or_category_for_a_light_novel_author()
        {
            var parsed = ParsedFromTitle("Overlord Vol. 5 [Yen Press]");
            var audioSearch = new AuthorSearchCriteria { MediaType = MediaType.Audio };

            ParsingService.RegradeForLibrary(parsed, "Overlord Vol. 5 [Yen Press]", LightNovel(), audioSearch, null).Should().BeTrue();
            parsed.Quality.Quality.Should().Be(Quality.UnknownAudio);
            ParsingService.ResolveMediaType(parsed, LightNovel(), audioSearch).Should().Be(MediaType.Audio);

            parsed = ParsedFromTitle("Overlord Vol. 5 [Yen Press]");
            ParsingService.RegradeForLibrary(parsed, "Overlord Vol. 5 [Yen Press]", LightNovel(), null, new List<int> { 3030 }).Should().BeTrue();
            parsed.Quality.Quality.Should().Be(Quality.UnknownAudio);
            ParsingService.ResolveMediaType(parsed, LightNovel(), null).Should().Be(MediaType.Audio);
        }

        // 2026-09-21: the audio search reaches book-only indexers now; a batch with no audiobook
        // wording from a book category is the ebook class however the search was typed.
        [Test]
        public void audio_search_result_from_a_book_only_category_without_audiobook_wording_grades_epub()
        {
            var title = "Sentenced to Be a Hero v01-05 [Yen Press] [Stick]";
            var parsed = ParsedFromTitle(title);
            var audioSearch = new AuthorSearchCriteria { MediaType = MediaType.Audio };
            var bookCategories = new List<int> { 7000, 117084 };

            ParsingService.RegradeForLibrary(parsed, title, LightNovel(), audioSearch, bookCategories).Should().BeTrue();
            parsed.Quality.Quality.Should().Be(Quality.EPUB);
            ParsingService.ResolveMediaType(parsed, LightNovel(), audioSearch, title, bookCategories).Should().Be(MediaType.Ebook);

            // audiobook wording from the same category is audio already (the quality parser grades it)
            var worded = new ParsedBookInfo { Quality = QualityParser.ParseQuality("Overlord Vol. 5 [Audiobook] [Yen Audio]") };
            worded.Quality.Quality.Should().Be(Quality.UnknownAudio);
            ParsingService.ResolveMediaType(worded, LightNovel(), audioSearch, "Overlord Vol. 5 [Audiobook] [Yen Audio]", bookCategories).Should().Be(MediaType.Audio);

            // an unknown-quality book-category release follows the same rule
            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), LightNovel(), audioSearch, "Overlord [Yen Press] [Stick]", bookCategories).Should().Be(MediaType.Ebook);
            ParsingService.ResolveMediaType(Parsed(Quality.Unknown), LightNovel(), audioSearch, "Overlord [Yen Press] [Stick]", null).Should().Be(MediaType.Audio);
        }

        [Test]
        public void explicit_archive_token_is_not_regraded_for_a_light_novel_author()
        {
            var parsed = ParsedFromTitle("Overlord Vol. 5 (cbz)");

            ParsingService.RegradeForLibrary(parsed, "Overlord Vol. 5 (cbz)", LightNovel(), null, null).Should().BeFalse();
            parsed.Quality.Quality.Should().Be(Quality.CBZ);
            ParsingService.ResolveMediaType(parsed, LightNovel(), null).Should().Be(MediaType.Archive);
        }

        // The 2026-09-18 grab: "Vol.06" is a volume token, so the manga-named release graded CBZ and
        // the light-novel re-grade turned it into an EPUB grab (History recorded quality 6). Manga
        // wording keeps the archive grade, so the media-type spec refuses it for a light novel.
        [Test]
        public void manga_named_volume_title_is_not_regraded_for_a_light_novel_author()
        {
            var parsed = ParsedFromTitle(MangaNamedRelease);

            ParsingService.RegradeForLibrary(parsed, MangaNamedRelease, LightNovel(), new BookSearchCriteria { MediaType = MediaType.Ebook }, new List<int> { 7020 }).Should().BeFalse();
            parsed.Quality.Quality.Should().Be(Quality.CBZ);
            ParsingService.ResolveMediaType(parsed, LightNovel(), new BookSearchCriteria { MediaType = MediaType.Ebook }, MangaNamedRelease).Should().Be(MediaType.Archive);

            // An audio search agrees.
            parsed = ParsedFromTitle(MangaNamedRelease);
            ParsingService.RegradeForLibrary(parsed, MangaNamedRelease, LightNovel(), new AuthorSearchCriteria { MediaType = MediaType.Audio }, null).Should().BeFalse();
            parsed.Quality.Quality.Should().Be(Quality.CBZ);
        }

        [Test]
        public void underscore_delimited_explicit_archive_token_is_not_regraded_for_a_light_novel_author()
        {
            var parsed = ParsedFromTitle("Overlord_Vol_5_CBZ");

            ParsingService.RegradeForLibrary(parsed, "Overlord_Vol_5_CBZ", LightNovel(), null, null).Should().BeFalse();
            parsed.Quality.Quality.Should().Be(Quality.CBZ);
        }

        [Test]
        public void unknown_and_non_archive_qualities_are_not_regraded()
        {
            // Unknown (mobi/azw, or no volume token) is ResolveMediaType's business; EPUB and audio
            // are already the right class.
            foreach (var quality in new[] { Quality.Unknown, Quality.EPUB, Quality.M4B })
            {
                var parsed = Parsed(quality);
                ParsingService.RegradeForLibrary(parsed, "Overlord Vol. 5 [Yen Press]", LightNovel(), null, null).Should().BeFalse();
                parsed.Quality.Quality.Should().Be(quality);
            }
        }

        // LN PDF (2026-09-22): a light novel's "[PDF]" release is its ebook -- quality Ebook PDF (8),
        // Ebook media type. The revision and the rest of the model are kept. Manga wording or a second
        // archive token keeps the manga PDF (refused by the media-type spec, as before); a manga
        // author is untouched.
        private static ParsedBookInfo ParsedPdf(string title)
        {
            var parsed = new ParsedBookInfo { ReleaseTitle = title, Quality = QualityParser.ParseQuality(title) };
            parsed.Quality.Quality.Should().Be(Quality.PDF, "the manga PDF grade is the input under test");

            return parsed;
        }

        [Test]
        public void pdf_release_is_regraded_ebook_pdf_for_a_light_novel_author()
        {
            var title = "Overlord Vol. 5 [PDF]";
            var parsed = ParsedPdf(title);
            parsed.Quality.Revision = new Revision(version: 2);

            ParsingService.RegradeForLibrary(parsed, title, LightNovel(), new BookSearchCriteria { MediaType = MediaType.Ebook }, null).Should().BeTrue();

            parsed.Quality.Quality.Should().Be(Quality.EbookPdf);
            parsed.Quality.Revision.Version.Should().Be(2);
            ParsingService.ResolveMediaType(parsed, LightNovel(), new BookSearchCriteria { MediaType = MediaType.Ebook }, title).Should().Be(MediaType.Ebook);

            // RSS (no search) and the queue's regrade (TrackedDownloadService) agree.
            parsed = ParsedPdf(title);
            ParsingService.RegradeForLibrary(parsed, title, LightNovel(), null, null).Should().BeTrue();
            parsed.Quality.Quality.Should().Be(Quality.EbookPdf);
        }

        [Test]
        public void manga_worded_pdf_release_keeps_the_manga_pdf_for_a_light_novel_author()
        {
            var title = "Overlord Vol. 5 (Manga) [PDF]";
            var parsed = ParsedPdf(title);

            ParsingService.RegradeForLibrary(parsed, title, LightNovel(), new BookSearchCriteria { MediaType = MediaType.Ebook }, null).Should().BeFalse();

            parsed.Quality.Quality.Should().Be(Quality.PDF);
            ParsingService.ResolveMediaType(parsed, LightNovel(), new BookSearchCriteria { MediaType = MediaType.Ebook }, title).Should().Be(MediaType.Archive);
        }

        [Test]
        public void pdf_release_with_a_second_archive_token_keeps_the_manga_pdf_for_a_light_novel_author()
        {
            var title = "Overlord Vol. 5 [PDF] [CBZ]";
            var parsed = ParsedPdf(title);

            ParsingService.RegradeForLibrary(parsed, title, LightNovel(), null, null).Should().BeFalse();
            parsed.Quality.Quality.Should().Be(Quality.PDF);
        }

        [Test]
        public void pdf_release_keeps_the_manga_pdf_for_a_manga_author()
        {
            var title = "Dandadan v05 [PDF]";
            var parsed = ParsedPdf(title);

            ParsingService.RegradeForLibrary(parsed, title, Manga(), null, null).Should().BeFalse();
            ParsingService.RegradeForLibrary(parsed, title, Manga(), new AuthorSearchCriteria { MediaType = MediaType.Ebook }, null).Should().BeFalse();

            parsed.Quality.Quality.Should().Be(Quality.PDF);
            ParsingService.ResolveMediaType(parsed, Manga(), null, title).Should().Be(MediaType.Archive);
        }
    }
}
