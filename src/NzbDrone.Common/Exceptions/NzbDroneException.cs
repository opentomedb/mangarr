using System;
using NzbDrone.Common.Localization;

namespace NzbDrone.Common.Exceptions
{
    public abstract class NzbDroneException : ApplicationException
    {
        // Server messages (2026-09-26): the template and arguments of a (message, args) exception, for an API
        // error in the UI language. Null for a plain message (matched whole). Not serialized: an exception is
        // never returned as API JSON itself (ReadarrErrorPipeline maps it to ErrorModel).
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ServerText Text { get; }

        // Server messages (2026-09-26, task 1b review): the params constructors have always
        // string.Formatted, even with zero args; WithEnglish keeps Text.English == Message.
        protected NzbDroneException(string message, params object[] args)
            : base(string.Format(message, args))
        {
            Text = ServerText.WithEnglish(string.Format(message, args), message, args);
        }

        protected NzbDroneException(string message)
            : base(message)
        {
        }

        // Server messages (2026-09-27, follow-ups): a message built elsewhere as a ServerText (DiskStation's
        // "Failed to {0}. Reason: {1}", whose arguments are themselves ServerTexts). Message is its English.
        protected NzbDroneException(ServerText text)
            : base(text.English)
        {
            Text = text;
        }

        protected NzbDroneException(string message, Exception innerException, params object[] args)
            : base(string.Format(message, args), innerException)
        {
            Text = ServerText.WithEnglish(string.Format(message, args), message, args);
        }

        protected NzbDroneException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
