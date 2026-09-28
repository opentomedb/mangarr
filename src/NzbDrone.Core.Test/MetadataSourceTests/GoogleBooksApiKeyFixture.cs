using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSourceTests
{
    // The Google Books API key can come from Settings -> Metadata (per-user, editable in the UI)
    // or the GOOGLE_BOOKS_API_KEY container variable (the original env-first path). The UI value
    // wins when set; blank everywhere means anonymous quota.
    [TestFixture]
    public class GoogleBooksApiKeyFixture : CoreTest
    {
        [Test]
        public void config_key_wins_over_environment()
        {
            GoogleBooksService.ResolveApiKey("config-key", "env-key").Should().Be("config-key");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void environment_key_is_the_fallback_when_config_is_blank(string configValue)
        {
            GoogleBooksService.ResolveApiKey(configValue, "env-key").Should().Be("env-key");
        }

        [Test]
        public void blank_everywhere_stays_blank()
        {
            GoogleBooksService.ResolveApiKey(null, null).Should().BeNull();
        }

        // The UI never sees the real key: a stored key is shown as a fixed mask, and the mask
        // echoing back on save must NOT overwrite the stored key (null = skip in the config save).
        [Test]
        public void stored_key_is_masked_for_display()
        {
            GoogleBooksService.MaskApiKey("real-key").Should().Be(GoogleBooksService.ApiKeyMask);
        }

        [TestCase(null)]
        [TestCase("")]
        public void blank_key_displays_as_empty(string configValue)
        {
            GoogleBooksService.MaskApiKey(configValue).Should().Be(string.Empty);
        }

        [Test]
        public void echoed_mask_must_not_overwrite_the_stored_key()
        {
            GoogleBooksService.SanitizeIncomingApiKey(GoogleBooksService.ApiKeyMask).Should().BeNull();
        }

        [Test]
        public void new_key_and_explicit_clear_pass_through()
        {
            GoogleBooksService.SanitizeIncomingApiKey("new-key").Should().Be("new-key");
            GoogleBooksService.SanitizeIncomingApiKey("").Should().Be("");
        }

        // Source indicator for the UI: which key (if any) is in effect.
        [TestCase("cfg", "env", "settings")]
        [TestCase("cfg", null, "settings")]
        [TestCase("", "env", "environment")]
        [TestCase(null, "env", "environment")]
        [TestCase("", "", "none")]
        [TestCase(null, null, "none")]
        public void source_reflects_which_key_is_in_effect(string configValue, string envValue, string expected)
        {
            GoogleBooksService.ResolveApiKeySource(configValue, envValue).Should().Be(expected);
        }
    }
}
