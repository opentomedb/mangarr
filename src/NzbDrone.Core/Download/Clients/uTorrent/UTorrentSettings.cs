using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.UTorrent
{
    public class UTorrentSettingsValidator : AbstractValidator<UTorrentSettings>
    {
        public UTorrentSettingsValidator()
        {
            RuleFor(c => c.Host).ValidHost();
            RuleFor(c => c.Port).InclusiveBetween(1, 65535);
            RuleFor(c => c.UrlBase).ValidUrlBase().When(c => c.UrlBase.IsNotNullOrWhiteSpace());
            RuleFor(c => c.MusicCategory).NotEmpty();
        }
    }

    public class UTorrentSettings : IProviderConfig
    {
        private static readonly UTorrentSettingsValidator Validator = new UTorrentSettingsValidator();

        public UTorrentSettings()
        {
            Host = "localhost";
            Port = 8080;
            MusicCategory = "readarr";
        }

        [FieldDefinition(0, Label = "Host", Type = FieldType.Textbox)]
        public string Host { get; set; }

        [FieldDefinition(1, Label = "Port", Type = FieldType.Textbox)]
        public int Port { get; set; }

        [FieldDefinition(2, Label = "UseSSL", Type = FieldType.Checkbox, HelpText = "UTorrentUseSslHelpText")]
        public bool UseSsl { get; set; }

        [FieldDefinition(3, Label = "DelugeUrlBase", Type = FieldType.Textbox, Advanced = true, HelpText = "UTorrentUrlBaseHelpText")]
        public string UrlBase { get; set; }

        [FieldDefinition(4, Label = "Username", Type = FieldType.Textbox, Privacy = PrivacyLevel.UserName)]
        public string Username { get; set; }

        [FieldDefinition(5, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password)]
        public string Password { get; set; }

        [FieldDefinition(6, Label = "Category", Type = FieldType.Textbox, HelpText = "DelugeMusicCategoryHelpText")]
        public string MusicCategory { get; set; }

        [FieldDefinition(7, Label = "PostImportCategory", Type = FieldType.Textbox, Advanced = true, HelpText = "QBittorrentMusicImportedCategoryHelpText")]
        public string MusicImportedCategory { get; set; }

        [FieldDefinition(8, Label = "RecentPriority", Type = FieldType.Select, SelectOptions = typeof(UTorrentPriority), HelpText = "NzbVortexRecentTvPriorityHelpText")]
        public int RecentTvPriority { get; set; }

        [FieldDefinition(9, Label = "OlderPriority", Type = FieldType.Select, SelectOptions = typeof(UTorrentPriority), HelpText = "NzbVortexOlderTvPriorityHelpText")]
        public int OlderTvPriority { get; set; }

        [FieldDefinition(10, Label = "InitialState", Type = FieldType.Select, SelectOptions = typeof(UTorrentState), HelpText = "UTorrentIntialStateHelpText")]
        public int IntialState { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
