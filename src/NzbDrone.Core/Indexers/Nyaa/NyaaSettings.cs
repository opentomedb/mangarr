using System.Text.RegularExpressions;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;
namespace NzbDrone.Core.Indexers.Nyaa
{
    public class NyaaSettingsValidator : AbstractValidator<NyaaSettings>
    {
        public NyaaSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.AdditionalParameters).Matches("(&[a-z]+=[a-z0-9_]+)*", RegexOptions.IgnoreCase);

            RuleFor(c => c.SeedCriteria).SetValidator(_ => new SeedCriteriaSettingsValidator());
        }
    }

    public class NyaaSettings : ITorrentIndexerSettings
    {
        private static readonly NyaaSettingsValidator Validator = new NyaaSettingsValidator();

        public NyaaSettings()
        {
            BaseUrl = "";
            AdditionalParameters = "&cats=3_0&filter=1";
            MinimumSeeders = IndexerDefaults.MINIMUM_SEEDERS;
        }

        [FieldDefinition(0, Label = "WebsiteURL")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "AdditionalParameters", Advanced = true, HelpText = "NyaaAdditionalParametersHelpText")]
        public string AdditionalParameters { get; set; }

        [FieldDefinition(2, Type = FieldType.Number, Label = "EarlyDownloadLimit", Unit = "days", HelpText = "FileListEarlyReleaseLimitHelpText", Advanced = true)]
        public int? EarlyReleaseLimit { get; set; }

        [FieldDefinition(3, Type = FieldType.Textbox, Label = "MinimumSeeders", HelpText = "FileListMinimumSeedersHelpText", Advanced = true)]
        public int MinimumSeeders { get; set; }

        [FieldDefinition(4)]
        public SeedCriteriaSettings SeedCriteria { get; set; } = new SeedCriteriaSettings();

        [FieldDefinition(5, Type = FieldType.Checkbox, Label = "RejectBlocklistedTorrentHashesWhileGrabbing", HelpText = "FileListRejectBlocklistedTorrentHashesWhileGrabbingHelpText", Advanced = true)]
        public bool RejectBlocklistedTorrentHashesWhileGrabbing { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
