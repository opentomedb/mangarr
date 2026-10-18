using System.Linq;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Validation.Paths
{
    // A folder Mangarr manages itself (PDF originals) must not sit inside a library root: the library scan
    // would pick its files up as volumes, and retention could then delete tracked files.
    public class InsideRootFolderValidator : PropertyValidator
    {
        private readonly IRootFolderService _rootFolderService;

        public InsideRootFolderValidator(IRootFolderService rootFolderService)
        {
            _rootFolderService = rootFolderService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is inside a library root folder; keep PDF originals in their own folder outside your libraries";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            var path = context.PropertyValue.ToString();

            context.MessageFormatter.AppendArgument("path", path);

            return !_rootFolderService.All().Any(r => r.Path.IsParentPath(path));
        }
    }
}
