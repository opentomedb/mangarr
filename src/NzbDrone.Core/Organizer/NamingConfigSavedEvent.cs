using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Organizer
{
    // Beta readiness (2026-09-28, review M8): naming config lives in its own table, so saving it published
    // nothing; AudiobookshelfCheck re-runs on this, so turning Rename Volumes on clears its line at once.
    public class NamingConfigSavedEvent : IEvent
    {
    }
}
