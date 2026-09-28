using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    // The pins file sits behind a mocked IDiskProvider (as the Audible fixture's cache does): a test
    // supplies the file, and its mtime, or the service sees no file at all.
    [TestFixture]
    public class MetadataOverridesServiceFixture : CoreTest<MetadataOverridesService>
    {
        [SetUp]
        public void Setup()
        {
            WithTempAsAppPath();
        }

        private string OverridesPath => System.IO.Path.Combine(TestFolderInfo.AppDataFolder, "metadata", "overrides.json");

        // Load() re-reads the file only when its mtime moved from the last load (DateTime.MinValue
        // at start), so the mock's mtime must be a real one.
        private void GivenPinsFile(string json)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(OverridesPath))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileGetLastWrite(OverridesPath))
                  .Returns(new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc));
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.ReadAllText(OverridesPath))
                  .Returns(json);
        }

        [Test]
        public void a_pinned_date_is_local_midnight_in_utc_like_an_artifact_date()
        {
            // 2026-09-17: the same value MangaSeriesMetadataProvider.ParseDate produces, so a pinned
            // volume's stored row compares equal to the remote on the next pass.
            GivenPinsFile(@"{ ""Berserk"": { ""10"": { ""releaseDate"": ""2020-09-22"" } } }");
            var volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 10 } };

            Subject.Apply("Berserk", volumes);

            var expected = new DateTime(2020, 9, 22, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();
            volumes[0].ReleaseDate.Should().Be(expected);
            volumes[0].ReleaseDate.Value.Kind.Should().Be(DateTimeKind.Utc);
            volumes[0].ReleaseDate.Value.ToLocalTime().Date.Should().Be(new DateTime(2020, 9, 22));
        }

        // Preferred Edition (2026-09-24, D6): a pinned date is a day -- it drops the edition's coarse precision.
        [Test]
        public void a_pinned_date_clears_a_coarse_precision()
        {
            GivenPinsFile(@"{ ""L'Attaque des Titans"": { ""3"": { ""releaseDate"": ""2014-02-05"" } } }");
            var volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 3, ReleaseDatePrecision = "year" } };

            Subject.Apply("L'Attaque des Titans", volumes);

            volumes[0].ReleaseDatePrecision.Should().BeNull();
        }

        // Cover pin (B3b, 2026-09-18): a pinned cover is the operator's word over every provider.
        [Test]
        public void a_pinned_cover_replaces_the_volumes_cover_with_source_pin()
        {
            GivenPinsFile(@"{ ""Fairy Tail"": { ""24"": { ""coverUrl"": "" https://example.org/fairy-tail-24.jpg "" } } }");
            var volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 24, CoverUrl = "https://books.google.com/24", CoverSource = "google" } };

            Subject.Apply("Fairy Tail", volumes);

            volumes[0].CoverUrl.Should().Be("https://example.org/fairy-tail-24.jpg");
            volumes[0].CoverSource.Should().Be("pin");
        }

        // A hand-edited cover that is not an http(s) URL is skipped with one Warn, not handed to
        // MediaCoverService to fail on every pass.
        [Test]
        public void a_cover_pin_that_is_not_an_http_url_is_ignored_with_a_warning()
        {
            GivenPinsFile(@"{ ""Fairy Tail"": { ""24"": { ""coverUrl"": ""not a url"" } } }");
            var volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 24, CoverUrl = "https://books.google.com/24", CoverSource = "google" } };

            Subject.Apply("Fairy Tail", volumes);

            volumes[0].CoverUrl.Should().Be("https://books.google.com/24");
            volumes[0].CoverSource.Should().Be("google");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_pin_without_a_cover_leaves_the_cover_alone()
        {
            GivenPinsFile(@"{ ""Fairy Tail"": { ""24"": { ""pageCount"": 192 } } }");
            var volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 24, CoverUrl = "https://books.google.com/24", CoverSource = "google" } };

            Subject.Apply("Fairy Tail", volumes);

            volumes[0].PageCount.Should().Be(192);
            volumes[0].CoverUrl.Should().Be("https://books.google.com/24");
            volumes[0].CoverSource.Should().Be("google");
        }

        // A cover-only pin is a pin, not an empty row: Set keeps it, under the key a hand edit would use.
        [Test]
        public void a_cover_only_pin_is_written_not_removed()
        {
            Subject.Set("Fairy Tail", "24", new VolumeOverride { CoverUrl = "https://example.org/fairy-tail-24.jpg" });

            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.WriteAllText(OverridesPath + ".tmp", It.Is<string>(json => json.Contains("\"coverUrl\": \"https://example.org/fairy-tail-24.jpg\""))), Times.Once());
        }
        [Test]
        public void audiobook_title_pin_replaces_the_audible_product_title()
        {
            Subject.Set("Mushoku Tensei: Jobless Reincarnation (light novel)", "18", new VolumeOverride { AudiobookTitle = "Mushoku Tensei: Jobless Reincarnation, Vol. 18" });

            var volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 18, Audio = new AudiobookIdentity { Asin = "B0X", Title = "Mushoku Tensei: Jobless Reincarnatio, Vol. 18" } } };
            Subject.Apply("Mushoku Tensei: Jobless Reincarnation (light novel)", volumes);

            volumes[0].Audio.Title.Should().Be("Mushoku Tensei: Jobless Reincarnation, Vol. 18");
            volumes[0].Audio.Asin.Should().Be("B0X");
        }

        [Test]
        public void audiobook_title_pin_is_ignored_without_an_audio_identity()
        {
            Subject.Set("Mushoku Tensei: Jobless Reincarnation (light novel)", "18", new VolumeOverride { AudiobookTitle = "Mushoku Tensei: Jobless Reincarnation, Vol. 18" });

            var volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 18 } };
            Subject.Apply("Mushoku Tensei: Jobless Reincarnation (light novel)", volumes);

            volumes[0].Audio.Should().BeNull();
        }

        [Test]
        public void audiobook_title_pin_alone_is_not_an_empty_override()
        {
            new VolumeOverride { AudiobookTitle = "x" }.IsEmpty.Should().BeFalse();
        }

        // One copy each (2026-09-20): the series row ("*") pins the light novel's real author. The
        // writer ladder reads it through GetSeriesAuthor; Apply keys by volume number and never sees it.
        [Test]
        public void the_series_row_s_author_is_read_by_get_series_author()
        {
            GivenPinsFile(@"{ ""Mushoku Tensei: Jobless Reincarnation (light novel)"": { ""*"": { ""author"": "" Rifujin na Magonote "" }, ""18"": { ""pageCount"": 300 } } }");

            Subject.GetSeriesAuthor("Mushoku Tensei: Jobless Reincarnation (light novel)").Should().Be("Rifujin na Magonote");
        }

        [Test]
        public void get_series_author_is_null_without_a_series_row_or_a_series()
        {
            GivenPinsFile(@"{ ""Berserk"": { ""10"": { ""pageCount"": 240 } } }");

            Subject.GetSeriesAuthor("Berserk").Should().BeNull();
            Subject.GetSeriesAuthor("Vinland Saga").Should().BeNull();
            Subject.GetSeriesAuthor(null).Should().BeNull();
        }

        [Test]
        public void the_series_row_is_ignored_by_apply()
        {
            GivenPinsFile(@"{ ""Mushoku Tensei: Jobless Reincarnation (light novel)"": { ""*"": { ""author"": ""Rifujin na Magonote"" }, ""18"": { ""pageCount"": 300 } } }");
            var volumes = new List<MangaVolumeMetadata> { new MangaVolumeMetadata { VolumeNumber = 18 }, new MangaVolumeMetadata { VolumeNumber = 1 } };

            Subject.Apply("Mushoku Tensei: Jobless Reincarnation (light novel)", volumes);

            volumes[0].PageCount.Should().Be(300);
            volumes[1].PageCount.Should().Be(0);
        }

        [Test]
        public void an_author_pin_on_the_series_row_is_written_under_the_star_key()
        {
            Subject.Set("Mushoku Tensei: Jobless Reincarnation (light novel)", MetadataOverridesService.SeriesKey, new VolumeOverride { Author = "Rifujin na Magonote" });

            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.WriteAllText(OverridesPath + ".tmp", It.Is<string>(json => json.Contains("\"*\": {") && json.Contains("\"author\": \"Rifujin na Magonote\""))), Times.Once());
            Subject.GetSeriesAuthor("Mushoku Tensei: Jobless Reincarnation (light novel)").Should().Be("Rifujin na Magonote");
        }

        [Test]
        public void an_author_pin_alone_is_not_an_empty_override()
        {
            new VolumeOverride { Author = "x" }.IsEmpty.Should().BeFalse();
        }
    }
}
