using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books.Commands;

namespace NzbDrone.Core.Test.BookTests
{
    // Settings tidy (2026-09-23): AuthorId null is registered on System -> Tasks as the library-wide
    // "Sync Light Novel Titles" task. TaskManager only records LastExecution for UpdateScheduledTask
    // true; a per-entry run (AuthorId set) must not stamp that library-wide task as just run.
    [TestFixture]
    public class SyncLightNovelTitlesCommandFixture
    {
        [Test]
        public void a_library_wide_run_updates_the_scheduled_task()
        {
            new SyncLightNovelTitlesCommand().UpdateScheduledTask.Should().BeTrue();
        }

        [Test]
        public void a_per_entry_run_does_not_update_the_scheduled_task()
        {
            new SyncLightNovelTitlesCommand(1).UpdateScheduledTask.Should().BeFalse();
        }
    }
}
