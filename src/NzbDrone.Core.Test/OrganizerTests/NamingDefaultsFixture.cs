using FluentAssertions;
using FluentValidation;
using NUnit.Framework;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.OrganizerTests
{
    // Beta polish (2026-09-28): the default only reaches a DB without a NamingConfig row (a fresh
    // install); a stored row, an existing install's, is returned as it is.
    [TestFixture]
    public class NamingDefaultsFixture : DbTest<NamingConfigService, NamingConfig>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<INamingConfigRepository>(Mocker.Resolve<NamingConfigRepository>());
        }

        [Test]
        public void a_fresh_install_renames_flat()
        {
            var config = Subject.GetConfig();

            config.RenameBooks.Should().BeTrue();
            config.StandardBookFormat.Should().Be("{Author Name} - Vol. {Volume:00}");
            config.AuthorFolderFormat.Should().Be("{Author Name}");
            config.ReplaceIllegalCharacters.Should().BeTrue();
            config.ColonReplacementFormat.Should().Be(ColonReplacementFormat.Smart);

            AllStoredModels.Should().HaveCount(1);
        }

        [Test]
        public void an_existing_row_is_kept()
        {
            Db.Insert(new NamingConfig
            {
                RenameBooks = false,
                ReplaceIllegalCharacters = true,
                ColonReplacementFormat = ColonReplacementFormat.Smart,
                StandardBookFormat = "{Book Title}/{Author Name} - {Book Title}{ (PartNumber)}",
                AuthorFolderFormat = "{Author Name}"
            });

            var config = Subject.GetConfig();

            config.RenameBooks.Should().BeFalse();
            config.StandardBookFormat.Should().Be("{Book Title}/{Author Name} - {Book Title}{ (PartNumber)}");
            AllStoredModels.Should().HaveCount(1);
        }

        [Test]
        public void the_fresh_format_passes_the_format_validator()
        {
            var validator = new InlineValidator<NamingConfig>();
            validator.RuleFor(c => c.StandardBookFormat).ValidBookFormat();
            validator.RuleFor(c => c.AuthorFolderFormat).ValidAuthorFolderFormat();

            validator.Validate(NamingConfig.Default).IsValid.Should().BeTrue();
        }
    }
}
