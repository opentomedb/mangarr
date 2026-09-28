using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.AutoMoq;

namespace NzbDrone.Core.Test.MediaFiles.AudioTagServiceFixture
{
    [TestFixture]
    public class AudioTagServiceFixture : CoreTest<AudioTagService>
    {
        public static class TestCaseFactory
        {
            private static readonly string[] MediaFiles = new[] { "nin.mp2", "nin.mp3", "nin.flac", "nin.m4a", "nin.wma", "nin.ape", "nin.opus" };

            // Additive (2026-09-18, D4) and AdditiveAuthors (2026-09-20) are a write mode, not a
            // tag: never read from a file.
            private static readonly string[] SkipProperties = new[] { "IsValid", "Duration", "Quality", "MediaInfo", "ImageFile", "BookAuthors", "Additive", "AdditiveAuthors" };
            private static readonly Dictionary<string, string[]> SkipPropertiesByFile = new Dictionary<string, string[]>
            {
                { "nin.mp2", new[] { "OriginalReleaseDate" } },

                // Series / SeriesPart (2026-09-18): written to ID3 (TXXX), Apple (dash box), Xiph and
                // ASF; the APE branch has no writer for them (.ape is not an importable extension).
                { "nin.ape", new[] { "Series", "SeriesPart" } }
            };

            public static IEnumerable TestCases
            {
                get
                {
                    foreach (var file in MediaFiles)
                    {
                        var toSkip = SkipProperties;
                        if (SkipPropertiesByFile.ContainsKey(file))
                        {
                            toSkip = toSkip.Union(SkipPropertiesByFile[file]).ToArray();
                        }

                        yield return new TestCaseData(file, toSkip).SetName($"{{m}}_{file.Replace("nin.", "")}");
                    }
                }
            }
        }

        private readonly string _testdir = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media");
        private string _copiedFile;
        private AudioTag _testTags;
        private IDiskProvider _diskProvider;
        private MemoryTarget _log;

