using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    // Preferred Edition (2026-09-24): the Add form's edition reaches the resolve (AddOptions carries it);
    // no edition keeps today's three-argument call.
    [TestFixture]
    public class AddAuthorEditionFixture : CoreTest<AddAuthorService>
    {
        private Author _resolved;

        [SetUp]
        public void Setup()
        {
            _resolved = Builder<Author>.CreateNew().With(s => s.Path = null).Build();
            _resolved.Books = new List<Book>();

            Mocker.GetMock<IAuthorService>().Setup(s => s.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>())).Returns<Author, bool>((a, _) => a);
            Mocker.GetMock<IBuildFileNames>().Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null)).Returns<Author, NamingConfig>((c, n) => c.Name);
            Mocker.GetMock<IAddAuthorValidator>().Setup(s => s.Validate(It.IsAny<Author>())).Returns(new ValidationResult());
            Mocker.GetMock<IProvideAuthorInfo>().Setup(s => s.GetAuthorInfo("local-attack-on-titan", false, It.IsAny<bool>())).Returns(_resolved);
            Mocker.GetMock<IProvideAuthorInfo>().Setup(s => s.GetAuthorInfo("local-attack-on-titan", false, false, "fr")).Returns(_resolved);
            Mocker.GetMock<IProvideAuthorInfo>().Setup(s => s.GetAuthorInfo("local-attack-on-titan", false, false, "fr", "rl_x")).Returns(_resolved);
        }

        private static Author NewAuthor(string edition)
        {
            return new Author
            {
                ForeignAuthorId = "local-attack-on-titan",
                RootFolderPath = "/manga",
                AddOptions = new AddAuthorOptions { EditionLanguage = edition }
            };
        }

        [Test]
        public void an_explicit_edition_reaches_the_resolve()
        {
            Subject.AddAuthor(NewAuthor("fr"));

            Mocker.GetMock<IProvideAuthorInfo>().Verify(s => s.GetAuthorInfo("local-attack-on-titan", false, false, "fr"), Times.Once());
            Mocker.GetMock<IProvideAuthorInfo>().Verify(s => s.GetAuthorInfo(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void no_edition_keeps_the_three_argument_call()
        {
            Subject.AddAuthor(NewAuthor(null));

            Mocker.GetMock<IProvideAuthorInfo>().Verify(s => s.GetAuthorInfo("local-attack-on-titan", false, It.IsAny<bool>()), Times.Once());
        }

        // Staging fix S1 (2026-10-01, spec §3.2): the fallback line the search chose rides in the posted metadata.
        private static Author FallbackAuthor(string addEdition, bool fallback, string tomeLineId, string language)
        {
            var author = NewAuthor(addEdition);
            author.Metadata = new AuthorMetadata { ForeignAuthorId = "local-attack-on-titan", EditionFallback = fallback, TomeLineId = tomeLineId, EditionLanguage = language };

            return author;
        }

        [Test]
        public void a_fallback_candidate_carries_its_line_into_the_resolve()
        {
            Subject.AddAuthor(FallbackAuthor(null, true, "rl_x", "fr"));

            Mocker.GetMock<IProvideAuthorInfo>().Verify(s => s.GetAuthorInfo("local-attack-on-titan", false, false, "fr", "rl_x"), Times.Once());
            Mocker.GetMock<IProvideAuthorInfo>().Verify(s => s.GetAuthorInfo(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [TestCase(false, "rl_x", "fr")]
        [TestCase(true, "", "fr")]
        [TestCase(true, "rl_x", null)]
        public void an_add_without_a_complete_fallback_binding_keeps_the_three_argument_call(bool fallback, string tomeLineId, string language)
        {
            Subject.AddAuthor(FallbackAuthor(null, fallback, tomeLineId, language));

            Mocker.GetMock<IProvideAuthorInfo>().Verify(s => s.GetAuthorInfo("local-attack-on-titan", false, It.IsAny<bool>()), Times.Once());
            Mocker.GetMock<IProvideAuthorInfo>().Verify(s => s.GetAuthorInfo(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void an_explicit_edition_wins_over_a_carried_fallback_line()
        {
            Subject.AddAuthor(FallbackAuthor("fr", true, "rl_x", "fr"));

            Mocker.GetMock<IProvideAuthorInfo>().Verify(s => s.GetAuthorInfo("local-attack-on-titan", false, false, "fr"), Times.Once());
            Mocker.GetMock<IProvideAuthorInfo>().Verify(s => s.GetAuthorInfo(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void an_edition_the_catalogue_lacks_is_a_validation_message()
        {
            Mocker.GetMock<IProvideAuthorInfo>().Setup(s => s.GetAuthorInfo("local-attack-on-titan", false, false, "fr"))
                  .Throws(new EditionUnavailableException("Attack On Titan", "fr"));

            var ex = Assert.Throws<ValidationException>(() => Subject.AddAuthor(NewAuthor("fr")));

            ex.Message.Should().Contain("No French edition of this series in the catalogue");
        }

        [Test]
        public void a_light_novel_with_no_line_in_any_chain_language_says_so()
        {
            Mocker.GetMock<IProvideAuthorInfo>().Setup(s => s.GetAuthorInfo("local-attack-on-titan", false, It.IsAny<bool>()))
                  .Throws(new NotInCatalogueException("Attack On Titan", true));

            var ex = Assert.Throws<ValidationException>(() => Subject.AddAuthor(NewAuthor(null)));

            ex.Errors.Should().Contain(e => e.ErrorMessage == "No novel line in any chain language");
        }

        [Test]
        public void an_english_light_novel_refusal_keeps_its_message()
        {
            Mocker.GetMock<IProvideAuthorInfo>().Setup(s => s.GetAuthorInfo("local-attack-on-titan", false, It.IsAny<bool>()))
                  .Throws(new NotInCatalogueException("Attack On Titan"));

            var ex = Assert.Throws<ValidationException>(() => Subject.AddAuthor(NewAuthor(null)));

            ex.Errors.Should().Contain(e => e.ErrorMessage == "Not in the catalogue as a light novel yet");
        }
    }
}
