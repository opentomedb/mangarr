using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Extras.Metadata.ComicInfo;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaFiles.PdfConversion;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.JobTests
{
    [TestFixture]
    public class TaskManagerFixture : CoreTest<TaskManager>
    {
        private List<ScheduledTask> _existingTasks;
        private List<ScheduledTask> _upserted;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());

            _existingTasks = new List<ScheduledTask>();
            _upserted = new List<ScheduledTask>();

            Mocker.GetMock<IScheduledTaskRepository>()
                  .Setup(s => s.All())
                  .Returns(() => _existingTasks);

            Mocker.GetMock<IScheduledTaskRepository>()
                  .Setup(s => s.Upsert(It.IsAny<ScheduledTask>()))
                  .Returns<ScheduledTask>(t => t)
                  .Callback<ScheduledTask>(t => _upserted.Add(t));

            GivenBookSearchInterval(360);
        }

        private void GivenBookSearchInterval(int interval)
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.BookSearchInterval)
                  .Returns(interval);
        }

        // Final review I1 (2026-09-20): the cutoff-unmet search runs weekly, not on the missing
        // search's interval -- an upgrade hunt over volumes we already hold is worth far less
        // indexer load, and the two maturing on the same tick doubled the fan-out. Turning the
        // missing search off (0) turns this one off with it.
        [TestCase(360, 10080)]
        [TestCase(10, 10080)]
        [TestCase(0, 0)]
        public void should_schedule_cutoff_unmet_search_weekly(int configured, int expected)
        {
            GivenBookSearchInterval(configured);

            Subject.Handle(new ApplicationStartedEvent());

            var tasks = Subject.GetAll();

            var cutoff = tasks.Single(t => t.TypeName == typeof(CutoffUnmetBookSearchCommand).FullName);

            cutoff.Interval.Should().Be(expected);
        }

        [Test]
        public void should_add_the_cutoff_unmet_search_to_an_existing_database()
        {
            _existingTasks.Add(new ScheduledTask
            {
                Id = 1,
                Interval = 1,
                TypeName = typeof(MissingBookSearchCommand).FullName
            });

            Subject.Handle(new ApplicationStartedEvent());

            var cutoff = _upserted.Single(t => t.TypeName == typeof(CutoffUnmetBookSearchCommand).FullName);

            cutoff.Id.Should().Be(0);
            cutoff.Interval.Should().Be(10080);

            // The existing missing-search row keeps its identity but is refreshed to the current interval.
            var missing = _upserted.Single(t => t.TypeName == typeof(MissingBookSearchCommand).FullName);

            missing.Id.Should().Be(1);
            missing.Interval.Should().Be(360);
        }

        // Settings tidy (2026-09-23); tasks follow-up (2026-09-23): "Convert existing library",
        // "Sync light-novel titles" and "Import existing light novels" moved to System -> Tasks as
        // manual-only entries -- registered (so a Run button exists) but never scheduled (Interval 0,
        // GetPending ignores it; the framework's disabled/manual-only form).
        [TestCase(typeof(ConvertLightNovelFormatCommand))]
        [TestCase(typeof(SyncLightNovelTitlesCommand))]
        [TestCase(typeof(ImportExistingLightNovelsCommand))]

        // UI pass (2026-09-24, decision 1): "Embed Metadata" left the Library toolbar for System ->
        // Tasks, manual only like the three above.
        [TestCase(typeof(WriteComicInfoCommand))]
        public void light_novel_library_maintenance_tasks_are_registered_manual_only(Type commandType)
        {
            Subject.Handle(new ApplicationStartedEvent());

            var task = Subject.GetAll().Single(t => t.TypeName == commandType.FullName);

            task.Interval.Should().Be(0);
            Subject.GetPending().Should().NotContain(t => t.TypeName == commandType.FullName);
        }

        // Beta readiness (2026-09-28, F3): the library-wide PDF->CBZ sweep is daily only with PdfToCbzSweep on
        // (an existing library, migration 059); off -- a new install's default -- it is manual only.
        [TestCase(true, 1440)]
        [TestCase(false, 0)]
        public void pdf_sweep_is_daily_only_when_enabled(bool enabled, int expected)
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfToCbzSweep).Returns(enabled);

            Subject.Handle(new ApplicationStartedEvent());

            var task = Subject.GetAll().Single(t => t.TypeName == typeof(ConvertPdfToCbzCommand).FullName);

            task.Interval.Should().Be(expected);
            Subject.GetPending().Should().NotContain(t => t.TypeName == typeof(ConvertPdfToCbzCommand).FullName);
        }

        // Beta readiness fix round (2026-09-28, F3): the Media Management checkbox takes effect on save.
        [Test]
        public void turning_the_pdf_sweep_on_schedules_it_daily()
        {
            var enabled = false;
            Mocker.GetMock<IConfigService>().SetupGet(s => s.PdfToCbzSweep).Returns(() => enabled);
            Mocker.GetMock<IScheduledTaskRepository>()
                  .Setup(s => s.GetDefinition(It.IsAny<Type>()))
                  .Returns<Type>(t => new ScheduledTask { TypeName = t.FullName });

            Subject.Handle(new ApplicationStartedEvent());
            Subject.GetAll().Single(t => t.TypeName == typeof(ConvertPdfToCbzCommand).FullName).Interval.Should().Be(0);

            enabled = true;
            Subject.HandleAsync(new ConfigSavedEvent());

            Subject.GetAll().Single(t => t.TypeName == typeof(ConvertPdfToCbzCommand).FullName).Interval.Should().Be(1440);
        }

        // Beta readiness (2026-09-28, F1): without a catalogue the metadata check retries hourly; with one,
        // daily as before. Re-evaluated after each run, so the first successful fetch restores the day.
        [TestCase(false, 60)]
        [TestCase(true, 1440)]
        public void metadata_update_interval_follows_the_catalogue(bool available, int expected)
        {
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(available);

            Subject.Handle(new ApplicationStartedEvent());

            Subject.GetAll().Single(t => t.TypeName == typeof(MetadataUpdateCommand).FullName).Interval.Should().Be(expected);
        }

        [Test]
        public void metadata_update_goes_back_to_daily_once_the_catalogue_arrives()
        {
            var available = false;
            Mocker.GetMock<IGcdMetadataService>().SetupGet(s => s.Available).Returns(() => available);

            Subject.Handle(new ApplicationStartedEvent());

            var task = Subject.GetAll().Single(t => t.TypeName == typeof(MetadataUpdateCommand).FullName);
            task.Interval.Should().Be(60);

            Mocker.GetMock<IScheduledTaskRepository>()
                  .Setup(s => s.GetDefinition(typeof(MetadataUpdateCommand)))
                  .Returns(new ScheduledTask { Id = 7, TypeName = typeof(MetadataUpdateCommand).FullName, Interval = 60 });

            available = true;
            Subject.Handle(new CommandExecutedEvent(new CommandModel { Body = new MetadataUpdateCommand(), StartedAt = DateTime.UtcNow }));

            Subject.GetAll().Single(t => t.TypeName == typeof(MetadataUpdateCommand).FullName).Interval.Should().Be(1440);
            Mocker.GetMock<IScheduledTaskRepository>()
                  .Verify(s => s.UpdateMany(It.Is<List<ScheduledTask>>(l => l.Single().Interval == 1440)), Times.Once());
        }
    }
}
