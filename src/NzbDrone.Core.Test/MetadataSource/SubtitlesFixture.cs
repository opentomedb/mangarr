using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // Audiobook identity B1 (2026-09-17, D3): the one subtitle rule, driven by the SAO / Re:ZERO /
    // Overlord / K-ON! shapes Audible and Google actually return. Aliases stay out of the [TestCase]
    // table (NUnit unwraps an array argument); the Re:ZERO alias row is its own test.
    [TestFixture]
    public class SubtitlesFixture : CoreTest
    {
        [TestCase("Sword Art Online 2: Aincrad (light novel)", null, null, "Sword Art Online", 2, "Aincrad")]
        [TestCase("Sword Art Online 7 (light novel)", "Mother's Rosary", null, "Sword Art Online", 7, "Mother's Rosary")]
        [TestCase("Sword Art Online 21 (light novel)", null, "Unital Ring I", "Sword Art Online", 21, "Unital Ring I")]
        [TestCase("Sword Art Online 1: Aincrad", null, null, "Sword Art Online", 1, "Aincrad")]
        [TestCase("Sword Art Online 3: Fairy Dance (light novel)", null, null, "Sword Art Online", 2, null)]
        [TestCase("Solo Leveling, Vol. 3", null, null, "Solo Leveling", 3, null)]
        [TestCase("K-ON!, Vol. 1: Vol. 1", null, null, "K-ON!", 1, null)]
        [TestCase("Overlord 5: The Paladin of the Holy Kingdom", null, null, "Overlord", 5, "The Paladin of the Holy Kingdom")]
        [TestCase("Sword Art Online 2 (light novel)", "Aincrad (light novel)", null, "Sword Art Online", 2, "Aincrad")]
        [TestCase("Sword Art Online 2 (light novel)", "Sword Art Online", null, "Sword Art Online", 2, null)]
        [TestCase("Sword Art Online 2 (light novel)", "Vol. 2", null, "Sword Art Online", 2, null)]
        [TestCase("Sword Art Online 2 (light novel)", "II", null, "Sword Art Online", 2, null)]
        [TestCase("Sword Art Online Progressive 2: Foo", null, null, "Sword Art Online", 2, null)]
        [TestCase("Overlord 1 (light novel)", "Overlord, Book 1", null, "Overlord", 1, null)]
        [TestCase("Sword Art Online 2 (light novel)", "Sword Art Online 2", null, "Sword Art Online", 2, null)]
        [TestCase("Sword Art Online 2 (light novel)", "A Light Novel", null, "Sword Art Online", 2, null)]
        [TestCase("Sword Art Online 3 (light novel)", "vol. 3", null, "Sword Art Online", 3, null)]
        public void derive(string title, string subtitle, string seriesBookTitle, string seriesName, double volumeNumber, string expected)
        {
            Subtitles.Derive(title, subtitle, seriesBookTitle, seriesName, null, volumeNumber).Should().Be(expected);
        }

        [Test]
        public void a_volume_without_a_subtitle_is_null_even_when_the_series_is_an_alias()
        {
            Subtitles.Derive("Re:ZERO -Starting Life in Another World-, Vol. 1 (light novel)", null, null, "Re:Zero", new[] { "Re:ZERO -Starting Life in Another World-" }, 1)
                .Should().BeNull();
        }

        [Test]
        public void an_alias_series_prefix_is_accepted()
        {
            Subtitles.Derive("Re:ZERO -Starting Life in Another World- 3: Truth of Zero", null, null, "Re:Zero", new[] { "Re:ZERO -Starting Life in Another World-" }, 3)
                .Should().Be("Truth of Zero");
        }

        // Fix wave (2026-09-17, I1): the gate rejects the series named by an alias too, not only by
        // its display name.
        [Test]
        public void a_candidate_that_is_an_alias_of_the_series_is_null()
        {
            Subtitles.Derive("Re:ZERO -Starting Life in Another World-, Vol. 1 (light novel)", "Re:ZERO -Starting Life in Another World-", null, "Re:Zero", new[] { "Re:ZERO -Starting Life in Another World-" }, 1)
                .Should().BeNull();
        }

        [Test]
        public void all_null_is_null()
        {
            Subtitles.Derive(null, null, null, "Sword Art Online", null, 1).Should().BeNull();
        }

        // Aincrad subtitle (2026-09-23, controller ruling): the catalogue artifact's own volume
        // title is trusted even when it equals one of the entry's own aliases (SAO's "Aincrad").
        [Test]
        public void a_trusted_artifact_candidate_that_is_an_alias_of_the_series_is_not_rejected()
        {
            Subtitles.Derive(null, "Aincrad", null, "Sword Art Online", new[] { "Aincrad" }, 1, trustedArtifactCandidate: true)
                .Should().Be("Aincrad");
        }

        // The default (Audible/Google candidates) keeps the gate -- only an explicitly trusted
        // artifact candidate bypasses it.
        [Test]
        public void an_untrusted_candidate_that_is_an_alias_of_the_series_is_still_null()
        {
            Subtitles.Derive(null, "Aincrad", null, "Sword Art Online", new[] { "Aincrad" }, 1)
                .Should().BeNull();
        }

        // Junk subtitle filter (fix round 3, 2026-09-24): live finding after 10.0.0.557 --
        // Audible/Google put the whole volume label into their subtitle field instead of a real arc
        // name for 10 Mushoku Tensei/Classroom of the Elite volumes, and it reached calibre/ABS as
        // "Mushoku Tensei: Jobless Reincarnation: Jobless Reincarnation (Light Novel), Vol. 1 (Vol. 1)".
        [TestCase(null, "Jobless Reincarnation (Light Novel), Vol. 1", null, "Mushoku Tensei: Jobless Reincarnation", 1)]
        [TestCase(null, "Light Novel, Vol. 5", null, "Classroom of the Elite", 5)]
        [TestCase(null, "Light Novel (Classroom of the Elite, Book 26)", null, "Classroom of the Elite", 10)]
        public void derive_rejects_the_junk_subtitles_from_the_live_finding(string title, string subtitle, string seriesBookTitle, string seriesName, double volumeNumber)
        {
            Subtitles.Derive(title, subtitle, seriesBookTitle, seriesName, null, volumeNumber).Should().BeNull();
        }

        // A genuine arc/volume subtitle keeps working -- the filter must not overreach.
        [TestCase("Aincrad", "Sword Art Online")]
        [TestCase("Alicization Uniting", "Sword Art Online")]
        [TestCase("Boy Meets Girl, Compulsively", "Mushoku Tensei: Jobless Reincarnation")]
        public void a_genuine_arc_subtitle_is_still_accepted(string subtitle, string seriesName)
        {
            Subtitles.Derive(null, subtitle, null, seriesName, null, 1).Should().Be(subtitle);
        }

        [TestCase("Vol. 1")]
        [TestCase("Vol 1")]
        [TestCase("Volume 12")]
        [TestCase("Book 26")]
        [TestCase("#3")]
        [TestCase("v1")]
        [TestCase("v 1")]
        public void is_junk_rejects_a_re_stated_volume_number_anywhere_in_the_text(string token)
        {
            Subtitles.IsJunk($"Some Arc Name, {token}", "Some Series").Should().BeTrue();
        }

        [TestCase("(Light Novel)")]
        [TestCase("light novel")]
        [TestCase("LIGHT NOVEL")]
        [TestCase("LightNovel")]
        public void is_junk_rejects_the_light_novel_tag_anywhere_in_the_text(string token)
        {
            Subtitles.IsJunk($"Some Arc Name {token}", "Some Series").Should().BeTrue();
        }

        [Test]
        public void is_junk_rejects_a_candidate_containing_the_whole_series_name()
        {
            Subtitles.IsJunk("A prefix, Classroom of the Elite, a suffix", "Classroom of the Elite").Should().BeTrue();
        }

        [Test]
        public void is_junk_rejects_a_candidate_containing_the_series_names_main_segment_after_a_colon()
        {
            Subtitles.IsJunk("Jobless Reincarnation", "Mushoku Tensei: Jobless Reincarnation").Should().BeTrue();
        }

        [Test]
        public void is_junk_does_not_reject_a_genuine_arc_name()
        {
            Subtitles.IsJunk("Aincrad", "Sword Art Online").Should().BeFalse();
            Subtitles.IsJunk("Alicization Uniting", "Sword Art Online").Should().BeFalse();
        }

        // Full-title subtitles (2026-09-24, the maintainer: "Rascal Does Not Dream have subtitles that should be
        // present in Mangarr for LN's too"): Yen Press's own volume titles for the line, as the
        // OpenTome artifact carries them. Vol 16's "+" is part of the published title (Yen Press,
        // Amazon, Audible: "Rascal Does Not Dream of a Beach Queen + (light novel)"); only the
        // trailing "(light novel)" edition tag goes.
        [TestCase("Rascal Does Not Dream of Bunny Girl Senpai", 1, "Rascal Does Not Dream of Bunny Girl Senpai")]
        [TestCase("Rascal Does Not Dream of Petite Devil Kohai", 2, "Rascal Does Not Dream of Petite Devil Kohai")]
        [TestCase("Rascal Does Not Dream of Logical Witch", 3, "Rascal Does Not Dream of Logical Witch")]
        [TestCase("Rascal Does Not Dream of Siscon Idol", 4, "Rascal Does Not Dream of Siscon Idol")]
        [TestCase("Rascal Does Not Dream of a Sister Home Alone", 5, "Rascal Does Not Dream of a Sister Home Alone")]
        [TestCase("Rascal Does Not Dream of a Dreaming Girl", 6, "Rascal Does Not Dream of a Dreaming Girl")]
        [TestCase("Rascal Does Not Dream of His First Love", 7, "Rascal Does Not Dream of His First Love")]
        [TestCase("Rascal Does Not Dream of a Sister Venturing Out", 8, "Rascal Does Not Dream of a Sister Venturing Out")]
        [TestCase("Rascal Does Not Dream of a Knapsack Kid", 9, "Rascal Does Not Dream of a Knapsack Kid")]
        [TestCase("Rascal Does Not Dream of a Lost Singer", 10, "Rascal Does Not Dream of a Lost Singer")]
        [TestCase("Rascal Does Not Dream of a Nightingale", 11, "Rascal Does Not Dream of a Nightingale")]
        [TestCase("Rascal Does Not Dream of His Student", 12, "Rascal Does Not Dream of His Student")]
        [TestCase("Rascal Does Not Dream of Santa Claus", 13, "Rascal Does Not Dream of Santa Claus")]
        [TestCase("Rascal Does Not Dream of His Girlfriend", 14, "Rascal Does Not Dream of His Girlfriend")]
        [TestCase("Rascal Does Not Dream of a Dear Friend", 15, "Rascal Does Not Dream of a Dear Friend")]
        [TestCase("Rascal Does Not Dream of a Beach Queen + (light novel)", 16, "Rascal Does Not Dream of a Beach Queen +")]
        [TestCase("Rascal Does Not Dream of a Beach Queen +", 16, "Rascal Does Not Dream of a Beach Queen +")]
        public void a_full_volume_title_opening_with_the_series_name_is_the_subtitle(string candidate, double volumeNumber, string expected)
        {
            Subtitles.Derive(null, candidate, null, "Rascal Does Not Dream", null, volumeNumber, trustedArtifactCandidate: true).Should().Be(expected);

            // Review fix (2026-09-24): only the catalogue's own title gets the exception.
            Subtitles.Derive(null, candidate, null, "Rascal Does Not Dream", null, volumeNumber).Should().BeNull();
        }

        // Other lines in the artifact whose volume titles open with the line name.
        [TestCase("Fullmetal Alchemist: The Land of Sand", "Fullmetal Alchemist")]
        [TestCase("Kokoro Connect Hito Random", "Kokoro Connect")]
        [TestCase("Book Girl and the Suicidal Mime", "Book Girl")]
        [TestCase("Sword Art Online Alternative Gun Gale Online I: Squad Jam", "Sword Art Online Alternative Gun Gale Online")]
        [TestCase("Magical Girl Raising Project: Restart (Part 1)", "Magical Girl Raising Project")]
        public void is_full_title_accepts_a_real_volume_title_that_opens_with_the_series_name(string candidate, string seriesName)
        {
            Subtitles.IsFullTitle(candidate, seriesName).Should().BeTrue();
            Subtitles.IsJunk(candidate, seriesName).Should().BeFalse();
        }

        // Still junk: the series restated (alone, with a number, with edition words, with one word
        // more), the name not as a whole-word prefix, the Vol./Light Novel tokens, and the
        // "contains a series segment elsewhere" shapes from the Mushoku finding.
        [TestCase("Rascal Does Not Dream", "Rascal Does Not Dream")]
        [TestCase("Rascal Does Not Dream 2", "Rascal Does Not Dream")]
        [TestCase("Rascal Does Not Dream of", "Rascal Does Not Dream")]
        [TestCase("Rascal Does Not Dream +", "Rascal Does Not Dream")]
        [TestCase("Rascal Does Not Dream Novel Series", "Rascal Does Not Dream")]
        [TestCase("Rascal Does Not Dream (Light Novel) of Petite Devil Kohai", "Rascal Does Not Dream")]
        [TestCase("Rascal Does Not Dream of Petite Devil Kohai, Vol. 2", "Rascal Does Not Dream")]
        [TestCase("Rascal Does Not Dreamer of Bunny Girls", "Rascal Does Not Dream")]
        [TestCase("Petite Devil Kohai: Rascal Does Not Dream", "Rascal Does Not Dream")]
        [TestCase("Sword Art Online Progressive", "Sword Art Online")]
        [TestCase("Sword Art Online Progressive 2", "Sword Art Online")]
        [TestCase("Sword Art Online Progressive 1", "Sword Art Online Progressive")]
        [TestCase("Mushoku Tensei: Jobless Reincarnation (Light Novel), Vol. 1", "Mushoku Tensei: Jobless Reincarnation")]
        [TestCase("Jobless Reincarnation (Light Novel), Vol. 1", "Mushoku Tensei: Jobless Reincarnation")]
        [TestCase("Jobless Reincarnation", "Mushoku Tensei: Jobless Reincarnation")]
        [TestCase("Mushoku Tensei: Jobless Reincarnation Recollections", "Mushoku Tensei: Jobless Reincarnation")]
        public void is_junk_still_rejects_the_series_restated(string candidate, string seriesName)
        {
            Subtitles.IsJunk(candidate, seriesName).Should().BeTrue();
        }

        // Arc/volume subtitles that never named the series are unchanged.
        [TestCase("Truth of Zero", "Re:Zero")]
        [TestCase("Aincrad", "Sword Art Online")]
        [TestCase("Unital Ring I", "Sword Art Online")]
        [TestCase("Aria of a Starless Night", "Sword Art Online Progressive")]
        [TestCase("Barcarolle of Froth", "Sword Art Online Progressive")]
        [TestCase("Oh! My Useless Goddess!", "KonoSuba: God's Blessing on this Wonderful World!")]
        [TestCase("God's Blessing on These Wonderful Adventurers!", "KonoSuba: God's Blessing on this Wonderful World!")]
        [TestCase("Megumin's Turn", "KonoSuba: An Explosion on this Wonderful World!")]
        [TestCase("Boy Meets Girl, Compulsively", "Mushoku Tensei: Jobless Reincarnation")]
        public void an_arc_subtitle_is_unchanged_by_the_full_title_rule(string candidate, string seriesName)
        {
            Subtitles.IsFullTitle(candidate, seriesName).Should().BeFalse();
            Subtitles.IsJunk(candidate, seriesName).Should().BeFalse();
            Subtitles.Derive(null, candidate, null, seriesName, null, 1).Should().Be(candidate);
        }

        // "<series>: X" subtitles (2026-09-24, controller): the exact series name + ": " opening the
        // candidate is cut and the remainder is the subtitle -- one-word titles included (Tokyo Ghoul,
        // Magical Girl Raising Project, from the OpenTome artifact). The remainder still faces every
        // gate; a name that is not at the start stays rejected (Haruhi, Slayers).
        [TestCase("Tokyo Ghoul: Days", "Tokyo Ghoul", "Days")]
        [TestCase("Tokyo Ghoul: Void", "Tokyo Ghoul", "Void")]
        [TestCase("Tokyo Ghoul: Past", "Tokyo Ghoul", "Past")]
        [TestCase("Magical Girl Raising Project: Black", "Magical Girl Raising Project", "Black")]
        [TestCase("Magical Girl Raising Project: Red", "Magical Girl Raising Project", "Red")]
        [TestCase("Magical Girl Raising Project: QUEENS", "Magical Girl Raising Project", "QUEENS")]
        [TestCase("Magical Girl Raising Project: Episodes Φ", "Magical Girl Raising Project", "Episodes Φ")]
        [TestCase("Magical Girl Raising Project: Restart (Part 1)", "Magical Girl Raising Project", "Restart (Part 1)")]
        [TestCase("Fullmetal Alchemist: The Land of Sand", "Fullmetal Alchemist", "The Land of Sand")]
        [TestCase("Lycoris Recoil: Ordinary Days (light novel)", "Lycoris Recoil", "Ordinary Days")]
        [TestCase("tokyo ghoul: Days", "Tokyo Ghoul", "Days")]
        [TestCase("The Melancholy of Haruhi Suzumiya", "Haruhi Suzumiya", null)]
        [TestCase("The Demon Slayers!", "Slayers", null)]
        [TestCase("Tokyo Ghoul: Vol. 3", "Tokyo Ghoul", null)]
        [TestCase("Tokyo Ghoul: Light Novel", "Tokyo Ghoul", null)]
        [TestCase("Tokyo Ghoul: Tokyo Ghoul", "Tokyo Ghoul", null)]
        [TestCase("Tokyo Ghoul: Re", "Tokyo Ghoul", null)]
        [TestCase("Tokyo Ghoul:", "Tokyo Ghoul", null)]
        [TestCase("Mushoku Tensei: Jobless Reincarnation: Jobless Reincarnation (Light Novel), Vol. 1", "Mushoku Tensei: Jobless Reincarnation", null)]
        [TestCase("Mushoku Tensei: Jobless Reincarnation (Light Novel), Vol. 1", "Mushoku Tensei: Jobless Reincarnation", null)]
        [TestCase("Mushoku Tensei: Jobless Reincarnation", "Mushoku Tensei: Jobless Reincarnation", null)]
        [TestCase("Mushoku Tensei: Jobless Reincarnation: Jobless Reincarnation", "Mushoku Tensei: Jobless Reincarnation", null)]
        [TestCase("Mushoku Tensei: Jobless Reincarnation: Boy Meets Girl, Compulsively", "Mushoku Tensei: Jobless Reincarnation", "Boy Meets Girl, Compulsively")]
        public void a_series_colon_prefix_is_cut_and_the_remainder_is_the_subtitle(string candidate, string seriesName, string expected)
        {
            Subtitles.Derive(null, candidate, null, seriesName, null, 1, trustedArtifactCandidate: true).Should().Be(expected);

            // Review fix (2026-09-24): an Audible/Google field is never cut.
            Subtitles.Derive(null, candidate, null, seriesName, null, 1).Should().BeNull();
        }

        // Review fix (2026-09-24): Audible/Google fields that open with the series name are product
        // or edition labels far more often than volume titles -- they keep the strict gate.
        [TestCase("Sword Art Online Alternative Gun Gale Online", "Sword Art Online")]
        [TestCase("Sword Art Online Progressive Barcarolle of Froth", "Sword Art Online")]
        [TestCase("Mushoku Tensei: Jobless Reincarnation Redundant Reincarnation", "Mushoku Tensei: Jobless Reincarnation")]
        [TestCase("Re:ZERO -Starting Life in Another World- Ex", "Re:Zero")]
        [TestCase("Classroom of the Elite: Year 2", "Classroom of the Elite")]
        [TestCase("Overlord Unabridged Audiobook", "Overlord")]
        public void an_untrusted_candidate_opening_with_the_series_name_is_null(string candidate, string seriesName)
        {
            Subtitles.Derive(null, candidate, null, seriesName, null, 1).Should().BeNull();
        }

        // Edition words (audiobook, unabridged, audio) never make a full title, even from the catalogue.
        [TestCase("Overlord Unabridged Audiobook")]
        [TestCase("Overlord Audio Edition")]
        public void edition_words_after_the_series_name_are_not_a_full_title(string candidate)
        {
            Subtitles.IsFullTitle(candidate, "Overlord").Should().BeFalse();
            Subtitles.Derive(null, candidate, null, "Overlord", null, 1, trustedArtifactCandidate: true).Should().BeNull();
        }

        [Test]
        public void is_junk_without_the_full_title_allowance_rejects_a_full_title()
        {
            Subtitles.IsJunk("Rascal Does Not Dream of Petite Devil Kohai", "Rascal Does Not Dream").Should().BeFalse();
            Subtitles.IsJunk("Rascal Does Not Dream of Petite Devil Kohai", "Rascal Does Not Dream", allowFullTitle: false).Should().BeTrue();
        }

        // "Re:Zero" is a whole-word prefix of its long alias; a candidate that IS that alias is still
        // no subtitle -- NamesSeries rejects it before the full-title rule is ever asked.
        [Test]
        public void a_candidate_that_is_the_long_re_zero_alias_is_still_null()
        {
            Subtitles.Derive(null, "Re:ZERO -Starting Life in Another World-", null, "Re:Zero", new[] { "Re:ZERO -Starting Life in Another World-" }, 1)
                .Should().BeNull();
        }

        [Test]
        public void is_junk_treats_a_blank_candidate_as_junk()
        {
            Subtitles.IsJunk(null, "Sword Art Online").Should().BeTrue();
            Subtitles.IsJunk("   ", "Sword Art Online").Should().BeTrue();
        }
    }
}
