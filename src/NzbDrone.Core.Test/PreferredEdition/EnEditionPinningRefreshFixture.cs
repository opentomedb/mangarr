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
    // Preferred Edition (2026-09-24, final fix round Minor 11): the refresh of an EXISTING English
    // series that already records a TomeLineId (plan A3). The expected JSON was captured from
    // 7ef67b6, before the final fix round touched src/. It pins the binding fields as well as the
    // volumes: TomeLineId carried/recorded, EditionLanguage and AnchorName null, every volume's
    // ReleaseDatePrecision null (day).
    [TestFixture]
    public class EnEditionPinningRefreshFixture : CoreTest<BookInfoProxy>
    {
        private static Author Stored(string foreignAuthorId, string name, int aniListId, string tomeLineId)
        {
            return new Author
            {
                Id = aniListId,
                CleanName = name.ToLowerInvariant(),
                Monitored = true,
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = foreignAuthorId,
                    Name = name,
                    AniListId = aniListId,
                    TomeLineId = tomeLineId,
                    Aliases = new List<string>()
                }
            };
        }

        [SetUp]
        public void Setup()
        {
            var manga = Stored("local-kaiju-no-8", "Kaiju No. 8", 108556, "rl_kaiju8_en");
            var lightNovel = Stored("local-sword-art-online~ln", "Sword Art Online", 86302, "rl_sao_en");

            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-kaiju-no-8")).Returns(manga);
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById("local-sword-art-online~ln")).Returns(lightNovel);

            // The manga's catalogue answer names the same line; the light novel's names none (an older
            // artifact) -- the stored TomeLineId carries forward.
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Setup(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>()))
                  .Returns<string, int, bool, LibraryType, int?, string>((name, cap, details, library, anilistId, idName) => new MangaSeriesMetadata
                  {
                      DisplayName = name,
                      Status = AuthorStatusType.Continuing,
                      VolumeCount = 2,
                      JapaneseTotal = 12,
                      Overview = "About " + name + ".",
                      CoverUrl = "https://example.test/poster.jpg",
                      PosterSource = "opentome",
                      VolumeCoverUrl = "https://example.test/poster.jpg",
                      RatingValue = 4.15m,
                      AltTitles = new List<string> { name + " alias" },
                      AniListId = anilistId,
                      MatchedVia = "id",
                      TomeLineId = library == LibraryType.LightNovel ? null : "rl_kaiju8_en",
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
                author.Metadata.Value.AniListId,
                author.Metadata.Value.TomeLineId,
                author.Metadata.Value.EditionLanguage,
                author.Metadata.Value.AnchorName,
                author.Metadata.Value.Aliases,
                author.Metadata.Value.TotalVolumes,
                author.Metadata.Value.Overview,
                Books = author.Books.Value.Select(b => new
                {
                    b.ForeignBookId,
                    b.Title,
                    b.Subtitle,
                    b.VolumeNumber,
                    b.ReleaseDate,
                    b.ReleaseDatePrecision,
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
                        e.Overview
                    }).ToList()
                }).ToList()
            };
        }

        [Test]
        public void an_existing_english_series_refresh_is_pinned()
        {
            var manga = Subject.GetAuthorInfo("local-kaiju-no-8");
            var lightNovel = Subject.GetAuthorInfo("local-sword-art-online~ln");

            EnGolden.Pin("refresh", new[] { Shape(manga), Shape(lightNovel) });

            // The refresh asks by the stored name and the stored AniList id, on the six-argument call.
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries("Kaiju No. 8", It.IsAny<int>(), It.IsAny<bool>(), LibraryType.Manga, 108556, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IMangaSeriesMetadataProvider>()
                  .Verify(s => s.GetSeries(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<LibraryType>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<EditionRequest>()), Times.Never());
        }
    }
}
