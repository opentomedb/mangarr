using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.PdfConversion;

namespace NzbDrone.Core.Test.MediaFiles.PdfConversion
{
    // The command doubles as a daily scheduled task. TaskManager only records LastExecution for
    // commands with UpdateScheduledTask true; without it the scheduler sees the task as
    // permanently overdue and refires it every tick (every 30 seconds, forever).
    [TestFixture]
    public class ConvertPdfToCbzCommandFixture
    {
        [Test]
        public void should_update_scheduled_task_so_the_daily_sweep_is_not_permanently_overdue()
        {
            new ConvertPdfToCbzCommand().UpdateScheduledTask.Should().BeTrue();
        }
    }
}
