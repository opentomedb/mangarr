using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    // A stored name is curated and never changes on refresh or rebind (2026-09-15): the
    // resolver's title names an entry only when it is being created.
    [TestFixture]
    public class BookInfoProxyNameFixture
    {
        private const string Fallback = "Rezero Starting Life In Another World Chapter 3 Truth Of Zero";
        private const string Proper = "Re:ZERO -Starting Life in Another World-, Chapter 3: Truth of Zero";

        [Test]
        public void an_existing_entry_keeps_its_stored_name_whatever_the_provider_resolves()
        {
            // a different AniList title (the Mushoku Tensei / Re:Zero ch.4 rebind case)
            BookInfoProxy.ResolveDisplayName("Mushoku Tensei: Jobless Reincarnation", "Mushoku Tensei").Should().Be("Mushoku Tensei");
            BookInfoProxy.ResolveDisplayName(Proper, "Some Old Name").Should().Be("Some Old Name");

            // a mere case / punctuation variant of the stored name
            BookInfoProxy.ResolveDisplayName("RE: Zero -Starting Life in Another World- Chapter 3: Truth of Zero", Proper).Should().Be(Proper);

            // the resolver echoed the de-slugged id (nothing matched)
            BookInfoProxy.ResolveDisplayName(Fallback, Proper).Should().Be(Proper);
        }

        [Test]
        public void a_new_entry_is_named_by_the_provider()
        {
            BookInfoProxy.ResolveDisplayName(Proper, null).Should().Be(Proper);
            BookInfoProxy.ResolveDisplayName(Proper, string.Empty).Should().Be(Proper);
            BookInfoProxy.ResolveDisplayName(Fallback, null).Should().Be(Fallback);
        }
    }
}
