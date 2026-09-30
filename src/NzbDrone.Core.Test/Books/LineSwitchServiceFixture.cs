using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.MetadataSource.Gcd;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.BookTests
{
    // Line safety (2026-09-28): Switch Line. The Trapped in a Dating Sim spin-off entry (107) holding the main
    // series' files is switched to the main line: every file stays on the volume with its number, the name
    // becomes the main line's, the refresh re-mints the volumes. Nothing on disk moves, nothing is retagged
    // until the user says yes.
    [TestFixture]
    public class LineSwitchServiceFixture : CoreTest<LineSwitchService>
    {
        private const string SpinOffEntryName = "Trapped in a Dating Sim: Otome Games Are Tough For Us, Too!";

        private Author _author;
        private List<Book> _books;
        private List<Edition> _editions;
        private List<BookFile> _files;

        [SetUp]
        public void Setup()
        {
            var gcd = Mocker.GetMock<IGcdMetadataService>();
            gcd.SetupGet(s => s.Available).Returns(true);
            gcd.Setup(s => s.FindSeriesByTomeId(OtomeLines.SpinOffId)).Returns(OtomeLines.SpinOff());
            gcd.Setup(s => s.FindSeriesByTomeId(OtomeLines.MainId)).Returns(OtomeLines.MainLine());
            gcd.Setup(s => s.GetWorkLines(OtomeLines.WorkId)).Returns(OtomeLines.Work());
            gcd.Setup(s => s.GetVolumes(5101)).Returns(Volumes(Enumerable.Range(1, 13)));
            gcd.Setup(s => s.GetVolumes(5102)).Returns(Volumes(Enumerable.Range(1, 6)));
            gcd.Setup(s => s.FindSeriesByTitle(OtomeLines.MainName, LibraryType.LightNovel)).Returns(OtomeLines.MainLine());
            gcd.Setup(s => s.FindSeriesByTitle(OtomeLines.SpinOffName, LibraryType.LightNovel)).Returns(OtomeLines.SpinOff());

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAllAuthors()).Returns(() => new List<Author> { _author });
            Mocker.GetMock<IManageCommandQueue>().Setup(q => q.Push(It.IsAny<RefreshAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                  .Returns(new CommandModel { Id = 900 });

            GivenEntry(OtomeLines.SpinOffId, SpinOffEntryName, Enumerable.Range(1, 6));
        }

        private static List<GcdVolume> Volumes(IEnumerable<int> numbers)
        {
            return numbers.Select(n => new GcdVolume { VolumeNumber = n }).ToList();
        }

        // A light-novel entry with an EPUB in calibre on every given volume, and on volume 2 an audiobook in ABS.
        private void GivenEntry(string tomeLineId, string name, IEnumerable<int> volumesWithFiles, LibraryType library = LibraryType.LightNovel)
        {
            _author = new Author
            {
                Id = 107,
                Path = "/lightnovels/" + name,
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = library == LibraryType.LightNovel ? "local-otome~ln" : "local-otome",
                    Name = name,
                    TomeLineId = tomeLineId,
                    AniListId = 102,
                    Aliases = new List<string> { "Otome Games Are Tough For Us, Too!" }
                }
            };

            _books = new List<Book>();
            _editions = new List<Edition>();
            _files = new List<BookFile>();

            foreach (var n in volumesWithFiles)
            {
                var book = new Book { Id = n, VolumeNumber = n, Subtitle = "Spin-off subtitle " + n };
                var ebook = new Edition { Id = 100 + n, BookId = n, MediaType = library == LibraryType.LightNovel ? MediaType.Ebook : MediaType.Archive, Isbn13 = "97816" + n, Overview = "Spin-off blurb", PageCount = 200 };
                _books.Add(book);
                _editions.Add(ebook);
                _files.Add(new BookFile { Id = 1000 + n, EditionId = ebook.Id, Home = library == LibraryType.LightNovel ? FileHome.Calibre : FileHome.Entry });

                if (library == LibraryType.LightNovel)
                {
                    var audio = new Edition { Id = 200 + n, BookId = n, MediaType = MediaType.Audio, Asin = "B0SPIN" + n, AudiobookTitle = "Spin-off audiobook " + n, RuntimeMinutes = 400 };
                    _editions.Add(audio);

                    if (n == 2)
                    {
                        _files.Add(new BookFile { Id = 2002, EditionId = audio.Id, Home = FileHome.Audiobooks });
                    }
                }
            }

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(107)).Returns(_author);
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthor(107)).Returns(() => _books);
            Mocker.GetMock<IEditionService>().Setup(s => s.GetEditionsByAuthor(107)).Returns(() => _editions);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(107)).Returns(() => _files);
        }

        private SwitchLineCommand Switch(string tomeLineId)
        {
            var command = new SwitchLineCommand { AuthorId = 107, TomeLineId = tomeLineId };
            Subject.Execute(command);
            return command;
        }

        private void VerifyNothingWritten()
        {
            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.IsAny<AuthorMetadata>()), Times.Never());
            Mocker.GetMock<IBookService>().Verify(s => s.UpdateMany(It.IsAny<List<Book>>()), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(s => s.UpdateMany(It.IsAny<List<Edition>>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<RefreshAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        private void VerifyNoExternalWrite()
        {
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<RetagFilesCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<SyncLightNovelTitlesCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<MoveAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
            Mocker.GetMock<IAuthorService>().Verify(s => s.UpdateAuthor(It.IsAny<Author>()), Times.Never());
        }

        [Test]
        public void the_spin_off_entry_lists_the_main_line_only()
        {
            var choices = Subject.Choices(_author);

            choices.CurrentLineName.Should().Be(OtomeLines.SpinOffName);
            choices.Options.Should().ContainSingle();

            var main = choices.Options.Single();
            main.TomeLineId.Should().Be(OtomeLines.MainId);
            main.Name.Should().Be(OtomeLines.MainName);
            main.VolumeCount.Should().Be(13);
            main.IsMain.Should().BeTrue();
            main.SpinOffOf.Should().BeNull();
            main.FilesMoving.Should().Be(7);
            main.KeptVolumes.Should().BeEmpty();
            main.VolumesAdded.Should().Be(7);
            main.NoAniListMatch.Should().BeFalse();
            main.BlockedReason.Should().BeNull();
        }

        [Test]
        public void an_unbound_series_has_nothing_to_switch_to()
        {
            _author.Metadata.Value.TomeLineId = null;

            Subject.Choices(_author).Options.Should().BeEmpty();
        }

        // Spin-off -> main: files 1-6 stay on volumes 1-6 (now the main line's), 7-13 are the main line's own
        // new volumes, the name becomes the main line's, the refresh is queued -- and nothing else happens.
        [Test]
        public void switching_the_spin_off_to_the_main_line()
        {
            var command = Switch(OtomeLines.MainId);

            command.Switched.Should().BeTrue();
            command.MovedBookFileIds.Should().BeEquivalentTo(new[] { 1001, 1002, 1003, 1004, 1005, 1006, 2002 });
            command.KeptVolumes.Should().BeEmpty();
            command.RefreshCommandId.Should().Be(900);
            command.OfferExternalSync.Should().BeTrue();

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m =>
                m.TomeLineId == OtomeLines.MainId &&
                m.Name == OtomeLines.MainName &&
                m.SortName == OtomeLines.MainName.ToLowerInvariant() &&
                m.AniListId == 101 &&
                !m.Aliases.Any() &&
                m.AnchorName == null &&
                !m.EditionCollected)), Times.Once());
            _author.Name.Should().Be(OtomeLines.MainName);
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.Is<RefreshAuthorCommand>(c => c.AuthorId == 107), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
            Mocker.GetMock<IMangaSeriesMetadataProvider>().Verify(p => p.ForgetLookup(SpinOffEntryName), Times.Once());

            // Review fixes (I1, I2): the refresh writes no tags and searches none of the volumes it adds.
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.Is<RefreshAuthorCommand>(c => c.SkipTagSync), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
            Mocker.GetMock<IBookAddedService>().Verify(s => s.SkipNextRefreshSearch(107), Times.Once());

            // No file row is rewritten: a volume's id is "<series>-v<N>", so each file is already on volume N.
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(It.IsAny<BookFile>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(It.IsAny<List<BookFile>>()), Times.Never());
            VerifyNoExternalWrite();
        }

        // Main -> spin-off: the 6-volume line has no volumes 7-13 -- their files stay where they are (listed,
        // never deleted) and their volumes keep the main line's data.
        [Test]
        public void switching_to_a_shorter_line_keeps_the_files_without_a_counterpart()
        {
            GivenEntry(OtomeLines.MainId, OtomeLines.MainName, Enumerable.Range(1, 13));

            var command = Switch(OtomeLines.SpinOffId);

            command.Switched.Should().BeTrue();
            command.MovedBookFileIds.Should().BeEquivalentTo(Enumerable.Range(1, 6).Select(n => 1000 + n).Append(2002));
            command.KeptVolumes.Should().Equal("7", "8", "9", "10", "11", "12", "13");
            command.ResultMessage.Should().Be("Switched to " + OtomeLines.SpinOffName + ": 7 file(s) on matching volumes, 7 kept");

            Mocker.GetMock<IBookService>().Verify(s => s.UpdateMany(It.Is<List<Book>>(l => l.Select(b => b.VolumeNumber).SequenceEqual(new double[] { 1, 2, 3, 4, 5, 6 }))), Times.Once());
            _books.Single(b => b.VolumeNumber == 9).Subtitle.Should().Be("Spin-off subtitle 9");
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IBookService>().Verify(s => s.DeleteBook(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        // A line whose volume numbers have a gap (no volume 3): the file on volume 3 has no counterpart.
        [Test]
        public void switching_to_a_line_with_a_volume_number_gap()
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetVolumes(5101)).Returns(Volumes(new[] { 1, 2, 4, 5, 6, 7 }));

            var command = Switch(OtomeLines.MainId);

            command.MovedBookFileIds.Should().NotContain(1003);
            command.KeptVolumes.Should().Equal("3");
            _books.Single(b => b.VolumeNumber == 3).Subtitle.Should().Be("Spin-off subtitle 3");
            _books.Single(b => b.VolumeNumber == 4).Subtitle.Should().BeNull();
        }

        // The ratchets keep a stored subtitle, ISBN and Audible product when the new line's volume has none
        // (Book/Edition.UseMetadataFrom) -- so the switch clears the old line's on the volumes that carry over,
        // audio edition included. An app-owned covered mark (an import's, a user's) is kept.
        [Test]
        public void a_light_novel_with_an_audio_edition_loses_the_old_lines_volume_identity()
        {
            var stale = new Book { Subtitle = "Spin-off subtitle 1" };
            stale.UseMetadataFrom(new Book { Subtitle = null });
            stale.Subtitle.Should().Be("Spin-off subtitle 1", "the ratchet this switch has to undo");

            _editions.Single(e => e.Id == 201).CoveredByVolume = 1;
            _editions.Single(e => e.Id == 201).CoveredSource = CoveredSources.Audible;
            _editions.Single(e => e.Id == 203).CoveredByVolume = 2;
            _editions.Single(e => e.Id == 203).CoveredSource = CoveredSources.Import;

            Switch(OtomeLines.MainId);

            _books.Should().OnlyContain(b => b.Subtitle == null);
            _editions.Should().OnlyContain(e => e.Isbn13 == null && e.Asin == null && e.AudiobookTitle == null && e.RuntimeMinutes == null && e.Overview == string.Empty && e.PageCount == 0);
            _editions.Single(e => e.Id == 201).CoveredByVolume.Should().BeNull();
            _editions.Single(e => e.Id == 203).CoveredByVolume.Should().Be(2);
            _editions.Single(e => e.Id == 203).CoveredSource.Should().Be(CoveredSources.Import);
            Mocker.GetMock<IEditionService>().Verify(s => s.UpdateMany(It.Is<List<Edition>>(l => l.Count == 12)), Times.Once());
        }

        [Test]
        public void a_manga_series_switches_and_is_never_offered_the_calibre_update()
        {
            var main = new GcdSeries { GcdSeriesId = 1, Name = "Kaiju No. 8", Language = "en", Medium = "manga", VolumeCount = 12, IsMain = true, TomeId = "rl_kaiju", TomeWorkId = "w_kaiju", AnilistId = 1001 };
            var relax = new GcdSeries { GcdSeriesId = 2, Name = "Kaiju No. 8: Relax", Language = "en", Medium = "manga", VolumeCount = 2, TomeId = "rl_relax", TomeWorkId = "w_kaiju", AnilistId = 1002 };
            var gcd = Mocker.GetMock<IGcdMetadataService>();
            gcd.Setup(s => s.FindSeriesByTomeId("rl_kaiju")).Returns(main);
            gcd.Setup(s => s.FindSeriesByTomeId("rl_relax")).Returns(relax);
            gcd.Setup(s => s.GetWorkLines("w_kaiju")).Returns(new List<GcdSeries> { main, relax });
            gcd.Setup(s => s.GetVolumes(1)).Returns(Volumes(Enumerable.Range(1, 12)));
            gcd.Setup(s => s.FindSeriesByTitle("Kaiju No. 8", LibraryType.Manga)).Returns(main);
            GivenEntry("rl_relax", "Kaiju No. 8: Relax", new[] { 1, 2 }, LibraryType.Manga);

            var command = Switch("rl_kaiju");

            command.Switched.Should().BeTrue();
            command.MovedBookFileIds.Should().BeEquivalentTo(new[] { 1001, 1002 });
            command.OfferExternalSync.Should().BeFalse();
            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m => m.Name == "Kaiju No. 8" && m.TomeLineId == "rl_kaiju" && m.AniListId == 1001)), Times.Once());
            VerifyNoExternalWrite();
        }

        // Review fixes (M1): a manga line with no AniList id is switchable, with a warning in the dialog.
        [Test]
        public void a_manga_line_without_an_anilist_id_is_flagged_not_blocked()
        {
            var main = new GcdSeries { GcdSeriesId = 1, Name = "Kaiju No. 8", Language = "en", Medium = "manga", VolumeCount = 12, IsMain = true, TomeId = "rl_kaiju", TomeWorkId = "w_kaiju" };
            var relax = new GcdSeries { GcdSeriesId = 2, Name = "Kaiju No. 8: Relax", Language = "en", Medium = "manga", VolumeCount = 2, TomeId = "rl_relax", TomeWorkId = "w_kaiju", AnilistId = 1002 };
            var gcd = Mocker.GetMock<IGcdMetadataService>();
            gcd.Setup(s => s.FindSeriesByTomeId("rl_relax")).Returns(relax);
            gcd.Setup(s => s.GetWorkLines("w_kaiju")).Returns(new List<GcdSeries> { main, relax });
            gcd.Setup(s => s.FindSeriesByTitle("Kaiju No. 8", LibraryType.Manga)).Returns(main);
            GivenEntry("rl_relax", "Kaiju No. 8: Relax", new[] { 1, 2 }, LibraryType.Manga);

            var option = Subject.Choices(_author).Options.Single();

            option.NoAniListMatch.Should().BeTrue();
            option.BlockedReason.Should().BeNull();
        }

        // Review fixes (I4): the old line's volume identity goes first, then the binding, then the refresh.
        [Test]
        public void the_writes_run_volume_data_then_binding_then_refresh()
        {
            var order = new List<string>();
            Mocker.GetMock<IBookService>().Setup(s => s.UpdateMany(It.IsAny<List<Book>>())).Callback(() => order.Add("books"));
            Mocker.GetMock<IEditionService>().Setup(s => s.UpdateMany(It.IsAny<List<Edition>>())).Callback(() => order.Add("editions"));
            Mocker.GetMock<IAuthorMetadataService>().Setup(s => s.Upsert(It.IsAny<AuthorMetadata>())).Callback(() => order.Add("binding"));
            Mocker.GetMock<IBookAddedService>().Setup(s => s.SkipNextRefreshSearch(107)).Callback(() => order.Add("no search"));
            Mocker.GetMock<IManageCommandQueue>().Setup(q => q.Push(It.IsAny<RefreshAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                  .Callback(() => order.Add("refresh"))
                  .Returns(new CommandModel { Id = 900 });

            Switch(OtomeLines.MainId).Switched.Should().BeTrue();

            order.Should().Equal("books", "editions", "binding", "no search", "refresh");
        }

        [Test]
        public void a_deleted_series_is_not_switched()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(107)).Throws(new ModelNotFoundException(typeof(Author), 107));

            var command = Switch(OtomeLines.MainId);

            command.Switched.Should().BeFalse();
            command.ResultMessage.Should().Be("Not switched: The series no longer exists");
            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        // Review fixes (I3): a Japanese-edition series switched to another Japanese line is named by the add
        // path's rule (AniList's romaji for the line's AniList id) and anchored to the line's English counterpart.
        private void GivenJapaneseEntry()
        {
            var jaMain = new GcdSeries { GcdSeriesId = 6001, Name = "Otome Game Sekai wa Mob ni Kibishii Sekai desu", LocalName = "乙女ゲー世界はモブに厳しい世界です", Language = "ja", Medium = "light_novel", VolumeCount = 14, IsMain = true, TomeId = "rl_ja_main", TomeWorkId = OtomeLines.WorkId, AnilistId = 301 };
            var jaSpinOff = new GcdSeries { GcdSeriesId = 6002, Name = "Akuyaku Reijou", LocalName = "あの乙女ゲーは俺たちに厳しい世界です", Language = "ja", Medium = "light_novel", VolumeCount = 6, TomeId = "rl_ja_spin", TomeWorkId = OtomeLines.WorkId, AnilistId = 302 };
            var main = OtomeLines.MainLine();
            main.OrigSeriesId = 6001;
            var spinOff = OtomeLines.SpinOff();
            spinOff.OrigSeriesId = 6002;

            var gcd = Mocker.GetMock<IGcdMetadataService>();
            gcd.Setup(s => s.FindSeriesByTomeId("rl_ja_main")).Returns(jaMain);
            gcd.Setup(s => s.FindSeriesByTomeId("rl_ja_spin")).Returns(jaSpinOff);
            gcd.Setup(s => s.GetWorkLines(OtomeLines.WorkId)).Returns(new List<GcdSeries> { main, spinOff, jaMain, jaSpinOff });
            gcd.Setup(s => s.GetVolumes(6001)).Returns(Volumes(Enumerable.Range(1, 14)));
            gcd.Setup(s => s.GetVolumes(6002)).Returns(Volumes(Enumerable.Range(1, 6)));

            GivenEntry("rl_ja_spin", "Ano Otome Game wa Oretachi ni Kibishii Sekai desu", Enumerable.Range(1, 6));
            _author.Metadata.Value.EditionLanguage = "ja";
            _author.Metadata.Value.AnchorName = OtomeLines.SpinOffName;
        }

        [Test]
        public void a_japanese_series_takes_the_lines_romaji_name_and_its_english_anchor()
        {
            GivenJapaneseEntry();
            Mocker.GetMock<IAniListService>().Setup(s => s.GetById(301)).Returns(new AniListSeries { RomajiTitle = "Otome Game Sekai wa Mob ni Kibishii Sekai desu" });

            var option = Subject.Choices(_author).Options.Single();
            option.Name.Should().Be("Otome Game Sekai wa Mob ni Kibishii Sekai desu");

            Switch("rl_ja_main").Switched.Should().BeTrue();

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m =>
                m.TomeLineId == "rl_ja_main" &&
                m.Name == "Otome Game Sekai wa Mob ni Kibishii Sekai desu" &&
                m.AniListId == 301 &&
                m.AnchorName == OtomeLines.MainName)), Times.Once());
        }

        // No romaji from AniList: the add path's fallback, the English anchor's name (then no separate anchor).
        [Test]
        public void a_japanese_series_without_a_romaji_title_falls_back_to_its_anchor()
        {
            GivenJapaneseEntry();

            Switch("rl_ja_main").Switched.Should().BeTrue();

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m =>
                m.Name == OtomeLines.MainName &&
                m.AnchorName == null)), Times.Once());
        }

        // Another series already named like the line (the incident's repair added the main series as its own
        // entry): the switch is blocked and nothing is written.
        [Test]
        public void a_namesake_blocks_the_switch()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindByName(OtomeLines.MainName, LibraryType.LightNovel))
                  .Returns(new Author { Id = 108, Metadata = new AuthorMetadata { Name = OtomeLines.MainName } });

            Subject.Choices(_author).Options.Single().BlockedReason.Should().Be("Another series is already named \"" + OtomeLines.MainName + "\"");

            var command = Switch(OtomeLines.MainId);

            command.Switched.Should().BeFalse();
            command.ResultMessage.Should().Be("Not switched: Another series is already named \"" + OtomeLines.MainName + "\"");
            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_series_already_on_the_line_blocks_the_switch()
        {
            var other = new Author { Id = 108, Metadata = new AuthorMetadata { Name = "Mobseka", TomeLineId = OtomeLines.MainId } };
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAllAuthors()).Returns(new List<Author> { _author, other });

            Subject.Choices(_author).Options.Single().BlockedReason.Should().Be("Mobseka is already bound to this line");
        }

        // An English series is resolved by its name: a name that finds another line would be undone by the refresh.
        [Test]
        public void a_line_whose_name_finds_another_line_is_blocked()
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTitle(OtomeLines.MainName, LibraryType.LightNovel)).Returns(OtomeLines.SpinOff());

            Subject.Choices(_author).Options.Single().BlockedReasonText.Should().NotBeNull();

            Switch(OtomeLines.MainId).Switched.Should().BeFalse();
            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_collected_light_novel_line_with_files_attached_is_blocked()
        {
            var omnibus = OtomeLines.MainLine();
            omnibus.IsOmnibus = true;
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetWorkLines(OtomeLines.WorkId)).Returns(new List<GcdSeries> { OtomeLines.SpinOff(), omnibus });

            Subject.Choices(_author).Options.Single().BlockedReason.Should().Be("Volume numbering differs between the two lines and 7 file(s) are attached");
        }

        // Staging, 2026-09-28 catalogue: the real main line's volumes run 320-546 pages, which the manga page-count
        // tell (median >= 320) reads as a collected edition. A light novel is judged by the flag and composition.
        [Test]
        public void a_light_novel_lines_page_counts_do_not_make_it_collected()
        {
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetVolumes(5101))
                  .Returns(Enumerable.Range(1, 13).Select(n => new GcdVolume { VolumeNumber = n, PageCount = n < 5 ? 320 : 428 }).ToList());
            Mocker.GetMock<IEditionResolver>().Setup(r => r.IsCollected(It.IsAny<GcdSeries>())).Returns(true);

            Subject.Choices(_author).Options.Single().BlockedReason.Should().BeNull();
        }

        [Test]
        public void a_collected_manga_line_with_files_attached_is_blocked()
        {
            var main = new GcdSeries { GcdSeriesId = 1, Name = "Kaiju No. 8", Language = "en", Medium = "manga", VolumeCount = 12, IsMain = true, TomeId = "rl_kaiju", TomeWorkId = "w_kaiju" };
            var relax = new GcdSeries { GcdSeriesId = 2, Name = "Kaiju No. 8: Relax", Language = "en", Medium = "manga", VolumeCount = 2, TomeId = "rl_relax", TomeWorkId = "w_kaiju" };
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTomeId("rl_relax")).Returns(relax);
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.GetWorkLines("w_kaiju")).Returns(new List<GcdSeries> { main, relax });
            Mocker.GetMock<IGcdMetadataService>().Setup(s => s.FindSeriesByTitle("Kaiju No. 8", LibraryType.Manga)).Returns(main);
            Mocker.GetMock<IEditionResolver>().Setup(r => r.IsCollected(It.Is<GcdSeries>(l => l.TomeId == "rl_kaiju"))).Returns(true);
            GivenEntry("rl_relax", "Kaiju No. 8: Relax", new[] { 1, 2 }, LibraryType.Manga);

            Subject.Choices(_author).Options.Single().BlockedReason.Should().Be("Volume numbering differs between the two lines and 2 file(s) are attached");
        }

        [Test]
        public void a_running_refresh_blocks_the_switch()
        {
            Mocker.GetMock<IManageCommandQueue>().Setup(q => q.GetStarted())
                  .Returns(new List<CommandModel> { new CommandModel { Body = new RefreshAuthorCommand(107), Status = CommandStatus.Started } });

            Subject.Choices(_author).Options.Single().BlockedReason.Should().NotBeNull();

            Switch(OtomeLines.MainId).Switched.Should().BeFalse();
            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_line_of_another_market_or_work_is_refused()
        {
            Switch("rl_otome_fr").Switched.Should().BeFalse();
            Switch("rl_otome_manga").Switched.Should().BeFalse();

            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(2);
        }

        // ---- the prompt afterwards ----

        // Yes: RetagFiles for the switched files (this series' own only) and SyncLightNovelTitles.
        [Test]
        public void yes_queues_the_retag_and_the_title_sync()
        {
            var refused = Subject.SyncExternal(_author, new List<int> { 1001, 2002, 5555 });

            refused.Should().BeNull();
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.Is<RetagFilesCommand>(c => c.AuthorId == 107 && c.Files.SequenceEqual(new[] { 1001, 2002 })), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.Is<SyncLightNovelTitlesCommand>(c => c.AuthorId == 107), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        // No is the client not calling: the switch itself never queues either command (see the switch tests).
        // Yes before the switch's refresh has finished would write the old line's titles: refused.
        [Test]
        public void yes_while_the_refresh_is_queued_writes_nothing()
        {
            Mocker.GetMock<IManageCommandQueue>().Setup(q => q.All())
                  .Returns(new List<CommandModel> { new CommandModel { Body = new RefreshAuthorCommand(107), Status = CommandStatus.Queued } });

            var refused = Subject.SyncExternal(_author, new List<int> { 1001 });

            refused.Should().NotBeNull();
            refused.English.Should().Be("A refresh is running for this series; try again when it finishes");
            VerifyNoExternalWrite();
        }

        [Test]
        public void yes_for_a_manga_series_writes_nothing()
        {
            GivenEntry(OtomeLines.SpinOffId, "Some manga", new[] { 1 }, LibraryType.Manga);

            Subject.SyncExternal(_author, new List<int> { 1001 }).Should().NotBeNull();
            VerifyNoExternalWrite();
        }
    }
}
