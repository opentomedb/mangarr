using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Manual
{
    // Light novels (2026-09): the EPUB row and the audiobook row of one volume are re-resolved
    // against their OWN edition when the user changes a selection in the interactive-import grid.
    [TestFixture]
    public class ManualImportServiceUpdateItemsFixture : CoreTest<ManualImportService>
    {
        private Author _author;
        private Book _volume;
        private Edition _ebook;
        private Edition _audio;
        private List<IdentificationOverrides> _overrides;

        [SetUp]
        public void Setup()
        {
            _author = new Author { Id = 1, Name = "Overlord" };
            _volume = new Book { Id = 5, Title = "Overlord Vol. 5" };
            _ebook = _volume.WithEdition(MediaType.Ebook, id: 51);
            _audio = _volume.WithEdition(MediaType.Audio, id: 52);
            _overrides = new List<IdentificationOverrides>();

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetFileInfo(It.IsAny<string>()))
                .Returns<string>(path =>
                {
                    var info = new Mock<IFileInfo>();
                    info.SetupGet(i => i.FullName).Returns(path);
                    return info.Object;
                });

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((files, overrides, info, config) => _overrides.Add(overrides))
                .Returns<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((files, overrides, info, config) =>
                    files.Select(f => new ImportDecision<LocalBook>(new LocalBook
                    {
                        Path = f.FullName,
                        Author = overrides.Author,
                        Book = overrides.Book,
                        Edition = overrides.Edition,
                        FileTrackInfo = new ParsedTrackInfo()
                    })).ToList());
        }

        private ManualImportItem Item(int id, string path, Edition edition)
        {
            return new ManualImportItem
            {
                Id = id,
                Path = path,
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                Author = _author,
                Book = _volume,
                Edition = edition,
                Rejections = new List<NzbDrone.Core.DecisionEngine.Rejection>()
            };
        }

        [Test]
        public void update_items_re_resolves_each_edition_of_a_volume_on_its_own()
        {
            var items = new List<ManualImportItem>
            {
                Item(1, "/downloads/Overlord/Overlord - Vol 005.epub", _ebook),
                Item(2, "/downloads/Overlord/Overlord - Vol 005 - 01.mp3", _audio),
                Item(3, "/downloads/Overlord/Overlord - Vol 005 - 02.mp3", _audio)
            };

            var result = Subject.UpdateItems(items);

            _overrides.Should().HaveCount(2);
            _overrides.Select(o => o.Edition).Should().BeEquivalentTo(new[] { _ebook, _audio });

            result.Single(i => i.Id == 1).Edition.Should().BeSameAs(_ebook);
            result.Where(i => i.Id != 1).Should().OnlyContain(i => i.Edition == _audio);
        }

        [Test]
        public void update_items_keeps_one_group_per_manga_volume()
        {
            var manga = new Book { Id = 6, Title = "Dandadan Vol. 1" };
            var archive = manga.WithEdition(MediaType.Archive, id: 61);

            var items = new List<ManualImportItem>
            {
                new ManualImportItem { Id = 1, Path = "/downloads/Dandadan/Dandadan - Vol 001.cbz", Name = "Dandadan - Vol 001", Author = _author, Book = manga, Edition = archive, Rejections = new List<NzbDrone.Core.DecisionEngine.Rejection>() }
            };

            Subject.UpdateItems(items);

            _overrides.Should().HaveCount(1);
            _overrides[0].Edition.Should().BeSameAs(archive);
        }
    }
}
