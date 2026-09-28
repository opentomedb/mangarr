using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.FileList
{
    public class FileListSettingsValidator : AbstractValidator<FileListSettings>
    {
        public FileListSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.Username).NotEmpty();
            RuleFor(c => c.Passkey).NotEmpty();

            RuleFor(c => c.SeedCriteria).SetValidator(_ => new SeedCriteriaSettingsValidator());
        }
    }

    public class FileListSettings : ITorrentIndexerSettings
    {
        private static readonly FileListSettingsValidator Validator = new FileListSettingsValidator();

        public FileListSettings()
        {
            BaseUrl = "https://filelist.io";
            MinimumSeeders = IndexerDefaults.MINIMUM_SEEDERS;

            Categories = new int[]
            {
                (int)FileListCategories.Docs
            };
        }

        [FieldDefinition(0, Label = "Username", Privacy = PrivacyLevel.UserName)]
        public string Username { get; set; }

        [FieldDefinition(1, Label = "Passkey", Privacy = PrivacyLevel.ApiKey)]
        public string Passkey { get; set; }

        [FieldDefinition(3, Label = "APIURL", Advanced = true, HelpText = "FileListBaseUrlHelpText")]
        public string BaseUrl { get; set; }

        [FieldDefinition(4, Label = "Categories", Type = FieldType.Select, SelectOptions = typeof(FileListCategories), HelpText = "FileListCategoriesHelpText")]
        public IEnumerable<int> Categories { get; set; }

        [FieldDefinition(5, Type = FieldType.Number, Label = "EarlyDownloadLimit", Unit = "days", HelpText = "FileListEarlyReleaseLimitHelpText", Advanced = true)]
        public int? EarlyReleaseLimit { get; set; }

        [FieldDefinition(6, Type = FieldType.Number, Label = "MinimumSeeders", HelpText = "FileListMinimumSeedersHelpText", Advanced = true)]
        public int MinimumSeeders { get; set; }

        [FieldDefinition(7)]
        public SeedCriteriaSettings SeedCriteria { get; set; } = new SeedCriteriaSettings();

        [FieldDefinition(8, Type = FieldType.Checkbox, Label = "RejectBlocklistedTorrentHashesWhileGrabbing", HelpText = "FileListRejectBlocklistedTorrentHashesWhileGrabbingHelpText", Advanced = true)]
        public bool RejectBlocklistedTorrentHashesWhileGrabbing { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }

    public enum FileListCategories
    {
        [FieldOption(Label = "Docs")]
        Docs = 16,
    }
}
