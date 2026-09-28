using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Http.REST.Attributes;
using Readarr.Http;

namespace Readarr.Api.V1.Config
{
    [V1ApiController("config/mediamanagement")]
    public class MediaManagementConfigController : ConfigController<MediaManagementConfigResource>
    {
        public MediaManagementConfigController(IConfigService configService,
                                           PathExistsValidator pathExistsValidator,
                                           FolderChmodValidator folderChmodValidator,
                                           FolderWritableValidator folderWritableValidator,
                                           AuthorPathValidator authorPathValidator,
                                           StartupFolderValidator startupFolderValidator,
                                           SystemFolderValidator systemFolderValidator,
                                           RootFolderAncestorValidator rootFolderAncestorValidator,
                                           RootFolderValidator rootFolderValidator)
            : base(configService)
        {
            SharedValidator.RuleFor(c => c.RecycleBin).IsValidPath()
                                                      .SetValidator(folderWritableValidator)
                                                      .SetValidator(rootFolderValidator)
                                                      .SetValidator(pathExistsValidator)
                                                      .SetValidator(authorPathValidator)
                                                      .SetValidator(rootFolderAncestorValidator)
                                                      .SetValidator(startupFolderValidator)
                                                      .SetValidator(systemFolderValidator)
                                                      .When(c => !string.IsNullOrWhiteSpace(c.RecycleBin));
            SharedValidator.RuleFor(c => c.RecycleBinCleanupDays).GreaterThanOrEqualTo(0);
            SharedValidator.RuleFor(c => c.ChmodFolder).SetValidator(folderChmodValidator).When(c => !string.IsNullOrEmpty(c.ChmodFolder) && (OsInfo.IsLinux || OsInfo.IsOsx));
            SharedValidator.RuleFor(c => c.MinimumFreeSpaceWhenImporting).GreaterThanOrEqualTo(100);

            // The light-novel delivery format (calibre conversion) accepts epub|azw3|kepub only; an
            // unknown value is rejected so a typo cannot look like it took effect.
            SharedValidator.RuleFor(c => c.PreferredLightNovelFormat)
                           .Must(PreferredFormats.IsKnownLightNovelFormat)
                           .WithMessage("Must be one of epub, azw3, kepub");

            // Light-novel storage (2026-09-22): a home is entry or its own service; a service that is a
            // home needs its address (and ABS its library); a path pair is both or neither.
            // Fix round 1: a null home (a PUT from a stale tab or an older client that never sends the
            // new fields) is not an unknown one -- SaveConfigDictionary skips it and the stored home
            // survives, so the Must rule only runs once a home is actually present. The URL and
            // path-pair rules below are scoped to their own home too, so an entry-homed kind's unused,
            // possibly half-filled or invalid calibre/ABS fields never block a save.
            SharedValidator.RuleFor(c => c.LightNovelEbookHome)
                           .Must(h => h == LightNovelHome.Entry || h == LightNovelHome.Calibre)
                           .When(c => c.LightNovelEbookHome != null)
                           .WithMessage("Must be Entry Folder or Calibre Library");
            SharedValidator.RuleFor(c => c.LightNovelAudioHome)
                           .Must(h => h == LightNovelHome.Entry || h == LightNovelHome.Audiobookshelf)
                           .When(c => c.LightNovelAudioHome != null)
                           .WithMessage("Must be Entry Folder or Audiobookshelf Library");
            SharedValidator.RuleFor(c => c.CalibreContentServerUrl)
                           .NotEmpty().When(c => c.LightNovelEbookHome == LightNovelHome.Calibre)
                           .WithMessage("Needed while ebooks go to Calibre");
            SharedValidator.RuleFor(c => c.CalibreContentServerUrl)
                           .ValidRootUrl().When(c => c.LightNovelEbookHome == LightNovelHome.Calibre && c.CalibreContentServerUrl.IsNotNullOrWhiteSpace());
            SharedValidator.RuleFor(c => c.AudiobookshelfUrl)
                           .NotEmpty().When(c => c.LightNovelAudioHome == LightNovelHome.Audiobookshelf)
                           .WithMessage("Needed while audiobooks go to Audiobookshelf");
            SharedValidator.RuleFor(c => c.AudiobookshelfUrl)
                           .ValidRootUrl().When(c => c.LightNovelAudioHome == LightNovelHome.Audiobookshelf && c.AudiobookshelfUrl.IsNotNullOrWhiteSpace());
            SharedValidator.RuleFor(c => c.AudiobookshelfLibraryId)
                           .NotEmpty().When(c => c.LightNovelAudioHome == LightNovelHome.Audiobookshelf)
                           .WithMessage("Choose the Audiobookshelf Library");
            SharedValidator.RuleFor(c => c.CalibreLocalPath).NotEmpty().When(c => c.LightNovelEbookHome == LightNovelHome.Calibre && c.CalibreRemotePath.IsNotNullOrWhiteSpace()).WithMessage("Set both paths, or leave both blank");
            SharedValidator.RuleFor(c => c.CalibreRemotePath).NotEmpty().When(c => c.LightNovelEbookHome == LightNovelHome.Calibre && c.CalibreLocalPath.IsNotNullOrWhiteSpace()).WithMessage("Set both paths, or leave both blank");
            SharedValidator.RuleFor(c => c.AudiobookshelfLocalPath).NotEmpty().When(c => c.LightNovelAudioHome == LightNovelHome.Audiobookshelf && c.AudiobookshelfRemotePath.IsNotNullOrWhiteSpace()).WithMessage("Set both paths, or leave both blank");
            SharedValidator.RuleFor(c => c.AudiobookshelfRemotePath).NotEmpty().When(c => c.LightNovelAudioHome == LightNovelHome.Audiobookshelf && c.AudiobookshelfLocalPath.IsNotNullOrWhiteSpace()).WithMessage("Set both paths, or leave both blank");
        }

        [RestPutById]
        public override ActionResult<MediaManagementConfigResource> SaveConfig(MediaManagementConfigResource resource)
        {
            // The UI only ever sees the mask; echoed back unchanged it is nulled so the save skips the
            // field and the stored secret survives. A new value or an explicit clear ("") passes.
            resource.CalibrePassword = GoogleBooksService.SanitizeIncomingApiKey(resource.CalibrePassword);
            resource.AudiobookshelfApiKey = GoogleBooksService.SanitizeIncomingApiKey(resource.AudiobookshelfApiKey);

            return base.SaveConfig(resource);
        }

        protected override MediaManagementConfigResource ToResource(IConfigService model)
        {
            return MediaManagementConfigResourceMapper.ToResource(model);
        }
    }
}
