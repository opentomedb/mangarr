using System.ComponentModel;
using FluentValidation;
using Newtonsoft.Json;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;

namespace NzbDrone.Core.Download.Clients.Blackhole
{
    public class TorrentBlackholeSettingsValidator : AbstractValidator<TorrentBlackholeSettings>
    {
        public TorrentBlackholeSettingsValidator()
        {
            //Todo: Validate that the path actually exists
            RuleFor(c => c.TorrentFolder).IsValidPath();
            RuleFor(c => c.MagnetFileExtension).NotEmpty();
        }
    }

    public class TorrentBlackholeSettings : IProviderConfig
    {
        public TorrentBlackholeSettings()
        {
            MagnetFileExtension = ".magnet";
            ReadOnly = true;
        }

        private static readonly TorrentBlackholeSettingsValidator Validator = new TorrentBlackholeSettingsValidator();

        [FieldDefinition(0, Label = "TorrentFolder", Type = FieldType.Path, HelpText = "TorrentBlackholeTorrentFolderHelpText")]
        public string TorrentFolder { get; set; }

        [FieldDefinition(1, Label = "WatchFolder", Type = FieldType.Path, HelpText = "TorrentBlackholeWatchFolderHelpText")]
        public string WatchFolder { get; set; }

        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [FieldDefinition(2, Label = "SaveMagnetFiles", Type = FieldType.Checkbox, HelpText = "TorrentBlackholeSaveMagnetFilesHelpText")]
        public bool SaveMagnetFiles { get; set; }

        [FieldDefinition(3, Label = "MagnetFileExtension", Type = FieldType.Textbox, HelpText = "TorrentBlackholeMagnetFileExtensionHelpText")]
        public string MagnetFileExtension { get; set; }

        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [FieldDefinition(4, Label = "ReadOnly", Type = FieldType.Checkbox, HelpText = "TorrentBlackholeReadOnlyHelpText")]
        public bool ReadOnly { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
