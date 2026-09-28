using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Validators;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Localization
{
    // Server messages (2026-09-26): spec section 6's localizer unit tests, over fixture keys only.
    [TestFixture]
    public class ServerMessageLocalizerFixture : TestBase
    {
        private static readonly (string Key, string En, string Fr)[] Keys =
        {
            ("ServerRejectionTestAge", "Only {age} minutes old, minimum age is {minimum} minutes", "Seulement {age} minutes, l'âge minimum est de {minimum} minutes"),
            ("ServerRejectionTestOlder", "{title} is older than {age}", "{age} de trop pour {title}"),
            ("ServerRejectionTestSize", "Size {bytes} of {total} GB", "Taille {bytes} sur {total} Go"),
            ("ServerRejectionTestSample", "Sample", "Échantillon"),
            ("ServerImportTestFailed", "Import failed: {reason}", "Échec de l'import : {reason}"),
            ("ServerRejectionTestEnglishOnly", "Only in English {thing}", null),
            ("ServerValidationPathExists", "Path '{path}' does not exist", "Le chemin '{path}' n'existe pas"),
            ("ServerValidationTestUnder", "Under {ComparisonValue} leads to H&R", "En dessous de {ComparisonValue}, risque de H&R"),
            ("ServerValidationTestSetTo", "set to", "défini sur"),
            ("ServerTestTestUnknownException", "Unknown exception: {error}", "Exception inconnue : {error}"),
            ("ServerValidationBrand", "Requires Mangarr setup", "Nécessite une configuration {appName}"),
            ("ServerValidationTestBrandMessage", "Requires Mangarr for {ComparisonValue}", "Nécessite {appName} pour {ComparisonValue}")
        };

        private IServerMessageLocalizer French()
        {
            return ServerMessageTestLocalization.Create(TempFolder, Language.French, Keys).Localizer;
        }

        private static T WithUiCulture<T>(string culture, Func<T> action)
        {
            var saved = CultureInfo.CurrentUICulture;

            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                return action();
            }
            finally
            {
                CultureInfo.CurrentUICulture = saved;
            }
        }

        [Test]
        public void an_english_ui_returns_the_carriers_own_string()
        {
            var localizer = ServerMessageTestLocalization.Create(TempFolder, Language.English, Keys).Localizer;
            var english = "Only 5 minutes old, minimum age is 10 minutes";

            localizer.Localize(english, new ServerText("Only {0} minutes old, minimum age is {1} minutes", 5, 10)).Should().BeSameAs(english);
            localizer.Localize("Sample").Should().Be("Sample");
        }

        [Test]
        public void a_carrier_fills_named_tokens_from_positional_arguments()
        {
            French().Localize("Only 5 minutes old, minimum age is 10 minutes", new ServerText("Only {0} minutes old, minimum age is {1} minutes", 5, 10))
                .Should().Be("Seulement 5 minutes, l'âge minimum est de 10 minutes");
        }

        [Test]
        public void out_of_order_placeholders_fill_the_right_tokens()
        {
            var text = new ServerText("{1} is older than {0}", "2 days", "Vol. 3");

            text.English.Should().Be("Vol. 3 is older than 2 days");
            French().Localize(text.English, text).Should().Be("2 days de trop pour Vol. 3");
        }

        [Test]
        public void format_specifiers_apply_as_the_call_site_did()
        {
            var text = new ServerText("Size {0:N0} of {1:0.0} GB", 1234567, 1.25);

            French().Localize(text.English, text).Should().Be(string.Format("Taille {0:N0} sur {1:0.0} Go", 1234567, 1.25));
        }

        [Test]
        public void a_plain_message_is_matched_whole()
        {
            var french = French();

            french.Localize("Sample").Should().Be("Échantillon");
            french.Localize("Sample!").Should().Be("Sample!");
        }

        [Test]
        public void a_nested_server_text_is_localized_first()
        {
            var text = new ServerText("Import failed: {0}", new ServerText("Sample"));

            French().Localize(text.English, text).Should().Be("Échec de l'import : Échantillon");
        }

        [Test]
        public void a_key_the_locale_lacks_falls_back_to_the_english()
        {
            var text = new ServerText("Only in English {0}", 1);

            French().Localize(text.English, text).Should().Be("Only in English 1");
        }

        [Test]
        public void a_template_without_a_key_keeps_the_english()
        {
            var text = new ServerText("Nobody keyed {0}", 1);

            French().Localize(text.English, text).Should().Be("Nobody keyed 1");
        }

        // Task 2 review (2026-09-26): the shipped keys end to end -- EditionLanguageSpecification's mismatch
        // with its language names as nested carriers, in a French UI. The values come from the real en.json
        // and fr.json (the only test here that reads them), so a draft change must update this string.
        [Test]
        public void an_edition_mismatch_shows_its_language_names_in_french()
        {
            var core = Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Localization/Core");
            var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "en.json")));
            var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "fr.json")));
            var keys = new[] { "ServerRejectionEditionLanguageMismatch", "ServerRejectionLanguageEnglish", "ServerRejectionLanguageFrench" }
                .Select(k => (k, en[k], fr[k]))
                .ToArray();
            var french = ServerMessageTestLocalization.Create(TempFolder, Language.French, keys).Localizer;

            var text = new ServerText("Release is {0}, the series is the {1} edition", new ServerText("English"), new ServerText("French"));

            text.English.Should().Be("Release is English, the series is the French edition");
            french.Localize(text.English, text).Should().Be("La version est en anglais, la série est l'édition en français");
        }

        // Task 6 (2026-09-26): a failing download client Test() in a French UI, with the shipped keys: a carrier
        // (third-party text kept as is), a plain literal wrapped by NzbDroneValidationResult (no carrier), and
        // Aria2's version failure as its call site builds it now (plan E33: the version formatted in).
        [Test]
        public void a_download_client_test_failure_shows_in_french()
        {
            var core = Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Localization/Core");
            var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "en.json")));
            var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "fr.json")));
            var keys = new[] { "ServerTestUnknownException", "ServerTestUnableToConnectToQbittorrent", "ServerTestAria2VersionTooLow" }
                .Select(k => (k, en[k], fr[k]))
                .ToArray();
            var french = ServerMessageTestLocalization.Create(TempFolder, Language.French, keys).Localizer;

            var failures = new NzbDroneValidationResult(new List<FluentValidation.Results.ValidationFailure>
            {
                new NzbDroneValidationFailure(string.Empty, new ServerText("Unknown exception: {0}", "Unexpected '{' at 3")),
                new FluentValidation.Results.ValidationFailure("Host", "Unable to connect to qBittorrent"),
                new NzbDroneValidationFailure(string.Empty, new ServerText("Aria2 version should be at least 1.34.0. Version reported is {0}", "1.33.0")) { AttemptedValue = "1.33.0" }
            }).Errors;

            french.Localize(failures);

            failures.Select(f => f.ErrorMessage).Should().Equal(
                "Exception inconnue : Unexpected '{' at 3",
                "Impossible de se connecter à qBittorrent",
                "La version d'Aria2 doit être au moins 1.34.0. Version signalée : 1.33.0");
        }

        // Task 6 follow-up (2026-09-27): Flood.Test() adds new ValidationFailure(field, ex.Message), and FloodProxy's
        // exception messages are Mangarr's own literals. Token-less keys with exactly that text let the
        // exact-text path translate them, with no carrier.
        [Test]
        public void a_flood_test_failure_with_the_proxy_text_shows_in_french()
        {
            var core = Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Localization/Core");
            var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "en.json")));
            var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "fr.json")));
            var keys = new[] { "ServerTestFloodAuthenticationFailed", "ServerTestFloodUnableToConnect" }
                .Select(k => (k, en[k], fr[k]))
                .ToArray();
            var french = ServerMessageTestLocalization.Create(TempFolder, Language.French, keys).Localizer;

            var failures = new NzbDroneValidationResult(new List<FluentValidation.Results.ValidationFailure>
            {
                new FluentValidation.Results.ValidationFailure("Password", new DownloadClientAuthenticationException("Failed to authenticate with Flood.").Message),
                new FluentValidation.Results.ValidationFailure("Host", new DownloadClientException("Unable to connect to Flood, please check your settings").Message)
            }).Errors;

            french.Localize(failures);

            failures.Select(f => f.ErrorMessage).Should().Equal(fr["ServerTestFloodAuthenticationFailed"], fr["ServerTestFloodUnableToConnect"]);
            failures.Select(f => f.ErrorMessage).Should().NotContain(en["ServerTestFloodAuthenticationFailed"]).And.NotContain(en["ServerTestFloodUnableToConnect"]);
        }

        // Server messages follow-ups (2026-09-27): ProwlProxy's and PlexServerProxy's (template, args) exceptions,
        // through the real proxies and Test() sites over a failing HTTP client, in a French UI with the shipped
        // keys; the English is the old concatenation/interpolation. Plex's version and auth-token messages too.
        [Test]
        public void prowl_and_plex_proxy_failures_show_in_french()
        {
            var core = Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Localization/Core");
            var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "en.json")));
            var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "fr.json")));
            var keys = new[] { "ServerTestProwlSendTextMessageFailed", "ServerTestPlexUnableToConnect", "ServerTestPlexAuthTokenInvalid", "ServerTestPlexVersionUpgradeRequired" }
                .Select(k => (k, en[k], fr[k]))
                .ToArray();
            var french = ServerMessageTestLocalization.Create(TempFolder, Language.French, keys).Localizer;

            var prowlRequest = new NzbDrone.Common.Http.HttpRequest("https://api.prowlapp.com/publicapi/add");
            var prowlError = new NzbDrone.Common.Http.HttpException(prowlRequest, new NzbDrone.Common.Http.HttpResponse(prowlRequest, new NzbDrone.Common.Http.HttpHeader(), string.Empty, System.Net.HttpStatusCode.BadRequest));
            var prowlHttp = new Mock<NzbDrone.Common.Http.IHttpClient>();
            prowlHttp.Setup(h => h.Post(It.IsAny<NzbDrone.Common.Http.HttpRequest>())).Throws(prowlError);

            var plexHttp = new Mock<NzbDrone.Common.Http.IHttpClient>();
            plexHttp.Setup(h => h.Execute(It.IsAny<NzbDrone.Common.Http.HttpRequest>())).Throws(new System.Net.WebException("No route to host", System.Net.WebExceptionStatus.ConnectFailure));
            var plexConfig = new Mock<NzbDrone.Core.Configuration.IConfigService>();
            plexConfig.SetupGet(c => c.PlexClientIdentifier).Returns("mangarr-test");
            var plexProxy = new NzbDrone.Core.Notifications.Plex.Server.PlexServerProxy(plexHttp.Object, plexConfig.Object, NLog.LogManager.CreateNullLogger());
            var plex = new NzbDrone.Core.Notifications.Plex.Server.PlexServerService(new NzbDrone.Common.Cache.CacheManager(), plexProxy, Mock.Of<NzbDrone.Core.RootFolders.IRootFolderService>(), NLog.LogManager.CreateNullLogger());

            var failures = new NzbDroneValidationResult(new List<FluentValidation.Results.ValidationFailure>
            {
                new NzbDrone.Core.Notifications.Prowl.ProwlProxy(prowlHttp.Object, NLog.LogManager.CreateNullLogger()).Test(new NzbDrone.Core.Notifications.Prowl.ProwlSettings { ApiKey = "key" }),
                plex.Test(new NzbDrone.Core.Notifications.Plex.Server.PlexServerSettings { Host = "plex.local", Port = 32400 })
            }).Errors;

            failures.Select(f => f.ErrorMessage).Should().Equal(
                "Unable to send text message: " + prowlError.Message,
                $"Unable to connect to Plex Media Server, {"No route to host"}");

            french.Localize(failures);

            failures.Select(f => f.ErrorMessage).Should().Equal(
                fr["ServerTestProwlSendTextMessageFailed"].Replace("{error}", prowlError.Message),
                fr["ServerTestPlexUnableToConnect"].Replace("{error}", "No route to host"));
            fr["ServerTestProwlSendTextMessageFailed"].Should().StartWith("Impossible d'envoyer le message texte");

            var version = new NzbDrone.Core.Notifications.Plex.PlexVersionException("Found version {0}, upgrade to PMS 1.3.1 to fix library updating and then restart Mangarr", new Version(1, 3, 0, 3104));

            version.Message.Should().Be("Found version 1.3.0.3104, upgrade to PMS 1.3.1 to fix library updating and then restart Mangarr");
            french.Localize(version.Message, version.Text).Should().Be(fr["ServerTestPlexVersionUpgradeRequired"].Replace("{version}", "1.3.0.3104").Replace("{appName}", "Mangarr"));
            french.Localize("Unauthorized - AuthToken is invalid").Should().Be(fr["ServerTestPlexAuthTokenInvalid"]);
        }

        // Server messages follow-ups (2026-09-27): EditionLanguages.Name's region names ("Portuguese (Brazil)",
        // "Chinese (Taiwan)") as nested carriers read in French, in a rejection and in the add-series validation.
        [Test]
        public void a_region_language_name_shows_in_french()
        {
            var core = Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Localization/Core");
            var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "en.json")));
            var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "fr.json")));
            var keys = new[]
                {
                    "ServerRejectionEditionLanguageMismatch", "ServerValidationNoLanguageEditionInCatalogue", "ServerRejectionLanguageEnglish",
                    "ServerRejectionLanguagePortugueseBrazil", "ServerRejectionLanguageChineseTaiwan"
                }
                .Select(k => (k, en[k], fr[k]))
                .ToArray();
            var french = ServerMessageTestLocalization.Create(TempFolder, Language.French, keys).Localizer;

            var rejection = new ServerText("Release is {0}, the series is the {1} edition", new ServerText("English"), new ServerText(NzbDrone.Core.Books.EditionLanguages.Name("pt-BR")));
            var validation = new ServerText("No {0} edition of this series in the catalogue", new ServerText(NzbDrone.Core.Books.EditionLanguages.Name("zh-TW")));

            rejection.English.Should().Be("Release is English, the series is the Portuguese (Brazil) edition");
            french.Localize(rejection.English, rejection).Should().Be("La version est en anglais, la série est l'édition en portugais (Brésil)");
            french.Localize(validation.English, validation).Should().Be("Aucune édition en chinois (Taïwan) de cette série dans le catalogue");
        }

        // i18n leftovers (2026-09-28): "zh-HK" (Chinese (Hong Kong)) gained an IsoLanguages entry, and its
        // name reads in French the same way pt-BR/zh-TW do above -- same mechanism, no site-specific code.
        [Test]
        public void zh_hk_region_name_shows_in_french()
        {
            var core = Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Localization/Core");
            var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "en.json")));
            var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "fr.json")));
            var keys = new[]
                {
                    "ServerRejectionEditionLanguageMismatch", "ServerRejectionLanguageEnglish", "ServerRejectionLanguageChineseHongKong"
                }
                .Select(k => (k, en[k], fr[k]))
                .ToArray();
            var french = ServerMessageTestLocalization.Create(TempFolder, Language.French, keys).Localizer;

            var rejection = new ServerText("Release is {0}, the series is the {1} edition", new ServerText("English"), new ServerText(NzbDrone.Core.Books.EditionLanguages.Name("zh-HK")));

            rejection.English.Should().Be("Release is English, the series is the Chinese (Hong Kong) edition");
            french.Localize(rejection.English, rejection).Should().Be("La version est en anglais, la série est l'édition en chinois (Hong Kong)");
        }

        // Task 7 (2026-09-27): indexer, import list and notification Test() failures in a French UI, with the
        // shipped keys: carriers with third-party text (an indexer failure, a Goodreads list id, Apprise's status
        // code), a plain literal wrapped by NzbDroneValidationResult, Mangarr's own proxy exception texts passed as
        // ex.Message (Notifiarr, Discord, Join: the exact-text path), and Telegram's wordless "{0}: {1}" (kept).
        // Fix round (2026-09-27): Webhook and Plex pass their (template, args) exception's Text.
        [Test]
        public void an_other_provider_test_failure_shows_in_french()
        {
            var core = Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Localization/Core");
            var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "en.json")));
            var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "fr.json")));
            var keys = new[]
                {
                    "ServerTestIndexerUnreachable", "ServerTestGoodreadsListNotFound", "ServerTestAppriseConnectionFailed", "ServerTestUnableToSendTestMessage",
                    "ServerTestNotifiarrApiKeyInvalid", "ServerTestUnableToPostPayload", "ServerTestJoinAuthenticationFailed",
                    "ServerTestWebhookPostFailed", "ServerTestPlexStatusCode"
                }
                .Select(k => (k, en[k], fr[k]))
                .ToArray();
            var french = ServerMessageTestLocalization.Create(TempFolder, Language.French, keys).Localizer;

            var failures = new NzbDroneValidationResult(new List<FluentValidation.Results.ValidationFailure>
            {
                new NzbDroneValidationFailure(string.Empty, new ServerText("Unable to connect to indexer. {0}", "Invalid XML")),
                new NzbDroneValidationFailure("ListId", new ServerText("List {0} not found", 12345)),
                new NzbDroneValidationFailure("Url", new ServerText("Unable to connect to Apprise API. Server connection failed: ({0}) {1}", System.Net.HttpStatusCode.BadGateway, "Apprise returned 502")),
                new FluentValidation.Results.ValidationFailure("BotToken", "Unable to send test message"),
                new NzbDroneValidationFailure("APIKey", new NzbDrone.Core.Notifications.Notifiarr.NotifiarrException("API key is invalid").Message),
                new NzbDroneValidationFailure("Unable to post", new NzbDrone.Core.Notifications.Discord.DiscordException("Unable to post payload", new Exception("inner")).Message),
                new FluentValidation.Results.ValidationFailure("ApiKey", new NzbDrone.Core.Notifications.Join.JoinAuthException("Authentication failed.").Message),
                new NzbDroneValidationFailure("Connection", new ServerText("{0}: {1}", System.Net.WebExceptionStatus.ConnectFailure.ToString(), "Connection refused")),
                new NzbDroneValidationFailure("Url", new NzbDrone.Core.Notifications.Webhook.WebhookException("Unable to post to webhook: {0}", new Exception("inner"), "Connection refused").Text),
                new NzbDroneValidationFailure("Host", new NzbDrone.Core.Notifications.Plex.PlexException("Unable to connect to Plex Media Server. Status Code: {0}", System.Net.HttpStatusCode.NotFound).Text)
            }).Errors;

            french.Localize(failures);

            failures.Select(f => f.ErrorMessage).Should().Equal(
                "Impossible de se connecter à l'indexeur. Invalid XML",
                "Liste 12345 introuvable",
                "Impossible de se connecter à l'API Apprise. Échec de la connexion au serveur : (BadGateway) Apprise returned 502",
                "Impossible d'envoyer le message de test",
                fr["ServerTestNotifiarrApiKeyInvalid"],
                fr["ServerTestUnableToPostPayload"],
                fr["ServerTestJoinAuthenticationFailed"],
                "ConnectFailure: Connection refused",
                "Impossible d'envoyer au webhook : Connection refused",
                "Impossible de se connecter à Plex Media Server. Code d'état : NotFound");
        }

        // Server messages (2026-09-26, task 1b review): Localize(english, text) trusts text only while
        // text.English still equals the english being localized -- the same race
        // CommandQueueManager.SetMessage has (Message written before MessageText is cleared), and any mapper
        // that might otherwise pair a stale template with a fresh string. A mismatch is treated as plain text.
        [Test]
        public void a_mismatched_carrier_is_treated_as_plain_text()
        {
            var mismatched = new ServerText("Only {0} minutes old, minimum age is {1} minutes", 5, 10);

            French().Localize("Something else entirely", mismatched).Should().Be("Something else entirely");
        }

        [Test]
        public void a_record_stores_the_key_and_the_rendered_placeholders()
        {
            var french = French();
            var text = new ServerText("Size {0:N0} of {1:0.0} GB", 1234567, 1.25);
            var record = french.Record(text);

            record.Key.Should().Be("ServerRejectionTestSize");
            record.Args.Should().Equal(string.Format("{0:N0}", 1234567), string.Format("{0:0.0}", 1.25));

            french.LocalizeStored(text.English, record.Key, record.Args)
                .Should().Be(string.Format("Taille {0:N0} sur {1:0.0} Go", 1234567, 1.25));
            french.Record(new ServerText("Nobody keyed {0}", 1)).Should().BeNull();
        }

        [Test]
        public void en_and_the_ui_language_are_cached_side_by_side()
        {
            var (_, _, service) = ServerMessageTestLocalization.Create(TempFolder, Language.French, Keys);

            service.GetLocalizationDictionary("en")["ServerRejectionTestSample"].Should().Be("Sample");
            service.GetLocalizationDictionary("fr_fr")["ServerRejectionTestSample"].Should().Be("Échantillon");
            service.GetLocalizationDictionary("en")["ServerRejectionTestSample"].Should().Be("Sample");
        }

        [Test]
        public void a_ui_language_change_is_seen_after_the_config_is_saved()
        {
            var (localizer, config, service) = ServerMessageTestLocalization.Create(TempFolder, Language.English, Keys);

            localizer.Localize("Sample").Should().Be("Sample");

            config.SetupGet(c => c.UILanguage).Returns((int)Language.French);
            service.HandleAsync(new ConfigSavedEvent());

            localizer.Localize("Sample").Should().Be("Échantillon");
        }

        [Test]
        public void a_template_carrier_on_a_validation_failure_is_localized()
        {
            var failures = new List<FluentValidation.Results.ValidationFailure>
            {
                new NzbDroneValidationFailure("Host", new ServerText("Unknown exception: {0}", "boom"))
            };

            French().Localize(failures);

            failures.Single().ErrorMessage.Should().Be("Exception inconnue : boom");
        }

        [Test]
        public void a_custom_validator_is_localized_by_its_error_code()
        {
            var disk = new Mock<IDiskProvider>();
            disk.Setup(d => d.FolderExists(It.IsAny<string>())).Returns(false);

            var validator = new InlineValidator<PathModel>();
            validator.RuleFor(m => m.Path).SetValidator(new PathExistsValidator(disk.Object));

            var failures = validator.Validate(new PathModel { Path = "/data/x" }).Errors;
            failures.Single().ErrorMessage.Should().Be("Path '/data/x' does not exist");

            French().Localize(failures);

            failures.Single().ErrorMessage.Should().Be("Le chemin '/data/x' n'existe pas");
        }

        // Fix round 1 (2026-09-26): a translation reached through FluentValidation's own MessageFormatter
        // (the custom-validator and .WithMessage render-compare paths) may use {appName} too, same as any
        // other Server* value.
        [Test]
        public void a_custom_validators_translation_can_use_appname()
        {
            var validator = new InlineValidator<PathModel>();
            validator.RuleFor(m => m.Path).SetValidator(new BrandValidator());

            var failures = validator.Validate(new PathModel { Path = "x" }).Errors;
            failures.Single().ErrorMessage.Should().Be("Requires Mangarr setup");

            French().Localize(failures);

            failures.Single().ErrorMessage.Should().Be("Nécessite une configuration Mangarr");
        }

        [Test]
        public void a_with_message_template_is_matched_by_rendering_its_placeholders()
        {
            var validator = new InlineValidator<PathModel>();
            validator.RuleFor(m => m.Ratio).GreaterThanOrEqualTo(2.5).WithMessage("Under {ComparisonValue} leads to H&R");

            var failures = validator.Validate(new PathModel { Ratio = 1 }).Errors;

            French().Localize(failures);

            failures.Single().ErrorMessage.Should().Be(string.Format("En dessous de {0}, risque de H&R", 2.5));
        }

        [Test]
        public void a_with_message_translation_can_use_appname()
        {
            var validator = new InlineValidator<PathModel>();
            validator.RuleFor(m => m.Ratio).GreaterThanOrEqualTo(2.5).WithMessage("Requires Mangarr for {ComparisonValue}");

            var failures = validator.Validate(new PathModel { Ratio = 1 }).Errors;

            French().Localize(failures);

            failures.Single().ErrorMessage.Should().Be(string.Format("Nécessite Mangarr pour {0}", 2.5));
        }

        [TestCase(2, "fr", "NotEmpty")]
        [TestCase(2, "fr", "NotNull")]
        [TestCase(2, "fr", "Matches")]
        [TestCase(2, "fr", "InclusiveBetween")]
        [TestCase(2, "fr", "Must")]
        [TestCase(2, "fr", "GreaterThanOrEqualTo")]
        [TestCase(2, "fr", "GreaterThan")]
        [TestCase(2, "fr", "LessThanOrEqualTo")]
        [TestCase(2, "fr", "EmailAddress")]
        [TestCase(2, "fr", "NotEqual")]
        [TestCase(2, "fr", "Equal")]
        [TestCase(2, "fr", "IsEnumName")]
        [TestCase(4, "de", "NotEmpty")]
        [TestCase(4, "de", "NotNull")]
        [TestCase(4, "de", "Matches")]
        [TestCase(4, "de", "InclusiveBetween")]
        [TestCase(4, "de", "Must")]
        [TestCase(4, "de", "GreaterThanOrEqualTo")]
        [TestCase(4, "de", "GreaterThan")]
        [TestCase(4, "de", "LessThanOrEqualTo")]
        [TestCase(4, "de", "EmailAddress")]
        [TestCase(4, "de", "NotEqual")]
        [TestCase(4, "de", "Equal")]
        [TestCase(4, "de", "IsEnumName")]
        [TestCase(8, "ja", "NotEmpty")]
        [TestCase(8, "ja", "NotNull")]
        [TestCase(8, "ja", "Matches")]
        [TestCase(8, "ja", "InclusiveBetween")]
        [TestCase(8, "ja", "Must")]
        [TestCase(8, "ja", "GreaterThanOrEqualTo")]
        [TestCase(8, "ja", "GreaterThan")]
        [TestCase(8, "ja", "LessThanOrEqualTo")]
        [TestCase(8, "ja", "EmailAddress")]
        [TestCase(8, "ja", "NotEqual")]
        [TestCase(8, "ja", "Equal")]
        [TestCase(8, "ja", "IsEnumName")]
        public void a_fluentvalidation_builtin_is_re_rendered_in_the_ui_language(int language, string culture, string builtIn)
        {
            // Task 5 (2026-09-26): one case per FluentValidation built-in the repo uses. The oracle is
            // FluentValidation's own rendering under that UI culture; validation itself runs in English.
            var (validator, model) = BuiltIn(builtIn);

            var failures = WithUiCulture("en", () => validator.Validate(model).Errors);
            var english = failures.Single().ErrorMessage;
            var expected = WithUiCulture(culture, () => validator.Validate(model).Errors.Single().ErrorMessage);

            ServerMessageTestLocalization.Create(TempFolder, (Language)language, Keys).Localizer.Localize(failures);

            failures.Single().ErrorMessage.Should().Be(expected);
            failures.Single().ErrorMessage.Should().NotBe(english);
        }

        private static (InlineValidator<PathModel> Validator, PathModel Model) BuiltIn(string builtIn)
        {
            var validator = new InlineValidator<PathModel>();
            var model = new PathModel { Path = "None", Ratio = 1, Count = 9, Email = "not-an-email", Flag = false };

            switch (builtIn)
            {
                case "NotEmpty":
                    validator.RuleFor(m => m.Path).NotEmpty();
                    model.Path = null;
                    break;
                case "NotNull":
                    validator.RuleFor(m => m.Path).NotNull();
                    model.Path = null;
                    break;
                case "Matches":
                    validator.RuleFor(m => m.Path).Matches("^[a-z]+$");
                    break;
                case "InclusiveBetween":
                    validator.RuleFor(m => m.Count).InclusiveBetween(1, 5);
                    break;
                case "Must":
                    validator.RuleFor(m => m.Path).Must(p => false);
                    break;
                case "GreaterThanOrEqualTo":
                    validator.RuleFor(m => m.Ratio).GreaterThanOrEqualTo(2.5);
                    break;
                case "GreaterThan":
                    validator.RuleFor(m => m.Ratio).GreaterThan(1.0);
                    break;
                case "LessThanOrEqualTo":
                    validator.RuleFor(m => m.Count).LessThanOrEqualTo(5);
                    break;
                case "EmailAddress":
                    validator.RuleFor(m => m.Email).EmailAddress();
                    break;
                case "NotEqual":
                    validator.RuleFor(m => m.Path).NotEqual("None");
                    break;
                case "Equal":
                    validator.RuleFor(m => m.Flag).Equal(true);
                    break;
                case "IsEnumName":
                    validator.RuleFor(m => m.Path).IsEnumName(typeof(DayOfWeek));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(builtIn), builtIn, null);
            }

            return (validator, model);
        }

        // Task 5 (2026-09-26): SystemFolderValidator passes the English words "set to" / "child of" as
        // {relationship}; the token-less ServerValidationRelationship* keys put them in the UI language inside
        // the translated template.
        [Test]
        public void a_relationship_word_reads_in_the_ui_language()
        {
            var keys = new[]
            {
                ("ServerValidationSystemFolder", "Path '{path}' is {relationship} system folder {systemFolder}", "Le chemin '{path}' est {relationship} dossier système {systemFolder}"),
                ("ServerValidationRelationshipSetTo", "set to", "identique au"),
                ("ServerValidationRelationshipChildOf", "child of", "un sous-dossier du")
            };
            var french = ServerMessageTestLocalization.Create(TempFolder, Language.French, keys).Localizer;
            var systemFolder = SystemFolders.GetSystemFolders().First();
            var child = Path.Combine(systemFolder, "manga");

            var validator = new InlineValidator<PathModel>();
            validator.RuleFor(m => m.Path).SetValidator(new SystemFolderValidator());

            var setTo = validator.Validate(new PathModel { Path = systemFolder }).Errors;
            var childOf = validator.Validate(new PathModel { Path = child }).Errors;

            setTo.Single().ErrorMessage.Should().Be($"Path '{systemFolder}' is set to system folder {systemFolder}");
            childOf.Single().ErrorMessage.Should().Be($"Path '{child}' is child of system folder {systemFolder}");

            french.Localize(setTo);
            french.Localize(childOf);

            setTo.Single().ErrorMessage.Should().Be($"Le chemin '{systemFolder}' est identique au dossier système {systemFolder}");
            childOf.Single().ErrorMessage.Should().Be($"Le chemin '{child}' est un sous-dossier du dossier système {systemFolder}");
        }

        // Task 5 (2026-09-26): a rule message built from a value fixed when the rule was built reaches the
        // localizer as rendered English only; ServerRuleMessages remembers its template by that exact text.
        [Test]
        public void a_rule_message_is_localized_by_its_remembered_template()
        {
            var keys = new[] { ("ServerValidationTestRuleMinimum", "Rule test minimum {minimum} for H&R", "Minimum de test {minimum} pour H&R") };
            var french = ServerMessageTestLocalization.Create(TempFolder, Language.French, keys).Localizer;

            var validator = new InlineValidator<PathModel>();
            validator.RuleFor(m => m.Ratio).Must(r => false).WithMessage(ServerRuleMessages.Format("Rule test minimum {0} for H&R", 1.5));

            var failures = validator.Validate(new PathModel()).Errors;
            failures.Single().ErrorMessage.Should().Be(string.Format("Rule test minimum {0} for H&R", 1.5));

            french.Localize(failures);

            failures.Single().ErrorMessage.Should().Be(string.Format("Minimum de test {0} pour H&R", 1.5));
        }

        // Task 5 (2026-09-26): a generic custom validator's ErrorCode carries the arity suffix
        // (AllowedValidator`1), so the ErrorCode-to-key step misses it; its token-less template still reads in
        // the UI language through the whole-text match.
        [Test]
        public void a_generic_custom_validator_is_localized_by_its_text()
        {
            var keys = new[] { ("ServerValidationAllowed", "Must contain at least one allowed quality", "Doit contenir au moins une qualité autorisée") };
            var french = ServerMessageTestLocalization.Create(TempFolder, Language.French, keys).Localizer;

            var validator = new InlineValidator<PathModel>();
            validator.RuleFor(m => m.Path).SetValidator(new Readarr.Api.V1.Profiles.Quality.AllowedValidator<string>());

            var failures = validator.Validate(new PathModel { Path = "x" }).Errors;
            failures.Single().ErrorMessage.Should().Be("Must contain at least one allowed quality");

            french.Localize(failures);

            failures.Single().ErrorMessage.Should().Be("Doit contenir au moins une qualité autorisée");
        }

        [Test]
        public void a_provider_settings_copy_keeps_what_the_re_render_needs()
        {
            var validator = new InlineValidator<PathModel>();
            validator.RuleFor(m => m.Path).NotEmpty();

            var copy = new NzbDroneValidationFailure(WithUiCulture("en", () => validator.Validate(new PathModel()).Errors.Single()));
            var expected = WithUiCulture("fr", () => validator.Validate(new PathModel()).Errors.Single().ErrorMessage);
            var failures = new List<FluentValidation.Results.ValidationFailure> { copy };

            French().Localize(failures);

            copy.ErrorMessage.Should().Be(expected);
        }

        [Test]
        public void a_detailed_description_is_localized_too()
        {
            var failure = new NzbDroneValidationFailure("Host", "Sample")
            {
                DetailedDescriptionText = new ServerText("Unknown exception: {0}", "boom")
            };

            French().Localize(new List<FluentValidation.Results.ValidationFailure> { failure });

            failure.ErrorMessage.Should().Be("Échantillon");
            failure.DetailedDescription.Should().Be("Exception inconnue : boom");
        }

        // Fix round 1 (2026-09-26): Text/DetailedDescriptionText are trusted only while ErrorMessage/
        // DetailedDescription still equal the English they produced. Something that changed either string
        // independently (bypassing the carrier) must fall through to the plain-text paths instead of getting
        // the stale carrier's translation.
        [Test]
        public void a_mismatched_carrier_falls_through_to_the_plain_text_paths()
        {
            var failure = new NzbDroneValidationFailure("Host", new ServerText("Unknown exception: {0}", "boom"))
            {
                ErrorMessage = "Something else entirely",
                DetailedDescriptionText = new ServerText("Unknown exception: {0}", "boom")
            };

            failure.DetailedDescription = "Some other detail";

            French().Localize(new List<FluentValidation.Results.ValidationFailure> { failure });

            failure.ErrorMessage.Should().Be("Something else entirely");
            failure.DetailedDescription.Should().Be("Some other detail");
        }

        public class PathModel
        {
            public string Path { get; set; }
            public double Ratio { get; set; }
            public int Count { get; set; }
            public string Email { get; set; }
            public bool Flag { get; set; }
        }

        // Fix round 1 (2026-09-26): a custom validator with a token-less message, for the appName test above.
        private class BrandValidator : PropertyValidator
        {
            protected override string GetDefaultMessageTemplate() => "Requires Mangarr setup";

            protected override bool IsValid(PropertyValidatorContext context) => false;
        }
    }
}
