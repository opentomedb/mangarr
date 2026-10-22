using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles.Chapters;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.Chapters
{
    // Volumes built from chapter packs (2026-10-21): one CBZ per volume the pack's own files complete,
    // pages copied verbatim in chapter order; the download folder is only ever read.
    [TestFixture]
    public class ChapterVolumeAssemblerFixture : CoreTest<ChapterVolumeAssembler>
    {
        private string _root;
        private string _download;
        private string _work;
        private Author _author;
        private List<Book> _series;

        [SetUp]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "mangarr-chapters-" + Path.GetRandomFileName());
            _download = Path.Combine(_root, "Sample Series c072-081 (Digital)");
            _work = Path.Combine(_root, "work");
            Directory.CreateDirectory(_download);
            Directory.CreateDirectory(_work);

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns<string>(Directory.Exists);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.CreateFolder(It.IsAny<string>())).Callback<string>(p => Directory.CreateDirectory(p));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFiles(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns<string, bool>((p, r) => Directory.GetFiles(p, "*", r ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));

            _author = new Author { Id = 3, Metadata = new AuthorMetadata { Name = "Sample Series", ForeignAuthorId = "sample-series" } };

            // Vol. 9 = chapters 72-81, Vol. 10 = 82-90 (the planner fixture's layout).
            _series = new List<Book> { Volume(9, 72, 81), Volume(10, 82, 90) };

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionsByAuthor(3))
                .Returns(() => _series.SelectMany(b => b.Editions.Value).ToList());
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private static Book Volume(int number, int first, int last, params double[] extra)
        {
            var book = new Book { Id = number, VolumeNumber = number };
            var chapters = Enumerable.Range(first, last - first + 1).Select(n => (double)n).Concat(extra).OrderBy(c => c).ToList();
            book.Editions = new List<Edition> { new Edition { Id = 100 + number, BookId = number, MediaType = MediaType.Archive, Chapters = chapters } };

            return book;
        }

        private Book Vol(int number) => _series.Single(b => b.VolumeNumber == number);

        // Page bytes say which chapter and page they are, so order and byte-identity can be read back.
        private static byte[] PageBytes(double chapter, int page)
        {
            return System.Text.Encoding.ASCII.GetBytes($"chapter {chapter:0.##} page {page} ").Concat(Enumerable.Repeat((byte)page, 64)).ToArray();
        }

        private void GivenChapter(double chapter, int pages = 2, string name = null)
        {
            var file = Path.Combine(_download, name ?? $"Sample Series c{chapter:000.##} (Digital).cbz");

            using var zip = ZipFile.Open(file, ZipArchiveMode.Create);

            // Written out of order on purpose: pages are read by name, not by archive position.
            foreach (var page in Enumerable.Range(1, pages).Reverse())
            {
                var entry = zip.CreateEntry($"p{page}.jpg");
                using var stream = entry.Open();
                var bytes = PageBytes(chapter, page);
                stream.Write(bytes, 0, bytes.Length);
            }

            zip.CreateEntry("ComicInfo.xml");
        }

        // A chapter archive holding exactly these entries, each entry's bytes naming it.
        private void GivenChapterEntries(double chapter, params string[] entries)
        {
            var file = Path.Combine(_download, $"Sample Series c{chapter:000.##} (Digital).cbz");

            using var zip = ZipFile.Open(file, ZipArchiveMode.Create);

            foreach (var name in entries)
            {
                var entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                var bytes = EntryBytes(chapter, name);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        private static byte[] EntryBytes(double chapter, string name)
        {
            return System.Text.Encoding.ASCII.GetBytes($"chapter {chapter:0.##} entry {name}");
        }

        private void GivenChapters(int first, int last)
        {
            for (var c = first; c <= last; c++)
            {
                GivenChapter(c);
            }
        }

        private static List<(string Name, byte[] Bytes, long Stored, long Length)> Pages(string cbz)
        {
            using var zip = ZipFile.OpenRead(cbz);

            return zip.Entries.Select(e =>
            {
                using var stream = e.Open();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                return (e.FullName, copy.ToArray(), e.CompressedLength, e.Length);
            }).ToList();
        }

        private Dictionary<string, long> DownloadSnapshot()
        {
            return Directory.GetFiles(_download, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => new FileInfo(f).Length);
        }

        [Test]
        public void chapters_72_to_81_build_volume_9_with_every_page_in_chapter_order()
        {
            GivenChapters(72, 81);

            var result = Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            result.Skipped.Should().BeEmpty();
            result.Volumes.Should().HaveCount(1);

            var volume = result.Volumes[0];
            volume.Book.Should().BeSameAs(Vol(9));
            volume.First.Should().Be(72);
            volume.Last.Should().Be(81);
            Path.GetFileName(volume.CbzPath).Should().Be("Sample Series Vol. 09 (Built from chapters 72–81).cbz");
            Path.GetDirectoryName(volume.CbzPath).Should().Be(_work);

            var pages = Pages(volume.CbzPath);
            pages.Select(p => p.Name).Should().Equal(Enumerable.Range(1, 20).Select(i => i.ToString("D4") + ".jpg").Append("0021.png"));

            var expected = Enumerable.Range(72, 10).SelectMany(c => new[] { PageBytes(c, 1), PageBytes(c, 2) }).ToList();

            for (var i = 0; i < 21; i++)
            {
                if (i < 20)
                {
                    pages[i].Bytes.Should().Equal(expected[i]);
                }

                pages[i].Stored.Should().Be(pages[i].Length, "pages are stored, never recompressed");
            }
        }

        [Test]
        public void every_entry_unzips_as_a_readable_file()
        {
            GivenChapters(72, 81);

            var cbz = Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work).Volumes.Single().CbzPath;

            using var zip = ZipFile.OpenRead(cbz);
            zip.Entries.Should().OnlyContain(e => ((uint)e.ExternalAttributes >> 16) == 0x81A4, "pages unzip as -rw-r--r--, never mode 000");
        }

        [Test]
        public void macos_metadata_entries_are_never_pages()
        {
            GivenChapters(72, 80);
            GivenChapterEntries(81, "001.jpg", "__MACOSX/._001.jpg", "__MACOSX/sub/002.jpg", "002.jpg", "sub/._002.jpg", "._cover.jpg");

            var pages = Pages(Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work).Volumes.Single().CbzPath);

            pages.Should().HaveCount(21);
            pages[18].Bytes.Should().Equal(EntryBytes(81, "001.jpg"));
            pages[19].Bytes.Should().Equal(EntryBytes(81, "002.jpg"));
        }

        // The same debris at the download's own level: an AppleDouble "._" twin beside a chapter, or a copy
        // under __MACOSX, is never a second file for that chapter.
        [Test]
        public void macos_metadata_files_in_the_download_are_never_chapters()
        {
            GivenChapters(72, 81);
            File.WriteAllBytes(Path.Combine(_download, "._Sample Series c075 (Digital).cbz"), new byte[] { 0, 5, 22, 7 });
            Directory.CreateDirectory(Path.Combine(_download, "__MACOSX"));
            File.WriteAllBytes(Path.Combine(_download, "__MACOSX", "Sample Series c076 (Digital).cbz"), new byte[] { 0, 5, 22, 7 });

            var result = Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            result.Skipped.Should().BeEmpty();
            Pages(result.Volumes.Single().CbzPath).Should().HaveCount(21);
        }

        // Catalogue data error: two volumes listing the same whole chapter. Which one it belongs to is a
        // guess, so neither is built, and the reason says so -- even when only one of them was mapped.
        [Test]
        public void a_volume_whose_chapters_overlap_another_volume_is_skipped_with_a_reason()
        {
            _series[1] = Volume(10, 81, 90);
            GivenChapters(72, 90);

            var result = Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            result.Volumes.Should().BeEmpty();
            result.Skipped.Should().Equal("Vol. 9: its chapters overlap another volume's in the catalogue");
            result.SkippedBooks.Should().Equal(Vol(9));
        }

        [Test]
        public void pages_are_in_natural_number_order()
        {
            GivenChapterEntries(72, "10.jpg", "2.jpg", "1.jpg");
            GivenChapterEntries(73, Enumerable.Range(1, 12).Select(i => $"{i:000}.jpg").Reverse().ToArray());

            for (var c = 74; c <= 81; c++)
            {
                GivenChapter(c);
            }

            var pages = Pages(Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work).Volumes.Single().CbzPath);

            pages.Should().HaveCount(3 + 12 + 16 + 1);
            pages.Take(3).Select(p => p.Bytes).Should().Equal(new[] { "1.jpg", "2.jpg", "10.jpg" }.Select(n => EntryBytes(72, n)), (a, b) => a.SequenceEqual(b));
            pages.Skip(3).Take(12).Select(p => p.Bytes).Should().Equal(Enumerable.Range(1, 12).Select(i => EntryBytes(73, $"{i:000}.jpg")), (a, b) => a.SequenceEqual(b));
        }

        [Test]
        public void the_mapped_books_are_not_modified()
        {
            GivenChapters(72, 81);
            var mapped = new Book { Id = 9, VolumeNumber = 9 };
            var mappedEditions = new List<Edition>();
            mapped.Editions = mappedEditions;

            var result = Subject.Assemble(_download, _author, new List<Book> { mapped }, _work);

            result.Volumes.Single().Book.Should().BeSameAs(mapped);
            mapped.Editions.Value.Should().BeSameAs(mappedEditions);
            mappedEditions.Should().BeEmpty();
        }

        [Test]
        public void a_half_chapter_sits_between_its_neighbours()
        {
            GivenChapters(72, 81);
            GivenChapter(73.5);

            var result = Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            var pages = Pages(result.Volumes.Single().CbzPath);
            pages.Should().HaveCount(23);
            pages[3].Bytes.Should().Equal(PageBytes(73, 2));
            pages[4].Bytes.Should().Equal(PageBytes(73.5, 1));
            pages[5].Bytes.Should().Equal(PageBytes(73.5, 2));
            pages[6].Bytes.Should().Equal(PageBytes(74, 1));
        }

        [Test]
        public void a_half_chapter_another_volume_lists_stays_out_of_this_one()
        {
            _series[1] = Volume(10, 82, 90, 81.5);
            GivenChapters(72, 81);
            GivenChapter(81.5);

            var result = Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            Pages(result.Volumes.Single().CbzPath).Should().HaveCount(21);
        }

        [Test]
        public void a_missing_chapter_skips_the_volume_and_says_which()
        {
            GivenChapters(72, 74);
            GivenChapters(76, 81);

            var result = Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            result.Volumes.Should().BeEmpty();
            result.Skipped.Should().Equal("Vol. 9: chapter 75 missing");
            result.SkippedTexts.Single().Template.Should().Be("Vol. {0}: chapter {1} missing");
            Directory.GetFiles(_work, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }

        [Test]
        public void only_completed_volumes_are_built()
        {
            GivenChapters(72, 85);

            var result = Subject.Assemble(_download, _author, new List<Book> { Vol(9), Vol(10) }, _work);

            result.Volumes.Select(v => v.Book.VolumeNumber).Should().Equal(9);
            result.Skipped.Should().Equal("Vol. 10: chapter 86 missing");
            result.SkippedBooks.Should().Equal(Vol(10));
        }

        [Test]
        public void two_files_for_one_chapter_skip_the_volume()
        {
            GivenChapters(72, 81);
            GivenChapter(75, name: "Sample Series c075 (Digital) (alt).cbz");

            var result = Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            result.Volumes.Should().BeEmpty();
            result.Skipped.Should().Equal("Vol. 9: chapter 75 has more than one file");
        }

        [Test]
        public void files_that_are_not_chapters_are_ignored()
        {
            GivenChapters(72, 81);
            File.WriteAllText(Path.Combine(_download, "credits.txt"), "thanks");
            File.WriteAllBytes(Path.Combine(_download, "cover.jpg"), new byte[] { 1, 2, 3 });

            var result = Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            Pages(result.Volumes.Single().CbzPath).Should().HaveCount(21);
        }

        [Test]
        public void chapter_files_in_sub_folders_count()
        {
            Directory.CreateDirectory(Path.Combine(_download, "part 2"));
            GivenChapters(72, 76);

            for (var c = 77; c <= 81; c++)
            {
                GivenChapter(c, name: Path.Combine("part 2", $"Sample Series c{c:000} (Digital).cbz"));
            }

            var result = Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            Pages(result.Volumes.Single().CbzPath).Should().HaveCount(21);
        }

        [Test]
        public void the_download_folder_is_left_exactly_as_it_was()
        {
            GivenChapters(72, 81);
            File.WriteAllText(Path.Combine(_download, "credits.txt"), "thanks");
            var before = DownloadSnapshot();

            Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            DownloadSnapshot().Should().BeEquivalentTo(before);
        }

        [Test]
        public void a_verify_mismatch_deletes_the_temp_file_and_reports_it()
        {
            GivenChapters(72, 81);

            var subject = new MiscountingAssembler(Mocker.GetMock<IDiskProvider>().Object, Mocker.GetMock<IEditionService>().Object, Mocker.GetMock<IMapCoversToLocal>().Object, Mocker.GetMock<ILocalizationService>().Object, TestLogger);
            var result = subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            result.Volumes.Should().BeEmpty();
            result.Skipped.Should().Equal("Vol. 9: could not be built (built file has 20 pages, expected 21)");
            Directory.GetFiles(_work, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }

        // A real tiny image of a given size and colour, in the given format.
        private static byte[] Picture(int width, int height, bool png, byte shade = 200)
        {
            using var image = new Image<Rgba32>(width, height, new Rgba32(shade, shade, shade));
            using var output = new MemoryStream();

            if (png)
            {
                image.SaveAsPng(output);
            }
            else
            {
                image.SaveAsJpeg(output);
            }

            return output.ToArray();
        }

        private string GivenCover(byte[] bytes, string extension = ".jpg")
        {
            var path = Path.Combine(_root, "cover" + extension);
            File.WriteAllBytes(path, bytes);

            Mocker.GetMock<IMapCoversToLocal>()
                .Setup(m => m.GetCoverPath(9, MediaCoverEntity.Book, MediaCoverTypes.Cover, It.IsAny<string>(), It.IsAny<int?>()))
                .Returns<int, MediaCoverEntity, MediaCoverTypes, string, int?>((id, e, t, ext, h) => Path.Combine(_root, "cover" + ext));

            return path;
        }

        // Chapter pages that are real images (so the closing page can take their size).
        private void GivenImageChapters(int first, int last, int width, int height)
        {
            for (var c = first; c <= last; c++)
            {
                var file = Path.Combine(_download, $"Sample Series c{c:000} (Digital).cbz");

                using var zip = ZipFile.Open(file, ZipArchiveMode.Create);

                foreach (var page in new[] { 1, 2 })
                {
                    using var stream = zip.CreateEntry($"p{page}.png").Open();
                    var bytes = Picture(width, height, true, (byte)(c + page));
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
        }

        [Test]
        public void the_cover_becomes_page_one_byte_for_byte_and_the_closing_page_has_the_chapter_pages_size()
        {
            GivenImageChapters(72, 81, 90, 140);
            var cover = Picture(30, 45, false);
            GivenCover(cover);

            var pages = Pages(Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work).Volumes.Single().CbzPath);

            pages.Should().HaveCount(1 + 20 + 1);
            pages[0].Name.Should().Be("0001.jpg");
            pages[0].Bytes.Should().Equal(cover);
            pages[1].Name.Should().Be("0002.png");
            pages[1].Bytes.Should().Equal(Picture(90, 140, true, 73));
            pages[2].Bytes.Should().Equal(Picture(90, 140, true, 74));
            pages[20].Bytes.Should().Equal(Picture(90, 140, true, 83));

            pages[21].Name.Should().Be("0022.png");
            var info = SixLabors.ImageSharp.Image.Identify(pages[21].Bytes);
            info.Width.Should().Be(90);
            info.Height.Should().Be(140);
        }

        [Test]
        public void without_a_cover_file_the_first_page_is_the_first_chapter_page()
        {
            GivenChapters(72, 81);

            var pages = Pages(Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work).Volumes.Single().CbzPath);

            pages[0].Name.Should().Be("0001.jpg");
            pages[0].Bytes.Should().Equal(PageBytes(72, 1));
        }

        [Test]
        public void a_cover_that_is_not_an_image_is_ignored_and_the_volume_is_still_built()
        {
            GivenChapters(72, 81);
            GivenCover(new byte[] { 1, 2, 3, 4, 5 });

            var result = Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            result.Skipped.Should().BeEmpty();

            var pages = Pages(result.Volumes.Single().CbzPath);
            pages.Should().HaveCount(21);
            pages[0].Bytes.Should().Equal(PageBytes(72, 1));
        }

        [Test]
        public void an_undecodable_first_page_gives_a_1200_by_1800_closing_page()
        {
            GivenChapters(72, 81);

            var last = Pages(Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work).Volumes.Single().CbzPath).Last();

            last.Name.Should().Be("0021.png");
            var info = SixLabors.ImageSharp.Image.Identify(last.Bytes);
            info.Width.Should().Be(1200);
            info.Height.Should().Be(1800);
        }

        [Test]
        public void the_closing_page_is_white_with_dark_grey_text()
        {
            GivenChapters(72, 81);

            var last = Pages(Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work).Volumes.Single().CbzPath).Last();

            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(last.Bytes);
            image[0, 0].Should().Be(new Rgba32(255, 255, 255));

            var dark = 0;

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    if (image[x, y].R < 128)
                    {
                        dark++;
                    }
                }
            }

            dark.Should().BeGreaterThan(500, "text was drawn");
        }

        [TestCase("Vol. 9: Ende", true)]
        [TestCase("Fin du volume", true)]
        [TestCase("Built from chapters 7–15", true)]
        [TestCase("Créé à partir des chapitres 72–81", true)]
        [TestCase("Ende von Band 9 – für Übergrößen", true)]
        [TestCase("第9巻の終わり", false)]
        [TestCase("제9권 끝", false)]
        public void roboto_glyph_check(string text, bool canDraw)
        {
            ChapterVolumeEndPage.CanDraw(text).Should().Be(canDraw);
        }

        [Test]
        public void a_translation_is_used_with_its_tokens_filled_and_missing_ones_read_as_none()
        {
            var tokens = new Dictionary<string, string> { { "volume", "9" } };
            var en = new Dictionary<string, string> { { "k", "End of Vol. {volume}" }, { "same", "Same" } };
            var fr = new Dictionary<string, string> { { "k", "Fin du tome {volume}" }, { "same", "Same" } };

            ChapterVolumeAssembler.Translation("k", fr, en, tokens).Should().Be("Fin du tome 9");
            ChapterVolumeAssembler.Translation("same", fr, en, tokens).Should().BeNull("an untranslated key reads as its English");
            ChapterVolumeAssembler.Translation("absent", fr, en, tokens).Should().BeNull();
            ChapterVolumeAssembler.Translation("k", null, en, tokens).Should().BeNull();
        }

        private void GivenTranslation(string language, string title, string built, string note)
        {
            _author.Metadata = new AuthorMetadata { Name = "Sample Series", ForeignAuthorId = "sample-series", EditionLanguage = language };

            Mocker.GetMock<ILocalizationService>().Setup(l => l.GetLocalizationDictionary("en")).Returns(new Dictionary<string, string>
            {
                { "ServerQueueChapterVolumeEndTitle", "End of Vol. {volume}" },
                { "ServerQueueChapterVolumeEndBuilt", "Built from chapters {first}–{last}" },
                { "ServerQueueChapterVolumeEndNote", "Note" }
            });
            Mocker.GetMock<ILocalizationService>().Setup(l => l.GetLocalizationDictionary(language)).Returns(new Dictionary<string, string>
            {
                { "ServerQueueChapterVolumeEndTitle", title },
                { "ServerQueueChapterVolumeEndBuilt", built },
                { "ServerQueueChapterVolumeEndNote", note }
            });
        }

        private RecordingAssembler Recording(bool throwOnRender = false)
        {
            return new RecordingAssembler(Mocker.GetMock<IDiskProvider>().Object, Mocker.GetMock<IEditionService>().Object, Mocker.GetMock<IMapCoversToLocal>().Object, Mocker.GetMock<ILocalizationService>().Object, TestLogger) { Throw = throwOnRender };
        }

        [Test]
        public void a_translation_roboto_cannot_draw_falls_back_to_english()
        {
            GivenTranslation("ja", "第{volume}巻の終わり", "{first}–{last}話から作成", "公式版が出たら置き換えます。");
            GivenChapters(72, 81);

            var subject = Recording();
            var result = subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            result.Skipped.Should().BeEmpty();
            Pages(result.Volumes.Single().CbzPath).Should().HaveCount(21);
            subject.Lines.Should().Equal("End of Vol. 9", "Built from chapters 72–81", "Mangarr will replace this copy when the official volume is released.");
        }

        [Test]
        public void a_translation_roboto_can_draw_is_the_text_drawn()
        {
            GivenTranslation("fr", "Fin du tome {volume}", "Créé à partir des chapitres {first}–{last}", "Mangarr remplacera cette copie à la sortie du tome officiel.");
            GivenChapters(72, 81);

            var subject = Recording();
            subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            subject.Lines.Should().Equal("Fin du tome 9", "Créé à partir des chapitres 72–81", "Mangarr remplacera cette copie à la sortie du tome officiel.");
        }

        [Test]
        public void a_closing_page_that_cannot_be_drawn_does_not_cost_the_volume()
        {
            GivenChapters(72, 81);

            var result = Recording(throwOnRender: true).Assemble(_download, _author, new List<Book> { Vol(9) }, _work);

            result.Skipped.Should().BeEmpty();

            var pages = Pages(result.Volumes.Single().CbzPath);
            pages.Should().HaveCount(20);
            pages.Last().Name.Should().Be("0020.jpg");
        }

        [Test]
        public void a_cover_whose_extension_is_not_a_page_extension_is_not_used()
        {
            GivenChapters(72, 81);
            GivenCover(Picture(30, 45, true), ".tiff");
            Vol(9).Editions.Value[0].Images.Add(new MediaCover.MediaCover { CoverType = MediaCoverTypes.Cover, Url = "http://example.test/cover.tiff" });

            var pages = Pages(Subject.Assemble(_download, _author, new List<Book> { Vol(9) }, _work).Volumes.Single().CbzPath);

            pages.Should().HaveCount(21);
            pages[0].Bytes.Should().Equal(PageBytes(72, 1));
        }

        private class RecordingAssembler : ChapterVolumeAssembler
        {
            public RecordingAssembler(IDiskProvider diskProvider, IEditionService editionService, IMapCoversToLocal mediaCoverService, ILocalizationService localizationService, NLog.Logger logger)
                : base(diskProvider, editionService, mediaCoverService, localizationService, logger)
            {
            }

            public string[] Lines { get; private set; }
            public bool Throw { get; set; }

            protected override byte[] RenderEndPage(int width, int height, string[] lines)
            {
                Lines = lines;

                if (Throw)
                {
                    throw new System.InvalidOperationException("no font");
                }

                return base.RenderEndPage(width, height, lines);
            }
        }

        private class MiscountingAssembler : ChapterVolumeAssembler
        {
            public MiscountingAssembler(IDiskProvider diskProvider, IEditionService editionService, IMapCoversToLocal mediaCoverService, ILocalizationService localizationService, NLog.Logger logger)
                : base(diskProvider, editionService, mediaCoverService, localizationService, logger)
            {
            }

            protected override int CountPages(string cbzPath)
            {
                return base.CountPages(cbzPath) - 1;
            }
        }
    }
}
