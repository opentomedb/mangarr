using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Books
{
    public interface IBookCutoffService
    {
        PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, LibraryType? library = null, WantedMediaTypeFilter? mediaType = null);
    }

    public class BookCutoffService : IBookCutoffService
    {
        private readonly IBookRepository _bookRepository;
        private readonly IQualityProfileService _qualityProfileService;

        public BookCutoffService(IBookRepository bookRepository, IQualityProfileService qualityProfileService)
        {
            _bookRepository = bookRepository;
            _qualityProfileService = qualityProfileService;
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, LibraryType? library = null, WantedMediaTypeFilter? mediaType = null)
        {
            var qualitiesBelowCutoff = new List<QualitiesBelowCutoff>();
            var profiles = _qualityProfileService.All();

            //Get all items less than the cutoff
            foreach (var profile in profiles)
            {
                var cutoff = profile.UpgradeAllowed ? profile.Cutoff : profile.FirstAllowedQuality().Id;
                var cutoffIndex = profile.GetIndex(cutoff);
                var belowCutoff = profile.Items.Take(cutoffIndex.Index).ToList();
                var belowCutoffIds = belowCutoff.SelectMany(i => i.GetQualities().Select(q => q.Id)).ToList();

                // A chapter-built volume stays wanted until a real volume replaces it, even when the
                // profile does not allow upgrades (then CbzChapters is the cutoff itself) or the
                // quality has been unticked since the build.
                if (!belowCutoffIds.Contains(Quality.CbzChapters.Id))
                {
                    belowCutoffIds.Add(Quality.CbzChapters.Id);
                }

                if (belowCutoffIds.Any())
                {
                    qualitiesBelowCutoff.Add(new QualitiesBelowCutoff(profile.Id, belowCutoffIds));
                }
            }

            if (qualitiesBelowCutoff.Empty())
            {
                pagingSpec.Records = new List<Book>();

                return pagingSpec;
            }

            return _bookRepository.BooksWhereCutoffUnmet(pagingSpec, qualitiesBelowCutoff, library, mediaType);
        }
    }
}
