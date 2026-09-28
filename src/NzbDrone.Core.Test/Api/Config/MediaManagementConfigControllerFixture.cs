using System.Collections.Generic;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using Readarr.Api.V1.Config;

namespace NzbDrone.Core.Test.Api.Config
{
    // Light-novel storage (2026-09-22, fix round 1): the media-management home/URL/path validators
    // sit in the controller constructor, and Readarr.Api.V1 has no NUnit project of its own, so this
    // fixture reaches the controller through a project reference from Readarr.Core.Test (see the
    // .csproj) and a test-only subclass that exposes the protected SharedValidator.
    [TestFixture]
    public class MediaManagementConfigControllerFixture : CoreTest<MediaManagementConfigControllerFixture.TestableController>
    {
        private static MediaManagementConfigResource ValidResource()
        {
            // Values every unconditional SharedValidator rule accepts, so only the light-novel
            // storage rules under test can fail.
            return new MediaManagementConfigResource
            {
                PreferredLightNovelFormat = "epub",
                RecycleBinCleanupDays = 0,
                MinimumFreeSpaceWhenImporting = 100
            };
        }

        // A stale tab or an external client that never learned the new fields sends a PUT with both
        // homes absent; that must validate (not 400) and must not touch the stored homes.
        [Test]
        public void saving_without_the_new_light_novel_home_fields_does_not_reject_or_change_the_stored_homes()
        {
            var resource = ValidResource();
            resource.LightNovelEbookHome = null;
            resource.LightNovelAudioHome = null;

            Subject.ValidateShared(resource).IsValid.Should().BeTrue();

            Subject.SaveConfig(resource);

            Mocker.GetMock<IConfigService>()
                  .Verify(c => c.SaveConfigDictionary(It.Is<Dictionary<string, object>>(d =>
                      d["LightNovelEbookHome"] == null && d["LightNovelAudioHome"] == null)));
        }

        // With ebooks staying in the entry folder, a half-filled calibre path pair and an invalid
        // calibre URL are calibre's problem, not entry's -- none of those rules should fire.
        [Test]
        public void an_entry_ebook_home_skips_the_calibre_url_and_path_pair_rules()
        {
            var resource = ValidResource();
            resource.LightNovelEbookHome = LightNovelHome.Entry;
            resource.LightNovelAudioHome = LightNovelHome.Entry;
            resource.CalibreContentServerUrl = "not a url";
            resource.CalibreRemotePath = "/only/one/side/";
            resource.CalibreLocalPath = null;

            Subject.ValidateShared(resource).IsValid.Should().BeTrue();
        }

        // Same shape, the Audiobookshelf half.
        [Test]
        public void an_entry_audio_home_skips_the_audiobookshelf_url_and_path_pair_rules()
        {
            var resource = ValidResource();
            resource.LightNovelEbookHome = LightNovelHome.Entry;
            resource.LightNovelAudioHome = LightNovelHome.Entry;
            resource.AudiobookshelfUrl = "not a url";
            resource.AudiobookshelfRemotePath = "/only/one/side/";
            resource.AudiobookshelfLocalPath = null;

            Subject.ValidateShared(resource).IsValid.Should().BeTrue();
        }

        public class TestableController : MediaManagementConfigController
        {
            public TestableController(IConfigService configService,
                                       PathExistsValidator pathExistsValidator,
                                       FolderChmodValidator folderChmodValidator,
                                       FolderWritableValidator folderWritableValidator,
                                       AuthorPathValidator authorPathValidator,
                                       StartupFolderValidator startupFolderValidator,
                                       SystemFolderValidator systemFolderValidator,
                                       RootFolderAncestorValidator rootFolderAncestorValidator,
                                       RootFolderValidator rootFolderValidator)
                : base(configService, pathExistsValidator, folderChmodValidator, folderWritableValidator, authorPathValidator, startupFolderValidator, systemFolderValidator, rootFolderAncestorValidator, rootFolderValidator)
            {
            }

            public ValidationResult ValidateShared(MediaManagementConfigResource resource)
            {
                return SharedValidator.Validate(resource);
            }
        }
    }
}