        [SetUp]
        public void Setup()
        {
            _diskProvider = Mocker.Resolve<IDiskProvider>(FileSystemType.Actual);

            Mocker.GetMock<IConfigService>()
                .Setup(x => x.WriteAudioTags)
                .Returns(WriteAudioTagsType.Sync);

            var imageFile = Path.Combine(_testdir, "nin.png");
            var imageSize = _diskProvider.GetFileSize(imageFile);

            // have to manually set the arrays of string parameters and integers to values > 1
            _testTags = Builder<AudioTag>.CreateNew()
                .With(x => x.Track = 2)
                .With(x => x.TrackCount = 33)
                .With(x => x.Disc = 44)
                .With(x => x.DiscCount = 55)
                .With(x => x.Date = new DateTime(2019, 3, 1))
                .With(x => x.Year = 2019)
                .With(x => x.OriginalReleaseDate = new DateTime(2009, 4, 1))
                .With(x => x.OriginalYear = 2009)
                .With(x => x.Performers = new[] { "Performer1" })
                .With(x => x.BookAuthors = new[] { "방탄소년단" })
                .With(x => x.Genres = new[] { "Genre1", "Genre2" })
                .With(x => x.Series = "Sword Art Online")
                .With(x => x.SeriesPart = "21")
                .With(x => x.ImageFile = imageFile)
                .With(x => x.ImageSize = imageSize)
                .Build();

            _log = new MemoryTarget("audio-tag-service") { Layout = "${level}|${message}" };
            LogManager.Configuration.AddTarget(_log.Name, _log);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, _log));
            LogManager.ReconfigExistingLoggers();
        }

        [TearDown]
        public void Cleanup()
        {
            if (File.Exists(_copiedFile))
            {
                File.Delete(_copiedFile);
            }

            foreach (var rule in LogManager.Configuration.LoggingRules.Where(r => r.Targets.Contains(_log)).ToList())
            {
                LogManager.Configuration.LoggingRules.Remove(rule);
            }

            LogManager.Configuration.RemoveTarget(_log.Name);
            LogManager.ReconfigExistingLoggers();
        }

        private void GivenFileCopy(string filename)
        {
            var original = Path.Combine(_testdir, filename);
            var tempname = $"temp_{Path.GetRandomFileName()}{Path.GetExtension(filename)}";
            _copiedFile = Path.Combine(_testdir, tempname);

            File.Copy(original, _copiedFile);
        }

        private void VerifyDifferent(AudioTag a, AudioTag b, string[] skipProperties)
        {
            foreach (var property in typeof(AudioTag).GetProperties())
            {
                if (skipProperties.Contains(property.Name))
                {
                    continue;
                }

                if (property.CanRead)
                {
                    if (property.PropertyType.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEquatable<>)) ||
                        Nullable.GetUnderlyingType(property.PropertyType) != null)
                    {
                        var val1 = property.GetValue(a, null);
                        var val2 = property.GetValue(b, null);
                        val1.Should().NotBe(val2, $"{property.Name} should not be equal.  Found {val1.NullSafe()} for both tags");
                    }
                    else if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                    {
                        var val1 = (IEnumerable)property.GetValue(a, null);
                        var val2 = (IEnumerable)property.GetValue(b, null);

                        if (val1 != null && val2 != null)
                        {
                            val1.Should().NotBeEquivalentTo(val2, $"{property.Name} should not be equal");
                        }
                    }
                }
            }
        }

        private void VerifySame(AudioTag a, AudioTag b, string[] skipProperties)
        {
            foreach (var property in typeof(AudioTag).GetProperties())
            {
                if (skipProperties.Contains(property.Name))
                {
                    continue;
                }

                if (property.CanRead)
                {
                    if (property.PropertyType.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEquatable<>)) ||
                        Nullable.GetUnderlyingType(property.PropertyType) != null)
                    {
                        var val1 = property.GetValue(a, null);
                        var val2 = property.GetValue(b, null);
                        val1.Should().Be(val2, $"{property.Name} should be equal");
                    }
                    else if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                    {
                        var val1 = (IEnumerable)property.GetValue(a, null);
                        var val2 = (IEnumerable)property.GetValue(b, null);

                        if (val1 != null || val2 != null)
                        {
                            val1.Should().BeEquivalentTo(val2, $"{property.Name} should be equal");
                        }
                    }
                }
            }
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_read_duration(string filename, string[] ignored)
        {
            var path = Path.Combine(_testdir, filename);

            var tags = Subject.ReadTags(path);

            tags.Duration.Should().BeCloseTo(new TimeSpan(0, 0, 1, 25, 130), 100);
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_read_write_tags(string filename, string[] skipProperties)
        {
            GivenFileCopy(filename);
            var path = _copiedFile;

            var initialtags = Subject.ReadAudioTag(path);

            VerifyDifferent(initialtags, _testTags, skipProperties);

            _testTags.Write(path);

            var writtentags = Subject.ReadAudioTag(path);

            VerifySame(writtentags, _testTags, skipProperties);
            writtentags.BookAuthors.Should().BeEquivalentTo(
                _testTags.BookAuthors.Concat(_testTags.Performers),
                options => options.WithStrictOrdering());
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_read_audiotag_from_file_with_no_tags(string filename, string[] skipProperties)
        {
            GivenFileCopy(filename);
            var path = _copiedFile;

            Subject.RemoveAllTags(path);

            var tag = Subject.ReadAudioTag(path);
            var expected = new AudioTag()
            {
                Performers = new string[0],
                BookAuthors = new string[0],
                Genres = new string[0]
            };

            VerifySame(tag, expected, skipProperties);
            tag.Quality.Should().NotBeNull();
            tag.MediaInfo.Should().NotBeNull();
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_read_parsedtrackinfo_from_file_with_no_tags(string filename, string[] skipProperties)
        {
            GivenFileCopy(filename);
            var path = _copiedFile;

            Subject.RemoveAllTags(path);

            var tag = Subject.ReadTags(path);

            tag.Quality.Should().NotBeNull();
            tag.MediaInfo.Should().NotBeNull();
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_set_quality_and_mediainfo_for_corrupt_file(string filename, string[] skipProperties)
        {
            // use missing to simulate corrupt
            var tag = Subject.ReadAudioTag(filename.Replace("nin", "missing"));
            var expected = new AudioTag();

            VerifySame(tag, expected, skipProperties);
            tag.Quality.Should().NotBeNull();
            tag.MediaInfo.Should().NotBeNull();

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_read_file_with_only_title_tag(string filename, string[] ignored)
        {
            GivenFileCopy(filename);
            var path = _copiedFile;

            Subject.RemoveAllTags(path);

            var nametag = new AudioTag();
            nametag.Title = "test";
            nametag.Write(path);

            var tag = Subject.ReadTags(path);
            tag.Title.Should().Be("test");

            tag.Quality.Should().NotBeNull();
            tag.MediaInfo.Should().NotBeNull();
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_remove_date_from_tags_when_not_in_metadata(string filename, string[] ignored)
        {
            GivenFileCopy(filename);
            var path = _copiedFile;

            _testTags.Write(path);

            _testTags.Date = null;
            _testTags.OriginalReleaseDate = null;

            _testTags.Write(path);

            var onDisk = Subject.ReadAudioTag(path);

            onDisk.Date.HasValue.Should().BeFalse();
            onDisk.OriginalReleaseDate.HasValue.Should().BeFalse();
        }

        [Test]
        public void should_ignore_non_parsable_id3v23_date()
        {
            GivenFileCopy("nin.mp2");

            using (var file = TagLib.File.Create(_copiedFile))
            {
                var id3tag = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
                id3tag.SetTextFrame("TORY", "0");
                file.Save();
            }

            var tag = Subject.ReadAudioTag(_copiedFile);
            tag.OriginalReleaseDate.HasValue.Should().BeFalse();
        }

        private BookFile GivenPopulatedTrackfile(int mediumOffset)
        {
            var meta = Builder<AuthorMetadata>.CreateNew().Build();
            var author = Builder<Author>.CreateNew()
                .With(x => x.Metadata = meta)
                .Build();

            var book = Builder<Book>.CreateNew()
                .With(x => x.Author = author)
                .Build();

            var edition = Builder<Edition>.CreateNew()
                .With(x => x.Book = book)
                .Build();

            var file = Builder<BookFile>.CreateNew()
                .With(x => x.Edition = edition)
                .With(x => x.Author = author)
                .Build();

            edition.BookFiles = new List<BookFile> { file };

            return file;
        }

        [Test]
        public void get_metadata_should_not_fail_with_missing_country()
        {
            var file = GivenPopulatedTrackfile(0);
            var tag = Subject.GetTrackMetadata(file);
        }

        [Test]
        public void should_not_fail_if_media_has_been_omitted()
        {
            GivenFileCopy("nin.mp3");

            var file = GivenPopulatedTrackfile(100);
            file.Path = _copiedFile;

            Assert.DoesNotThrow(() => Subject.GetTrackMetadata(file));
        }

        [TestCase("nin.mp3")]
        public void write_tags_should_update_trackfile_size_and_modified(string filename)
        {
            Mocker.GetMock<IConfigService>()
                .Setup(x => x.ScrubAudioTags)
                .Returns(true);

            GivenFileCopy(filename);

            var file = GivenPopulatedTrackfile(0);

            file.Path = _copiedFile;
            Subject.WriteTags(file, false, true);

            var fileInfo = _diskProvider.GetFileInfo(file.Path);
            file.Modified.Should().Be(fileInfo.LastWriteTimeUtc);
            file.Size.Should().Be(fileInfo.Length);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Once());
        }

        [TestCase("nin.mp3")]
        public void write_tags_should_not_update_tags_if_already_updated(string filename)
        {
            Mocker.GetMock<IConfigService>()
                .Setup(x => x.ScrubAudioTags)
                .Returns(true);

            GivenFileCopy(filename);

            var file = GivenPopulatedTrackfile(0);

            file.Path = _copiedFile;
            Subject.WriteTags(file, false, true);
            Subject.WriteTags(file, false, true);
            Subject.WriteTags(file, false, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Once());
        }

        [Test]
        public void write_tags_is_a_no_op_when_write_audio_tags_is_no()
        {
            // Light novels (2026-09): audio files import now. The setting defaults to No and the
            // import path never passes force, so an audiobook is never rewritten unless the maintainer
            // turns the setting on.
            Mocker.GetMock<IConfigService>()
                .Setup(x => x.WriteAudioTags)
                .Returns(WriteAudioTagsType.No);

            GivenFileCopy("nin.mp3");

            Subject.WriteTags(new BookFile { Path = _copiedFile }, true);

            // the gate returns before the file is read, diffed or written
            Mocker.GetMock<IRootFolderWatchingService>().Verify(v => v.ReportFileSystemChangeBeginning(It.IsAny<string[]>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Never());
        }

        [Test]
        public void should_not_fail_reading_metadata_with_dates_omitted()
        {
            GivenFileCopy("nin.mp3");

            var bookFile = GivenPopulatedTrackfile(0);
            bookFile.Path = _copiedFile;
            bookFile.Edition.Value.ReleaseDate = null;
            bookFile.Edition.Value.Book.Value.ReleaseDate = null;

            Assert.DoesNotThrow(() => Subject.GetTrackMetadata(bookFile));
        }

        // Light-novel audio tags (2026-09-18): the Audio edition of a light-novel volume is tagged
        // for Audiobookshelf -- album (what ABS reads as the book title) = the Audible product
        // title (AudiobookTitle, falling back to the edition title), Title = the same value, plus
        // SERIES / SERIES-PART. Everything else (manga, EPUB) keeps Readarr's tags byte-identical.
        private BookFile GivenTrackfile(string authorName, string foreignAuthorId, MediaType mediaType, string audiobookTitle, double volumeNumber, int part, int partCount, string audiobookSubtitle = null)
        {
            // One copy each (2026-09-20): no writer, not adopted, no opt-in unless a case says so
            // (NBuilder would otherwise fill Writer and alternate the bools)
            var meta = Builder<AuthorMetadata>.CreateNew()
                .With(x => x.Name = authorName)
                .With(x => x.ForeignAuthorId = foreignAuthorId)
                .With(x => x.Writer = null)
                .Build();

            var author = Builder<Author>.CreateNew()
                .With(x => x.Name = authorName)
                .With(x => x.Metadata = meta)
                .With(x => x.AdoptedTagWrite = false)
                .Build();

            var book = Builder<Book>.CreateNew()
                .With(x => x.Title = $"{authorName}, Vol. {volumeNumber}")
                .With(x => x.VolumeNumber = volumeNumber)
                .With(x => x.Author = author)
                .Build();

            var edition = Builder<Edition>.CreateNew()
                .With(x => x.Title = $"{authorName}, Vol. {volumeNumber}")
                .With(x => x.MediaType = mediaType)
                .With(x => x.AudiobookTitle = audiobookTitle)
                .With(x => x.AudiobookSubtitle = audiobookSubtitle)
                .With(x => x.Book = book)
                .Build();

            var files = Builder<BookFile>.CreateListOfSize(partCount)
                .All()
                .With(x => x.Edition = edition)
                .With(x => x.Author = author)
                .With(x => x.Home = FileHome.Entry)
                .With(x => x.Adopted = false)
                .Build()
                .ToList();

            edition.BookFiles = files;

            var file = files[part - 1];
            file.Part = part;
            file.Path = _copiedFile;

            return file;
        }

        // a single-file edition by default: the title is the album's; multi-part editions keep
        // their per-file titles (pinned below)
        private BookFile GivenLightNovelAudioFile(string audiobookTitle, double volumeNumber = 1, int part = 1, int partCount = 1, string audiobookSubtitle = null)
        {
            return GivenTrackfile("The Beginning After the End", "local-the-beginning-after-the-end~ln", MediaType.Audio, audiobookTitle, volumeNumber, part, partCount, audiobookSubtitle);
        }

        [Test]
        public void get_metadata_should_use_audiobook_title_and_series_for_light_novel_audio()
        {
            GivenFileCopy("nin.m4a");

            var file = GivenLightNovelAudioFile("Early Years");

            var tag = Subject.GetTrackMetadata(file);

            tag.Title.Should().Be("Early Years");
            tag.Book.Should().Be("Early Years");
            tag.Series.Should().Be("The Beginning After the End");
            tag.SeriesPart.Should().Be("1");

            // D4: an additive tag; its numbers are the file's, not the part index (pinned below)
            tag.Additive.Should().BeTrue();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void get_metadata_should_fall_back_to_edition_title_for_light_novel_audio_without_audiobook_title(string audiobookTitle)
        {
            GivenFileCopy("nin.m4a");

            var file = GivenLightNovelAudioFile(audiobookTitle);

            var tag = Subject.GetTrackMetadata(file);

            tag.Title.Should().Be(file.Edition.Value.Title);
            tag.Book.Should().Be(file.Edition.Value.Title);
            tag.Series.Should().Be("The Beginning After the End");
            tag.SeriesPart.Should().Be("1");
        }

        [Test]
        public void get_metadata_should_format_fractional_volume_as_series_part()
        {
            GivenFileCopy("nin.m4a");

            var file = GivenLightNovelAudioFile("Early Years", volumeNumber: 5.5);

            var tag = Subject.GetTrackMetadata(file);

            tag.SeriesPart.Should().Be("5.5");
        }

        [Test]
        public void get_metadata_should_not_write_series_part_for_unnumbered_volume()
        {
            GivenFileCopy("nin.m4a");

            // an unnumbered row must not sort as #0 in Audiobookshelf
            var file = GivenLightNovelAudioFile("Early Years", volumeNumber: 0);

            var tag = Subject.GetTrackMetadata(file);

            tag.Series.Should().Be("The Beginning After the End");
            tag.SeriesPart.Should().BeNull();
        }

        // Task 8 (2026-09-18): Audible splits some product names into title + subtitle ("Mushoku
        // Tensei" / "Jobless Reincarnation (Light Novel), Vol. 1"; "Sword Art Online 6" / "Phantom
        // Bullet"); the audiobook's name is both, as the product page shows it, so the album (what
        // ABS reads as the book title) does not lose the volume. A subtitle that only names the
        // series or an edition label adds nothing, and one the title already carries is not repeated.
        [Test]
        public void get_metadata_should_compose_the_audible_title_and_subtitle_for_light_novel_audio()
        {
            GivenFileCopy("nin.m4a");

            var file = GivenTrackfile("Mushoku Tensei", "local-mushoku-tensei~ln", MediaType.Audio, "Mushoku Tensei", 1, 1, 1, "Jobless Reincarnation (Light Novel), Vol. 1");

            var tag = Subject.GetTrackMetadata(file);

            tag.Title.Should().Be("Mushoku Tensei: Jobless Reincarnation (Light Novel), Vol. 1");
            tag.Book.Should().Be("Mushoku Tensei: Jobless Reincarnation (Light Novel), Vol. 1");
        }

        [TestCase("Light Novel")]
        [TestCase("Konosuba: God's Blessing on This Wonderful World!")]
        public void get_metadata_should_not_append_a_subtitle_that_is_only_an_edition_label_or_the_series(string subtitle)
        {
            GivenFileCopy("nin.m4a");

            var file = GivenTrackfile("Konosuba: God's Blessing on This Wonderful World!", "local-konosuba~ln", MediaType.Audio, "Konosuba: God's Blessing on This Wonderful World!, Vol. 8", 8, 1, 1, subtitle);

            var tag = Subject.GetTrackMetadata(file);

            tag.Title.Should().Be("Konosuba: God's Blessing on This Wonderful World!, Vol. 8");
            tag.Book.Should().Be("Konosuba: God's Blessing on This Wonderful World!, Vol. 8");
        }

        [TestCase("Aincrad")]
        [TestCase("aincrad")]
        public void get_metadata_should_not_repeat_a_subtitle_the_audible_title_already_carries(string subtitle)
        {
            GivenFileCopy("nin.m4a");

            var file = GivenTrackfile("Sword Art Online", "local-sword-art-online~ln", MediaType.Audio, "Sword Art Online 1: Aincrad", 1, 1, 1, subtitle);

            var tag = Subject.GetTrackMetadata(file);

            tag.Title.Should().Be("Sword Art Online 1: Aincrad");
            tag.Book.Should().Be("Sword Art Online 1: Aincrad");
        }

        [Test]
        public void get_metadata_should_ignore_the_subtitle_when_there_is_no_audiobook_title()
        {
            GivenFileCopy("nin.m4a");

            var file = GivenTrackfile("Mushoku Tensei", "local-mushoku-tensei~ln", MediaType.Audio, null, 1, 1, 1, "Jobless Reincarnation (Light Novel), Vol. 1");

            var tag = Subject.GetTrackMetadata(file);

            tag.Title.Should().Be(file.Edition.Value.Title);
            tag.Book.Should().Be(file.Edition.Value.Title);
        }

        [Test]
        public void get_metadata_should_leave_manga_archive_tags_unchanged()
        {
            GivenFileCopy("nin.m4a");

            // AudiobookTitle is set on purpose: the manga path must not look at it.
            var file = GivenTrackfile("Dandadan", "local-dandadan", MediaType.Archive, "Not An Audiobook", 1, 3, 10);

            var tag = Subject.GetTrackMetadata(file);

            tag.Title.Should().Be(file.Edition.Value.Title);
            tag.Book.Should().Be(file.Edition.Value.Book.Value.Title);
            tag.Series.Should().BeNull();
            tag.SeriesPart.Should().BeNull();
            tag.Track.Should().Be(3);
            tag.TrackCount.Should().Be(10);
            tag.Additive.Should().BeFalse();
            tag.AdditiveAuthors.Should().BeNull();
            tag.Performers.Should().BeEquivalentTo(new[] { "Dandadan" });
        }

        [Test]
        public void get_metadata_should_leave_light_novel_ebook_tags_unchanged()
        {
            GivenFileCopy("nin.m4a");

            var file = GivenTrackfile("The Beginning After the End", "local-the-beginning-after-the-end~ln", MediaType.Ebook, "Early Years", 1, 1, 1);

            var tag = Subject.GetTrackMetadata(file);

            tag.Title.Should().Be(file.Edition.Value.Title);
            tag.Book.Should().Be(file.Edition.Value.Book.Value.Title);
            tag.Series.Should().BeNull();
            tag.SeriesPart.Should().BeNull();
            tag.Additive.Should().BeFalse();
            tag.Performers.Should().BeEquivalentTo(new[] { "The Beginning After the End" });
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void write_should_not_create_series_tags_when_null(string filename, string[] ignored)
        {
            GivenFileCopy(filename);
            var path = _copiedFile;

            Subject.RemoveAllTags(path);

            // a manga / ebook tag set: Series and SeriesPart are null
            var nametag = new AudioTag();
            nametag.Title = "test";
            nametag.Write(path);

            var tag = Subject.ReadAudioTag(path);

            tag.Series.Should().BeNull();
            tag.SeriesPart.Should().BeNull();
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void should_remove_series_from_tags_when_not_in_metadata(string filename, string[] skipProperties)
        {
            if (skipProperties.Contains("Series"))
            {
                Assert.Ignore("Series is not written for this format");
            }

            GivenFileCopy(filename);
            var path = _copiedFile;

            _testTags.Write(path);

            _testTags.Series = null;
            _testTags.SeriesPart = null;

            _testTags.Write(path);

            var onDisk = Subject.ReadAudioTag(path);

            onDisk.Series.Should().BeNull();
            onDisk.SeriesPart.Should().BeNull();
        }

        [Test]
        public void should_adopt_existing_lowercase_series_frame_on_id3()
        {
            GivenFileCopy("nin.mp3");

            // a TXXX frame another tool wrote with a lowercase description
            using (var file = TagLib.File.Create(_copiedFile))
            {
                var id3tag = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
                var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3tag, "series", true);
                frame.Text = new[] { "Overlord" };
                file.Save();
            }

            Subject.ReadAudioTag(_copiedFile).Series.Should().Be("Overlord");

            _testTags.Write(_copiedFile);

            using (var file = TagLib.File.Create(_copiedFile))
            {
                var id3tag = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
                var frames = id3tag.GetFrames<TagLib.Id3v2.UserTextInformationFrame>()
                    .Where(x => string.Equals(x.Description, "SERIES", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // adopted, not duplicated
                frames.Should().HaveCount(1);
                frames[0].Text.Should().BeEquivalentTo(new[] { "Sword Art Online" });
            }
        }

        [Test]
        public void diff_should_detect_series_changes()
        {
            var other = new AudioTag
            {
                Title = _testTags.Title,
                Performers = _testTags.Performers,
                BookAuthors = _testTags.BookAuthors,
                Track = _testTags.Track,
                TrackCount = _testTags.TrackCount,
                Book = _testTags.Book,
                Disc = _testTags.Disc,
                DiscCount = _testTags.DiscCount,
                Media = _testTags.Media,
                Date = _testTags.Date,
                OriginalReleaseDate = _testTags.OriginalReleaseDate,
                Publisher = _testTags.Publisher,
                Genres = _testTags.Genres,
                ImageSize = _testTags.ImageSize,
                Series = "Sword Art Online: Progressive",
                SeriesPart = "1"
            };

            var diff = _testTags.Diff(other);

            diff.Should().HaveCount(2);
            diff.Should().ContainKey("Series");
            diff["Series"].Should().Be(Tuple.Create("Sword Art Online", "Sword Art Online: Progressive"));
            diff.Should().ContainKey("Series Part");
            diff["Series Part"].Should().Be(Tuple.Create("21", "1"));
        }

        // Additive tag write (2026-09-18, D4): the Audio edition of a light-novel volume writes only
        // title, album, SERIES and SERIES-PART. Performers, album artists, track/disc numbers,
        // publisher, genres, dates, media and the embedded picture stay exactly as the file has
        // them, the diff looks only at the four written fields, and a scrub never runs first.
        // Manga / EPUB: the full write, byte-identical.
        [Test]
        public void get_metadata_should_carry_the_file_values_for_every_unwritten_field_of_light_novel_audio()
        {
            GivenFileCopy("nin.m4a");

            // a pre-tagged audiobook: artist, label, genres, numbers and dates are the file's;
            // the picture stays whatever nin.m4a shipped with
            _testTags.ImageFile = null;
            _testTags.Write(_copiedFile);
            var onDisk = Subject.ReadAudioTag(_copiedFile);

            var file = GivenLightNovelAudioFile("Early Years");

            // the app holds a cover for the volume: a full write would embed it
            file.Edition.Value.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://covers/early-years.png"));
            Mocker.GetMock<IMapCoversToLocal>()
                .Setup(x => x.GetCoverPath(It.IsAny<int>(), MediaCoverEntity.Book, MediaCoverTypes.Cover, ".png", null))
                .Returns(Path.Combine(_testdir, "nin.png"));

            var tag = Subject.GetTrackMetadata(file);

            tag.Additive.Should().BeTrue();
            tag.Title.Should().Be("Early Years");
            tag.Book.Should().Be("Early Years");
            tag.Series.Should().Be("The Beginning After the End");
            tag.SeriesPart.Should().Be("1");

            tag.Performers.Should().BeEquivalentTo(new[] { "Performer1" });
            tag.BookAuthors.Should().BeEquivalentTo(onDisk.BookAuthors, options => options.WithStrictOrdering());
            tag.Track.Should().Be(2);
            tag.TrackCount.Should().Be(33);
            tag.Disc.Should().Be(44);
            tag.DiscCount.Should().Be(55);
            tag.Publisher.Should().Be(_testTags.Publisher);
            tag.Genres.Should().BeEquivalentTo(new[] { "Genre1", "Genre2" });
            tag.Date.Should().Be(new DateTime(2019, 3, 1));
            tag.Year.Should().Be(2019);
            tag.OriginalReleaseDate.Should().Be(new DateTime(2009, 4, 1));
            tag.OriginalYear.Should().Be(2009);
            tag.Media.Should().Be(_testTags.Media);
            tag.ImageFile.Should().BeNull();
            tag.ImageSize.Should().Be(onDisk.ImageSize);
            tag.ImageSize.Should().NotBe(_diskProvider.GetFileSize(Path.Combine(_testdir, "nin.png")));

            // the file's picture is never touched, so the cover is never even looked up
            Mocker.GetMock<IMapCoversToLocal>()
                .Verify(x => x.GetCoverPath(It.IsAny<int>(), It.IsAny<MediaCoverEntity>(), It.IsAny<MediaCoverTypes>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Never());
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void additive_write_should_change_only_title_album_and_series(string filename, string[] skipProperties)
        {
            if (skipProperties.Contains("Series"))
            {
                Assert.Ignore("Series is not written for this format");
            }

            GivenFileCopy(filename);
            var path = _copiedFile;

            // a Yen Audio-style file: fully tagged, cover embedded
            _testTags.Performers = new[] { "Original Artist" };
            _testTags.BookAuthors = new[] { "Original Artist" };
            _testTags.Genres = new[] { "Audiobook" };
            _testTags.Publisher = "Yen Audio";
            _testTags.Write(path);

            var before = Subject.ReadAudioTag(path);
            before.Performers.Should().BeEquivalentTo(new[] { "Original Artist" });
            before.ImageSize.Should().Be(_testTags.ImageSize);

            var additive = new AudioTag
            {
                Additive = true,
                Title = "Sword Art Online: Progressive 8",
                Book = "Sword Art Online: Progressive 8",
                Series = "Sword Art Online: Progressive",
                SeriesPart = "8"
            };

            additive.Write(path);

            var after = Subject.ReadAudioTag(path);

            after.Title.Should().Be("Sword Art Online: Progressive 8");
            after.Book.Should().Be("Sword Art Online: Progressive 8");
            after.Series.Should().Be("Sword Art Online: Progressive");
            after.SeriesPart.Should().Be("8");

            // everything else is exactly as the file had it
            VerifySame(after, before, skipProperties.Union(new[] { "Title", "Book", "Series", "SeriesPart" }).ToArray());
            after.BookAuthors.Should().BeEquivalentTo(before.BookAuthors, options => options.WithStrictOrdering());
            after.Performers.Should().BeEquivalentTo(new[] { "Original Artist" });
            after.Genres.Should().BeEquivalentTo(new[] { "Audiobook" });
            after.Publisher.Should().Be("Yen Audio");
            after.Date.Should().Be(new DateTime(2019, 3, 1));
            after.ImageSize.Should().Be(_testTags.ImageSize);
        }

        [Test]
        public void diff_of_two_additive_tags_should_compare_only_the_written_fields()
        {
            var a = new AudioTag
            {
                Additive = true,
                Title = "Sword Art Online: Progressive 8",
                Book = "Sword Art Online: Progressive 8",
                Series = "Sword Art Online: Progressive",
                SeriesPart = "8",
                Performers = new[] { "Original Artist" },
                Genres = new[] { "Audiobook" },
                ImageSize = 74760
            };

            var b = new AudioTag
            {
                Additive = true,
                Title = "Sword Art Online: Progressive 8",
                Book = "Sword Art Online: Progressive 8",
                Series = "Sword Art Online: Progressive",
                SeriesPart = "8",
                Performers = new[] { "Sword Art Online: Progressive" },
                Genres = new string[0],
                ImageSize = 0
            };

            a.Diff(b).Should().BeEmpty();

            b.SeriesPart = "9";

            var diff = a.Diff(b);

            diff.Should().HaveCount(1);
            diff["Series Part"].Should().Be(Tuple.Create("8", "9"));
        }

        [Test]
        public void diff_should_be_additive_when_only_the_generated_tag_is_additive()
        {
            GivenFileCopy("nin.mp3");

            _testTags.Write(_copiedFile);

            // production orientation: the tag read from the file (never additive) diffed against
            // the generated one, which carries none of the file's other values
            var fileTags = Subject.ReadAudioTag(_copiedFile);
            var generated = new AudioTag
            {
                Additive = true,
                Title = fileTags.Title,
                Book = fileTags.Book,
                Series = fileTags.Series,
                SeriesPart = fileTags.SeriesPart,
                Performers = new[] { "Sword Art Online" },
                BookAuthors = new[] { "Sword Art Online" },
                Genres = new string[0],
                ImageSize = 0
            };

            fileTags.Diff(generated).Should().BeEmpty();

            generated.Book = "Sword Art Online 21";

            var diff = fileTags.Diff(generated);

            diff.Should().HaveCount(1);
            diff["Book"].Should().Be(Tuple.Create(fileTags.Book, "Sword Art Online 21"));
        }

        [Test]
        public void write_tags_should_not_scrub_before_an_additive_write()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(x => x.ScrubAudioTags)
                .Returns(true);

            GivenFileCopy("nin.m4a");

            _testTags.Performers = new[] { "Original Artist" };
            _testTags.BookAuthors = new[] { "Original Artist" };
            _testTags.Write(_copiedFile);

            var file = GivenLightNovelAudioFile("Early Years");

            Subject.WriteTags(file, false, true);

            var onDisk = Subject.ReadAudioTag(_copiedFile);

            // the title and series are the app's; the artist and cover are still the file's
            onDisk.Title.Should().Be("Early Years");
            onDisk.Book.Should().Be("Early Years");
            onDisk.Series.Should().Be("The Beginning After the End");
            onDisk.Performers.Should().BeEquivalentTo(new[] { "Original Artist" });
            onDisk.ImageSize.Should().Be(_testTags.ImageSize);

            _log.Logs.Should().Contain(l => l.EndsWith("additive tag write, scrub skipped"));
            _log.Logs.Should().NotContain(l => l.StartsWith("Debug|Scrubbing tags for"));

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookFileRetaggedEvent>(e => !e.Scrubbed)), Times.Once());

            // the file now matches: a second pass diffs nothing and is not rewritten
            Subject.WriteTags(file, false, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Once());
        }

        [Test]
        public void retag_preview_should_list_only_the_written_fields_for_light_novel_audio()
        {
            GivenFileCopy("nin.m4a");

            // a Yen Audio-style file that already carries the right series
            _testTags.Performers = new[] { "Original Artist" };
            _testTags.BookAuthors = new[] { "Original Artist" };
            _testTags.Publisher = "Yen Audio";
            _testTags.Series = "The Beginning After the End";
            _testTags.SeriesPart = "1";
            _testTags.Write(_copiedFile);

            var file = GivenLightNovelAudioFile("Early Years");
            var bookId = file.Edition.Value.Book.Value.Id;

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(bookId))
                .Returns(new List<BookFile> { file });

            var previews = Subject.GetRetagPreviewsByBook(bookId);

            // the staging preview listed Author / Book Author / Label / Genres / Track Count /
            // Image Size rows for a file like this; now only what the additive write changes
            previews.Should().HaveCount(1);
            previews[0].Changes.Keys.Should().BeEquivalentTo(new[] { "Title", "Book" });

            // once written the file matches and drops out of the preview
            Subject.WriteTags(file, false, true);

            Subject.GetRetagPreviewsByBook(bookId).Should().BeEmpty();
        }

        // Fix round 0 (2026-09-18): a multi-part audiobook's per-file title tags are the chapter
        // titles Audiobookshelf reads, so the additive write sets title only on a single-file
        // edition; album / SERIES / SERIES-PART go on every part.
        [Test]
        public void get_metadata_should_write_the_title_only_for_a_single_file_edition()
        {
            GivenFileCopy("nin.m4a");

            _testTags.Title = "Chapter 1";
            _testTags.Write(_copiedFile);

            var file = GivenLightNovelAudioFile("Early Years", part: 1, partCount: 1);

            var tag = Subject.GetTrackMetadata(file);

            tag.Title.Should().Be("Early Years");
            tag.Book.Should().Be("Early Years");
        }

        [Test]
        public void get_metadata_should_keep_the_file_title_on_every_part_of_a_multi_file_edition()
        {
            GivenFileCopy("nin.m4a");

            foreach (var part in new[] { 1, 2, 3 })
            {
                _testTags.Title = $"Chapter {part}";
                _testTags.Write(_copiedFile);

                var file = GivenLightNovelAudioFile("Early Years", part: part, partCount: 3);

                var tag = Subject.GetTrackMetadata(file);

                tag.Additive.Should().BeTrue();
                tag.Title.Should().Be($"Chapter {part}");
                tag.Book.Should().Be("Early Years");
                tag.Series.Should().Be("The Beginning After the End");
                tag.SeriesPart.Should().Be("1");
            }
        }

        // Fix round 1 (review): the import-time write runs before the files are inserted, so the
        // edition's DB list is empty then and the batch count lives on the BookFile (PartCount,
        // not mapped: 0 on a DB-loaded file). The single-file rule must hold at both moments.
        [Test]
        public void get_metadata_should_write_the_title_at_import_for_a_single_file_edition()
        {
            GivenFileCopy("nin.m4a");

            _testTags.Title = "Chapter 1";
            _testTags.Write(_copiedFile);

            var file = GivenLightNovelAudioFile("Early Years");
            file.Edition.Value.BookFiles = new List<BookFile>();
            file.PartCount = 1;

            var tag = Subject.GetTrackMetadata(file);

            tag.Title.Should().Be("Early Years");
            tag.Book.Should().Be("Early Years");
        }

        [Test]
        public void get_metadata_should_keep_the_file_title_at_import_for_a_multi_file_edition()
        {
            GivenFileCopy("nin.m4a");

            _testTags.Title = "Chapter 2";
            _testTags.Write(_copiedFile);

            var file = GivenLightNovelAudioFile("Early Years");
            file.Edition.Value.BookFiles = new List<BookFile>();
            file.Part = 2;
            file.PartCount = 3;

            var tag = Subject.GetTrackMetadata(file);

            tag.Title.Should().Be("Chapter 2");
            tag.Book.Should().Be("Early Years");
            tag.Series.Should().Be("The Beginning After the End");
        }

        [Test]
        public void get_metadata_should_write_the_title_on_retag_for_a_single_file_edition()
        {
            GivenFileCopy("nin.m4a");

            _testTags.Title = "Chapter 1";
            _testTags.Write(_copiedFile);

            // a DB-loaded file: the edition lists it, PartCount reads 0
            var file = GivenLightNovelAudioFile("Early Years");
            file.Edition.Value.BookFiles = new List<BookFile> { file };
            file.PartCount = 0;

            var tag = Subject.GetTrackMetadata(file);

            tag.Title.Should().Be("Early Years");
        }

        [Test]
        public void write_tags_should_leave_the_file_title_alone_on_a_multi_file_edition()
        {
            GivenFileCopy("nin.m4a");

            _testTags.Title = "Chapter 2";
            _testTags.Performers = new[] { "Original Artist" };
            _testTags.BookAuthors = new[] { "Original Artist" };
            _testTags.Write(_copiedFile);

            var file = GivenLightNovelAudioFile("Early Years", part: 2, partCount: 3);

            Subject.WriteTags(file, false, true);

            var onDisk = Subject.ReadAudioTag(_copiedFile);

            onDisk.Title.Should().Be("Chapter 2");
            onDisk.Book.Should().Be("Early Years");
            onDisk.Series.Should().Be("The Beginning After the End");
            onDisk.SeriesPart.Should().Be("1");
            onDisk.Performers.Should().BeEquivalentTo(new[] { "Original Artist" });

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookFileRetaggedEvent>(e => !e.Diff.ContainsKey("Title"))), Times.Once());

            // the file now matches: nothing left to write
            Subject.WriteTags(file, false, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Once());
        }

        [Test]
        public void write_tags_should_still_scrub_before_a_full_write()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(x => x.ScrubAudioTags)
                .Returns(true);

            GivenFileCopy("nin.mp3");

            // a frame the full write never touches: only a scrub removes it
            using (var f = TagLib.File.Create(_copiedFile))
            {
                var id3tag = (TagLib.Id3v2.Tag)f.GetTag(TagLib.TagTypes.Id3v2);
                TagLib.Id3v2.UserTextInformationFrame.Get(id3tag, "RIPPER", true).Text = new[] { "abcde" };
                f.Save();
            }

            var file = GivenTrackfile("Dandadan", "local-dandadan", MediaType.Archive, null, 1, 1, 1);

            Subject.WriteTags(file, false, true);

            using (var f = TagLib.File.Create(_copiedFile))
            {
                var id3tag = (TagLib.Id3v2.Tag)f.GetTag(TagLib.TagTypes.Id3v2);
                TagLib.Id3v2.UserTextInformationFrame.Get(id3tag, "RIPPER", false).Should().BeNull();
            }

            _log.Logs.Should().Contain(l => l.StartsWith("Debug|Scrubbing tags for"));

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookFileRetaggedEvent>(e => e.Scrubbed)), Times.Once());
        }

        // Task 7 (2026-09-18): TagLibSharp-Lidarr's UnknownBox loads its payload from wherever the
        // file position happens to be, and Save() re-renders the whole udta from the parsed
        // children, so every udta child TagLib does not model -- the Nero chpl chapter list here --
        // came back shifted and truncated (53 -> 29 bytes on this file). The MP4 write goes through
        // Mpeg4TagFile, which reloads those payloads from disk first.
        [Test]
        public void mp4_write_should_keep_the_chpl_chapter_atom_byte_identical()
        {
            GivenFileCopy("chapters.m4b");

            var chpl = ReadChplBox(_copiedFile);
            chpl.Length.Should().Be(53);

            var additive = new AudioTag
            {
                Additive = true,
                Title = "Mushoku Tensei: Jobless Reincarnation (Light Novel), Vol. 1",
                Book = "Mushoku Tensei: Jobless Reincarnation (Light Novel), Vol. 1",
                Series = "Mushoku Tensei",
                SeriesPart = "1"
            };

            additive.Write(_copiedFile);

            ReadChplBox(_copiedFile).Should().Equal(chpl);
            Subject.ReadAudioTag(_copiedFile).Title.Should().Be("Mushoku Tensei: Jobless Reincarnation (Light Novel), Vol. 1");

            // the full write (cover embedded: a large udta size change) must keep it too
            _testTags.Write(_copiedFile);

            ReadChplBox(_copiedFile).Should().Equal(chpl);
            Subject.ReadAudioTag(_copiedFile).Title.Should().Be(_testTags.Title);
        }

        // One copy each (2026-09-20): an adopted original is never written into unless the entry
        // opts in (AdoptedTagWrite), and even then the additive write leaves its artist alone;
        // Mangarr-grabbed light-novel audio carries the series' Writer as performers / album
        // artists (AdditiveAuthors) on top of the additive write.
        private BookFile GivenAdoptedLightNovelAudioFile(bool optIn, string writer = "Reki Kawahara")
        {
            var file = GivenLightNovelAudioFile("Early Years");
            file.Adopted = true;
            file.Home = FileHome.Audiobooks;
            file.Author.Value.AdoptedTagWrite = optIn;
            file.Author.Value.Metadata.Value.Writer = writer;

            return file;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void write_tags_leaves_an_adopted_original_alone_without_the_opt_in(bool force)
        {
            GivenFileCopy("nin.m4a");

            _testTags.Title = "Chapter 1";
            _testTags.Performers = new[] { "Original Artist" };
            _testTags.Write(_copiedFile);

            var file = GivenAdoptedLightNovelAudioFile(optIn: false);

            Subject.WriteTags(file, true, force);

            _log.Logs.Should().Contain(l => l.EndsWith("adopted original, tags left alone"));

            // it returns before the file is even read
            _log.Logs.Should().NotContain(l => l.StartsWith("Debug|Starting tag read for"));
            _log.Logs.Should().NotContain(l => l.StartsWith("Debug|Writing tags for"));

            var onDisk = Subject.ReadAudioTag(_copiedFile);
            onDisk.Title.Should().Be("Chapter 1");
            onDisk.Series.Should().Be("Sword Art Online");
            onDisk.Performers.Should().BeEquivalentTo(new[] { "Original Artist" });

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Never());
        }

        [Test]
        public void write_tags_writes_an_adopted_original_additively_with_the_opt_in()
        {
            GivenFileCopy("nin.m4a");

            _testTags.Performers = new[] { "Original Artist" };
            _testTags.BookAuthors = new[] { "Original Artist" };
            _testTags.Write(_copiedFile);

            // the writer is known, but an adopted file keeps its own artist
            var file = GivenAdoptedLightNovelAudioFile(optIn: true, writer: "TurtleMe");

            var tag = Subject.GetTrackMetadata(file);
            tag.Additive.Should().BeTrue();
            tag.AdditiveAuthors.Should().BeNull();

            Subject.WriteTags(file, false, true);

            var onDisk = Subject.ReadAudioTag(_copiedFile);
            onDisk.Title.Should().Be("Early Years");
            onDisk.Book.Should().Be("Early Years");
            onDisk.Series.Should().Be("The Beginning After the End");
            onDisk.Performers.Should().BeEquivalentTo(new[] { "Original Artist" });
            onDisk.BookAuthors.Should().BeEquivalentTo(new[] { "Original Artist" });

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Once());
        }

        [Test]
        public void get_metadata_carries_the_writer_as_additive_authors_for_grabbed_light_novel_audio()
        {
            GivenFileCopy("nin.m4a");

            _testTags.Performers = new[] { "Original Artist" };
            _testTags.BookAuthors = new[] { "Original Artist" };
            _testTags.Write(_copiedFile);

            var file = GivenLightNovelAudioFile("Early Years");
            file.Author.Value.Metadata.Value.Writer = "TurtleMe";

            var tag = Subject.GetTrackMetadata(file);

            tag.Additive.Should().BeTrue();
            tag.AdditiveAuthors.Should().Equal("TurtleMe");

            // the tag's own performers stay the file's: AdditiveAuthors is what the write applies
            tag.Performers.Should().BeEquivalentTo(new[] { "Original Artist" });
            tag.BookAuthors.Should().BeEquivalentTo(new[] { "Original Artist" });
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void get_metadata_has_no_additive_authors_without_a_writer(string writer)
        {
            GivenFileCopy("nin.m4a");

            var file = GivenLightNovelAudioFile("Early Years");
            file.Author.Value.Metadata.Value.Writer = writer;

            Subject.GetTrackMetadata(file).AdditiveAuthors.Should().BeNull();
        }

        [Test]
        public void get_metadata_has_no_additive_authors_for_a_light_novel_ebook()
        {
            GivenFileCopy("nin.m4a");

            var file = GivenTrackfile("Overlord", "local-overlord~ln", MediaType.Ebook, null, 5, 1, 1);
            file.Author.Value.Metadata.Value.Writer = "Kugane Maruyama";

            Subject.GetTrackMetadata(file).AdditiveAuthors.Should().BeNull();
        }

        [Test]
        [TestCaseSource(typeof(TestCaseFactory), nameof(TestCaseFactory.TestCases))]
        public void additive_write_sets_the_performers_and_album_artists_to_the_additive_authors(string filename, string[] skipProperties)
        {
            GivenFileCopy(filename);
            var path = _copiedFile;

            _testTags.Performers = new[] { "Original Artist" };
            _testTags.BookAuthors = new[] { "Original Artist" };
            _testTags.Genres = new[] { "Audiobook" };
            _testTags.Publisher = "Yen Audio";
            _testTags.Write(path);

            var before = Subject.ReadAudioTag(path);
            before.Performers.Should().BeEquivalentTo(new[] { "Original Artist" });

            var additive = new AudioTag
            {
                Additive = true,
                Title = "Sword Art Online: Progressive 8",
                Book = "Sword Art Online: Progressive 8",
                Series = "Sword Art Online: Progressive",
                SeriesPart = "8",
                AdditiveAuthors = new[] { "Reki Kawahara" }
            };

            additive.Write(path);

            var after = Subject.ReadAudioTag(path);

            after.Title.Should().Be("Sword Art Online: Progressive 8");
            after.Book.Should().Be("Sword Art Online: Progressive 8");
            after.Performers.Should().BeEquivalentTo(new[] { "Reki Kawahara" });
            after.BookAuthors.Should().BeEquivalentTo(new[] { "Reki Kawahara" });

            if (!skipProperties.Contains("Series"))
            {
                after.Series.Should().Be("Sword Art Online: Progressive");
                after.SeriesPart.Should().Be("8");
            }

            // everything else is exactly as the file had it
            VerifySame(after, before, skipProperties.Union(new[] { "Title", "Book", "Series", "SeriesPart", "Performers", "BookAuthors" }).ToArray());
            after.Genres.Should().BeEquivalentTo(new[] { "Audiobook" });
            after.Publisher.Should().Be("Yen Audio");
            after.ImageSize.Should().Be(_testTags.ImageSize);
        }

        [Test]
        public void diff_includes_the_author_only_when_additive_authors_differs_from_the_file()
        {
            GivenFileCopy("nin.mp3");

            _testTags.Performers = new[] { "Original Artist" };
            _testTags.BookAuthors = new[] { "Original Artist" };
            _testTags.Write(_copiedFile);

            var fileTags = Subject.ReadAudioTag(_copiedFile);
            var generated = new AudioTag
            {
                Additive = true,
                Title = fileTags.Title,
                Book = fileTags.Book,
                Series = fileTags.Series,
                SeriesPart = fileTags.SeriesPart,
                Performers = fileTags.Performers,
                BookAuthors = fileTags.BookAuthors
            };

            // no additive authors: the file's artist is not the generated tag's business
            generated.AdditiveAuthors = null;
            generated.Performers = new[] { "Sword Art Online" };
            fileTags.Diff(generated).Should().BeEmpty();

            generated.AdditiveAuthors = new[] { "Original Artist" };
            fileTags.Diff(generated).Should().BeEmpty();

            generated.AdditiveAuthors = new[] { "Reki Kawahara" };
            var diff = fileTags.Diff(generated);

            diff.Should().HaveCount(1);
            diff["Author"].Should().Be(Tuple.Create("Original Artist", "Reki Kawahara"));
        }

        [Test]
        public void write_tags_writes_the_writer_into_grabbed_light_novel_audio()
        {
            GivenFileCopy("nin.m4a");

            _testTags.Performers = new[] { "Original Artist" };
            _testTags.BookAuthors = new[] { "Original Artist" };
            _testTags.Publisher = "Yen Audio";
            _testTags.Write(_copiedFile);

            var file = GivenLightNovelAudioFile("Early Years");
            file.Author.Value.Metadata.Value.Writer = "TurtleMe";
            var bookId = file.Edition.Value.Book.Value.Id;

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(bookId))
                .Returns(new List<BookFile> { file });

            var previews = Subject.GetRetagPreviewsByBook(bookId);
            previews.Should().HaveCount(1);
            previews[0].Changes.Keys.Should().Contain("Author");
            previews[0].Changes["Author"].Should().Be(Tuple.Create("Original Artist", "TurtleMe"));

            Subject.WriteTags(file, false, true);

            var onDisk = Subject.ReadAudioTag(_copiedFile);
            onDisk.Title.Should().Be("Early Years");
            onDisk.Series.Should().Be("The Beginning After the End");
            onDisk.Performers.Should().BeEquivalentTo(new[] { "TurtleMe" });
            onDisk.BookAuthors.Should().BeEquivalentTo(new[] { "TurtleMe" });
            onDisk.Publisher.Should().Be("Yen Audio");
            onDisk.ImageSize.Should().Be(_testTags.ImageSize);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookFileRetaggedEvent>(e => e.Diff.ContainsKey("Author"))), Times.Once());

            // the file now matches: nothing left to write, nothing left to preview
            Subject.WriteTags(file, false, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<BookFileRetaggedEvent>()), Times.Once());
            Subject.GetRetagPreviewsByBook(bookId).Should().BeEmpty();
        }

        // One copy each (2026-09-20): an adopted original's series and sequence go to Audiobookshelf
        // over its API (AdoptedAudioSyncService) instead of into the file; Mangarr's own files are
        // written as before. Grouped per entry, on every path that writes tags.
        private BookFile GivenAdoptedRowOffTheEntry(bool optIn)
        {
            var file = GivenAdoptedLightNovelAudioFile(optIn);
            file.Path = "/srv/audiobooks/The Beginning After the End/Vol 2/Vol 2.m4b";
            file.Edition.Value.Book.Value.VolumeNumber = 2;

            return file;
        }

        // Coverage (2026-09-23): a grabbed row Mangarr routed to Audiobookshelf (never adopted) goes
        // to the same sync as an adopted row -- its tags are still written as usual, the sync is on
        // top, not instead.
        // Keeps the real on-disk path (_copiedFile via GivenFileCopy/GivenLightNovelAudioFile) --
        // unlike an adopted row (Audiobookshelf's own file, GivenAdoptedRowOffTheEntry's synthetic
        // path is fine since WriteTags never runs against it), a grabbed row's WriteTags call reads
        // and writes the actual file.
        private BookFile GivenGrabbedAudiobookshelfRow()
        {
            var file = GivenLightNovelAudioFile("Early Years", volumeNumber: 3);
            file.Home = FileHome.Audiobooks;

            return file;
        }

        private void VerifySynced(BookFile adopted, Times times)
        {
            var authorId = adopted.Edition.Value.Book.Value.Author.Value.Id;

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(It.Is<Author>(a => a.Id == authorId), It.Is<List<BookFile>>(l => l.Count == 1 && l[0] == adopted), out It.Ref<string>.IsAny), times);
        }

        [Test]
        public void sync_tags_sends_adopted_audio_to_the_audiobookshelf_sync_and_writes_the_rest()
        {
            GivenFileCopy("nin.m4a");
            _testTags.Write(_copiedFile);

            var grabbed = GivenLightNovelAudioFile("Early Years");
            var adopted = GivenAdoptedRowOffTheEntry(optIn: false);

            Subject.SyncTags(new List<Edition> { grabbed.Edition.Value, adopted.Edition.Value });

            VerifySynced(adopted, Times.Once());
            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny), Times.Once());

            // the grabbed file was written; the adopted row never reached WriteTags at all
            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookFileRetaggedEvent>(e => e.BookFile == grabbed)), Times.Once());
            _log.Logs.Should().NotContain(l => l.EndsWith("adopted original, tags left alone"));
        }

        [Test]
        public void sync_tags_groups_adopted_rows_per_entry()
        {
            var first = GivenAdoptedRowOffTheEntry(optIn: false);
            var second = GivenAdoptedRowOffTheEntry(optIn: false);
            second.Path = "/srv/audiobooks/Sword Art Online/Vol 1/Vol 1.m4b";
            second.Edition.Value.Book.Value.Author.Value.Id = 2;

            Subject.SyncTags(new List<Edition> { first.Edition.Value, second.Edition.Value });

            VerifySynced(first, Times.Once());
            VerifySynced(second, Times.Once());
        }

        [Test]
        public void sync_tags_off_syncs_nothing_to_audiobookshelf_either()
        {
            Mocker.GetMock<IConfigService>().Setup(x => x.WriteAudioTags).Returns(WriteAudioTagsType.No);

            var adopted = GivenAdoptedRowOffTheEntry(optIn: false);

            Subject.SyncTags(new List<Edition> { adopted.Edition.Value });

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny), Times.Never());
        }

        [Test]
        public void retag_files_syncs_an_adopted_row_over_the_api_and_writes_only_the_rest()
        {
            GivenFileCopy("nin.m4a");
            _testTags.Write(_copiedFile);

            var grabbed = GivenLightNovelAudioFile("Early Years");
            var adopted = GivenAdoptedRowOffTheEntry(optIn: false);
            var author = adopted.Edition.Value.Book.Value.Author.Value;

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(author.Id)).Returns(author);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<BookFile> { adopted, grabbed });

            Subject.RetagFiles(new RetagFilesCommand(author.Id, new List<int> { adopted.Id, grabbed.Id }));

            VerifySynced(adopted, Times.Once());
            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookFileRetaggedEvent>(e => e.BookFile == grabbed)), Times.Once());
            _log.Logs.Should().NotContain(l => l.EndsWith("adopted original, tags left alone"));
        }

        // With the entry's opt-in the adopted original is written too (T5's additive write), on top
        // of the API sync -- never instead of it: ABS reads its own metadata, not the file's tags.
        [Test]
        public void retag_files_with_the_opt_in_writes_the_adopted_row_as_well()
        {
            GivenFileCopy("nin.m4a");
            _testTags.Write(_copiedFile);

            var adopted = GivenAdoptedLightNovelAudioFile(optIn: true);
            var author = adopted.Edition.Value.Book.Value.Author.Value;

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(author.Id)).Returns(author);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<BookFile> { adopted });

            Subject.RetagFiles(new RetagFilesCommand(author.Id, new List<int> { adopted.Id }));

            VerifySynced(adopted, Times.Once());
            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookFileRetaggedEvent>(e => e.BookFile == adopted)), Times.Once());
            Subject.ReadAudioTag(_copiedFile).Series.Should().Be("The Beginning After the End");
        }

        [Test]
        public void retag_author_syncs_the_adopted_rows_over_the_api_and_writes_only_the_rest()
        {
            GivenFileCopy("nin.m4a");
            _testTags.Write(_copiedFile);

            var grabbed = GivenLightNovelAudioFile("Early Years");
            var adopted = GivenAdoptedRowOffTheEntry(optIn: false);
            var author = adopted.Edition.Value.Book.Value.Author.Value;

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthors(It.IsAny<IEnumerable<int>>())).Returns(new List<Author> { author });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(author.Id)).Returns(new List<BookFile> { adopted, grabbed });

            Subject.RetagAuthor(new RetagAuthorCommand { AuthorIds = new List<int> { author.Id } });

            VerifySynced(adopted, Times.Once());
            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookFileRetaggedEvent>(e => e.BookFile == grabbed)), Times.Once());
            _log.Logs.Should().NotContain(l => l.EndsWith("adopted original, tags left alone"));
        }

        [Test]
        public void retag_author_with_no_adopted_rows_never_calls_the_sync()
        {
            GivenFileCopy("nin.m4a");
            _testTags.Write(_copiedFile);

            var grabbed = GivenLightNovelAudioFile("Early Years");
            var author = grabbed.Edition.Value.Book.Value.Author.Value;

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthors(It.IsAny<IEnumerable<int>>())).Returns(new List<Author> { author });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(author.Id)).Returns(new List<BookFile> { grabbed });

            Subject.RetagAuthor(new RetagAuthorCommand { AuthorIds = new List<int> { author.Id } });

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.IsAny<List<BookFile>>(), out It.Ref<string>.IsAny), Times.Never());
        }

        [Test]
        public void sync_tags_also_sends_a_grabbed_audiobookshelf_homed_row_to_the_sync_and_still_writes_its_tags()
        {
            GivenFileCopy("nin.m4a");
            _testTags.Write(_copiedFile);

            var grabbed = GivenGrabbedAudiobookshelfRow();

            Subject.SyncTags(new List<Edition> { grabbed.Edition.Value });

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(It.IsAny<Author>(), It.Is<List<BookFile>>(l => l.Count == 1 && l[0] == grabbed), out It.Ref<string>.IsAny), Times.Once());
            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookFileRetaggedEvent>(e => e.BookFile == grabbed)), Times.Once());
        }

        [Test]
        public void retag_author_also_sends_a_grabbed_audiobookshelf_homed_row_to_the_sync()
        {
            GivenFileCopy("nin.m4a");
            _testTags.Write(_copiedFile);

            var grabbed = GivenGrabbedAudiobookshelfRow();
            var author = grabbed.Edition.Value.Book.Value.Author.Value;

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthors(It.IsAny<IEnumerable<int>>())).Returns(new List<Author> { author });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(author.Id)).Returns(new List<BookFile> { grabbed });

            Subject.RetagAuthor(new RetagAuthorCommand { AuthorIds = new List<int> { author.Id } });

            Mocker.GetMock<IAdoptedAudioSyncService>()
                .Verify(v => v.Sync(It.Is<Author>(a => a.Id == author.Id), It.Is<List<BookFile>>(l => l.Count == 1 && l[0] == grabbed), out It.Ref<string>.IsAny), Times.Once());
            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookFileRetaggedEvent>(e => e.BookFile == grabbed)), Times.Once());
        }

        // The Nero chapter list as raw bytes: the one chpl box in the file, from its 4-byte
        // big-endian size field, size bytes long (Take clamps at end of file, so a truncated box
        // fails on length rather than throwing).
        private static byte[] ReadChplBox(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var starts = new List<int>();

            for (var i = 4; i + 4 <= bytes.Length; i++)
            {
                if (bytes[i] == 'c' && bytes[i + 1] == 'h' && bytes[i + 2] == 'p' && bytes[i + 3] == 'l')
                {
                    starts.Add(i - 4);
                }
            }

            starts.Should().HaveCount(1, "the file has exactly one chpl box");

            var start = starts[0];
            var size = (bytes[start] << 24) | (bytes[start + 1] << 16) | (bytes[start + 2] << 8) | bytes[start + 3];

            return bytes.Skip(start).Take(size).ToArray();
        }
    }
}
