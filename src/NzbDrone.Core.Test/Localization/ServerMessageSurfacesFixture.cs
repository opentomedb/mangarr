using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Composition;
using NzbDrone.Common.Localization;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Validation;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Blocklist;
using Readarr.Api.V1.Commands;
using Readarr.Api.V1.History;
using Readarr.Api.V1.Indexers;
using Readarr.Api.V1.ManualImport;
using Readarr.Api.V1.PreferredEdition;
using Readarr.Api.V1.Queue;
using Readarr.Http.ErrorManagement;

namespace NzbDrone.Core.Test.Localization
{
    // Server messages (2026-09-26): one test per API surface of spec section 2, UI in French, over fixture
    // keys. Each also pins that an English UI gets the model's own string and that the model is unchanged.
    [TestFixture]
    public class ServerMessageSurfacesFixture : TestBase
    {
        private static readonly (string Key, string En, string Fr)[] Keys =
        {
            ("ServerRejectionTestSmaller", "{size} is smaller than minimum allowed {minimum}", "{size} est plus petit que le minimum autorisé {minimum}"),
            ("ServerImportTestUnknownSeries", "Unknown Series", "Série inconnue"),
            ("ServerQueueTestNoFiles", "No files found are eligible for import in {path}", "Aucun fichier importable dans {path}"),
            ("ServerQueueTestChapterRip", "Chapter-numbered rip: {chapters} of {files} comic files are per-chapter, no volume files", "Rip par chapitres : {chapters} fichiers sur {files} sont des chapitres, aucun fichier de tome"),
            ("ServerQueueTestManuallyFailed", "Manually marked as failed", "Marqué manuellement comme échoué"),
            ("ServerProgressTestProcessing", "Processing {count} releases", "Traitement de {count} versions"),
            ("ServerTestTestUnknownException", "Unknown exception: {error}", "Exception inconnue : {error}"),
            ("ServerApiTestNotInCache", "Couldn't find requested release in cache, try searching again", "Version introuvable dans le cache, relancez la recherche"),
            ("ServerApiTestRootFolder", "Series' root folder ({path}) doesn't exist.", "Le dossier racine de la série ({path}) n'existe pas."),
            ("ServerApiTestUnparsable", "Unable to parse volumes in the release", "Impossible d'analyser les tomes de la version")
        };

        private IServerMessageLocalizer Localizer(Language language)
        {
            return ServerMessageTestLocalization.Create(TempFolder, language, Keys).Localizer;
        }

        private class ReleaseMapper : ReleaseControllerBase
        {
            public ReleaseMapper(IServerMessageLocalizer messages)
                : base(messages)
            {
            }

            public List<ReleaseResource> Map(params DownloadDecision[] decisions)
            {
                return MapDecisions(decisions);
            }
        }

        private static DownloadDecision DecisionWith(params Rejection[] rejections)
        {
            var remoteBook = new RemoteBook
            {
                Release = new ReleaseInfo { Title = "Series v01", Guid = "guid", Indexer = "indexer" },
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.Unknown) },
                CustomFormats = new List<CustomFormat>()
            };

