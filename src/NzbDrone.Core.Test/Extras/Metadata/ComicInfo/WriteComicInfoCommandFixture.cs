using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Extras.Metadata.ComicInfo;

namespace NzbDrone.Core.Test.Extras.Metadata.ComicInfo
{
    // UI pass (2026-09-24, decision 1): AuthorId null is registered on System -> Tasks as the
    // library-wide "Embed Metadata" task. TaskManager only records LastExecution for
    // UpdateScheduledTask true; a per-series run (AuthorId set) must not stamp that task as just run.
    [TestFixture]
    public class WriteComicInfoCommandFixture
    {
        [Test]
        public void a_library_wide_run_updates_the_scheduled_task()
        {
            new WriteComicInfoCommand().UpdateScheduledTask.Should().BeTrue();
        }

        [Test]
        public void a_per_series_run_does_not_update_the_scheduled_task()
        {
            new WriteComicInfoCommand(1).UpdateScheduledTask.Should().BeFalse();
        }
    }
}
