using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class QualityParserFixture : CoreTest
    {
        public static object[] SelfQualityParserCases =
        {
            new object[] { Quality.MP3 },
            new object[] { Quality.FLAC },
            new object[] { Quality.CBR },
            new object[] { Quality.CBZ },
            new object[] { Quality.ZIP },
            new object[] { Quality.AZW3 }
        };

        [TestCase("VA - The Best 101 Love Ballads (2017) MP3 [192 kbps]")]
        [TestCase("Maula - Jism 2 [2012] Mp3 - 192Kbps [Extended]- TK")]
        [TestCase("VA - Complete Clubland - The Ultimate Ride Of Your Lfe [2014][MP3][192 kbps]")]
        [TestCase("Complete Clubland - The Ultimate Ride Of Your Lfe [2014][MP3](192kbps)")]
        [TestCase("The Ultimate Ride Of Your Lfe [192 KBPS][2014][MP3]")]
        [TestCase("Gary Clark Jr - Live North America 2016 (2017) MP3 192kbps")]
        [TestCase("Some Song [192][2014][MP3]")]
        [TestCase("Other Song (192)[2014][MP3]")]
        [TestCase("Caetano Veloso Discografia Completa MP3 @256")]
        [TestCase("Jake Bugg - Jake Bugg (Book) [2012] {MP3 256 kbps}")]
        [TestCase("Clean Bandit - New Eyes [2014] [Mp3-256]-V3nom [GLT]")]
        [TestCase("PJ Harvey - Let England Shake [mp3-256-2011][trfkad]")]
        [TestCase("Childish Gambino - Awaken, My Love Book 2016 mp3 320 Kbps")]
        [TestCase("Maluma – Felices Los 4 MP3 320 Kbps 2017 Download")]
        [TestCase("Sia - This Is Acting (Standard Edition) [2016-Web-MP3-V0(VBR)]")]
        [TestCase("Mount Eerie - A Crow Looked at Me (2017) [MP3 V0 VBR)]")]
        [TestCase("Queen - The Ultimate Best Of Queen(2011)[mp3]")]
        [TestCase("Maroon 5 Ft Kendrick Lamar -Dont Wanna Know MP3 2016")]
        public void should_parse_mp3_quality(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.MP3);
        }

        [TestCase("Kendrick Lamar - DAMN (2017) FLAC")]
        [TestCase("Alicia Keys - Vault Playlist Vol. 1 (2017) [FLAC CD]")]
        [TestCase("Gorillaz - Humanz (Deluxe) - lossless FLAC Tracks - 2017 - CDrip")]
        [TestCase("David Bowie - Blackstar (2016) [FLAC]")]
        [TestCase("The Cure - Greatest Hits (2001) FLAC Soup")]
        [TestCase("Slowdive- Souvlaki (FLAC)")]
        [TestCase("John Coltrane - Kulu Se Mama (1965) [EAC-FLAC]")]
        [TestCase("The Rolling Stones - The Very Best Of '75-'94 (1995) {FLAC}")]
        [TestCase("Migos-No_Label_II-CD-FLAC-2014-FORSAKEN")]
        [TestCase("ADELE 25 CD FLAC 2015 PERFECT")]
        public void should_parse_flac_quality(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.FLAC);
        }

        // Flack doesn't get match for 'FLAC' quality
        [TestCase("Roberta Flack 2006 - The Very Best of")]
        public void should_not_parse_flac_quality(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.Unknown);
        }

        // Manga: scene releases carry a volume token but rarely a CBZ keyword, so they fall back to
        // CBZ instead of Unknown (which the quality profile rejects). Import corrects the real format.
        [TestCase("Jujutsu Kaisen v25 (2025) (Digital) (LuCaZ)")]
        [TestCase("Chainsaw Man v13 (2023) (Digital) (1r0n)")]
        [TestCase("Demon Slayer Vol. 23 (2021)")]
        [TestCase("Solo Leveling Book 4")]
        [TestCase("Dandadan Volumes 1 to 13 (2023) (Digital)")]
        [TestCase("Spy x Family v01-v12 Complete")]
        public void should_default_manga_volume_release_to_cbz(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.CBZ);
        }

        // An explicit quality keyword still wins over the manga default.
        [TestCase("Chainsaw Man v13 (2023) (CBR)")]
        public void should_keep_explicit_quality_over_manga_default(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.CBR);
        }

        // A non-manga release with no quality token and no volume token stays Unknown.
        [TestCase("Some Random Audiobook Without Format Tag (2016)")]
        public void should_not_default_non_manga_to_cbz(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.Unknown);
        }

        // A light-novel release with an explicit epub token, or LN wording and no format token
        // at all, is an EPUB: that is what LN rips are. The manga profile does not allow EPUB,
        // so manga entries still reject it; the Light Novel EPUB profile accepts it.
        [TestCase("Fuse - That Time I Got Reincarnated as a Slime Vol. 21 (epub)")]
        [TestCase("Fuse - Reincarnated as a Slime 07 - That Time I Got Reincarnated as a Slime, Vol. 7 (epub)")]
        [TestCase("Overlord Vol. 16 Light Novel")]
        [TestCase("Sword Art Online - Volume 17 - Alicization Awakening [Yen On][Kobo]")]
        [TestCase("Re:ZERO Vol. 25 [Yen On]")]
        [TestCase("The Rising of the Shield Hero Vol. 22 (Novel)")]
        [TestCase("Overlord Vol. 16 [J-Novel Club]")]
        [TestCase("Spice and Wolf Vol. 21 Kindle")]
        [TestCase("Series v05 (Kindle) [Yen Press]")]
        public void should_grade_light_novel_volume_release_as_epub(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.EPUB);
        }

        // Formats nothing imports (mobi/kepub) stay Unknown so no profile grabs them. AZW3 is the
        // one exception (below) - it now grades its own quality, a light-novel fallback.
        [TestCase("Mushoku Tensei Jobless Reincarnation Vol. 22 (Light Novel) (mobi)")]
        [TestCase("Series v05 [MOBI]")]
        [TestCase("Overlord Vol. 3 (kepub)")]
        public void should_keep_non_epub_ebook_formats_unknown(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.Unknown);
        }

        // AZW3 (id 7): a light-novel ebook fallback, grabbed only when no EPUB release exists.
        // The azw3 token grades AZW3 through the main codec path (CodecRegex), which runs before
        // the volume-token wording fallback ever executes - so a name carrying BOTH signals (the
        // last two cases below, mixing azw3 with Kindle/Light Novel wording) still grades AZW3,
        // not the LN-wording EPUB default. Every case here is matched by the codec path (the name
        // contains "azw3" somewhere); the volume-token Azw3Regex branch and the extension map are
        // defensive fallbacks for names the codec path would not catch, and are not exercised by
        // these cases (extension coverage lives in MediaFileExtensionsFixture, whose
        // GetQualityForExtension(".azw3") check goes through the extension map directly).
        // It counts as an ebook signal for LooksLikeEbook too.
        [TestCase("Series v05 [AZW3]")]
        [TestCase("Sword Art Online Progressive Vol. 8 [azw3]")]
        [TestCase("Series v05.azw3")]
        [TestCase("Series v05 (Kindle) [AZW3]")]
        [TestCase("Overlord Vol. 5 (Light Novel) [azw3]")]
        [TestCase("Series Light Novel v05 [AZW3]")]
        public void should_grade_azw3_as_azw3(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.AZW3);
            QualityParser.LooksLikeEbook(title).Should().BeTrue();
        }

        // Final review I3 (2026-09-22): an azw3 release that calls itself manga is a manga Kindle
        // rip, not a light-novel fallback. It grades Unknown as it did before AZW3 existed, so the
        // manga-wording guard keeps it off a light-novel twin's EPUB profile.
        [TestCase("Series v05 [AZW3] (Digital) (manga)")]
        [TestCase("Series.v05.Manga.AZW3")]
        [TestCase("Series Comic v05 (azw3)")]
        public void should_keep_a_manga_worded_azw3_release_unknown(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.Unknown);
        }

        // Audiobook wording without a codec token: Unknown Audio (allowed at the bottom of the
        // Light Novel Audio profile). A codec token still wins (M4B/MP3 cases above and below).
        [TestCase("Konosuba Vol 3 Audiobook")]
        [TestCase("Overlord Vol. 16 Light Novel Audiobook")]
        [TestCase("Overlord Vol. 16 Audiobooks")]
        [TestCase("Mushoku Tensei Jobless Reincarnation Vol. 5 (Unabridged)")]
        public void should_grade_audiobook_wording_as_unknown_audio(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.UnknownAudio);
        }

        [TestCase("Mushoku Tensei Jobless Reincarnation Vol. 5 (Light Novel) [M4B]")]
        [TestCase("Konosuba Vol 3 Audiobook M4B")]
        public void should_parse_m4b_light_novel_audiobook(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.M4B);
        }

        [TestCase("Konosuba Vol 3 Audiobook MP3")]
        public void should_parse_mp3_light_novel_audiobook(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.MP3);
        }

        // Guard against over-blocking: a real manga release from Yen Press (the manga imprint, not
        // the "Yen On" LN line) must still parse as CBZ.
        [TestCase("Spy x Family v10 (2023) (Digital) (Yen Press)")]
        [TestCase("Kaiju No. 8 Vol. 12 (2024) (Digital)")]
        [TestCase("Jujutsu Kaisen v25 (2025) (Digital) (LuCaZ)")]
        public void should_still_default_real_manga_to_cbz(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.CBZ);
        }

        // Ebook release groups: [Stick] and [LuCaZ] ship epub/audiobook BATCHES whose names carry
        // no format token at all ("Series [Yen Press] [Stick]"), so the tokenless-batch bridge
        // graded them CBZ and grabbed a full light-novel batch. Only the square-bracketed tag is
        // the ebook signal: the same groups release real manga singles under parenthesized tags
        // ("(Digital) (LuCaZ)"), and a series title containing the bare word must not match.
        [TestCase("That Time I Got Reincarnated as a Slime [Yen Press] [Stick]", true)]
        [TestCase("Mushoku Tensei - Jobless Reincarnation [Seven Seas] [LuCaZ]", true)]
        [TestCase("Mushoku Tensei - Jobless Reincarnation (Digital)", false)]
        [TestCase("Jujutsu Kaisen v25 (2025) (Digital) (LuCaZ)", false)]
        [TestCase("Stick Fight Chronicles [Kodansha Comics]", false)]
        public void should_flag_ebook_release_group_tags(string title, bool expected)
        {
            QualityParser.LooksLikeEbook(title).Should().Be(expected);
        }

        // The archive-token guard reads the same normalised name ParseQuality grades from: an
        // underscore-delimited "Overlord_Vol_5_CBZ" grades CBZ explicitly and must say so, or the
        // light-novel re-grade turns it into an EPUB grab for a .cbz (final review I6).
        [TestCase("Overlord_Vol_5_CBZ", true)]
        [TestCase("Overlord Vol. 5 (cbz)", true)]
        [TestCase("Overlord_Vol_5_[Yen_Press]", false)]
        [TestCase("Overlord Vol. 5 [Yen Press]", false)]
        public void should_flag_explicit_archive_tokens_in_underscore_delimited_names(string title, bool expected)
        {
            QualityParser.HasExplicitArchiveToken(title).Should().Be(expected);
        }

        // LN PDF (2026-09-22): "pdf" as the name's ONLY explicit archive token -- a light novel's
        // "[PDF]" can be its ebook; a second archive token makes it a comic release.
        [TestCase("Overlord Vol. 5 [PDF]", true)]
        [TestCase("Overlord_Vol_5_pdf", true)]
        [TestCase("Overlord Vol. 5 (PDF) (pdf)", true)]
        [TestCase("Overlord Vol. 5 [PDF] [CBZ]", false)]
        [TestCase("Overlord Vol. 5 (pdf, rar)", false)]
        [TestCase("Overlord Vol. 5 [EPUB]", false)]
        [TestCase("Overlord Vol. 5", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void has_only_pdf_archive_token(string name, bool expected)
        {
            QualityParser.HasOnlyPdfArchiveToken(name).Should().Be(expected);
        }

        // Manga wording (manga / comic(s) / cbz / cbr) in a release name that carries no format
        // token: a light-novel search must treat it as the archive class, not the searched one
        // (D1: "Yen.Press-The.Eminence.In.Shadow.Vol.06.Manga...Comic.eBook" grabbed twelve
        // times as an EPUB). Read on the raw name, tags included — "[Manga] Series" and a manga
        // imprint tag are manga releases too — with "_"/"." as spaces; "Comix" is not "comic".
        [TestCase("Yen.Press-The.Eminence.In.Shadow.Vol.06.Manga.2022.Hybrid.Comic.eBook-BitBook", true)]
        [TestCase("Yen_Press_The_Eminence_In_Shadow_Vol_06_Manga_eBook", true)]
        [TestCase("Overlord Vol. 5 CBR", true)]
        [TestCase("[Manga] The Eminence in Shadow Vol 6", true)]
        [TestCase("Overlord Vol. 5 [Kodansha Comics]", true)]
        [TestCase("The Eminence in Shadow v01-06 [Yen Press] [Stick]", false)]
        [TestCase("Overlord Vol 5 (Light Novel) epub", false)]
        [TestCase("Chainsaw Man v13 (Digital) (LuCaZ)", false)]
        [TestCase("Solo Leveling 8 (Comix Wave) [M4B]", false)]
        [TestCase("", false)]
        public void should_flag_manga_wording(string title, bool expected)
        {
            QualityParser.LooksLikeManga(title).Should().Be(expected);
        }

        [TestCase("The Chainsmokers & Coldplay - Something Just Like This")]
        [TestCase("Frank Ocean Blonde 2016")]
        public void quality_parse(string title)
        {
            ParseAndVerifyQuality(title, null, 0, Quality.Unknown);
        }

        [Test]
        [TestCaseSource(nameof(SelfQualityParserCases))]
        public void parsing_our_own_quality_enum_name(Quality quality)
        {
            var fileName = string.Format("Some book [{0}]", quality.Name);
            var result = QualityParser.ParseQuality(fileName);
            result.Quality.Should().Be(quality);
        }

        [TestCase("Little Mix - Salute [Deluxe Edition] [2013] [M4A-256]-V3nom [GLT")]
        public void should_parse_quality_from_name(string title)
        {
            QualityParser.ParseQuality(title).QualityDetectionSource.Should().Be(QualityDetectionSource.Name);
        }

        [Test]
        public void should_parse_null_quality_description_as_unknown()
        {
            QualityParser.ParseCodec(null, null).Should().Be(Codec.Unknown);
        }

        [TestCase("Author Title - Book Title 2017 REPACK FLAC aAF", true)]
        [TestCase("Author Title - Book Title 2017 RERIP FLAC aAF", true)]
        [TestCase("Author Title - Book Title 2017 PROPER FLAC aAF", false)]
        public void should_be_able_to_parse_repack(string title, bool isRepack)
        {
            var result = QualityParser.ParseQuality(title);
            result.Revision.Version.Should().Be(2);
            result.Revision.IsRepack.Should().Be(isRepack);
        }

        private void ParseAndVerifyQuality(string name, string desc, int bitrate, Quality quality, int sampleSize = 0)
        {
            var result = QualityParser.ParseQuality(name);
            result.Quality.Should().Be(quality);
        }
    }
}
