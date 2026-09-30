using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.MediaFiles;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.OrganizerTests.FileNameBuilderTests
{
    [TestFixture]

    public class FileNameBuilderFixture : CoreTest<FileNameBuilder>
    {
        private Author _author;
        private Book _book;
        private Edition _edition;
        private BookFile _trackFile;
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            LightNovelStorageMock.Setup(Mocker.GetMock<ILightNovelStorage>());

            _author = Builder<Author>
                    .CreateNew()
                    .With(s => s.Name = "Linkin Park")
                    .With(s => s.Metadata = new AuthorMetadata
                    {
                        Disambiguation = "US Rock Band",
                        Name = "Linkin Park"
                    })
                    .Build();

            var series = Builder<Series>
                .CreateNew()
                .With(x => x.Title = "Series Title")
                .Build();

            var seriesLink = Builder<SeriesBookLink>
                .CreateListOfSize(1)
                .All()
                .With(s => s.Position = "1-2")
                .With(s => s.Series = series)
                .BuildListOfNew();

            _book = Builder<Book>
                .CreateNew()
                .With(s => s.Title = "Hybrid Theory")
                .With(s => s.AuthorMetadata = _author.Metadata.Value)
                .With(s => s.SeriesLinks = seriesLink)
                .Build();

            _edition = Builder<Edition>
                .CreateNew()
                .With(s => s.Title = _book.Title)
                .With(s => s.Disambiguation = "The Best Book")
                .With(s => s.Book = _book)
                .Build();

            _namingConfig = NamingConfig.Default;
            _namingConfig.RenameBooks = true;

            Mocker.GetMock<INamingConfigService>()
                  .Setup(c => c.GetConfig()).Returns(_namingConfig);

            _trackFile = Builder<BookFile>.CreateNew()
                .With(e => e.Part = 1)
                .With(e => e.PartCount = 1)
                .With(e => e.Quality = new QualityModel(Quality.MP3))
                .With(e => e.ReleaseGroup = "ReadarrTest")
                .With(e => e.MediaInfo = new Parser.Model.MediaInfoModel
                {
                    AudioBitrate = 320,
                    AudioBits = 16,
                    AudioChannels = 2,
                    AudioFormat = "Flac Audio",
                    AudioSampleRate = 44100
                }).Build();

            Mocker.GetMock<IQualityDefinitionService>()
                .Setup(v => v.Get(Moq.It.IsAny<Quality>()))
                .Returns<Quality>(v => Quality.DefaultQualityDefinitions.First(c => c.Quality == v));

            Mocker.GetMock<ICustomFormatService>()
                  .Setup(v => v.All())
                  .Returns(new List<CustomFormat>());
        }

        private void GivenProper()
        {
            _trackFile.Quality.Revision.Version = 2;
        }

        private void GivenReal()
        {
            _trackFile.Quality.Revision.Real = 1;
        }

        [Test]
        public void should_replace_Author_space_Name()
        {
            _namingConfig.StandardBookFormat = "{Author Name}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Linkin Park");
        }

        [Test]
        public void should_replace_Author_underscore_Name()
        {
            _namingConfig.StandardBookFormat = "{Author_Name}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Linkin_Park");
        }

        [Test]
        public void should_replace_Author_dot_Name()
        {
            _namingConfig.StandardBookFormat = "{Author.Name}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Linkin.Park");
        }

        [Test]
        public void should_replace_Author_dash_Name()
        {
            _namingConfig.StandardBookFormat = "{Author-Name}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Linkin-Park");
        }

        [Test]
        public void should_replace_ARTIST_NAME_with_all_caps()
        {
            _namingConfig.StandardBookFormat = "{AUTHOR NAME}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("LINKIN PARK");
        }

        [Test]
        public void should_replace_ARTIST_NAME_with_random_casing_should_keep_original_casing()
        {
            _namingConfig.StandardBookFormat = "{aUtHoR-nAmE}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(_author.Name.Replace(' ', '-'));
        }

        [Test]
        public void should_replace_author_name_with_all_lower_case()
        {
            _namingConfig.StandardBookFormat = "{author name}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("linkin park");
        }

        [Test]
        public void should_cleanup_Author_Name()
        {
            _namingConfig.StandardBookFormat = "{Author.CleanName}";
            _author.Name = "Linkin Park (1997)";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Linkin.Park.1997");
        }

        [Test]
        public void should_replace_Author_Disambiguation()
        {
            _namingConfig.StandardBookFormat = "{Author Disambiguation}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("US Rock Band");
        }

        [Test]
        public void should_replace_edition_space_Title()
        {
            _namingConfig.StandardBookFormat = "{Book Title}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Hybrid Theory");
        }

        [Test]
        public void should_replace_Book_Disambiguation()
        {
            _namingConfig.StandardBookFormat = "{Book Disambiguation}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                .Should().Be("The Best Book");
        }

        [Test]
        public void should_replace_Book_underscore_Title()
        {
            _namingConfig.StandardBookFormat = "{Book_Title}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Hybrid_Theory");
        }

        [Test]
        public void should_replace_Book_dot_Title()
        {
            _namingConfig.StandardBookFormat = "{Book.Title}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Hybrid.Theory");
        }

        [Test]
        public void should_replace_Book_dash_Title()
        {
            _namingConfig.StandardBookFormat = "{Book-Title}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Hybrid-Theory");
        }

        [Test]
        public void should_replace_ALBUM_TITLE_with_all_caps()
        {
            _namingConfig.StandardBookFormat = "{BOOK TITLE}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("HYBRID THEORY");
        }

        [Test]
        public void should_replace_ALBUM_TITLE_with_random_casing_should_keep_original_casing()
        {
            _namingConfig.StandardBookFormat = "{bOoK-tItLE}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(_book.Title.Replace(' ', '-'));
        }

        [Test]
        public void should_replace_book_title_with_all_lower_case()
        {
            _namingConfig.StandardBookFormat = "{book title}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("hybrid theory");
        }

        [Test]
        public void should_cleanup_Book_Title()
        {
            _namingConfig.StandardBookFormat = "{Author.CleanName}";
            _author.Name = "Hybrid Theory (2000)";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Hybrid.Theory.2000");
        }

        [Test]
        public void should_set_series()
        {
            _namingConfig.StandardBookFormat = "{Book Series}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                .Should().Be("Series Title");
        }

        [Test]
        public void should_set_series_number()
        {
            _namingConfig.StandardBookFormat = "{Book SeriesPosition}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                .Should().Be("1-2");
        }

        [Test]
        public void should_set_series_title()
        {
            _namingConfig.StandardBookFormat = "{Book SeriesTitle}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                .Should().Be("Series Title #1-2");
        }

        [Test]
        public void should_set_part_number()
        {
            _namingConfig.StandardBookFormat = "{(PartNumber)}";
            _trackFile.PartCount = 2;
            _trackFile.Part = 1;

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                .Should().Be("(1)");
        }

        [Test]
        public void should_set_part_number_with_prefix()
        {
            _namingConfig.StandardBookFormat = "{(ptPartNumber)}";
            _trackFile.PartCount = 2;
            _trackFile.Part = 1;

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                .Should().Be("(pt1)");
        }

        [Test]
        public void should_set_part_number_with_format()
        {
            _namingConfig.StandardBookFormat = "{(ptPartNumber:00)}";
            _trackFile.PartCount = 2;
            _trackFile.Part = 1;

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                .Should().Be("(pt01)");
        }

        [Test]
        public void should_set_part_number_and_count_with_format()
        {
            _namingConfig.StandardBookFormat = "{(ptPartNumber:00 of PartCount:00)}";
            _trackFile.PartCount = 2;
            _trackFile.Part = 1;

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                .Should().Be("(pt01 of 02)");
        }

        [Test]
        public void should_remove_part_token_for_single_files()
        {
            _namingConfig.StandardBookFormat = "{(ptPartNumber:00 of PartCount:00)}";
            _trackFile.PartCount = 1;
            _trackFile.Part = 1;

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                .Should().Be("");
        }

        [Test]
        public void part_regex_should_not_gobble_others()
        {
            _namingConfig.StandardBookFormat = "{Book Title}{ (PartNumber)} - {Author Name}";
            _trackFile.Part = 1;
            _trackFile.PartCount = 2;

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                .Should().Be("Hybrid Theory (1) - Linkin Park");
        }

        [Test]
        public void should_replace_quality_title()
        {
            _namingConfig.StandardBookFormat = "{Quality Title}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("MP3");
        }

        [Test]
        public void should_replace_media_info_audio_codec()
        {
            _namingConfig.StandardBookFormat = "{MediaInfo AudioCodec}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("FLAC");
        }

        [Test]
        public void should_replace_media_info_audio_bitrate()
        {
            _namingConfig.StandardBookFormat = "{MediaInfo AudioBitRate}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("320 kbps");
        }

        [Test]
        public void should_replace_media_info_audio_channels()
        {
            _namingConfig.StandardBookFormat = "{MediaInfo AudioChannels}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("2.0");
        }

        [Test]
        public void should_replace_media_info_bits_per_sample()
        {
            _namingConfig.StandardBookFormat = "{MediaInfo AudioBitsPerSample}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("16bit");
        }

        [Test]
        public void should_replace_media_info_sample_rate()
        {
            _namingConfig.StandardBookFormat = "{MediaInfo AudioSampleRate}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("44.1kHz");
        }

        [Test]
        public void should_replace_all_contents_in_pattern()
        {
            _namingConfig.StandardBookFormat = "{Author Name} - {Book Title} - [{Quality Title}]";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Linkin Park - Hybrid Theory - [MP3]");
        }

        [Test]
        public void use_file_name_when_sceneName_is_null()
        {
            _namingConfig.RenameBooks = false;
            _trackFile.Path = "Linkin Park - 06 - Test";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(Path.GetFileNameWithoutExtension(_trackFile.Path));
        }

        [Test]
        public void use_file_name_when_sceneName_is_not_null()
        {
            _namingConfig.RenameBooks = false;
            _trackFile.Path = "Linkin Park - 06 - Test";
            _trackFile.SceneName = "SceneName";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(Path.GetFileNameWithoutExtension(_trackFile.Path));
        }

        [Test]
        public void use_path_when_sceneName_and_relative_path_are_null()
        {
            _namingConfig.RenameBooks = false;
            _trackFile.Path = @"C:\Test\Unsorted\Author - 01 - Test";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(Path.GetFileNameWithoutExtension(_trackFile.Path));
        }

        [Test]
        public void should_should_replace_release_group()
        {
            _namingConfig.StandardBookFormat = "{Release Group}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(_trackFile.ReleaseGroup);
        }

        [Test]
        public void should_be_able_to_use_original_title()
        {
            _author.Name = "Linkin Park";
            _namingConfig.StandardBookFormat = "{Author Name} - {Original Title}";

            _trackFile.SceneName = "Linkin.Park.Meteora.320-LOL";
            _trackFile.Path = "30 Rock - 01 - Test";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Linkin Park - Linkin.Park.Meteora.320-LOL");
        }

        [Test]
        public void should_replace_double_period_with_single_period()
        {
            _namingConfig.StandardBookFormat = "{Author.Name}.{Book.Title}";

            Subject.BuildBookFileName(new Author { Name = "In The Woods." }, new Edition { Title = "30 Rock", Book = new Book { AuthorMetadata = new AuthorMetadata { Name = "Author" }, SeriesLinks = new List<SeriesBookLink>() } }, _trackFile)
                   .Should().Be("In.The.Woods.30.Rock");
        }

        [Test]
        public void should_replace_triple_period_with_single_period()
        {
            _namingConfig.StandardBookFormat = "{Author.Name}.{Book.Title}";

            Subject.BuildBookFileName(new Author { Name = "In The Woods..." }, new Edition { Title = "30 Rock", Book = new Book { AuthorMetadata = new AuthorMetadata { Name = "Author" }, SeriesLinks = new List<SeriesBookLink>() } }, _trackFile)
                   .Should().Be("In.The.Woods.30.Rock");
        }

        [Test]
        public void should_include_affixes_if_value_not_empty()
        {
            _namingConfig.StandardBookFormat = "{Author.Name}{_Book.Title_}{Quality.Title}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Linkin.Park_Hybrid.Theory_MP3");
        }

        [Test]
        public void should_not_include_affixes_if_value_empty()
        {
            _namingConfig.StandardBookFormat = "{Author.Name}{_Book.Title_}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Linkin.Park_Hybrid.Theory");
        }

        [Test]
        public void should_remove_duplicate_non_word_characters()
        {
            _author.Name = "Venture Bros.";
            _namingConfig.StandardBookFormat = "{Author.Name}.{Book.Title}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Venture.Bros.Hybrid.Theory");
        }

        [Test]
        public void should_use_existing_filename_when_scene_name_is_not_available()
        {
            _namingConfig.RenameBooks = true;
            _namingConfig.StandardBookFormat = "{Original Title}";

            _trackFile.SceneName = null;
            _trackFile.Path = "existing.file.mkv";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(Path.GetFileNameWithoutExtension(_trackFile.Path));
        }

        [Test]
        public void should_be_able_to_use_only_original_title()
        {
            _author.Name = "30 Rock";
            _namingConfig.StandardBookFormat = "{Original Title}";

            _trackFile.SceneName = "30.Rock.S01E01.xvid-LOL";
            _trackFile.Path = "30 Rock - S01E01 - Test";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("30.Rock.S01E01.xvid-LOL");
        }

        [Test]
        public void should_not_include_quality_proper_when_release_is_not_a_proper()
        {
            _namingConfig.StandardBookFormat = "{Quality Title} {Quality Proper}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("MP3");
        }

        [Test]
        public void should_not_wrap_proper_in_square_brackets_when_not_a_proper()
        {
            _namingConfig.StandardBookFormat = "{Author Name} - {Book Title} [{Quality Title}] {[Quality Proper]}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Linkin Park - Hybrid Theory [MP3]");
        }

        [Test]
        public void should_replace_quality_full_with_quality_title_only_when_not_a_proper()
        {
            _namingConfig.StandardBookFormat = "{Author Name} - {Book Title} [{Quality Full}]";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Linkin Park - Hybrid Theory [MP3]");
        }

        [TestCase(' ')]
        [TestCase('-')]
        [TestCase('.')]
        [TestCase('_')]
        public void should_trim_extra_separators_from_end_when_quality_proper_is_not_included(char separator)
        {
            _namingConfig.StandardBookFormat = string.Format("{{Quality{0}Title}}{0}{{Quality{0}Proper}}", separator);

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("MP3");
        }

        [TestCase(' ')]
        [TestCase('-')]
        [TestCase('.')]
        [TestCase('_')]
        public void should_trim_extra_separators_from_middle_when_quality_proper_is_not_included(char separator)
        {
            _namingConfig.StandardBookFormat = string.Format("{{Quality{0}Title}}{0}{{Quality{0}Proper}}{0}{{Book{0}Title}}", separator);

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(string.Format("MP3{0}Hybrid{0}Theory", separator));
        }

        [Test]
        public void should_be_able_to_use_original_filename()
        {
            _author.Name = "30 Rock";
            _namingConfig.StandardBookFormat = "{Author Name} - {Original Filename}";

            _trackFile.SceneName = "30.Rock.S01E01.xvid-LOL";
            _trackFile.Path = "30 Rock - S01E01 - Test";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("30 Rock - 30 Rock - S01E01 - Test");
        }

        [Test]
        public void should_be_able_to_use_original_filename_only()
        {
            _author.Name = "30 Rock";
            _namingConfig.StandardBookFormat = "{Original Filename}";

            _trackFile.SceneName = "30.Rock.S01E01.xvid-LOL";
            _trackFile.Path = "30 Rock - S01E01 - Test";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("30 Rock - S01E01 - Test");
        }

        [Test]
        public void should_use_Mangarr_as_release_group_when_not_available()
        {
            _trackFile.ReleaseGroup = null;
            _namingConfig.StandardBookFormat = "{Release Group}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be("Mangarr");
        }

        [TestCase("{Book Title}{-Release Group}", "Hybrid Theory")]
        [TestCase("{Book Title}{ Release Group}", "Hybrid Theory")]
        [TestCase("{Book Title}{ [Release Group]}", "Hybrid Theory")]
        public void should_not_use_Readarr_as_release_group_if_pattern_has_separator(string pattern, string expectedFileName)
        {
            _trackFile.ReleaseGroup = null;
            _namingConfig.StandardBookFormat = pattern;

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(expectedFileName);
        }

        [TestCase("0SEC")]
        [TestCase("2HD")]
        [TestCase("IMMERSE")]
        public void should_use_existing_casing_for_release_group(string releaseGroup)
        {
            _trackFile.ReleaseGroup = releaseGroup;
            _namingConfig.StandardBookFormat = "{Release Group}";

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(releaseGroup);
        }

        // Beta polish (2026-09-28): a fresh install's default names a manga volume flat in the series
        // folder, and a light novel keeps its own "<Series> - Vol. N" folder around the same name.
        private void GivenFreshDefault(double volume)
        {
            _namingConfig = NamingConfig.Default;
            Mocker.GetMock<INamingConfigService>().Setup(c => c.GetConfig()).Returns(_namingConfig);
            _author.Path = @"C:\manga\Linkin Park".AsOsAgnostic();
            _book.VolumeNumber = volume;
        }

        [TestCase(1, "Linkin Park - Vol. 01")]
        [TestCase(12, "Linkin Park - Vol. 12")]
        [TestCase(114, "Linkin Park - Vol. 114")]
        [TestCase(3.5, "Linkin Park - Vol. 03.5")]
        public void fresh_default_names_a_manga_volume_flat_in_the_series_folder(double volume, string expected)
        {
            GivenFreshDefault(volume);
            _edition.MediaType = MediaType.Archive;

            var fileName = Subject.BuildBookFileName(_author, _edition, _trackFile);

            fileName.Should().Be(expected);
            Subject.BuildBookFilePath(_author, _edition, fileName, ".cbz")
                   .Should().Be(Path.Combine(_author.Path, expected + ".cbz"));
        }

        [Test]
        public void fresh_default_light_novel_epub_keeps_the_volume_folder()
        {
            GivenFreshDefault(8);
            _author.Metadata.Value.ForeignAuthorId = "local-linkin-park~ln";
            _edition.MediaType = MediaType.Ebook;

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(Path.Combine("Linkin Park - Vol. 8", "Linkin Park - Vol. 08"));
        }

        [Test]
        public void fresh_default_light_novel_split_audio_gets_its_part_number()
        {
            GivenFreshDefault(8);
            _author.Metadata.Value.ForeignAuthorId = "local-linkin-park~ln";
            _edition.MediaType = MediaType.Audio;
            _trackFile.Part = 2;
            _trackFile.PartCount = 3;

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(Path.Combine("Linkin Park - Vol. 8", "Linkin Park - Vol. 08 - Part 02"));
        }

        // Light novels (2026-09): one folder per volume, part numbers on split audio (D6).

        private void GivenLightNovel(MediaType editionType)
        {
            _author.Metadata.Value.ForeignAuthorId = "local-linkin-park~ln";
            _namingConfig.StandardBookFormat = "{Author Name} - Vol {Volume:000}";
            _book.VolumeNumber = 5;
            _edition.MediaType = editionType;
        }

        [Test]
        public void light_novel_epub_gets_a_folder_per_volume()
        {
            GivenLightNovel(MediaType.Ebook);

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(Path.Combine("Linkin Park - Vol. 5", "Linkin Park - Vol 005"));
        }

        [Test]
        public void light_novel_audio_with_parts_gets_a_part_number()
        {
            GivenLightNovel(MediaType.Audio);
            _trackFile.Part = 3;
            _trackFile.PartCount = 12;

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(Path.Combine("Linkin Park - Vol. 5", "Linkin Park - Vol 005 - Part 03"));
        }

        [Test]
        public void light_novel_single_file_audio_has_no_part_suffix()
        {
            GivenLightNovel(MediaType.Audio);

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(Path.Combine("Linkin Park - Vol. 5", "Linkin Park - Vol 005"));
        }

        [Test]
        public void light_novel_side_story_folder_keeps_the_fraction()
        {
            GivenLightNovel(MediaType.Ebook);
            _book.VolumeNumber = 3.5;

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(Path.Combine("Linkin Park - Vol. 3.5", "Linkin Park - Vol 003.5"));
        }

        [Test]
        public void light_novel_pattern_with_its_own_part_token_is_not_doubled()
        {
            GivenLightNovel(MediaType.Audio);
            _namingConfig.StandardBookFormat = "{Author Name} - Vol {Volume:000}{ (PartNumber)}";
            _trackFile.Part = 3;
            _trackFile.PartCount = 12;

            Subject.BuildBookFileName(_author, _edition, _trackFile)
                   .Should().Be(Path.Combine("Linkin Park - Vol. 5", "Linkin Park - Vol 005 (3)"));
        }

        [Test]
        public void light_novel_file_path_is_nested_under_the_series_folder()
        {
            GivenLightNovel(MediaType.Ebook);
            _author.Path = @"C:\lightnovels\Linkin Park".AsOsAgnostic();

            var fileName = Subject.BuildBookFileName(_author, _edition, _trackFile);

            Subject.BuildBookFilePath(_author, _edition, fileName, ".epub")
                   .Should().Be(Path.Combine(_author.Path, "Linkin Park - Vol. 5", "Linkin Park - Vol 005.epub"));
        }

        [Test]
        public void an_audio_editions_mp4_is_written_as_m4b()
        {
            // SAB sniffs some M4B posts as .mp4 (same container); the Audio leg writes them .m4b.
            GivenLightNovel(MediaType.Audio);
            _author.Path = @"C:\lightnovels\Linkin Park".AsOsAgnostic();

            Subject.BuildBookFilePath(_author, _edition, "Linkin Park - Vol 006", ".mp4")
                   .Should().Be(Path.Combine(LightNovelStorageMock.AbsRoot, "Linkin Park - Vol 006.m4b"));
        }

        // Light-novel storage (2026-09-22): an unknown Audiobookshelf folder (unmapped, ABS silent) fails
        // the import the way a missing one does -- the download stays put.
        [Test]
        public void light_novel_audio_path_fails_when_audiobookshelfs_folder_is_unknown()
        {
            GivenLightNovel(MediaType.Audio);
            Mocker.GetMock<ILightNovelStorage>().Setup(s => s.AudiobookshelfRoot()).Returns((string)null);

            Assert.Throws<RootFolderNotFoundException>(() => Subject.BuildBookFilePath(_author, _edition, "Linkin Park - Vol 005", ".m4b"));
        }

        // One copy each (2026-09-20): a light novel's audio is placed in Audiobookshelf's own tree,
        // one flat "<Series> - Vol. N" folder per audiobook under the mount; the EPUB path stays under
        // the entry (a staging name only -- calibre takes the file), and manga never leaves the entry.

        [Test]
        public void light_novel_audio_path_is_under_the_audiobooks_mount()
        {
            GivenLightNovel(MediaType.Audio);
            _author.Path = "/lightnovels/Linkin Park";

            var fileName = Subject.BuildBookFileName(_author, _edition, _trackFile);

            Subject.BuildBookFilePath(_author, _edition, fileName, ".m4b")
                   .Should().Be(Path.Combine("/srv/audiobooks", "Linkin Park - Vol. 5", "Linkin Park - Vol 005.m4b"));
        }

        // Light-novel storage (2026-09-22): a kind whose home is the entry folder imports like manga.
        [Test]
        public void light_novel_audio_with_the_entry_home_is_nested_under_the_series_folder()
        {
            GivenLightNovel(MediaType.Audio);
            Mocker.GetMock<ILightNovelStorage>().SetupGet(s => s.AudioHome).Returns(LightNovelHome.Entry);
            _author.Path = "/lightnovels/Linkin Park";

            var fileName = Subject.BuildBookFileName(_author, _edition, _trackFile);

            Subject.BuildBookFilePath(_author, _edition, fileName, ".m4b")
                   .Should().Be(Path.Combine("/lightnovels/Linkin Park", "Linkin Park - Vol. 5", "Linkin Park - Vol 005.m4b"));
            Mocker.GetMock<ILightNovelStorage>().Verify(v => v.AudiobookshelfRoot(), Times.Never());
        }

        // A rename keeps an existing file where its home is, whatever new imports would do.
        [Test]
        public void an_entry_homed_audio_file_stays_in_the_series_folder_while_new_audio_goes_to_audiobookshelf()
        {
            GivenLightNovel(MediaType.Audio);
            _author.Path = "/lightnovels/Linkin Park";

            Subject.BuildBookFilePath(_author, _edition, "Linkin Park - Vol 005", ".m4b", FileHome.Entry)
                   .Should().Be(Path.Combine("/lightnovels/Linkin Park", "Linkin Park - Vol 005.m4b"));
            Subject.BuildBookFilePath(_author, _edition, "Linkin Park - Vol 005", ".m4b", FileHome.Audiobooks)
                   .Should().Be(Path.Combine(LightNovelStorageMock.AbsRoot, "Linkin Park - Vol 005.m4b"));
        }

        [Test]
        public void light_novel_epub_path_stays_under_the_entry()
        {
            GivenLightNovel(MediaType.Ebook);
            _author.Path = "/lightnovels/Linkin Park";

            var fileName = Subject.BuildBookFileName(_author, _edition, _trackFile);

            Subject.BuildBookFilePath(_author, _edition, fileName, ".epub")
                   .Should().Be(Path.Combine("/lightnovels/Linkin Park", "Linkin Park - Vol. 5", "Linkin Park - Vol 005.epub"));
        }

        [Test]
        public void manga_path_stays_under_the_entry()
        {
            _namingConfig.StandardBookFormat = "{Author Name} - Vol {Volume:000}";
            _book.VolumeNumber = 5;
            _edition.MediaType = MediaType.Archive;
            _author.Path = "/manga/Linkin Park";

            var fileName = Subject.BuildBookFileName(_author, _edition, _trackFile);

            Subject.BuildBookFilePath(_author, _edition, fileName, ".cbz")
                   .Should().Be(Path.Combine("/manga/Linkin Park", "Linkin Park - Vol 005.cbz"));
        }

        [Test]
        public void an_ebook_editions_extension_is_untouched()
        {
            GivenLightNovel(MediaType.Ebook);
            _author.Path = @"C:\lightnovels\Linkin Park".AsOsAgnostic();

            Subject.BuildBookFilePath(_author, _edition, "Linkin Park - Vol 006", ".epub")
                   .Should().Be(Path.Combine(_author.Path, "Linkin Park - Vol 006.epub"));
            Subject.BuildBookFilePath(_author, _edition, "Linkin Park - Vol 006", ".mp4")
                   .Should().Be(Path.Combine(_author.Path, "Linkin Park - Vol 006.mp4"));
        }

        [Test]
        public void manga_keeps_the_flat_name_even_with_parts()
        {
            _namingConfig.StandardBookFormat = "{Author Name} - Vol {Volume:000}";
            _book.VolumeNumber = 5;
            _trackFile.Part = 3;
            _trackFile.PartCount = 12;

            Subject.BuildBookFileName(_author, _edition, _trackFile).Should().Be("Linkin Park - Vol 005");
        }
    }
}
