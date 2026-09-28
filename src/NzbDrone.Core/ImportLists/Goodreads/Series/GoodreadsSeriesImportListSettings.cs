using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Goodreads
{
    public class GoodreadsSeriesImportListValidator : AbstractValidator<GoodreadsSeriesImportListSettings>
    {
        public GoodreadsSeriesImportListValidator()
        {
            RuleFor(c => c.SeriesId).GreaterThan(0);
        }
    }

    public class GoodreadsSeriesImportListSettings : IImportListSettings
    {
        private static readonly GoodreadsSeriesImportListValidator Validator = new ();

        public GoodreadsSeriesImportListSettings()
        {
            BaseUrl = "www.goodreads.com";
        }

        public string BaseUrl { get; set; }

        [FieldDefinition(0, Label = "SeriesID", HelpText = "GoodreadsSeriesImportListSeriesIdHelpText")]
        public int SeriesId { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
