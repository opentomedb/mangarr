using System.IO;
using System.Linq;
using System.Text.Json;
using Moq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.Test.Localization
{
    // Server messages (2026-09-26): a real LocalizationService over fixture locale files in a temp folder, so
    // the localizer and surface tests never depend on (or add to) the shipped en.json. de.json and ja.json
    // are written empty so a German or Japanese UI loads without the "missing resource" Error.
    public static class ServerMessageTestLocalization
    {
        public static (IServerMessageLocalizer Localizer, Mock<IConfigService> Config, LocalizationService Service) Create(string folder, Language uiLanguage, params (string Key, string En, string Fr)[] keys)
        {
            var core = Path.Combine(folder, "Localization", "Core");
            Directory.CreateDirectory(core);

            File.WriteAllText(Path.Combine(core, "en.json"), JsonSerializer.Serialize(keys.ToDictionary(k => k.Key, k => k.En)));
            File.WriteAllText(Path.Combine(core, "fr.json"), JsonSerializer.Serialize(keys.Where(k => k.Fr != null).ToDictionary(k => k.Key, k => k.Fr)));
            File.WriteAllText(Path.Combine(core, "de.json"), "{}");
            File.WriteAllText(Path.Combine(core, "ja.json"), "{}");

            var config = new Mock<IConfigService>();
            config.SetupGet(c => c.UILanguage).Returns((int)uiLanguage);

            var folders = new Mock<IAppFolderInfo>();
            folders.SetupGet(f => f.StartUpFolder).Returns(folder);

            var service = new LocalizationService(config.Object, folders.Object, new CacheManager(), LogManager.GetLogger(nameof(ServerMessageTestLocalization)));

            return (new ServerMessageLocalizer(service, config.Object), config, service);
        }
    }
}
