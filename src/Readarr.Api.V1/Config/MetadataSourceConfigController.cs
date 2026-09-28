using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Http.REST.Attributes;
using Readarr.Http;

namespace Readarr.Api.V1.Config
{
    // GET  /api/v1/config/metadatasource   -> loaded artifact + updater settings
    // PUT  /api/v1/config/metadatasource/1 -> saves MetadataAutoUpdate / MetadataManifestUrl /
    //                                        GoogleBooksApiKey
    // A manual check is the MetadataUpdate task's Run Now on System -> Tasks.
    [V1ApiController("config/metadatasource")]
    public class MetadataSourceConfigController : ConfigController<MetadataSourceConfigResource>
    {
        private readonly IGcdMetadataService _gcdMetadataService;
        private readonly IScheduledTaskRepository _taskRepository;

        public MetadataSourceConfigController(
            IConfigService configService,
            IGcdMetadataService gcdMetadataService,
            IScheduledTaskRepository taskRepository)
            : base(configService)
        {
            _gcdMetadataService = gcdMetadataService;
            _taskRepository = taskRepository;
        }

        [RestPutById]
        public override ActionResult<MetadataSourceConfigResource> SaveConfig(MetadataSourceConfigResource resource)
        {
            // The UI only ever sees the mask; echoed back unchanged it is nulled so the save skips the
            // field and the stored key survives (the same rule as MetadataProviderConfigController).
            resource.GoogleBooksApiKey = GoogleBooksService.SanitizeIncomingApiKey(resource.GoogleBooksApiKey);

            return base.SaveConfig(resource);
        }

        protected override MetadataSourceConfigResource ToResource(IConfigService model)
        {
            return MetadataSourceConfigResourceMapper.ToResource(model, _gcdMetadataService, _taskRepository);
        }
    }
}
