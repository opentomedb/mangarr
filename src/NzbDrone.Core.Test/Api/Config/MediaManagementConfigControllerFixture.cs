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

        // .When() puts its condition on every validator of the rule, so the Recycle Bin and PDF Originals rules must
        // not share validator instances: each folder has to validate on its own (found on staging 2026-10-10, when a
        // bad folder saved without error unless both were set). Real validators, built the way the container does:
        // one fresh instance per constructor parameter.
        private TestableController RealController()
        {
            var disk = Mocker.GetMock<NzbDrone.Common.Disk.IDiskProvider>();
            disk.Setup(d => d.FolderExists(It.IsAny<string>())).Returns(false);
            disk.Setup(d => d.FolderWritable(It.IsAny<string>())).Returns(false);

            var authorsMock = Mocker.GetMock<NzbDrone.Core.Books.IAuthorService>();
            authorsMock.Setup(a => a.AllAuthorPaths()).Returns(new System.Collections.Generic.Dictionary<int, string>());
            var authors = authorsMock.Object;
            var rootsMock = Mocker.GetMock<NzbDrone.Core.RootFolders.IRootFolderService>();
            rootsMock.Setup(r => r.All()).Returns(new System.Collections.Generic.List<NzbDrone.Core.RootFolders.RootFolder>());
            var roots = rootsMock.Object;
            var appMock = Mocker.GetMock<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
            appMock.SetupGet(a => a.StartUpFolder).Returns("/app/bin");
            appMock.SetupGet(a => a.AppDataFolder).Returns("/config");
            var app = appMock.Object;

            return new TestableController(Mocker.GetMock<IConfigService>().Object,
                new PathExistsValidator(disk.Object), new FolderChmodValidator(disk.Object), new FolderWritableValidator(disk.Object),
                new AuthorPathValidator(authors), new StartupFolderValidator(app), new SystemFolderValidator(),
                new RootFolderAncestorValidator(roots), new RootFolderValidator(roots), new InsideRootFolderValidator(roots),
                new PathExistsValidator(disk.Object), new FolderWritableValidator(disk.Object),
                new AuthorPathValidator(authors), new StartupFolderValidator(app), new SystemFolderValidator(),
                new RootFolderAncestorValidator(roots), new RootFolderValidator(roots));
        }

        [Test]
        public void a_missing_pdf_originals_folder_is_refused_without_a_recycle_bin()
        {
            var resource = ValidResource();
            resource.PdfOriginalsFolder = "/does/not/exist";

            RealController().ValidateShared(resource).Errors.Should().Contain(e => e.PropertyName == "PdfOriginalsFolder");
        }

        [Test]
        public void a_missing_recycle_bin_is_refused_without_a_pdf_originals_folder()
        {
            var resource = ValidResource();
            resource.RecycleBin = "/does/not/exist";

            RealController().ValidateShared(resource).Errors.Should().Contain(e => e.PropertyName == "RecycleBin");
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
                                       RootFolderValidator rootFolderValidator,
                                       InsideRootFolderValidator insideRootFolderValidator,
                                       PathExistsValidator pdfPathExistsValidator,
                                       FolderWritableValidator pdfFolderWritableValidator,
                                       AuthorPathValidator pdfAuthorPathValidator,
                                       StartupFolderValidator pdfStartupFolderValidator,
                                       SystemFolderValidator pdfSystemFolderValidator,
                                       RootFolderAncestorValidator pdfRootFolderAncestorValidator,
                                       RootFolderValidator pdfRootFolderValidator)
                : base(configService, pathExistsValidator, folderChmodValidator, folderWritableValidator, authorPathValidator, startupFolderValidator, systemFolderValidator, rootFolderAncestorValidator, rootFolderValidator, insideRootFolderValidator, pdfPathExistsValidator, pdfFolderWritableValidator, pdfAuthorPathValidator, pdfStartupFolderValidator, pdfSystemFolderValidator, pdfRootFolderAncestorValidator, pdfRootFolderValidator)
            {
            }

            public ValidationResult ValidateShared(MediaManagementConfigResource resource)
            {
                return SharedValidator.Validate(resource);
            }
        }
    }
}
