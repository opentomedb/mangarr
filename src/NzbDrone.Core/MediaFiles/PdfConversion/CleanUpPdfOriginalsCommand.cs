using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.PdfConversion
{
    public class CleanUpPdfOriginalsCommand : Command
    {
        public override bool SendUpdatesToClient => false;
    }
}
