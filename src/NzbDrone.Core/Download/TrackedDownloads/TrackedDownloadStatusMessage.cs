using System.Collections.Generic;
using NzbDrone.Common.Localization;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class TrackedDownloadStatusMessage
    {
        public string Title { get; set; }
        public List<string> Messages { get; set; }

        // Server messages (2026-09-26): the template behind each entry of Messages (null where plain), for the
        // queue in the UI language. Never serialized: the queue API, History and DownloadHistory
        // (statusMessages) keep their JSON, and a message read back from DownloadHistory has none.
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public List<ServerText> MessageTexts { get; set; }

        public TrackedDownloadStatusMessage(string title, List<string> messages)
        {
            Title = title;
            Messages = messages;
        }

        public TrackedDownloadStatusMessage(string title, List<string> messages, List<ServerText> messageTexts)
        {
            Title = title;
            Messages = messages;
            MessageTexts = messageTexts;
        }

        public TrackedDownloadStatusMessage(string title, string message)
        {
            Title = title;
            Messages = new List<string> { message };
        }

        public TrackedDownloadStatusMessage(string title, ServerText message)
        {
            Title = title;
            Messages = new List<string> { message.English };
            MessageTexts = new List<ServerText> { message };
        }

        //Constructor for use when deserializing JSON
        public TrackedDownloadStatusMessage()
        {
        }
    }
}
