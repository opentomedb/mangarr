using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles
{
    [TestFixture]

    public class QualityProfileServiceFixture : CoreTest<QualityProfileService>
    {
        [Test]
        public void init_should_add_default_profiles()
        {
            Mocker.GetMock<ICustomFormatService>()
                .Setup(s => s.All())
                .Returns(new List<CustomFormat>());

            Subject.Handle(new ApplicationStartedEvent());

            // Manga + Light Novel EPUB + Light Novel Audio
            Mocker.GetMock<IProfileRepository>()
                .Verify(v => v.Insert(It.IsAny<QualityProfile>()), Times.Exactly(3));
        }

        [Test]
        public void init_should_skip_when_every_default_profile_already_exists()
        {
            // A user who deleted or renamed a default on purpose must not get it back.
            Mocker.GetMock<IProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<QualityProfile>
                  {
                      new QualityProfile { Id = 1, Name = "Manga" },
                      new QualityProfile { Id = 2, Name = "Light Novel EPUB" },
                      new QualityProfile { Id = 3, Name = "Light Novel Audio" }
                  });

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IProfileRepository>()
                .Verify(v => v.Insert(It.IsAny<QualityProfile>()), Times.Never());
        }

        [Test]
        public void should_add_the_light_novel_profiles_when_only_manga_exists()
        {
            Mocker.GetMock<ICustomFormatService>()
                .Setup(s => s.All())
                .Returns(new List<CustomFormat>());

            Mocker.GetMock<IProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<QualityProfile> { new QualityProfile { Id = 1, Name = "Manga" } });

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IProfileRepository>()
                  .Verify(v => v.Insert(It.Is<QualityProfile>(p => p.Name == "Light Novel EPUB" && p.Cutoff == Quality.EPUB.Id && p.UpgradeAllowed)), Times.Once());
            Mocker.GetMock<IProfileRepository>()
                  .Verify(v => v.Insert(It.Is<QualityProfile>(p => p.Name == "Light Novel Audio" && p.Cutoff == Quality.M4B.Id && p.UpgradeAllowed)), Times.Once());
            Mocker.GetMock<IProfileRepository>()
                  .Verify(v => v.Insert(It.Is<QualityProfile>(p => p.Name == "Manga")), Times.Never());
        }

        [Test]
        public void the_audio_profile_allows_exactly_the_audiobook_ladder()
        {
            Mocker.GetMock<ICustomFormatService>()
                .Setup(s => s.All())
                .Returns(new List<CustomFormat>());

            Mocker.GetMock<IProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<QualityProfile> { new QualityProfile { Id = 1, Name = "Manga" } });

            QualityProfile audio = null;
            Mocker.GetMock<IProfileRepository>()
                  .Setup(s => s.Insert(It.Is<QualityProfile>(p => p.Name == "Light Novel Audio")))
                  .Callback<QualityProfile>(p => audio = p);

            Subject.Handle(new ApplicationStartedEvent());

            audio.Should().NotBeNull();
            audio.Items.Where(i => i.Allowed).Select(i => i.Quality).Should().Equal(Quality.UnknownAudio, Quality.MP3, Quality.FLAC, Quality.M4B);
        }

        [Test]
        public void the_epub_profile_allows_azw3_below_epub()
        {
            // AZW3 (id 7) is a light-novel fallback: grabbed only when no EPUB release exists, so
            // it sits below EPUB (worst-first) and the cutoff stays EPUB.
            Mocker.GetMock<ICustomFormatService>()
                .Setup(s => s.All())
                .Returns(new List<CustomFormat>());

            Mocker.GetMock<IProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<QualityProfile> { new QualityProfile { Id = 1, Name = "Manga" } });

            QualityProfile epub = null;
            Mocker.GetMock<IProfileRepository>()
                  .Setup(s => s.Insert(It.Is<QualityProfile>(p => p.Name == "Light Novel EPUB")))
                  .Callback<QualityProfile>(p => epub = p);

            Subject.Handle(new ApplicationStartedEvent());

            epub.Should().NotBeNull();
            epub.Items.Where(i => i.Allowed).Select(i => i.Quality).Should().Equal(Quality.AZW3, Quality.EPUB);
            epub.Cutoff.Should().Be(Quality.EPUB.Id);
        }

        // LN PDF (2026-09-22): a fresh install's light-novel profile offers Ebook PDF UNTICKED,
        // directly below AZW3 -- a user who wants light-novel PDFs ticks it.
        [Test]
        public void the_epub_profile_offers_ebook_pdf_unticked_directly_below_azw3()
        {
            Mocker.GetMock<ICustomFormatService>()
                .Setup(s => s.All())
                .Returns(new List<CustomFormat>());

            Mocker.GetMock<IProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<QualityProfile> { new QualityProfile { Id = 1, Name = "Manga" } });

            QualityProfile epub = null;
            Mocker.GetMock<IProfileRepository>()
                  .Setup(s => s.Insert(It.Is<QualityProfile>(p => p.Name == "Light Novel EPUB")))
                  .Callback<QualityProfile>(p => epub = p);

            Subject.Handle(new ApplicationStartedEvent());

            epub.Should().NotBeNull();

            var qualities = epub.Items.Select(i => i.Quality).ToList();
            qualities.IndexOf(Quality.EbookPdf).Should().Be(qualities.IndexOf(Quality.AZW3) - 1);
            epub.Items.Single(i => i.Quality == Quality.EbookPdf).Allowed.Should().BeFalse();
            epub.Items.Where(i => i.Allowed).Select(i => i.Quality).Should().Equal(Quality.AZW3, Quality.EPUB);
            epub.Cutoff.Should().Be(Quality.EPUB.Id);
        }

        // Beta readiness (2026-09-28, F7): the light-novel profiles are seeded once. A start after that adds
        // nothing, even when one was deleted or renamed; the first seeding start records the flag.
        [Test]
        public void a_seeded_install_does_not_get_a_deleted_light_novel_profile_back()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.LightNovelProfilesSeeded).Returns(true);
            Mocker.GetMock<IProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<QualityProfile> { new QualityProfile { Id = 1, Name = "Manga" }, new QualityProfile { Id = 2, Name = "My EPUB" } });

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IProfileRepository>().Verify(v => v.Insert(It.IsAny<QualityProfile>()), Times.Never());
            Mocker.GetMock<IConfigService>().VerifySet(s => s.LightNovelProfilesSeeded = It.IsAny<bool>(), Times.Never());
        }

        [Test]
        public void the_first_seeding_start_records_the_flag()
        {
            Mocker.GetMock<IProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<QualityProfile>
                  {
                      new QualityProfile { Id = 1, Name = "Manga" },
                      new QualityProfile { Id = 2, Name = "Light Novel EPUB" },
                      new QualityProfile { Id = 3, Name = "Light Novel Audio" }
                  });

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IProfileRepository>().Verify(v => v.Insert(It.IsAny<QualityProfile>()), Times.Never());
            Mocker.GetMock<IConfigService>().VerifySet(s => s.LightNovelProfilesSeeded = true, Times.Once());
        }

        [Test]
        public void should_not_be_able_to_delete_profile_if_assigned_to_author()
        {
            var profile = Builder<QualityProfile>.CreateNew()
                                          .With(p => p.Id = 2)
                                          .Build();

            var authorList = Builder<Author>.CreateListOfSize(3)
                                            .Random(1)
                                            .With(c => c.QualityProfileId = profile.Id)
                                            .Build().ToList();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.ProfileId = 1)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .All()
                .With(f => f.DefaultQualityProfileId = 1)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.GetAllAuthors()).Returns(authorList);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<QualityProfileInUseException>(() => Subject.Delete(profile.Id));

            Mocker.GetMock<IProfileRepository>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_not_be_able_to_delete_profile_if_assigned_to_import_list()
        {
            var profile = Builder<QualityProfile>.CreateNew()
                .With(p => p.Id = 2)
                .Build();

            var authorList = Builder<Author>.CreateListOfSize(3)
                .All()
                .With(c => c.QualityProfileId = 1)
                .Build().ToList();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .Random(1)
                .With(c => c.ProfileId = profile.Id)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .All()
                .With(f => f.DefaultQualityProfileId = 1)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.GetAllAuthors()).Returns(authorList);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<QualityProfileInUseException>(() => Subject.Delete(profile.Id));

            Mocker.GetMock<IProfileRepository>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_not_be_able_to_delete_profile_if_assigned_to_root_folder()
        {
            var profile = Builder<QualityProfile>.CreateNew()
                .With(p => p.Id = 2)
                .Build();

            var authorList = Builder<Author>.CreateListOfSize(3)
                .All()
                .With(c => c.QualityProfileId = 1)
                .Build().ToList();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.ProfileId = 1)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .Random(1)
                .With(f => f.DefaultQualityProfileId = profile.Id)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.GetAllAuthors()).Returns(authorList);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<QualityProfileInUseException>(() => Subject.Delete(profile.Id));

            Mocker.GetMock<IProfileRepository>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_delete_profile_if_not_assigned_to_author_import_list_or_root_folder()
        {
            var authorList = Builder<Author>.CreateListOfSize(3)
                                            .All()
                                            .With(c => c.QualityProfileId = 2)
                                            .Build().ToList();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.ProfileId = 2)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .All()
                .With(f => f.DefaultQualityProfileId = 2)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.GetAllAuthors()).Returns(authorList);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);

            Subject.Delete(1);

            Mocker.GetMock<IProfileRepository>().Verify(c => c.Delete(1), Times.Once());
        }
    }
}
