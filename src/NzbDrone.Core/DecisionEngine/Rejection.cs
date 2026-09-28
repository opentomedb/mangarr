using NzbDrone.Common.Localization;

namespace NzbDrone.Core.DecisionEngine
{
    public class Rejection
    {
        public string Reason { get; set; }
        public RejectionType Type { get; set; }

        // Server messages (2026-09-26): Reason's template, for the UI language at the API boundary. Never
        // serialized: Manual Import returns and receives Rejection JSON unchanged.
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ServerText Text { get; set; }

        // The constructor both serializers use (ManualImportUpdateResource posts rejections back).
        [System.Text.Json.Serialization.JsonConstructor]
        [Newtonsoft.Json.JsonConstructor]
        public Rejection(string reason, RejectionType type = RejectionType.Permanent)
        {
            Reason = reason;
            Type = type;
        }

        public Rejection(ServerText reason, RejectionType type = RejectionType.Permanent)
            : this(reason.English, type)
        {
            Text = reason;
        }

        // A spec's decision, wrapped: its template travels along as it is, never re-parsed (spec section 1).
        public Rejection(Decision decision, RejectionType type = RejectionType.Permanent)
            : this(decision.Reason, type)
        {
            Text = decision.Text;
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1}", Type, Reason);
        }
    }
}
