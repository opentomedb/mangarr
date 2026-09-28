using System.Collections.Generic;
using FluentValidation.Results;
using NzbDrone.Common.Localization;

namespace NzbDrone.Core.Validation
{
    public class NzbDroneValidationFailure : ValidationFailure
    {
        private ServerText _detailedDescriptionText;

        public bool IsWarning { get; set; }
        public string DetailedDescription { get; set; }
        public string InfoLink { get; set; }

        // Server messages (2026-09-26): the template ErrorMessage was built from, for the UI language at the
        // API boundary. Never serialized: the failure JSON the UI reads stays exactly as it was.
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ServerText Text { get; private set; }

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ServerText DetailedDescriptionText
        {
            get => _detailedDescriptionText;
            set
            {
                _detailedDescriptionText = value;
                DetailedDescription = value?.English;
            }
        }

        // What the copy constructor used to drop: the wrapped FluentValidation failure's validator and
        // placeholder values, so a provider settings failure can still be re-rendered in the UI language.
        // Not serialized, so the JSON keeps the null errorCode it always had for these.
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string SourceErrorCode { get; }

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<string, object> SourcePlaceholderValues { get; }

        public NzbDroneValidationFailure(string propertyName, string error)
            : base(propertyName, error)
        {
        }

        public NzbDroneValidationFailure(string propertyName, string error, object attemptedValue)
            : base(propertyName, error, attemptedValue)
        {
        }

        public NzbDroneValidationFailure(string propertyName, ServerText error)
            : base(propertyName, error.English)
        {
            Text = error;
        }

        public NzbDroneValidationFailure(ValidationFailure validationFailure)
            : base(validationFailure.PropertyName, validationFailure.ErrorMessage, validationFailure.AttemptedValue)
        {
            CustomState = validationFailure.CustomState;
            var state = validationFailure.CustomState as NzbDroneValidationState;

            IsWarning = state is { IsWarning: true };

            SourceErrorCode = validationFailure.ErrorCode;
            SourcePlaceholderValues = validationFailure.FormattedMessagePlaceholderValues;
        }
    }
}
