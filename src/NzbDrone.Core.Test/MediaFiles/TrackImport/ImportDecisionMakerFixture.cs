using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport
{
    [TestFixture]
    public class ImportDecisionMakerFixture : FileSystemTest<ImportDecisionMaker>
    {
        private List<IFileInfo> _fileInfos;
        private LocalBook _localTrack;
        private Author _author;
        private Book _book;
        private Edition _edition;
        private QualityModel _quality;

        private IdentificationOverrides _idOverrides;
        private ImportDecisionMakerConfig _idConfig;

        private Mock<IImportDecisionEngineSpecification<LocalEdition>> _bookpass1;
        private Mock<IImportDecisionEngineSpecification<LocalEdition>> _bookpass2;
        private Mock<IImportDecisionEngineSpecification<LocalEdition>> _bookpass3;

        private Mock<IImportDecisionEngineSpecification<LocalEdition>> _bookfail1;
        private Mock<IImportDecisionEngineSpecification<LocalEdition>> _bookfail2;
        private Mock<IImportDecisionEngineSpecification<LocalEdition>> _bookfail3;

        private Mock<IImportDecisionEngineSpecification<LocalBook>> _pass1;
        private Mock<IImportDecisionEngineSpecification<LocalBook>> _pass2;
        private Mock<IImportDecisionEngineSpecification<LocalBook>> _pass3;

        private Mock<IImportDecisionEngineSpecification<LocalBook>> _fail1;
        private Mock<IImportDecisionEngineSpecification<LocalBook>> _fail2;
        private Mock<IImportDecisionEngineSpecification<LocalBook>> _fail3;

        [SetUp]
        public void Setup()
        {
            _bookpass1 = new Mock<IImportDecisionEngineSpecification<LocalEdition>>();
            _bookpass2 = new Mock<IImportDecisionEngineSpecification<LocalEdition>>();
            _bookpass3 = new Mock<IImportDecisionEngineSpecification<LocalEdition>>();

            _bookfail1 = new Mock<IImportDecisionEngineSpecification<LocalEdition>>();
            _bookfail2 = new Mock<IImportDecisionEngineSpecification<LocalEdition>>();
            _bookfail3 = new Mock<IImportDecisionEngineSpecification<LocalEdition>>();

            _pass1 = new Mock<IImportDecisionEngineSpecification<LocalBook>>();
            _pass2 = new Mock<IImportDecisionEngineSpecification<LocalBook>>();
            _pass3 = new Mock<IImportDecisionEngineSpecification<LocalBook>>();

            _fail1 = new Mock<IImportDecisionEngineSpecification<LocalBook>>();
            _fail2 = new Mock<IImportDecisionEngineSpecification<LocalBook>>();
            _fail3 = new Mock<IImportDecisionEngineSpecification<LocalBook>>();

            _bookpass1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Accept());
            _bookpass2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Accept());
            _bookpass3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Accept());

            _bookfail1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Reject("_bookfail1"));
            _bookfail2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Reject("_bookfail2"));
            _bookfail3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Reject("_bookfail3"));

            _pass1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Accept());
            _pass2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Accept());
            _pass3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Accept());

            _fail1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Reject("_fail1"));
            _fail2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Reject("_fail2"));
            _fail3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>())).Returns(Decision.Reject("_fail3"));

            _author = Builder<Author>.CreateNew()
                .With(e => e.QualityProfileId = 1)
                .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                .Build();

            _book = Builder<Book>.CreateNew()
                .With(x => x.Author = _author)
                .Build();

            _edition = Builder<Edition>.CreateNew()
                .With(x => x.Book = _book)
                .Build();

            _quality = new QualityModel(Quality.MP3);

            _localTrack = new LocalBook
            {
                Author = _author,
                Quality = _quality,
                Book = new Book(),
                Path = @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV.avi".AsOsAgnostic()
            };

            _idOverrides = new IdentificationOverrides
            {
                Author = _author
            };

            _idConfig = new ImportDecisionMakerConfig();

            GivenAudioFiles(new List<string> { @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV.avi".AsOsAgnostic() });

            Mocker.GetMock<IIdentificationService>()
                .Setup(s => s.Identify(It.IsAny<List<LocalBook>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns((List<LocalBook> tracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config) =>
                {
                    var ret = new LocalEdition(tracks);
                    ret.Edition = _edition;
                    return new List<LocalEdition> { ret };
                });

            Mocker.GetMock<IMediaFileService>()
                .Setup(c => c.FilterUnchangedFiles(It.IsAny<List<IFileInfo>>(), It.IsAny<FilterFilesType>()))
                .Returns((List<IFileInfo> files, FilterFilesType filter) => files);

            Mocker.GetMock<IMetadataTagService>()
                .Setup(s => s.ReadTags(It.IsAny<IFileInfo>()))
                .Returns(new ParsedTrackInfo());

            Mocker.GetMock<IHistoryService>()
                .Setup(x => x.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<EntityHistory>());

            GivenSpecifications(_bookpass1);
        }

        private void GivenSpecifications<T>(params Mock<IImportDecisionEngineSpecification<T>>[] mocks)
        {
            Mocker.SetConstant(mocks.Select(c => c.Object));
        }

        private void GivenAudioFiles(IEnumerable<string> videoFiles)
        {
            foreach (var file in videoFiles)
            {
                FileSystem.AddFile(file, new MockFileData(string.Empty));
            }

            _fileInfos = videoFiles.Select(x => DiskProvider.GetFileInfo(x)).ToList();
        }

        private void GivenAugmentationSuccess()
        {
            Mocker.GetMock<IAugmentingService>()
                  .Setup(s => s.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()))
                  .Callback<LocalBook, bool>((localTrack, otherFiles) =>
                  {
                      localTrack.Book = _localTrack.Book;
                  });
        }

        [Test]
        public void should_call_all_book_specifications()
        {
            var downloadClientItem = Builder<DownloadClientItem>.CreateNew().Build();
            var itemInfo = new ImportDecisionMakerInfo { DownloadClientItem = downloadClientItem };

            GivenAugmentationSuccess();
            GivenSpecifications(_bookpass1, _bookpass2, _bookpass3, _bookfail1, _bookfail2, _bookfail3);

            Subject.GetImportDecisions(_fileInfos, null, itemInfo, _idConfig);

            _bookfail1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _bookfail2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _bookfail3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _bookpass1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _bookpass2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _bookpass3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>()), Times.Once());
        }

        [Test]
        public void should_call_all_track_specifications_if_book_accepted()
        {
            var downloadClientItem = Builder<DownloadClientItem>.CreateNew().Build();
            var itemInfo = new ImportDecisionMakerInfo { DownloadClientItem = downloadClientItem };

            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            Subject.GetImportDecisions(_fileInfos, null, itemInfo, _idConfig);

            _fail1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _fail2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _fail3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Once());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Once());
        }

        [Test]
        public void should_call_no_track_specifications_if_book_rejected()
        {
            var downloadClientItem = Builder<DownloadClientItem>.CreateNew().Build();
            var itemInfo = new ImportDecisionMakerInfo { DownloadClientItem = downloadClientItem };

            GivenAugmentationSuccess();
            GivenSpecifications(_bookpass1, _bookpass2, _bookpass3, _bookfail1, _bookfail2, _bookfail3);
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            Subject.GetImportDecisions(_fileInfos, null, itemInfo, _idConfig);

            _fail1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _fail2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _fail3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }

        [Test]
        public void should_return_rejected_if_only_book_spec_fails()
        {
            GivenSpecifications(_bookfail1);
            GivenSpecifications(_pass1);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_only_track_spec_fails()
        {
            GivenSpecifications(_bookpass1);
            GivenSpecifications(_fail1);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_one_book_spec_fails()
        {
            GivenSpecifications(_bookpass1, _bookfail1, _bookpass2, _bookpass3);
            GivenSpecifications(_pass1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_one_track_spec_fails()
        {
            GivenSpecifications(_bookpass1, _bookpass2, _bookpass3);
            GivenSpecifications(_pass1, _fail1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_approved_if_all_specs_pass()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_bookpass1, _bookpass2, _bookpass3);
            GivenSpecifications(_pass1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            result.Single().Approved.Should().BeTrue();
        }

        [Test]
        public void should_have_same_number_of_rejections_as_specs_that_failed()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            var result = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);
            result.Single().Rejections.Should().HaveCount(3);
        }

        [Test]
        public void should_not_blowup_the_process_due_to_failed_augment()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IAugmentingService>()
                  .Setup(c => c.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()))
                  .Throws<TestException>();

            GivenAudioFiles(new[]
                {
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic(),
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic(),
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic()
                });

            var decisions = Subject.GetImportDecisions(_fileInfos, _idOverrides, null, _idConfig);

            Mocker.GetMock<IAugmentingService>()
                  .Verify(c => c.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()), Times.Exactly(_fileInfos.Count));

            ExceptionVerification.ExpectedErrors(3);
        }

        [Test]
        public void should_not_throw_if_release_not_identified()
        {
            GivenSpecifications(_pass1);

            GivenAudioFiles(new[]
                {
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic(),
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic(),
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic()
                });

            Mocker.GetMock<IIdentificationService>()
                .Setup(s => s.Identify(It.IsAny<List<LocalBook>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns((List<LocalBook> tracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config) =>
                    {
                        return new List<LocalEdition> { new LocalEdition(tracks) };
                    });

            var decisions = Subject.GetImportDecisions(_fileInfos, _idOverrides, null, _idConfig);

            Mocker.GetMock<IAugmentingService>()
                  .Verify(c => c.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()), Times.Exactly(_fileInfos.Count));

            decisions.Should().HaveCount(3);
            decisions.First().Rejections.Should().NotBeEmpty();
        }

        [Test]
        public void should_not_throw_if_tracks_are_not_found()
        {
            GivenSpecifications(_pass1);

            GivenAudioFiles(new[]
                {
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic(),
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic(),
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic()
                });

            var decisions = Subject.GetImportDecisions(_fileInfos, _idOverrides, null, _idConfig);

            Mocker.GetMock<IAugmentingService>()
                  .Verify(c => c.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()), Times.Exactly(_fileInfos.Count));

            decisions.Should().HaveCount(3);
            decisions.First().Rejections.Should().NotBeEmpty();
        }

        [Test]
        public void should_return_a_decision_when_exception_is_caught()
        {
            Mocker.GetMock<IAugmentingService>()
                  .Setup(c => c.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()))
                  .Throws<TestException>();

            GivenAudioFiles(new[]
                {
                    @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.XviD-OSiTV".AsOsAgnostic()
                });

            Subject.GetImportDecisions(_fileInfos, _idOverrides, null, _idConfig).Should().HaveCount(1);

            ExceptionVerification.ExpectedErrors(1);
        }

        // LN PDF (2026-09-22): identification names the author; a light novel's PDF is then graded
        // Ebook PDF (revision kept) before any spec reads the quality. The identification mock
        // stands in for LocalEdition.PopulateMatch, which sets LocalBook.Author.
        private List<ImportDecision<LocalBook>> DecideWith(string path, Author author, QualityModel quality)
        {
            GivenAudioFiles(new List<string> { path });

            GivenSpecifications(_pass1);

            Mocker.GetMock<IAugmentingService>()
                  .Setup(s => s.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()))
                  .Callback<LocalBook, bool>((localTrack, otherFiles) =>
                  {
                      localTrack.Book = _localTrack.Book;
                      localTrack.Quality = quality;
                  });

            Mocker.GetMock<IIdentificationService>()
                .Setup(s => s.Identify(It.IsAny<List<LocalBook>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns((List<LocalBook> tracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config) =>
                {
                    tracks.ForEach(t => t.Author = author);
                    return new List<LocalEdition> { new LocalEdition(tracks) { Edition = _edition } };
                });

            return Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);
        }

        private static Author WithForeignId(string foreignAuthorId)
        {
            return new Author { Id = 3, QualityProfileId = 1, Metadata = new AuthorMetadata { ForeignAuthorId = foreignAuthorId } };
        }

        [Test]
        public void a_light_novel_pdf_is_graded_ebook_pdf_once_its_author_is_known()
        {
            var decisions = DecideWith(@"C:\Test\Unsorted\Overlord - Vol 005.pdf".AsOsAgnostic(),
                                       WithForeignId("local-overlord~ln"),
                                       new QualityModel(Quality.PDF, new Revision(version: 2)));

            decisions.Single().Item.Quality.Quality.Should().Be(Quality.EbookPdf);
            decisions.Single().Item.Quality.Revision.Version.Should().Be(2);
        }

        [Test]
        public void a_manga_pdf_keeps_the_manga_pdf_quality()
        {
            var decisions = DecideWith(@"C:\Test\Unsorted\Dandadan - Vol 001.pdf".AsOsAgnostic(),
                                       WithForeignId("local-dandadan"),
                                       new QualityModel(Quality.PDF));

            decisions.Single().Item.Quality.Quality.Should().Be(Quality.PDF);
        }

        [Test]
        public void a_light_novel_epub_keeps_its_quality()
        {
            var decisions = DecideWith(@"C:\Test\Unsorted\Overlord - Vol 005.epub".AsOsAgnostic(),
                                       WithForeignId("local-overlord~ln"),
                                       new QualityModel(Quality.EPUB));

            decisions.Single().Item.Quality.Quality.Should().Be(Quality.EPUB);
        }

        // LN PDF (2026-09-22) end-to-end-ish: the real MediaTypeMatchesEditionSpecification and
        // EbookPdfAllowedSpecification, not mocks, decide the per-file import, so the whole path --
        // author known, quality regraded to Ebook PDF, file typed and matched against its own
        // edition, the profile opt-in checked -- is exercised together. The author's ebook profile
        // wants Ebook PDF here (final review round, 2026-09-22); the unticked twin is below. The
        // manga twin stays PDF against its one Archive edition.
        [Test]
        public void a_light_novel_pdf_is_approved_against_the_ebook_edition_through_the_real_media_type_spec()
        {
            Mocker.SetConstant<IEnumerable<IImportDecisionEngineSpecification<LocalBook>>>(
                new List<IImportDecisionEngineSpecification<LocalBook>> { new MediaTypeMatchesEditionSpecification(TestLogger), new EbookPdfAllowedSpecification(TestLogger) });

            var lnAuthor = WithForeignId("local-overlord~ln");
            lnAuthor.QualityProfile = new QualityProfile
            {
                Items = new List<QualityProfileQualityItem> { new QualityProfileQualityItem { Quality = Quality.EbookPdf, Allowed = true } }
            };

            var lnVolume = new Book { Id = 9 };
            var lnEbook = lnVolume.WithEdition(MediaType.Ebook, id: 91);
            lnVolume.WithEdition(MediaType.Audio, id: 92);

            var lnPath = @"C:\Test\Unsorted\Overlord - Vol 005.pdf".AsOsAgnostic();
            GivenAudioFiles(new List<string> { lnPath });

            Mocker.GetMock<IAugmentingService>()
                  .Setup(s => s.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()))
                  .Callback<LocalBook, bool>((localTrack, otherFiles) =>
                  {
                      localTrack.Book = lnVolume;
                      localTrack.Quality = new QualityModel(Quality.PDF, new Revision(version: 2));
                  });

            Mocker.GetMock<IIdentificationService>()
                .Setup(s => s.Identify(It.IsAny<List<LocalBook>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns((List<LocalBook> tracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config) =>
                {
                    tracks.ForEach(t =>
                    {
                        t.Author = lnAuthor;
                        t.Edition = lnEbook;
                    });
                    return new List<LocalEdition> { new LocalEdition(tracks) { Edition = _edition } };
                });

            var decisions = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            decisions.Single().Approved.Should().BeTrue();
            decisions.Single().Item.Edition.Should().BeSameAs(lnEbook);
            decisions.Single().Item.Quality.Quality.Should().Be(Quality.EbookPdf);

            // The manga twin: same shape, no ~ln suffix -- the real spec keeps it PDF/Archive.
            var mangaAuthor = WithForeignId("local-dandadan");
            var manga = new Book { Id = 8 };
            var archive = manga.WithEdition(MediaType.Archive, id: 81);

            var mangaPath = @"C:\Test\Unsorted\Dandadan - Vol 001.pdf".AsOsAgnostic();
            GivenAudioFiles(new List<string> { mangaPath });

            Mocker.GetMock<IAugmentingService>()
                  .Setup(s => s.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()))
                  .Callback<LocalBook, bool>((localTrack, otherFiles) =>
                  {
                      localTrack.Book = manga;
                      localTrack.Quality = new QualityModel(Quality.PDF);
                  });

            Mocker.GetMock<IIdentificationService>()
                .Setup(s => s.Identify(It.IsAny<List<LocalBook>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns((List<LocalBook> tracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config) =>
                {
                    tracks.ForEach(t =>
                    {
                        t.Author = mangaAuthor;
                        t.Edition = archive;
                    });
                    return new List<LocalEdition> { new LocalEdition(tracks) { Edition = _edition } };
                });

            var mangaDecisions = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            mangaDecisions.Single().Approved.Should().BeTrue();
            mangaDecisions.Single().Item.Edition.Should().BeSameAs(archive);
            mangaDecisions.Single().Item.Quality.Quality.Should().Be(Quality.PDF);
        }

        // LN PDF final review (2026-09-22): the unticked twin -- the maintainer's default. With the real
        // EbookPdfAllowedSpecification registered alongside the real MediaTypeMatchesEditionSpecification
        // and the profile NOT wanting Ebook PDF, the decision is rejected with the "not wanted"
        // reason instead of silently importing (the quality is still regraded to Ebook PDF first --
        // the regrade and the profile gate are separate steps).
        [Test]
        public void a_light_novel_pdf_is_rejected_through_the_real_specs_when_ebook_pdf_is_not_allowed()
        {
            Mocker.SetConstant<IEnumerable<IImportDecisionEngineSpecification<LocalBook>>>(
                new List<IImportDecisionEngineSpecification<LocalBook>> { new MediaTypeMatchesEditionSpecification(TestLogger), new EbookPdfAllowedSpecification(TestLogger) });

            var lnAuthor = WithForeignId("local-overlord~ln");
            lnAuthor.QualityProfile = new QualityProfile
            {
                Items = new List<QualityProfileQualityItem> { new QualityProfileQualityItem { Quality = Quality.EbookPdf, Allowed = false } }
            };

            var lnVolume = new Book { Id = 9 };
            var lnEbook = lnVolume.WithEdition(MediaType.Ebook, id: 91);
            lnVolume.WithEdition(MediaType.Audio, id: 92);

            var lnPath = @"C:\Test\Unsorted\Overlord - Vol 005.pdf".AsOsAgnostic();
            GivenAudioFiles(new List<string> { lnPath });

            Mocker.GetMock<IAugmentingService>()
                  .Setup(s => s.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()))
                  .Callback<LocalBook, bool>((localTrack, otherFiles) =>
                  {
                      localTrack.Book = lnVolume;
                      localTrack.Quality = new QualityModel(Quality.PDF);
                  });

            Mocker.GetMock<IIdentificationService>()
                .Setup(s => s.Identify(It.IsAny<List<LocalBook>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns((List<LocalBook> tracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config) =>
                {
                    tracks.ForEach(t =>
                    {
                        t.Author = lnAuthor;
                        t.Edition = lnEbook;
                    });
                    return new List<LocalEdition> { new LocalEdition(tracks) { Edition = _edition } };
                });

            var decisions = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            decisions.Single().Approved.Should().BeFalse();
            decisions.Single().Rejections.Should().Contain(r => r.Reason == "Ebook PDF is not wanted in the light-novel profile");
            decisions.Single().Item.Quality.Quality.Should().Be(Quality.EbookPdf);
        }
    }
}
