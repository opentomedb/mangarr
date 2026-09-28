using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.PreferredEdition
{
    // Preferred Edition (2026-09-24): what MangaSeriesMetadataProvider resolves for an English manga
    // and an English light novel from a fixed catalogue / AniList / Google / Audible answer.
    [TestFixture]
    public class EnEditionPinningProviderFixture : CoreTest<MangaSeriesMetadataProvider>
    {
        private const string Blurb = "Kafka Hibino dreams of joining the Defense Force, but a strange creature changes everything.";

        [SetUp]
        public void Setup()
        {
            var gcd = Mocker.GetMock<IGcdMetadataService>();
            gcd.SetupGet(s => s.Available).Returns(true);
            gcd.Setup(s => s.FindSeriesByTitle("Kaiju No. 8", LibraryType.Manga))
               .Returns(new GcdSeries { GcdSeriesId = 10, Name = "Kaiju No. 8", Language = "en", VolumeCount = 3, Status = "ongoing", YearBegan = 2021, IsMain = true, Medium = "manga" });
            gcd.Setup(s => s.FindSeriesByTitle("Sword Art Online", LibraryType.LightNovel))
               .Returns(new GcdSeries { GcdSeriesId = 20, Name = "Sword Art Online", Language = "en", VolumeCount = 2, Status = "ongoing", YearBegan = 2014, IsMain = true, Medium = "light_novel", Author = "Reki Kawahara" });
            gcd.Setup(s => s.GetVolumes(10)).Returns(new List<GcdVolume>
            {
                new GcdVolume { VolumeNumber = 1, ReleaseDate = "2021-12-21", Isbn13 = "9781974725984", PageCount = 200, CoverUrl = "https://covers.openlibrary.org/b/id/1-L.jpg", CoverSource = "openlibrary" },
                new GcdVolume { VolumeNumber = 2, ReleaseDate = "2022-04-19", Isbn13 = "9781974727247" },
                new GcdVolume { VolumeNumber = 3 }
            });
            gcd.Setup(s => s.GetVolumes(20)).Returns(new List<GcdVolume>
            {
                new GcdVolume { VolumeNumber = 1, ReleaseDate = "2014-04-22", Isbn13 = "9780316371247", Title = "Aincrad" },
                new GcdVolume { VolumeNumber = 2, ReleaseDate = "2014-08-26", Isbn13 = "9780316376815", Title = "Aincrad" }
            });
            gcd.Setup(s => s.GetAliases(It.IsAny<int>())).Returns(new List<string> { "kaiju no 8" });

            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.FindSeries(It.IsAny<string>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns<string, LibraryType, int?, IReadOnlyList<string>, bool, bool>((name, library, count, aliases, relaxed, isLineName) => new AniListSeries
                  {
                      Id = library == LibraryType.LightNovel ? 86302 : 108556,
                      EnglishTitle = name,
                      RomajiTitle = library == LibraryType.LightNovel ? "Sword Art Online" : "Kaijuu 8-gou",
                      NativeTitle = "怪獣８号",
                      Synonyms = new List<string> { "Monster #8", "Kaiju N°8" },
                      Description = "About " + name,
                      CoverImageUrl = "https://s4.anilist.co/bx1.jpg",
                      Status = "RELEASING",
                      Format = library == LibraryType.LightNovel ? "NOVEL" : "MANGA",
                      AverageScore = 83,
                      MatchedVia = "primary"
                  });

            Mocker.GetMock<IGoogleBooksService>()
                  .Setup(s => s.LookupByIsbn(It.IsAny<string>()))
                  .Returns<string>(isbn => new VolumeDetails
                  {
                      Title = isbn == "9780316371247" ? "Sword Art Online 1: Aincrad (light novel)" : "Kaiju No. 8, Vol. 1",
                      Description = Blurb,
                      Language = "en",
                      CoverUrl = "https://books.google.com/books/content?id=x&printsec=frontcover&img=1&zoom=1&edge=curl",
                      CoverSize = "thumbnail",
                      PageCount = 200
                  });

            Mocker.GetMock<IAudibleCatalogService>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                  .Returns(new List<AudibleProduct>
                  {
                      new AudibleProduct { Asin = "B00SAO0001", Title = "Sword Art Online 1", Subtitle = "Aincrad", Sequence = "1", RuntimeMinutes = 600 }
                  });
        }

        private static object Shape(MangaSeriesMetadata s)
        {
            return new
            {
                s.DisplayName,
                s.Status,
                s.VolumeCount,
                s.JapaneseTotal,
                s.Overview,
                s.CoverUrl,
                s.PosterSource,
                s.VolumeCoverUrl,
                s.RatingValue,
                s.AltTitles,
                s.AniListId,
                s.MatchedVia,
                s.Writer,
                s.ParentName,
                s.AudibleAnswered,
                Volumes = s.Volumes.Select(v => new
                {
                    v.VolumeNumber,
                    v.ReleaseDate,
                    v.Isbn13,
                    v.PageCount,
                    v.CoverUrl,
                    v.CoverSource,
                    v.Overview,
                    v.OverviewSource,
                    v.ArtifactTitle,
                    v.Subtitle,
                    v.GoogleMissed,
                    v.GoogleRejected,
                    Audio = v.Audio?.Asin,
                    v.CoveredByVolume
                }).ToList()
            };
        }

        [Test]
        public void english_manga_and_light_novel_are_pinned()
        {
            var manga = Subject.GetSeries("Kaiju No. 8", 0, true, LibraryType.Manga, null, "Kaiju No 8");
            var lightNovel = Subject.GetSeries("Sword Art Online", 0, true, LibraryType.LightNovel, null, "Sword Art Online");
            var lookup = Subject.GetSeries("Kaiju No. 8", 0, false, LibraryType.Manga, null, "Kaiju No 8");

            EnGolden.Pin("provider", new[] { Shape(manga), Shape(lightNovel), Shape(lookup) });

            // M5: the English path never enters the edition path.
            Mocker.GetMock<IEditionResolver>().VerifyNoOtherCalls();
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.FindSeriesByTomeId(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.GetWorkLines(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.GetAliasRows(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.FindSeriesByTitle(It.IsAny<string>(), It.IsAny<LibraryType>(), It.IsAny<IReadOnlyList<string>>()), Times.Never());
            Mocker.GetMock<IGcdMetadataService>().Verify(s => s.Markets(), Times.Never());

            // M6b: English never asks for locale covers or a language-restricted title search.
            Mocker.GetMock<IMangaDexService>().Verify(m => m.GetLocaleCovers(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IGoogleBooksService>().Verify(g => g.LookupVolume(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
        }
    }
}
