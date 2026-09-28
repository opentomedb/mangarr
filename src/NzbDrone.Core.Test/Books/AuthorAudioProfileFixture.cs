using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class AuthorAudioProfileFixture
    {
        private static readonly QualityProfile Epub = new QualityProfile { Id = 2, Name = "Light Novel EPUB" };
        private static readonly QualityProfile Audio = new QualityProfile { Id = 3, Name = "Light Novel Audio" };

        private static Author LightNovel(bool withAudioProfile)
        {
            var author = new Author
            {
                QualityProfileId = Epub.Id,
                QualityProfile = Epub
            };

            if (withAudioProfile)
            {
                author.AudioQualityProfileId = Audio.Id;
                author.AudioQualityProfile = Audio;
            }

            return author;
        }

        [Test]
        public void audio_asks_the_audio_profile_and_everything_else_the_main_one()
        {
            var author = LightNovel(true);

            author.QualityProfileFor(MediaType.Ebook).Should().BeSameAs(Epub);
            author.QualityProfileFor(MediaType.Archive).Should().BeSameAs(Epub);
            author.QualityProfileFor(MediaType.Audio).Should().BeSameAs(Audio);

            author.QualityProfileIdFor(MediaType.Ebook).Should().Be(2);
            author.QualityProfileIdFor(MediaType.Audio).Should().Be(3);
        }

        [Test]
        public void audio_falls_back_to_the_main_profile_when_no_audio_profile_is_set()
        {
            var author = LightNovel(false);

            author.QualityProfileFor(MediaType.Audio).Should().BeSameAs(Epub);
            author.QualityProfileIdFor(MediaType.Audio).Should().Be(2);
        }

        [Test]
        public void a_zero_audio_profile_id_means_unset_for_both_lookups()
        {
            // 0 is what the validator admits as "unset" and what the mapping treats as no profile.
            var author = LightNovel(false);
            author.AudioQualityProfileId = 0;

            author.QualityProfileFor(MediaType.Audio).Should().BeSameAs(Epub);
            author.QualityProfileIdFor(MediaType.Audio).Should().Be(2);
        }

        [Test]
        public void a_manga_entry_always_gets_its_one_profile()
        {
            var manga = new Author { QualityProfileId = 1, QualityProfile = new QualityProfile { Id = 1, Name = "Manga" } };

            manga.QualityProfileFor(MediaType.Archive).Id.Should().Be(1);
            manga.QualityProfileFor(MediaType.Audio).Id.Should().Be(1);
            manga.QualityProfileIdFor(MediaType.Audio).Should().Be(1);
        }

        [Test]
        public void apply_changes_carries_the_audio_settings_from_the_api()
        {
            var stored = LightNovel(false);
            var edited = LightNovel(true);
            edited.AudioAvailable = true;

            stored.ApplyChanges(edited);

            stored.AudioQualityProfileId.Should().Be(3);
            stored.AudioQualityProfile.Value.Should().BeSameAs(Audio);
            stored.AudioAvailable.Should().BeTrue();
        }

        [Test]
        public void use_db_fields_from_keeps_the_stored_audio_settings_across_a_refresh()
        {
            var remote = LightNovel(false);
            var local = LightNovel(true);
            local.AudioAvailable = true;
            local.LastAudioSearch = new DateTime(2026, 9, 7, 1, 0, 0, DateTimeKind.Utc);

            remote.UseDbFieldsFrom(local);

            remote.AudioQualityProfileId.Should().Be(3);
            remote.AudioQualityProfile.Value.Should().BeSameAs(Audio);
            remote.AudioAvailable.Should().BeTrue();
            remote.LastAudioSearch.Should().Be(local.LastAudioSearch);
        }
    }
}
