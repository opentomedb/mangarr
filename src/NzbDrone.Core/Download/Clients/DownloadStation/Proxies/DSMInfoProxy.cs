using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Download.Clients.DownloadStation.Responses;

namespace NzbDrone.Core.Download.Clients.DownloadStation.Proxies
{
    public interface IDSMInfoProxy
    {
        string GetSerialNumber(DownloadStationSettings settings);
    }

    public class DSMInfoProxy : DiskStationProxyBase, IDSMInfoProxy
    {
        public DSMInfoProxy(IHttpClient httpClient, ICacheManager cacheManager, Logger logger)
            : base(DiskStationApi.DSMInfo, "SYNO.DSM.Info", httpClient, cacheManager, logger)
        {
        }

        public string GetSerialNumber(DownloadStationSettings settings)
        {
            var info = GetApiInfo(settings);

            var requestBuilder = BuildRequest(settings, "getinfo", info.MinVersion);

            var response = ProcessRequest<DSMInfoResponse>(requestBuilder, new ServerText("get serial number"), settings);

            return response.Data.SerialNumber;
        }
    }
}
