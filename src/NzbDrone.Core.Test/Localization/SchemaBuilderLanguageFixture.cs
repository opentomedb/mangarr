using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Localization;
using NzbDrone.Test.Common;
using Readarr.Http.ClientSchema;

namespace NzbDrone.Core.Test.Localization
{
    // UI translations v1 (2026-09-25): SchemaBuilder used to localize a provider's labels once per process
    // and cache them, so a UI Language change reached provider settings only after a restart.
    [TestFixture]
    public class SchemaBuilderLanguageFixture : TestBase
    {
        private string _watchFolder;
        private List<Dictionary<string, object>> _tokenDictionaries;

        [SetUp]
        public void Setup()
        {
            _watchFolder = "Watch Folder";
            _tokenDictionaries = new List<Dictionary<string, object>>();

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                  .Callback<string, Dictionary<string, object>>((phrase, tokens) => _tokenDictionaries.Add(tokens))
                  .Returns<string, Dictionary<string, object>>((phrase, tokens) => phrase == "SchemaLanguageTestWatchFolder" ? _watchFolder : phrase);

            SchemaBuilder.Initialize(Mocker.Container);
        }

        // SchemaBuilder's localization service is static: leave the real English one behind, never this
        // fixture's mock or French service, so a later fixture that skips Initialize still renders English.
        [TearDown]
        public void TearDown()
        {
            Mocker.SetConstant(EnglishLocalization.Create());
            SchemaBuilder.Initialize(Mocker.Container);
        }

        [Test]
        public void a_ui_language_change_reaches_provider_labels_without_a_restart()
        {
            SchemaBuilder.ToSchema(new LabelledSettings()).Single().Label.Should().Be("Watch Folder");

            _watchFolder = "Dossier surveillé";

            SchemaBuilder.ToSchema(new LabelledSettings()).Single().Label.Should().Be("Dossier surveillé");
        }

        [Test]
        public void every_lookup_gets_its_own_token_dictionary()
        {
            // LocalizationService.ReplaceTokens adds appName to the dictionary it is given.
            SchemaBuilder.ToSchema(new LabelledSettings());
            SchemaBuilder.ToSchema(new LabelledSettings());

            _tokenDictionaries.Should().HaveCount(4);
            _tokenDictionaries[0].Should().NotBeSameAs(_tokenDictionaries[2]);
        }

        // The configured UI Language switched between two calls, through the real LocalizationService. Its
        // dictionary cache holds one entry per language; saving the setting clears them
        // (HandleAsync(ConfigSavedEvent)), and SchemaBuilder must not hold the old language either.
        [Test]
        public void a_configured_ui_language_switch_reaches_provider_labels_on_the_next_call()
        {
            var config = new Mock<IConfigService>();
            config.SetupGet(c => c.UILanguage).Returns((int)Language.English);

            var folders = new Mock<IAppFolderInfo>();
            folders.SetupGet(f => f.StartUpFolder).Returns(TestContext.CurrentContext.TestDirectory);

            var service = new LocalizationService(config.Object, folders.Object, new CacheManager(), LogManager.GetLogger(nameof(SchemaBuilderLanguageFixture)));

            Mocker.SetConstant<ILocalizationService>(service);
            SchemaBuilder.Initialize(Mocker.Container);

            SchemaBuilder.ToSchema(new HostSettings()).Single().Label.Should().Be("Host");

            config.SetupGet(c => c.UILanguage).Returns((int)Language.French);
            service.HandleAsync(new ConfigSavedEvent());

            SchemaBuilder.ToSchema(new HostSettings()).Single().Label.Should().Be("Hôte");
        }

        // Ported from NzbDrone.Api.Test's SchemaBuilderFixture (never built by the maintainer's test script or CI): the
        // rewrite moves Label/HelpText/HelpTextWarning out of the cached mapping, so pin that all three still
        // arrive, with the nested prefix and the renumbered Order.
        [Test]
        public void should_return_field_for_every_property()
        {
            var schema = SchemaBuilder.ToSchema(new TestModel());
            schema.Should().HaveCount(2);
        }

        [Test]
        public void schema_should_have_proper_fields()
        {
            var model = new TestModel
            {
                FirstName = "Bob",
                LastName = "Poop"
            };

            var schema = SchemaBuilder.ToSchema(model);

            schema.Should().Contain(c => c.Order == 1 && c.Name == "lastName" && c.Label == "Last Name" && c.HelpText == "Your Last Name" && c.HelpTextWarning == "Mandatory Last Name" && (string)c.Value == "Poop");
            schema.Should().Contain(c => c.Order == 0 && c.Name == "firstName" && c.Label == "First Name" && c.HelpText == "Your First Name" && c.HelpTextWarning == "Mandatory First Name" && (string)c.Value == "Bob");
        }

        [Test]
        public void schema_should_have_nested_fields()
        {
            var model = new NestedTestModel
            {
                Name =
                {
                    FirstName = "Bob",
                    LastName = "Poop"
                }
            };

            var schema = SchemaBuilder.ToSchema(model);

            schema.Should().Contain(c => c.Order == 0 && c.Name == "name.firstName" && c.Label == "First Name" && c.HelpText == "Your First Name" && c.HelpTextWarning == "Mandatory First Name" && (string)c.Value == "Bob");
            schema.Should().Contain(c => c.Order == 1 && c.Name == "name.lastName" && c.Label == "Last Name" && c.HelpText == "Your Last Name" && c.HelpTextWarning == "Mandatory Last Name" && (string)c.Value == "Poop");
            schema.Should().Contain(c => c.Order == 2 && c.Name == "quote" && c.Label == "Quote" && c.HelpText == "Your Favorite Quote");
        }

        public class LabelledSettings
        {
            [FieldDefinition(0, Label = "SchemaLanguageTestWatchFolder", HelpText = "SchemaLanguageTestWatchFolderHelpText")]
            public string Folder { get; set; }
        }

        public class HostSettings
        {
            [FieldDefinition(0, Label = "Host")]
            public string Host { get; set; }
        }

        public class TestModel
        {
            [FieldDefinition(0, Label = "First Name", HelpText = "Your First Name", HelpTextWarning = "Mandatory First Name")]
            public string FirstName { get; set; }

            [FieldDefinition(1, Label = "Last Name", HelpText = "Your Last Name", HelpTextWarning = "Mandatory Last Name")]
            public string LastName { get; set; }

            public string Other { get; set; }
        }

        public class NestedTestModel
        {
            [FieldDefinition(0)]
            public TestModel Name { get; set; } = new TestModel();

            [FieldDefinition(1, Label = "Quote", HelpText = "Your Favorite Quote")]
            public string Quote { get; set; }
        }
    }
}
