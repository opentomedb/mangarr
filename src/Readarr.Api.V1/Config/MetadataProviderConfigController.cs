using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Http.REST.Attributes;
using Readarr.Http;

namespace Readarr.Api.V1.Config
{
    [V1ApiController("config/metadataprovider")]
    public class MetadataProviderConfigController : ConfigController<MetadataProviderConfigResource>
    {
        public MetadataProviderConfigController(IConfigService configService)
            : base(configService)
        {
        }

        [RestPutById]
        public override ActionResult<MetadataProviderConfigResource> SaveConfig(MetadataProviderConfigResource resource)
        {
            // The UI only ever sees the mask; when it echoes back unchanged, null it out so the
            // config save skips the field and the stored key survives. A new key or an explicit
            // clear ("") passes through.
            resource.GoogleBooksApiKey = GoogleBooksService.SanitizeIncomingApiKey(resource.GoogleBooksApiKey);

            return base.SaveConfig(resource);
        }

        protected override MetadataProviderConfigResource ToResource(IConfigService model)
        {
            return MetadataProviderConfigResourceMapper.ToResource(model);
        }
    }
}
