using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.Test.Localization
{
    // UI translations v1 (2026-09-25): the real LocalizationService over the en.json the test build
    // copies next to the tests. A routed backend string proves its English by rendering through it.
    public static class EnglishLocalization
    {
        public static ILocalizationService Create()
        {
            var config = new Mock<IConfigService>();
            config.SetupGet(c => c.UILanguage).Returns((int)Language.English);

            var folders = new Mock<IAppFolderInfo>();
            folders.SetupGet(f => f.StartUpFolder).Returns(TestContext.CurrentContext.TestDirectory);

            return new LocalizationService(config.Object, folders.Object, new CacheManager(), LogManager.GetLogger(nameof(EnglishLocalization)));
        }
    }
}
