using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class ReResolveEditionServiceFixture : CoreTest<ReResolveEditionService>
    {
        private Author _author;

        private void GivenAuthor(string name, string edition, string anchorName, string path = "/manga/Attack on Titan", string foreignId = "local-attack-on-titan")
        {
            _author = new Author
            {
                Id = 7,
                Path = path,
                Metadata = new AuthorMetadata { ForeignAuthorId = foreignId, Name = name, EditionLanguage = edition, AnchorName = anchorName, TomeLineId = "rl_en" }
            };

            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(7)).Returns(_author);
        }

        private void GivenPreview(string to, string newName, string blocked = null, bool renameOnly = false)
        {
            Mocker.GetMock<IEditionPreviewService>().Setup(s => s.Preview(It.IsAny<Author>(), to)).Returns(new EditionPreview
            {
                AuthorId = 7, FromLanguage = "en", ToLanguage = to, ToTomeLineId = "rl_" + to, NewName = newName, BlockedReason = blocked, Compatible = blocked == null, RenameOnly = renameOnly
            });
        }

        private void VerifyNothingWritten()
        {
            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.IsAny<AuthorMetadata>()), Times.Never());
            Mocker.GetMock<IMetadataOverridesService>().Verify(s => s.Set(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<VolumeOverride>()), Times.Never());
            Mocker.GetMock<IMetadataOverridesService>().Verify(s => s.Remove(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IAuthorService>().Verify(s => s.UpdateAuthor(It.IsAny<Author>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<RefreshAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<MoveAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        private void GivenStarted(Command body)
        {
            Mocker.GetMock<IManageCommandQueue>().Setup(q => q.GetStarted())
                  .Returns(new List<CommandModel> { new CommandModel { Body = body, Status = CommandStatus.Started } });
        }

        // A rename of the default series to "L'Attaque des Titans", destination checks all clear.
        private void GivenAFrenchRename()
        {
            GivenAuthor("Attack on Titan", null, null);
            GivenPreview("fr", "L'Attaque des Titans");
            Mocker.GetMock<IMetadataOverridesService>().Setup(s => s.Get("Attack on Titan")).Returns(SeriesAndVolumePins());
            Mocker.GetMock<IBuildFileNames>().Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null)).Returns("L'Attaque des Titans");
        }

        private void Execute(string language, bool rename = false)
        {
            Subject.Execute(new ReResolveEditionCommand { AuthorIds = new List<int> { 7 }, Language = language, Rename = rename });
        }

        [Test]
        public void a_compatible_change_writes_the_binding_and_refreshes()
        {
            GivenAuthor("Attack on Titan", null, null);
            GivenPreview("fr", "L'Attaque des Titans");

            Execute("fr");

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m =>
                m.EditionLanguage == "fr" && m.TomeLineId == "rl_fr" && m.AnchorName == null && m.Name == "Attack on Titan")), Times.Once());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<RefreshAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<MoveAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        // 2026-09-26 (migration 058): the old line's collected flag is cleared, not carried to the new line.
        [Test]
        public void a_change_clears_the_old_lines_collected_flag()
        {
            GivenAuthor("Attack on Titan", null, null);
            _author.Metadata.Value.EditionCollected = true;
            GivenPreview("fr", "L'Attaque des Titans");

            Execute("fr");

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m => m.TomeLineId == "rl_fr" && !m.EditionCollected)), Times.Once());
            _author.Metadata.Value.EditionCollected.Should().BeFalse();
        }

        [Test]
        public void a_blocked_change_writes_nothing()
        {
            GivenAuthor("Attack on Titan", null, null);
            GivenPreview("fr", null, "Volume numbering differs between the two editions and 3 file(s) are attached");

            Execute("fr");

            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void renaming_a_manga_moves_its_pins_and_its_folder_and_keeps_the_anchor()
        {
            GivenAuthor("Attack on Titan", null, null);
            GivenPreview("fr", "L'Attaque des Titans");
            Mocker.GetMock<IMetadataOverridesService>().Setup(s => s.Get("Attack on Titan"))
                  .Returns(new Dictionary<string, VolumeOverride> { { "3", new VolumeOverride { ReleaseDate = "2013-08-01" } } });
            Mocker.GetMock<IBuildFileNames>().Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null)).Returns("L'Attaque des Titans");

            Execute("fr", rename: true);

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m =>
                m.Name == "L'Attaque des Titans" && m.AnchorName == "Attack on Titan" && m.EditionLanguage == "fr")), Times.Once());
            Mocker.GetMock<IMetadataOverridesService>().Verify(s => s.Set("L'Attaque des Titans", "3", It.IsAny<VolumeOverride>()), Times.Once());
            Mocker.GetMock<IMetadataOverridesService>().Verify(s => s.Remove("Attack on Titan", "3"), Times.Once());
            Mocker.GetMock<IAuthorService>().Verify(s => s.UpdateAuthor(It.Is<Author>(a => a.Path == "/manga/L'Attaque des Titans")), Times.Once());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.Is<MoveAuthorCommand>(c => c.SourcePath == "/manga/Attack on Titan" && c.DestinationPath == "/manga/L'Attaque des Titans"), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void a_light_novel_is_never_renamed()
        {
            GivenAuthor("Sword Art Online", null, null, "/lightnovels/Sword Art Online", "local-sword-art-online~ln");
            GivenPreview("fr", "Sword Art Online (FR)");

            Execute("fr", rename: true);

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m => m.Name == "Sword Art Online")), Times.Once());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<MoveAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void back_to_english_without_a_rename_clears_the_edition_and_keeps_the_anchor()
        {
            GivenAuthor("L'Attaque des Titans", "fr", "Attack on Titan");
            GivenPreview("en", "Attack on Titan");

            Execute("en");

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m =>
                m.EditionLanguage == null && m.AnchorName == "Attack on Titan" && m.Name == "L'Attaque des Titans")), Times.Once());
        }

        // Preferred Edition (2026-09-24, controller pin ruling): every row under the old pin key moves --
        // the series row ("*": writer, cover, ...) as well as each volume's -- in both directions.
        private static Dictionary<string, VolumeOverride> SeriesAndVolumePins()
        {
            return new Dictionary<string, VolumeOverride>
            {
                { MetadataOverridesService.SeriesKey, new VolumeOverride { Author = "Hajime Isayama" } },
                { "3", new VolumeOverride { ReleaseDate = "2013-08-01" } }
            };
        }

        private void VerifyPinsMoved(string from, string to)
        {
            foreach (var key in new[] { MetadataOverridesService.SeriesKey, "3" })
            {
                Mocker.GetMock<IMetadataOverridesService>().Verify(s => s.Set(to, key, It.IsAny<VolumeOverride>()), Times.Once());
                Mocker.GetMock<IMetadataOverridesService>().Verify(s => s.Remove(from, key), Times.Once());
            }
        }

        [Test]
        public void renaming_english_to_local_moves_the_series_row_and_the_volume_rows()
        {
            GivenAuthor("Attack on Titan", null, null);
            GivenPreview("fr", "L'Attaque des Titans");
            Mocker.GetMock<IMetadataOverridesService>().Setup(s => s.Get("Attack on Titan")).Returns(SeriesAndVolumePins());
            Mocker.GetMock<IBuildFileNames>().Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null)).Returns("L'Attaque des Titans");

            Execute("fr", rename: true);

            VerifyPinsMoved("Attack on Titan", "L'Attaque des Titans");
        }

        [Test]
        public void renaming_local_back_to_english_moves_the_pins_the_folder_and_drops_the_anchor()
        {
            GivenAuthor("L'Attaque des Titans", "fr", "Attack on Titan", "/manga/L'Attaque des Titans");
            GivenPreview("en", "Attack on Titan");
            Mocker.GetMock<IMetadataOverridesService>().Setup(s => s.Get("L'Attaque des Titans")).Returns(SeriesAndVolumePins());
            Mocker.GetMock<IBuildFileNames>().Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null)).Returns("Attack on Titan");

            Execute("en", rename: true);

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m =>
                m.Name == "Attack on Titan" && m.AnchorName == null && m.EditionLanguage == null && m.TomeLineId == "rl_en")), Times.Once());
            VerifyPinsMoved("L'Attaque des Titans", "Attack on Titan");
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.Is<MoveAuthorCommand>(c => c.SourcePath == "/manga/L'Attaque des Titans" && c.DestinationPath == "/manga/Attack on Titan"), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<RefreshAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        // The store is case-insensitive: a rename that only changes case must not Set-then-Remove the same
        // row (which would delete the pins).
        [Test]
        public void a_rename_that_only_changes_case_leaves_the_pins_alone()
        {
            GivenAuthor("Attack On Titan", null, null);
            GivenPreview("fr", "Attack on Titan");
            Mocker.GetMock<IMetadataOverridesService>().Setup(s => s.Get(It.IsAny<string>())).Returns(SeriesAndVolumePins());
            Mocker.GetMock<IBuildFileNames>().Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null)).Returns("Attack on Titan");

            Execute("fr", rename: true);

            Mocker.GetMock<IMetadataOverridesService>().Verify(s => s.Remove(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m => m.Name == "Attack on Titan")), Times.Once());
        }

        [Test]
        public void a_rename_without_a_path_writes_no_move()
        {
            GivenAuthor("Attack on Titan", null, null, path: null);
            GivenPreview("fr", "L'Attaque des Titans");

            Execute("fr", rename: true);

            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<MoveAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
            _author.Name.Should().Be("L'Attaque des Titans");
        }

        // Fix round 1 (I1): a refresh already running would save its stale name and path over the change.
        [TestCase(7)]
        [TestCase(null)]
        public void a_series_a_running_refresh_covers_is_skipped_with_nothing_written(int? refreshing)
        {
            GivenAFrenchRename();
            GivenStarted(new RefreshAuthorCommand(refreshing));

            Execute("fr", rename: true);

            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_series_in_a_running_bulk_refresh_is_skipped()
        {
            GivenAFrenchRename();
            GivenStarted(new BulkRefreshAuthorCommand(new List<int> { 3, 7 }));

            Execute("fr", rename: true);

            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_refresh_of_another_series_does_not_block()
        {
            GivenAuthor("Attack on Titan", null, null);
            GivenPreview("fr", "L'Attaque des Titans");
            GivenStarted(new RefreshAuthorCommand(8));

            Execute("fr");

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.IsAny<AuthorMetadata>()), Times.Once());
        }

        // Fix round 1 (I2): a taken destination blocks a rename at run time, whatever the preview said.
        [Test]
        public void a_rename_onto_an_existing_folder_is_blocked()
        {
            GivenAFrenchRename();
            Mocker.GetMock<NzbDrone.Common.Disk.IDiskProvider>().Setup(d => d.FolderExists("/manga/L'Attaque des Titans")).Returns(true);

            Execute("fr", rename: true);

            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_rename_onto_another_series_path_is_blocked()
        {
            GivenAFrenchRename();
            Mocker.GetMock<IAuthorService>().Setup(s => s.AuthorPathExists("/manga/L'Attaque des Titans")).Returns(true);

            Execute("fr", rename: true);

            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_rename_onto_another_series_name_is_blocked()
        {
            GivenAFrenchRename();
            Mocker.GetMock<IAuthorService>().Setup(s => s.FindByName("L'Attaque des Titans", LibraryType.Manga))
                  .Returns(new Author { Id = 9, Metadata = new AuthorMetadata { Name = "L'Attaque des Titans" } });

            Execute("fr", rename: true);

            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_rename_onto_a_pin_key_that_holds_rows_is_blocked()
        {
            GivenAFrenchRename();
            Mocker.GetMock<IMetadataOverridesService>().Setup(s => s.Get("L'Attaque des Titans"))
                  .Returns(new Dictionary<string, VolumeOverride> { { "1", new VolumeOverride { ReleaseDate = "2013-06-05" } } });

            Execute("fr", rename: true);

            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        // The same checks never stand in the way of a change that keeps the name.
        [Test]
        public void a_taken_rename_destination_does_not_block_a_change_that_keeps_the_name()
        {
            GivenAFrenchRename();
            Mocker.GetMock<NzbDrone.Common.Disk.IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns(true);

            Execute("fr");

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m => m.Name == "Attack on Titan" && m.EditionLanguage == "fr")), Times.Once());
        }

        // Fix round 1 (I3): the pins are copied before the name is saved and removed only after -- a failed
        // save leaves them readable under the old key (plus a duplicate), and moves no folder.
        [Test]
        public void a_failed_binding_write_leaves_the_pins_under_the_old_key_and_moves_nothing()
        {
            GivenAFrenchRename();
            Mocker.GetMock<IAuthorMetadataService>().Setup(s => s.Upsert(It.IsAny<AuthorMetadata>())).Throws(new InvalidOperationException("database is locked"));

            Execute("fr", rename: true);

            Mocker.GetMock<IMetadataOverridesService>().Verify(s => s.Set("L'Attaque des Titans", It.IsAny<string>(), It.IsAny<VolumeOverride>()), Times.Exactly(2));
            Mocker.GetMock<IMetadataOverridesService>().Verify(s => s.Remove("Attack on Titan", It.IsAny<string>()), Times.Never());

            // Polish: the copies are undone, so the new key is clean and a retry passes the pin-key check.
            Mocker.GetMock<IMetadataOverridesService>().Verify(s => s.Remove("L'Attaque des Titans", MetadataOverridesService.SeriesKey), Times.Once());
            Mocker.GetMock<IMetadataOverridesService>().Verify(s => s.Remove("L'Attaque des Titans", "3"), Times.Once());
            Mocker.GetMock<IAuthorService>().Verify(s => s.UpdateAuthor(It.IsAny<Author>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<MoveAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<RefreshAuthorCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void the_pins_are_set_before_the_binding_and_removed_after()
        {
            GivenAFrenchRename();
            var order = new List<string>();
            Mocker.GetMock<IMetadataOverridesService>().Setup(s => s.Set(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<VolumeOverride>())).Callback(() => order.Add("set"));
            Mocker.GetMock<IMetadataOverridesService>().Setup(s => s.Remove(It.IsAny<string>(), It.IsAny<string>())).Callback(() => order.Add("remove"));
            Mocker.GetMock<IAuthorMetadataService>().Setup(s => s.Upsert(It.IsAny<AuthorMetadata>())).Callback(() => order.Add("upsert")).Returns(true);
            Mocker.GetMock<IAuthorService>().Setup(s => s.UpdateAuthor(It.IsAny<Author>())).Callback(() => order.Add("path")).Returns((Author a) => a);

            Execute("fr", rename: true);

            order.Should().Equal("set", "set", "upsert", "remove", "remove", "path");
        }

        // Fix round 1 (minor): after "back to English without Rename" the series can be renamed back to
        // its English name -- a rename-only change, which needs Rename ticked.
        [Test]
        public void a_rename_only_change_renames_back_to_the_anchor()
        {
            GivenAuthor("L'Attaque des Titans", null, "Attack on Titan", "/manga/L'Attaque des Titans");
            GivenPreview("en", "Attack on Titan", renameOnly: true);
            Mocker.GetMock<IBuildFileNames>().Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null)).Returns("Attack on Titan");

            Execute("en", rename: true);

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.Is<AuthorMetadata>(m =>
                m.Name == "Attack on Titan" && m.AnchorName == null && m.EditionLanguage == null)), Times.Once());
        }

        [Test]
        public void a_rename_only_change_without_rename_is_blocked()
        {
            GivenAuthor("L'Attaque des Titans", null, "Attack on Titan", "/manga/L'Attaque des Titans");
            GivenPreview("en", "Attack on Titan", renameOnly: true);

            Execute("en");

            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        // Fix round 1 (minor): one failing series never stops the rest of a bulk run; the toast counts both.
        [Test]
        public void a_bulk_run_continues_past_a_series_that_throws_and_reports_the_counts()
        {
            GivenAuthor("Attack on Titan", null, null);
            GivenPreview("fr", "L'Attaque des Titans");
            Mocker.GetMock<IAuthorService>().Setup(s => s.GetAuthor(8)).Throws(new InvalidOperationException("gone"));
            var command = new ReResolveEditionCommand { AuthorIds = new List<int> { 8, 7 }, Language = "fr" };

            Subject.Execute(command);

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.IsAny<AuthorMetadata>()), Times.Once());
            command.ResultMessage.Should().Be("1 changed, 0 blocked, 1 failed");
            ExceptionVerification.ExpectedWarns(1);
        }

        // Polish: the refresh check runs again right before the first write -- a refresh that started after
        // the checks at the top still stops the change, with nothing written.
        [Test]
        public void a_refresh_that_starts_after_the_checks_stops_the_change_before_any_write()
        {
            GivenAFrenchRename();
            Mocker.GetMock<IManageCommandQueue>().SetupSequence(q => q.GetStarted())
                  .Returns(new List<CommandModel>())
                  .Returns(new List<CommandModel> { new CommandModel { Body = new RefreshAuthorCommand(7), Status = CommandStatus.Started } });

            Execute("fr", rename: true);

            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        // Polish: the rebind pass loads its authors at start and upserts copies -- it counts as a refresh.
        [TestCase(7)]
        [TestCase(null)]
        public void a_running_rebind_pass_covering_the_series_stops_the_change(int? rebinding)
        {
            GivenAFrenchRename();
            GivenStarted(new ReResolveMetadataCommand { AuthorId = rebinding, Rebind = true });

            Execute("fr", rename: true);

            VerifyNothingWritten();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void a_date_re_resolve_or_another_series_rebind_does_not_stop_the_change()
        {
            GivenAuthor("Attack on Titan", null, null);
            GivenPreview("fr", "L'Attaque des Titans");
            Mocker.GetMock<IManageCommandQueue>().Setup(q => q.GetStarted()).Returns(new List<CommandModel>
            {
                new CommandModel { Body = new ReResolveMetadataCommand(7), Status = CommandStatus.Started },
                new CommandModel { Body = new ReResolveMetadataCommand { AuthorId = 8, Rebind = true }, Status = CommandStatus.Started }
            });

            Execute("fr");

            Mocker.GetMock<IAuthorMetadataService>().Verify(s => s.Upsert(It.IsAny<AuthorMetadata>()), Times.Once());
        }
    }
}
