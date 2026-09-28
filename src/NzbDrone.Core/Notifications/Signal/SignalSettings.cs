using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Signal
{
    public class SignalSettingsValidator : AbstractValidator<SignalSettings>
    {
        public SignalSettingsValidator()
        {
            RuleFor(c => c.Host).NotEmpty();
            RuleFor(c => c.Port).NotEmpty();
            RuleFor(c => c.SenderNumber).NotEmpty();
            RuleFor(c => c.ReceiverId).NotEmpty();
        }
    }

    public class SignalSettings : IProviderConfig
    {
        private static readonly SignalSettingsValidator Validator = new ();

        [FieldDefinition(0, Label = "Host", Type = FieldType.Textbox, HelpText = "SignalHostHelpText")]
        public string Host { get; set; }

        [FieldDefinition(1, Label = "Port", Type = FieldType.Textbox, HelpText = "SignalPortHelpText")]
        public int Port { get; set; }

        [FieldDefinition(2, Label = "UseSSL", Type = FieldType.Checkbox, HelpText = "SignalUseSslHelpText")]
        public bool UseSsl { get; set; }

        [FieldDefinition(3, Label = "SenderNumber", Privacy = PrivacyLevel.ApiKey, HelpText = "SignalSenderNumberHelpText")]
        public string SenderNumber { get; set; }

        [FieldDefinition(4, Label = "GroupIDPhoneNumber", HelpText = "SignalReceiverIdHelpText")]
        public string ReceiverId { get; set; }

        [FieldDefinition(5, Label = "Login", Privacy = PrivacyLevel.UserName, HelpText = "SignalAuthUsernameHelpText")]
        public string AuthUsername { get; set; }

        [FieldDefinition(6, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "SignalAuthPasswordHelpText")]
        public string AuthPassword { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
