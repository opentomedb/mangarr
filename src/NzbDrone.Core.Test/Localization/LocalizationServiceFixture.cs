using System;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Localization
{
    [TestFixture]
    public class LocalizationServiceFixture : CoreTest<LocalizationService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns((int)Language.English);

            Mocker.GetMock<IAppFolderInfo>().Setup(m => m.StartUpFolder).Returns(TestContext.CurrentContext.TestDirectory);
        }

        [Test]
        public void should_get_string_in_dictionary_if_lang_exists_and_string_exists()
        {
            var localizedString = Subject.GetLocalizedString("UiLanguage");

            localizedString.Should().Be("UI Language");
        }

        // UI translations v1 (2026-09-25): fr and de ask for fr_FR/de_DE (and pt for pt_PT), which never
        // shipped. Readarr logged an Error for the missing regional file on every cache fill; a language
        // whose base file exists is not an error.
        [Test]
        public void should_get_string_in_french()
        {
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns((int)Language.French);

            var localizedString = Subject.GetLocalizedString("UiLanguage");

            localizedString.Should().Be("Langue de l'IU");

            ExceptionVerification.ExpectedErrors(0);
        }

        // Server messages (2026-09-26): the cache held one dictionary whatever the language (its key was the
        // constant "localization"), so the first language to fill it answered for every other one. The
        // server-message localizer needs en.json and the UI language's file side by side.
        [Test]
        public void each_language_has_its_own_cached_dictionary()
        {
            Subject.GetLocalizationDictionary("en")["Host"].Should().Be("Host");
            Subject.GetLocalizationDictionary("fr_fr")["Host"].Should().Be("Hôte");
            Subject.GetLocalizationDictionary("en")["Host"].Should().Be("Host");
        }

        [Test]
        public void a_saved_config_clears_every_language()
        {
            var en = Subject.GetLocalizationDictionary("en");
            var fr = Subject.GetLocalizationDictionary("fr_fr");

            Subject.HandleAsync(new ConfigSavedEvent());

            Subject.GetLocalizationDictionary("en").Should().NotBeSameAs(en);
            Subject.GetLocalizationDictionary("fr_fr").Should().NotBeSameAs(fr);
        }

        [TestCase(18)] // Language.Portuguese -> pt_PT, only pt.json ships
        [TestCase(4)] // Language.German -> de_DE, only de.json ships
        [TestCase(8)] // Language.Japanese -> ja, no country code
        public void should_not_log_an_error_when_the_base_language_file_exists(int language)
        {
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns(language);

            Subject.GetLocalizedString("UiLanguage").Should().NotBe("UI Language");

            ExceptionVerification.ExpectedErrors(0);
        }

        [Test]
        public void should_still_log_an_error_when_no_file_for_the_language_exists()
        {
            // Language.Norwegian -> "no", and there is no no.json (only nb_NO.json).
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns((int)Language.Norwegian);

            Subject.GetLocalizedString("UiLanguage").Should().Be("UI Language");

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_keep_chinese_on_its_regional_file()
        {
            // Language.Chinese -> zh_CN; zh_CN.json ships, zh.json does not.
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns((int)Language.Chinese);

            Subject.GetLocalizedString("UiLanguage").Should().NotBe("UI Language");

            ExceptionVerification.ExpectedErrors(0);
        }

        // i18n leftovers (2026-09-28): the zh-HK IsoLanguages entry (Parser/IsoLanguages.cs) was added after
        // the existing "tw" one, so it must not become the first Language.Chinese entry -- this pins the
        // actual file name GetSetLanguageFileName() builds, not just IsoLanguages.Get(Language.Chinese).
        // GetSetLanguageFileName() itself builds "zh_cn" (the "cn" entry's CountryCode, as stored);
        // GetResourceFilename uppercases the region part when it resolves the file on disk (zh_CN.json).
        [Test]
        public void get_set_language_file_name_still_yields_zh_cn_for_chinese()
        {
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns((int)Language.Chinese);

            var fileName = (string)typeof(LocalizationService)
                .GetMethod("GetSetLanguageFileName", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(Subject, null);

            fileName.Should().Be("zh_cn");
        }

        [Test]
        public void should_get_string_in_default_dictionary_if_unknown_language_and_string_exists()
        {
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns(0);
            var localizedString = Subject.GetLocalizedString("UiLanguage");

            localizedString.Should().Be("UI Language");
        }

        [Test]
        public void should_return_argument_if_string_doesnt_exists()
        {
            var localizedString = Subject.GetLocalizedString("badString");

            localizedString.Should().Be("badString");
        }

        [Test]
        public void should_return_argument_if_string_doesnt_exists_default_lang()
        {
            var localizedString = Subject.GetLocalizedString("badString");

            localizedString.Should().Be("badString");
        }

        [Test]
        public void should_throw_if_empty_string_passed()
        {
            Assert.Throws<ArgumentNullException>(() => Subject.GetLocalizedString(""));
        }

        [Test]
        public void should_throw_if_null_string_passed()
        {
            Assert.Throws<ArgumentNullException>(() => Subject.GetLocalizedString(null));
        }
    }
}
