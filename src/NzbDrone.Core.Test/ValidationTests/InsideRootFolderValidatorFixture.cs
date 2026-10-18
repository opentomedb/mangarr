using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ValidationTests
{
    public class InsideRootFolderValidatorFixture : CoreTest<InsideRootFolderValidator>
    {
        private TestValidator<Author> _validator;
        private string _root;

        [SetUp]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "mangarr-library");

            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.All())
                  .Returns(new List<RootFolder> { new RootFolder { Path = _root } });

            _validator = new TestValidator<Author>
                            {
                                v => v.RuleFor(s => s.Path).SetValidator(Subject)
                            };
        }

        private bool IsValid(string path)
        {
            var author = Builder<Author>.CreateNew().With(s => s.Path = path).Build();

            return _validator.Validate(author).IsValid;
        }

        [Test]
        public void should_not_be_valid_inside_a_root_folder()
        {
            IsValid(Path.Combine(_root, "pdf-originals")).Should().BeFalse();
        }

        [Test]
        public void should_not_be_valid_deep_inside_a_root_folder()
        {
            IsValid(Path.Combine(_root, "a", "b")).Should().BeFalse();
        }

        [Test]
        public void should_be_valid_outside_every_root_folder()
        {
            IsValid(Path.Combine(Path.GetTempPath(), "pdf-originals")).Should().BeTrue();
        }

        [Test]
        public void should_be_valid_for_a_sibling_with_the_same_prefix()
        {
            IsValid(_root + "-originals").Should().BeTrue();
        }

        [Test]
        public void should_be_valid_when_there_are_no_root_folders()
        {
            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder>());

            IsValid(Path.Combine(_root, "pdf-originals")).Should().BeTrue();
        }
    }
}
