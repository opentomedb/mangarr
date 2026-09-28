using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers
{
    public class SeedCriteriaSettingsValidator : AbstractValidator<SeedCriteriaSettings>
    {
        public SeedCriteriaSettingsValidator(double seedRatioMinimum = 0.0, int seedTimeMinimum = 0, int discographySeedTimeMinimum = 0)
        {
            RuleFor(c => c.SeedRatio).GreaterThan(0.0)
                .When(c => c.SeedRatio.HasValue)
                .AsWarning().WithMessage("Should be greater than zero");

            RuleFor(c => c.SeedTime).GreaterThan(0)
                .When(c => c.SeedTime.HasValue)
                .AsWarning().WithMessage("Should be greater than zero");

            RuleFor(c => c.DiscographySeedTime).GreaterThan(0)
                .When(c => c.DiscographySeedTime.HasValue)
                .AsWarning().WithMessage("Should be greater than zero");

            if (seedRatioMinimum != 0.0)
            {
                RuleFor(c => c.SeedRatio).GreaterThanOrEqualTo(seedRatioMinimum)
                    .When(c => c.SeedRatio > 0.0)
                    .AsWarning()
                    .WithMessage(ServerRuleMessages.Format("Under {0} leads to H&R", seedRatioMinimum));
            }

            if (seedTimeMinimum != 0)
            {
                RuleFor(c => c.SeedTime).GreaterThanOrEqualTo(seedTimeMinimum)
                    .When(c => c.SeedTime > 0)
                    .AsWarning()
                    .WithMessage(ServerRuleMessages.Format("Under {0} leads to H&R", seedTimeMinimum));
            }

            if (discographySeedTimeMinimum != 0)
            {
                RuleFor(c => c.DiscographySeedTime).GreaterThanOrEqualTo(discographySeedTimeMinimum)
                    .When(c => c.DiscographySeedTime > 0)
                    .AsWarning()
                    .WithMessage(ServerRuleMessages.Format("Under {0} leads to H&R", discographySeedTimeMinimum));
            }
        }
    }

    public class SeedCriteriaSettings
    {
        [FieldDefinition(0, Type = FieldType.Number, Label = "IndexerSettingsSeedRatio", HelpText = "IndexerSettingsSeedRatioHelpText")]
        public double? SeedRatio { get; set; }

        [FieldDefinition(1, Type = FieldType.Number, Label = "IndexerSettingsSeedTime", Unit = "minutes", HelpText = "IndexerSettingsSeedTimeHelpText", Advanced = true)]
        public int? SeedTime { get; set; }

        [FieldDefinition(2, Type = FieldType.Textbox, Label = "DiscographySeedTime", Unit = "minutes", HelpText = "SeedCriteriaDiscographySeedTimeHelpText", Advanced = true)]
        public int? DiscographySeedTime { get; set; }
    }
}
