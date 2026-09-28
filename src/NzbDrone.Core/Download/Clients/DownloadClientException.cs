using System;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Localization;

namespace NzbDrone.Core.Download.Clients
{
    public class DownloadClientException : NzbDroneException
    {
        public DownloadClientException(string message, params object[] args)
            : base(string.Format(message, args))
        {
        }

        public DownloadClientException(string message)
            : base(message)
        {
        }

        // Server messages (2026-09-27, follow-ups): DiskStationProxyBase's messages keep their template.
        public DownloadClientException(ServerText text)
            : base(text)
        {
        }

        public DownloadClientException(string message, Exception innerException, params object[] args)
            : base(string.Format(message, args), innerException)
        {
        }

        public DownloadClientException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
