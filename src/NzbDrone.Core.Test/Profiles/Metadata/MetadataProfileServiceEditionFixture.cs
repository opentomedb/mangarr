using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles.Metadata
{
    // Preferred Edition (2026-09-24, spec §1 "Trap to fix"): the seeded Standard profile allows "eng, null";
    // a French series' "fra" edition must not be filtered out ("all editions filtered out") -- the
    // series' own edition language is allowed too. An English series is unchanged.
    [TestFixture]
    public class MetadataProfileServiceEditionFixture : CoreTest<MetadataProfileService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IMetadataProfileRepository>().Setup(r => r.Get(1)).Returns(new MetadataProfile { Id = 1, AllowedLanguages = "eng, null", Ignored = new List<string>() });
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindById(It.IsAny<string>())).Returns((Author)null);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByAuthor(It.IsAny<int>())).Returns(new List<BookFile>());
        }

        private static Author SeriesWithEdition(string editionLanguage, string editionIso3)
        {
            var edition = new Edition { ForeignEditionId = "local-attack-on-titan-v1-ed", Title = "x Tome 1", Language = editionIso3, PageCount = 0, Isbn13 = "9782811611699" };
            var book = new Book { ForeignBookId = "local-attack-on-titan-v1", Title = "x Tome 1", Editions = new List<Edition> { edition }, ReleaseDate = new System.DateTime(2013, 6, 5), Ratings = new Ratings { Votes = 1000, Value = 4 } };

            return new Author
            {
                Metadata = new AuthorMetadata { ForeignAuthorId = "local-attack-on-titan", Name = "x", EditionLanguage = editionLanguage },
                Books = new List<Book> { book },
                Series = new List<Series>()
            };
        }

        // Final fix round Minor 2 (2026-09-24): a French light novel volume -- Ebook "fra" + Audio "eng" (the
        // English audiobook, D8).
        private static Author FrenchNovel()
        {
            var ebook = new Edition { ForeignEditionId = "local-sword-art-online~ln-v1-ed", Title = "x Tome 1", Language = "fra", MediaType = MediaType.Ebook, Isbn13 = "9782811611699" };
            var audio = new Edition { ForeignEditionId = "local-sword-art-online~ln-v1-audio-ed", Title = "x Tome 1", Language = "eng", MediaType = MediaType.Audio, Isbn13 = "9782811611699" };
            var book = new Book { ForeignBookId = "local-sword-art-online~ln-v1", Title = "x Tome 1", Editions = new List<Edition> { ebook, audio }, ReleaseDate = new System.DateTime(2013, 6, 5), Ratings = new Ratings { Votes = 1000, Value = 4 } };

            return new Author
            {
                Metadata = new AuthorMetadata { ForeignAuthorId = "local-sword-art-online~ln", Name = "x", EditionLanguage = "fr" },
                Books = new List<Book> { book },
                Series = new List<Series>()
            };
        }

        [TestCase("eng, null")]
        [TestCase("fra")]
        [TestCase("")]
        public void a_french_light_novels_english_audio_edition_passes_the_profile(string allowed)
        {
            Mocker.GetMock<IMetadataProfileRepository>().Setup(r => r.Get(1)).Returns(new MetadataProfile { Id = 1, AllowedLanguages = allowed, Ignored = new List<string>() });

            var book = Subject.FilterBooks(FrenchNovel(), 1).Single();

            book.Editions.Value.Select(e => e.MediaType).Should().BeEquivalentTo(new[] { MediaType.Ebook, MediaType.Audio });
        }

        // An English series gains nothing from it: its audio edition was "eng" already, and a profile
        // without English still drops it as before.
        [Test]
        public void an_english_series_audio_edition_is_filtered_as_before()
        {
            Mocker.GetMock<IMetadataProfileRepository>().Setup(r => r.Get(1)).Returns(new MetadataProfile { Id = 1, AllowedLanguages = "fra", Ignored = new List<string>() });
            var author = FrenchNovel();
            author.Metadata.Value.EditionLanguage = null;

            Subject.FilterBooks(author, 1).Single().Editions.Value.Select(e => e.MediaType).Should().BeEquivalentTo(new[] { MediaType.Ebook });
        }

        [Test]
        public void a_french_series_keeps_its_french_edition()
        {
            Subject.FilterBooks(SeriesWithEdition("fr", "fra"), 1).Should().HaveCount(1);
        }

        [Test]
        public void an_english_series_still_drops_a_french_edition()
        {
            Subject.FilterBooks(SeriesWithEdition(null, "fra"), 1).Should().BeEmpty();
        }

        [Test]
        public void an_english_series_keeps_its_english_edition()
        {
            Subject.FilterBooks(SeriesWithEdition(null, "eng"), 1).Should().HaveCount(1);
        }

        // Follow-up round (KR/CN consumer, I1 residue): a fallback series is English-style for the profile -- its
        // editions are minted "eng" (BookInfoProxy) and pass the Standard profile as an English series' do; its
        // bound line's language earns no exemption. The same binding as a Japanese edition keeps its exemption.
        private static Author JapaneseSeries(bool fallback, string editionIso3)
        {
            var author = SeriesWithEdition("ja", editionIso3);
            author.Metadata.Value.EditionFallback = fallback;

            return author;
        }

        [Test]
        public void a_fallback_series_keeps_its_english_edition()
        {
            Subject.FilterBooks(JapaneseSeries(true, "eng"), 1).Should().HaveCount(1);
        }

        [Test]
        public void a_fallback_series_is_filtered_like_an_english_series()
        {
            Subject.FilterBooks(JapaneseSeries(true, "jpn"), 1).Should().BeEmpty();
        }

        [Test]
        public void the_same_binding_as_an_edition_keeps_its_japanese_edition()
        {
            Subject.FilterBooks(JapaneseSeries(false, "jpn"), 1).Should().HaveCount(1);
        }
    }
}
