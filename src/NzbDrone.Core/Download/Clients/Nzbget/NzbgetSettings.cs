using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.Nzbget
{
    public class NzbgetSettingsValidator : AbstractValidator<NzbgetSettings>
    {
        public NzbgetSettingsValidator()
        {
            RuleFor(c => c.Host).ValidHost();
            RuleFor(c => c.Port).InclusiveBetween(1, 65535);
            RuleFor(c => c.UrlBase).ValidUrlBase().When(c => c.UrlBase.IsNotNullOrWhiteSpace());

            RuleFor(c => c.Username).NotEmpty().When(c => !string.IsNullOrWhiteSpace(c.Password));
            RuleFor(c => c.Password).NotEmpty().When(c => !string.IsNullOrWhiteSpace(c.Username));

            RuleFor(c => c.MusicCategory).NotEmpty().WithMessage("A category is recommended").AsWarning();
        }
    }

    public class NzbgetSettings : IProviderConfig
    {
        private static readonly NzbgetSettingsValidator Validator = new NzbgetSettingsValidator();

        public NzbgetSettings()
        {
            Host = "localhost";
            Port = 6789;
            MusicCategory = "mangarr";
            Username = "nzbget";
            Password = "tegbzn6789";
            RecentTvPriority = (int)NzbgetPriority.Normal;
            OlderTvPriority = (int)NzbgetPriority.Normal;
        }

        [FieldDefinition(0, Label = "Host", Type = FieldType.Textbox)]
        public string Host { get; set; }

        [FieldDefinition(1, Label = "Port", Type = FieldType.Textbox)]
        public int Port { get; set; }

        [FieldDefinition(2, Label = "UseSSL", Type = FieldType.Checkbox, HelpText = "NzbgetUseSslHelpText")]
        public bool UseSsl { get; set; }

        [FieldDefinition(3, Label = "DelugeUrlBase", Type = FieldType.Textbox, Advanced = true, HelpText = "NzbgetUrlBaseHelpText")]
        public string UrlBase { get; set; }

        [FieldDefinition(3, Label = "Username", Type = FieldType.Textbox, Privacy = PrivacyLevel.UserName)]
        public string Username { get; set; }

        [FieldDefinition(5, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password)]
        public string Password { get; set; }

        [FieldDefinition(6, Label = "Category", Type = FieldType.Textbox, HelpText = "DelugeMusicCategoryHelpText")]
        public string MusicCategory { get; set; }

        [FieldDefinition(7, Label = "RecentPriority", Type = FieldType.Select, SelectOptions = typeof(NzbgetPriority), HelpText = "NzbVortexRecentTvPriorityHelpText")]
        public int RecentTvPriority { get; set; }

        [FieldDefinition(8, Label = "OlderPriority", Type = FieldType.Select, SelectOptions = typeof(NzbgetPriority), HelpText = "NzbVortexOlderTvPriorityHelpText")]
        public int OlderTvPriority { get; set; }

        [FieldDefinition(9, Label = "AddPaused", Type = FieldType.Checkbox, HelpText = "NzbgetAddPausedHelpText")]
        public bool AddPaused { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
