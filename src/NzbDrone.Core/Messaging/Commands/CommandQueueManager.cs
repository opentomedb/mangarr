using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Composition;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Messaging.Commands
{
    public interface IManageCommandQueue
    {
        List<CommandModel> PushMany<TCommand>(List<TCommand> commands)
            where TCommand : Command;
        CommandModel Push<TCommand>(TCommand command, CommandPriority priority = CommandPriority.Normal, CommandTrigger trigger = CommandTrigger.Unspecified)
            where TCommand : Command;
        CommandModel Push(string commandName, DateTime? lastExecutionTime, DateTime? lastStartTime, CommandPriority priority = CommandPriority.Normal, CommandTrigger trigger = CommandTrigger.Unspecified);
        IEnumerable<CommandModel> Queue(CancellationToken cancellationToken);
        IEnumerable<CommandModel> Queue(CancellationToken cancellationToken, bool highPriorityOnly);
        List<CommandModel> All();
        CommandModel Get(int id);
        List<CommandModel> GetStarted();
        void SetMessage(CommandModel command, string message);
        void SetResult(CommandModel command, CommandResult result);
        void Start(CommandModel command);
        void Complete(CommandModel command, string message);
        void Fail(CommandModel command, string message, Exception e);
        void Requeue();
        void Cancel(int id);
        void CleanCommands();
    }

    public class CommandQueueManager : IManageCommandQueue, IHandle<ApplicationStartedEvent>
    {
        private readonly ICommandRepository _repo;
        private readonly KnownTypes _knownTypes;
        private readonly Logger _logger;

        private readonly CommandQueue _commandQueue;

        // Commands whose SQLite insert is still in flight. The insert runs OUTSIDE the
        // queue lock (it can stall tens of seconds when the DB is busy, and holding the
        // lock through it froze every other Push — notably the UI's POST /api/v1/command);
        // this list keeps the dedupe airtight in the window before the row lands.
        private readonly List<CommandModel> _pendingInserts = new List<CommandModel>();

        public CommandQueueManager(ICommandRepository repo,
                                   IServiceFactory serviceFactory,
                                   KnownTypes knownTypes,
                                   Logger logger)
        {
            _repo = repo;
            _knownTypes = knownTypes;
            _logger = logger;

            _commandQueue = new CommandQueue();
        }

        public List<CommandModel> PushMany<TCommand>(List<TCommand> commands)
            where TCommand : Command
        {
            _logger.Trace("Publishing {0} commands", commands.Count);

            lock (_commandQueue)
            {
                var commandModels = new List<CommandModel>();
                var existingCommands = _commandQueue.QueuedOrStarted();

                foreach (var command in commands)
                {
                    var existing = existingCommands.FirstOrDefault(c => c.Name == command.Name && CommandEqualityComparer.Instance.Equals(c.Body, command))
                                   ?? _pendingInserts.FirstOrDefault(c => c.Name == command.Name && CommandEqualityComparer.Instance.Equals(c.Body, command));

                    if (existing != null)
                    {
                        continue;
                    }

                    var commandModel = new CommandModel
                    {
                        Name = command.Name,
                        Body = command,
                        QueuedAt = DateTime.UtcNow,
                        Trigger = CommandTrigger.Unspecified,
                        Priority = CommandPriority.Normal,
                        Status = CommandStatus.Queued
                    };

                    commandModels.Add(commandModel);
                }

                _repo.InsertMany(commandModels);

                foreach (var commandModel in commandModels)
                {
                    _commandQueue.Add(commandModel);
                }

                return commandModels;
            }
        }

        public CommandModel Push<TCommand>(TCommand command, CommandPriority priority = CommandPriority.Normal, CommandTrigger trigger = CommandTrigger.Unspecified)
            where TCommand : Command
        {
            Ensure.That(command, () => command).IsNotNull();

            _logger.Trace("Publishing {0}", command.Name);
            _logger.Trace("Checking if command is queued or started: {0}", command.Name);

            while (true)
            {
                CommandModel commandModel;

                lock (_commandQueue)
                {
                    var existingCommands = QueuedOrStarted(command.Name);
                    var existing = existingCommands.FirstOrDefault(c => CommandEqualityComparer.Instance.Equals(c.Body, command));

                    if (existing != null)
                    {
                        _logger.Trace("Command is already in progress: {0}", command.Name);

                        return existing;
                    }

                    var pending = _pendingInserts.FirstOrDefault(c => c.Name == command.Name && CommandEqualityComparer.Instance.Equals(c.Body, command));

                    if (pending != null)
                    {
                        // A duplicate push while the original's insert is still in flight:
                        // wait for that insert to land (or fail) so callers never observe a
                        // command with Id 0, then re-evaluate — same result the old wide
                        // lock produced, but only duplicates ever wait here.
                        Monitor.Wait(_commandQueue, 1000);
                        continue;
                    }

                    commandModel = new CommandModel
                    {
                        Name = command.Name,
                        Body = command,
                        QueuedAt = DateTime.UtcNow,
                        Trigger = trigger,
                        Priority = priority,
                        Status = CommandStatus.Queued
                    };

                    _pendingInserts.Add(commandModel);
                }

                _logger.Trace("Inserting new command: {0}", commandModel.Name);

                try
                {
                    _repo.Insert(commandModel);

                    // Atomic pending -> queued swap so a concurrent duplicate can never
                    // observe the command in neither structure.
                    lock (_commandQueue)
                    {
                        _commandQueue.Add(commandModel);
                        _pendingInserts.Remove(commandModel);
                        Monitor.PulseAll(_commandQueue);
                    }
                }
                catch
                {
                    lock (_commandQueue)
                    {
                        _pendingInserts.Remove(commandModel);
                        Monitor.PulseAll(_commandQueue);
                    }

                    throw;
                }

                return commandModel;
            }
        }

        public CommandModel Push(string commandName, DateTime? lastExecutionTime, DateTime? lastStartTime, CommandPriority priority = CommandPriority.Normal, CommandTrigger trigger = CommandTrigger.Unspecified)
        {
            var command = GetCommand(commandName);
            command.LastExecutionTime = lastExecutionTime;
            command.LastStartTime = lastStartTime;
            command.Trigger = trigger;

            return Push(command, priority, trigger);
        }

        public IEnumerable<CommandModel> Queue(CancellationToken cancellationToken)
        {
            return _commandQueue.GetConsumingEnumerable(cancellationToken);
        }

        public IEnumerable<CommandModel> Queue(CancellationToken cancellationToken, bool highPriorityOnly)
        {
            return _commandQueue.GetConsumingEnumerable(cancellationToken, highPriorityOnly);
        }

        public List<CommandModel> All()
        {
            _logger.Trace("Getting all commands");
            return _commandQueue.All();
        }

        public CommandModel Get(int id)
        {
            var command = _commandQueue.Find(id);

            if (command == null)
            {
                command = _repo.Get(id);
            }

            return command;
        }

        public List<CommandModel> GetStarted()
        {
            _logger.Trace("Getting started commands");
            return _commandQueue.All().Where(c => c.Status == CommandStatus.Started).ToList();
        }

        public void SetMessage(CommandModel command, string message)
        {
            command.Message = message;

            // Server messages (2026-09-26): a new message never carries the previous one's template;
            // ProgressMessageTarget and CommandExecutor set the new one's after this call.
            command.MessageText = null;
        }

        public void SetResult(CommandModel command, CommandResult result)
        {
            command.Result = result;
        }

        public void Start(CommandModel command)
        {
            // Marks the command as started in the DB, the queue takes care of marking it as started on it's own
            _logger.Trace("Marking command as started: {0}", command.Name);
            _repo.Start(command);
        }

        public void Complete(CommandModel command, string message)
        {
            // If the result hasn't been set yet then set it to successful
            if (command.Result == CommandResult.Unknown)
            {
                command.Result = CommandResult.Successful;
            }

            Update(command, CommandStatus.Completed, message);

            _commandQueue.PulseAllConsumers();
        }

        public void Fail(CommandModel command, string message, Exception e)
        {
            command.Exception = e.ToString();

            Update(command, CommandStatus.Failed, message);

            _commandQueue.PulseAllConsumers();
        }

        public void Requeue()
        {
            foreach (var command in _repo.Queued())
            {
                _commandQueue.Add(command);
            }
        }

        public void Cancel(int id)
        {
            if (!_commandQueue.RemoveIfQueued(id))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Unable to cancel task");
            }
        }

        public void CleanCommands()
        {
            _logger.Trace("Cleaning up old commands");

            var commands = _commandQueue.All()
                .Where(c => c.EndedAt < DateTime.UtcNow.AddMinutes(-5))
                .ToList();

            _commandQueue.RemoveMany(commands);

            _repo.Trim();
        }

        private dynamic GetCommand(string commandName)
        {
            commandName = commandName.Split('.').Last();
            var commands = _knownTypes.GetImplementations(typeof(Command));
            var commandType = commands.Single(c => c.Name.Equals(commandName, StringComparison.InvariantCultureIgnoreCase));

            return Json.Deserialize("{}", commandType);
        }

        private void Update(CommandModel command, CommandStatus status, string message)
        {
            SetMessage(command, message);

            command.EndedAt = DateTime.UtcNow;
            command.Duration = command.EndedAt.Value.Subtract(command.StartedAt.Value);
            command.Status = status;

            _logger.Trace("Updating command status");

            try
            {
                _repo.End(command);
            }
            catch (Exception ex)
            {
                // In-memory state is authoritative for the UI, and OrphanStarted reconciles
                // the DB on restart; a failed bookkeeping write must not take down an
                // executor thread.
                _logger.Error(ex, "Failed to persist final state for command {0}", command.Name);
            }
        }

        private List<CommandModel> QueuedOrStarted(string name)
        {
            return _commandQueue.QueuedOrStarted()
                .Where(q => q.Name == name)
                .ToList();
        }

        public void Handle(ApplicationStartedEvent message)
        {
            _logger.Trace("Orphaning incomplete commands");
            _repo.OrphanStarted();
            Requeue();
        }
    }
}
