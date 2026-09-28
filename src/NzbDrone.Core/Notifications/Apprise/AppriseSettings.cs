using System;
using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Apprise
{
    public class AppriseSettingsValidator : AbstractValidator<AppriseSettings>
    {
        public AppriseSettingsValidator()
        {
            RuleFor(c => c.ServerUrl).IsValidUrl();

            RuleFor(c => c.ConfigurationKey).NotEmpty()
                .When(c => c.StatelessUrls.IsNullOrWhiteSpace())
                .WithMessage("Use either Configuration Key or Stateless URLs");

            RuleFor(c => c.ConfigurationKey).Matches("^[a-z0-9-]*$")
                .WithMessage("Allowed characters a-z, 0-9 and -");

            RuleFor(c => c.StatelessUrls).NotEmpty()
                .When(c => c.ConfigurationKey.IsNullOrWhiteSpace())
                .WithMessage("Use either Configuration Key or Stateless URLs");

            RuleFor(c => c.StatelessUrls).Empty()
                .When(c => c.ConfigurationKey.IsNotNullOrWhiteSpace())
                .WithMessage("Use either Configuration Key or Stateless URLs");

            RuleFor(c => c.Tags).Empty()
                .When(c => c.StatelessUrls.IsNotNullOrWhiteSpace())
                .WithMessage("Stateless URLs do not support tags");
        }
    }

    public class AppriseSettings : IProviderConfig
    {
        private static readonly AppriseSettingsValidator Validator = new ();

        public AppriseSettings()
        {
            NotificationType = (int)AppriseNotificationType.Info;
            Tags = Array.Empty<string>();
        }

        [FieldDefinition(1, Label = "AppriseServerURL", Type = FieldType.Url, Placeholder = "http://localhost:8000", HelpText = "AppriseServerUrlHelpText", HelpLink = "https://github.com/caronc/apprise-api")]
        public string ServerUrl { get; set; }

        [FieldDefinition(2, Label = "AppriseConfigurationKey", Type = FieldType.Textbox, HelpText = "AppriseConfigurationKeyHelpText", HelpLink = "https://github.com/caronc/apprise-api#persistent-storage-solution")]
        public string ConfigurationKey { get; set; }

        [FieldDefinition(3, Label = "AppriseStatelessURLs", Type = FieldType.Textbox, HelpText = "AppriseStatelessUrlsHelpText", HelpLink = "https://github.com/caronc/apprise#productivity-based-notifications")]
        public string StatelessUrls { get; set; }

        [FieldDefinition(4, Label = "AppriseNotificationType", Type = FieldType.Select, SelectOptions = typeof(AppriseNotificationType))]
        public int NotificationType { get; set; }

        [FieldDefinition(5, Label = "AppriseTags", Type = FieldType.Tag, HelpText = "AppriseTagsHelpText")]
        public IEnumerable<string> Tags { get; set; }

        [FieldDefinition(6, Label = "Username", Type = FieldType.Textbox, HelpText = "AppriseAuthUsernameHelpText", Privacy = PrivacyLevel.UserName)]
        public string AuthUsername { get; set; }

        [FieldDefinition(7, Label = "Password", Type = FieldType.Password, HelpText = "AppriseAuthPasswordHelpText", Privacy = PrivacyLevel.Password)]
        public string AuthPassword { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
