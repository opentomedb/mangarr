using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Books;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Parser
{
    public static class Parser
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(Parser));

        private static readonly Regex[] ReportMusicTitleRegex = new[]
        {
            // Track with author (01 - author - trackName)
            new Regex(@"(?<trackNumber>\d*){0,1}([-| ]{0,1})(?<author>[a-zA-Z0-9, ().&_]*)[-| ]{0,1}(?<trackName>[a-zA-Z0-9, ().&_]+)",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Track without author (01 - trackName)
            new Regex(@"(?<trackNumber>\d*)[-| .]{0,1}(?<trackName>[a-zA-Z0-9, ().&_]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Track without trackNumber or author(trackName)
            new Regex(@"(?<trackNumber>\d*)[-| .]{0,1}(?<trackName>[a-zA-Z0-9, ().&_]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Track without trackNumber and  with author(author - trackName)
            new Regex(@"(?<trackNumber>\d*)[-| .]{0,1}(?<trackName>[a-zA-Z0-9, ().&_]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Track with author and starting title (01 - author - trackName)
            new Regex(@"(?<trackNumber>\d*){0,1}[-| ]{0,1}(?<author>[a-zA-Z0-9, ().&_]*)[-| ]{0,1}(?<trackName>[a-zA-Z0-9, ().&_]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        private static readonly Regex[] ReportBookTitleRegex = new[]
        {
            //ruTracker - (Genre) [Source]? Author - Discography
            new Regex(@"^(?:\(.+?\))(?:\W*(?:\[(?<source>.+?)\]))?\W*(?<author>.+?)(?: - )(?<discography>Discography|Discografia).+?(?<startyear>\d{4}).+?(?<endyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author - Discography with two years
            new Regex(@"^(?<author>.+?)(?: - )(?:.+?)?(?<discography>Discography|Discografia).+?(?<startyear>\d{4}).+?(?<endyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author - Discography with end year
            new Regex(@"^(?<author>.+?)(?: - )(?:.+?)?(?<discography>Discography|Discografia).+?(?<endyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author Discography with two years
            new Regex(@"^(?<author>.+?)\W*(?<discography>Discography|Discografia).+?(?<startyear>\d{4}).+?(?<endyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author Discography with end year
            new Regex(@"^(?<author>.+?)\W*(?<discography>Discography|Discografia).+?(?<endyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author Discography
            new Regex(@"^(?<author>.+?)\W*(?<discography>Discography|Discografia)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //a private tracker - Title by Author [lang / pdf]
            new Regex(@"^(?<book>.+)\bby\b(?<author>.+?)(?:\[|\()",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //ruTracker - (Genre) [Source]? Author - Book - Year
            new Regex(@"^(?:\(.+?\))(?:\W*(?:\[(?<source>.+?)\]))?\W*(?<author>.+?)(?: - )(?<book>.+?)(?: - )(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author-Book-Version-Source-Year
            //ex. Imagine Dragons-Smoke And Mirrors-Deluxe Edition-2CD-FLAC-2015-JLM
            new Regex(@"^(?<author>.+?)[-](?<book>.+?)[-](?:[\(|\[]?)(?<version>.+?(?:Edition)?)(?:[\)|\]]?)[-](?<source>\d?CD|WEB).+?(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author-Book-Source-Year
            //ex. Dani_Sbert-Togheter-WEB-2017-FURY
            new Regex(@"^(?<author>.+?)[-](?<book>.+?)[-](?<source>\d?CD|WEB).+?(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author - Book (Year) Strict
            new Regex(@"^(?:(?<author>.+?)(?: - )+)(?<book>.+?)\W*(?:\(|\[).+?(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author - Book (Year)
            new Regex(@"^(?:(?<author>.+?)(?: - )+)(?<book>.+?)\W*(?:\(|\[)(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author - Book - Year [something]
            new Regex(@"^(?:(?<author>.+?)(?: - )+)(?<book>.+?)\W*(?: - )(?<releaseyear>\d{4})\W*(?:\(|\[)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author - Book [something] or Author - Book (something)
            new Regex(@"^(?:(?<author>.+?)(?: - )+)(?<book>.+?)\W*(?:\(|\[)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author - Book Year
            new Regex(@"^(?:(?<author>.+?)(?: - )+)(?<book>.+?)\W*(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author-Book (Year) Strict
            //Hyphen no space between author and book
            new Regex(@"^(?:(?<author>.+?)(?:-)+)(?<book>.+?)\W*(?:\(|\[).+?(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author-Book (Year)
            //Hyphen no space between author and book
            new Regex(@"^(?:(?<author>.+?)(?:-)+)(?<book>.+?)\W*(?:\(|\[)(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author-Book [something] or Author-Book (something)
            //Hyphen no space between author and book
            new Regex(@"^(?:(?<author>.+?)(?:-)+)(?<book>.+?)\W*(?:\(|\[)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author-Book-something-Year
            new Regex(@"^(?:(?<author>.+?)(?:-)+)(?<book>.+?)(?:-.+?)(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author-Book Year
            //Hyphen no space between author and book
            new Regex(@"^(?:(?<author>.+?)(?:-)+)(?:(?<book>.+?)(?:-)+)(?<releaseyear>\d{4})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            //Author - Year - Book
            // Hypen with no or more spaces between author/book/year
            new Regex(@"^(?:(?<author>.+?)(?:-))(?<releaseyear>\d{4})(?:-)(?<book>[^-]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        private static readonly Regex[] RejectHashedReleasesRegex = new Regex[]
            {
                // Generic match for md5 and mixed-case hashes.
                new Regex(@"^[0-9a-zA-Z]{32}", RegexOptions.Compiled),

                // Generic match for shorter lower-case hashes.
                new Regex(@"^[a-z0-9]{24}$", RegexOptions.Compiled),

                // Format seen on some indexers' releases
                // Be very strict with these coz they are very close to the valid 101 ep numbering.
                new Regex(@"^[A-Z]{11}\d{3}$", RegexOptions.Compiled),
                new Regex(@"^[a-z]{12}\d{3}$", RegexOptions.Compiled),

                //Backup filename (Unknown origins)
                new Regex(@"^Backup_\d{5,}S\d{2}-\d{2}$", RegexOptions.Compiled),

                //123 - Started appearing December 2014
                new Regex(@"^123$", RegexOptions.Compiled),

                //abc - Started appearing January 2015
                new Regex(@"^abc$", RegexOptions.Compiled | RegexOptions.IgnoreCase),

                //b00bs - Started appearing January 2015
                new Regex(@"^b00bs$", RegexOptions.Compiled | RegexOptions.IgnoreCase)
            };

        private static readonly RegexReplace NormalizeRegex = new RegexReplace(@"((?:\b|_)(?<!^)(a(?!$)|an|the|and|or|of)(?!$)(?:\b|_))|\W|_",
                                                                string.Empty,
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PercentRegex = new Regex(@"(?<=\b\d+)%", RegexOptions.Compiled);

        private static readonly Regex FileExtensionRegex = new Regex(@"\.[a-z0-9]{2,4}$",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        //TODO Rework this Regex for Music
        // "<label> N-M" at the end of a light-novel batch name, the label being an edition word
        // ("English Light Novels", "Light Novel", "LN", "Novels", "EPUB(s)") with no vol token.
        private static readonly Regex LightNovelBatchRangeRegex = new Regex(@"\s+(?:english\s+)?(?:light\s*novels?|novels?|ln|epubs?)\s+(?<start>\d{1,3})\s*\p{Pd}\s*(?<end>\d{1,3})\b",
                                                                            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly RegexReplace SimpleTitleRegex = new RegexReplace(@"(?:(480|720|1080|2160|320)[ip]|[xh][\W_]?26[45]|DD\W?5\W1|848x480|1280x720|1920x1080|3840x2160|4096x2160|(8|10)b(it)?)\s*",
                                                                string.Empty,
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Valid TLDs http://data.iana.org/TLD/tlds-alpha-by-domain.txt
        private static readonly RegexReplace WebsitePrefixRegex = new RegexReplace(@"^(?:\[\s*)?(?:www\.)?[-a-z0-9-]{1,256}\.(?:[a-z]{2,6}\.[a-z]{2,6}|xn--[a-z0-9-]{4,}|[a-z]{2,})\b(?:\s*\]|[ -]{2,})[ -]*",
                                                                string.Empty,
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly RegexReplace WebsitePostfixRegex = new RegexReplace(@"(?:\[\s*)?(?:www\.)?[-a-z0-9-]{1,256}\.(?:xn--[a-z0-9-]{4,}|[a-z]{2,6})\b(?:\s*\])$",
                                                                string.Empty,
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AirDateRegex = new Regex(@"^(.*?)(?<!\d)((?<airyear>\d{4})[_.-](?<airmonth>[0-1][0-9])[_.-](?<airday>[0-3][0-9])|(?<airmonth>[0-1][0-9])[_.-](?<airday>[0-3][0-9])[_.-](?<airyear>\d{4}))(?!\d)",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SixDigitAirDateRegex = new Regex(@"(?<=[_.-])(?<airdate>(?<!\d)(?<airyear>[1-9]\d{1})(?<airmonth>[0-1][0-9])(?<airday>[0-3][0-9]))(?=[_.-])",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly RegexReplace CleanReleaseGroupRegex = new RegexReplace(@"^(.*?[-._ ])|(-(RP|1|NZBGeek|Obfuscated|Scrambled|sample|Pre|postbot|xpost|Rakuv[a-z0-9]*|WhiteRev|BUYMORE|AsRequested|AlternativeToRequested|GEROV|Z0iDS3N|Chamele0n|4P|4Planet))+$",
                                                                string.Empty,
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly RegexReplace CleanTorrentSuffixRegex = new RegexReplace(@"\[(?:ettv|rartv|rarbg|cttv)\]$",
                                                                string.Empty,
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ReleaseGroupRegex = new Regex(@"-(?<releasegroup>[a-z0-9]+)(?<!MP3|ALAC|FLAC|WEB)(?:\b|[-._ ])",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AnimeReleaseGroupRegex = new Regex(@"^(?:\[(?<subgroup>(?!\s).+?(?<!\s))\](?:_|-|\s|\.)?)",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex YearInTitleRegex = new Regex(@"^(?<title>.+?)(?:\W|_)?(?<year>\d{4})",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<char> WordDelimiters = new HashSet<char>(" .,_-=()[]|\"`'’");
        private static readonly Regex WordDelimiterRegex = new Regex(@"(\s|\.|,|_|-|=|\(|\)|\[|\]|\|)+", RegexOptions.Compiled);
        private static readonly Regex PunctuationRegex = new Regex(@"[^\w\s]", RegexOptions.Compiled);
        private static readonly Regex CommonWordRegex = new Regex(@"\b(a|an|the|and|or|of)\b\s?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SpecialEpisodeWordRegex = new Regex(@"\b(part|special|edition|christmas)\b\s?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DuplicateSpacesRegex = new Regex(@"\s{2,}", RegexOptions.Compiled);

        private static readonly Regex RequestInfoRegex = new Regex(@"\[.+?\]", RegexOptions.Compiled);

        private static readonly string[] Numbers = new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        private static readonly Regex[] CommonTagRegex = new Regex[]
        {
            new Regex(@"(\[|\()*\b((featuring|feat.|feat|ft|ft.)\s{1}){1}\s*.*(\]|\))*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"(?:\(|\[)(?:[^\(\[]*)(?:version|limited|deluxe|single|clean|book|special|bonus|promo|remastered)(?:[^\)\]]*)(?:\)|\])", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };

        private static readonly Regex[] BracketRegex = new Regex[]
        {
            new Regex(@"\(.*\)", RegexOptions.Compiled),
            new Regex(@"\[.*\]", RegexOptions.Compiled)
        };

        private static readonly Regex AfterDashRegex = new Regex(@"[-:].*", RegexOptions.Compiled);

        public static ParsedTrackInfo ParseMusicPath(string path)
        {
            var fileInfo = new FileInfo(path);

            ParsedTrackInfo result = null;

            Logger.Debug("Attempting to parse book info using directory and file names. {0}", fileInfo.Directory.Name);
            result = ParseTitle(fileInfo.Directory.Name + " " + fileInfo.Name);

            if (result == null)
            {
                Logger.Debug("Attempting to parse book info using directory name. {0}", fileInfo.Directory.Name);
                result = ParseTitle(fileInfo.Directory.Name + fileInfo.Extension);
            }

            return result;
        }

        public static ParsedTrackInfo ParseTitle(string title)
        {
            try
            {
                if (!ValidateBeforeParsing(title))
                {
                    return null;
                }

                Logger.Debug("Parsing string '{0}'", title);

                var releaseTitle = RemoveFileExtension(title);

                releaseTitle = releaseTitle.Replace("【", "[").Replace("】", "]");

                var simpleTitle = SimpleTitleRegex.Replace(releaseTitle);

                // TODO: Quick fix stripping [url] - prefixes.
                simpleTitle = WebsitePrefixRegex.Replace(simpleTitle);
                simpleTitle = WebsitePostfixRegex.Replace(simpleTitle);

                simpleTitle = CleanTorrentSuffixRegex.Replace(simpleTitle);

                var airDateMatch = AirDateRegex.Match(simpleTitle);
                if (airDateMatch.Success)
                {
                    simpleTitle = airDateMatch.Groups[1].Value + airDateMatch.Groups["airyear"].Value + "." + airDateMatch.Groups["airmonth"].Value + "." + airDateMatch.Groups["airday"].Value;
                }

                var sixDigitAirDateMatch = SixDigitAirDateRegex.Match(simpleTitle);
                if (sixDigitAirDateMatch.Success)
                {
                    var airYear = sixDigitAirDateMatch.Groups["airyear"].Value;
                    var airMonth = sixDigitAirDateMatch.Groups["airmonth"].Value;
                    var airDay = sixDigitAirDateMatch.Groups["airday"].Value;

                    if (airMonth != "00" || airDay != "00")
                    {
                        var fixedDate = string.Format("20{0}.{1}.{2}", airYear, airMonth, airDay);

                        simpleTitle = simpleTitle.Replace(sixDigitAirDateMatch.Groups["airdate"].Value, fixedDate);
                    }
                }

                foreach (var regex in ReportMusicTitleRegex)
                {
                    var match = regex.Matches(simpleTitle);

                    if (match.Count != 0)
                    {
                        Logger.Trace(regex);
                        try
                        {
                            var result = ParseMatchMusicCollection(match);

                            if (result != null)
                            {
                                result.Quality = QualityParser.ParseQuality(title);
                                Logger.Debug("Quality parsed: {0}", result.Quality);

                                return result;
                            }
                        }
                        catch (InvalidDateException ex)
                        {
                            Logger.Debug(ex, ex.Message);
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (!title.ToLower().Contains("password") && !title.ToLower().Contains("yenc"))
                {
                    Logger.Error(e, "An error has occurred while trying to parse {0}", title);
                }
            }

            Logger.Debug("Unable to parse {0}", title);
            return null;
        }

        // mediaType (2026-09-18, D4): the class the search asked for, so a light-novel audio search
        // can bridge Audible-named releases (below); null for every other caller, byte-identical.
        public static ParsedBookInfo ParseBookTitleWithSearchCriteria(string title, Author author, List<Book> books, MediaType? mediaType = null)
        {
            try
            {
                if (!ValidateBeforeParsing(title))
                {
                    return null;
                }

                var authorName = author.Name == "Various Authors" ? "VA" : author.Name.RemoveAccent();

                Logger.Debug("Parsing string '{0}' using search criteria author: '{1}' books: '{2}'",
                             title,
                             authorName.RemoveAccent(),
                             string.Join(", ", books.Select(a => a.Title.RemoveAccent())));

                var releaseTitle = RemoveFileExtension(title);

                // Same CJK-bracket normalization as ParseTitle: "【OSHI NO KO】 v13" reads as "[OSHI NO KO] v13".
                releaseTitle = releaseTitle.Replace("【", "[").Replace("】", "]");

                // Light-novel EPUB batches (2026-09-21): "[Synthworks] Rascal Does Not Dream English
                // Light Novels 1-15" names the series, an edition label and a BARE range, so no bridge
                // saw a volume token and the batch died as Unable to parse (then, force-grabbed, typed
                // archive and refused at import). For a light-novel entry the label + bare range
                // becomes a volume-tokened range ("... Vol. 1-15") that TryParseSeries / ParseVolume
                // already read as a pack. Never for audio searches or manga entries.
                if (author.Library == LibraryType.LightNovel && mediaType != MediaType.Audio)
                {
                    releaseTitle = LightNovelBatchRangeRegex.Replace(releaseTitle, " Vol. ${start}-${end}");
                }

                var simpleTitle = SimpleTitleRegex.Replace(releaseTitle);

                simpleTitle = WebsitePrefixRegex.Replace(simpleTitle);
                simpleTitle = WebsitePostfixRegex.Replace(simpleTitle);

                simpleTitle = CleanTorrentSuffixRegex.Replace(simpleTitle);

                // Manga search bridge: scene releases like "Series vN (Year) (Group)" carry no
                // author, and the fuzzy author/book match below can't bridge "v25" to the edition
                // title "Vol. 25". When the release's own series (parsed from its title) matches
                // the searched author, attribute it here and let GetBooks map the volume number.
                // Returns before the fuzzy path; only fires when a volume token is present and the
                // series matches the searched author, so non-manga releases are untouched.
                // TryParseSeries also recognises pack ranges ("Series Volumes 1 to 13"), so a
                // whole-series pack is attributed too; ParseVolume sets VolumeStart/VolumeEnd and
                // GetBooks fans it out to every volume in range.
                // Accepted keys for both manga bridges below: the series name plus its
                // AniList-synonym aliases (so the fan abbreviation "Tensura" resolves to "That
                // Time I Got Reincarnated as a Slime"), each an EXACT normalized match with a
                // length floor — no fuzzy widening, so neither bridge can open the floodgates to
                // unrelated series.
                var acceptedKeys = new HashSet<string>();
                void AddKey(string s)
                {
                    var key = s.CleanAuthorName();
                    if (key.Length >= 4)
                    {
                        acceptedKeys.Add(key);
                    }
                }

                AddKey(author.Name);
                AddKey(MangaVolumeParser.StripParentheticals(author.Name));
                foreach (var alias in author.Metadata?.Value?.Aliases ?? Enumerable.Empty<string>())
                {
                    AddKey(alias);
                }

                bool IsAcceptedSeries(string s)
                {
                    return acceptedKeys.Contains(s.CleanAuthorName()) ||
                           acceptedKeys.Contains(MangaVolumeParser.StripParentheticals(s).CleanAuthorName());
                }

                // Preferred Edition (2026-09-24, spec §3): a non-English series' own volume tokens ("Tome 5",
                // "Tomes 1 à 5", "Band 05", "Bd. 5", "Bände 1-5", "5巻"; "T05" only right after an accepted
                // series name) read as "Vol." -- an English series never takes this branch. Ruling S7: its
                // English anchor name is an accepted series too ("Attack on Titan T05 [FR]" for "L'Attaque des
                // Titans"), added before the rewrite because the T05 gate asks IsAcceptedSeries.
                // Follow-up round (KR/CN consumer): a fallback series parses like English (EditionLanguages.ReleaseLanguage).
                var editionLanguage = EditionLanguages.ReleaseLanguage(author.Metadata?.Value);
                var editionSearch = !EditionLanguages.IsEnglish(editionLanguage);

                if (editionSearch)
                {
                    var anchorName = author.Metadata.Value.AnchorName;

                    if (anchorName.IsNotNullOrWhiteSpace())
                    {
                        AddKey(anchorName);
                        AddKey(MangaVolumeParser.StripParentheticals(anchorName));
                    }

                    releaseTitle = EditionVolumeTokens.Rewrite(releaseTitle, editionLanguage, IsAcceptedSeries);
                    simpleTitle = EditionVolumeTokens.Rewrite(simpleTitle, editionLanguage, IsAcceptedSeries);
                }

                if (MangaVolumeParser.HasVolumeToken(simpleTitle))
                {
                    // A volume-tokened manga release: its parsed series IS its identity. Match that
                    // series against the searched series' name OR a curated alias. If nothing matches,
                    // REJECT here rather than falling through to the fuzzy author match below — fuzzy
                    // prefix-matches the searched author inside a DIFFERENT series ("Tokyo Ghoul"
                    // inside "Tokyo Ghoul - re v01", the :re sequel) and would grab the wrong series.
                    //
                    // The candidates cover both scene orderings: "Series vN" (series before the token)
                    // and the token-first "Vol 7 Dan da Dan" / "Book 5 Tensura by Fuse" (series after).
                    string matchedSeries = null;
                    foreach (var candidate in MangaVolumeParser.GetSeriesCandidates(simpleTitle))
                    {
                        if (acceptedKeys.Contains(candidate.CleanAuthorName()) ||
                            acceptedKeys.Contains(MangaVolumeParser.StripParentheticals(candidate).CleanAuthorName()))
                        {
                            matchedSeries = candidate;
                            break;
                        }

                        // Dot-joined publisher prefix ("VIZ.Media.Jujutsu.Kaisen.Vol.30...") — shave a
                        // KNOWN publisher off the front and retry the exact normalized match. Unknown
                        // leading words still reject ("Shin <Series>" must not match "<Series>").
                        var withoutPublisher = MangaVolumeParser.StripLeadingPublisher(candidate);
                        if (withoutPublisher != null &&
                            (acceptedKeys.Contains(withoutPublisher.CleanAuthorName()) ||
                             acceptedKeys.Contains(MangaVolumeParser.StripParentheticals(withoutPublisher).CleanAuthorName())))
                        {
                            matchedSeries = withoutPublisher;
                            break;
                        }

                        // Scene eBook releases prefix the series with the publisher and dotted spacing
                        // ("Seven.Seas.Entertainment-Mushoku.Tensei...Vol.05...eBook-GROUP"); try the
                        // text after each hyphen — still an exact normalized match against an accepted
                        // key, so a genuinely different series can't slip through.
                        var hyphenIndex = candidate.IndexOf('-');
                        while (hyphenIndex >= 0 && hyphenIndex < candidate.Length - 1)
                        {
                            var suffix = candidate.Substring(hyphenIndex + 1).Trim();
                            if (suffix.Length > 0 &&
                                (acceptedKeys.Contains(suffix.CleanAuthorName()) ||
                                 acceptedKeys.Contains(MangaVolumeParser.StripParentheticals(suffix).CleanAuthorName())))
                            {
                                matchedSeries = suffix;
                                break;
                            }

                            hyphenIndex = candidate.IndexOf('-', hyphenIndex + 1);
                        }

                        if (matchedSeries != null)
                        {
                            break;
                        }
                    }

                    if (matchedSeries != null)
                    {
                        var mangaResult = new ParsedBookInfo
                        {
                            AuthorName = author.Name,
                            AuthorTitleInfo = GetAuthorTitleInfo(author.Name),
                            BookTitle = matchedSeries,
                            Quality = QualityParser.ParseQuality(title),
                            ReleaseGroup = ParseReleaseGroup(releaseTitle)
                        };

                        MangaVolumeParser.ParseVolume(releaseTitle, mangaResult);

                        // Preferred Edition (2026-09-24, ruling A9, v1): an "Intégrale" / "Doppelband" / "Perfect Edition" /
                        // "Coffret" / "Box Set" with a number or a range is a collected book, not that volume
                        // (or those volumes) of this edition. Fix round 1 (⚠2): not for a series bound to a collected
                        // line -- its own names carry the marker, and so do its releases.
                        if (editionSearch && EditionVolumeTokens.IsCollectedEdition(title) && !EditionVolumeTokens.IsCollectedSeries(author.Metadata.Value))
                        {
                            Logger.Debug("Manga release '{0}': a numbered collected edition for '{1}', rejecting", title, author.Name);
                            return null;
                        }

                        // A light-novel batch graded Unknown (no format token) takes the class its
                        // wording names, as the tokenless batch bridge below does; RSS would
                        // otherwise type it archive and drop it.
                        if (author.Library == LibraryType.LightNovel && mangaResult.Quality.Quality == Quality.Unknown)
                        {
                            var lnQuality = QualityParser.LooksLikeManga(title) ? Quality.CBZ
                                : QualityParser.LooksLikeAudiobook(title) || mediaType == MediaType.Audio ? Quality.UnknownAudio
                                : Quality.EPUB;

                            mangaResult.Quality = new QualityModel(lnQuality);
                        }

                        var bridged = mangaResult.VolumeStart.HasValue
                            ? $"pack {mangaResult.VolumeStart}-{mangaResult.VolumeEnd}"
                            : $"volume {mangaResult.VolumeNumber}";
                        Logger.Debug("Manga search bridge: '{0}' -> author '{1}', {2}", title, author.Name, bridged);
                        return mangaResult;
                    }

                    Logger.Debug("Manga release '{0}': no series candidate matched searched author '{1}' or its aliases, rejecting (no fuzzy fallback)", title, author.Name);
                    return null;
                }

                // Tokenless whole-series batch bridge: private-tracker-style batches ("That Time I Got
                // Reincarnated as a Slime [Yen Press] [Stick]", "ReZERO ... The Frozen Bond
                // (2022-2023) (Digital)") carry no volume token at all, so the bridge above can't
                // fire and they used to die as Unknown Author. When the cleaned release title IS
                // the searched series — the same exact normalized match, so unrelated series and
                // sequels ("Tokyo Ghoul - re") still reject — treat it as a whole-series pack:
                // VolumeStart 1 with an open-ended VolumeEnd that GetBooks clamps to the library's
                // volumes. Pack decisioning then accepts it only when it fills a wanted volume.
                string matchedBatchSeries = null;
                foreach (var batchCandidate in MangaVolumeParser.GetBatchSeriesCandidates(simpleTitle))
                {
                    if (IsAcceptedSeries(batchCandidate))
                    {
                        matchedBatchSeries = batchCandidate;
                        break;
                    }

                    var withoutPublisher = MangaVolumeParser.StripLeadingPublisher(batchCandidate);
                    if (withoutPublisher != null && IsAcceptedSeries(withoutPublisher))
                    {
                        matchedBatchSeries = withoutPublisher;
                        break;
                    }
                }

                if (matchedBatchSeries != null)
                {
                    var batchResult = new ParsedBookInfo
                    {
                        AuthorName = author.Name,
                        AuthorTitleInfo = GetAuthorTitleInfo(author.Name),
                        BookTitle = matchedBatchSeries,
                        Quality = QualityParser.ParseQuality(title),
                        ReleaseGroup = ParseReleaseGroup(releaseTitle),
                        VolumeStart = 1,
                        VolumeEnd = 9999
                    };

                    // Tokenless batches carry no format token. Grade by the SEARCHED entry's
                    // library (this bridge only runs with search criteria): a light novel is EPUB
                    // unless the title says audiobook (Unknown Audio, ranked by the audio profile)
                    // or manga (CBZ, which the media-type spec refuses for a light novel — D1);
                    // a manga entry keeps the CBZ default with the same ebook guard as before.
                    if (batchResult.Quality.Quality == Quality.Unknown)
                    {
                        if (author.Library == LibraryType.LightNovel)
                        {
                            var quality = QualityParser.LooksLikeManga(title) ? Quality.CBZ
                                : QualityParser.LooksLikeAudiobook(title) ? Quality.UnknownAudio
                                : Quality.EPUB;

                            batchResult.Quality = new QualityModel(quality);
                        }
                        else if (!QualityParser.LooksLikeEbook(title))
                        {
                            batchResult.Quality = new QualityModel(Quality.CBZ);
                        }
                    }

                    Logger.Debug("Manga batch bridge: '{0}' -> author '{1}', whole-series pack", title, author.Name);
                    return batchResult;
                }

                // Light-novel audio bridge (2026-09-18, D4): an Audible-named release ("Unital Ring I
                // (Sword Art Online 21) [M4B]", "Sword Art Online 21 - Unital Ring I by Reki Kawahara
                // [ENG / M4B]") carries no volume token, so neither bridge above fires and the fuzzy
                // match below dies as Unknown Author. When the search is for a light novel's Audio
                // edition and the release IS one searched volume's audiobook title, "<Series> <N>:
                // <Subtitle>" or subtitle (AudiobookTitleMatcher: exact normalised keys, one volume,
                // no fuzzy widening), attribute it to the searched series with that volume number, and
                // GetBooks maps it as any volume-tokened release. Manga and EPUB searches never enter.
                if (mediaType == MediaType.Audio && author.Library == LibraryType.LightNovel)
                {
                    var hit = AudiobookTitleMatcher.Match(releaseTitle, books, author);

                    if (hit != null)
                    {
                        var audiobookResult = new ParsedBookInfo
                        {
                            AuthorName = author.Name,
                            AuthorTitleInfo = GetAuthorTitleInfo(author.Name),
                            BookTitle = hit.Title,
                            VolumeNumber = hit.VolumeNumber,
                            ReleaseTitle = title,
                            Quality = QualityParser.ParseQuality(title),
                            ReleaseGroup = ParseReleaseGroup(releaseTitle)
                        };

                        Logger.Debug("Audiobook title bridge: '{0}' → {1}", title, hit);
                        return audiobookResult;
                    }
                }

                // Light-novel ebook bridge (2026-09-20, T2): usenet names an ebook after its WRITER
                // ("Reki.Kawahara.-.Sword.Art.Online-.Unital.Ring.I.21.(epub)", "TurtleMe - [The
                // Beginning After the End 02] - New Heights (epub)"), so the series attribution above
                // refuses it — the release's "author" is the writer, not the series — and there is no
                // v/Vol token for either volume bridge. When the search is for a light novel's ebook
                // (or is untyped) and the release IS one searched volume (EbookReleaseMatcher: the
                // writer stripped, a bracketed series unwrapped, then exact normalised keys or the
                // writer-anchored "<Series> <N>" prefix), attribute it with that volume number and
                // GetBooks maps it as any volume-tokened release. Audio searches keep the audiobook
                // bridge above; manga entries never enter.
                if (mediaType != MediaType.Audio && author.Library == LibraryType.LightNovel)
                {
                    var ebookHit = EbookReleaseMatcher.Match(releaseTitle, books, author);

                    if (ebookHit != null)
                    {
                        var ebookResult = new ParsedBookInfo
                        {
                            AuthorName = author.Name,
                            AuthorTitleInfo = GetAuthorTitleInfo(author.Name),
                            BookTitle = ebookHit.Title,
                            VolumeNumber = ebookHit.VolumeNumber,
                            ReleaseTitle = title,
                            Quality = QualityParser.ParseQuality(title),
                            ReleaseGroup = ParseReleaseGroup(releaseTitle)
                        };

                        Logger.Debug("Ebook release bridge: '{0}' → {1}", title, ebookHit);
                        return ebookResult;
                    }
                }

                // Only consider books that actually have a monitored edition. An empty book list
                // or a book with no monitored edition previously threw (Single/First), which was
                // caught and logged at Error on EVERY unparseable search — fall through to a clean
                // null instead. Use First (not Single) so multiple monitored editions don't throw.
                var candidateBooks = books
                    .Where(b => b.Editions?.Value != null && b.Editions.Value.Any(e => e.Monitored))
                    .ToList();

                if (candidateBooks.Count == 0)
                {
                    Logger.Debug("No monitored editions to match '{0}' against", title);
                    return null;
                }

                var bestBook = candidateBooks
                    .OrderByDescending(x => simpleTitle.FuzzyMatch(x.Editions.Value.First(x => x.Monitored).Title, wordDelimiters: WordDelimiters))
                    .First()
                    .Editions.Value
                    .First(x => x.Monitored);

                var foundAuthor = GetTitleFuzzy(simpleTitle, authorName, out var remainder);

                if (foundAuthor == null)
                {
                    foundAuthor = GetTitleFuzzy(simpleTitle, authorName.ToLastFirst(), out remainder);
                }

                var foundBook = GetTitleFuzzy(remainder, bestBook.Title, out _);

                if (foundBook == null)
                {
                    foundBook = GetTitleFuzzy(remainder, bestBook.Title.SplitBookTitle(authorName).Item1, out _);
                }

                Logger.Trace($"Found {foundAuthor} - {foundBook} with fuzzy parser");

                if (foundAuthor == null || foundBook == null)
                {
                    return null;
                }

                var result = new ParsedBookInfo
                {
                    AuthorName = foundAuthor,
                    AuthorTitleInfo = GetAuthorTitleInfo(foundAuthor),
                    BookTitle = foundBook
                };

                try
                {
                    result.Quality = QualityParser.ParseQuality(title);
                    Logger.Debug("Quality parsed: {0}", result.Quality);

                    result.ReleaseGroup = ParseReleaseGroup(releaseTitle);

                    Logger.Debug("Release Group parsed: {0}", result.ReleaseGroup);

                    return result;
                }
                catch (InvalidDateException ex)
                {
                    Logger.Debug(ex, ex.Message);
                }
            }
            catch (Exception e)
            {
                if (!title.ToLower().Contains("password") && !title.ToLower().Contains("yenc"))
                {
                    Logger.Error(e, "An error has occurred while trying to parse {0}", title);
                }
            }

            Logger.Debug("Unable to parse {0}", title);
            return null;
        }

        public static string GetTitleFuzzy(string report, string name, out string remainder)
        {
            remainder = report;

            Logger.Trace($"Finding '{name}' in '{report}'");

            var (locStart, matchLength, score) = report.ToLowerInvariant().FuzzyMatch(name.ToLowerInvariant(), 0.6, WordDelimiters);

            if (locStart == -1)
            {
                return null;
            }

            var found = report.Substring(locStart, matchLength);

            if (score >= 0.8)
            {
                remainder = report.Remove(locStart, matchLength);
                return found.Replace('.', ' ').Replace('_', ' ');
            }

            return null;
        }

        public static ParsedBookInfo ParseBookTitle(string title)
        {
            try
            {
                if (!ValidateBeforeParsing(title))
                {
                    return null;
                }

                Logger.Debug("Parsing string '{0}'", title);

                var releaseTitle = RemoveFileExtension(title);

                var simpleTitle = SimpleTitleRegex.Replace(releaseTitle);

                // TODO: Quick fix stripping [url] - prefixes.
                simpleTitle = WebsitePrefixRegex.Replace(simpleTitle);
                simpleTitle = WebsitePostfixRegex.Replace(simpleTitle);

                simpleTitle = CleanTorrentSuffixRegex.Replace(simpleTitle);

                var airDateMatch = AirDateRegex.Match(simpleTitle);
                if (airDateMatch.Success)
                {
                    simpleTitle = airDateMatch.Groups[1].Value + airDateMatch.Groups["airyear"].Value + "." + airDateMatch.Groups["airmonth"].Value + "." + airDateMatch.Groups["airday"].Value;
                }

                var sixDigitAirDateMatch = SixDigitAirDateRegex.Match(simpleTitle);
                if (sixDigitAirDateMatch.Success)
                {
                    var airYear = sixDigitAirDateMatch.Groups["airyear"].Value;
                    var airMonth = sixDigitAirDateMatch.Groups["airmonth"].Value;
                    var airDay = sixDigitAirDateMatch.Groups["airday"].Value;

                    if (airMonth != "00" || airDay != "00")
                    {
                        var fixedDate = string.Format("20{0}.{1}.{2}", airYear, airMonth, airDay);

                        simpleTitle = simpleTitle.Replace(sixDigitAirDateMatch.Groups["airdate"].Value, fixedDate);
                    }
                }

                foreach (var regex in ReportBookTitleRegex)
                {
                    var match = regex.Matches(simpleTitle);

                    if (match.Count != 0)
                    {
                        Logger.Trace(regex);
                        try
                        {
                            var result = ParseBookMatchCollection(match, releaseTitle);

                            if (result != null)
                            {
                                result.Quality = QualityParser.ParseQuality(title);
                                Logger.Debug("Quality parsed: {0}", result.Quality);

                                result.ReleaseGroup = ParseReleaseGroup(releaseTitle);

                                var subGroup = GetSubGroup(match);
                                if (!subGroup.IsNullOrWhiteSpace())
                                {
                                    result.ReleaseGroup = subGroup;
                                }

                                Logger.Debug("Release Group parsed: {0}", result.ReleaseGroup);

                                result.ReleaseHash = GetReleaseHash(match);
                                if (!result.ReleaseHash.IsNullOrWhiteSpace())
                                {
                                    Logger.Debug("Release Hash parsed: {0}", result.ReleaseHash);
                                }

                                // Phase 3: detect manga volume number / pack range (nullable; no-op for non-manga).
                                MangaVolumeParser.ParseVolume(releaseTitle, result);

                                return result;
                            }
                        }
                        catch (InvalidDateException ex)
                        {
                            Logger.Debug(ex, ex.Message);
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (!title.ToLower().Contains("password") && !title.ToLower().Contains("yenc"))
                {
                    Logger.Error(e, "An error has occurred while trying to parse {0}", title);
                }
            }

            Logger.Debug("Unable to parse {0}", title);
            return null;
        }

        public static (string, string) SplitBookTitle(this string book, string author)
        {
            // Strip author from title, eg Tom Clancy: Ghost Protocol
            if (book.StartsWith($"{author}:"))
            {
                book = book.Split(':', 2)[1].Trim();
            }

            var parenthesis = book.IndexOf('(');
            var colon = book.IndexOf(':');

            string[] parts = null;

            if (parenthesis > -1)
            {
                var endParenthesis = book.IndexOf(')', parenthesis);
                if (endParenthesis == -1 || !book.Substring(parenthesis + 1, endParenthesis - parenthesis).Contains(' '))
                {
                    parenthesis = -1;
                }
            }

            if (colon > -1 && parenthesis > -1)
            {
                if (colon < parenthesis)
                {
                    parts = book.Split(':', 2);
                }
                else
                {
                    parts = book.Split('(', 2);
                    parts[1] = parts[1].TrimEnd(')');
                }
            }
            else if (colon > -1)
            {
                parts = book.Split(':', 2);
            }
            else if (parenthesis > -1)
            {
                parts = book.Split('(');
                parts[1] = parts[1].TrimEnd(')');
            }

            if (parts != null)
            {
                return (parts[0].Trim(), parts[1].TrimEnd(':').Trim());
            }

            return (book, string.Empty);
        }

        public static string CleanAuthorName(this string name)
        {
            if (name.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            // If Title only contains numbers return it as is.
            if (long.TryParse(name, out _))
            {
                return name;
            }

            name = PercentRegex.Replace(name, "percent");

            return NormalizeRegex.Replace(name).ToLower().RemoveAccent();
        }

        public static string NormalizeTrackTitle(this string title)
        {
            title = SpecialEpisodeWordRegex.Replace(title, string.Empty);
            title = PunctuationRegex.Replace(title, " ");
            title = DuplicateSpacesRegex.Replace(title, " ");

            return title.Trim().ToLower();
        }

        public static string NormalizeTitle(string title)
        {
            title = WordDelimiterRegex.Replace(title, " ");
            title = PunctuationRegex.Replace(title, string.Empty);
            title = CommonWordRegex.Replace(title, string.Empty);
            title = DuplicateSpacesRegex.Replace(title, " ");

            return title.Trim().ToLower();
        }

        public static string ParseReleaseGroup(string title)
        {
            title = title.Trim();
            title = RemoveFileExtension(title);
            title = WebsitePrefixRegex.Replace(title);

            var animeMatch = AnimeReleaseGroupRegex.Match(title);

            if (animeMatch.Success)
            {
                return animeMatch.Groups["subgroup"].Value;
            }

            title = CleanReleaseGroupRegex.Replace(title);

            var matches = ReleaseGroupRegex.Matches(title);

            if (matches.Count != 0)
            {
                var group = matches.OfType<Match>().Last().Groups["releasegroup"].Value;

                if (int.TryParse(group, out _))
                {
                    return null;
                }

                return group;
            }

            return null;
        }

        public static string RemoveFileExtension(string title)
        {
            title = FileExtensionRegex.Replace(title, m =>
            {
                var extension = m.Value.ToLower();
                if (MediaFiles.MediaFileExtensions.AllExtensions.Contains(extension) || new[] { ".par2", ".nzb" }.Contains(extension))
                {
                    return string.Empty;
                }

                return m.Value;
            });

            return title;
        }

        public static string CleanBookTitle(this string book)
        {
            return CommonTagRegex[1].Replace(book, string.Empty).Trim();
        }

        public static string RemoveBracketsAndContents(this string book)
        {
            var intermediate = book;
            foreach (var regex in BracketRegex)
            {
                intermediate = regex.Replace(intermediate, string.Empty).Trim();
            }

            return intermediate;
        }

        public static string RemoveAfterDash(this string text)
        {
            return AfterDashRegex.Replace(text, string.Empty).Trim();
        }

        public static string CleanTrackTitle(this string title)
        {
            var intermediateTitle = title;
            foreach (var regex in CommonTagRegex)
            {
                intermediateTitle = regex.Replace(intermediateTitle, string.Empty).Trim();
            }

            return intermediateTitle;
        }

        private static ParsedTrackInfo ParseMatchMusicCollection(MatchCollection matchCollection)
        {
            var authorName = matchCollection[0].Groups["author"].Value./*Removed for cases like Will.I.Am Replace('.', ' ').*/Replace('_', ' ');
            authorName = RequestInfoRegex.Replace(authorName, "").Trim(' ');

            // Coppied from Radarr (https://github.com/Radarr/Radarr/blob/develop/src/NzbDrone.Core/Parser/Parser.cs)
            // TODO: Split into separate method and write unit tests for.
            var parts = authorName.Split('.');
            authorName = "";
            var n = 0;
            var previousAcronym = false;
            var nextPart = "";
            foreach (var part in parts)
            {
                if (parts.Length >= n + 2)
                {
                    nextPart = parts[n + 1];
                }

                if (part.Length == 1 && part.ToLower() != "a" && !int.TryParse(part, out n))
                {
                    authorName += part + ".";
                    previousAcronym = true;
                }
                else if (part.ToLower() == "a" && (previousAcronym == true || nextPart.Length == 1))
                {
                    authorName += part + ".";
                    previousAcronym = true;
                }
                else
                {
                    if (previousAcronym)
                    {
                        authorName += " ";
                        previousAcronym = false;
                    }

                    authorName += part + " ";
                }

                n++;
            }

            authorName = authorName.Trim(' ');

            var result = new ParsedTrackInfo();

            result.Authors = new List<string> { authorName };

            Logger.Debug("Track Parsed. {0}", result);
            return result;
        }

        private static AuthorTitleInfo GetAuthorTitleInfo(string title)
        {
            var authorTitleInfo = new AuthorTitleInfo();
            authorTitleInfo.Title = title;

            return authorTitleInfo;
        }

        public static string ParseAuthorName(string title)
        {
            Logger.Debug("Parsing string '{0}'", title);

            var parseResult = ParseBookTitle(title);

            if (parseResult == null)
            {
                return CleanAuthorName(title);
            }

            return parseResult.AuthorName;
        }

        private static ParsedBookInfo ParseBookMatchCollection(MatchCollection matchCollection, string releaseTitle)
        {
            var authorName = matchCollection[0].Groups["author"].Value.Replace('.', ' ').Replace('_', ' ');
            var bookTitle = matchCollection[0].Groups["book"].Value.Replace('.', ' ').Replace('_', ' ');
            var releaseVersion = matchCollection[0].Groups["version"].Value.Replace('.', ' ').Replace('_', ' ');
            authorName = RequestInfoRegex.Replace(authorName, "").Trim(' ');
            bookTitle = RequestInfoRegex.Replace(bookTitle, "").Trim(' ');
            releaseVersion = RequestInfoRegex.Replace(releaseVersion, "").Trim(' ');

            int.TryParse(matchCollection[0].Groups["releaseyear"].Value, out var releaseYear);

            ParsedBookInfo result;

            result = new ParsedBookInfo
            {
                ReleaseTitle = releaseTitle
            };

            result.AuthorName = authorName;
            result.BookTitle = bookTitle;
            result.AuthorTitleInfo = GetAuthorTitleInfo(result.AuthorName);
            result.ReleaseDate = releaseYear.ToString();
            result.ReleaseVersion = releaseVersion;

            if (matchCollection[0].Groups["discography"].Success)
            {
                int.TryParse(matchCollection[0].Groups["startyear"].Value, out var discStart);
                int.TryParse(matchCollection[0].Groups["endyear"].Value, out var discEnd);
                result.Discography = true;

                if (discStart > 0 && discEnd > 0)
                {
                    result.DiscographyStart = discStart;
                    result.DiscographyEnd = discEnd;
                }
                else if (discEnd > 0)
                {
                    result.DiscographyEnd = discEnd;
                }

                result.BookTitle = "Discography";
            }

            Logger.Debug("Book Parsed. {0}", result);

            return result;
        }

        private static bool ValidateBeforeParsing(string title)
        {
            if (title.ToLower().Contains("password") && title.ToLower().Contains("yenc"))
            {
                Logger.Debug("");
                return false;
            }

            if (!title.Any(char.IsLetterOrDigit))
            {
                return false;
            }

            var titleWithoutExtension = RemoveFileExtension(title);

            if (RejectHashedReleasesRegex.Any(v => v.IsMatch(titleWithoutExtension)))
            {
                Logger.Debug("Rejected Hashed Release Title: " + title);
                return false;
            }

            return true;
        }

        private static string GetSubGroup(MatchCollection matchCollection)
        {
            var subGroup = matchCollection[0].Groups["subgroup"];

            if (subGroup.Success)
            {
                return subGroup.Value;
            }

            return string.Empty;
        }

        private static string GetReleaseHash(MatchCollection matchCollection)
        {
            var hash = matchCollection[0].Groups["hash"];

            if (hash.Success)
            {
                var hashValue = hash.Value.Trim('[', ']');

                if (hashValue.Equals("1280x720"))
                {
                    return string.Empty;
                }

                return hashValue;
            }

            return string.Empty;
        }

        private static int ParseNumber(string value)
        {
            if (int.TryParse(value, out var number))
            {
                return number;
            }

            number = Array.IndexOf(Numbers, value.ToLower());

            if (number != -1)
            {
                return number;
            }

            throw new FormatException(string.Format("{0} isn't a number", value));
        }
    }
}
