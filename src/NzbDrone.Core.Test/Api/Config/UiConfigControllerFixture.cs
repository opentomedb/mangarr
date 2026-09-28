using System.Collections.Generic;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;
using Readarr.Api.V1.Config;

namespace NzbDrone.Core.Test.Api.Config
{
    // Preferred Edition (2026-09-24, final fix round Minor 3): a tab loaded before Preferred Edition existed
    // PUTs /config/ui without PreferredEditionLanguages. That must not 400; the stored chain is kept. A value
    // that is sent must still be a valid chain. Same test-only subclass pattern as
    // MediaManagementConfigControllerFixture (SharedValidator is protected).
    [TestFixture]
    public class UiConfigControllerFixture : CoreTest<UiConfigControllerFixture.TestableController>
    {
        private static UiConfigResource ValidResource(string chain)
        {
            return new UiConfigResource { UILanguage = 1, PreferredEditionLanguages = chain };
        }

        [Test]
        public void a_put_without_the_preferred_edition_field_validates_and_passes_null_through()
        {
            var resource = ValidResource(null);

            Subject.ValidateShared(resource).IsValid.Should().BeTrue();

            Subject.SaveConfig(resource);

            Mocker.GetMock<IConfigService>()
                  .Verify(c => c.SaveConfigDictionary(It.Is<Dictionary<string, object>>(d => d["PreferredEditionLanguages"] == null)));
        }

        [TestCase("")]
        [TestCase("french")]
        [TestCase("fr,fr")]
        public void an_invalid_chain_that_is_sent_still_fails(string chain)
        {
            var result = Subject.ValidateShared(ValidResource(chain));

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "PreferredEditionLanguages");
        }

        [Test]
        public void a_valid_chain_passes()
        {
            Subject.ValidateShared(ValidResource("fr,en")).IsValid.Should().BeTrue();
        }

        public class TestableController : UiConfigController
        {
            public TestableController(IConfigFileProvider configFileProvider, IConfigService configService)
                : base(configFileProvider, configService)
            {
            }

            public ValidationResult ValidateShared(UiConfigResource resource)
            {
                return SharedValidator.Validate(resource);
            }
        }
    }
}
