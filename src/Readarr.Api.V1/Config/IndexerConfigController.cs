using FluentValidation;
using NzbDrone.Core.Configuration;
using Readarr.Http;
using Readarr.Http.Validation;

namespace Readarr.Api.V1.Config
{
    [V1ApiController("config/indexer")]
    public class IndexerConfigController : ConfigController<IndexerConfigResource>
    {
        public IndexerConfigController(IConfigService configService)
            : base(configService)
        {
            SharedValidator.RuleFor(c => c.MinimumAge)
                           .GreaterThanOrEqualTo(0);

            SharedValidator.RuleFor(c => c.MaximumSize)
                           .GreaterThanOrEqualTo(0);

            SharedValidator.RuleFor(c => c.Retention)
                           .GreaterThanOrEqualTo(0);

            SharedValidator.RuleFor(c => c.RssSyncInterval)
                           .IsValidRssSyncInterval();

            // 0 disables the automatic missing-volume search; any positive value is floored to 30
            // minutes by the scheduler (TaskManager.GetBookSearchInterval).
            SharedValidator.RuleFor(c => c.BookSearchInterval)
                           .GreaterThanOrEqualTo(0);
        }

        protected override IndexerConfigResource ToResource(IConfigService model)
        {
            return IndexerConfigResourceMapper.ToResource(model);
        }
    }
}
