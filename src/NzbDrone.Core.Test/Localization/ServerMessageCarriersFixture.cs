using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Localization;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ProgressMessaging;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.Localization
{
    // Server messages (2026-09-26): each carrier keeps the English it always built, adds its template, and
    // serializes exactly as before (Global Constraints: nothing new is serialized).
    [TestFixture]
    public class ServerMessageCarriersFixture
    {
        [Test]
        public void decision_reject_keeps_its_english_and_its_template()
        {
            var decision = Decision.Reject("Only {0} minutes old, minimum age is {1} minutes", 5, 10);

            decision.Reason.Should().Be("Only 5 minutes old, minimum age is 10 minutes");
            decision.Text.Template.Should().Be("Only {0} minutes old, minimum age is {1} minutes");
            Decision.Reject("Sample").Text.Should().BeNull();
            Decision.Reject(new ServerText("Size {0}", 5)).Reason.Should().Be("Size 5");
        }

        [Test]
        public void a_wrapped_decision_keeps_its_template()
        {
            var decision = Decision.Reject("Only {0} minutes old", 5);
            var rejection = new Rejection(decision, RejectionType.Temporary);

            rejection.Reason.Should().Be("Only 5 minutes old");
            rejection.Text.Should().BeSameAs(decision.Text);
            rejection.Type.Should().Be(RejectionType.Temporary);
        }

        [Test]
        public void a_tracked_download_warning_keeps_its_template()
        {
            var trackedDownload = new TrackedDownload { DownloadItem = new DownloadClientItem { Title = "Series v01" } };

            trackedDownload.Warn("No files found are eligible for import in {0}", "/downloads/x");

            var message = trackedDownload.StatusMessages.Single();
            message.Title.Should().Be("Series v01");
            message.Messages.Should().Equal("No files found are eligible for import in /downloads/x");
            message.MessageTexts.Single().Template.Should().Be("No files found are eligible for import in {0}");
            trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        // Controller ruling (2026-09-26): TrackedDownload.Warn(string, params object[]) has always run
        // string.Format even with zero arguments (unlike Decision.Reject(string)/Rejection(string), which
        // never format). WithEnglish must reproduce that -- a zero-arg warning still unescapes "{{"/"}}".
        [Test]
        public void a_zero_arg_warning_still_formats()
        {
            var trackedDownload = new TrackedDownload { DownloadItem = new DownloadClientItem { Title = "Series v01" } };

            trackedDownload.Warn("a {{b}}");

            trackedDownload.StatusMessages.Single().Messages.Should().Equal("a {b}");
        }

        [Test]
        public void a_rejected_import_result_keeps_each_rejections_template()
        {
            var decision = new ImportDecision<LocalBook>(new LocalBook { Path = "/downloads/x.cbz" },
                new Rejection(new ServerText("Couldn't parse volume from: {0}", "x")),
                new Rejection("Sample"));

            var result = ImportResult.Rejected(decision);

            result.Errors.Should().Equal("Couldn't parse volume from: x", "Sample");
            result.ErrorTexts.Select(t => t?.Template).Should().Equal("Couldn't parse volume from: {0}", null);
            new ImportResult(decision, "Book has already been imported").ErrorTexts.Should().Equal(new ServerText[] { null });
        }

        [Test]
        public void a_templated_nzbdrone_exception_keeps_its_template()
        {
            var exception = new NzbDroneClientException(HttpStatusCode.Conflict, "Series' root folder ({0}) doesn't exist.", "/data");

            exception.Message.Should().Be("Series' root folder (/data) doesn't exist.");
            exception.Text.Template.Should().Be("Series' root folder ({0}) doesn't exist.");
            new NzbDroneClientException(HttpStatusCode.NotFound, "plain").Text.Should().BeNull();
        }

        [Test]
        public void a_progress_message_keeps_the_template_nlog_formatted()
        {
            var command = new CommandModel { Body = new TestCommand() };
            var queue = new Mock<IManageCommandQueue>();
            queue.Setup(q => q.SetMessage(It.IsAny<CommandModel>(), It.IsAny<string>()))
                 .Callback<CommandModel, string>((c, m) =>
                 {
                     c.Message = m;
                     c.MessageText = null;
                 });

            var target = new ProgressMessageTarget(Mock.Of<IEventAggregator>(), queue.Object);
            var logEvent = new LogEventInfo(LogLevel.Info, "test", null, "Processing {0} releases", new object[] { 3 });
            logEvent.Properties.Add("Status", "");

            ProgressMessageContext.CommandModel = command;

            try
            {
                typeof(ProgressMessageTarget)
                    .GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(LogEventInfo) }, null)
                    .Invoke(target, new object[] { logEvent });
            }
            finally
            {
                ProgressMessageContext.CommandModel = null;
            }

            command.Message.Should().Be("Processing 3 releases");
            command.MessageText.Template.Should().Be("Processing {0} releases");
            command.MessageText.English.Should().Be("Processing 3 releases");
        }

        [Test]
        public void a_command_message_template_is_never_a_column()
        {
            RuntimeHelpers.RunClassConstructor(typeof(DbFactory).TypeHandle);

            TableMapping.Mapper.ExcludeProperties(typeof(CommandModel)).Select(p => p.Name).Should().Contain("MessageText");
        }

        [Test]
        public void a_rejection_serializes_as_it_did()
        {
            var carried = new Rejection(new ServerText("Size {0}", 5), RejectionType.Temporary);
            var plain = new Rejection("Size 5", RejectionType.Temporary);

            STJson.ToJson(carried).Should().Be(STJson.ToJson(plain));
            carried.ToJson().Should().Be(plain.ToJson());
        }

        // ManualImportUpdateResource posts rejections back: with three constructors Rejection needs its
        // [JsonConstructor] or System.Text.Json refuses to build it.
        [Test]
        public void rejections_still_deserialize_from_the_manual_import_api()
        {
            var rejection = STJson.Deserialize<List<Rejection>>("[{\"reason\":\"Unknown Series\",\"type\":\"temporary\"}]").Single();

            rejection.Reason.Should().Be("Unknown Series");
            rejection.Type.Should().Be(RejectionType.Temporary);
            Json.Deserialize<List<Rejection>>("[{\"Reason\":\"Unknown Series\",\"Type\":1}]").Single().Reason.Should().Be("Unknown Series");
        }

        [Test]
        public void a_status_message_serializes_as_it_did_and_reads_back()
        {
            var carried = new TrackedDownloadStatusMessage("x.cbz", new ServerText("Couldn't parse volume from: {0}", "x"));
            var plain = new TrackedDownloadStatusMessage("x.cbz", "Couldn't parse volume from: x");

            STJson.ToJson(carried).Should().Be(STJson.ToJson(plain));
            new[] { carried }.ToJson().Should().Be(new[] { plain }.ToJson());
            Json.Deserialize<List<TrackedDownloadStatusMessage>>(new[] { carried }.ToJson()).Single().Messages.Should().Equal("Couldn't parse volume from: x");
        }

        // i18n leftovers (2026-09-28): EditionPreview.BlockedReasonText, the Change Edition BlockedReason carrier.
        [Test]
        public void an_edition_preview_serializes_as_it_did()
        {
            var carried = new EditionPreview
            {
                AuthorId = 7,
                BlockedReason = "Already the French edition",
                BlockedReasonText = new ServerText("Already the {0} edition", new ServerText("French"))
            };
            var plain = new EditionPreview
            {
                AuthorId = 7,
                BlockedReason = "Already the French edition"
            };

            STJson.ToJson(carried).Should().Be(STJson.ToJson(plain));
            STJson.ToJson(carried).Should().NotContain("blockedReasonText").And.NotContain("BlockedReasonText");
        }

        [Test]
        public void a_validation_failure_serializes_as_it_did()
        {
            var carried = new List<NzbDroneValidationFailure>
            {
                new NzbDroneValidationFailure("Host", new ServerText("Unknown exception: {0}", "boom")) { DetailedDescriptionText = new ServerText("Details {0}", 1) }
            };
            var plain = new List<NzbDroneValidationFailure>
            {
                new NzbDroneValidationFailure("Host", "Unknown exception: boom") { DetailedDescription = "Details 1" }
            };

            STJson.ToJson(carried).Should().Be(STJson.ToJson(plain));

            var source = new ValidationFailure("Host", "'Host' must not be empty.")
            {
                ErrorCode = "NotEmptyValidator",
                FormattedMessagePlaceholderValues = new Dictionary<string, object> { { "PropertyName", "Host" } }
            };

            STJson.ToJson(new List<NzbDroneValidationFailure> { new NzbDroneValidationFailure(source) })
                .Should().NotContain("sourceErrorCode").And.NotContain("sourcePlaceholderValues").And.NotContain("NotEmptyValidator");
        }

        [Test]
        public void a_command_result_template_is_never_serialized()
        {
            var command = new RefreshAuthorCommand();

            command.SetResultMessage(new ServerText("{0} changed, {1} blocked, {2} failed", 3, 1, 0));

            command.ResultMessage.Should().Be("3 changed, 1 blocked, 0 failed");
            STJson.ToJson(command).Should().NotContain("resultText").And.Contain("3 changed, 1 blocked, 0 failed");
        }
    }
}
