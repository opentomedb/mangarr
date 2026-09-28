using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books.Calibre;

namespace NzbDrone.Core.Test.BookTests.Calibre
{
    // Settings tidy (2026-09-23): AuthorId null is registered on System -> Tasks as the library-wide
    // "Convert Light Novel Format" task. TaskManager only records LastExecution for
    // UpdateScheduledTask true; a per-series run (AuthorId set, pushed from the series toolbar) must
    // not stamp that library-wide task as just run.
    [TestFixture]
    public class ConvertLightNovelFormatCommandFixture
    {
        [Test]
        public void a_library_wide_run_updates_the_scheduled_task()
        {
            new ConvertLightNovelFormatCommand().UpdateScheduledTask.Should().BeTrue();
        }

        [Test]
        public void a_per_series_run_does_not_update_the_scheduled_task()
        {
            new ConvertLightNovelFormatCommand(1).UpdateScheduledTask.Should().BeFalse();
        }
    }
}
