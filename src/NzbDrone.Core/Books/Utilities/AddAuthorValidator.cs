using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;

namespace NzbDrone.Core.Books
{
    public interface IAddAuthorValidator
    {
        ValidationResult Validate(Author instance);
    }

    public class AddAuthorValidator : AbstractValidator<Author>, IAddAuthorValidator
    {
        public AddAuthorValidator(RootFolderValidator rootFolderValidator,
                                  RecycleBinValidator recycleBinValidator,
                                  AuthorPathValidator authorPathValidator,
                                  AuthorAncestorValidator authorAncestorValidator,
                                  QualityProfileExistsValidator qualityProfileExistsValidator,
                                  MetadataProfileExistsValidator metadataProfileExistsValidator)
        {
            RuleFor(c => c.Path).Cascade(CascadeMode.Stop)
                                .IsValidPath()
                                .SetValidator(rootFolderValidator)
                                .SetValidator(recycleBinValidator)
                                .SetValidator(authorPathValidator)
                                .SetValidator(authorAncestorValidator);

            RuleFor(c => c.QualityProfileId).SetValidator(qualityProfileExistsValidator);
            RuleFor(c => c.AudioQualityProfileId).SetValidator(qualityProfileExistsValidator);

            RuleFor(c => c.MetadataProfileId).SetValidator(metadataProfileExistsValidator);

            // Reject single-character / blank series names. A 1-char term (the 'L'/'N' phantom
            // lookups) matches nothing useful, only produces a junk "local-l" series, and triggers
            // an expensive metadata lookup. A real manga title is always at least two characters.
            RuleFor(c => c.Name)
                .Must(name => name != null && name.Trim().Length >= 2)
                .WithMessage("Series name must be at least 2 characters");
        }
    }
}
