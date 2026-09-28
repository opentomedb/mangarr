namespace Readarr.Api.V1.Author
{
    // PUT /api/v1/author/{id}/match body. Null unbinds: the next refresh re-runs the ranked search.
    public class MatchResource
    {
        public int? AniListId { get; set; }
    }
}
