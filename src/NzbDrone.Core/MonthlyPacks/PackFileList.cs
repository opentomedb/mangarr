using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.TorrentInfo;

namespace NzbDrone.Core.MonthlyPacks
{
    public class PackFileList : ModelBase
    {
        public string InfoHash { get; set; }
        public string Title { get; set; }
        public string Name { get; set; }
        public List<TorrentFileEntry> Files { get; set; }
        public byte[] Torrent { get; set; }
        public DateTime Fetched { get; set; }
    }
}
