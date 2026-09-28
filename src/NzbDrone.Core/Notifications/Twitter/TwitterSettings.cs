using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Twitter
{
    public class TwitterSettingsValidator : AbstractValidator<TwitterSettings>
    {
        public TwitterSettingsValidator()
        {
            RuleFor(c => c.ConsumerKey).NotEmpty();
            RuleFor(c => c.ConsumerSecret).NotEmpty();
            RuleFor(c => c.AccessToken).NotEmpty();
            RuleFor(c => c.AccessTokenSecret).NotEmpty();

            //TODO: Validate that it is a valid username (numbers, letters and underscores - I think)
            RuleFor(c => c.Mention).NotEmpty().When(c => c.DirectMessage);

            RuleFor(c => c.DirectMessage).Equal(true)
                                         .WithMessage("Using Direct Messaging is recommended, or use a private account.")
                                         .AsWarning();

            RuleFor(c => c.AuthorizeNotification).Empty()
                                                 .When(c => c.AccessToken.IsNullOrWhiteSpace() || c.AccessTokenSecret.IsNullOrWhiteSpace())
                                                 .WithMessage("Authenticate app.");
        }
    }

    public class TwitterSettings : IProviderConfig
    {
        private static readonly TwitterSettingsValidator Validator = new TwitterSettingsValidator();

        public TwitterSettings()
        {
            DirectMessage = true;
            AuthorizeNotification = "startOAuth";
        }

        [FieldDefinition(0, Label = "ConsumerKey", Privacy = PrivacyLevel.ApiKey, HelpText = "TwitterConsumerKeyHelpText", HelpLink = "https://wiki.servarr.com/useful-tools#twitter-connect")]
        public string ConsumerKey { get; set; }

        [FieldDefinition(1, Label = "ConsumerSecret", Privacy = PrivacyLevel.ApiKey, HelpText = "TwitterConsumerSecretHelpText", HelpLink = "https://wiki.servarr.com/useful-tools#twitter-connect")]
        public string ConsumerSecret { get; set; }

        [FieldDefinition(2, Label = "AccessToken", Privacy = PrivacyLevel.ApiKey, Advanced = true)]
        public string AccessToken { get; set; }

        [FieldDefinition(3, Label = "AccessTokenSecret", Privacy = PrivacyLevel.ApiKey, Advanced = true)]
        public string AccessTokenSecret { get; set; }

        [FieldDefinition(4, Label = "Mention", HelpText = "TwitterMentionHelpText")]
        public string Mention { get; set; }

        [FieldDefinition(5, Label = "DirectMessage", Type = FieldType.Checkbox, HelpText = "TwitterDirectMessageHelpText")]
        public bool DirectMessage { get; set; }

        [FieldDefinition(6, Label = "ConnectToTwitter", Type = FieldType.OAuth)]
        public string AuthorizeNotification { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
