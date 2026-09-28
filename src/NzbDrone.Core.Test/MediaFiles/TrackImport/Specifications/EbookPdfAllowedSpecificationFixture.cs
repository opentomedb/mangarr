using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    // LN PDF fix round 1 (2026-09-22): the profile opt-in has to hold at import time too -- the maintainer's
    // default profiles ship Ebook PDF (id 8) unticked, so nothing already graded Ebook PDF should
    // import into a light-novel entry whose profile doesn't want it.
    [TestFixture]
    public class EbookPdfAllowedSpecificationFixture : CoreTest<EbookPdfAllowedSpecification>
    {
        private static Author LightNovelAuthor(bool ebookPdfAllowed)
        {
            return new Author
            {
                Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord~ln" },
                QualityProfile = new QualityProfile
                {
                    Items = new List<QualityProfileQualityItem> { new QualityProfileQualityItem { Quality = Quality.EbookPdf, Allowed = ebookPdfAllowed } }
                }
            };
        }

        private static LocalBook Local(string path, Quality quality, Author author, bool existingFile = false)
        {
            return new LocalBook
            {
                Path = path,
                Quality = new QualityModel(quality),
                Author = author,
                ExistingFile = existingFile
            };
        }

        [Test]
        public void ebook_pdf_is_accepted_when_the_profile_allows_it()
        {
            var local = Local("/lightnovels/Overlord/Overlord - Vol 005.pdf", Quality.EbookPdf, LightNovelAuthor(true));

            Subject.IsSatisfiedBy(local, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void ebook_pdf_is_rejected_when_the_profile_does_not_allow_it()
        {
            var local = Local("/lightnovels/Overlord/Overlord - Vol 005.pdf", Quality.EbookPdf, LightNovelAuthor(false));

            Subject.IsSatisfiedBy(local, null).Accepted.Should().BeFalse();
        }

        // A manga PDF is never graded Ebook PDF, so it never reaches this gate -- unaffected by the
        // light-novel profile's Ebook PDF item either way.
        [Test]
        public void a_manga_pdf_is_unaffected()
        {
            var local = Local("/manga/Dandadan/Dandadan - Vol 001.pdf", Quality.PDF, LightNovelAuthor(false));

            Subject.IsSatisfiedBy(local, null).Accepted.Should().BeTrue();
        }

        // Fix round 1 (2026-09-22): a rescan reads the same per-file spec the download-import path
        // does (ImportDecisionMaker's _trackSpecifications), so an existing entry-folder .pdf already
        // graded Ebook PDF is rejected the same way once the profile stops wanting it.
        [Test]
        public void a_rescanned_existing_file_is_rejected_the_same_way()
        {
            var local = Local("/lightnovels/Overlord/Overlord - Vol 005.pdf", Quality.EbookPdf, LightNovelAuthor(false), existingFile: true);

            Subject.IsSatisfiedBy(local, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void a_non_ebook_pdf_quality_is_unaffected_by_a_missing_profile()
        {
            var author = new Author { Metadata = new AuthorMetadata { Name = "Overlord", ForeignAuthorId = "local-overlord~ln" } };
            var local = Local("/lightnovels/Overlord/Overlord - Vol 005.epub", Quality.EPUB, author);

            Subject.IsSatisfiedBy(local, null).Accepted.Should().BeTrue();
        }
    }
}
