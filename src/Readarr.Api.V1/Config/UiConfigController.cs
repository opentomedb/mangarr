using System.Linq;
using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Http.REST.Attributes;
using Readarr.Http;

namespace Readarr.Api.V1.Config
{
    [V1ApiController("config/ui")]
    public class UiConfigController : ConfigController<UiConfigResource>
    {
        private readonly IConfigFileProvider _configFileProvider;

        public UiConfigController(IConfigFileProvider configFileProvider, IConfigService configService)
            : base(configService)
        {
            _configFileProvider = configFileProvider;
            SharedValidator.RuleFor(c => c.UILanguage).Custom((value, context) =>
            {
                if (!Language.All.Any(o => o.Id == value))
                {
                    context.AddFailure("Invalid UI Language value");
                }
            });

            SharedValidator.RuleFor(c => c.UILanguage)
                           .GreaterThanOrEqualTo(1)
                           .WithMessage("The UI Language value cannot be less than 1");

            // Preferred Edition (2026-09-24, final fix round Minor 3): a tab loaded before the setting existed
            // PUTs the UI config without it (null). That keeps the stored chain (SaveConfigDictionary skips a
            // null value) instead of failing the whole save; any value that IS sent must be a valid chain.
            SharedValidator.RuleFor(c => c.PreferredEditionLanguages)
                           .Must(EditionLanguages.IsValidChain)
                           .When(c => c.PreferredEditionLanguages != null)
                           .WithMessage("Preferred Edition must be language codes separated by commas, such as fr,en");
        }

        [RestPutById]
        public override ActionResult<UiConfigResource> SaveConfig(UiConfigResource resource)
        {
            var dictionary = resource.GetType()
                                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                     .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

            _configFileProvider.SaveConfigDictionary(dictionary);
            _configService.SaveConfigDictionary(dictionary);

            return Accepted(resource.Id);
        }

        protected override UiConfigResource ToResource(IConfigService model)
        {
            return UiConfigResourceMapper.ToResource(_configFileProvider, model);
        }
    }
}
