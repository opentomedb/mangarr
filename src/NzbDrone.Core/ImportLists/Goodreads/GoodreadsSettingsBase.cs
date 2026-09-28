using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Goodreads
{
    public class GoodreadsSettingsBaseValidator<TSettings> : AbstractValidator<TSettings>
        where TSettings : GoodreadsSettingsBase<TSettings>
    {
        public GoodreadsSettingsBaseValidator()
        {
            RuleFor(c => c.AccessToken).NotEmpty();
            RuleFor(c => c.AccessTokenSecret).NotEmpty();
        }
    }

    public class GoodreadsSettingsBase<TSettings> : IImportListSettings
        where TSettings : GoodreadsSettingsBase<TSettings>
    {
        public GoodreadsSettingsBase()
        {
            SignIn = "startOAuth";
        }

        public string BaseUrl { get; set; }

        public string SigningUrl => "https://auth.servarr.com/v1/goodreads/sign";
        public string OAuthUrl => "https://www.goodreads.com/oauth/authorize";
        public string OAuthRequestTokenUrl => "https://www.goodreads.com/oauth/request_token";
        public string OAuthAccessTokenUrl => "https://www.goodreads.com/oauth/access_token";

        [FieldDefinition(0, Label = "AccessToken", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string AccessToken { get; set; }

        [FieldDefinition(0, Label = "AccessTokenSecret", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string AccessTokenSecret { get; set; }

        [FieldDefinition(0, Label = "RequestTokenSecret", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string RequestTokenSecret { get; set; }

        [FieldDefinition(0, Label = "UserId", HelpText = "GoodreadsSettingsBaseUserIdHelpText", Type = FieldType.Textbox, Advanced = true)]
        public string UserId { get; set; }

        [FieldDefinition(0, Label = "GoodreadsSettingsBaseUserName", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public string UserName { get; set; }

        [FieldDefinition(99, Label = "AuthenticateWithGoodreads", Type = FieldType.OAuth)]
        public string SignIn { get; set; }

        protected virtual AbstractValidator<TSettings> Validator => new GoodreadsSettingsBaseValidator<TSettings>();

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate((TSettings)this));
        }
    }
}
