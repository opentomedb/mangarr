using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation.Aggregators;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Aggregation.Aggregators
{
    [TestFixture]
    public class AggregateQualityFixture : CoreTest<AggregateQuality>
    {
        // Volumes built from chapter packs (2026-10-21): the built file is graded by its own name, whatever
        // the chapter release's title says, and whether or not the generic file-name parse reads it.
        [Test]
        public void a_chapter_built_volume_grades_cbz_chapters_over_the_release_title()
        {
            var localBook = new LocalBook
            {
                Path = @"C:\Temp\mangarr-chapter-volumes\abc123\Sample Series Vol. 09 (Built from chapters 72–81).cbz".AsOsAgnostic(),
                FileTrackInfo = new ParsedTrackInfo(),
                DownloadClientBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.CBZ) }
            };

            Subject.Aggregate(localBook, false).Quality.Quality.Should().Be(Quality.CbzChapters);
        }

        [Test]
        public void a_plain_volume_keeps_the_quality_it_parsed()
        {
            var localBook = new LocalBook
            {
                Path = @"C:\Downloads\Sample Series v09 (Digital).cbz".AsOsAgnostic(),
                FileTrackInfo = new ParsedTrackInfo(),
                DownloadClientBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.CBZ) }
            };

            Subject.Aggregate(localBook, false).Quality.Quality.Should().Be(Quality.CBZ);
        }
    }
}
