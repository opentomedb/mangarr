using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.EditionRepositoryTests
{
    [TestFixture]
    public class EditionRepositoryFixture : DbTest<EditionRepository, Edition>
    {
        private Book _volume;
        private Edition _ebook;
        private Edition _audio;
        private Edition _secondAudio;

        [SetUp]
        public void Setup()
        {
            var meta = Builder<AuthorMetadata>.CreateNew().With(a => a.Id = 0).Build();
            Db.Insert(meta);

            var author = Builder<Author>.CreateNew().With(a => a.Id = 0).With(a => a.AuthorMetadataId = meta.Id).Build();
            Db.Insert(author);

            _volume = Builder<Book>.CreateNew().With(b => b.Id = 0).With(b => b.AuthorMetadataId = meta.Id).Build();
            Db.Insert(_volume);

            _ebook = Insert("local-x~ln-v1-ed", MediaType.Ebook, true);
            _audio = Insert("local-x~ln-v1-audio-ed", MediaType.Audio, true);
            _secondAudio = Insert("local-x~ln-v1-audio-ed-2", MediaType.Audio, false);
        }

        private Edition Insert(string foreignId, MediaType mediaType, bool monitored, int? bookId = null)
        {
            var edition = Builder<Edition>.CreateNew()
                .With(e => e.Id = 0)
                .With(e => e.BookId = bookId ?? _volume.Id)
                .With(e => e.ForeignEditionId = foreignId)
                .With(e => e.TitleSlug = foreignId)
                .With(e => e.MediaType = mediaType)
                .With(e => e.Monitored = monitored)
                .Build();
            Db.Insert(edition);
            return edition;
        }

        private Dictionary<string, bool> Monitored()
        {
            return Db.All<Edition>().ToDictionary(e => e.ForeignEditionId, e => e.Monitored);
        }

        [Test]
        public void set_monitored_unmonitors_only_the_same_media_type_siblings()
        {
            Subject.SetMonitored(_secondAudio);

            var monitored = Monitored();
            monitored[_ebook.ForeignEditionId].Should().BeTrue();
            monitored[_audio.ForeignEditionId].Should().BeFalse();
            monitored[_secondAudio.ForeignEditionId].Should().BeTrue();
        }

        [Test]
        public void set_monitored_keeps_the_other_media_type_monitored()
        {
            Subject.SetMonitored(_ebook);

            var monitored = Monitored();
            monitored[_ebook.ForeignEditionId].Should().BeTrue();
            monitored[_audio.ForeignEditionId].Should().BeTrue();
            monitored[_secondAudio.ForeignEditionId].Should().BeFalse();
        }

        [Test]
        public void set_monitored_returns_every_edition_of_the_book()
        {
            Subject.SetMonitored(_audio).Select(e => e.ForeignEditionId).Should().BeEquivalentTo(_ebook.ForeignEditionId, _audio.ForeignEditionId, _secondAudio.ForeignEditionId);
        }

        [Test]
        public void a_manga_volume_with_one_edition_behaves_as_before()
        {
            var manga = Builder<Book>.CreateNew().With(b => b.Id = 0).With(b => b.AuthorMetadataId = _volume.AuthorMetadataId).With(b => b.ForeignBookId = "local-m-v1").With(b => b.TitleSlug = "local-m-v1").Build();
            Db.Insert(manga);
            var only = Insert("local-m-v1-ed", MediaType.Archive, false, manga.Id);

            var result = Subject.SetMonitored(only);

            result.Should().HaveCount(1);
            result.Single().Monitored.Should().BeTrue();
            Monitored()[only.ForeignEditionId].Should().BeTrue();
        }

        // Covered volumes (2026-09-17, D4a): only this author's Audio editions whose mark points at
        // the covering volume — not its ebook, not a mark by another volume, not another author's.
        [Test]
        public void get_covered_by_returns_the_authors_audio_editions_marked_by_that_volume()
        {
            var vol2 = Builder<Book>.CreateNew().With(b => b.Id = 0).With(b => b.AuthorMetadataId = _volume.AuthorMetadataId).With(b => b.ForeignBookId = "local-x~ln-v2").With(b => b.TitleSlug = "local-x~ln-v2").Build();
            Db.Insert(vol2);
            var covered = Mark(Insert("local-x~ln-v2-audio-ed", MediaType.Audio, true, vol2.Id), 7, CoveredSources.Audible);
            var coveredEbook = Mark(Insert("local-x~ln-v2-ed", MediaType.Ebook, true, vol2.Id), 7, CoveredSources.Import);
            var byAnotherVolume = Mark(Insert("local-x~ln-v3-audio-ed", MediaType.Audio, true, vol2.Id), 8, CoveredSources.Import);
            var unmarked = Mark(Insert("local-x~ln-v4-audio-ed", MediaType.Audio, true, vol2.Id), null, null);

            var otherMeta = Builder<AuthorMetadata>.CreateNew().With(a => a.Id = 0).With(a => a.ForeignAuthorId = "local-y~ln").With(a => a.TitleSlug = "local-y~ln").Build();
            Db.Insert(otherMeta);
            var otherVolume = Builder<Book>.CreateNew().With(b => b.Id = 0).With(b => b.AuthorMetadataId = otherMeta.Id).With(b => b.ForeignBookId = "local-y~ln-v2").With(b => b.TitleSlug = "local-y~ln-v2").Build();
            Db.Insert(otherVolume);
            var otherAuthors = Mark(Insert("local-y~ln-v2-audio-ed", MediaType.Audio, true, otherVolume.Id), 7, CoveredSources.Import);

            var result = Subject.GetCoveredBy(_volume.AuthorMetadataId, 7);

            result.Select(e => e.Id).Should().Equal(covered.Id);
            result.Single().CoveredSource.Should().Be(CoveredSources.Audible);
        }

        private Edition Mark(Edition edition, double? coveredByVolume, string coveredSource)
        {
            edition.CoveredByVolume = coveredByVolume;
            edition.CoveredSource = coveredSource;
            Db.Update(edition);
            return edition;
        }
    }
}
