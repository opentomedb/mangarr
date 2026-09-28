using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Extras;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class ImportApprovedTracksFixture : CoreTest<ImportApprovedBooks>
    {
        private List<ImportDecision<LocalBook>> _rejectedDecisions;
        private List<ImportDecision<LocalBook>> _approvedDecisions;

        private DownloadClientItem _downloadClientItem;
        private DownloadClientItemClientInfo _clientInfo;
        private Author _author;

        [SetUp]
        public void Setup()
        {
            _rejectedDecisions = new List<ImportDecision<LocalBook>>();
            _approvedDecisions = new List<ImportDecision<LocalBook>>();

            _author = Builder<Author>.CreateNew()
                                        .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                        .With(s => s.Path = @"C:\Test\Music\Alien Ant Farm".AsOsAgnostic())
                                        .Build();

            var book = Builder<Book>.CreateNew()
                .With(e => e.Author = _author)
                .Build();

            var edition = Builder<Edition>.CreateNew()
                .With(e => e.Book = book)
                .With(e => e.Monitored = true)
                .Build();

            book.Editions = new List<Edition> { edition };

            var rootFolder = Builder<RootFolder>.CreateNew()
                .With(r => r.IsCalibreLibrary = false)
                .Build();

            _rejectedDecisions.Add(new ImportDecision<LocalBook>(new LocalBook(), new Rejection("Rejected!")));
            _rejectedDecisions.Add(new ImportDecision<LocalBook>(new LocalBook(), new Rejection("Rejected!")));
            _rejectedDecisions.Add(new ImportDecision<LocalBook>(new LocalBook(), new Rejection("Rejected!")));

            _approvedDecisions.Add(new ImportDecision<LocalBook>(
                                       new LocalBook
                                       {
                                           Author = _author,
                                           Book = book,
                                           Edition = edition,
                                           Part = 1,
                                           Path = Path.Combine(_author.Path, "Alien Ant Farm - 01 - Pilot.mp3"),
                                           Quality = new QualityModel(Quality.MP3),
                                           FileTrackInfo = new ParsedTrackInfo
                                           {
                                               ReleaseGroup = "DRONE"
                                           }
                                       }));

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>()))
                  .Returns(new BookFileMoveResult());

            _clientInfo = Builder<DownloadClientItemClientInfo>.CreateNew().Build();
            _downloadClientItem = Builder<DownloadClientItem>.CreateNew().With(x => x.DownloadClientInfo = _clientInfo).Build();

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByBook(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder(It.IsAny<string>()))
                .Returns(rootFolder);

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitored(edition))
                .Returns(new List<Edition> { edition });
        }

        [Test]
        public void should_not_import_any_if_there_are_no_approved_decisions()
        {
            Subject.Import(_rejectedDecisions, false).Where(i => i.Result == ImportResultType.Imported).Should().BeEmpty();

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.IsAny<BookFile>()), Times.Never());
        }

        [Test]
        public void should_import_each_approved()
        {
            Subject.Import(_approvedDecisions, false).Should().HaveCount(1);
        }

        [Test]
        public void should_only_import_approved()
        {
            var all = new List<ImportDecision<LocalBook>>();
            all.AddRange(_rejectedDecisions);
            all.AddRange(_approvedDecisions);

            var result = Subject.Import(all, false);

            result.Should().HaveCount(all.Count);
            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_only_import_each_track_once()
        {
            var all = new List<ImportDecision<LocalBook>>();
            all.AddRange(_approvedDecisions);
            all.Add(new ImportDecision<LocalBook>(_approvedDecisions.First().Item));

            var result = Subject.Import(all, false);

            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_move_new_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, false),
                          Times.Once());
        }

        [Test]
        public void should_publish_TrackImportedEvent_for_new_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<TrackImportedEvent>()), Times.Once());
        }

        // Beta readiness (2026-09-28, review M4): Write ComicInfo To = New Downloads keys on the published
        // BookImportedEvent's DownloadId, which comes from the download-client item the caller passes.
        [Test]
        public void book_imported_event_carries_the_download_id_of_a_queue_item()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId("SABnzbd_nzo_abc123"))
                .Returns(new List<EntityHistory>());

            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true,
                new DownloadClientItem { Title = "Alien.Ant.Farm-Truant", DownloadId = "SABnzbd_nzo_abc123", CanMoveFiles = true, DownloadClientInfo = _clientInfo });

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookImportedEvent>(e => e.DownloadId == "SABnzbd_nzo_abc123")), Times.Once());
        }

        [Test]
        public void book_imported_event_has_no_download_id_for_loose_files()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookImportedEvent>(e => e.DownloadId == null)), Times.Once());
        }

        [Test]
        public void should_not_move_existing_files()
        {
            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            Subject.Import(new List<ImportDecision<LocalBook>> { track }, false);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, false),
                          Times.Never());
        }

        [Test]
        public void should_import_higher_quality_files_first()
        {
            var lqDecision = _approvedDecisions.First();
            lqDecision.Item.Quality = new QualityModel(Quality.CBZ);
            lqDecision.Item.Size = 10.Megabytes();

            var hqDecision = new ImportDecision<LocalBook>(
                new LocalBook
                {
                    Author = lqDecision.Item.Author,
                    Book = lqDecision.Item.Book,
                    Edition = lqDecision.Item.Edition,
                    Part = 1,
                    Path = @"C:\Test\Music\Alien Ant Farm\Alien Ant Farm - 01 - Pilot.mp3".AsOsAgnostic(),
                    Quality = new QualityModel(Quality.ZIP),
                    Size = 1.Megabytes(),
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        ReleaseGroup = "DRONE"
                    }
                });

            var all = new List<ImportDecision<LocalBook>>();
            all.Add(lqDecision);
            all.Add(hqDecision);

            var results = Subject.Import(all, false);

            results.Should().HaveCount(all.Count);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported && d.ImportDecision.Item.Size == hqDecision.Item.Size);
        }

        [Test]
        public void should_import_larger_files_for_same_quality_first()
        {
            var fileDecision = _approvedDecisions.First();
            fileDecision.Item.Size = 1.Gigabytes();

            var sampleDecision = new ImportDecision<LocalBook>(
                new LocalBook
                {
                    Author = fileDecision.Item.Author,
                    Book = fileDecision.Item.Book,
                    Edition = fileDecision.Item.Edition,
                    Part = 1,
                    Path = @"C:\Test\Music\Alien Ant Farm\Alien Ant Farm - 01 - Pilot.mp3".AsOsAgnostic(),
                    Quality = new QualityModel(Quality.MP3),
                    Size = 80.Megabytes()
                });

            var all = new List<ImportDecision<LocalBook>>();
            all.Add(fileDecision);
            all.Add(sampleDecision);

            var results = Subject.Import(all, false);

            results.Should().HaveCount(all.Count);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported && d.ImportDecision.Item.Size == fileDecision.Item.Size);
        }

        [Test]
        public void should_copy_when_cannot_move_files_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true, new DownloadClientItem { Title = "Alien.Ant.Farm-Truant", CanMoveFiles = false, DownloadClientInfo = _clientInfo });

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, true), Times.Once());
        }

        [Test]
        public void should_use_override_importmode()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true, new DownloadClientItem { Title = "Alien.Ant.Farm-Truant", CanMoveFiles = false, DownloadClientInfo = _clientInfo }, ImportMode.Move);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, false), Times.Once());
        }

        [Test]
        public void should_delete_existing_trackfiles_with_the_same_path()
        {
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(It.IsAny<string>()))
                .Returns(Builder<BookFile>.CreateNew().Build());

            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            Subject.Import(new List<ImportDecision<LocalBook>> { track }, false);

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.IsAny<BookFile>(), DeleteMediaFileReason.ManualOverride), Times.Once());
        }

        // One copy each (2026-09-20): where the file lives and whether it was adopted in place
        // are decided on the LocalBook and must reach the stored row.
        [Test]
        public void should_carry_home_and_adopted_from_the_local_book()
        {
            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            track.Item.Home = FileHome.Audiobooks;
            track.Item.Adopted = true;

            Subject.Import(new List<ImportDecision<LocalBook>> { track }, false);

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.AddMany(It.Is<List<BookFile>>(f => f.Count == 1 && f[0].Home == FileHome.Audiobooks && f[0].Adopted)), Times.Once());
        }

        // One copy each (2026-09-20, final review): Adopt Existing registers an original through this
        // pipeline with NewDownload = false, which ImportDecisionMaker turns into ExistingFile = true.
        // That one flag is the whole "an adopted original is never moved" guarantee at this seam: no
        // upgrader (so no mover, no calibre add, no recycle of old files), no extras, no disk move.
        [Test]
        public void an_adopted_original_is_registered_in_place_and_never_handed_to_the_upgrader()
        {
            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            track.Item.Adopted = true;
            track.Item.Home = FileHome.Calibre;
            track.Item.CalibreId = 7;
            track.Item.Path = "/books/Author/Title (7)/Title.epub";

            Subject.Import(new List<ImportDecision<LocalBook>> { track }, false);

            Mocker.GetMock<IUpgradeMediaFiles>().Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IExtraService>().Verify(v => v.ImportTrack(It.IsAny<LocalBook>(), It.IsAny<BookFile>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.AddMany(It.Is<List<BookFile>>(f => f.Count == 1 && f[0].Home == FileHome.Calibre && f[0].Adopted && f[0].CalibreId == 7 && f[0].Path == "/books/Author/Title (7)/Title.epub")), Times.Once());
        }

        // Light novels (2026-09): the EPUB and the audiobook of one volume are two editions.
        private (Book Volume, Edition Ebook, Edition Audio) GivenLightNovelVolume()
        {
            var volume = new Book
            {
                Id = 50,
                Title = "Overlord Vol. 5",
                ForeignBookId = "local-overlord~ln-v5",
                AuthorMetadataId = _author.AuthorMetadataId,
                Author = _author
            };

            var ebook = volume.WithEdition(MediaType.Ebook, id: 501);
            ebook.ForeignEditionId = "local-overlord~ln-v5-ed";

            var audio = volume.WithEdition(MediaType.Audio, id: 502);
            audio.ForeignEditionId = "local-overlord~ln-v5-audio-ed";

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitored(It.IsAny<Edition>()))
                .Returns(volume.Editions.Value);

            return (volume, ebook, audio);
        }

        private ImportDecision<LocalBook> Decision(Book volume, Edition edition, string fileName, Quality quality, int part = 1)
        {
            return new ImportDecision<LocalBook>(new LocalBook
            {
                Author = _author,
                Book = volume,
                Edition = edition,
                Part = part,
                Path = Path.Combine(_author.Path, "Overlord - Vol. 5", fileName),
                Quality = new QualityModel(quality),
                FileTrackInfo = new ParsedTrackInfo { ReleaseGroup = "DRONE" }
            });
        }

        [Test]
        public void should_import_the_epub_and_the_audiobook_of_one_volume_to_their_own_editions()
        {
            var (volume, ebook, audio) = GivenLightNovelVolume();

            var decisions = new List<ImportDecision<LocalBook>>
            {
                Decision(volume, ebook, "Overlord - Vol 005.epub", Quality.EPUB),
                Decision(volume, audio, "Overlord - Vol 005.m4b", Quality.M4B)
            };

            var results = Subject.Import(decisions, true);

            results.Where(r => r.Result == ImportResultType.Imported).Should().HaveCount(2);

            Mocker.GetMock<IEditionService>().Verify(v => v.SetMonitored(ebook), Times.Once());
            Mocker.GetMock<IEditionService>().Verify(v => v.SetMonitored(audio), Times.Once());

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.AddMany(It.Is<List<BookFile>>(f => f.Count == 2 && f.Any(x => x.EditionId == ebook.Id) && f.Any(x => x.EditionId == audio.Id))), Times.Once());

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookImportedEvent>(e => e.ImportedBooks.Count == 1 && e.ImportedBooks[0].EditionId == ebook.Id)), Times.Once());

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookImportedEvent>(e => e.ImportedBooks.Count == 1 && e.ImportedBooks[0].EditionId == audio.Id)), Times.Once());
        }

        [Test]
        public void should_number_audio_parts_within_the_audio_edition_only()
        {
            var (volume, ebook, audio) = GivenLightNovelVolume();

            var decisions = new List<ImportDecision<LocalBook>>
            {
                Decision(volume, ebook, "Overlord - Vol 005.epub", Quality.EPUB),
                Decision(volume, audio, "Overlord - Vol 005 - 02.mp3", Quality.MP3, part: 0),
                Decision(volume, audio, "Overlord - Vol 005 - 01.mp3", Quality.MP3, part: 0),
                Decision(volume, audio, "Overlord - Vol 005 - 03.mp3", Quality.MP3, part: 0)
            };

            var results = Subject.Import(decisions, true);

            results.Where(r => r.Result == ImportResultType.Imported).Should().HaveCount(4);
            decisions.Where(d => d.Item.Edition == audio).OrderBy(d => d.Item.Path).Select(d => d.Item.Part).Should().Equal(1, 2, 3);
            decisions.Single(d => d.Item.Edition == ebook).Item.Part.Should().Be(1);
        }

        [Test]
        public void should_flip_audio_available_on_the_first_audio_import()
        {
            var (volume, _, audio) = GivenLightNovelVolume();
            _author.AudioAvailable = false;

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.GetAuthor(_author.Id))
                .Returns(_author);

            Subject.Import(new List<ImportDecision<LocalBook>> { Decision(volume, audio, "Overlord - Vol 005.m4b", Quality.M4B) }, true);

            Mocker.GetMock<IAuthorService>()
                .Verify(v => v.UpdateAuthor(It.Is<Author>(a => a.Id == _author.Id && a.AudioAvailable)), Times.Once());
        }

        [Test]
        public void should_not_touch_audio_available_for_an_epub_import()
        {
            var (volume, ebook, _) = GivenLightNovelVolume();
            _author.AudioAvailable = false;

            Subject.Import(new List<ImportDecision<LocalBook>> { Decision(volume, ebook, "Overlord - Vol 005.epub", Quality.EPUB) }, true);

            Mocker.GetMock<IAuthorService>().Verify(v => v.UpdateAuthor(It.IsAny<Author>()), Times.Never());
        }
    }
}
