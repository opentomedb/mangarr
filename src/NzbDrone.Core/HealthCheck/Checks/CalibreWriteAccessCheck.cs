using System.Linq;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // Light-novel storage (2026-09-22): while light-novel ebooks go to calibre, one health line names
    // what stops them -- unset, unreachable, login rejected, library missing, no write access (calibre's
    // trusted_ips or a user), mapped folder missing -- instead of a failure on every EPUB import.
    // Silent while ebooks go to the entry folder or no light-novel entry exists.
    [CheckOn(typeof(ConfigSavedEvent))]
    public class CalibreWriteAccessCheck : HealthCheckBase
    {
        private readonly IAuthorService _authorService;
        private readonly ILightNovelStorage _storage;
        private readonly ILightNovelStorageProbe _probe;

        public CalibreWriteAccessCheck(IAuthorService authorService,
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
            if (_storage.EbookHome != LightNovelHome.Calibre ||
                !_authorService.GetAllAuthors().Any(a => a.Library == LibraryType.LightNovel))
            {
                return new HealthCheck(GetType());
            }

            var problems = _probe.TestCalibre(_storage.Calibre);

            return problems.Any()
                ? new HealthCheck(GetType(), HealthCheckResult.Error, string.Format(_localizationService.GetLocalizedString("LightNovelStorageCalibreCheck"), string.Join("; ", problems)), "#light-novel-storage-calibre")
                : new HealthCheck(GetType());
        }
    }
}
