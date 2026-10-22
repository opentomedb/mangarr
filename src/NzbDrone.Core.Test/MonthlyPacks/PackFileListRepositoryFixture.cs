using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.MonthlyPacks;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MonthlyPacks
{
    [TestFixture]
    public class PackFileListRepositoryFixture : DbTest<PackFileListRepository, PackFileList>
    {
        [Test]
        public void should_round_trip_files_and_torrent_by_hash()
        {
            Subject.Insert(new PackFileList
            {
                InfoHash = "ABCDEF",
                Title = "Monthly Pack",
                Name = "Pack",
                Files = new List<TorrentFileEntry>
                {
                    new TorrentFileEntry(0, "a v01.cbz", 1000),
                    new TorrentFileEntry(1, "sub/b v02.cbz", 2500)
                },
                Torrent = new byte[] { 1, 2, 3, 250 },
                Fetched = DateTime.UtcNow
            });

            var found = Subject.FindByHash("ABCDEF");

            found.Should().NotBeNull();
            found.Name.Should().Be("Pack");
            found.Files.Should().Equal(new TorrentFileEntry(0, "a v01.cbz", 1000), new TorrentFileEntry(1, "sub/b v02.cbz", 2500));
            found.Torrent.Should().Equal(1, 2, 3, 250);
        }

        [Test]
        public void should_return_null_for_unknown_hash()
        {
            Subject.FindByHash("nope").Should().BeNull();
        }
    }
}
