using System;
using NzbDrone.Common.Localization;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Messaging.Commands
{
    public class CommandModel : ModelBase, IMessage
    {
        public string Name { get; set; }
        public Command Body { get; set; }
        public CommandPriority Priority { get; set; }
        public CommandStatus Status { get; set; }
        public CommandResult Result { get; set; }
        public DateTime QueuedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public TimeSpan? Duration { get; set; }
        public string Exception { get; set; }
        public CommandTrigger Trigger { get; set; }
        public string Message { get; set; }

        // Server messages (2026-09-26): Message's template (a progress line's or the completion text's), for the
        // sidebar in the UI language. Not persisted (TableMapping ignores it, as it ignores Message) and never
        // serialized: a command read back from the database has none and is matched whole.
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ServerText MessageText { get; set; }
    }
}
