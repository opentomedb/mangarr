using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers
{
    // Opt-in: only generators that can run the series-independent monthly-pack query implement this.
    public interface IPackSearchRequestGenerator
    {
        IndexerPageableRequestChain GetPackSearchRequests(PackSearchCriteria criteria);
    }
}
