using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.PushBullet
{
    public class PushBulletSettingsValidator : AbstractValidator<PushBulletSettings>
    {
        public PushBulletSettingsValidator()
        {
            RuleFor(c => c.ApiKey).NotEmpty();
        }
    }

    public class PushBulletSettings : IProviderConfig
    {
        private static readonly PushBulletSettingsValidator Validator = new PushBulletSettingsValidator();

        public PushBulletSettings()
        {
            DeviceIds = new string[] { };
            ChannelTags = new string[] { };
        }

        [FieldDefinition(0, Label = "AccessToken", Privacy = PrivacyLevel.ApiKey, HelpLink = "https://www.pushbullet.com/#settings/account")]
        public string ApiKey { get; set; }

        [FieldDefinition(1, Label = "DeviceIDs", HelpText = "PushBulletDeviceIdsHelpText", Type = FieldType.Device)]
        public IEnumerable<string> DeviceIds { get; set; }

        [FieldDefinition(2, Label = "ChannelTags", HelpText = "PushBulletChannelTagsHelpText", Type = FieldType.Tag)]
        public IEnumerable<string> ChannelTags { get; set; }

        [FieldDefinition(3, Label = "SenderID", HelpText = "PushBulletSenderIdHelpText")]
        public string SenderId { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
