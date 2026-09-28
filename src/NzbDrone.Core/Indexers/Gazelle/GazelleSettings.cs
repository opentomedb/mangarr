using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Gazelle
{
    public class GazelleSettingsValidator : AbstractValidator<GazelleSettings>
    {
        public GazelleSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.Username).NotEmpty();
            RuleFor(c => c.Password).NotEmpty();

            RuleFor(c => c.SeedCriteria).SetValidator(_ => new SeedCriteriaSettingsValidator());
        }
    }

    public class GazelleSettings : ITorrentIndexerSettings
    {
        private static readonly GazelleSettingsValidator Validator = new GazelleSettingsValidator();

        public GazelleSettings()
        {
            MinimumSeeders = IndexerDefaults.MINIMUM_SEEDERS;
        }

        public string AuthKey;
        public string PassKey;

        [FieldDefinition(0, Label = "GazelleBaseUrl", Advanced = true, HelpText = "GazelleBaseUrlHelpText")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "Username", HelpText = "Username", Privacy = PrivacyLevel.UserName)]
        public string Username { get; set; }

        [FieldDefinition(2, Label = "Password", Type = FieldType.Password, HelpText = "Password", Privacy = PrivacyLevel.Password)]
        public string Password { get; set; }

        [FieldDefinition(3, Type = FieldType.Checkbox, Label = "UseFreeleechToken", HelpText = "GazelleUseFreeleechTokenHelpText", Advanced = true)]
        public bool UseFreeleechToken { get; set; }

        [FieldDefinition(4, Type = FieldType.Number, Label = "EarlyDownloadLimit", Unit = "days", HelpText = "FileListEarlyReleaseLimitHelpText", Advanced = true)]
        public int? EarlyReleaseLimit { get; set; }

        [FieldDefinition(5, Type = FieldType.Textbox, Label = "MinimumSeeders", HelpText = "FileListMinimumSeedersHelpText", Advanced = true)]
        public int MinimumSeeders { get; set; }

        [FieldDefinition(6)]
        public SeedCriteriaSettings SeedCriteria { get; set; } = new SeedCriteriaSettings();

        [FieldDefinition(7, Type = FieldType.Checkbox, Label = "RejectBlocklistedTorrentHashesWhileGrabbing", HelpText = "FileListRejectBlocklistedTorrentHashesWhileGrabbingHelpText", Advanced = true)]
        public bool RejectBlocklistedTorrentHashesWhileGrabbing { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
