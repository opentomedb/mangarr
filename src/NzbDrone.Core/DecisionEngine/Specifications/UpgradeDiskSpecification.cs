using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class UpgradeDiskSpecification : IDecisionEngineSpecification
    {
        private readonly UpgradableSpecification _upgradableSpecification;
        private readonly ICustomFormatCalculationService _formatService;
        private readonly Logger _logger;

        public UpgradeDiskSpecification(UpgradableSpecification qualityUpgradableSpecification,
                                        ICacheManager cacheManager,
                                        ICustomFormatCalculationService formatService,
                                        Logger logger)
        {
            _upgradableSpecification = qualityUpgradableSpecification;
            _formatService = formatService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            foreach (var file in subject.Books.SelectMany(c => c.EditionOf(subject.MediaType)?.BookFiles.Value ?? new List<BookFile>()))
            {
                if (file == null)
                {
                    return Decision.Accept();
                }

                if (UpgradableSpecification.ReplacesStandIn(file.Quality, subject.ParsedBookInfo.Quality))
                {
                    continue;
                }

                // The pack re-grab (2026-11-22): a release graded by wording alone (no format token:
                // QualityDetectionSource.Guess) is never an upgrade over a file on disk. A "Series
                // v01-v12" pack was guessed CBZ, taken as an upgrade over twelve CBR volumes, and held the
                // same CBR files byte for byte. It still fills a missing volume and still replaces a stand-in.
                if (subject.ParsedBookInfo.Quality.QualityDetectionSource == QualityDetectionSource.Guess)
                {
                    return Decision.Reject("Release states no format; a guessed {0} is not an upgrade over the {1} on disk", subject.ParsedBookInfo.Quality.Quality.Name, file.Quality.Quality.Name);
                }

                var customFormats = _formatService.ParseCustomFormat(file);

                if (!_upgradableSpecification.IsUpgradable(subject.Author.QualityProfileFor(subject.MediaType),
                                                           file.Quality,
                                                           customFormats,
                                                           subject.ParsedBookInfo.Quality,
                                                           subject.CustomFormats))
                {
                    return Decision.Reject("Existing files on disk is of equal or higher preference: {0}", file.Quality.Quality.Name);
                }
            }

            return Decision.Accept();
        }
    }
}
