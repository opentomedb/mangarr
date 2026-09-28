using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.RTorrent
{
    public class RTorrentSettingsValidator : AbstractValidator<RTorrentSettings>
    {
        public RTorrentSettingsValidator()
        {
            RuleFor(c => c.Host).ValidHost();
            RuleFor(c => c.Port).InclusiveBetween(1, 65535);
            RuleFor(c => c.MusicCategory).NotEmpty()
                                      .WithMessage("A category is recommended")
                                      .AsWarning();
        }
    }

    public class RTorrentSettings : IProviderConfig
    {
        private static readonly RTorrentSettingsValidator Validator = new RTorrentSettingsValidator();

        public RTorrentSettings()
        {
            Host = "localhost";
            Port = 8080;
            UrlBase = "RPC2";
            MusicCategory = "readarr";
            OlderTvPriority = (int)RTorrentPriority.Normal;
            RecentTvPriority = (int)RTorrentPriority.Normal;
        }

        [FieldDefinition(0, Label = "Host", Type = FieldType.Textbox)]
        public string Host { get; set; }

        [FieldDefinition(1, Label = "Port", Type = FieldType.Textbox)]
        public int Port { get; set; }

        [FieldDefinition(2, Label = "UseSSL", Type = FieldType.Checkbox, HelpText = "RTorrentUseSslHelpText")]
        public bool UseSsl { get; set; }

        [FieldDefinition(3, Label = "UrlPath", Type = FieldType.Textbox, HelpText = "RTorrentUrlBaseHelpText")]
        public string UrlBase { get; set; }

        [FieldDefinition(4, Label = "Username", Type = FieldType.Textbox, Privacy = PrivacyLevel.UserName)]
        public string Username { get; set; }

        [FieldDefinition(5, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password)]
        public string Password { get; set; }

        [FieldDefinition(6, Label = "Category", Type = FieldType.Textbox, HelpText = "DelugeMusicCategoryHelpText")]
        public string MusicCategory { get; set; }

        [FieldDefinition(7, Label = "PostImportCategory", Type = FieldType.Textbox, Advanced = true, HelpText = "DelugeMusicImportedCategoryHelpText")]
        public string MusicImportedCategory { get; set; }

        [FieldDefinition(8, Label = "Directory", Type = FieldType.Textbox, Advanced = true, HelpText = "RTorrentMusicDirectoryHelpText")]
        public string MusicDirectory { get; set; }

        [FieldDefinition(9, Label = "RecentPriority", Type = FieldType.Select, SelectOptions = typeof(RTorrentPriority), HelpText = "NzbVortexRecentTvPriorityHelpText")]
        public int RecentTvPriority { get; set; }

        [FieldDefinition(10, Label = "OlderPriority", Type = FieldType.Select, SelectOptions = typeof(RTorrentPriority), HelpText = "NzbVortexOlderTvPriorityHelpText")]
        public int OlderTvPriority { get; set; }

        [FieldDefinition(11, Label = "AddStopped", Type = FieldType.Checkbox, HelpText = "RTorrentAddStoppedHelpText")]
        public bool AddStopped { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
