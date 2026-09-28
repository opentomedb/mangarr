using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Reflection;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.OrganizerTests
{
    // Beta readiness (2026-09-28, review M8): saving naming config announces it, and the Audiobookshelf
    // health check re-runs on it, so Rename Volumes on clears its line without waiting for the schedule.
    [TestFixture]
    public class NamingConfigSavedEventFixture : CoreTest<NamingConfigService>
    {
        [Test]
        public void save_publishes_naming_config_saved()
        {
            Subject.Save(NamingConfig.Default);

            Mocker.GetMock<INamingConfigRepository>().Verify(r => r.Upsert(It.IsAny<NamingConfig>()), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<NamingConfigSavedEvent>()), Times.Once());
        }

        [Test]
        public void the_audiobookshelf_check_runs_on_a_naming_save()
        {
            typeof(AudiobookshelfCheck).GetAttributes<CheckOnAttribute>()
                .Select(a => a.EventType)
                .Should().Contain(typeof(NamingConfigSavedEvent));
        }
    }
}
