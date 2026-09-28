using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.MangaDex
{
    public class MangaDexSettingsValidator : AbstractValidator<MangaDexSettings>
    {
        public MangaDexSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).IsValidUrl();
            RuleFor(c => c.AuthUrl).IsValidUrl();
            RuleFor(c => c.Username).NotEmpty();
            RuleFor(c => c.Password).NotEmpty();
            RuleFor(c => c.ClientId).NotEmpty();
            RuleFor(c => c.ClientSecret).NotEmpty();
        }
    }

    public class MangaDexSettings : IImportListSettings
    {
        private static readonly MangaDexSettingsValidator Validator = new MangaDexSettingsValidator();

        public MangaDexSettings()
        {
            BaseUrl = "https://api.mangadex.org";
            AuthUrl = "https://auth.mangadex.org/realms/mangadex/protocol/openid-connect/token";
        }

        [FieldDefinition(0, Label = "Username", HelpText = "MangaDexUsernameHelpText")]
        public string Username { get; set; }

        [FieldDefinition(1, Label = "Password", Type = FieldType.Password)]
        public string Password { get; set; }

        [FieldDefinition(2, Label = "ClientID", HelpText = "MangaDexClientIdHelpText")]
        public string ClientId { get; set; }

        [FieldDefinition(3, Label = "ClientSecret", Type = FieldType.Password)]
        public string ClientSecret { get; set; }

        [FieldDefinition(4, Label = "APIURL", Advanced = true)]
        public string BaseUrl { get; set; }

        [FieldDefinition(5, Label = "AuthURL", Advanced = true)]
        public string AuthUrl { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
