using NzbDrone.Core.MetadataSource;

namespace Readarr.Api.V1.Author
{
    // One AniList hit for the Fix Match modal (D5): what the user needs to tell entries apart.
    public class MatchCandidateResource
    {
        public int AniListId { get; set; }
        public string Title { get; set; }
        public string Format { get; set; }
        public int? Year { get; set; }
        public int? Volumes { get; set; }
        public int? Popularity { get; set; }
        public string CoverUrl { get; set; }
        public string Description { get; set; }

        public static MatchCandidateResource From(AniListCandidate candidate)
        {
            return new MatchCandidateResource
            {
                AniListId = candidate.Id,
                Title = candidate.DisplayTitle,
                Format = candidate.Format,
                Year = candidate.StartYear,
                Volumes = candidate.Volumes,
                Popularity = candidate.Popularity,
                CoverUrl = candidate.CoverUrl,
                Description = candidate.Description
            };
        }
    }
}
