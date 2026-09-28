using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.AuthorRepositoryTests
{
    [TestFixture]

    public class AuthorRepositoryFixture : DbTest<AuthorRepository, Author>
    {
        private AuthorRepository _authorRepo;
        private AuthorMetadataRepository _authorMetadataRepo;

        [SetUp]
        public void Setup()
        {
            _authorRepo = Mocker.Resolve<AuthorRepository>();
            _authorMetadataRepo = Mocker.Resolve<AuthorMetadataRepository>();
        }

        private void AddAuthor(string name, string foreignId, List<string> oldIds = null)
        {
            if (oldIds == null)
            {
                oldIds = new List<string>();
            }

            var metadata = Builder<AuthorMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.Name = name)
                .With(a => a.TitleSlug = foreignId)
                .BuildNew();

            var author = Builder<Author>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.Metadata = metadata)
                .With(a => a.CleanName = Parser.Parser.CleanAuthorName(name))
                .With(a => a.ForeignAuthorId = foreignId)

                // NBuilder fills nullable scalars too; a freshly added series has no audio settings.
                .With(a => a.AudioQualityProfileId = null)
                .With(a => a.AudioAvailable = false)
                .With(a => a.LastAudioSearch = null)
                .BuildNew();

            _authorMetadataRepo.Insert(metadata);
            author.AuthorMetadataId = metadata.Id;
            _authorRepo.Insert(author);
        }

        private void GivenAuthors()
        {
            AddAuthor("The Black Eyed Peas", "d5be5333-4171-427e-8e12-732087c6b78e");
            AddAuthor("The Black Keys", "d15721d8-56b4-453d-b506-fc915b14cba2", new List<string> { "6f2ed437-825c-4cea-bb58-bf7688c6317a" });
        }

        [Test]
        public void should_lazyload_profiles()
        {
            var profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(Quality.FLAC, Quality.MP3, Quality.MP3),

                Cutoff = Quality.FLAC.Id,
                Name = "TestProfile"
            };

            var metaProfile = new MetadataProfile
            {
                Name = "TestProfile"
            };

            Mocker.Resolve<QualityProfileRepository>().Insert(profile);
            Mocker.Resolve<MetadataProfileRepository>().Insert(metaProfile);

            var author = Builder<Author>.CreateNew().BuildNew();
            author.QualityProfileId = profile.Id;
            author.MetadataProfileId = metaProfile.Id;

            Subject.Insert(author);

            StoredModel.QualityProfile.Should().NotBeNull();
            StoredModel.MetadataProfile.Should().NotBeNull();
        }

        [TestCase("The Black Eyed Peas")]
        [TestCase("The Black Keys")]
        public void should_find_author_in_db_by_name(string name)
        {
            GivenAuthors();
            var author = _authorRepo.FindByName(Parser.Parser.CleanAuthorName(name));

            author.Should().NotBeNull();
            author.Name.Should().Be(name);
        }

        [Test]
        public void should_find_author_in_by_id()
        {
            GivenAuthors();
            var author = _authorRepo.FindById("d5be5333-4171-427e-8e12-732087c6b78e");

            author.Should().NotBeNull();
            author.ForeignAuthorId.Should().Be("d5be5333-4171-427e-8e12-732087c6b78e");
        }

        // Beta readiness (2026-09-28, S1): the fork's rule. Upstream let two authors share a CleanName and then
        // found neither by name; migration 046 made CleanName UNIQUE, so the second insert is refused and
        // the name keeps finding the one series.
        [Test]
        public void should_keep_one_author_per_clean_name()
        {
            GivenAuthors();

            var name = "Alice Cooper";
            AddAuthor(name, "ee58c59f-8e7f-4430-b8ca-236c4d3745ae");

            Action second = () => AddAuthor(name, "4d7928cd-7ed2-4282-8c29-c0c9f966f1bd");
            second.Should().Throw<Exception>();

            _authorRepo.All().Should().HaveCount(3);

            var author = _authorRepo.FindByName(Parser.Parser.CleanAuthorName(name));
            author.Should().NotBeNull();
            author.ForeignAuthorId.Should().Be("ee58c59f-8e7f-4430-b8ca-236c4d3745ae");
        }

        [Test]
        public void should_throw_sql_exception_adding_duplicate_author()
        {
            var name = "test";
            var metadata = Builder<AuthorMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.Name = name)
                .BuildNew();

            var author1 = Builder<Author>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.Metadata = metadata)
                .With(a => a.CleanName = Parser.Parser.CleanAuthorName(name))
                .BuildNew();

            var author2 = author1.JsonClone();
            author2.Metadata = metadata;

            _authorMetadataRepo.Insert(metadata);
            _authorRepo.Insert(author1);

            Action insertDupe = () => _authorRepo.Insert(author2);
            if (Db.DatabaseType == DatabaseType.PostgreSQL)
            {
                insertDupe.Should().Throw<PostgresException>();
            }
            else
            {
                insertDupe.Should().Throw<SQLiteException>();
            }
        }

        [Test]
        public void should_persist_the_audio_settings_and_lazy_load_the_audio_profile()
        {
            var audioProfile = new QualityProfile
            {
                Name = "Light Novel Audio",
                Cutoff = Quality.M4B.Id,
                Items = NzbDrone.Core.Test.Qualities.QualityFixture.GetDefaultQualities(Quality.MP3, Quality.FLAC)
            };
            Db.Insert(audioProfile);

            AddAuthor("Mushoku Tensei", "local-mushoku-tensei~ln");
            var stored = _authorRepo.All().Single();
            stored.AudioQualityProfileId.Should().BeNull();
            stored.AudioQualityProfile.Value.Should().BeNull();
            stored.AudioAvailable.Should().BeFalse();
            stored.LastAudioSearch.Should().BeNull();

            stored.AudioQualityProfileId = audioProfile.Id;
            stored.AudioAvailable = true;
            stored.LastAudioSearch = new DateTime(2026, 9, 7, 1, 0, 0, DateTimeKind.Utc);
            _authorRepo.Update(stored);

            var reloaded = _authorRepo.All().Single();
            reloaded.AudioQualityProfileId.Should().Be(audioProfile.Id);
            reloaded.AudioQualityProfile.Value.Name.Should().Be("Light Novel Audio");
            reloaded.AudioAvailable.Should().BeTrue();
            reloaded.LastAudioSearch.Should().Be(stored.LastAudioSearch);
            reloaded.Library.Should().Be(LibraryType.LightNovel);
        }
    }
}