            return new DownloadDecision(remoteBook, rejections);
        }

        [Test]
        public void interactive_search_rejections_show_in_the_ui_language()
        {
            var rejection = new Rejection(new ServerText("{0} is smaller than minimum allowed {1}", "1.0 MB", "2.0 MB"));

            new ReleaseMapper(Localizer(Language.French)).Map(DecisionWith(rejection)).Single().Rejections
                .Should().Equal("1.0 MB est plus petit que le minimum autorisé 2.0 MB");
            new ReleaseMapper(Localizer(Language.English)).Map(DecisionWith(rejection)).Single().Rejections
                .Should().Equal("1.0 MB is smaller than minimum allowed 2.0 MB");
            rejection.Reason.Should().Be("1.0 MB is smaller than minimum allowed 2.0 MB");
        }

        [Test]
        public void manual_import_rejections_show_in_the_ui_language()
        {
            var rejection = new Rejection(new ServerText("Unknown Series"), RejectionType.Temporary);
            var item = new ManualImportItem { Path = "/downloads/Series v01.cbz", Name = "Series v01.cbz", Rejections = new List<Rejection> { rejection } };

            var resource = item.ToResource(Localizer(Language.French));

            resource.Rejections.Single().Reason.Should().Be("Série inconnue");
            resource.Rejections.Single().Type.Should().Be(RejectionType.Temporary);
            item.ToResource(Localizer(Language.English)).Rejections.Single().Reason.Should().Be("Unknown Series");
            rejection.Reason.Should().Be("Unknown Series");
        }

        [Test]
        public void queue_warnings_show_in_the_ui_language()
        {
            var warning = new TrackedDownloadStatusMessage("Series v01", new ServerText("No files found are eligible for import in {0}", "/downloads/x"));
            var item = new NzbDrone.Core.Queue.Queue { Id = 1, Title = "Series v01", Status = "Completed", StatusMessages = new List<TrackedDownloadStatusMessage> { warning } };

            var queueService = new Mock<IQueueService>();
            queueService.Setup(q => q.GetQueue()).Returns(new List<NzbDrone.Core.Queue.Queue> { item });

            var pending = new Mock<IPendingReleaseService>();
            pending.Setup(p => p.GetPendingQueue()).Returns(new List<NzbDrone.Core.Queue.Queue>());

            var controller = new QueueDetailsController(Mock.Of<IBroadcastSignalRMessage>(), queueService.Object, pending.Object, Localizer(Language.French));

            controller.GetQueue(null, new List<int>()).Single().StatusMessages.Single().Messages
                .Should().Equal("Aucun fichier importable dans /downloads/x");
            item.ToResource(false, false, Localizer(Language.English)).StatusMessages.Single().Messages
                .Should().Equal("No files found are eligible for import in /downloads/x");
            warning.Messages.Should().Equal("No files found are eligible for import in /downloads/x");
        }

        [Test]
        public void command_progress_shows_in_the_ui_language_and_a_stored_message_is_matched_whole()
        {
            var progress = new CommandModel
            {
                Id = 1,
                Name = "RefreshAuthor",
                Body = new RefreshAuthorCommand(),
                Status = CommandStatus.Started,
                Message = "Processing 3 releases",
                MessageText = ServerText.WithEnglish("Processing 3 releases", "Processing {0} releases", 3)
            };
            var stored = new CommandModel { Id = 2, Name = "RefreshAuthor", Body = new RefreshAuthorCommand(), Status = CommandStatus.Completed, Message = "Processing 3 releases" };

            var queue = new Mock<IManageCommandQueue>();
            queue.Setup(q => q.All()).Returns(new List<CommandModel> { progress, stored });

            var controller = new CommandController(queue.Object, Mock.Of<IBroadcastSignalRMessage>(), new KnownTypes(), EnglishLocalization.Create(), Localizer(Language.French));
            var messages = controller.GetStartedCommands().ToDictionary(c => c.Id, c => c.Message);

            messages[1].Should().Be("Traitement de 3 versions");
            messages[2].Should().Be("Processing 3 releases");
            progress.Message.Should().Be("Processing 3 releases");
        }

        [Test]
        public void a_new_history_row_stores_its_key_and_the_details_show_it_in_the_ui_language()
        {
            var french = Localizer(Language.French);
            var repository = new Mock<IHistoryRepository>();
            EntityHistory inserted = null;
            repository.Setup(r => r.Insert(It.IsAny<EntityHistory>())).Callback<EntityHistory>(h => inserted = h);

            var text = new ServerText("Chapter-numbered rip: {0} of {1} comic files are per-chapter, no volume files", 118, 120);

            new HistoryService(repository.Object, french, LogManager.GetLogger("test")).Handle(new DownloadFailedEvent
            {
                BookIds = new List<int> { 1 },
                Quality = new QualityModel(),
                SourceTitle = "MARRIAGE TOXIN",
                Message = text.English,
                MessageText = text
            });

            inserted.Data["Message"].Should().Be(text.English);
            inserted.Data["MessageKey"].Should().Be("ServerQueueTestChapterRip");
            inserted.Data["MessageArgs"].Should().Be("[\"118\",\"120\"]");

            // As the History API reads the row back: EmbeddedDocumentConverter camel-cases the stored keys.
            var stored = new EntityHistory { Data = inserted.Data.ToDictionary(e => char.ToLowerInvariant(e.Key[0]) + e.Key.Substring(1), e => e.Value) };
            var old = new EntityHistory { Data = new Dictionary<string, string> { { "message", "Manually marked as failed" } } };
            var formats = new Mock<ICustomFormatCalculationService>();
            formats.Setup(f => f.ParseCustomFormat(It.IsAny<EntityHistory>(), It.IsAny<Author>())).Returns(new List<CustomFormat>());

            var resource = stored.ToResource(formats.Object, french);

            resource.Data["message"].Should().Be("Rip par chapitres : 118 fichiers sur 120 sont des chapitres, aucun fichier de tome");
            resource.Data.Keys.Should().NotContain("messageKey").And.NotContain("messageArgs");
            old.ToResource(formats.Object, french).Data["message"].Should().Be("Marqué manuellement comme échoué");
            stored.ToResource(formats.Object, Localizer(Language.English)).Data["message"].Should().Be(text.English);
            stored.Data.Should().ContainKey("messageKey");
        }

        // Server messages (2026-09-26, task 1b review): a hand-edited or corrupted messageArgs value must
        // not throw out of the History API -- it falls back to the plain English message instead.
        [Test]
        public void a_malformed_stored_args_falls_back_to_the_english_message()
        {
            var french = Localizer(Language.French);
            var formats = new Mock<ICustomFormatCalculationService>();
            formats.Setup(f => f.ParseCustomFormat(It.IsAny<EntityHistory>(), It.IsAny<Author>())).Returns(new List<CustomFormat>());

            var stored = new EntityHistory
            {
                Data = new Dictionary<string, string>
                {
                    { "message", "Chapter-numbered rip: 118 of 120 comic files are per-chapter, no volume files" },
                    { "messageKey", "ServerQueueTestChapterRip" },
                    { "messageArgs", "not valid json" }
                }
            };

            var resource = stored.ToResource(formats.Object, french);

            resource.Data["message"].Should().Be("Chapter-numbered rip: 118 of 120 comic files are per-chapter, no volume files");
            resource.Data.Keys.Should().NotContain("messageKey").And.NotContain("messageArgs");
        }

        // Server messages follow-ups (2026-09-27): an import-incomplete row stores StatusMessageKeys beside its
        // English StatusMessages. The details show each message in the UI language -- from its record, else matched
        // whole -- with the file-name titles unchanged; an unkeyed message stays English.
        private static EntityHistory ImportIncompleteRow(IServerMessageLocalizer messages)
        {
            var repository = new Mock<IHistoryRepository>();
            EntityHistory inserted = null;
            repository.Setup(r => r.Insert(It.IsAny<EntityHistory>())).Callback<EntityHistory>(h => inserted = h);

            var tracked = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem { Title = "Series v01-v02", DownloadId = "abc" },
                RemoteBook = new RemoteBook { Books = new List<Book> { new Book { Id = 1, AuthorId = 2 } }, ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel() } }
            };

            tracked.Warn(
                new TrackedDownloadStatusMessage(
                    "Series v01.cbz",
                    new List<string> { "Chapter-numbered rip: 118 of 120 comic files are per-chapter, no volume files", "Unknown Series" },
                    new List<ServerText> { new ServerText("Chapter-numbered rip: {0} of {1} comic files are per-chapter, no volume files", 118, 120), null }),
                new TrackedDownloadStatusMessage("Series v02.cbz", "Download client said no"));

            new HistoryService(repository.Object, messages, LogManager.GetLogger("test")).Handle(new NzbDrone.Core.MediaFiles.Events.BookImportIncompleteEvent(tracked));

            // As the History API reads the row back: EmbeddedDocumentConverter camel-cases the stored keys.
            return new EntityHistory { Data = inserted.Data.ToDictionary(e => char.ToLowerInvariant(e.Key[0]) + e.Key.Substring(1), e => e.Value) };
        }

        private static List<TrackedDownloadStatusMessage> StatusMessages(string json)
        {
            return Json.Deserialize<List<TrackedDownloadStatusMessage>>(json);
        }

        [Test]
        public void an_import_incomplete_row_shows_its_status_messages_in_the_ui_language()
        {
            var french = Localizer(Language.French);
            var formats = new Mock<ICustomFormatCalculationService>();
            formats.Setup(f => f.ParseCustomFormat(It.IsAny<EntityHistory>(), It.IsAny<Author>())).Returns(new List<CustomFormat>());
            var stored = ImportIncompleteRow(french);

            stored.Data.Should().ContainKey("statusMessageKeys");

            var resource = stored.ToResource(formats.Object, french);
            var shown = StatusMessages(resource.Data["statusMessages"]);

            resource.Data.Keys.Should().NotContain("statusMessageKeys");
            shown.Select(m => m.Title).Should().Equal("Series v01.cbz", "Series v02.cbz");
            shown[0].Messages.Should().Equal("Rip par chapitres : 118 fichiers sur 120 sont des chapitres, aucun fichier de tome", "Série inconnue");
            shown[1].Messages.Should().Equal("Download client said no");
            StatusMessages(stored.Data["statusMessages"])[0].Messages[0].Should().Be("Chapter-numbered rip: 118 of 120 comic files are per-chapter, no volume files");

            // An old row (before StatusMessageKeys): matched whole only, so the templated message stays English.
            var old = new EntityHistory { Data = stored.Data.Where(e => e.Key != "statusMessageKeys").ToDictionary(e => e.Key, e => e.Value) };
            var oldShown = StatusMessages(old.ToResource(formats.Object, french).Data["statusMessages"]);

            oldShown[0].Messages.Should().Equal("Chapter-numbered rip: 118 of 120 comic files are per-chapter, no volume files", "Série inconnue");

            // Malformed StatusMessageKeys (or StatusMessages): the stored English, untouched.
            var malformed = new EntityHistory { Data = new Dictionary<string, string>(stored.Data) { ["statusMessageKeys"] = "not valid json" } };
            var corrupt = new EntityHistory { Data = new Dictionary<string, string> { { "statusMessages", "[{\"title\": " } } };

            malformed.ToResource(formats.Object, french).Data["statusMessages"].Should().Be(stored.Data["statusMessages"]);
            malformed.ToResource(formats.Object, french).Data.Keys.Should().NotContain("statusMessageKeys");
            corrupt.ToResource(formats.Object, french).Data["statusMessages"].Should().Be("[{\"title\": ");
        }

        // A carrier whose English no longer matches its message (a stale or mismatched pair) records null at that
        // slot, so the details fall back to the exact-match path: here the message stays English, while its keyed
        // neighbour still reads in French.
        [Test]
        public void a_mismatched_status_message_carrier_records_null_and_shows_english()
        {
            var french = Localizer(Language.French);
            var repository = new Mock<IHistoryRepository>();
            EntityHistory inserted = null;
            repository.Setup(r => r.Insert(It.IsAny<EntityHistory>())).Callback<EntityHistory>(h => inserted = h);

            var tracked = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem { Title = "Series v03", DownloadId = "def" },
                RemoteBook = new RemoteBook { Books = new List<Book> { new Book { Id = 3, AuthorId = 2 } }, ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel() } }
            };

            tracked.Warn(new TrackedDownloadStatusMessage(
                "Series v03.cbz",
                new List<string> { "Chapter-numbered rip: 1 of 2 comic files are per-chapter, no volume files", "Unknown Series" },
                new List<ServerText> { new ServerText("Chapter-numbered rip: {0} of {1} comic files are per-chapter, no volume files", 118, 120), new ServerText("Unknown Series") }));

            new HistoryService(repository.Object, french, LogManager.GetLogger("test")).Handle(new NzbDrone.Core.MediaFiles.Events.BookImportIncompleteEvent(tracked));

            var records = Json.Deserialize<List<List<ServerMessageRecord>>>(inserted.Data["StatusMessageKeys"]);

            records.Single()[0].Should().BeNull();
            records.Single()[1].Key.Should().Be("ServerImportTestUnknownSeries");

            var formats = new Mock<ICustomFormatCalculationService>();
            formats.Setup(f => f.ParseCustomFormat(It.IsAny<EntityHistory>(), It.IsAny<Author>())).Returns(new List<CustomFormat>());
            var stored = new EntityHistory { Data = inserted.Data.ToDictionary(e => char.ToLowerInvariant(e.Key[0]) + e.Key.Substring(1), e => e.Value) };

            StatusMessages(stored.ToResource(formats.Object, french).Data["statusMessages"]).Single().Messages
                .Should().Equal("Chapter-numbered rip: 1 of 2 comic files are per-chapter, no volume files", "Série inconnue");
        }

        [Test]
        public void an_import_incomplete_row_is_byte_identical_for_an_english_ui()
        {
            var english = Localizer(Language.English);
            var formats = new Mock<ICustomFormatCalculationService>();
            formats.Setup(f => f.ParseCustomFormat(It.IsAny<EntityHistory>(), It.IsAny<Author>())).Returns(new List<CustomFormat>());
            var stored = ImportIncompleteRow(english);
            var withoutKeys = stored.Data.Where(e => e.Key != "statusMessageKeys").ToDictionary(e => e.Key, e => e.Value);

            var resource = stored.ToResource(formats.Object, english);

            resource.Data.Should().Equal(withoutKeys);
            resource.Data["statusMessages"].Should().BeSameAs(stored.Data["statusMessages"]);
            resource.Data["statusMessages"].Should().Be(new[]
            {
                new TrackedDownloadStatusMessage("Series v01.cbz", new List<string> { "Chapter-numbered rip: 118 of 120 comic files are per-chapter, no volume files", "Unknown Series" }),
                new TrackedDownloadStatusMessage("Series v02.cbz", "Download client said no")
            }.ToJson());
        }

        [Test]
        public void a_blocklist_message_is_matched_whole()
        {
            var formats = new Mock<ICustomFormatCalculationService>();
            formats.Setup(f => f.ParseCustomFormat(It.IsAny<NzbDrone.Core.Blocklisting.Blocklist>(), It.IsAny<Author>())).Returns(new List<CustomFormat>());
            var french = Localizer(Language.French);

            new NzbDrone.Core.Blocklisting.Blocklist { Message = "Manually marked as failed" }.MapToResource(formats.Object, french).Message
                .Should().Be("Marqué manuellement comme échoué");
            new NzbDrone.Core.Blocklisting.Blocklist { Message = "Download client said no" }.MapToResource(formats.Object, french).Message
                .Should().Be("Download client said no");
        }

        // Final review (2026-09-27): the common token-less messages, with the shipped keys and the real call sites'
        // English. FailedDownloadService's "Manually marked as failed" becomes a History row that stores no
        // key (a plain message) and still reads back in French; Blocklist and an old "Manually ignored" row
        // match whole; a failed command's "Failed" (CommandExecutor) shows in the sidebar in French.
        [Test]
        public void common_failure_messages_show_in_french_with_the_shipped_keys()
        {
            var core = Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Localization/Core");
            var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "en.json")));
            var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "fr.json")));
            var keys = new[]
                {
                    "ServerQueueManuallyMarkedAsFailed", "ServerQueueFailedDownloadDetected", "ServerQueueEncryptedDownloadDetected",
                    "ServerQueueManuallyIgnored", "ServerProgressFailed"
                }
                .Select(k => (k, en[k], fr[k]))
                .ToArray();
            var french = ServerMessageTestLocalization.Create(TempFolder, Language.French, keys).Localizer;
            var english = ServerMessageTestLocalization.Create(Path.Combine(TempFolder, "en"), Language.English, keys).Localizer;

            var grabbed = new EntityHistory { Id = 1, BookId = 1, Quality = new QualityModel(), SourceTitle = "Series v01", Data = new Dictionary<string, string>() };
            var historyLookup = new Mock<IHistoryService>();
            historyLookup.Setup(h => h.Get(1)).Returns(grabbed);
            var events = new Mock<NzbDrone.Core.Messaging.Events.IEventAggregator>();
            DownloadFailedEvent failed = null;
            events.Setup(e => e.PublishEvent(It.IsAny<DownloadFailedEvent>())).Callback<DownloadFailedEvent>(e => failed = e);

            new FailedDownloadService(historyLookup.Object, Mock.Of<ITrackedDownloadService>(), events.Object).MarkAsFailed(1);

            var repository = new Mock<IHistoryRepository>();
            EntityHistory inserted = null;
            repository.Setup(r => r.Insert(It.IsAny<EntityHistory>())).Callback<EntityHistory>(h => inserted = h);
            new HistoryService(repository.Object, french, LogManager.GetLogger("test")).Handle(failed);

            inserted.Data["Message"].Should().Be("Manually marked as failed");
            inserted.Data.Should().NotContainKey("MessageKey");

            var formats = new Mock<ICustomFormatCalculationService>();
            formats.Setup(f => f.ParseCustomFormat(It.IsAny<EntityHistory>(), It.IsAny<Author>())).Returns(new List<CustomFormat>());
            formats.Setup(f => f.ParseCustomFormat(It.IsAny<NzbDrone.Core.Blocklisting.Blocklist>(), It.IsAny<Author>())).Returns(new List<CustomFormat>());
            var stored = new EntityHistory { Data = inserted.Data.ToDictionary(e => char.ToLowerInvariant(e.Key[0]) + e.Key.Substring(1), e => e.Value) };
            var ignored = new EntityHistory { Data = new Dictionary<string, string> { { "message", "Manually ignored" } } };

            stored.ToResource(formats.Object, french).Data["message"].Should().Be(fr["ServerQueueManuallyMarkedAsFailed"]);
            stored.ToResource(formats.Object, english).Data["message"].Should().Be("Manually marked as failed");
            ignored.ToResource(formats.Object, french).Data["message"].Should().Be(fr["ServerQueueManuallyIgnored"]);
            new NzbDrone.Core.Blocklisting.Blocklist { Message = "Failed download detected" }.MapToResource(formats.Object, french).Message
                .Should().Be(fr["ServerQueueFailedDownloadDetected"]);
            new NzbDrone.Core.Blocklisting.Blocklist { Message = "Encrypted download detected" }.MapToResource(formats.Object, french).Message
                .Should().Be(fr["ServerQueueEncryptedDownloadDetected"]);

            var command = new CommandModel { Id = 1, Name = "RefreshAuthor", Body = new RefreshAuthorCommand(), Status = CommandStatus.Failed, Message = "Failed" };
            var queue = new Mock<IManageCommandQueue>();
            queue.Setup(q => q.All()).Returns(new List<CommandModel> { command });

            new CommandController(queue.Object, Mock.Of<IBroadcastSignalRMessage>(), new KnownTypes(), EnglishLocalization.Create(), french)
                .GetStartedCommands().Single().Message.Should().Be(fr["ServerProgressFailed"]);
            new CommandController(queue.Object, Mock.Of<IBroadcastSignalRMessage>(), new KnownTypes(), EnglishLocalization.Create(), english)
                .GetStartedCommands().Single().Message.Should().Be("Failed");

            fr["ServerQueueManuallyMarkedAsFailed"].Should().NotBe("Manually marked as failed");
            fr["ServerProgressFailed"].Should().NotBe("Failed");
        }

        private static async Task<string> Handle(IServerMessageLocalizer messages, Exception exception)
        {
            return (await Respond(messages, exception)).Body;
        }

        private static async Task<(int Status, string Body)> Respond(IServerMessageLocalizer messages, Exception exception)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/api/v1/downloadclient/test";
            context.Response.Body = new MemoryStream();
            context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature { Error = exception, Path = "/api/v1/downloadclient/test" });

            var services = new Mock<IServiceFactory>();
            services.Setup(f => f.Build<IServerMessageLocalizer>()).Returns(messages);

            await new ReadarrErrorPipeline(LogManager.GetLogger("test"), services.Object).HandleException(context);

            context.Response.Body.Position = 0;
            return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
        }

        private static string Message(string body)
        {
            return JsonDocument.Parse(body).RootElement.GetProperty("message").GetString();
        }

        [Test]
        public async Task validation_and_test_failures_show_in_the_ui_language()
        {
            ExceptionVerification.IgnoreWarns();

            static ValidationException Failures() => new ValidationException(new List<ValidationFailure>
            {
                new NzbDroneValidationFailure("Host", new ServerText("Unknown exception: {0}", "connection refused"))
            });

            (await Handle(Localizer(Language.French), Failures())).Should().Contain("Exception inconnue : connection refused");
            (await Handle(Localizer(Language.English), Failures()))
                .Should().Be(STJson.ToJson(new List<ValidationFailure> { new NzbDroneValidationFailure("Host", "Unknown exception: connection refused") }));
        }

        // Final review (2026-09-27): a localizer that throws never turns an error response into a bodiless 500.
        // The English goes out with the exception's own status, and validation failures the localizer already
        // changed before it threw get their English back.
        [Test]
        public async Task a_throwing_localizer_falls_back_to_the_english()
        {
            ExceptionVerification.IgnoreWarns();

            var throwing = new Mock<IServerMessageLocalizer>();
            throwing.Setup(m => m.Localize(It.IsAny<string>(), It.IsAny<ServerText>())).Throws(new InvalidOperationException("boom"));
            throwing.Setup(m => m.Localize(It.IsAny<IEnumerable<ValidationFailure>>()))
                .Callback<IEnumerable<ValidationFailure>>(f => f.First().ErrorMessage = "Exception inconnue : connection refused")
                .Throws(new InvalidOperationException("boom"));

            var validation = await Respond(throwing.Object, new ValidationException(new List<ValidationFailure>
            {
                new NzbDroneValidationFailure("Host", new ServerText("Unknown exception: {0}", "connection refused")),
                new NzbDroneValidationFailure("Port", "Port is invalid")
            }));

            validation.Status.Should().Be(400);
            validation.Body.Should().Be(STJson.ToJson(new List<ValidationFailure>
            {
                new NzbDroneValidationFailure("Host", "Unknown exception: connection refused"),
                new NzbDroneValidationFailure("Port", "Port is invalid")
            }));

            var api = await Respond(throwing.Object, new Readarr.Http.REST.BadRequestException("Unable to parse volumes in the release"));

            api.Status.Should().Be(400);
            Message(api.Body).Should().Be("BadRequest: Unable to parse volumes in the release");

            var client = await Respond(throwing.Object, new NzbDroneClientException(HttpStatusCode.Conflict, "Series' root folder ({0}) doesn't exist.", "/data/manga"));

            client.Status.Should().Be(409);
            Message(client.Body).Should().Be("Series' root folder (/data/manga) doesn't exist.");
        }

        [Test]
        public async Task an_api_error_shows_in_the_ui_language_and_keeps_its_status_prefix()
        {
            ExceptionVerification.IgnoreWarns();

            Message(await Handle(Localizer(Language.French), new Readarr.Http.REST.BadRequestException("Unable to parse volumes in the release")))
                .Should().Be("BadRequest: Impossible d'analyser les tomes de la version");
            Message(await Handle(Localizer(Language.English), new Readarr.Http.REST.BadRequestException("Unable to parse volumes in the release")))
                .Should().Be("BadRequest: Unable to parse volumes in the release");
        }

        // Task 8 (2026-09-27): API errors in a French UI through ReadarrErrorPipeline, with the shipped keys: a
        // Readarr.Http exception built from a ServerText (RestController, BackupController) or a plain literal
        // (DelayProfileController), an exception whose constructor passes its own template to base
        // (QualityProfileInUseException), NzbDroneClientException carriers (MediaFileDeletionService, plan E31;
        // BackupService's RestoreBackupFailedException), a converted BookInfoException, and a kept ex.Message.
        [Test]
        public async Task an_api_error_with_a_shipped_key_shows_in_french()
        {
            ExceptionVerification.IgnoreWarns();

            var core = Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Localization/Core");
            var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "en.json")));
            var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "fr.json")));
            var keys = new[]
                {
                    "ServerApiInvalidId", "ServerApiInvalidBackupExtension", "ServerApiGlobalDelayProfile", "ServerApiProfileInUse",
                    "ServerApiSeriesRootFolderMissing", "ServerApiUnableToRestoreDatabaseFromBackup", "ServerApiFailedToGetSeriesVolumes",
                    "ServerApiSeriesMetadataMissingFromVolume"
                }
                .Select(k => (k, en[k], fr[k]))
                .ToArray();
            var french = ServerMessageTestLocalization.Create(TempFolder, Language.French, keys).Localizer;

            Message(await Handle(french, new Readarr.Http.REST.BadRequestException(new ServerText("{0} is not a valid ID", 0))))
                .Should().Be("BadRequest: 0 n'est pas un identifiant valide");
            Message(await Handle(french, new Readarr.Http.REST.UnsupportedMediaTypeException(new ServerText("Invalid extension, must be one of: {0}", ".zip, .db, .xml"))))
                .Should().Be("UnsupportedMediaType: Extension non valide, elle doit être l'une des suivantes : .zip, .db, .xml");
            Message(await Handle(french, new Readarr.Http.REST.MethodNotAllowedException("Cannot delete global delay profile")))
                .Should().Be("MethodNotAllowed: Impossible de supprimer le profil de retard global");
            Message(await Handle(french, new NzbDrone.Core.Profiles.Qualities.QualityProfileInUseException("HD-1080p")))
                .Should().Be("Le profil [HD-1080p] est utilisé.");
            Message(await Handle(french, new NzbDroneClientException(HttpStatusCode.Conflict, "Series' root folder ({0}) doesn't exist.", "/data/manga")))
                .Should().Be("Le dossier racine de la série (/data/manga) n'existe pas.");
            Message(await Handle(french, new NzbDrone.Core.Backup.RestoreBackupFailedException(HttpStatusCode.NotFound, "Unable to restore database file from backup")))
                .Should().Be("Impossible de restaurer le fichier de base de données depuis la sauvegarde");
            Message(await Handle(french, new NzbDrone.Core.MetadataSource.BookInfo.BookInfoException("Failed to get works for {0}", "anilist:30013")))
                .Should().Be("Impossible de récupérer les tomes de la série anilist:30013");
            Message(await Handle(french, new NzbDrone.Core.MetadataSource.BookInfo.BookInfoException("Expected author metadata for id [{0}] in book data {1}", "anilist:30013", "[anilist:30013-v1][Vol. 1]")))
                .Should().Be("Métadonnées de la série attendues pour l'identifiant [anilist:30013] dans les données du tome [anilist:30013-v1][Vol. 1]");
            Message(await Handle(french, new NzbDroneClientException(HttpStatusCode.InternalServerError, "Indexer said no")))
                .Should().Be("Indexer said no");

            var english = ServerMessageTestLocalization.Create(Path.Combine(TempFolder, "en"), Language.English, keys).Localizer;

            Message(await Handle(english, new NzbDrone.Core.Profiles.Qualities.QualityProfileInUseException("HD-1080p")))
                .Should().Be("Profile [HD-1080p] is in use.");
            Message(await Handle(english, new Readarr.Http.REST.BadRequestException(new ServerText("{0} is not a valid ID", 0))))
                .Should().Be("BadRequest: 0 is not a valid ID");
        }

        [Test]
        public async Task a_client_error_shows_in_the_ui_language()
        {
            Message(await Handle(Localizer(Language.French), new NzbDroneClientException(HttpStatusCode.Conflict, "Series' root folder ({0}) doesn't exist.", "/data/manga")))
                .Should().Be("Le dossier racine de la série (/data/manga) n'existe pas.");
            Message(await Handle(Localizer(Language.French), new NzbDroneClientException(HttpStatusCode.NotFound, "Couldn't find requested release in cache, try searching again")))
                .Should().Be("Version introuvable dans le cache, relancez la recherche");
            Message(await Handle(Localizer(Language.English), new NzbDroneClientException(HttpStatusCode.Conflict, "Series' root folder ({0}) doesn't exist.", "/data/manga")))
                .Should().Be("Series' root folder (/data/manga) doesn't exist.");
        }

        // i18n leftovers (2026-09-28): Change Edition's BlockedReason (EditionPreviewService.cs:213-214/228,
        // ReResolveEditionService.cs:126) is a nested-language ServerText now, localized at the API boundary
        // by EditionPreviewResourceMapper.ToResource -- proven here through the real PreferredEditionController,
        // a French preview of an already-French series reading the French sentence with the French language
        // name, and an English UI staying byte-identical.
        [Test]
        public void change_edition_blocked_reason_shows_in_the_ui_language()
        {
            var core = Path.Combine(ServerMessageSource.RepoRoot(), "src/NzbDrone.Core/Localization/Core");
            var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "en.json")));
            var fr = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(core, "fr.json")));
            var keys = new[] { "ServerValidationAlreadyLanguageEdition", "ServerRejectionLanguageFrench" }
                .Select(k => (k, en[k], fr[k]))
                .ToArray();

            var author = new Author { Id = 7, Metadata = new AuthorMetadata { EditionLanguage = "fr" } };
            var authorService = new Mock<IAuthorService>();
            authorService.Setup(s => s.GetAuthor(7)).Returns(author);

            var text = new ServerText("Already the {0} edition", new ServerText("French"));
            var previewService = new Mock<IEditionPreviewService>();
            previewService.Setup(s => s.Preview(author, "fr")).Returns(new EditionPreview
            {
                AuthorId = 7,
                ToLanguage = "fr",
                BlockedReason = text.English,
                BlockedReasonText = text
            });

            var request = new PreferredEditionController.EditionPreviewRequestResource { AuthorIds = new List<int> { 7 }, Language = "fr" };

            text.English.Should().Be("Already the French edition");

            var french = new PreferredEditionController(Mock.Of<IGcdMetadataService>(), authorService.Object, Mock.Of<IMangaSeriesMetadataProvider>(), previewService.Object, ServerMessageTestLocalization.Create(Path.Combine(TempFolder, "fr"), Language.French, keys).Localizer, Mock.Of<ILineSwitchService>());
            french.Preview(request).Single().BlockedReason.Should().Be("Déjà l'édition en français");

            var english = new PreferredEditionController(Mock.Of<IGcdMetadataService>(), authorService.Object, Mock.Of<IMangaSeriesMetadataProvider>(), previewService.Object, ServerMessageTestLocalization.Create(Path.Combine(TempFolder, "en"), Language.English, keys).Localizer, Mock.Of<ILineSwitchService>());
            english.Preview(request).Single().BlockedReason.Should().Be("Already the French edition");
        }
    }
}
