using NzbDrone.Common.Localization;

namespace NzbDrone.Core.DecisionEngine
{
    public class Decision
    {
        public bool Accepted { get; private set; }
        public string Reason { get; private set; }

        // Server messages (2026-09-26): the template and arguments Reason was built from, for the UI language
        // at the API boundary. Null for a plain reason (matched whole there). Not serialized: Decision itself
        // never reaches the API (ReleaseResourceMapper maps DownloadDecision/Rejection instead).
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ServerText Text { get; private set; }

        private static readonly Decision AcceptDecision = new Decision { Accepted = true };
        private Decision()
        {
        }

        public static Decision Accept()
        {
            return AcceptDecision;
        }

        public static Decision Reject(string reason, params object[] args)
        {
            return Reject(new ServerText(reason, args));
        }

        public static Decision Reject(ServerText reason)
        {
            return new Decision
            {
                Accepted = false,
                Reason = reason.English,
                Text = reason
            };
        }

        public static Decision Reject(string reason)
        {
            return new Decision
            {
                Accepted = false,
                Reason = reason
            };
        }
    }
}
