using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Collections;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Collections
{
    // A collection member that is not in the library gets its poster from the artifact's own
    // volume cover, else from AniList by id -- asked once and kept on disk.
    [TestFixture]
    public class CollectionPosterServiceFixture : CoreTest<CollectionPosterService>
    {
        private const int AnilistId = 555;
        private GcdSeries _line;

        [SetUp]
        public void Setup()
        {
            WithTempAsAppPath();

            _line = new GcdSeries { GcdSeriesId = 10, Name = "Attack on Titan (Before the Fall)", AnilistId = AnilistId };

            GivenVolumes();
        }

        private string CachePath => Path.Combine(TestFolderInfo.AppDataFolder, "metadata", "collection-posters.json");

        private void GivenVolumes(params GcdVolume[] volumes)
        {
            Mocker.GetMock<IGcdMetadataService>()
                  .Setup(s => s.GetVolumes(_line.GcdSeriesId))
                  .Returns(volumes.ToList());
        }

        private void GivenAniList(string coverUrl)
        {
            Mocker.GetMock<IAniListService>()
                  .Setup(s => s.GetById(AnilistId))
                  .Returns(coverUrl == null ? null : new AniListSeries { Id = AnilistId, CoverImageUrl = coverUrl });
        }

        private void GivenOnDiskEntry(DateTime fetchedAt, string url)
        {
            var store = new Dictionary<string, CollectionPosterService.CacheEntry>
            {
                [AnilistId.ToString()] = new CollectionPosterService.CacheEntry { FetchedAt = fetchedAt, Url = url }
            };

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(CachePath))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.ReadAllText(CachePath))
                  .Returns(store.ToJson());
        }

        private static GcdVolume Volume(int number, string coverUrl)
        {
            return new GcdVolume { VolumeNumber = number, CoverUrl = coverUrl };
        }

        [Test]
        public void the_artifact_cover_of_the_lowest_covered_volume_is_the_poster()
        {
            GivenVolumes(Volume(1, null), Volume(2, "https://covers/vol2.jpg"), Volume(3, "https://covers/vol3.jpg"));
            GivenAniList("https://anilist/art.jpg");

            Subject.PosterFor(_line).Should().Be("https://covers/vol2.jpg");

            Mocker.GetMock<IAniListService>().Verify(v => v.GetById(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void anilist_art_is_the_poster_when_the_artifact_has_no_cover()
        {
            GivenVolumes(Volume(1, null), Volume(2, null));
            GivenAniList("https://anilist/art.jpg");

            Subject.PosterFor(_line).Should().Be("https://anilist/art.jpg");
        }

        [Test]
        public void an_anilist_answer_is_asked_once_and_written_to_disk()
        {
            GivenAniList("https://anilist/art.jpg");

            string written = null;
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.WriteAllText(CachePath + ".tmp", It.IsAny<string>()))
                  .Callback<string, string>((path, text) => written = text);

            Subject.PosterFor(_line).Should().Be("https://anilist/art.jpg");
            Subject.PosterFor(_line).Should().Be("https://anilist/art.jpg");

            Mocker.GetMock<IAniListService>().Verify(v => v.GetById(AnilistId), Times.Once());
            written.Should().Contain("https://anilist/art.jpg");
        }

        [Test]
        public void a_stored_answer_is_read_from_disk_instead_of_anilist()
        {
            GivenOnDiskEntry(DateTime.UtcNow.AddDays(-100), "https://disk/art.jpg");
            GivenAniList("https://anilist/art.jpg");

            Subject.PosterFor(_line).Should().Be("https://disk/art.jpg");

            Mocker.GetMock<IAniListService>().Verify(v => v.GetById(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void a_fresh_miss_is_not_asked_again()
        {
            GivenOnDiskEntry(DateTime.UtcNow.AddDays(-1), null);
            GivenAniList("https://anilist/art.jpg");

            Subject.PosterFor(_line).Should().BeNull();

            Mocker.GetMock<IAniListService>().Verify(v => v.GetById(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void a_miss_is_asked_again_after_a_week()
        {
            GivenOnDiskEntry(DateTime.UtcNow.AddDays(-8), null);
            GivenAniList("https://anilist/art.jpg");

            Subject.PosterFor(_line).Should().Be("https://anilist/art.jpg");

            Mocker.GetMock<IAniListService>().Verify(v => v.GetById(AnilistId), Times.Once());
        }

        [Test]
        public void a_line_anilist_does_not_know_has_no_poster()
        {
            GivenAniList(null);

            Subject.PosterFor(_line).Should().BeNull();
        }

        [Test]
        public void a_line_without_an_anilist_id_or_cover_has_no_poster()
        {
            _line.AnilistId = null;

            Subject.PosterFor(_line).Should().BeNull();

            Mocker.GetMock<IAniListService>().Verify(v => v.GetById(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void no_line_means_no_poster()
        {
            Subject.PosterFor(null).Should().BeNull();
        }
    }
}
