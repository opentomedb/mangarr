using System.Linq;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Light-novel storage (2026-09-22): while light-novel audio goes to Audiobookshelf, one health line
    // names what stops it -- unset, unreachable, key rejected, library missing, folder not mapped,
    // folder missing here. Silent while audio goes to the entry folder or no light-novel entry exists.
    [CheckOn(typeof(ConfigSavedEvent))]
    [CheckOn(typeof(NamingConfigSavedEvent))]
    public class AudiobookshelfCheck : HealthCheckBase
    {
        private readonly IAuthorService _authorService;
        private readonly ILightNovelStorage _storage;
        private readonly ILightNovelStorageProbe _probe;

        public AudiobookshelfCheck(IAuthorService authorService,
                                   ILightNovelStorage storage,
                                   ILightNovelStorageProbe probe,
                                   ILocalizationService localizationService)
            : base(localizationService)
        {
            _authorService = authorService;
            _storage = storage;
            _probe = probe;
        }

        public override HealthCheck Check()
        {
            if (_storage.AudioHome != LightNovelHome.Audiobookshelf ||
                !_authorService.GetAllAuthors().Any(a => a.Library == LibraryType.LightNovel))
            {
                return new HealthCheck(GetType());
            }

            var problems = _probe.TestAudiobookshelf(_storage.Audiobookshelf);

            return problems.Any()
                ? new HealthCheck(GetType(), HealthCheckResult.Error, string.Format(_localizationService.GetLocalizedString("LightNovelStorageAudiobookshelfCheck"), string.Join("; ", problems)), "#light-novel-storage-audiobookshelf")
                : new HealthCheck(GetType());
        }
    }
}
