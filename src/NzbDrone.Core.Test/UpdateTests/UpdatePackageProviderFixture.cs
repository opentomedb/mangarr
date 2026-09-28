using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.UpdateTests
{
    // Mangarr ships as a Docker image; the in-app updater must never offer upstream Readarr
    // packages (the fork's Updates page shows none, regardless of branch or version).
    public class UpdatePackageProviderFixture : CoreTest<UpdatePackageProvider>
    {
        [Test]
        public void never_offers_an_upstream_update()
        {
            Subject.GetLatestUpdate("develop", new Version(0, 1)).Should().BeNull();
            Subject.GetLatestUpdate("master", new Version(10, 0)).Should().BeNull();
        }

        [Test]
        public void recent_updates_are_always_empty()
        {
            Subject.GetRecentUpdates("develop", new Version(10, 0)).Should().BeEmpty();
            Subject.GetRecentUpdates("develop", new Version(10, 0), new Version(9, 0)).Should().BeEmpty();
        }
    }
}
