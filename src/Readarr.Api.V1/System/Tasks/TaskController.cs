using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;
using Readarr.Http;
using Readarr.Http.REST;

namespace Readarr.Api.V1.System.Tasks
{
    [V1ApiController("system/task")]
    public class TaskController : RestControllerWithSignalR<TaskResource, ScheduledTask>, IHandle<CommandExecutedEvent>
    {
        private readonly ITaskManager _taskManager;
        private readonly ILocalizationService _localizationService;

        public TaskController(ITaskManager taskManager, IBroadcastSignalRMessage broadcastSignalRMessage, ILocalizationService localizationService)
            : base(broadcastSignalRMessage)
        {
            _taskManager = taskManager;
            _localizationService = localizationService;
        }

        [HttpGet]
        public List<TaskResource> GetAll()
        {
            return _taskManager.GetAll()
                               .Select(ConvertToResource)
                               .OrderBy(t => t.Name)
                               .ToList();
        }

        protected override TaskResource GetResourceById(int id)
        {
            var task = _taskManager.GetAll()
                               .SingleOrDefault(t => t.Id == id);

            if (task == null)
            {
                return null;
            }

            return ConvertToResource(task);
        }

        private TaskResource ConvertToResource(ScheduledTask scheduledTask)
        {
            var taskName = scheduledTask.TypeName.Split('.').Last().Replace("Command", "");

            return new TaskResource
            {
                Id = scheduledTask.Id,
                Name = CommandDisplayName.For(taskName, _localizationService),
                TaskName = taskName,
                Interval = scheduledTask.Interval,
                LastExecution = scheduledTask.LastExecution,
                LastStartTime = scheduledTask.LastStartTime,
                NextExecution = scheduledTask.LastExecution.AddMinutes(scheduledTask.Interval)
            };
        }

        [NonAction]
        public void Handle(CommandExecutedEvent message)
        {
            BroadcastResourceChange(ModelAction.Sync);
        }
    }
}
