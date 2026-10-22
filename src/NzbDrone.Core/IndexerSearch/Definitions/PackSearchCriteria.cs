namespace NzbDrone.Core.IndexerSearch.Definitions
{
    // Deliberately not a SearchCriteriaBase: it names no series, so nothing that reads Author sees it.
    public class PackSearchCriteria
    {
        public string Query { get; set; } = "Volumes Update";
    }
}
