using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.ProgressMessaging;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    // Line safety review fixes (2026-09-28, I1): the refresh a line switch queues (SkipTagSync) writes no tags,
    // even with Write Audio/Book Tags = Sync; every other refresh syncs as before.
    [TestFixture]
    public class RefreshEditionServiceTagSyncFixture : CoreTest<RefreshEditionService>
    {
        private List<Edition> _updated;
        private List<Edition> _remote;

        [SetUp]
        public void Setup()
        {
            _updated = new List<Edition> { new Edition { Id = 1, ForeignEditionId = "e1", Title = "Vol. 1" } };
            _remote = new List<Edition> { new Edition { ForeignEditionId = "e1", Title = "Vol. 1" } };
        }

        // The context is thread/async-local: it must not leak into other tests.
        [TearDown]
        public void TearDown()
        {
            ProgressMessageContext.CommandModel = null;
        }

        private void Refresh(bool forceUpdateFileTags)
        {
            Subject.RefreshEditionInfo(new List<Edition>(), _updated, new List<Tuple<Edition, Edition>>(), new List<Edition>(), new List<Edition>(), _remote, forceUpdateFileTags);
        }

        [Test]
        public void a_refresh_syncs_the_tags_of_its_updated_editions()
        {
            ProgressMessageContext.CommandModel = new CommandModel { Body = new RefreshAuthorCommand(107) };

            Refresh(true);

            Mocker.GetMock<IMetadataTagService>().Verify(s => s.SyncTags(It.IsAny<List<Edition>>()), Times.Once());
        }

        [Test]
        public void the_line_switchs_refresh_syncs_no_tags()
        {
            ProgressMessageContext.CommandModel = new CommandModel { Body = new RefreshAuthorCommand(107) { SkipTagSync = true } };

            Refresh(true);

            Mocker.GetMock<IMetadataTagService>().Verify(s => s.SyncTags(It.IsAny<List<Edition>>()), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(s => s.UpdateMany(It.IsAny<List<Edition>>()), Times.Once());
        }
    }
}
