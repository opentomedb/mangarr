using NzbDrone.Common.Http;

namespace NzbDrone.Common.Cloud
{
    // Beta readiness (2026-09-28, E5): the Services factory (Readarr's retired service host: server-side
    // notifications, /time, /ping) is gone with its three callers. Metadata stays: BookInfoProxy's
    // SPIKE-guarded upstream paths still name it.
    public interface IReadarrCloudRequestBuilder
    {
        IHttpRequestBuilderFactory Metadata { get; }
    }

    public class ReadarrCloudRequestBuilder : IReadarrCloudRequestBuilder
    {
        public ReadarrCloudRequestBuilder()
        {
            Metadata = new HttpRequestBuilder("https://api.bookinfo.club/v1/{route}")
                .CreateFactory();
        }

        public IHttpRequestBuilderFactory Metadata { get; }
    }
}
