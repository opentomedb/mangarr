using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Core.Test.Localization
{
    // Server messages (2026-09-26): the guard's reading of C# source, on small synthetic files.
    [TestFixture]
    public class ServerMessageSourceFixture
    {
        private const string Spec = "src/NzbDrone.Core/DecisionEngine/Specifications/X.cs";

        private static ServerMessageSite Single(string path, string source)
        {
            return ServerMessageSource.ScanText(path, source).Single();
        }

        [Test]
        public void a_regular_literal_with_arguments_is_a_template()
        {
            var site = Single(Spec, "return Decision.Reject(\"Only {0} minutes old, minimum age is {1} minutes\", age, minimum);");

            site.Kind.Should().Be("reject");
            site.Category.Should().Be("decision");
            site.ArgKind.Should().Be(ServerMessageArgKind.Literal);
            site.Template.Should().Be("Only {0} minutes old, minimum age is {1} minutes");
        }

        [Test]
        public void escapes_in_a_regular_literal_are_decoded()
        {
            Single(Spec, "return Decision.Reject(\"Contains \\\"{0}\\\" and \\\\ path\", x);").Template
                .Should().Be("Contains \"{0}\" and \\ path");
        }

        [Test]
        public void a_verbatim_literal_is_decoded()
        {
            var site = Single("src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentSettings.cs",
                @"RuleFor(c => c.MusicCategory).WithMessage(@""Can not contain '\', '//', or start/end with '/'"");");

            site.Category.Should().Be("validation");
            site.Template.Should().Be(@"Can not contain '\', '//', or start/end with '/'");
        }

        [Test]
        public void an_interpolated_literal_is_flagged()
        {
            Single(Spec, "return Decision.Reject($\"Release is {found}, the series is the {name} edition\");").ArgKind
                .Should().Be(ServerMessageArgKind.Interpolated);
        }

        [Test]
        public void a_concatenation_is_flagged()
        {
            var site = Single("src/NzbDrone.Core/Download/Clients/Deluge/Deluge.cs",
                "return new NzbDroneValidationFailure(string.Empty, \"Unknown exception: \" + ex.Message);");

            site.Category.Should().Be("test-download");
            site.ArgKind.Should().Be(ServerMessageArgKind.Concatenation);
        }

        // Fix round 1 (2026-09-26): a field like _trackedDownload has no word boundary before its "t" (the
        // preceding "_" is a word char too), so the pattern must match on the identifier ending in
        // trackedDownload/TrackedDownload, not only that exact name.
        [Test]
        public void a_prefixed_trackeddownload_receiver_is_still_a_warn_site()
        {
            var site = Single("src/NzbDrone.Core/Download/CompletedDownloadService.cs", "_trackedDownload.Warn(\"Sample {0}\", x);");

            site.Kind.Should().Be("warn");
            site.Category.Should().Be("queue");
        }

        [Test]
        public void comments_and_literal_contents_are_not_sites()
        {
            ServerMessageSource.ScanText(Spec, "// return Decision.Reject(\"Not an upgrade\");\n/* Decision.Reject(\"x\") */\nvar s = \"Decision.Reject(\\\"x\\\")\";")
                .Should().BeEmpty();
        }

        [Test]
        public void string_format_and_server_text_are_templates()
        {
            var sites = ServerMessageSource.ScanText("src/NzbDrone.Core/Download/CompletedDownloadService.cs",
                "_logger.ProgressInfo(string.Format(\"RSS Sync Completed. Reports found: {0}\", n));\ntrackedDownload.Warn(new ServerText(\"No files found are eligible for import in {0}\", path));");

            sites.Select(s => (s.Kind, s.ArgKind, s.Template)).Should().Equal(
                ("progress", ServerMessageArgKind.Literal, "RSS Sync Completed. Reports found: {0}"),
                ("warn", ServerMessageArgKind.Literal, "No files found are eligible for import in {0}"),
                ("servertext", ServerMessageArgKind.Literal, "No files found are eligible for import in {0}"));
        }

        [Test]
        public void an_identifier_reads_its_nearest_declaration()
        {
            var sites = ServerMessageSource.ScanText("src/NzbDrone.Core/Download/CompletedDownloadService.cs",
                "var reason = new ServerText(\"First {0}\", a);\ntrackedDownload.Warn(reason);\nvar reason = $\"Second {b}\";\ntrackedDownload.Warn(reason);")
                .Where(s => s.Kind == "warn").ToList();

            sites.Select(s => s.ArgKind).Should().Equal(ServerMessageArgKind.Literal, ServerMessageArgKind.Interpolated);
            sites[0].Template.Should().Be("First {0}");
        }

        // Fix round 1 (2026-09-26): Declaration() no longer falls back to a later declaration -- that let an
        // unrelated same-named declaration elsewhere in the file stand in for a real one (see the parameter
        // case below). A constant declared further down than its use site is therefore Unresolved, same as
        // any other name the guard can't trace to a preceding declaration.
        [Test]
        public void a_constant_declared_further_down_is_not_resolved()
        {
            var site = ServerMessageSource.ScanText("src/NzbDrone.Core/MediaFiles/PdfConversion/PdfConversionService.cs",
                "message.ResultMessage = PopplerMissingMessage;\nprivate const string PopplerMissingMessage = \"PDF conversion unavailable\";").Single();

            site.Category.Should().Be("progress");
            site.ArgKind.Should().Be(ServerMessageArgKind.Unresolved);
            site.Expression.Should().Be("PopplerMissingMessage");
        }

        // The false positive this guards against: a method parameter has no declaration the regex matches at
        // all, so without the fix above, the fallback would have grabbed an unrelated "var path = ..." from a
        // completely different, later method and reported its text as if it were this call's argument.
        [Test]
        public void a_parameter_is_not_resolved_to_an_unrelated_later_declaration()
        {
            var site = Single("src/NzbDrone.Core/Download/CompletedDownloadService.cs",
                "void M(string path)\n{\n    trackedDownload.Warn(path);\n}\nvoid N()\n{\n    var path = \"Unrelated {0}\";\n}");

            site.ArgKind.Should().Be(ServerMessageArgKind.Unresolved);
            site.Expression.Should().Be("path");
        }

        // Task 8 (2026-09-27): the other direction -- an earlier same-named local in another method is shadowed by
        // the site's own parameter, which has no initializer to read.
        [Test]
        public void a_parameter_is_not_resolved_to_an_unrelated_earlier_declaration()
        {
            var site = Single("src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs",
                "void M()\n{\n    var foreignBookId = $\"{a}-v{b}\";\n}\nvoid N(string foreignBookId)\n{\n    throw new BadRequestException(foreignBookId);\n}");

            site.ArgKind.Should().Be(ServerMessageArgKind.Unresolved);
            site.Expression.Should().Be("foreignBookId");

            var passed = Single("src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs",
                "var id = \"Known {0}\";\nFoo(ref id);\nthrow new BadRequestException(id);");

            passed.ArgKind.Should().Be(ServerMessageArgKind.Literal);

            foreach (var type in new[] { "string[]", "List<int>", "int?" })
            {
                Single("src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs",
                    "var id = \"Known {0}\";\nvoid M(" + type + " id)\n{\n    throw new BadRequestException(id);\n}").ArgKind
                    .Should().Be(ServerMessageArgKind.Unresolved, type + " declares a parameter");
            }
        }

        // Task 8 review (2026-09-27): a lambda arrow or a null-coalescing operator before the name is not a type,
        // so it does not shadow the declaration.
        [TestCase("Foo(x => id);")]
        [TestCase("Foo(a ?? id);")]
        public void an_operator_before_the_name_is_not_a_parameter(string use)
        {
            var site = Single("src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs",
                "var id = \"Known {0}\";\n" + use + "\nthrow new BadRequestException(id);");

            site.ArgKind.Should().Be(ServerMessageArgKind.Literal);
            site.Template.Should().Be("Known {0}");
        }

        [Test]
        public void an_unknown_expression_is_unresolved()
        {
            var site = Single("src/NzbDrone.Core/Download/CompletedDownloadService.cs", "trackedDownload.Warn(statusMessages);");

            site.ArgKind.Should().Be(ServerMessageArgKind.Unresolved);
            site.Expression.Should().Be("statusMessages");
        }

        [Test]
        public void nzbdroneclientexception_reads_its_second_argument()
        {
            var site = Single("src/Readarr.Api.V1/Indexers/ReleaseController.cs",
                "throw new NzbDroneClientException(HttpStatusCode.NotFound, \"Couldn't find requested release in cache, try searching again\");");

            site.Category.Should().Be("api");
            site.Template.Should().Be("Couldn't find requested release in cache, try searching again");
        }

        // Task 8 (2026-09-27): RestoreBackupFailedException takes its status code first, like
        // NzbDroneClientException, so its message is the 2nd argument too.
        [Test]
        public void restorebackupfailedexception_reads_its_second_argument()
        {
            var site = Single("src/NzbDrone.Core/Backup/BackupService.cs",
                "throw new RestoreBackupFailedException(HttpStatusCode.NotFound, \"Unable to restore database file from backup\");");

            site.Kind.Should().Be("exception");
            site.Category.Should().Be("api");
            site.ArgKind.Should().Be(ServerMessageArgKind.Literal);
            site.Template.Should().Be("Unable to restore database file from backup");
        }

        // Task 8 (2026-09-27): an exception whose constructor passes its own literal to base(HttpStatusCode.X, …).
        [Test]
        public void an_exception_base_call_with_a_status_code_is_an_api_site()
        {
            var site = Single("src/NzbDrone.Core/Profiles/Qualities/QualityProfileInUseException.cs",
                "public QualityProfileInUseException(string name)\n    : base(HttpStatusCode.BadRequest, \"Profile [{0}] is in use.\", name)\n{\n}");

            site.Kind.Should().Be("exceptionbase");
            site.Category.Should().Be("api");
            site.ArgKind.Should().Be(ServerMessageArgKind.Literal);
            site.Template.Should().Be("Profile [{0}] is in use.");
        }

        [Test]
        public void an_exception_without_a_message_has_none()
        {
            Single("src/Readarr.Api.V1/Queue/QueueController.cs", "throw new NotFoundException();").ArgKind
                .Should().Be(ServerMessageArgKind.None);
        }

        [Test]
        public void a_default_template_knows_its_class()
        {
            var site = Single("src/NzbDrone.Core/Validation/Paths/PathExistsValidator.cs",
                "public class PathExistsValidator : PropertyValidator\n{\n    protected override string GetDefaultMessageTemplate() => \"Path '{path}' does not exist\";\n}");

            site.ClassName.Should().Be("PathExistsValidator");
            site.Template.Should().Be("Path '{path}' does not exist");
        }

        [Test]
        public void a_target_typed_failure_is_a_validation_site()
        {
            var site = Single("src/Readarr.Api.V1/Indexers/ReleasePushController.cs",
                "throw new ValidationException(new List<ValidationFailure> { new (\"Title\", \"Unable to parse\", release.Title) });");

            site.Kind.Should().Be("validationfailure");
            site.Template.Should().Be("Unable to parse");
        }

        // Task 5 (2026-09-26): a rule message built from a value fixed when the rule is built goes through
        // ServerRuleMessages.Format, whose first argument is the template the guard checks.
        [Test]
        public void a_rule_message_format_is_a_literal_site()
        {
            var site = Single("src/NzbDrone.Core/Indexers/SeedCriteriaSettings.cs",
                "RuleFor(c => c.SeedRatio).GreaterThanOrEqualTo(seedRatioMinimum).WithMessage(ServerRuleMessages.Format(\"Under {0} leads to H&R\", seedRatioMinimum));");

            site.Kind.Should().Be("withmessage");
            site.Category.Should().Be("validation");
            site.ArgKind.Should().Be(ServerMessageArgKind.Literal);
            site.Template.Should().Be("Under {0} leads to H&R");
        }

        [Test]
        public void completion_and_result_read_to_the_end_of_the_statement()
        {
            var sites = ServerMessageSource.ScanText("src/NzbDrone.Core/Books/Commands/RefreshBookCommand.cs",
                "public override string CompletionMessage => \"Completed\";\nmessage.ResultMessage = \"Volume not found\";\nmessage.ResultMessage = null;");

            sites.Select(s => (s.Kind, s.ArgKind, s.Template)).Should().Equal(
                ("completion", ServerMessageArgKind.Literal, "Completed"),
                ("result", ServerMessageArgKind.Literal, "Volume not found"),
                ("result", ServerMessageArgKind.None, null));
        }
    }
}
