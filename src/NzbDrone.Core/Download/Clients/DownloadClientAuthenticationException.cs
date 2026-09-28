using System;
using NzbDrone.Common.Localization;

namespace NzbDrone.Core.Download.Clients
{
    public class DownloadClientAuthenticationException : DownloadClientException
    {
        public DownloadClientAuthenticationException(string message, params object[] args)
            : base(message, args)
        {
        }

        public DownloadClientAuthenticationException(string message)
            : base(message)
        {
        }

        // Server messages (2026-09-27, follow-ups): DiskStationProxyBase's session error keeps its template.
        public DownloadClientAuthenticationException(ServerText text)
            : base(text)
        {
        }

        public DownloadClientAuthenticationException(string message, Exception innerException, params object[] args)
            : base(message, innerException, args)
        {
        }

        public DownloadClientAuthenticationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
