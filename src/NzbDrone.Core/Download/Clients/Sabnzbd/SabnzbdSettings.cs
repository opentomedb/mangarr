using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.Sabnzbd
{
    public class SabnzbdSettingsValidator : AbstractValidator<SabnzbdSettings>
    {
        public SabnzbdSettingsValidator()
        {
            RuleFor(c => c.Host).ValidHost();
            RuleFor(c => c.Port).InclusiveBetween(1, 65535);
            RuleFor(c => c.UrlBase).ValidUrlBase().When(c => c.UrlBase.IsNotNullOrWhiteSpace());

            RuleFor(c => c.ApiKey).NotEmpty()
                                  .WithMessage("API Key is required when username/password are not configured")
                                  .When(c => string.IsNullOrWhiteSpace(c.Username));

            RuleFor(c => c.Username).NotEmpty()
                                    .WithMessage("Username is required when API key is not configured")
                                    .When(c => string.IsNullOrWhiteSpace(c.ApiKey));

            RuleFor(c => c.Password).NotEmpty()
                                    .WithMessage("Password is required when API key is not configured")
                                    .When(c => string.IsNullOrWhiteSpace(c.ApiKey));

            RuleFor(c => c.MusicCategory).NotEmpty()
                                      .WithMessage("A category is recommended")
                                      .AsWarning();
        }
    }

    public class SabnzbdSettings : IProviderConfig
    {
        private static readonly SabnzbdSettingsValidator Validator = new SabnzbdSettingsValidator();

        public SabnzbdSettings()
        {
            Host = "localhost";
            Port = 8080;
            MusicCategory = "mangarr";
            RecentTvPriority = (int)SabnzbdPriority.Default;
            OlderTvPriority = (int)SabnzbdPriority.Default;
        }

        [FieldDefinition(0, Label = "Host", Type = FieldType.Textbox)]
        public string Host { get; set; }

        [FieldDefinition(1, Label = "Port", Type = FieldType.Textbox)]
        public int Port { get; set; }

        [FieldDefinition(2, Label = "UseSSL", Type = FieldType.Checkbox, HelpText = "SabnzbdUseSslHelpText")]
        public bool UseSsl { get; set; }

        [FieldDefinition(3, Label = "DelugeUrlBase", Type = FieldType.Textbox, Advanced = true, HelpText = "SabnzbdUrlBaseHelpText")]
        public string UrlBase { get; set; }

        [FieldDefinition(4, Label = "ApiKey", Type = FieldType.Textbox, Privacy = PrivacyLevel.ApiKey)]
        public string ApiKey { get; set; }

        [FieldDefinition(5, Label = "Username", Type = FieldType.Textbox, Privacy = PrivacyLevel.UserName)]
        public string Username { get; set; }

        [FieldDefinition(6, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password)]
        public string Password { get; set; }

        [FieldDefinition(7, Label = "Category", Type = FieldType.Textbox, HelpText = "DelugeMusicCategoryHelpText")]
        public string MusicCategory { get; set; }

        [FieldDefinition(8, Label = "RecentPriority", Type = FieldType.Select, SelectOptions = typeof(SabnzbdPriority), HelpText = "NzbVortexRecentTvPriorityHelpText")]
        public int RecentTvPriority { get; set; }

        [FieldDefinition(9, Label = "OlderPriority", Type = FieldType.Select, SelectOptions = typeof(SabnzbdPriority), HelpText = "NzbVortexOlderTvPriorityHelpText")]
        public int OlderTvPriority { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
