using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MetadataSource.Gcd
{
    public class MetadataUpdateCommand : Command
    {
        public override string CompletionMessage => "GCD metadata update check complete";
    }
}
