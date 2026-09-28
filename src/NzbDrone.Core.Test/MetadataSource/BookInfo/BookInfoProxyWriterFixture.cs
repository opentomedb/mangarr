using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    // One copy each (2026-09-20): the writer ladder runs on an add or a refresh (a resolve by id)
    // and never on a search candidate -- it asks calibre, and a candidate's Writer is never used.
    // A lookup that skips the ladder carries the stored Writer forward rather than erasing it.
    [TestFixture]
    public class BookInfoProxyWriterFixture : CoreTest<BookInfoProxy>
    {
        private const string LightNovelId = "local-overlord~ln";
        private const string MangaId = "local-overlord";
        private const string PinKey = "Overlord (light novel)";

        private static Author Stored(string id, string writer)
        {
            return new Author
            {
                Id = 7,
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = id,
                    Name = "Overlord",
                    Writer = writer
                }
            };
        }

        private void GivenStored(string id, string writer)
        {
            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.FindById(id))
                  .Returns(Stored(id, writer));
        }

        private void GivenResolved(string catalogueWriter)
        {
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns(new MangaSeriesMetadata
                  {
                      DisplayName = "Overlord",
                      VolumeCount = 1,
                      JapaneseTotal = 16,
                      Volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 1 } },
                      Writer = catalogueWriter
                  });
        }

        [Test]
        public void a_refresh_by_id_takes_the_writer_from_the_ladder()
        {
            GivenStored(LightNovelId, "Stored Writer");
            GivenResolved("kugane maruyama");
            Mocker.GetMock<IWriterResolver>()
                  .Setup(s => s.Resolve(It.Is<Author>(a => a.Id == 7), PinKey, "kugane maruyama"))
                  .Returns("Kugane Maruyama");

            var author = Subject.GetAuthorInfo(LightNovelId);

            author.Metadata.Value.Writer.Should().Be("Kugane Maruyama");
            Mocker.GetMock<IWriterResolver>()
                  .Verify(s => s.Resolve(It.IsAny<Author>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void a_title_search_skips_the_ladder_and_carries_the_stored_writer()
        {
            GivenStored(LightNovelId, "Stored Writer");
            GivenResolved("kugane maruyama");

            var candidates = Subject.SearchForNewAuthor("Overlord", LibraryType.LightNovel);

            candidates.Should().HaveCount(1);
            candidates[0].Metadata.Value.Writer.Should().Be("Stored Writer");
            Mocker.GetMock<IWriterResolver>()
                  .Verify(s => s.Resolve(It.IsAny<Author>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_manga_refresh_has_no_writer_and_never_asks_the_ladder()
        {
            GivenStored(MangaId, null);
            GivenResolved(null);

            var author = Subject.GetAuthorInfo(MangaId);

            author.Metadata.Value.Writer.Should().BeNull();
            Mocker.GetMock<IWriterResolver>()
                  .Verify(s => s.Resolve(It.IsAny<Author>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }
    }
}
