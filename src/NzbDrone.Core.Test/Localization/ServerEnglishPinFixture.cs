using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Localization;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Update;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.Localization
{
    // Server messages (2026-09-26): every call site converted from interpolation, concatenation or a built
    // string renders, as a template, the English the old code rendered for the same values (plan step P7).
    // The expected strings are written from the OLD code by hand, never from ServerText.
    [TestFixture]
    public class ServerEnglishPinFixture
    {
        [TestCase("Indexer {0} is blocked till {1} due to failures, cannot grab release.", new object[] { "Nyaa", "9/26/2026 10:00:00 AM" }, "Indexer Nyaa is blocked till 9/26/2026 10:00:00 AM due to failures, cannot grab release.")]
        [TestCase("No {0} language marker on this release (the series is the {0} edition)", new object[] { "French" }, "No French language marker on this release (the series is the French edition)")]
        [TestCase("Release is {0}, the series is the {1} edition", new object[] { "English", "French" }, "Release is English, the series is the French edition")]
        [TestCase("Release is {0}, the series is the {1} edition", new object[] { "French/German", "English" }, "Release is French/German, the series is the English edition")]
        [TestCase("{0} is too big, maximum size is {1} (Settings->Indexers->Maximum Size)", new object[] { "2.0 GB", "1.0 GB" }, "2.0 GB is too big, maximum size is 1.0 GB (Settings->Indexers->Maximum Size)")]
        [TestCase("Release published date, {0}, is outside of {1} day early grab limit allowed by user", new object[] { "9/26/2026", 3 }, "Release published date, 9/26/2026, is outside of 3 day early grab limit allowed by user")]
        [TestCase("{0}: {1}", new object[] { "AcceptableSizeSpecification", "boom" }, "AcceptableSizeSpecification: boom")]
        [TestCase("Chapter-numbered rip: {0} of {1} comic files are per-chapter, no volume files", new object[] { 118, 120 }, "Chapter-numbered rip: 118 of 120 comic files are per-chapter, no volume files")]
        [TestCase("Download contains only ebook files, which no monitored edition of the grabbed volume(s) can hold", new object[0], "Download contains only ebook files, which no monitored edition of the grabbed volume(s) can hold")]
        [TestCase("Download contains only audiobook files, which no monitored edition of the grabbed volume(s) can hold", new object[0], "Download contains only audiobook files, which no monitored edition of the grabbed volume(s) can hold")]
        [TestCase("Download contains only archive files, which no monitored edition of the grabbed volume(s) can hold", new object[0], "Download contains only archive files, which no monitored edition of the grabbed volume(s) can hold")]
        [TestCase("Couldn't find similar volume for {0}", new object[] { "[/downloads/Series v01]" }, "Couldn't find similar volume for [/downloads/Series v01]")]
        [TestCase("Couldn't parse volume from: {0}", new object[] { "Author - Title - 0:[Unknown Track] T: EPUB" }, "Couldn't parse volume from: Author - Title - 0:[Unknown Track] T: EPUB")]
        [TestCase("{0}: {1}", new object[] { "FreeSpaceSpecification", "boom" }, "FreeSpaceSpecification: boom")]
        [TestCase("Destination series folder {0} is not in a Root Folder", new object[] { "/manga/Series" }, "Destination series folder /manga/Series is not in a Root Folder")]
        [TestCase("Destination folder {0} is not in a Root Folder", new object[] { "/manga/Series" }, "Destination folder /manga/Series is not in a Root Folder")]
        [TestCase("Book already imported at {0}", new object[] { "9/26/2026 10:00:00 AM" }, "Book already imported at 9/26/2026 10:00:00 AM")]
        // Plan E37 (final review 2026-09-27): the corrected English; the old code said "Unknown Author for file: {0}".
        [TestCase("Unknown Series for file: {0}", new object[] { "Series v01.cbz" }, "Unknown Series for file: Series v01.cbz")]
        [TestCase("This series has already been added as '{0}'", new object[] { "Rascal Does Not Dream" }, "This series has already been added as 'Rascal Does Not Dream'")]
        [TestCase("This series has already been added as '{0}'", new object[] { "Odd {Name}" }, "This series has already been added as 'Odd {Name}'")]
        [TestCase("No {0} edition of this series in the catalogue", new object[] { "Portuguese (Brazil)" }, "No Portuguese (Brazil) edition of this series in the catalogue")]
        // i18n leftovers (2026-09-28): "Already the {0} edition", one row per converted site -- English/English
        // no-op (EditionPreviewService.cs:213-214), same-edition not English (EditionPreviewService.cs:228),
        // rename-only without rename (ReResolveEditionService.cs:126, log-only).
        [TestCase("Already the {0} edition", new object[] { "English" }, "Already the English edition")]
        [TestCase("Already the {0} edition", new object[] { "French" }, "Already the French edition")]
        [TestCase("Already the {0} edition", new object[] { "Japanese" }, "Already the Japanese edition")]
        public void a_converted_call_site_renders_the_old_english(string template, object[] args, string english)
        {
            new ServerText(template, args).English.Should().Be(english);
        }

        // Task 4 command results (SetResultMessage): ReResolveEditionService, FlipPageOrderService (both
        // results), ConvertLightNovelFormatService (both branches of the old optional ", {n} skipped (PDF only)"
        // tail) and PdfConversionService.FormatResult. The expected strings match the fixtures that pin
        // command.ResultMessage for the same services.
        [TestCase("{0} changed, {1} blocked, {2} failed", new object[] { 1, 0, 1 }, "1 changed, 0 blocked, 1 failed")]
        [TestCase("{0} is not a CBZ archive; page order not flipped", new object[] { "Series - Vol 012" }, "Series - Vol 012 is not a CBZ archive; page order not flipped")]
        [TestCase("Page order flipped for {0}", new object[] { "Series - Vol 012" }, "Page order flipped for Series - Vol 012")]
        [TestCase("Converted {0} book(s) to {1}, {2} already had it, {3} failed", new object[] { 2, "KEPUB", 1, 0 }, "Converted 2 book(s) to KEPUB, 1 already had it, 0 failed")]
        [TestCase("Converted {0} book(s) to {1}, {2} already had it, {3} skipped (PDF only), {4} failed", new object[] { 0, "AZW3", 0, 1, 0 }, "Converted 0 book(s) to AZW3, 0 already had it, 1 skipped (PDF only), 0 failed")]
        [TestCase("Converted {0} PDF volume(s) to CBZ, {1} failed", new object[] { 3, 1 }, "Converted 3 PDF volume(s) to CBZ, 1 failed")]
        public void a_converted_command_result_renders_the_old_english(string template, object[] args, string english)
        {
            new ServerText(template, args).English.Should().Be(english);
        }

        // Task 4 progress lines: they are log lines too, so both the carrier's English (ProgressMessageTarget
        // builds it from FormattedMessage) and NLog's own rendering of the new template must equal the old
        // text. Old code: RssSyncService's string.Format + ", Reports pending: " + n tail; ImportListSyncService's
        // $"Starting Import List Refresh for List {definition.Name}"; ImportDecisionMaker's
        // $"Reading file {i++}/{files.Count}"; TrackGroupingService's $"Grouping {localTracks.Count} tracks".
        // The two E-changed rows expect the corrected English (plan E24, E26): IdentificationService's
        // $"Identifying book {i}/{releases.Count}" and ImportListSyncService's "…Authors added: {1}, Books added: {2}".
        [TestCase("RSS Sync Completed. Reports found: {0}, Reports grabbed: {1}", new object[] { 12, 2 }, "RSS Sync Completed. Reports found: 12, Reports grabbed: 2")]
        [TestCase("RSS Sync Completed. Reports found: {0}, Reports grabbed: {1}, Reports pending: {2}", new object[] { 12, 2, 1 }, "RSS Sync Completed. Reports found: 12, Reports grabbed: 2, Reports pending: 1")]
        [TestCase("Starting Import List Refresh for List {0}", new object[] { "Goodreads {Shelf}" }, "Starting Import List Refresh for List Goodreads {Shelf}")]
        [TestCase("Reading file {0}/{1}", new object[] { 1, 4 }, "Reading file 1/4")]
        [TestCase("Grouping {0} tracks", new object[] { 4 }, "Grouping 4 tracks")]
        [TestCase("Identifying volume {0}/{1}", new object[] { 1, 3 }, "Identifying volume 1/3")]
        [TestCase("Import List Sync Completed. Items found: {0}, Series added: {1}, Volumes added: {2}", new object[] { 5, 2, 3 }, "Import List Sync Completed. Items found: 5, Series added: 2, Volumes added: 3")]
        // Fix round 1: enum values rendered into the old template become literal words, one template per value
        // (old: "One {0} series search ...", type; "... using mode {1}", ImportMode; "... please use {0} to install",
        // UpdateMechanism.External). Each row renders what the enum's ToString() produced.
        [TestCase("One Audio series search for {0} volumes of {1}", new object[] { 3, "Rascal" }, "One Audio series search for 3 volumes of Rascal")]
        [TestCase("One Ebook series search for {0} volumes of {1}", new object[] { 3, "Rascal" }, "One Ebook series search for 3 volumes of Rascal")]
        [TestCase("One Archive series search for {0} volumes of {1}", new object[] { 3, "Rascal" }, "One Archive series search for 3 volumes of Rascal")]
        [TestCase("Manually importing {0} files using mode Auto", new object[] { 2 }, "Manually importing 2 files using mode Auto")]
        [TestCase("Manually importing {0} files using mode Move", new object[] { 2 }, "Manually importing 2 files using mode Move")]
        [TestCase("Manually importing {0} files using mode Copy", new object[] { 2 }, "Manually importing 2 files using mode Copy")]
        [TestCase("Built-In updater disabled, please use External to install", new object[0], "Built-In updater disabled, please use External to install")]
        [TestCase("Update available, please use External to install", new object[0], "Update available, please use External to install")]
        public void a_converted_progress_line_logs_the_old_english(string template, object[] args, string english)
        {
            new ServerText(template, args).English.Should().Be(english);
            new LogEventInfo(LogLevel.Info, "ServerEnglishPin", null, template, args).FormattedMessage.Should().Be(english);
        }

        // Fix round 1: the literal per-value templates above equal the old template rendered with the enum itself.
        [TestCase("One Audio series search for {0} volumes of {1}", "One {0} series search for {1} volumes of {2}", MediaType.Audio)]
        [TestCase("One Ebook series search for {0} volumes of {1}", "One {0} series search for {1} volumes of {2}", MediaType.Ebook)]
        [TestCase("One Archive series search for {0} volumes of {1}", "One {0} series search for {1} volumes of {2}", MediaType.Archive)]
        public void a_media_type_literal_matches_the_old_enum_rendering(string literal, string old, MediaType type)
        {
            new LogEventInfo(LogLevel.Info, "ServerEnglishPin", null, literal, new object[] { 3, "Rascal" }).FormattedMessage
                .Should().Be(new LogEventInfo(LogLevel.Info, "ServerEnglishPin", null, old, new object[] { type, 3, "Rascal" }).FormattedMessage);
        }

        [TestCase("Manually importing {0} files using mode Auto", ImportMode.Auto)]
        [TestCase("Manually importing {0} files using mode Move", ImportMode.Move)]
        [TestCase("Manually importing {0} files using mode Copy", ImportMode.Copy)]
        public void an_import_mode_literal_matches_the_old_enum_rendering(string literal, ImportMode mode)
        {
            new LogEventInfo(LogLevel.Trace, "ServerEnglishPin", null, literal, new object[] { 2 }).FormattedMessage
                .Should().Be(new LogEventInfo(LogLevel.Trace, "ServerEnglishPin", null, "Manually importing {0} files using mode {1}", new object[] { 2, mode }).FormattedMessage);
        }

        [TestCase("Built-In updater disabled, please use External to install", "Built-In updater disabled, please use {0} to install")]
        [TestCase("Update available, please use External to install", "Update available, please use {0} to install")]
        public void the_external_update_literal_matches_the_old_enum_rendering(string literal, string old)
        {
            new LogEventInfo(LogLevel.Debug, "ServerEnglishPin", null, literal, new object[0]).FormattedMessage
                .Should().Be(new LogEventInfo(LogLevel.Debug, "ServerEnglishPin", null, old, new object[] { UpdateMechanism.External }).FormattedMessage);
        }

        // CloseBookMatchSpecification (E17): the percentages keep their P1/P0 formats (old:
        // $"Book match is not close enough: {1 - dist:P1} vs {1 - _bookThreshold:P0} {reasons}").
        [Test]
        [SetCulture("en-US")]
        public void close_match_percentages_render_the_old_english()
        {
            new ServerText("Volume match is not close enough: {0:P1} vs {1:P0} {2}", 1 - 0.25, 1 - 0.20, "[title: 0.5]").English
                .Should().Be("Volume match is not close enough: 75.0% vs 80% [title: 0.5]");
        }

        // AlreadyImportedSpecification: the inlined literal is still RejectionMessagePrefix + " at {0}", so
        // IsAlreadyImportedRejection's StartsWith keeps matching it.
        [Test]
        public void already_imported_template_starts_with_the_rejection_prefix()
        {
            var english = new ServerText("Book already imported at {0}", "9/26/2026 10:00:00 AM").English;

            english.Should().Be(AlreadyImportedSpecification.RejectionMessagePrefix + " at 9/26/2026 10:00:00 AM");
            AlreadyImportedSpecification.IsAlreadyImportedRejection(english).Should().BeTrue();
        }

        // EditionLanguageSpecification passes its language names as nested carriers: they render their own
        // English inside the outer message (old: $"Release is {found}, the series is the English edition").
        [Test]
        public void nested_language_names_render_the_old_english()
        {
            new ServerText("Release is {0}, the series is the {1} edition", new ServerText("French"), new ServerText("English")).English
                .Should().Be("Release is French, the series is the English edition");

            new ServerText("No {0} language marker on this release (the series is the {0} edition)", new ServerText("Japanese")).English
                .Should().Be("No Japanese language marker on this release (the series is the Japanese edition)");
        }

        // Task 5 AddAuthorService/AddBookService: the edition-unavailable language name travels as a nested
        // carrier (old: $"No {EditionLanguages.Name(ex.Language)} edition of this series in the catalogue").
        [Test]
        public void a_nested_edition_language_renders_the_old_english()
        {
            new ServerText("No {0} edition of this series in the catalogue", new ServerText(EditionLanguages.Name("fr"))).English
                .Should().Be("No French edition of this series in the catalogue");
        }

        // Task 5 rule messages (old: $"Under {seedRatioMinimum} leads to H&R", the same for the two int seed
        // times, and $"Must be a valid URL path (ie: '{example}')"): ServerRuleMessages.Format keeps the text.
        [TestCase("Under {0} leads to H&R", 1.5, "Under 1.5 leads to H&R")]
        [TestCase("Under {0} leads to H&R", 72, "Under 72 leads to H&R")]
        [TestCase("Must be a valid URL path (ie: '{0}')", "/readarr", "Must be a valid URL path (ie: '/readarr')")]
        [TestCase("Must be a valid URL path (ie: '{0}')", "/api", "Must be a valid URL path (ie: '/api')")]
        [SetCulture("en-US")]
        public void a_rule_message_renders_the_old_english(string template, object arg, string english)
        {
            ServerRuleMessages.Format(template, arg).Should().Be(english);
            ServerRuleMessages.Find(english).Template.Should().Be(template);
        }

        // The same, through the real rules and FluentValidation's own message rendering.
        [Test]
        [SetCulture("en-US")]
        public void seed_criteria_rule_messages_render_the_old_english()
        {
            var failures = new SeedCriteriaSettingsValidator(1.5, 72, 120)
                .Validate(new SeedCriteriaSettings { SeedRatio = 1.0, SeedTime = 10, DiscographySeedTime = 10 }).Errors;

            failures.Select(f => f.ErrorMessage).Should().Equal("Under 1.5 leads to H&R", "Under 72 leads to H&R", "Under 120 leads to H&R");
            failures.Select(f => ServerRuleMessages.Find(f.ErrorMessage)?.Template).Should().OnlyContain(t => t == "Under {0} leads to H&R");
        }

        [TestCase(null, "Must be a valid URL path (ie: '/readarr')")]
        [TestCase("/api", "Must be a valid URL path (ie: '/api')")]
        public void url_base_rule_messages_render_the_old_english(string example, string english)
        {
            var validator = new InlineValidator<UrlBaseModel>();

            if (example == null)
            {
                validator.RuleFor(m => m.UrlBase).ValidUrlBase();
            }
            else
            {
                validator.RuleFor(m => m.UrlBase).ValidUrlBase(example);
            }

            var failure = validator.Validate(new UrlBaseModel { UrlBase = "http://example.com" }).Errors.Single();

            failure.ErrorMessage.Should().Be(english);
            ServerRuleMessages.Find(english).Should().NotBeNull();
        }

        // Task 5 target-typed failures made explicit: a failure built from a ServerText (attempted value set by
        // initializer) serializes, in the List<ValidationFailure> ReadarrErrorPipeline writes, exactly like the
        // old ValidationFailure.
        [Test]
        public void a_server_text_failure_serializes_like_the_old_failure()
        {
            var old = new List<ValidationFailure>
            {
                new ValidationFailure("Name", "This series has already been added as 'Rascal'"),
                new ValidationFailure("AddOptions.EditionLanguage", "No French edition of this series in the catalogue", "fr")
            };
            var converted = new List<ValidationFailure>
            {
                new NzbDroneValidationFailure("Name", new ServerText("This series has already been added as '{0}'", "Rascal")),
                new NzbDroneValidationFailure("AddOptions.EditionLanguage", new ServerText("No {0} edition of this series in the catalogue", new ServerText("French")))
                {
                    AttemptedValue = "fr"
                }
            };

            STJson.ToJson(converted).Should().Be(STJson.ToJson(old));
        }

        // Task 6 download client Test() failures. Old code: "Test was aborted due to an error: " + ex.Message
        // (DownloadClientBase), "Unknown exception: " + ex.Message and $"Unknown exception: {ex.Message}" (QBittorrent,
        // uTorrent, Deluge, TransmissionBase, both DownloadStation clients), the torrent/NZB list failures in both
        // forms (+ Hadouken, rTorrent), Sabnzbd's "Unknown Version: " + rawVersion and "Version 0.7.0+ is required,
        // but found: " + version, and DownloadStation's $"…It supports from {info.MinVersion} to {info.MaxVersion}"
        // (ints). Exception texts with braces prove they stay arguments, not template.
        [TestCase("Test was aborted due to an error: {0}", new object[] { "Object reference not set to an instance of an object." }, "Test was aborted due to an error: Object reference not set to an instance of an object.")]
        [TestCase("Unknown exception: {0}", new object[] { "Unexpected '{' at 3" }, "Unknown exception: Unexpected '{' at 3")]
        [TestCase("Unknown exception: {0}", new object[] { null }, "Unknown exception: ")]
        [TestCase("Failed to get the list of torrents: {0}", new object[] { "{\"error\":\"Forbidden\"}" }, "Failed to get the list of torrents: {\"error\":\"Forbidden\"}")]
        [TestCase("Failed to get the list of NZBs: {0}", new object[] { "The operation has timed out." }, "Failed to get the list of NZBs: The operation has timed out.")]
        [TestCase("Unknown Version: {0}", new object[] { "4.x-beta" }, "Unknown Version: 4.x-beta")]
        [TestCase("Version 0.7.0+ is required, but found: {0}", new object[] { "0.6.9" }, "Version 0.7.0+ is required, but found: 0.6.9")]
        [TestCase("Download Station API version not supported, should be at least 2. It supports from {0} to {1}", new object[] { 3, 5 }, "Download Station API version not supported, should be at least 2. It supports from 3 to 5")]
        public void a_converted_test_failure_renders_the_old_english(string template, object[] args, string english)
        {
            new NzbDroneValidationFailure(string.Empty, new ServerText(template, args)).ErrorMessage.Should().Be(english);
        }

        // Sabnzbd's version is a System.Version: concatenation and string.Format both call its ToString().
        [Test]
        public void the_sabnzbd_version_renders_the_old_english()
        {
            var version = new System.Version(0, 6, 9);

            new ServerText("Version 0.7.0+ is required, but found: {0}", version).English.Should().Be("Version 0.7.0+ is required, but found: " + version);
        }

        // Aria2/rTorrent (plan E33): the old ValidationFailure's third argument was the attempted value, not a
        // format argument, so the old English showed a literal "{0}". The version is now formatted in, and the
        // attempted value is kept.
        [TestCase("Aria2 version should be at least 1.34.0. Version reported is {0}", "1.33.0", "Aria2 version should be at least 1.34.0. Version reported is 1.33.0")]
        [TestCase("rTorrent version should be at least 0.9.0. Version reported is {0}", "0.8.9", "rTorrent version should be at least 0.9.0. Version reported is 0.8.9")]
        public void a_version_failure_shows_the_reported_version(string template, string version, string english)
        {
            var converted = new NzbDroneValidationFailure(string.Empty, new ServerText(template, version))
            {
                AttemptedValue = version
            };

            converted.ErrorMessage.Should().Be(english);
            converted.AttemptedValue.Should().Be(version);
        }

        // Sabnzbd "Check before download" (plan E34): the transposed "Sabnbzd" is corrected to "Sabnzbd", the
        // spelling of the same failure's DetailedDescription.
        [Test]
        public void the_sabnzbd_check_before_download_failure_spells_sabnzbd()
        {
            var en = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(System.IO.Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Localization/Core/en.json")));

            en["ServerTestSabnzbdDisableCheckBeforeDownload"].Should().Be("Disable 'Check before download' option in Sabnzbd");
            System.IO.File.ReadAllText(System.IO.Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Download/Clients/Sabnzbd/Sabnzbd.cs"))
                .Should().Contain("\"Disable 'Check before download' option in Sabnzbd\"").And.NotContain("Sabnbzd");
        }

        // Task 7 indexer, import list, notification and calibre Test() failures. Old code, one row per template:
        // "Test was aborted due to an error: " + ex.Message (IndexerBase, ImportListBase); "Unknown exception: " +
        // ex.Message (CalibreProxy); HttpIndexerBase's nine "…" + ex.Message / webException.Message failures (the
        // server-unavailable one twice, and the connection-failure one with no space before the message);
        // HttpImportListBase's two; $"Goodreads authentication error: {ex.Message}" (import list and notification);
        // ReadarrV1Proxy's $"…import list: {ex.Message}. Check…" (twice); Apprise's four; Signal's four
        // $"Unable to send test message: {…}" (ex.Message three times, error.Error once). Plan E35 (fix round
        // 2026-09-27): the connection-failure message gains the space before the exception text it lacked.
        [TestCase("Test was aborted due to an error: {0}", new object[] { "Indexer returned 500" }, "Test was aborted due to an error: Indexer returned 500")]
        [TestCase("Unknown exception: {0}", new object[] { "The remote name could not be resolved: 'calibre'" }, "Unknown exception: The remote name could not be resolved: 'calibre'")]
        [TestCase("Request limit reached: {0}", new object[] { "API limit {100/day} reached" }, "Request limit reached: API limit {100/day} reached")]
        [TestCase("Indexer feed is not supported: {0}", new object[] { "Unexpected '<' at 1" }, "Indexer feed is not supported: Unexpected '<' at 1")]
        [TestCase("Unable to connect to indexer. {0}", new object[] { "Invalid XML" }, "Unable to connect to indexer. Invalid XML")]
        [TestCase("Unable to connect to indexer, indexer's server is unavailable. Try again later. {0}", new object[] { "HTTP request failed: [503:ServiceUnavailable]" }, "Unable to connect to indexer, indexer's server is unavailable. Try again later. HTTP request failed: [503:ServiceUnavailable]")]
        [TestCase("Unable to connect to indexer, invalid credentials. {0}", new object[] { "HTTP request failed: [401:Unauthorized]" }, "Unable to connect to indexer, invalid credentials. HTTP request failed: [401:Unauthorized]")]
        [TestCase("Unable to connect to indexer, check the log above the ValidationFailure for more details. {0}", new object[] { "HTTP request failed: [400:BadRequest]" }, "Unable to connect to indexer, check the log above the ValidationFailure for more details. HTTP request failed: [400:BadRequest]")]
        [TestCase("Unable to connect to indexer, please check your DNS settings and ensure IPv6 is working or disabled. {0}", new object[] { "Name or service not known" }, "Unable to connect to indexer, please check your DNS settings and ensure IPv6 is working or disabled. Name or service not known")]
        [TestCase("Unable to connect to indexer, possibly due to a timeout. Try again or check your network settings. {0}", new object[] { "A task was canceled." }, "Unable to connect to indexer, possibly due to a timeout. Try again or check your network settings. A task was canceled.")]
        [TestCase("Unable to connect to indexer connection failure. Check your connection to the indexer's server and DNS. {0}", new object[] { "No such host is known." }, "Unable to connect to indexer connection failure. Check your connection to the indexer's server and DNS. No such host is known.")]
        [TestCase("Import list feed is not supported: {0}", new object[] { "Unexpected '<' at 1" }, "Import list feed is not supported: Unexpected '<' at 1")]
        [TestCase("Unable to connect to import list. {0}", new object[] { "Invalid response" }, "Unable to connect to import list. Invalid response")]
        [TestCase("Goodreads authentication error: {0}", new object[] { "HTTP request failed: [401:Unauthorized]" }, "Goodreads authentication error: HTTP request failed: [401:Unauthorized]")]
        [TestCase("Unable to connect to import list: {0}. Check the log surrounding this error for details.", new object[] { "HTTP request failed: [500:InternalServerError]" }, "Unable to connect to import list: HTTP request failed: [500:InternalServerError]. Check the log surrounding this error for details.")]
        [TestCase("HTTP Auth credentials are invalid: {0}", new object[] { "Apprise returned 401" }, "HTTP Auth credentials are invalid: Apprise returned 401")]
        [TestCase("Unable to send test message. Response from API: {0}", new object[] { "No {tag} matched" }, "Unable to send test message. Response from API: No {tag} matched")]
        [TestCase("Unable to connect to Apprise API. Server connection failed: ({0}) {1}", new object[] { System.Net.HttpStatusCode.BadGateway, "Apprise returned 502" }, "Unable to connect to Apprise API. Server connection failed: (BadGateway) Apprise returned 502")]
        [TestCase("Unable to send test message: {0}", new object[] { "Invalid group id" }, "Unable to send test message: Invalid group id")]
        [TestCase("Script exited with code: {0}", new object[] { 2 }, "Script exited with code: 2")]
        [TestCase("{0}: {1}", new object[] { "ConnectFailure", "Connection refused" }, "ConnectFailure: Connection refused")]
        public void a_converted_other_test_failure_renders_the_old_english(string template, object[] args, string english)
        {
            new NzbDroneValidationFailure(string.Empty, new ServerText(template, args)).ErrorMessage.Should().Be(english);
        }

        // Goodreads ListId/SeriesId are ints; Apprise's StatusCode an HttpStatusCode; CustomScript's ExitCode an
        // int; Telegram passes webException.Status.ToString(). Interpolation and string.Format render them alike.
        [Test]
        public void typed_other_test_failure_arguments_render_the_old_english()
        {
            var listId = 12345;
            var seriesId = 678;
            var status = System.Net.HttpStatusCode.BadGateway;
            var exitCode = -1;
            var webStatus = System.Net.WebExceptionStatus.ConnectFailure;

            new ServerText("List {0} not found", listId).English.Should().Be($"List {listId} not found");
            new ServerText("Series {0} not found", seriesId).English.Should().Be($"Series {seriesId} not found");
            new ServerText("Unable to connect to Apprise API. Server connection failed: ({0}) {1}", status, "x").English.Should().Be($"Unable to connect to Apprise API. Server connection failed: ({status}) x");
            new ServerText("Script exited with code: {0}", exitCode).English.Should().Be($"Script exited with code: {exitCode}");
            new ServerText("{0}: {1}", webStatus.ToString(), "Connection refused").English.Should().Be($"{webStatus.ToString()}: Connection refused");
        }

        // Task 7 fix round (2026-09-27): Webhook and Plex now pass their exception's Text, whose English is the
        // exception's own Message -- the text the old ex.Message failure showed.
        [Test]
        public void webhook_and_plex_exception_text_failures_render_the_old_english()
        {
            var webhook = new NzbDrone.Core.Notifications.Webhook.WebhookException("Unable to post to webhook: {0}", new System.Exception("inner"), "Connection refused {sic}");
            var plex = new NzbDrone.Core.Notifications.Plex.PlexException("Unable to connect to Plex Media Server. Status Code: {0}", System.Net.HttpStatusCode.NotFound);

            webhook.Message.Should().Be("Unable to post to webhook: Connection refused {sic}");
            plex.Message.Should().Be("Unable to connect to Plex Media Server. Status Code: NotFound");
            new NzbDroneValidationFailure("Url", webhook.Text).ErrorMessage.Should().Be(webhook.Message);
            new NzbDroneValidationFailure("Host", plex.Text).ErrorMessage.Should().Be(plex.Message);
            new NzbDrone.Core.Notifications.Plex.PlexException("Unable to connect to Plex Media Server, certificate validation failed.", new System.Exception("inner")).Text.Should().BeNull();
        }

        // Plan E36 (fix round 2026-09-27): Mailgun's Test failure says "through Mailgun", not "though". Plan E38
        // (server messages follow-ups 2026-09-27): so do the test email's body and both log lines, and the success
        // log line says "Successfully" (was "Successsfully").
        [Test]
        public void the_mailgun_test_failure_says_through()
        {
            var en = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(System.IO.Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Localization/Core/en.json")));
            var source = System.IO.File.ReadAllText(System.IO.Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Notifications/Mailgun/Mailgun.cs"));

            en["ServerTestMailgunSendTestMessageFailed"].Should().Be("Unable to send test message through Mailgun.");
            source.Should().Contain("new ValidationFailure(\"\", \"Unable to send test message through Mailgun.\")");
            source.Should().Contain("\"This is a test message from Mangarr, through Mailgun.\"");
            source.Should().Contain("_logger.Info(\"Successfully sent email through Mailgun.\");");
            source.Should().Contain("_logger.Error(ex, \"Unable to send test message through Mailgun.\");");

            // The test email's body and the Test failure, as MailGun.Test() produces them.
            var proxy = new Moq.Mock<NzbDrone.Core.Notifications.Mailgun.IMailgunProxy>();
            var mailgun = new NzbDrone.Core.Notifications.Mailgun.MailGun(proxy.Object, LogManager.CreateNullLogger())
            {
                Definition = new NzbDrone.Core.Notifications.NotificationDefinition { Settings = new NzbDrone.Core.Notifications.Mailgun.MailgunSettings() }
            };
            string body = null;
            proxy.Setup(p => p.SendNotification(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<NzbDrone.Core.Notifications.Mailgun.MailgunSettings>()))
                 .Callback<string, string, NzbDrone.Core.Notifications.Mailgun.MailgunSettings>((t, m, s) => body = m);

            mailgun.Test().IsValid.Should().BeTrue();
            body.Should().Be("This is a test message from Mangarr, through Mailgun.");

            proxy.Setup(p => p.SendNotification(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<NzbDrone.Core.Notifications.Mailgun.MailgunSettings>()))
                 .Throws(new System.Exception("boom"));

            mailgun.Test().Errors.Select(e => e.ErrorMessage).Should().Equal("Unable to send test message through Mailgun.");
        }

        // Server messages follow-ups (2026-09-27): DiskStation's operation phrases that were interpolated
        // ($"add task from data {filename}", $"add task from url {url}", $"remove item {downloadId}",
        // $"get info of {path}"), its unknown-code reason ($"{Code} - Unknown error"), its api-info failure
        // (string.Format through the params constructor, now a ServerText), ProwlProxy's
        // "Unable to send text message: " + ex.Message and PlexServerProxy's $"Unable to connect to Plex Media Server, {ex.Message}".
        [TestCase("add task from data {0}", new object[] { "Series v01 {1}.torrent" }, "add task from data Series v01 {1}.torrent")]
        [TestCase("add task from url {0}", new object[] { "magnet:?xt=urn:btih:5dee65101db2&dn=Series+v01" }, "add task from url magnet:?xt=urn:btih:5dee65101db2&dn=Series+v01")]
        [TestCase("remove item {0}", new object[] { "dbid_12" }, "remove item dbid_12")]
        [TestCase("get info of {0}", new object[] { "/downloads/manga" }, "get info of /downloads/manga")]
        [TestCase("{0} - Unknown error", new object[] { 599 }, "599 - Unknown error")]
        [TestCase("Info of {0} not found on {1}:{2}", new object[] { NzbDrone.Core.Download.Clients.DownloadStation.DiskStationApi.DownloadStationTask, "nas.local", 5000 }, "Info of DownloadStationTask not found on nas.local:5000")]
        // Prowl: the old concatenation went through ProwlException's (message, innerException, params args)
        // constructor with zero args, which string.Formatted it -- a brace in ex.Message threw FormatException.
        // The new form passes ex.Message as the argument, so the brace row renders instead of throwing.
        [TestCase("Unable to send text message: {0}", new object[] { "HTTP request failed: [400:BadRequest] {x}" }, "Unable to send text message: HTTP request failed: [400:BadRequest] {x}")]
        [TestCase("Unable to connect to Plex Media Server, {0}", new object[] { "No route to host {x}" }, "Unable to connect to Plex Media Server, No route to host {x}")]
        public void a_converted_proxy_message_renders_the_old_english(string template, object[] args, string english)
        {
            new ServerText(template, args).English.Should().Be(english);
        }

        // Server messages follow-ups (2026-09-27): the real DiskStation proxy over a fake NAS. Old code:
        // $"Failed to {operation}. Reason: {responseContent.Error.GetMessage(api)}", thrown as a
        // DownloadClientAuthenticationException for session error 105 and a DownloadClientException otherwise.
        [TestCase(ApiInfoFailed, null, "Failed to get api info. Reason: The requested API does not exist", false)]
        [TestCase(ApiInfo, LoginFailed, "Failed to login. Reason: No such account or incorrect password", false)]
        [TestCase(ApiInfo, LoginSessionError, "Failed to login. Reason: The logged in session does not have permission", true)]
        public void a_disk_station_request_failure_renders_the_old_english(string query, string login, string english, bool authentication)
        {
            var http = new Moq.Mock<NzbDrone.Common.Http.IHttpClient>();
            http.Setup(h => h.Execute(Moq.It.IsAny<NzbDrone.Common.Http.HttpRequest>()))
                .Returns<NzbDrone.Common.Http.HttpRequest>(r => new NzbDrone.Common.Http.HttpResponse(r, new NzbDrone.Common.Http.HttpHeader(), r.Url.FullUri.Contains("method=login") ? login : query));
            var proxy = new NzbDrone.Core.Download.Clients.DownloadStation.Proxies.DSMInfoProxy(http.Object, new NzbDrone.Common.Cache.CacheManager(), LogManager.CreateNullLogger());
            var settings = new NzbDrone.Core.Download.Clients.DownloadStation.DownloadStationSettings { Host = "127.0.0.1", Port = 5000, Username = "admin", Password = "pass" };

            var exception = Assert.Catch<NzbDrone.Core.Download.Clients.DownloadClientException>(() => proxy.GetSerialNumber(settings));

            exception.Message.Should().Be(english);
            (exception is NzbDrone.Core.Download.Clients.DownloadClientAuthenticationException).Should().Be(authentication);
            exception.Text.English.Should().Be(english);
            exception.Text.Args.Should().AllBeOfType<ServerText>();
        }

        private const string ApiInfo = "{\"success\":true,\"data\":{\"SYNO.API.Auth\":{\"path\":\"auth.cgi\",\"minVersion\":1,\"maxVersion\":6},\"SYNO.DSM.Info\":{\"path\":\"entry.cgi\",\"minVersion\":1,\"maxVersion\":2}}}";
        private const string ApiInfoFailed = "{\"success\":false,\"error\":{\"code\":102}}";
        private const string LoginFailed = "{\"success\":false,\"error\":{\"code\":400}}";
        private const string LoginSessionError = "{\"success\":false,\"error\":{\"code\":105}}";

        // Server messages follow-ups (2026-09-27): the exceptions keep their template; the DownloadStation Test()
        // sites nest the DiskStation Text in "Unknown exception: {0}" with the same English as ex.Message.
        [Test]
        public void proxy_exception_texts_render_the_old_english()
        {
            var inner = new System.Exception("inner");
            var prowl = new NzbDrone.Core.Notifications.Prowl.ProwlException("Unable to send text message: {0}", inner, "HTTP request failed: [400:BadRequest]");
            var plex = new NzbDrone.Core.Notifications.Plex.PlexException("Unable to connect to Plex Media Server, {0}", inner, "No route to host");
            var diskStation = new NzbDrone.Core.Download.Clients.DownloadClientException(new ServerText("Failed to {0}. Reason: {1}", new ServerText("get config"), new ServerText("Invalid parameter")));

            prowl.Message.Should().Be("Unable to send text message: " + "HTTP request failed: [400:BadRequest]");
            prowl.InnerException.Should().BeSameAs(inner);
            plex.Message.Should().Be($"Unable to connect to Plex Media Server, {"No route to host"}");
            plex.InnerException.Should().BeSameAs(inner);
            new[] { prowl.Text, plex.Text }.Should().OnlyContain(t => t != null);
            new NzbDroneValidationFailure("ApiKey", prowl.Text).ErrorMessage.Should().Be(prowl.Message);
            new ServerText("Unknown exception: {0}", (object)diskStation.Text ?? diskStation.Message).English.Should().Be("Unknown exception: " + diskStation.Message);
            new ServerText("Unknown exception: {0}", (object)(new System.Exception("boom") as NzbDrone.Core.Download.Clients.DownloadClientException)?.Text ?? "boom").English.Should().Be("Unknown exception: boom");
        }

        // Task 8 (2026-09-27): API errors converted from concatenation or interpolation, built with their real
        // exception types, so each pin also proves the overload that keeps the template (Text). Old code:
        // id + " is not a valid ID" (RestController); $"Invalid extension, must be one of: {ValidExtensions.Join(", ")}"
        // (BackupController); "Received an error from Goodreads " + error (HttpResponseExtensions);
        // $"Unexpected response from {httpRequest.Url}", $"Failed to get works for {foreignAuthorId}" and
        // $"Failed to get books for {foreignBookId}" (BookInfoProxy).
        [Test]
        public void converted_api_errors_render_the_old_english()
        {
            var id = -5;
            var badId = new Readarr.Http.REST.BadRequestException(new ServerText("{0} is not a valid ID", id));

            badId.Content.Should().Be(id + " is not a valid ID");
            badId.Message.Should().Be("BadRequest: -5 is not a valid ID");
            badId.Text.Should().NotBeNull();

            var validExtensions = new List<string> { ".zip", ".db", ".xml" };
            var extension = new Readarr.Http.REST.UnsupportedMediaTypeException(new ServerText("Invalid extension, must be one of: {0}", NzbDrone.Common.Extensions.StringExtensions.Join(validExtensions, ", ")));

            extension.Content.Should().Be($"Invalid extension, must be one of: {NzbDrone.Common.Extensions.StringExtensions.Join(validExtensions, ", ")}");
            extension.Message.Should().Be("UnsupportedMediaType: Invalid extension, must be one of: .zip, .db, .xml");
            extension.Text.Should().NotBeNull();

            var error = "Book {1234} not found";
            var goodreads = new NzbDrone.Core.MetadataSource.BookInfo.BookInfoException("Received an error from Goodreads {0}", error);

            goodreads.Message.Should().Be("Received an error from Goodreads " + error);
            goodreads.Text.Should().NotBeNull();

            var url = new NzbDrone.Common.Http.HttpUri("https://api.bookinfo.club/v1/book/{1}");
            var foreignAuthorId = "anilist:{30013}~ln";
            var foreignBookId = "anilist:30013-v1";
            var unexpected = new NzbDrone.Core.MetadataSource.BookInfo.BookInfoException("Unexpected response from {0}", url);
            var works = new NzbDrone.Core.MetadataSource.BookInfo.BookInfoException("Failed to get works for {0}", foreignAuthorId);
            var books = new NzbDrone.Core.MetadataSource.BookInfo.BookInfoException("Failed to get books for {0}", foreignBookId);

            unexpected.Message.Should().Be($"Unexpected response from {url}");
            works.Message.Should().Be($"Failed to get works for {foreignAuthorId}");
            books.Message.Should().Be($"Failed to get books for {foreignBookId}");
            // Task 8 review: BookInfoProxy's missing-series invariant was new BookInfoException(string.Format(…)) (the
            // plain constructor, so no Text); it now takes the params constructor.
            var book = new Book { ForeignBookId = "anilist:30013-v1", Title = "Vol. {1}" };
            var missing = new NzbDrone.Core.MetadataSource.BookInfo.BookInfoException("Expected author metadata for id [{0}] in book data {1}", "anilist:30013", book);

            missing.Message.Should().Be(string.Format("Expected author metadata for id [{0}] in book data {1}", "anilist:30013", book));
            new[] { unexpected, works, books, missing }.Should().OnlyContain(e => e.Text != null && e.Text.English == e.Message);
        }

        public class UrlBaseModel
        {
            public string UrlBase { get; set; }
        }
    }
}
