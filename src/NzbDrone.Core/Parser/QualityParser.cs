using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Parser
{
    public class QualityParser
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(QualityParser));

        private static readonly Regex ProperRegex = new (@"\b(?<proper>proper)\b",
                                                                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RepackRegex = new (@"\b(?<repack>repack|rerip)\b",
                                                                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex VersionRegex = new (@"\d[-._ ]?v(?<version>\d)[-._ ]|\[v(?<version>\d)\]",
                                                                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RealRegex = new (@"\b(?<real>REAL)\b",
                                                                RegexOptions.Compiled);

        private static readonly Regex CodecRegex = new (@"\b(?:(?<PDF>PDF)|(?<EPUB>EPUB)|(?<AZW3>AZW3)|(?<CBZ>CBZ)|(?<CBR>CBR)|(?<ZIP>ZIP)|(?<RAR>RAR)|(?<MP1>MPEG Version \d(.5)? Audio, Layer 1|MP1)|(?<MP2>MPEG Version \d(.5)? Audio, Layer 2|MP2)|(?<MP3VBR>MP3.*VBR|MPEG Version \d(.5)? Audio, Layer 3 vbr)|(?<MP3CBR>MP3|MPEG Version \d(.5)? Audio, Layer 3)|(?<FLAC>flac)|(?<WAVPACK>wavpack|wv)|(?<ALAC>alac)|(?<WMA>WMA\d?)|(?<WAV>WAV|PCM)|(?<AAC>M4A|M4P|M4B|AAC|mp4a|MPEG-4 Audio(?!.*alac))|(?<OGG>OGG|OGA|Vorbis))\b|(?<APE>monkey's audio|[\[|\(].*\bape\b.*[\]|\)])|(?<OPUS>Opus Version \d(.5)? Audio|[\[|\(].*\bopus\b.*[\]|\)])",
                                                             RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Light-novel signals. Three regexes because they now mean different grades:
        //   - Azw3Regex: the azw3 token -> Quality.AZW3, a fallback grabbed when no EPUB exists.
        //   - NonEpubEbookRegex: formats nothing imports (mobi/azw/kepub, without the 3) -> stays
        //     Unknown.
        //   - LightNovelWordingRegex: LN wording / LN-only imprints / ebook stores with no format
        //     token at all -> EPUB (what LN rips are). The manga profile does not allow EPUB, so a
        //     manga entry still rejects it; the Light Novel EPUB profile accepts it.
        private static readonly Regex Azw3Regex = new Regex(
            @"\bazw3\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex NonEpubEbookRegex = new Regex(
            @"\b(?:mobi|azw|kepub)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LightNovelWordingRegex = new Regex(
            @"\b(?:epub|light\s*novels?|j-?novel|yen\s*on|kobo|kindle)\b|[\[(]novels?[\])]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ExplicitArchiveTokenRegex = new Regex(
            @"\b(?:cbz|cbr|zip|rar|pdf)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Manga wording in a release name that carries no format token ("...Vol.06.Manga.2022.
        // Hybrid.Comic.eBook", "[Manga] Series"). Read only inside the light-novel branches: such
        // a release is the archive class whatever the search asked for.
        private static readonly Regex MangaWordingRegex = new Regex(
            @"\b(?:manga|comics?|cbz|cbr)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Audiobook wording with no codec token: "Audiobook(s)", "Unabridged", "Narrated by".
        private static readonly Regex AudiobookWordingRegex = new Regex(
            @"\b(?:audio\s*books?|unabridged|narrated)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Square-bracketed tags of groups whose BATCHES are epub/audiobook ("Series [Yen Press]
        // [Stick]" — no format token anywhere, which is how a full LN epub batch got graded CBZ
        // and grabbed). Deliberately NOT part of LightNovelWordingRegex: these groups also release real
        // manga singles under parenthesized tags ("Jujutsu Kaisen v25 (Digital) (LuCaZ)"), so the
        // signal is only trustworthy square-bracketed and only in the tokenless-batch path below.
        private static readonly Regex EbookGroupTagRegex = new Regex(
            @"\[(?:stick|lucaz)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Whether a release name carries an explicit ebook / light-novel signal. Exposed for the
        // tokenless-batch bridge, which applies this guard before defaulting a matched series
        // batch to CBZ; the group-tag signal applies here only, never to volume-token releases.
        public static bool LooksLikeEbook(string name)
        {
            return name.IsNotNullOrWhiteSpace() && (NonEpubEbookRegex.IsMatch(name) || Azw3Regex.IsMatch(name) || LightNovelWordingRegex.IsMatch(name) || EbookGroupTagRegex.IsMatch(name));
        }

        // Audiobook wording with no codec token ("Audiobook", "Unabridged", "Narrated by").
        public static bool LooksLikeAudiobook(string name)
        {
            return name.IsNotNullOrWhiteSpace() && AudiobookWordingRegex.IsMatch(name);
        }

        // Manga wording (manga / comic(s) / cbz / cbr) on the raw name, tags included — a "[Manga]"
        // or manga-imprint tag is a manga release too — with "_" and "." as spaces ("_" is a word
        // character, so "_Manga_" has no boundary without it). "Comix" is not "comic".
        public static bool LooksLikeManga(string name)
        {
            return name.IsNotNullOrWhiteSpace() && MangaWordingRegex.IsMatch(name.Replace('_', ' ').Replace('.', ' '));
        }

        // Whether the name itself carries one of the archive tokens the codec regex grades from,
        // i.e. a CBZ/CBR/ZIP/RAR/PDF grade is explicit rather than the volume-token default. Reads
        // the same underscore-normalised name ParseQuality grades from ("Overlord_Vol_5_CBZ").
        public static bool HasExplicitArchiveToken(string name)
        {
            return name.IsNotNullOrWhiteSpace() && ExplicitArchiveTokenRegex.IsMatch(name.Replace('_', ' '));
        }

        // LN PDF (2026-09-22): whether "pdf" is the name's ONLY explicit archive token (the same
        // underscore-normalised read as HasExplicitArchiveToken). A light novel's "[PDF]" release can
        // be its ebook; "PDF + CBZ" (or any other archive token) is a comic release.
        public static bool HasOnlyPdfArchiveToken(string name)
        {
            if (name.IsNullOrWhiteSpace())
            {
                return false;
            }

            var tokens = ExplicitArchiveTokenRegex.Matches(name.Replace('_', ' '))
                .Select(m => m.Value.ToLowerInvariant())
                .Distinct()
                .ToList();

            return tokens.Count == 1 && tokens[0] == "pdf";
        }

        public static QualityModel ParseQuality(string name, string desc = null, List<int> categories = null)
        {
            Logger.Debug("Trying to parse quality for '{0}'", name);

            if (name.IsNullOrWhiteSpace() && desc.IsNullOrWhiteSpace())
            {
                return new QualityModel { Quality = Quality.Unknown };
            }

            var normalizedName = name.Replace('_', ' ').Trim().ToLower();
            var result = ParseQualityModifiers(name, normalizedName);

            if (desc.IsNotNullOrWhiteSpace())
            {
                var descCodec = ParseCodec(desc, "");
                Logger.Trace($"Got codec {descCodec}");

                result.Quality = FindQuality(descCodec);

                if (result.Quality != Quality.Unknown)
                {
                    result.QualityDetectionSource = QualityDetectionSource.TagLib;
                    return result;
                }
            }

            var codec = ParseCodec(normalizedName, name);

            switch (codec)
            {
                case Codec.PDF:
                    result.Quality = Quality.PDF;
                    break;
                case Codec.EPUB:
                    result.Quality = Quality.EPUB;
                    break;
                case Codec.AZW3:
                    result.Quality = Quality.AZW3;
                    break;
                case Codec.CBZ:
                    result.Quality = Quality.CBZ;
                    break;
                case Codec.CBR:
                    result.Quality = Quality.CBR;
                    break;
                case Codec.ZIP:
                    result.Quality = Quality.ZIP;
                    break;
                case Codec.RAR:
                    result.Quality = Quality.RAR;
                    break;
                case Codec.FLAC:
                case Codec.ALAC:
                case Codec.WAVPACK:
                    result.Quality = Quality.FLAC;
                    break;
                case Codec.AAC:
                    result.Quality = Quality.M4B;
                    break;
                case Codec.MP1:
                case Codec.MP2:
                case Codec.MP3VBR:
                case Codec.MP3CBR:
                case Codec.APE:
                case Codec.WMA:
                case Codec.WAV:
                case Codec.AACVBR:
                case Codec.OGG:
                case Codec.OPUS:
                    result.Quality = Quality.MP3;
                    break;
                case Codec.Unknown:
                default:
                    result.Quality = Quality.Unknown;
                    break;
            }

            //Based on extension
            if (result.Quality == Quality.Unknown && !name.ContainsInvalidPathChars())
            {
                try
                {
                    result.Quality = MediaFileExtensions.GetQualityForExtension(name.GetPathExtension());
                    result.QualityDetectionSource = QualityDetectionSource.Extension;
                }
                catch (ArgumentException)
                {
                    //Swallow exception for cases where string contains illegal
                    //path characters.
                }
            }

            //Based on category
            if (result.Quality == Quality.Unknown && categories != null)
            {
                if (categories.Any(x => x >= 3000 && x < 4000))
                {
                    result.Quality = Quality.UnknownAudio;
                    result.QualityDetectionSource = QualityDetectionSource.Category;
                }
            }

            // Volume-tokened releases rarely carry a format token. Grade them by wording so the
            // right profile decides: audiobook wording -> Unknown Audio; light-novel wording ->
            // EPUB; a format nothing imports (mobi/azw) -> Unknown; else the manga default CBZ.
            // Import re-detects the real quality from the file either way.
            if (result.Quality == Quality.Unknown && MangaVolumeParser.HasVolumeToken(name))
            {
                if (AudiobookWordingRegex.IsMatch(name))
                {
                    result.Quality = Quality.UnknownAudio;
                }
                else if (Azw3Regex.IsMatch(name))
                {
                    result.Quality = Quality.AZW3;
                }
                else if (NonEpubEbookRegex.IsMatch(name))
                {
                    return result;
                }
                else if (LightNovelWordingRegex.IsMatch(name))
                {
                    result.Quality = Quality.EPUB;
                }
                else
                {
                    result.Quality = Quality.CBZ;
                }

                result.QualityDetectionSource = QualityDetectionSource.Name;
            }

            // Final review I3 (2026-09-22): an azw3 release that calls itself manga ("Series v05
            // [AZW3] (Digital) (manga)") is a manga Kindle rip, not the light-novel fallback. It
            // stays Unknown, as azw3 graded before AZW3 existed, whichever path above graded it --
            // so a light-novel twin's EPUB profile never takes it. An imported file's quality comes
            // from the file itself (EbookTagService), not from this.
            if (result.Quality == Quality.AZW3 && LooksLikeManga(name))
            {
                result.Quality = Quality.Unknown;
            }

            return result;
        }

        public static Codec ParseCodec(string name, string origName)
        {
            if (name.IsNullOrWhiteSpace())
            {
                return Codec.Unknown;
            }

            var match = CodecRegex.Match(name);

            if (!match.Success)
            {
                return Codec.Unknown;
            }

            if (match.Groups["PDF"].Success)
            {
                return Codec.PDF;
            }

            if (match.Groups["EPUB"].Success)
            {
                return Codec.EPUB;
            }

            if (match.Groups["AZW3"].Success)
            {
                return Codec.AZW3;
            }

            if (match.Groups["CBZ"].Success)
            {
                return Codec.CBZ;
            }

            if (match.Groups["CBR"].Success)
            {
                return Codec.CBR;
            }

            if (match.Groups["ZIP"].Success)
            {
                return Codec.ZIP;
            }

            if (match.Groups["RAR"].Success)
            {
                return Codec.RAR;
            }

            if (match.Groups["FLAC"].Success)
            {
                return Codec.FLAC;
            }

            if (match.Groups["ALAC"].Success)
            {
                return Codec.ALAC;
            }

            if (match.Groups["WMA"].Success)
            {
                return Codec.WMA;
            }

            if (match.Groups["WAV"].Success)
            {
                return Codec.WAV;
            }

            if (match.Groups["AAC"].Success)
            {
                return Codec.AAC;
            }

            if (match.Groups["OGG"].Success)
            {
                return Codec.OGG;
            }

            if (match.Groups["OPUS"].Success)
            {
                return Codec.OPUS;
            }

            if (match.Groups["MP1"].Success)
            {
                return Codec.MP1;
            }

            if (match.Groups["MP2"].Success)
            {
                return Codec.MP2;
            }

            if (match.Groups["MP3VBR"].Success)
            {
                return Codec.MP3VBR;
            }

            if (match.Groups["MP3CBR"].Success)
            {
                return Codec.MP3CBR;
            }

            if (match.Groups["WAVPACK"].Success)
            {
                return Codec.WAVPACK;
            }

            if (match.Groups["APE"].Success)
            {
                return Codec.APE;
            }

            return Codec.Unknown;
        }

        private static Quality FindQuality(Codec codec)
        {
            switch (codec)
            {
                case Codec.ALAC:
                case Codec.FLAC:
                case Codec.WAVPACK:
                case Codec.WAV:
                    return Quality.FLAC;
                case Codec.AAC:
                    return Quality.M4B;
                default:
                    return Quality.MP3;
            }
        }

        private static QualityModel ParseQualityModifiers(string name, string normalizedName)
        {
            var result = new QualityModel { Quality = Quality.Unknown };

            if (ProperRegex.IsMatch(normalizedName))
            {
                result.Revision.Version = 2;
            }

            if (RepackRegex.IsMatch(normalizedName))
            {
                result.Revision.Version = 2;
                result.Revision.IsRepack = true;
            }

            var versionRegexResult = VersionRegex.Match(normalizedName);

            if (versionRegexResult.Success)
            {
                result.Revision.Version = Convert.ToInt32(versionRegexResult.Groups["version"].Value);
            }

            //TODO: re-enable this when we have a reliable way to determine real
            var realRegexResult = RealRegex.Matches(name);

            if (realRegexResult.Count > 0)
            {
                result.Revision.Real = realRegexResult.Count;
            }

            return result;
        }
    }

    public enum Codec
    {
        MP1,
        MP2,
        MP3CBR,
        MP3VBR,
        FLAC,
        ALAC,
        APE,
        WAVPACK,
        WMA,
        AAC,
        AACVBR,
        OGG,
        OPUS,
        WAV,
        PDF,
        EPUB,
        AZW3,
        CBZ,
        CBR,
        ZIP,
        RAR,
        Unknown
    }
}
