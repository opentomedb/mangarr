using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.PreferredEdition
{
    // Preferred Edition (2026-09-24): the Author + volumes + editions BookInfoProxy mints for an
    // English manga and an English light novel from a fixed provider answer.
    [TestFixture]
    public class EnEditionPinningBookInfoFixture : CoreTest<BookInfoProxy>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById(It.IsAny<string>())).Returns((Author)null);

            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns<string, int, bool, LibraryType, int?, string>((name, cap, details, library, anilistId, idName) => new MangaSeriesMetadata
                  {
                      DisplayName = library == LibraryType.LightNovel ? "Sword Art Online" : "Kaiju No. 8",
                      Status = AuthorStatusType.Continuing,
                      VolumeCount = 2,
                      OriginTotal = 12,
                      Overview = "About the series.",
                      CoverUrl = "https://example.test/poster.jpg",
                      PosterSource = "opentome",
                      VolumeCoverUrl = "https://example.test/poster.jpg",
                      RatingValue = 4.15m,
                      AltTitles = new List<string> { "Kaijuu 8-gou" },
                      AniListId = 108556,
                      MatchedVia = "primary",
                      Volumes = new List<MangaVolumeMetadata>
                      {
                          new MangaVolumeMetadata { VolumeNumber = 1, ReleaseDate = new DateTime(2021, 12, 21, 6, 0, 0, DateTimeKind.Utc), Isbn13 = "9781974725984", PageCount = 200, CoverUrl = "https://example.test/v1.jpg", CoverSource = "opentome", Overview = "Volume one.", OverviewSource = "isbn", Subtitle = library == LibraryType.LightNovel ? "Aincrad" : null },
                          new MangaVolumeMetadata { VolumeNumber = 2, OverviewSource = "none", CoverSource = "series" }
                      }
                  });
        }

        private static object Shape(Author author)
        {
            return new
            {
                author.ForeignAuthorId,
                author.Name,
                author.CleanName,
                author.Metadata.Value.Aliases,
                author.Metadata.Value.AniListId,
                author.Metadata.Value.TotalVolumes,
                author.Metadata.Value.Overview,
                Images = author.Metadata.Value.Images.Select(i => i.Url).ToList(),
                Books = author.Books.Value.Select(b => new
                {
                    b.ForeignBookId,
                    b.Title,
                    b.Subtitle,
                    b.VolumeNumber,
                    b.ReleaseDate,
                    b.CleanTitle,
                    Editions = b.Editions.Value.Select(e => new
                    {
                        e.ForeignEditionId,
                        e.Title,
                        e.Language,
                        e.Isbn13,
                        e.Format,
                        e.MediaType,
                        e.Monitored,
                        e.PageCount,
                        e.Overview,
                        Images = e.Images.Select(i => i.Url).ToList()
                    }).ToList()
                }).ToList()
            };
        }

        [Test]
        public void english_manga_and_light_novel_are_pinned()
        {
            var manga = Subject.GetAuthorInfo("local-kaiju-no-8");
            var lightNovel = Subject.GetAuthorInfo("local-sword-art-online~ln");

            EnGolden.Pin("bookinfo", new[] { Shape(manga), Shape(lightNovel) });

            // M5: with the default chain the 7-argument (edition) overload is never called.
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()), Times.Never());

            // M6b: nor the edition-language ResolveVolume for an extra volume.
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.ResolveVolume(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never());
        }
    }
}
