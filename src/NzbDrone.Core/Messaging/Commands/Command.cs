using System;
using System.Text.Json.Serialization;
using NzbDrone.Common.Localization;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Messaging.Commands
{
    [JsonConverter(typeof(PolymorphicWriteOnlyJsonConverter<Command>))]
    public abstract class Command
    {
        private bool _sendUpdatesToClient;

        public virtual bool SendUpdatesToClient
        {
            get
            {
                return _sendUpdatesToClient;
            }

            set
            {
                _sendUpdatesToClient = value;
            }
        }

        public virtual bool UpdateScheduledTask => true;
        public virtual string CompletionMessage => null;
        public virtual bool RequiresDiskAccess => false;
        public virtual bool IsExclusive => false;
        public virtual bool IsTypeExclusive => false;
        public virtual bool IsLongRunning => false;

        public string Name { get; private set; }
        public DateTime? LastExecutionTime { get; set; }
        public DateTime? LastStartTime { get; set; }
        public CommandTrigger Trigger { get; set; }
        public bool SuppressMessages { get; set; }

        public string ClientUserAgent { get; set; }

        // What the handler actually did, for the toast the client shows on completion. Set by the
        // executing service on the command instance it was handed; CommandExecutor prefers it over
        // the static CompletionMessage. It lives on the base class deliberately:
        // CommandEqualityComparer skips properties declared on Command, so a running command whose
        // handler has already filled this in still dedupes against an identical new push.
        public string ResultMessage { get; set; }

        // Server messages (2026-09-26, plan ruling R3): ResultMessage's template, for the completion text in
        // the UI language. Declared on Command, so CommandEqualityComparer skips it like ResultMessage; never
        // serialized (the command body is stored as JSON).
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ServerText ResultText { get; private set; }

        public void SetResultMessage(ServerText result)
        {
            ResultMessage = result?.English;
            ResultText = result;
        }

        public Command()
        {
            Name = GetType().Name.Replace("Command", "");
        }
    }
}
