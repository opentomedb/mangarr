using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class LibraryTypeFixture
    {
        [TestCase(null, LibraryType.Manga)]
        [TestCase("", LibraryType.Manga)]
        [TestCase("local-chainsaw-man", LibraryType.Manga)]
        [TestCase("local-mushoku-tensei~ln", LibraryType.LightNovel)]
        [TestCase("local-mushoku-tensei~ln-v5", LibraryType.LightNovel)]
        [TestCase("local-mushoku-tensei~ln-v5-audio-ed", LibraryType.LightNovel)]
        public void parses_the_library_out_of_any_foreign_id(string id, LibraryType expected)
        {
            LibraryTypes.Parse(id).Should().Be(expected);
        }

        [Test]
        public void suffix_is_empty_for_manga_and_ln_for_light_novels()
        {
            LibraryTypes.Suffix(LibraryType.Manga).Should().Be(string.Empty);
            LibraryTypes.Suffix(LibraryType.LightNovel).Should().Be("~ln");
        }

        [Test]
        public void with_type_replaces_any_existing_suffix()
        {
            LibraryTypes.WithType("local-x", LibraryType.LightNovel).Should().Be("local-x~ln");
            LibraryTypes.WithType("local-x~ln", LibraryType.LightNovel).Should().Be("local-x~ln");
            LibraryTypes.WithType("local-x~ln", LibraryType.Manga).Should().Be("local-x");
            LibraryTypes.WithType("local-x", LibraryType.Manga).Should().Be("local-x");
        }

        [Test]
        public void base_id_strips_the_suffix_wherever_it_sits()
        {
            LibraryTypes.BaseId("local-x~ln-v005").Should().Be("local-x-v005");
            LibraryTypes.BaseId("local-x~ln").Should().Be("local-x");
            LibraryTypes.BaseId("local-x").Should().Be("local-x");
            LibraryTypes.BaseId(null).Should().BeNull();
        }

        [Test]
        public void clean_name_and_pin_key_carry_the_library()
        {
            LibraryTypes.CleanNameFor("mushokutensei", LibraryType.Manga).Should().Be("mushokutensei");
            LibraryTypes.CleanNameFor("mushokutensei", LibraryType.LightNovel).Should().Be("mushokutensei~ln");

            LibraryTypes.PinKey("Re:Zero", LibraryType.Manga).Should().Be("Re:Zero");
            LibraryTypes.PinKey("Re:Zero", LibraryType.LightNovel).Should().Be("Re:Zero (light novel)");
        }

        [Test]
        public void author_library_is_derived_from_its_metadata_id()
        {
            new Author { Metadata = new AuthorMetadata { ForeignAuthorId = "local-x~ln" } }.Library.Should().Be(LibraryType.LightNovel);
            new Author { Metadata = new AuthorMetadata { ForeignAuthorId = "local-x" } }.Library.Should().Be(LibraryType.Manga);
            new Author().Library.Should().Be(LibraryType.Manga);
        }
    }
}
