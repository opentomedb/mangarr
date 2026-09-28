using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Localization;
using NzbDrone.SignalR;
using Readarr.Api.V1.Commands;
using Readarr.Api.V1.System.Tasks;

namespace NzbDrone.Core.Test.Messaging.Commands
{
    // UI translations v1 (2026-09-25): System -> Tasks and the queued-task list show en.json's
    // TaskName<Name> in the UI language. Its English must stay what CommandDisplayName.For computes,
    // for every command, and the API's TaskName/Name stay the class names (Run Now matches on them).
    [TestFixture]
    public class CommandDisplayNameLocalizationFixture
    {
        private static List<string> CommandNames()
        {
            return typeof(Command).Assembly.GetTypes()
                .Where(t => typeof(Command).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(t => t.Name.Replace("Command", ""))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }

        private static ILocalizationService French()
        {
            var french = new Mock<ILocalizationService>();
            french.Setup(s => s.GetLocalizedString(It.IsAny<string>())).Returns<string>(phrase => phrase);
            french.Setup(s => s.GetLocalizedString("TaskNameRefreshAuthor")).Returns("Actualiser la série");

            return french.Object;
        }

        [Test]
        public void every_command_has_a_task_name_key_whose_english_is_its_display_name()
        {
            var english = EnglishLocalization.Create();

            CommandNames().Should().Contain("RefreshAuthor");

            CommandNames()
                .Where(n => english.GetLocalizedString("TaskName" + n) != CommandDisplayName.For(n))
                .Select(n => $"TaskName{n}: en.json '{english.GetLocalizedString("TaskName" + n)}', For '{CommandDisplayName.For(n)}'")
                .Should().BeEmpty();
        }

        [Test]
        public void english_leaves_every_display_name_as_it_was()
        {
            var english = EnglishLocalization.Create();

            CommandNames().Where(n => CommandDisplayName.For(n, english) != CommandDisplayName.For(n)).Should().BeEmpty();
        }

        [Test]
        public void a_translated_task_name_is_shown()
        {
            CommandDisplayName.For("RefreshAuthor", French()).Should().Be("Actualiser la série");
            CommandDisplayName.For("RefreshAuthorCommand", French()).Should().Be("Actualiser la série");
        }

        [Test]
        public void a_name_without_a_key_keeps_the_english()
        {
            // An UnknownCommand carries the stored name of a command that no longer exists.
            CommandDisplayName.For("RetiredThingSync", French()).Should().Be("Retired Thing Sync");
        }

        [Test]
        public void system_tasks_show_the_translated_name_and_keep_the_class_name()
        {
            var taskManager = new Mock<ITaskManager>();
            taskManager.Setup(s => s.GetAll()).Returns(new List<ScheduledTask>
            {
                new ScheduledTask { Id = 1, TypeName = typeof(RefreshAuthorCommand).FullName, Interval = 720 }
            });

            var controller = new TaskController(taskManager.Object, new Mock<IBroadcastSignalRMessage>().Object, French());
            var task = controller.GetAll().Single();

            task.Name.Should().Be("Actualiser la série");
            task.TaskName.Should().Be("RefreshAuthor");
        }

        [Test]
        public void queued_commands_show_the_translated_name_and_keep_the_class_name()
        {
            var model = new CommandModel { Id = 7, Name = "RefreshAuthor", Body = new RefreshAuthorCommand() };

            var resource = model.ToResource(French());

            resource.CommandName.Should().Be("Actualiser la série");
            resource.Name.Should().Be("RefreshAuthor");
        }
    }
}
