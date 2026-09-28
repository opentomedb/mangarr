using NLog;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ProgressMessaging
{
    public class ProgressMessageTarget : Target, IHandle<ApplicationStartedEvent>
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private static LoggingRule _rule;

        public ProgressMessageTarget(IEventAggregator eventAggregator, IManageCommandQueue commandQueueManager)
        {
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
        }

        protected override void Write(LogEventInfo logEvent)
        {
            var command = ProgressMessageContext.CommandModel;

            if (!IsClientMessage(logEvent, command))
            {
                return;
            }

            if (!ProgressMessageContext.LockReentrancy())
            {
                return;
            }

            try
            {
                _commandQueueManager.SetMessage(command, logEvent.FormattedMessage);

                // Server messages (2026-09-26): the template NLog formatted, for the sidebar in the UI language.
                // A message without parameters is plain and matched whole.
                command.MessageText = logEvent.Parameters?.Length > 0
                    ? ServerText.WithEnglish(logEvent.FormattedMessage, logEvent.Message, logEvent.Parameters)
                    : null;

                _eventAggregator.PublishEvent(new CommandUpdatedEvent(command));
            }
            finally
            {
                ProgressMessageContext.UnlockReentrancy();
            }
        }

        private bool IsClientMessage(LogEventInfo logEvent, CommandModel command)
        {
            if (command == null || !command.Body.SendUpdatesToClient)
            {
                return false;
            }

            return logEvent.Properties.ContainsKey("Status");
        }

        public void Handle(ApplicationStartedEvent message)
        {
            _rule = new LoggingRule("*", LogLevel.Trace, this);

            LogManager.Configuration.AddTarget("ProgressMessagingLogger", this);
            LogManager.Configuration.LoggingRules.Add(_rule);
            LogManager.ReconfigExistingLoggers();
        }
    }
}
